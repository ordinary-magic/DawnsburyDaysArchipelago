using System;
using System.Collections.Generic;
using System.Linq;
using Dawnsbury.Campaign.Path;
using Dawnsbury.Campaign.Path.CampaignStops;
using Dawnsbury.Campaign.Encounters;
using Dawnsbury.Core.Mechanics.Treasure;
using Dawnsbury.Display.Illustrations;
using DawnsburyArchipelago.Data;
using Dawnsbury.Core.CharacterBuilder;
using Dawnsbury.IO;
using Dawnsbury.Core.CharacterBuilder.Library;
using Dawnsbury.Core.StatBlocks.Monsters.L12;

namespace DawnsburyArchipelago;

/*
 * Class which will setup an randomized, Archipelago linked adventure path variant of an existing dawnsbury days campaign
 */
public class ArchipelagoPathRandomizer(AdventurePath[] paths, ArchipelagoClient archipelago) : AdventurePathRandomizer(paths)
{
    // Save a known reference to the archipelago client so we dont have to keep using the instance
    private readonly ArchipelagoClient archipelago = archipelago;

    /// Overwritable the base class metadata properties ///
    protected override string Id => "Archipelago";
    protected override string Name => " Archipelago";
    protected override string Description => $"{inputName}, but the encounter order is random, and you gain level-ups and upgrades as your archipelago progresses!";
    protected override int StartLevel => endLevel; // start at the final level (for builds), and then level down the pcs in encounters
    protected override int StartingShopLevel => 1; // Dont let player buy high level items (TODO: placeholder for filtering shop inventory)
    protected override Illustration Icon => new ModdedIllustration("archipelago_logo.png");
    protected override Func<Item, bool> CustomShopFilter => (item) => Loot.ShouldBeIncludedInArchipelagoShop(item, archipelago);
    protected Random LootRng = MakeSeededRng(archipelago.RngSeed);

    /**
     * Create a new adventure path that shuffles the order of the input adventure path encounters. 
     */
    public static new AdventurePath ShufflePath(AdventurePath input) => ShufflePaths([input]);

    /**
     * Create a new adventure path that shuffles the order of the input adventure path encounters. 
     */
    public static AdventurePath ShufflePaths(AdventurePath[] input)
    {
        if (ArchipelagoClient.Instance == null)
            throw new Exception("Tried to do archipelago path shuffle with a disconnected client.");

        var client = ArchipelagoClient.Instance;
        return new ArchipelagoPathRandomizer(input, client).ShufflePath(client.RngSeed);
    }

    /**
     * Override the default campaign stop generator to remove level up stops from the final campaign
     */
    protected override bool KeepCampaignStop(CampaignStop stop)
    {
        return stop is not LevelUpStop;
    }

    /**
     * Override parent method to give us the option to turn off randomization if requested.
     */
    protected override IEnumerable<EncounterCampaignStop> GetEncountersToBeRandomized(IEnumerable<CampaignStop> inputStops)
    {
        return archipelago.UseRandomEncounterOrder ? base.GetEncountersToBeRandomized(inputStops) : [];
    }

    /**
     * Override the parent method to allow us to include the free encounters if requested
     */
    protected override IEnumerable<EncounterCampaignStop> GetEncounterReplacementPool(IEnumerable<CampaignStop> inputStops, string seed)
    {
        var campaign_encoutners = GetEncountersToBeRandomized(inputStops).ToList();
        var PoolRng = MakeSeededRng(seed);
        
        if (archipelago.IncludeFreeEncounters == ApFreeEncounterOptions.None)
        {
            // No extra encounters allowed, we dont need to do anything
            return campaign_encoutners;
        }
        else if (archipelago.IncludeFreeEncounters == ApFreeEncounterOptions.EveryEncounterPossible)
        {
            // Return every encounter we have access to
            // TODO: Strongly consider making this more customizable
            return campaign_encoutners.Concat(GetFreeEncounters(1000, PoolRng));
        }
        // Make sure we are also using random encounter order, otherwise this makes no sense
        else if (archipelago.IncludeFreeEncounters == ApFreeEncounterOptions.InRandomizerPool && archipelago.UseRandomEncounterOrder)
        {
            // Return the exact amount of requested encounters, selected randomly from the entire pool
            var pool = campaign_encoutners.Concat(GetFreeEncounters(1000, PoolRng));
            var selected = pool.OrderBy(_ => PoolRng.Next()).Take(archipelago.LocationCount).ToList();

            // By removing campaign encounters, we lose access to their original rewards in the shuffle pool.
            // Therefore, we must extract encounter rewards for them now instead of later as normal.
            foreach (var replaced_encounter in campaign_encoutners.Where(e => !selected.Contains(e)))
            {
                var encounter = replaced_encounter.EncounterProvider();
                lootPile.AddRange(encounter.Rewards);
                goldPile.Add(encounter.RewardGold);
            }

            return selected;
        }
        else
        {
            // Otherwise, just match our encounter amount to the number of locations
            int encounters_needed = archipelago.LocationCount - inputStops.Count(IsNonCutsceneEncounter);
            var freeEncounters = GetFreeEncounters(encounters_needed, PoolRng);
            return campaign_encoutners.Concat(freeEncounters);
        }
    }
    
