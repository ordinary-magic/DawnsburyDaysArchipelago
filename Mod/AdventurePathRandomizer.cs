using System;
using System.Collections.Generic;
using System.Linq;
using Dawnsbury.Campaign.Path;
using Dawnsbury.Campaign.Path.CampaignStops;
using Dawnsbury.Campaign.Encounters;
using System.Data;
using Dawnsbury.Core.Mechanics.Treasure;
using System.Security.Cryptography;
using System.Text;
using System.IO;

namespace DawnsburyArchipelago;

/*
 * Class which will setup an randomized adventure path variant of an existing dawnsbury days campaign
 */
public class AdventurePathRandomizer(AdventurePath input)
{
    /// Metadata fields ///
    protected readonly string inputId = input.Id;
    protected readonly string inputName = input.Name;
    protected readonly int startLevel = input.StartingLevel;
    protected readonly int endLevel = input.StartingLevel + input.CampaignStops.Where(stop => stop is LevelUpStop).Count();
    protected readonly int startingShopLevel = input.StartingShopLevel;
    protected readonly string? credits = input.CreditsVictoryString;

    /// Overwritable metadata properties ///
    protected virtual string Id => "Random_" + inputId;
    protected virtual string Name => " Randomized " + inputName;
    protected virtual string Description => $"{input.Name}, but the encounter order, enemies, and loot are all randomly determined.";
    protected virtual int StartLevel => ArchipelagoClient.MockArchipelago? endLevel : startLevel;
    protected virtual int StartingShopLevel => startingShopLevel;

    /// State Tracking Fields ///
    private int encounterCounter = 0;
    protected List<Item> lootPile = [];
    protected List<int> goldPile = [];

    /**
     * Create a new adventure path that shuffles the order of the input adventure path encounters. 
     */
    public static AdventurePath ShufflePath(AdventurePath input)
    {
        if (ArchipelagoClient.MockArchipelago)
            CharacterStatus.InitializeCampaignHeroesAsMock();

        return new AdventurePathRandomizer(input).ShufflePath();
    }

    /**
     * Create a new shuffled adventure path from the provided path info 
     */
    public AdventurePath ShufflePath(string seed = "")
    {
        // Make it into an adventure path
        var path = new AdventurePath(Id, Name, Description, StartLevel, StartingShopLevel, ShuffleCampaignStops(seed))
        {
            BackgroundMusic = input.BackgroundMusic,
            CreditsVictoryString = (credits ?? "") + "\nRandomization by Ordinary Magician ✨",
        };
        return path;
    }

    /**
     * Shuffle only the combat encounters
     */
    protected virtual List<CampaignStop> ShuffleCampaignStops(string seed = "")
    {
        var rng = new Random();
        if (seed != "")
        {
            // If a seed was specified, get its hash to use for the RNG
            var hash = BitConverter.ToInt32(MD5.HashData(Encoding.UTF8.GetBytes(seed)));
            rng = new Random(hash);
        }

        List<CampaignStop> newPath = [];
        var currentLevel = startLevel;

        // Add explainer to opening narration stop: (note, i want this to explode if i am ever wrong about the order)
        newPath.Add(AddTextToStartOfNarrationStop((NarratorStop) input.CampaignStops[0], "Randomizer!", GetExplainerText()));

        // Add the initial dawnsbury shop (many things break without this)
        int stopCount = 1;
        newPath.Add(CopyStop(input.CampaignStops[1], stopCount++, currentLevel, true));

        // Get a list of campaign stops to shuffle and then shuffle them
        var remainingStops = input.CampaignStops.Skip(stopCount);
        var stopsToShuffle = GetEncountersToBeRandomized(remainingStops).ToList();
        var shuffledStops = GetEncounterReplacementPool(remainingStops).OrderBy(_ => rng.Next()).ToList();

        // Iterate through the filtered list of campaign stops
        foreach (var encounter in input.CampaignStops.Skip(stopCount))
        {
            var stopToAdd = encounter;

            // Check for shuffled encounters and get their random replacement
            if (stopsToShuffle.Contains(encounter))
                stopToAdd = PopRandomValidStop(shuffledStops, currentLevel, rng);

            // Check if we need to apply a level up
            if (stopToAdd is LevelUpStop)
                currentLevel++;
            if (KeepCampaignStop(stopToAdd))
                newPath.Add(CopyStop(stopToAdd, stopCount++, currentLevel));
        }

        // Data Mine the loot (DEBUG)
        //DataMineLoot(lootPile);

        // Randomize the loot
        goldPile = [.. goldPile.OrderBy(_ => rng.Next())];
        lootPile = [.. lootPile.OrderBy(_ => rng.Next())];

        return newPath;
    }

    /**
     * Virutal method to determine if we should keep a campaign stop or filter it from the final campaign
     */
    protected virtual bool KeepCampaignStop(CampaignStop inputStop) => true;

    /**
     * Virutal method to filter a list of campaign stops to only the ones which we wish to replace.
     */
    protected virtual IEnumerable<EncounterCampaignStop> GetEncountersToBeRandomized(IEnumerable<CampaignStop> inputStops)
    {
        return inputStops.Where(stop => stop is EncounterCampaignStop).Cast<EncounterCampaignStop>();
    }

