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
using Dawnsbury.Display.Illustrations;
using Dawnsbury.Core;
using Dawnsbury.Core.CharacterBuilder;

namespace DawnsburyArchipelago;

/*
 * Class which will setup an randomized adventure path variant of an existing dawnsbury days campaign
 */
public class AdventurePathRandomizer(AdventurePath[] input)
{
    /// Metadata fields ///
    protected readonly string inputId = input[0].Id;
    protected readonly string inputName = input[0].Name;
    protected readonly string? credits = input[0].CreditsVictoryString;
    protected readonly int startLevel = input.Min(path => path.StartingLevel);
    protected readonly int startingShopLevel = input.Min(path => path.StartingShopLevel);
    protected readonly int endLevel =
        input.Min(path => path.StartingLevel) + // get the starting level
        input.SelectMany(path => path.CampaignStops).Count(stop => stop is LevelUpStop) + // plus every level up in the campaigns
        input.Length - 1; // plus one more level for each campaign beyond the first (since you level up between them)

    /// Overwritable metadata properties ///
    protected virtual string Id => "Random_" + inputId;
    protected virtual string Name => " Randomized " + inputName;
    protected virtual string Description => $"{inputName}, but the encounter order and loot are all randomly determined.";
    protected virtual int StartLevel => ArchipelagoClient.MockArchipelago? endLevel : startLevel;
    protected virtual int StartingShopLevel => startingShopLevel;
    protected virtual Illustration Icon => IllustrationName.BlueD20;
    protected virtual Func<Item, bool>? CustomShopFilter => null;
    protected int OriginalCampaignLength = 0; // the number of encounters in the original campaign
    protected int CampaignLength = 0; // the number of encounters in the campaign
    protected int CampaignLengthIncludingNoncombat = 0; // the number of encounters in the campaign

    /// State Tracking Fields ///
    private int encounterCounter = 0;
    protected List<Item> lootPile = [];
    protected List<int> goldPile = [];
    protected Random ShuffleRng {get; set;} = new Random();

    /**
     * Create a new adventure path that shuffles the order of the input adventure path encounters. 
     */
    public static AdventurePath ShufflePath(AdventurePath input)
    {
        if (ArchipelagoClient.MockArchipelago)
            CharacterStatus.InitializeCampaignHeroesAsMock();

        return new AdventurePathRandomizer([input]).ShufflePath();
    }

    /**
     * Create a new shuffled adventure path from the provided path info 
     */
    public AdventurePath ShufflePath(string seed = "")
    {
        ShuffleRng = MakeSeededRng(seed);

        // Make it into an adventure path
        var path = new CustomAdventurePath(Id, Name, Description, StartLevel, StartingShopLevel, ShuffleCampaignStops(seed))
        {
            BackgroundMusic = input[0].BackgroundMusic,
            CreditsVictoryString = (credits ?? "") + "\nRandomization by Ordinary Magician ✨",
            Icon = Icon,
            CustomHeroes = CustomCampaignHeroes,
        };
        return path;
    }