    /**
     * Get free encounters which match our archipelago requirements, up to the given max amount
     */
    protected IEnumerable<EncounterCampaignStop> GetFreeEncounters(int maxAmount, Random rng)
    {
        // Get a list of valid encounters
        var choices = GetFreeEncounterList(archipelago.ShouldIncludeMods)
            .Where(e => e.Level >= startLevel && e.Level <= endLevel) // Make sure the level works for our pool
            .Where(e => e.Name != "Training Grounds") // Skip the testing encounter
            .Where(e => e.ThreatCategory <= ThreatCategory.Extreme
                || e.ThreatCategory == ThreatCategory.DependsOnDifficultySetting
                || archipelago.IncludeExtremePlusFreeEncounters)
            .Select(ConvertFreeToCampaignEncounter)
            .ToList();

        //ApMessages.LogEvent($"Found {choices.Count} valid random encounters", Color.BurlyWood);
        //foreach (var choice in choices)
        //    ApMessages.LogEvent(choice.Name, Color.BurlyWood);
        
        // If we are less than the max amount, just return everything
        if (choices.Count <= maxAmount)
            return choices;

        // Otherwise, shuffle the list and select N random encounters to return
        return choices.OrderBy(_ => rng.Next()).Take(maxAmount);
    }
    
    /**
     * Construct a Campaign Encounter stop from a random encounter
     */
    public static EncounterCampaignStop ConvertFreeToCampaignEncounter(RandomEncounter input)
    {
        return new EncounterCampaignStop(
            () => new Encounter(input.Map.Name, input.Map.Filename, null, 0)
        );
    }

    /**
     * Override the parent method to allow us to select the max level.
     */
    protected override int GetMaxLevelForReplacementStop(int originalLevel) 
        => originalLevel + archipelago.MaxShuffleLevelDifference;

    /**
     * Remove any weapons/armor from the loot pool, in order to not compete with progression bonuses.
     */
    protected override IEnumerable<Item> FilterLoot(IEnumerable<Item> loot, string seed)
    {
        bool allowModded = archipelago.ShouldIncludeMods;
        bool allowItemBonus = archipelago.ItemBonusSetting == ApItemBonusSettings.None;

        // Determine which randomization function to use
        Func<Item, Item> randomizerFunction = archipelago.RandomizeEncounterLoot switch
        {
            ApLootRandomization.SameTypeAndLevel => 
                item => Loot.RandomizeItemSameType(item, true, endLevel, allowModded, allowItemBonus, LootRng),
            ApLootRandomization.SameType => 
                item => Loot.RandomizeItemSameType(item, false, endLevel, allowModded, allowItemBonus, LootRng),
            ApLootRandomization.SameLevel => 
                item => Loot.RandomizeItemAnyType(item, true, endLevel, allowModded, allowItemBonus, LootRng),
            ApLootRandomization.Any =>
                item => Loot.RandomizeItemAnyType(item, false, endLevel, allowModded, allowItemBonus, LootRng),
            _ => item => item,
        };

        // Apply the function to the input loot
        // Randomize the loot first so we can try to replace "banned" items before they get adjusted/removed
        var randomizedLoot = loot.Select(randomizerFunction);

        // Filter fundamental runes if needed
        if (archipelago.ItemBonusSetting != ApItemBonusSettings.None)
            return Loot.RemoveFundamentalRunes(randomizedLoot, allowModded);
        
        return randomizedLoot;

    }

    /**
     * When a bonus stop is added to our list, register it with archipelago.
     */
    protected override void NotifyBonusStop(EncounterCampaignStop stop, int encounterIndex)
    {
        // If we have too many bonuses already, then this is a "no-reward" encounter
        if (standaloneBonusesThisModule++ >= MaxBonusesThisLevel)
            archipelago.NoRewardEncounters.Add(encounterIndex);
        else
            archipelago.StandaloneBonusEncoutners.Add(encounterIndex);
    }
    
    // Track the index where the last module ended.
    protected int standaloneBonusesThisModule = 0;
    
    // The maximum number of bonuses we are allowed to award this level
    protected int MaxBonusesThisLevel => (int) Math.Ceiling(
        (archipelago.LocationCount - OriginalCampaignLength) / (double) (endLevel - startLevel + 1)
    );

