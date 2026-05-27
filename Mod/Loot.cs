using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Dawnsbury.Campaign.Path;
using Dawnsbury.Core.CharacterBuilder;
using Dawnsbury.Core.CharacterBuilder.FeatsDb.Spellbook;
using Dawnsbury.Core.Creatures;
using Dawnsbury.Core.Mechanics.Enumerations;
using Dawnsbury.Core.Mechanics.Treasure;

namespace DawnsburyArchipelago;

public class Loot
{
    // Concurrent Queue of loot which we must drop at the next oppurtunity //
    public static ConcurrentQueue<Item> LootDropQueue { get; } = new();

    public delegate bool CanUseItemDelegate(CalculatedCharacterSheetValues character, Item item);

    /**
     * Apply the relevant archipelago modifications to the input item.
     */
    public static Item AddApItemModifications(Item item)
    {
        // We first ignore the property rune limit
        //item.Traits.Add(Trait.IgnorePropertyRuneLimit);

        // Add a rule to restrict equiping items based on Archipealgo Rules
        var originalCanUse = item.CanUse ?? ((_ , _) => true);
        item.CanUse = (c, i) => originalCanUse(c, i) && APCanUseItem(c, i);

        return item;
    }

    /**
     * Method to restrict equipping items which we do not have enough implicit potency runes to use
     */
    public static bool APCanUseItem(CalculatedCharacterSheetValues character, Item item)
    {
        // Respect character proficencies (this call is skipped if Item.CanUse exists)
        if ((item.HasTrait(Trait.Weapon) && !item.HasTrait(Trait.Shield)) || item.HasTrait(Trait.Armor))
            if (character.GetProficiency(item.Traits) == Proficiency.Untrained)
                return false;
        
        // Only affect items in an archipealgo campaign
        if (DawnsburyArchipelagoLoader.IsArchipelagoCampaignActive())
        {
            // Only affect items in runs where we dont drop potency runes
            if (!ArchipelagoClient.InstancePotencyRunes)
            {
                // Check if the character is a pc
                if (TryGetCharacterStatus(character.Sheet) is {} status)
                {
                    // Calculate the potency of the character's items vs the number of equipped runes.
                    int potency = 0;
                    var numPropertyRunes = item.Runes
                        .Where(rune => rune.RuneProperties != null)
                        .Count(rune => rune.RuneProperties!.RuneKind == RuneKind.ArmorProperty
                                    || rune.RuneProperties!.RuneKind == RuneKind.WeaponProperty);

                    // Item is armor
                    if (item.ArmorProperties != null)
                        potency = status.ArmorPotency;

                    // Item is a weapon
                    if (item.WeaponProperties != null)
                        potency = status.WeaponPotency;

                    // In either case, tell it to ignore the property rune limit
                    // Doesn't seem to do anything?
                    //if (item.ArmorProperties != null || item.WeaponProperties != null)
                    //    item.Traits.Add(Trait.IgnorePropertyRuneLimit);

                    // Ensure we have enough weapon potency to hold something with this many runes
                    return potency >= numPropertyRunes;
                }
            }
        }

        // If not in a pc in an archipelago campaign, dont restrict their equipment.
        return true;
    }

    /**
     * Investigate an item's properties and ruens to see if it should be allowed in the archipelago campaign shop
     */
    public static bool ShouldBeIncludedInArchipelagoShop(Item item, ArchipelagoClient apClient)
    {
        // Todo: should we exclude skill items? 
        //  Currently its only a very rare bonus, so probably not. Also, because it hits everything, its far stronger.

        // Exclude higher-potency shiels from the shop pool if they are to be automatically upgraded
        if (shieldsByPotency.Skip(1).Any(shield => shield.Name == item.Name) && !apClient.PotencyRunes)
            return false;

        // Determine if the item is or contains any fundamental runes
        bool isFundmanetal = item.Traits.Contains(Trait.Fundamental) ||
            item.Runes.Any(rune => rune.Traits.Contains(Trait.Fundamental));

        // If we are in a mode where we dont use those, exclude any items which contain them. 
        if (!apClient.PotencyRunes) return !isFundmanetal;

        // In runs where we do use them, we must allow the player to purchase any qhich they have fully unlocked
        if (isFundmanetal)
        {
            // Get all runes of concern on the item
            RuneProperties[] runes = (item.RuneProperties != null)? [item.RuneProperties] :
                [.. item.Runes.Select(i => item.RuneProperties).Where(p => p!= null).Cast<RuneProperties>()];
            foreach (var rune in runes)
            {
                // Local function to compare the level of the rune against the lowest hero unlock of a given type 
                bool CheckValue(Func<CharacterStatus, int> type) 
                    => rune.FundamentalLevel <= CharacterStatus.Heroes.Values.Min(type);

                // Make sure we meet the minimum for each type
                if (!(rune.RuneKind switch
                {
                    RuneKind.WeaponPotency => CheckValue(h => h.WeaponPotency),
                    RuneKind.WeaponStriking => CheckValue(h => h.Striking),
                    RuneKind.ArmorPotency => CheckValue(h => h.ArmorPotency),
                    RuneKind.ArmorResilient => CheckValue(h => h.Resilient),
                    _ => true
                })) return false;
            }
        }
        
        // Nothing about the item flagged, so allow it.
        return true;
    }

    /**
     * Try to resolve a character's name into their archipelago status
     */
    public static CharacterStatus? TryGetCharacterStatus(CharacterSheet sheet)
    {
        // Comapre the input character name to the campaign hero sheets to see which one we are
        int slot = CampaignState.Instance?.Heroes.FindIndex(h => h == sheet.LinkedHero) ?? -1;
        
        // Try to get a corresponding character status for that hero
        if (slot >= 0)
            return CharacterStatus.Heroes[CharacterStatus.CampaignHeroes[slot]];

        return null;
    }
    