    /**
     * Shuffle only the combat encounters
     */
    protected virtual List<CampaignStop> ShuffleCampaignStops(string seed = "")
    {
        List<CampaignStop> newPath = [];
        var AllCampaignStops = MergeAdventurePathStops(input);
        var currentLevel = startLevel;
        int stopCount = 0;

        if (AllCampaignStops[0] is NarratorStop stop)
        {
            // If the first stop is a narration stop (as it is in the first campaign), modify the narration
            newPath.Add(AddTextToStartOfNarrationStop(stop, "Randomizer!", GetExplainerText()));
            stopCount++;
        }

        // Add the initial dawnsbury shop (many things break without this)
        var initalShop = AllCampaignStops[stopCount];
        newPath.Add(CopyStop(initalShop, stopCount, currentLevel, initalShop.OpensChapter, seed, initialDawsnbury: true));
        stopCount++;

        // Get a list of campaign stops to shuffle and then shuffle them
        var remainingStops = AllCampaignStops.Skip(stopCount);
        var stopsToShuffle = GetEncountersToBeRandomized(remainingStops).ToList();
        var replacementPool = GetEncounterReplacementPool(remainingStops, seed).OrderBy(_ => ShuffleRng.Next()).ToList();
        int remainingFillerStops = replacementPool.Count - stopsToShuffle.Count;
        int timesAddedFiller = 0;

        // Total campaign length: the number of unrandomized stops + the number of randomized stops
        OriginalCampaignLength = AllCampaignStops.Count(IsNonCutsceneEncounter);
        CampaignLength = remainingStops.Count(IsNonCutsceneEncounter) - stopsToShuffle.Count + replacementPool.Count;
        CampaignLengthIncludingNoncombat = remainingStops.Count(stop => stop is EncounterCampaignStop) - stopsToShuffle.Count + replacementPool.Count;

        // Iterate through the filtered list of campaign stops
        foreach (var encounter in AllCampaignStops.Skip(stopCount))
        {
            var stopToAdd = encounter;

            // Check for shuffled encounters and get their random replacement
            if (stopsToShuffle.Contains(encounter))
                stopToAdd = PopRandomValidStop(replacementPool, currentLevel, ShuffleRng);

            // Check if we are at the end of a module, at because we reached a level up stop, or we are the last stop in the campaign
            if (stopToAdd is LevelUpStop || stopToAdd == AllCampaignStops.Last())
            {
                // Before we end this module, check if we need to add any extra encounters (round up)
                var extraAtThisLevel = Math.Ceiling((double) remainingFillerStops / (endLevel - startLevel + 1 - timesAddedFiller++));
                remainingFillerStops -= (int) extraAtThisLevel;
                
                // Add extra stops for as long as we still have them
                while (extraAtThisLevel > 0)
                {
                    // Add an extra long rest (Copy stop is nessecarry to setup stop metadata)
                    var lrStop = new LongRestCampaignStop("The party rests, and gathers their strength for the challenge ahead.");
                    newPath.Add(CopyStop(lrStop, stopCount++, currentLevel, null, seed));

                    // Then add up to 3 extra stops
                    for (int i=3; extraAtThisLevel > 0 && i > 0; i--)
                    {
                        var extraStop = PopRandomValidStop(replacementPool, currentLevel, ShuffleRng);
                        NotifyBonusStop(extraStop, encounterCounter);
                        newPath.Add(CopyStop(extraStop, stopCount++, currentLevel, null, seed));
                        extraAtThisLevel--;
                        
                        // Must have a rest stops between encounters, so if this isnt the last one in the sequence, we must add one
                        if (extraAtThisLevel > 0 && i > 1)
                            newPath.Add(CopyStop(new MediumRestCampaignStop(null), stopCount++, currentLevel, null, seed));
                    }
                }
                NotifyEndOfModule(CampaignLength, encounterCounter - 1);
            }

            // Increment the current level
            if (stopToAdd is LevelUpStop)
                currentLevel++;

            if (KeepCampaignStop(stopToAdd))
                newPath.Add(CopyStop(stopToAdd, stopCount++, currentLevel, encounter.OpensChapter, seed));
        }

        // Add custom loot to the pile
        lootPile.AddRange(MakeCustomLoot());

        // Randomize the loot
        goldPile = [.. goldPile.OrderBy(_ => ShuffleRng.Next())];
        lootPile = [.. lootPile.OrderBy(_ => ShuffleRng.Next())];

        return newPath;
    }

    /**
     * Combine multiple campaigns worth of adventure stops into one.
     */
    private static List<CampaignStop> MergeAdventurePathStops(IEnumerable<AdventurePath> paths)
    {
        List<CampaignStop> result = [];
        foreach(var path in paths)
        {
            result.AddRange(path.CampaignStops);
            result.Add(new LevelUpStop("End of campaign level up"));
        }

        // Return all but the last level up stop
        return [.. result.Take(result.Count - 1)];
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
        return inputStops.Where(IsNonCutsceneEncounter).Cast<EncounterCampaignStop>();
    }

