using System;
using System.Collections.Generic;
using System.Linq;
using Dawnsbury.Campaign.Path;
using Dawnsbury.Campaign.Path.CampaignStops;
using Dawnsbury.Campaign.Encounters;
using System.Data;
using Dawnsbury.Core.Mechanics.Treasure;
using Dawnsbury.Core.Mechanics.Enumerations;

namespace DawnsburyArchipelago;

/*
 * Class which will setup an randomized, Archipelago linked adventure path variant of an existing dawnsbury days campaign
 */
public class ArchipelagoPathRandomizer(AdventurePath path, ArchipelagoClient archipelago) : AdventurePathRandomizer(path)
{
    // Save a known reference to the archipelago client so we dont have to keep using the instance
    private readonly ArchipelagoClient archipelago = archipelago;

    /// Overwritable the base class metadata properties ///
    protected override string Id => "Archipelago_" + inputId;
    protected override string Name => " Archipelago " + inputName;
    protected override string Description => $"{inputName}, but the encounter order, enemies, and loot are all randomly determined.";
    protected override int StartLevel => endLevel; // start at the final level (for builds), and then level down the pcs in encounters
    protected override int StartingShopLevel => 1; // Dont let player buy high level items (TODO: placeholder for filtering shop inventory)


    /**
     * Create a new adventure path that shuffles the order of the input adventure path encounters. 
     */
    public static new AdventurePath ShufflePath(AdventurePath input)
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
    protected override IEnumerable<EncounterCampaignStop> GetEncounterReplacementPool(IEnumerable<CampaignStop> inputStops)
    {
        var campaign_encoutners = GetEncountersToBeRandomized(inputStops);
        
        if (archipelago.IncludeFreeEncounters)
        {
            // TODO: Figure out how to find these
            IEnumerable<EncounterCampaignStop> freeEncounters = [];
            return campaign_encoutners.Concat(freeEncounters);
        }
        else
            return campaign_encoutners;
    }

    /**
     * Override the parent method to allow us to select the max level.
     */
    protected override int GetMaxLevelForReplacementStop(int originalLevel)
    {
        return archipelago.EncounterDifficulty switch
        {
            ArchipelagoClient.ApEncounterDifficulty.Simple => originalLevel,
            ArchipelagoClient.ApEncounterDifficulty.Balanced => originalLevel + 1,
            ArchipelagoClient.ApEncounterDifficulty.Difficult => 99,// no level restriction
            _ => originalLevel + 1,
        };
    }

    /**
     * Remove any weapons from the loot pool, in order to not compete with progression bonuses.
     */
    protected override IEnumerable<Item> FilterLoot(IEnumerable<Item> loot)
    {
        // Eventually, will look for cold iron/anarchic weapons here, but there arent yet any of those
        return loot.Where(item => !item.Traits.Contains(Trait.Weapon));
    }

    /**
     * Archipelago Encounter provider wrapper function.
     * Everything is done via dynamic triggers instead of static drops to enable more responsive updates.
     */
    protected override Func<Encounter> RandomEncounterProviderWrapper(Encounter encounter, int currentLevel, int index)
    {
        return () =>
        {
            // Override the default level (always at max level, and then selectivley leveled down later)
            encounter.CharacterLevel = endLevel;

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
                // Even if we dont shuffle, we dont award banned items.
                var loot = FilterLoot(encounter.Rewards).ToList();
                encounter.Rewards.Clear();
                encounter.Rewards.AddRange(loot);
            }

            return encounter;
        };
    }

    /**
     * Get the explainer text for the initial narrator stop that describes the randomizer.
     */
    protected override string GetExplainerText() =>
@"This is the archipelago version of the randomizer. If you can see this message, it means you are successfully connected to Archipelago!
In this modded adventure path, you will play through a version of this campaign with the help of your archipelago.
Your character level ups and item bonuses will come from them, and every encounter you clear will send someone an item.
Good Luck!";

}