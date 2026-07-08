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
using DawnsburyArchipelago.Data;

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
        // Add a rule to restrict equiping items based on Archipealgo Rules
        var originalCanUse = item.CanUse ?? ((_ , _) => true);
        item.CanUse = (c, i) => originalCanUse(c, i) && APCanUseItem(c, i);

        // Add the IgnorePropertyRuneLimit trait if relevant
        // Note: Can't use ShouldIgnorePropertyRuneLimit because the items are generated before we are in the menu
        //   hopefully, someone with a connected archipelago wont open other campaigns or notice this property.
        //   Player facing its slightly worse, but its less risk than two harmony patches and a rewrite of the statblock generator.
        if (ArchipelagoClient.Instance?.ItemBonusSetting == ApItemBonusSettings.Automatic)
            if (!item.HasTrait(Trait.IgnorePropertyRuneLimit))
                item.Traits.Add(Trait.IgnorePropertyRuneLimit);

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
        if (ShouldIgnorePropretyRuneLimit())
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

                // In either case, if the bonus is > 0, the item is magical
                if (potency > 0 && !item.Traits.Contains(Trait.Magical))
                    item.Traits.Add(Trait.Magical);

                // Ensure we have enough weapon potency to hold something with this many runes
                return potency >= numPropertyRunes;
            }
        }

        // If not in a pc in an archipelago campaign, dont restrict their equipment.
        return true;
    }

    /**
     * Quick helper method to check if we should ignore the property rune limit on items
     */
    public static bool ShouldIgnorePropretyRuneLimit()
    {
        return DawnsburyArchipelagoLoader.IsArchipelagoCampaignActive() &&
            ArchipelagoClient.Instance?.ItemBonusSetting == ApItemBonusSettings.Automatic;
    }

    /**
     * Investigate an item's properties and ruens to see if it should be allowed in the archipelago campaign shop
     */
    public static bool ShouldBeIncludedInArchipelagoShop(Item item, ArchipelagoClient apClient)
    {
        // Todo: should we exclude skill items? 
        //  It's a not-guaranteed bonus, so probably not. The ap bonus is much stronger anyway.

        // If we are in manual item bonus mode, we must specifically allow unlocked fundamental runes
        if (apClient.ItemBonusSetting == ApItemBonusSettings.Manual)
            if (item.Traits.Contains(Trait.Fundamental) && item.RuneProperties is {} rune)
            {
                // Local function to check the lowest value of a sepcific unlock type across all heroes
                bool CheckValue(Func<CharacterStatus, int> type) 
                    => rune.FundamentalLevel <= CharacterStatus.Heroes.Values.Min(type);

                // Get the correct field based on the type
                bool fullyUnlocked = rune.RuneKind switch
                {
                    RuneKind.WeaponPotency => CheckValue(h => h.WeaponPotency),
                    RuneKind.WeaponStriking => CheckValue(h => h.Striking),
                    RuneKind.ArmorPotency => CheckValue(h => h.ArmorPotency),
                    RuneKind.ArmorResilient => CheckValue(h => h.Resilient),
                    _ => true
                };

                // If its fully unlocked, allow it, otherwise ban it.
                return fullyUnlocked;
            }

        // Otherwise, simply check if the item is legal
        bool allowItemBonus = apClient.ItemBonusSetting != ApItemBonusSettings.None;
        return IsLegalItem(item, allowItemBonus, apClient.ShouldIncludeMods);
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
    
    // List of items which give item bonuses that we want to block
    public static readonly List<ItemName> ItemBonusItems = [
        ItemName.AlchemistGoggles, ItemName.AlchemistGogglesGreater, ItemName.AlchemistGogglesMajor,
        ItemName.GateAttenuator, ItemName.GateAttenuatorGreater, // no +3?
        ];

    /**
     * Remove all instances of fundamental runes from an encounter's dropped items
     */
    public static IEnumerable<Item> RemoveFundamentalRunes(IEnumerable<Item> loot, bool allowModded)
    {
        return loot

            // Pop all runestones off of dropped items
            .SelectMany(item => item.SelfAndIncludedItems)
            .Select(item => item.DuplicateWithout(ItemModificationKind.Rune))

            // Check if the item is leagal
            .Where(item => IsLegalItem(item, false, allowModded));
    }
    
    /**
     * Check if a given item is legal under this run's constraints
     */
    public static bool IsLegalItem(Item item, bool allowItemBonus, bool allowModdeditems)
    {
        // Check if the item is modded
        if(!allowModdeditems && IsModdedItem(item))
            return false;

        // Check if the item is banned as something that gives an item bonus
        if (!allowItemBonus)
        {
            // Exclude reinforced shields and fundamental runes
            if (item.Traits.Contains(Trait.Fundamental) || 
                    (item.Traits.Contains(Trait.Shield) && 
                    shieldsByPotency.Skip(1).Any(shield => shield == item.ItemName)))
                return false;


            // Exclude any item which has fundamental runes or is a specific magic item with a built in bonus
            if (item.Runes.Any(rune => rune.Traits.Contains(Trait.Fundamental)) ||
                    item.WeaponProperties?.ItemBonus > 0 ||
                    item.ArmorProperties?.ItemBonus > 0 ||
                    ItemBonusItems.Contains(item.ItemName))
                return false;
        }

        // All conditions met
        return true;
    }

    // Cachce to save a generated list of legal items used by GetLegalItems // 
    private static readonly Dictionary<(bool, bool), IEnumerable<Item>> _legalItems = [];

    /**
     * Get a list of every item which is legal in this run
     */
    public static IEnumerable<Item> GetLegalItems(bool allowItemBonus, bool allowModdeditems)
    {
        // Make a tuple from the two bools to use as a key
        var key = (allowItemBonus, allowModdeditems);
        
        // Try to get a cached value for the given settings
        if (_legalItems.TryGetValue(key, out var value))
            return value;

        // Otherwise, generate the cache and return it
        else
        {
            _legalItems[key] = Items.ShopItems.Where(item => IsLegalItem(item, allowItemBonus, allowModdeditems));
            return _legalItems[key];
        }
    }

    /**
     * Check if the item is a modded item
     */
    public static bool IsModdedItem(Item item) => HasModTrait(item.Traits);

    /**
     * Check if a list of traits contains a "mod" trait
     */
    public static bool HasModTrait(IEnumerable<Trait> traits)
    {
        // The humanized name of any mod's dynamic mod trait just turns into "Mod", so we look for that 
        return traits
            .Select(trait => trait.GetTraitProperties().HumanizedName)
            .Any(name => name == "Mod");
    }

    // List of shields by potency, useful for various functions.
    static readonly List<ItemName> shieldsByPotency = [
        ItemName.SteelShield,
        ItemName.SturdyShield8,
        ItemName.SturdyShield10,
        ItemName.SturdyShield13,
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
            item => shieldsByPotency.Any(shield => shield == item.ItemName));
        
        // Add in a new shield of the desired potency for every shield we removed
        while (itemsToReplace-- > 0)
            creature.HeldItems.Add(shieldsByPotency[shieldPotency]);

        // Remove all shields from the list of carried items
        itemsToReplace = creature.CarriedItems.RemoveAll(
            item => shieldsByPotency.Any(shield => shield == item.ItemName));
        
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
        if (ArchipelagoClient.Instance?.ItemBonusSetting == ApItemBonusSettings.Manual)
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
    public static Item RandomizeItemSameType(Item original, bool keepLevel, int campaignEndLevel, bool allowModded, bool allowItemBonus, Random rng)
    {
        // Item is a spell scroll, pick a scroll within the allowed level range which is upcast by the same amount
        if (original.ScrollProperties != null)
        {
            int spellLevel = original.ScrollProperties.Spell.MinimumSpellLevel;
            int upcastBy = original.ScrollProperties.Spell.SpellLevel - spellLevel;
            int minSpellLvl = keepLevel? spellLevel : 1;
            int maxSpellLvl = keepLevel? spellLevel : Math.Max(((campaignEndLevel + 1) / 2) - upcastBy, minSpellLvl);
            
            // Choose a random spell that matches the criteria
            var options = AllSpells.All
                .Where(spell => !spell.Traits.Contains(Trait.Focus))
                .Where(spell => allowModded || !HasModTrait(spell.Traits))
                .Where(spell => spell.MinimumSpellLevel >= minSpellLvl)
                .Where(spell => spell.MinimumSpellLevel <= maxSpellLvl)
                .ToList();

            if (options.Count == 0)
                throw new ArgumentOutOfRangeException("Could not find replacement for spell scroll: " +
                    $"{original.ScrollProperties.Spell.Name} ({spellLevel}, +{upcastBy})");
            
            var spell = options[rng.Next(options.Count)];

            // We only give a hightened level if its actually hightened, or else the scroll will break
            int? heightenedTo = (upcastBy > 0)? spell.SpellLevel + upcastBy : null;
            return Items.CreateSpellScroll(spell.SpellId, heightenedTo);
        }

        // Determine the replacement should be
        int minLevel = keepLevel? original.Level : 0;
        int maxLevel = keepLevel? original.Level : campaignEndLevel;

        // Pick a random item with a matching type
        var item = TryReplacingItemByType(original, minLevel, maxLevel, allowModded, allowItemBonus, rng);

        // If we couldn't find one, try loosening the level range (for instance, weapons which are forced to lvl 0)
        item ??= TryReplacingItemByType(original, 0, maxLevel, allowModded, allowItemBonus, rng);

        // Return the chosen item if we found one, or a longsword otherwise.
        return item ?? ItemName.Longsword;
    }

    /**
     * Iterate over a list of traits to return an item from a replcaement pool which matches the listed traits of the original item
     */
    public static Item? TryReplacingItemByType(Item original, int minLevel, int maxLevel, bool allowModded, bool allowItemBonus, Random rng)
    {
        // Get the search pool for the input item
        var pool = GetItemPool(original.Traits, minLevel, maxLevel, allowModded, allowItemBonus);

        // If the item has runes, include them in the pool too (most "weapon" drops are just rune upgrades in disguise)
        if (original.Runes.Count > 0)
            pool.AddRange(GetItemPool([Trait.Runestone], minLevel, maxLevel, allowModded, allowItemBonus));

        // Pick a random value from the list
        if (pool.Count > 0)
            return pool[rng.Next(pool.Count)];
        
        return null; // No matching items found
    }
    
    /**
     * Iterate over a list of traits to return an item from a replcaement pool which matches the listed traits of the original item
     */
    public static Item? TryReplacingItemByType(IEnumerable<Trait> traits, int minLevel, int maxLevel, bool allowModded, bool allowItemBonus, Random rng)
    {
        // Get the search pool
        var pool = GetItemPool(traits, minLevel, maxLevel, allowModded, allowItemBonus);

        // Pick a random value from the list
        if (pool.Count > 0)
            return pool[rng.Next(pool.Count)];
        
        return null; // No matching items
    }
    
    /**
     * Return a list of items which match the input criteria
     */
    public static List<Item> GetItemPool(IEnumerable<Trait> traits, int minLevel, int maxLevel, bool allowModded, bool allowItemBonus)
    {
        // Get a list of all legal replacements
        var legalItems = GetLegalItems(allowItemBonus, allowModded)
            .Where(item => item.Level >= minLevel)
            .Where(item => item.Level <= maxLevel);

        // Traits of items that represent categories of interest to check
        // Note: Using "consumable" instead of potion because 1) more variety and 2) potions seemingly dont have that trait consistently
        //   excluding "shield" makes them get randomized with weapons, which is probably better since the pool is so small
        Trait[] categories = [Trait.Weapon, Trait.Armor, Trait.Runestone, Trait.Consumable, Trait.Scroll]; //Trait.Shield];

        // Get a list of all items which fully match the given categories
        return [.. 
            legalItems.Where(item => categories.All(cat => 
                traits.Contains(cat) == item.Traits.Contains(cat)))
        ];
    }

    /**
     * Turn an item into a fully random replacement
     */
    public static Item RandomizeItemAnyType(Item original, bool keepLevel, int campaignEndLevel, bool allowModded, bool allowItemBonus, Random rng)
    {
        // Get a list of all legal items
        int minLevel = keepLevel? original.Level : 0;
        int maxLevel = keepLevel? original.Level : campaignEndLevel;
        var allItems = GetLegalItems(allowItemBonus, allowModded);

        var pool = allItems
            .Where(item => item.Level >= minLevel && item.Level <= maxLevel)
            .ToList();

        // Select a random item from the list
        if (pool.Count > 0)
            return pool[rng.Next(pool.Count)];

        // If we cant find one, remove the minimum level requirement and try again
        pool = [.. allItems.Where(item => item.Level <= maxLevel)];
        if (pool.Count > 0)
            return pool[rng.Next(pool.Count)];

        return ItemName.Longsword; // Failsafe Longsword
    }

    /**
     * Award a loot bag filler item to the player, returning true if successfully created
     */
    public static void AwardLootBag(bool allowModded, bool allowItemBonus)
    {
        // Create a bag of holding containing the loot (because its cool)
        Item bag = ItemName.BagOfHolding1; // can hold 5 items
        bag.Nickname = "Loot Bag!";

        var minLevel = CharacterStatus.PartyMinLevel;
        var maxLevel = CharacterStatus.PartyMaxLevel;
        var rng = new Random();

        // Give them some consumables, a runestone, a weapon/armor, and a misc item (none of the other categories).
        Trait[][] itemsToAdd = [
            [Trait.Consumable],
            [Trait.Consumable],
            [Trait.Runestone],
            [(rng.Next(4) == 0)? Trait.Armor : Trait.Weapon], // 3/4 chance weapon, 1/4 armor
            []
        ];
        
        foreach (var traits in itemsToAdd)
        {
            // Try to find a match for our criteria, and add it to the bag
            var item = TryReplacingItemByType(traits, minLevel, maxLevel, allowModded, allowItemBonus, rng);
            if (item is Item realItem)
                bag.WithModification(
                    new ItemModification(ItemModificationKind.StoredItem) { 
                        StoredItem = realItem 
                });
        }

        // Try to give the player a loot bag
        LootDropQueue.Enqueue(bag);
        TryToAwardPendingLoot();
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
}