    /**
     * Virutal method to return a list of campaign stops which are allowed to be randomly shuffled to replace other ones.
     */
    protected virtual IEnumerable<EncounterCampaignStop> GetEncounterReplacementPool(IEnumerable<CampaignStop> inputStops)
    {
        return GetEncountersToBeRandomized(inputStops);
    }

    /**
     * Method to pick a random valid stop in the list of unused stops, remove it from thh list, and return it.
     */
    private EncounterCampaignStop PopRandomValidStop(List<EncounterCampaignStop> stops, int level, Random rng)
    {
        // TODO: throwing array exception because everything is level 4 for some reason?
        // Works fine for non-archipelago mode tho
        var valid = stops.Where(stop => stop.EncounterSample.Map.Level <= GetMaxLevelForReplacementStop(level)).ToArray();
        var chosen = valid[rng.Next(valid.Length)];
        stops.Remove(chosen); // "pop" the stop off the list so we know its been used
        return chosen;
    }

    /**
     * Virutal method to define what the maximum level of a campaign stop which replaces an existing one can be.
     */
    protected virtual int GetMaxLevelForReplacementStop(int originalLevel) => originalLevel + 1;

    /**
     * Overwritable loot filtering method
     */
    protected virtual IEnumerable<Item> FilterLoot(IEnumerable<Item> loot) => loot; 

    /**
     * Create a copy of a "CampaignStop" object, with new index and spoiler values
     */
    protected CampaignStop CopyStop(CampaignStop input, int index, int level, bool initialDawsnbury = false)
    {
        CampaignStop newStop = input;

        // Have to use the correct constructor for the encounter type
        if (input is EncounterCampaignStop eStop)
        {
            var encounter = eStop.EncounterProvider();
            lootPile.AddRange(FilterLoot(encounter.Rewards));
            goldPile.Add(encounter.RewardGold);
            var encounterProvider = RandomEncounterProviderWrapper(encounter, level, encounterCounter++);
            newStop = new EncounterCampaignStop(encounterProvider);
        }
        else if (input is LevelUpStop)
            newStop = new LevelUpStop("Chapter Complete!"); // Not accessible, so make it up (happens a few times)
        else if (input is MediumRestCampaignStop)
            newStop = new MediumRestCampaignStop(null);
        else if (input is LongRestCampaignStop lrStop)
            newStop = new LongRestCampaignStop("You take a long rest and recover.", lrStop.WaveOfGood);
        else if (input is NarratorStop nStop)
            newStop = new NarratorStop(nStop.Name, nStop.Description, nStop.VoiceLine);
        else if (input is DawnsburyStop dStop)
            newStop = new DawnsburyStop(null, initialDawsnbury, level, dStop.DawnsburyUnderTheSea, dStop.Name);

        // Values we are keeping
        newStop.OpensChapter = input.OpensChapter;

        // Values we are changing
        newStop.Index = index;
        newStop.Spoiler = true;

        return newStop;
    }

    /**
     * Wrapper for the encounter providor function to allow us to change and pre-bake loot and level values during path construction
     */
    protected virtual Func<Encounter> RandomEncounterProviderWrapper(Encounter encounter, int level, int index)
    {
        return () =>
        {
            // Override the default level
            encounter.CharacterLevel = level;

            // Overwrite the default rewards
            var (gold, loot) = GetLoot(index);
            encounter.RewardGold = gold;
            encounter.Rewards.Clear();
            encounter.Rewards.AddRange(loot);

            return encounter;
        };
    }

    /**
     * Helper to get the shuffled reward gold and item loot for a specific encounter index 
     */
    protected (int, IEnumerable<Item>) GetLoot(int index)
    {
        // Break the randomized loot pile into equal groups and award a range that aligns with this index.
        int startIndex = lootPile.Count * index / encounterCounter;
        int nextIndex = lootPile.Count * (index + 1) / encounterCounter;
        var loot = lootPile.GetRange(startIndex, nextIndex - startIndex);
        return (goldPile[index], loot);
    }

    /**
     * Debug method to data mine the encounter loot for an adventure 
     */
    private static void DataMineLoot(IEnumerable<Item> loot)
    {
        using var lootfile = new StreamWriter("lootdump.txt");
        foreach (var item in loot)
            lootfile.WriteLine(item.ToString());

    }

    /**
     * Narration wrapper to copy a narration stop and add randomizer explainer text
     */
    protected static NarratorStop AddTextToStartOfNarrationStop(NarratorStop original, string? newName, string explainerText) =>
        new(newName ?? original.Name, explainerText + "\n\n\n\n" + original.Description, original.VoiceLine) { Index = 0 };

    /**
     * Get the explainer text for the initial narrator stop that describes the randomizer.
     */
    protected virtual string GetExplainerText() =>
@"This is the non-archipelago version of the randomizer. If you are trying to play archipelago, you should return to the menu and perform the setup.
In this modded adventure path, we will randomize the order of the game's combat encounters, and shuffle the loot drops and gold rewards around.
As a result, the game will usually be significantly harder than normal early on and a bit easier later.
Good Luck!";
}