    /**
     * When we see a non-combat encounter, we must tell archipelago to skip giving rewards during it.
     */
    protected override void NotifyConversationStop(EncounterCampaignStop stop, int encounterIndex)
    {
        archipelago.NoRewardEncounters.Add(encounterIndex);
    } 

    /**
     * When we reach the end of a module, check if we need to register bonus drops with archipealgo.
     */
    protected override void NotifyEndOfModule(int numTotalEncounters, int previousEncounterIndex)
    {
        // If we have too many items, put bonus drops at the last few stops
        if (numTotalEncounters < archipelago.LocationCount)
        {
            int bonusToAward;

            // If we are on the last encounter, just fill all remaining slots
            if (numTotalEncounters - 1 == previousEncounterIndex)
                bonusToAward = archipelago.LocationCount - numTotalEncounters - archipelago.ExtraBonusEncounters.Count;
                
            // Otherwise, give bonuses proprotional to the amount of encounters we have claered so far
            else 
                bonusToAward = (int) Math.Round(
                    (archipelago.LocationCount - numTotalEncounters)
                        * ((double)previousEncounterIndex / numTotalEncounters)
                    ) - archipelago.ExtraBonusEncounters.Count;

            // Count backwards from the last encounter until we run out of bonuses
            // At normal (low) numbers, this will double up on the last few encounters in a module.
            // At high numbers, this will still bias the end of module, but will stack onto the earlier modules too
            //   thus, early modules will have more encounters - which is honestly fine enough to not make this more complicated
            while (bonusToAward > 0)
            {
                bonusToAward--;
                int id = (previousEncounterIndex - bonusToAward) % (previousEncounterIndex + 1);
                archipelago.ExtraBonusEncounters.Add(id);
            }
        }

        // Reset the count now that we are done with the module
        standaloneBonusesThisModule = 0;   
    }

    /**
     * Archipelago Encounter provider wrapper function.
     * Everything is done via dynamic triggers instead of static drops to enable more responsive updates.
     */
    protected override Func<Encounter> RandomEncounterProviderWrapper(Encounter encounter, int currentLevel, int index, string seed)
    {
        return () =>
        {
            // Update the state tracker, as having loaded an encounter, we are no longer in a menu
            DawnsburyArchipelagoLoader.InApCampaignMenu = false;

            // Override the default level (always at max level, and then selectivley leveled down later)
            encounter.CharacterLevel = endLevel;

            // Check if this is the last encounter (both for credits and because we check it later)
            encounter.IsFinalCampaignEncounter = index + 1 == CampaignLengthIncludingNoncombat;

            // Overwrite the default rewards if requested
            if (archipelago.ShuffleEncounterLoot)
            {
                var (gold, loot) = GetLoot(index);
                encounter.RewardGold = gold;
                encounter.Rewards.Clear();
                encounter.Rewards.AddRange(loot);
            }
            else
            {
                // Even if we dont shuffle, we must still block banned items and potentially randomize the loot.
                var loot = FilterLoot(encounter.Rewards, seed).ToList();
                encounter.Rewards.Clear();
                encounter.Rewards.AddRange(loot);
            }

            // Return the modified encounter
            return encounter;
        };
    }

    // Override the custom campaign heros
    protected override List<CharacterSheet>? CustomCampaignHeroes()
    {
        // Check if we aren't randomizing the builds
        if (!archipelago.RandomizeBuilds)
            return null;

        // Select 4 random pregens
        var rng = MakeSeededRng(archipelago.RngSeed); // always use the same rng for a given seed
        var pregens = CharacterLibrary.Instance.PregenProfiles.OrderBy(_ => rng.Next()).Take(4);

        // Make copies of the selected sheets (by serializing and deserializing them) so we dont affect the saved versions
        var copies = pregens.Select(sheet =>
            LocalDataStore.LoadText<CharacterSheet>(LocalDataStore.SerializeSystemTextJson(sheet)));

        // Initialize their campaign inventory (throw on deserialization failure)
        var chars = copies.Select(sheet => { 
            sheet!.CampaignInventory.BecomeFrom(sheet.InventoriesByLevel[startLevel]);
            return sheet; 
        });

        // Finally, return them
        return [.. chars];
    }

    /**
     * Get the explainer text for the initial narrator stop that describes the randomizer.
     */
    protected override string GetExplainerText() =>
@"This is the archipelago version of the randomizer. If you can see this message, it means you are successfully connected to Archipelago!
In this modded adventure path, you will play through a version of this campaign with the help of your archipelago.
Your character will unlock levels, bonuses and loot as a result of your archipelago, and every encounter you clear will send someone an item.
Good Luck!";
}