    /**
     * Virutal method to return a list of campaign stops which are allowed to be randomly shuffled to replace other ones.
     */
    protected virtual IEnumerable<EncounterCampaignStop> GetEncounterReplacementPool(IEnumerable<CampaignStop> inputStops, string seed)
    {
        return GetEncountersToBeRandomized(inputStops);
    }

    /**
     * Helper method to get all the random encounters in the game.
     */
    public static List<RandomEncounter> GetFreeEncounterList(bool allowModded)
    {
        // Must init the random encounters, as this doesnt happen until the menu is opened
        if (RandomEncounter.RandomEncounterGroups.Count == 0)
            RandomEncounter.Init();

        // A list of encounter groups which exist in the base game.
        string[] BASE_GAME_ENCOUNTERS = ["Additional scenarios", "High-level encounters", "The Fifteen Obelisks"];

        return [.. 
            RandomEncounter.RandomEncounterGroups

            // The "null" group seems to contain a duplicate of every single encounter, for some reason.
            .Where(group => group is not null)

            // If modding is not allowed, restrict to base game encounters only
            .Where(group => allowModded || BASE_GAME_ENCOUNTERS.Contains(group))

            // Remove campaign encounters (Ch1: Golden Candelabra) by searching for "Ch1: "
            .Where(group => !((group ?? "").StartsWith("Ch") && (group ?? "").Contains(": ")))

            .SelectMany(RandomEncounter.EncountersInGroup)
        ];
    }

    /**
     * Method to pick a random valid stop in the list of unused stops, remove it from the list, and return it.
     */
    private EncounterCampaignStop PopRandomValidStop(List<EncounterCampaignStop> stops, int level, Random rng)
    {
        // Boundry check to make sure we dont go infinite
        if (stops.Count == 0)
            throw new InvalidOperationException("Cant select a random encounter stop, there are no stops left!");

        // Get a list of valid stops of the specified level, increasing it by each time we dont find any.
        // Note: in theory this shouldn't happen, but it covers me making a mistake (which has happened before).
        EncounterCampaignStop[] valid = [];
        while (valid.Length == 0)
        {
            int maxLevel = GetMaxLevelForReplacementStop(level++);
            valid = [.. stops.Where(stop => stop.EncounterSample.Map.Level <= maxLevel)];
        }

        // Select a random stop from the list
        var chosen = valid[rng.Next(valid.Length)];
        stops.Remove(chosen); // "pop" the stop off the list so we dont use it again
        return chosen;
    }

    /**
     * Virutal method to define what the maximum level of a campaign stop which replaces an existing one can be.
     */
    protected virtual int GetMaxLevelForReplacementStop(int originalLevel) => originalLevel + 1;

    /**
     * Overwritable loot filtering method
     */
    protected virtual IEnumerable<Item> FilterLoot(IEnumerable<Item> loot, string seed) => loot;

    /**
     * Overwritable method to add custom loot
     */
    protected virtual IEnumerable<Item> MakeCustomLoot() => [];

    /**
     * Overwritable method to allow children to react to bonus stops being added to the list.
     *  stop - the stop being added
     *  encounterIndex - the index of the stop, relative to all encounter stops.
     */
    protected virtual void NotifyBonusStop(EncounterCampaignStop stop, int encounterIndex) {}
    
    /**
     * Overwritable method to allow children to react to non-combat stops being added to the list.
     *  stop - the stop being added
     *  encounterIndex - the index of the stop, relative to all encounter stops.
     */
    protected virtual void NotifyConversationStop(EncounterCampaignStop stop, int encounterIndex) {}

    /**
     * Overwritable method to allow children to react to the end of a level module.
     *  numTotalEncounters - the total number of all encounters we will include in this adventure
     *  lastEncounterIndex - the index of the last encounter stop, relative to all encounter stops.
     */
    protected virtual void NotifyEndOfModule(int numTotalEncounters, int lastEncounterIndex) {}