    /**
     * Convert a typical modification rune into an archipelago rune.
     */
    public static IEnumerable<Item> FilterLoot(IEnumerable<Item> loot)
    {
        return loot
        
            // Pop all runestones off of dropped items
            .SelectMany(item => item.SelfAndIncludedItems)
            .Select(item => item.DuplicateWithout(ItemModificationKind.Rune))

            // Exclude magic shields and fundamental runes from the drop pile
            .Where(item => !(item.Traits.Contains(Trait.Shield) && item.Traits.Contains(Trait.Magical)))
            .Where(item => !item.Traits.Contains(Trait.Fundamental)); // Exclude all fundamental runes
    }

    // List of shields by potency, useful for various functions.
    static readonly List<Item> shieldsByPotency = [
        Items.GetItemTemplate(ItemName.SteelShield),
        Items.GetItemTemplate(ItemName.SturdyShield8),
        Items.GetItemTemplate(ItemName.SturdyShield10),
        Items.GetItemTemplate(ItemName.SturdyShield13),
    ];
    
    /**
     * Update a character's shields to make them match their amror potency.
     * Note: these are slightly misaligned, (5/4, 11/13, 18/19), but its kinda good enough.
     */
    public static void UpgradeShields(Creature creature, int shieldPotency)
    {
        // The list of all reinforced shields, sorted by potency in ascending order
        shieldPotency = Math.Clamp(shieldPotency, 0, 3);

        // Construct a list of all shields below our target potency level
        //List<Item> shieldsToReplace = [.. shieldsByPotency.Take(shieldPotency)];

        // Remove all shields from the list of held items
        int itemsToReplace = creature.HeldItems.RemoveAll(
            item => shieldsByPotency.Any(shield => shield.ItemName == item.ItemName));
        
        // Add in a new shield of the desired potency for every shield we removed
        while (itemsToReplace-- > 0)
            creature.HeldItems.Add(shieldsByPotency[shieldPotency]);

        // Remove all shields from the list of carried items
        itemsToReplace = creature.CarriedItems.RemoveAll(
            item => shieldsByPotency.Any(shield => shield.ItemName == item.ItemName));
        
        // Add in a new shield of the desired potency for every shield we removed
        while (itemsToReplace-- > 0)
            creature.CarriedItems.Add(shieldsByPotency[shieldPotency]);
    }

    /**
     * Prepare a fundmanetal runestone of the provided tier and type to drop in the game
     */
    public static void DropFundamentalRunestone(int tier, bool isPotency, bool isWeapon)
    {
        // Only proceed if the run settings say we should drop the rune
        if (ArchipelagoClient.InstancePotencyRunes)
        {
            // Prepare a list of options sorted by type
            ItemName[] options = [
                ItemName.WeaponPotencyRunestone, ItemName.WeaponPotencyRunestone2, ItemName.WeaponPotencyRunestone3,
                ItemName.StrikingRunestone, ItemName.GreaterStrikingRunestone, ItemName.MajorStrikingRunestone,
                ItemName.ArmorPotencyRunestone, ItemName.ArmorPotencyRunestone2, ItemName.ArmorPotencyRunestone3,
                ItemName.ResilientRunestone, ItemName.GreaterResilientRunestone, ItemName.MajorResilientRunestone,
            ];

            // Calcualte the list index of what we want
            int index = (tier - 1) + (isPotency? 0 : 3) + (isWeapon? 0 : 6);
            
            // Make sure the input is valid
            if (index >= 0 && index < options.Length)

                // Finally, enqueue it to drop in game
                LootDropQueue.Enqueue(Items.GetItemTemplate(options[index]));

            TryToAwardPendingLoot();
        }
    }
    
    /**
     * Randomize the input item within its item category
     */
    public static Item RandomizeItem(Item original, Random rng)
    {
        // Item is a spell scroll, then pick a random one at the same level
        if (original.ScrollProperties != null)
        {
            int scrollLevel = original.ScrollProperties.Spell.SpellLevel;
            int spellLevel = original.ScrollProperties.Spell.MinimumSpellLevel;
            var options = AllSpells.All.Where(spell => 
                spell.MinimumSpellLevel == spellLevel && spell.SpellLevel == scrollLevel)
                .ToList();

            var spell = options[rng.Next(options.Count)];
            var scroll = Items.GetItemTemplate(ItemName.SpellScroll);
            scroll.ScrollProperties = new ScrollProperties(spell);
            return scroll;
        }
        
        // Item is a runestone, pick a random replacement
        // TODO
        
        // Item is a potion, pick a random one at the same level
        // TODO
        
        // Item is a weapon/armor, pick a random replacement
        // TODO

        return original;
    }

    /**
     * Try to award any pending loot drops to the player
     */
    public static void TryToAwardPendingLoot()
    {
        if (DawnsburyArchipelagoLoader.IsArchipelagoCampaignActive(false))
        {
            while (LootDropQueue.TryDequeue(out var item))
                if (item != null)
                    CampaignState.Instance!.CommonLoot.Add(item);

            ArchipelagoClient.Instance?.SaveInventory();
        }
    }

    /**
     * Postfix method for Weapon/ArmorProperties.ItemBonus, 
     *  such that it always returns +3 in menus where we are faking item bonuses.
     */
    public static void ItemBonusPostfix(ref int __result)
    {
        if (DawnsburyArchipelagoLoader.InApCampaignMenu)
            if (!ArchipelagoClient.InstancePotencyRunes)
                __result = 3;
    }
}