    /**
     * Helper to check if a given campaign stop is a "real" encounter - that is, not a cutscene.
     */
    protected static bool IsNonCutsceneEncounter(CampaignStop stop)
    {
        if (stop is EncounterCampaignStop encounter)
        {
            var map = encounter.EncounterProvider();
            return !map.IsConversation && !map.IsCutscene;
        }
        return false;
    }

    /**
     * Helper to check if a given campaign stop is a cutscene.
     */
    protected static bool IsCutsceneEncounter(CampaignStop stop)
    {
        if (stop is EncounterCampaignStop encounter)
        {
            var map = encounter.EncounterProvider();
            return map.IsConversation || map.IsCutscene;
        }
        return false;
    }

    /**
     * Create a copy of a "CampaignStop" object, with new index and spoiler values
     */
    protected CampaignStop CopyStop(CampaignStop input, int index, int level, string? opensChapter, string seed, bool initialDawsnbury = false)
    {
        CampaignStop newStop = input;

        // Have to use the correct constructor for the encounter type
        if (input is EncounterCampaignStop eStop)
        {
            // Get the encounter built in to the stop
            var encounter = eStop.EncounterProvider();
            lootPile.AddRange(FilterLoot(encounter.Rewards, seed));
            goldPile.Add(encounter.RewardGold);

            // If the stop is a cutscene, tell the tracker
            if (IsCutsceneEncounter(eStop))
                NotifyConversationStop(eStop, encounterCounter);

            // Make a new encounter provider with our changes and attach it to the stop
            var encounterProvider = RandomEncounterProviderWrapper(encounter, level, encounterCounter++, seed);
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
            newStop = new DawnsburyStop(dStop.ShopkeeperFlavorText, initialDawsnbury, level, dStop.DawnsburyUnderTheSea, dStop.Name)
                .WithCustomShop(dStop.CustomShop?.Name ?? dStop.Name, dStop.CustomShop?.Illustration ?? IllustrationName.Shopkeeper1, 
                    dStop.ShopkeeperFlavorText, false, null, CustomShopFilter, null);

        // Keep the "opens chapter" stop for the stop we repalce
        newStop.OpensChapter = opensChapter;

        // Values we are changing
        newStop.Index = index;
        newStop.Spoiler = true;

        return newStop;
    }

    /**
     * Wrapper for the encounter providor function to allow us to change and pre-bake loot and level values during path construction
     */
    protected virtual Func<Encounter> RandomEncounterProviderWrapper(Encounter encounter, int level, int index, string seed)
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

            // Override the "last encounter" status
            encounter.IsFinalCampaignEncounter = index + 1 == CampaignLengthIncludingNoncombat;

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
     * Method which will create a seeded pseuedorandom number generator from the given seed 
     */
    public static Random MakeSeededRng(string seed)
    {
        if (seed != "")
        {
            // If a seed was specified, get its hash to use for the RNG
            var hash = BitConverter.ToInt32(MD5.HashData(Encoding.UTF8.GetBytes(seed)));
            return new Random(hash);
        }
        return new Random();
    }

    /**
     * Narration wrapper to copy a narration stop and add randomizer explainer text
     */
    protected static NarratorStop AddTextToStartOfNarrationStop(NarratorStop original, string? newName, string explainerText) =>
        new(newName ?? original.Name, explainerText + "\n\n\n\n" + original.Description, original.VoiceLine) { Index = 0 };

    // Overloadable method to specify custom campaign heroes for "Quick Start" mode.
    protected virtual List<CharacterSheet>? CustomCampaignHeroes() => null;

    /**
     * Get the explainer text for the initial narrator stop that describes the randomizer.
     */
    protected virtual string GetExplainerText() =>
@"This is the non-archipelago version of the randomizer. If you are trying to play archipelago, you should return to the menu and perform the setup.
In this modded adventure path, we will randomize the order of the game's combat encounters, and shuffle the loot drops and gold rewards around.
As a result, the game will usually be significantly harder than normal early on and a bit easier later.
Good Luck!";
}