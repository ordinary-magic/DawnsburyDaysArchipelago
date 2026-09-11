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
using Dawnsbury.Core;
using Dawnsbury.Core.CharacterBuilder;
using System.Reflection;
using Dawnsbury.Display.Illustrations;
using DawnsburyArchipelago.Data;
using Dawnsbury.Core.CharacterBuilder.Library;
using Dawnsbury.Auxiliary;

namespace DawnsburyArchipelago;

/// <summary>
/// Custom adventure path which handles archipelago wrapping of existing adventure paths.
/// </summary>
public class ArchipelagoAdventurePath : AdventurePath
{
    /// <summary>
    /// Create a new Archipealgo adventure path for a given set of existing adventure paths.
    /// </summary>
    /// <param name="input">The existing adventure paths to wrap.</param>
    /// <param name="archipelago">The archipealgo client to build around.</param>
    public ArchipelagoAdventurePath(AdventurePath[] input, ArchipelagoClient archipelago): base(
        "Archipelago",
        "Archipelago",
        $"{input[0].Name}, but the encounter order is random, and you gain level-ups and upgrades as your archipelago progresses!",
        archipelago.ApLevelUps? GetEndLevel(input) : GetStartLevel(input), // for automatic level ups, we must start at the final level for builds, and then level down the pcs in encounters
        1, // Dont let player buy high level items (TODO: do we still need this after filtering?)
        MergeAdventurePathStops(input)
    )
    {
        // Save things which are useful later
        startLevel = GetStartLevel(input);
        endLevel = GetEndLevel(input);
        Archipelago = archipelago;
        OriginalCampaignStops = CampaignStops;

        // Set campaign path metadata
        BackgroundMusic = input[0].BackgroundMusic;
        Icon = new ModdedIllustration("archipelago_logo.png");
        CreditsVictoryString = (input[0].CreditsVictoryString ?? "") + "\n" + "Archipelago Integration by Ordinary Magician ✨";
        if (Archipelago.Campaign.IsGameCampaign())
            HeroCampaignTraits = CampaignTraits.CreateCanonicalDawnsburyFourCampaignTraits();

        // Initialize the randomizer
        ShuffleRng = MakeSeededRng(Archipelago.RngSeed);
        PoolRng = MakeSeededRng(Archipelago.RngSeed + "-EncounterPool");
        LootRng = MakeSeededRng(Archipelago.RngSeed + "-Loot");
    }

    /// Saved / Cached Metadata & Useful References ///
    private ArchipelagoClient Archipelago {get; init;}
    public List<CampaignStop> OriginalCampaignStops {get; set;}
    private readonly int startLevel;
    private readonly int endLevel;

    /// Randomization State ///
    private int OriginalCampaignLength = 0; // the number of encounters in the original campaign
    private int CampaignLength = 0; // the number of encounters in the campaign
    private int CampaignLengthIncludingNoncombat = 0; // the number of encounters in the campaign
    private int encounterCounter = 0;
    protected List<Item> lootPile = [];
    protected List<int> goldPile = [];
    public bool HasProcessedCampaignStops { get; protected set; } = false;
    
    /// Various rng objects, trakced seperatley to make features more consistent across settings. ///
    protected Random ShuffleRng {get; init;}
    protected Random PoolRng {get; init;}
    protected Random LootRng {get; init;}

    /// <summary>
    /// Process the campaign stops we copied, including: randomzing (if enabled), 
    ///     adjusting encounters, adjusting loot, and removing stops (such as level ups). 
    /// </summary>
    /// <returns>A copy of the newly processed campaign stops.</returns>
    public List<CampaignStop> ProcessCampaignStops()
    {
        List<CampaignStop> newPath = [];
        var currentLevel = startLevel;
        int stopCount = encounterCounter = 0;

        if (OriginalCampaignStops[0] is NarratorStop stop)
        {
            // If the first stop is a narration stop (as it is in the first campaign), modify the narration
            newPath.Add(AddTextToStartOfNarrationStop(stop, "Archipelago!", ExplainerText));
            stopCount++;
        }

        // Add the initial dawnsbury shop (many things break without this)
        var initalShop = OriginalCampaignStops[stopCount];
        newPath.Add(CopyStop(initalShop, stopCount, currentLevel, initalShop.OpensChapter, initialDawsnbury: true));
        stopCount++;

        // Get a list of campaign stops to shuffle and then shuffle them
        var remainingStops = OriginalCampaignStops.Skip(stopCount);
        var stopsToShuffle = GetEncountersToBeRandomized(remainingStops).ToList();
        var replacementPool = GetEncounterReplacementPool(remainingStops).OrderBy(_ => ShuffleRng.Next()).ToList();
        int remainingFillerStops = replacementPool.Count - stopsToShuffle.Count;
        int timesAddedFiller = 0;

        // Total campaign length: the number of unrandomized stops + the number of randomized stops
        OriginalCampaignLength = OriginalCampaignStops.Count(IsNonCutsceneEncounter);
        CampaignLength = remainingStops.Count(IsNonCutsceneEncounter) - stopsToShuffle.Count + replacementPool.Count;
        CampaignLengthIncludingNoncombat = remainingStops.Count(stop => stop is EncounterCampaignStop) - stopsToShuffle.Count + replacementPool.Count;

        // Iterate through the filtered list of campaign stops
        foreach (var encounter in OriginalCampaignStops.Skip(stopCount))
        {
            var stopToAdd = encounter;

            // Check for shuffled encounters and get their random replacement
            if (stopsToShuffle.Contains(encounter))
                stopToAdd = PopRandomValidStop(replacementPool, currentLevel);

            // Check if we are at the end of a module, at because we reached a level up stop, or we are the last stop in the campaign
            if (stopToAdd is LevelUpStop || stopToAdd == OriginalCampaignStops.Last())
            {
                // Before we end this module, check if we need to add any extra encounters (round up)
                var extraAtThisLevel = Math.Ceiling((double) remainingFillerStops / (endLevel - startLevel + 1 - timesAddedFiller++));
                remainingFillerStops -= (int) extraAtThisLevel;
                
                // Add extra stops for as long as we still have them
                while (extraAtThisLevel > 0)
                {
                    // Add an extra long rest (Copy stop is nessecarry to setup stop metadata)
                    var lrStop = new LongRestCampaignStop("The party rests, and gathers their strength for the challenge ahead.");
                    newPath.Add(CopyStop(lrStop, stopCount++, currentLevel, null));

                    // Then add up to 3 extra stops
                    for (int i=3; extraAtThisLevel > 0 && i > 0; i--)
                    {
                        var extraStop = PopRandomValidStop(replacementPool, currentLevel);
                        NotifyBonusStop(encounterCounter);
                        newPath.Add(CopyStop(extraStop, stopCount++, currentLevel, null));
                        extraAtThisLevel--;
                        
                        // Must have a rest stops between encounters, so if this isnt the last one in the sequence, we must add one
                        if (extraAtThisLevel > 0 && i > 1)
                            newPath.Add(CopyStop(new MediumRestCampaignStop(null), stopCount++, currentLevel, null));
                    }
                }
                NotifyEndOfModule(CampaignLength, encounterCounter - 1);
            }

            // Increment the current level
            if (stopToAdd is LevelUpStop)
                currentLevel++;

            // Check if the stop is one which should be replaced with a placeholder
            if (ShouldRepalceWithPlaceholder(stopToAdd))
                newPath.Add(NewPlaceholderStop(stopToAdd, stopCount++, encounter.OpensChapter));

            // Add the new stop
            else if (KeepCampaignStop(stopToAdd))
                newPath.Add(CopyStop(stopToAdd, stopCount++, currentLevel, encounter.OpensChapter));
        }

        // Add additional loot to the pile
        //lootPile.AddRange(MakeCustomLoot());

        // Randomize the loot
        goldPile = [.. goldPile.OrderBy(_ => ShuffleRng.Next())];
        lootPile = [.. lootPile.OrderBy(_ => ShuffleRng.Next())];

        // Finally, save & return the campaign stops
        CampaignStops = newPath;
        HasProcessedCampaignStops = true;
        return newPath;
    }

    /// <summary>
    /// Combine multiple campaigns worth of adventure stops into one, adding level ups between them.
    /// </summary>
    /// <param name="paths">The paths to merge.</param>
    /// <returns>A merged list of campaign stop</returns>
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

    /// <summary>
    /// Should we this campaign stop, or should it be removed from the new path
    /// </summary>
    /// <param name="inputStop">The stop to potentially remove.</param>
    /// <returns>True/False if it should be repalced.</returns>
    private bool KeepCampaignStop(CampaignStop stop) => stop is not LevelUpStop || !Archipelago.ApLevelUps;
    
    /// <summary>
    /// Should this campaign stop be replaced swith a placeholder.
    /// Note: this takes priority over KeepCampaignStop. 
    /// </summary>
    /// <param name="inputStop">The stop to potentially replace.</param>
    /// <returns>True/False if it should be repalced.</returns>
    private bool ShouldRepalceWithPlaceholder(CampaignStop stop) =>
        !KeepCampaignStop(stop) && Archipelago.Campaign == ApCampaignChoice.Roguelike;

    /// <summary>
    /// Filter the list of campaign stops, returning only those which should be randomized.
    /// </summary>
    private IEnumerable<EncounterCampaignStop> GetEncountersToBeRandomized(IEnumerable<CampaignStop> inputStops)
    {
        return (Archipelago.UseRandomEncounterOrder && !Archipelago.DisallowRandomization) ?
            inputStops.Where(IsNonCutsceneEncounter).Cast<EncounterCampaignStop>() : [];
    }

    /// <summary>
    /// Get a list of "alternate" encounter stops to be shuffled into the adventure
    ///     either in place of or instead of the existing ones.
    /// </summary>
    private IEnumerable<EncounterCampaignStop> GetEncounterReplacementPool(IEnumerable<CampaignStop> inputStops)
    {
        var campaign_encoutners = GetEncountersToBeRandomized(inputStops).ToList();
        
        if (Archipelago.IncludeFreeEncounters == ApFreeEncounterOptions.None || Archipelago.DisallowRandomization)
        {
            // No extra encounters allowed, we dont need to do anything
            return campaign_encoutners;
        }
        else if (Archipelago.IncludeFreeEncounters == ApFreeEncounterOptions.EveryEncounterPossible)
        {
            // Return every encounter we have access to
            // TODO: Strongly consider making this more customizable
            return campaign_encoutners.Concat(GetFreeEncounters(1000, PoolRng));
        }
        // Make sure we are also using random encounter order, otherwise this makes no sense
        else if (Archipelago.IncludeFreeEncounters == ApFreeEncounterOptions.InRandomizerPool && Archipelago.UseRandomEncounterOrder)
        {
            // Return the exact amount of requested encounters, selected randomly from the entire pool
            var pool = campaign_encoutners.Concat(GetFreeEncounters(1000, PoolRng));
            var selected = pool.OrderBy(_ => PoolRng.Next()).Take(Archipelago.LocationCount).ToList();

            // By removing campaign encounters, we lose access to their original rewards in the shuffle pool.
            // Therefore, we must extract encounter rewards for them now instead of later as normal.
            if (!HasProcessedCampaignStops) // Only do this once, since it doesnt change.
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
            int encounters_needed = Archipelago.LocationCount - inputStops.Count(IsNonCutsceneEncounter);
            var freeEncounters = GetFreeEncounters(encounters_needed, PoolRng);
            return campaign_encoutners.Concat(freeEncounters);
        }
    }
    
    /// <summary>
    /// Get a set of free encounters which match the archipelago's requirements, up to a given maximum amount.
    /// </summary>
    protected IEnumerable<EncounterCampaignStop> GetFreeEncounters(int maxAmount, Random rng)
    {
        // Get a list of valid encounters
        var choices = GetFreeEncounterList(Archipelago.ShouldIncludeMods)
            .Where(e => e.Level >= startLevel && e.Level <= endLevel) // Make sure the level works for our pool
            .Where(e => e.Name != "Training Grounds") // Skip the testing encounter
            .Where(e => e.ThreatCategory <= ThreatCategory.Extreme
                || e.ThreatCategory == ThreatCategory.DependsOnDifficultySetting
                || Archipelago.IncludeExtremePlusFreeEncounters)
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

    /// <summary>
    /// Copy and Construct a campaign style encounter stop for a random encounter.
    /// </summary>
    public static EncounterCampaignStop ConvertFreeToCampaignEncounter(RandomEncounter input)
    {
        return new EncounterCampaignStop(
            () => new Encounter(input.Map.Name, input.Map.Filename, null, 0)
        );
    }

    /// <summary>
    /// Helper method to get all of the random encounters in the game
    /// </summary>
    /// <param name="allowModded">Should the list include modded encounters?</param>
    /// <returns>A list of RandomEncounter's</returns>
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

    /// <summary>
    /// Method to pick a random valid stop in the list of unused stops, remove it from the list, and return it.
    /// </summary>
    /// <param name="stops">List of stops to pop from</param>
    /// <param name="level">The level of the stop which we are replacing</param>
    /// <returns>A randomly selected stop.</returns>
    /// <exception cref="InvalidOperationException">If there are stops left in the pool. (Designed to not happen, so we want to see it).</exception>
    private EncounterCampaignStop PopRandomValidStop(List<EncounterCampaignStop> stops, int level)
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
        var chosen = valid[ShuffleRng.Next(valid.Length)];
        stops.Remove(chosen); // "pop" the stop off the list so we dont use it again
        return chosen;
    }

    /// <summary>
    /// Determine the maximum level of a campaign stop to replace one of a given level.
    /// </summary>
    /// <param name="originalLevel">The level of the stop to replace</param>
    /// <returns>The maximum allowed level of the replacement.</returns>
    private int GetMaxLevelForReplacementStop(int originalLevel) => originalLevel + Archipelago.MaxShuffleLevelDifference;

    /// <summary>
    /// Filter the loot rewareded in an encoutner's rewards. Doing some/all of the follwing, based on settings.
    ///     1) Remove any weapons/armor from the loot pool, in order to not compete with progression bonuses.
    ///     2) Randomize the rewarded loot.
    /// </summary>
    /// <param name="loot">The un-filtered item list.</param>
    /// <returns></returns>
    private IEnumerable<Item> FilterLoot(IEnumerable<Item> loot)
    {
        bool allowModded = Archipelago.ShouldIncludeMods;
        bool allowItemBonus = Archipelago.ItemBonusSetting == ApItemBonusSettings.None;

        // Determine which randomization function to use
        Func<Item, Item> randomizerFunction = Archipelago.RandomizeEncounterLoot switch
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

        // Override the randomizer function if we've disallowed it
        if (Archipelago.DisallowRandomization)
            randomizerFunction = item => item;

        // Apply the function to the input loot
        // Randomize the loot first so we can try to replace "banned" items before they get adjusted/removed
        var randomizedLoot = loot.Select(randomizerFunction);

        // Filter fundamental runes if needed
        if (Archipelago.ItemBonusSetting != ApItemBonusSettings.None)
            return Loot.RemoveFundamentalRunes(randomizedLoot, allowModded);
        
        return randomizedLoot;

    }

    /// <summary>
    /// When a "bonus" stop is added to the campaign, we must inform archipelago.
    /// </summary>
    /// <param name="encounterIndex">The index of the stop.</param>
    private void NotifyBonusStop(int encounterIndex)
    {
        // If we have too many bonuses already, then this is a "no-reward" encounter
        if (standaloneBonusesThisModule++ >= MaxBonusesThisLevel)
            Archipelago.NoRewardEncounters.Add(encounterIndex);
        else
            Archipelago.StandaloneBonusEncoutners.Add(encounterIndex);
    }
    
    // Track the index where the last module ended.
    private int standaloneBonusesThisModule = 0;
    
    // The maximum number of bonuses we are allowed to award this level
    private int MaxBonusesThisLevel => (int) Math.Ceiling(
        (Archipelago.LocationCount - OriginalCampaignLength) / (double) (endLevel - startLevel + 1)
    );

    /// <summary>
    /// When we see a non-combat encounter, we must tell archipelago to skip giving rewards during it.
    /// </summary>
    /// <param name="encounterIndex">The index of the stop.</param>
    private void NotifyConversationStop(int encounterIndex)
    {
        Archipelago.NoRewardEncounters.Add(encounterIndex);
    } 

    /// <summary>
    /// When we reach the end of a module, we must check if we need to register bonus drops with archipealgo.
    /// </summary>
    /// <param name="encounterIndex">The index of the stop.</param>
    private void NotifyEndOfModule(int numTotalEncounters, int previousEncounterIndex)
    {
        // If we have too many items, put bonus drops at the last few stops
        if (numTotalEncounters < Archipelago.LocationCount)
        {
            int bonusToAward;

            // If we are on the last encounter, just fill all remaining slots
            if (numTotalEncounters - 1 == previousEncounterIndex)
                bonusToAward = Archipelago.LocationCount - numTotalEncounters - Archipelago.ExtraBonusEncounters.Count;
                
            // Otherwise, give bonuses proprotional to the amount of encounters we have claered so far
            else 
                bonusToAward = (int) Math.Round(
                    (Archipelago.LocationCount - numTotalEncounters)
                        * ((double)previousEncounterIndex / numTotalEncounters)
                    ) - Archipelago.ExtraBonusEncounters.Count;

            // Count backwards from the last encounter until we run out of bonuses
            // At normal (low) numbers, this will double up on the last few encounters in a module.
            // At high numbers, this will still bias the end of module, but will stack onto the earlier modules too
            //   thus, early modules will have more encounters - which is honestly fine enough to not make this more complicated
            while (bonusToAward > 0)
            {
                bonusToAward--;
                int id = (previousEncounterIndex - bonusToAward) % (previousEncounterIndex + 1);
                Archipelago.ExtraBonusEncounters.Add(id);
            }
        }

        // Reset the count now that we are done with the module
        standaloneBonusesThisModule = 0;   
    }

    /// <summary>
    /// Helper to check if a given encounter stop is a "real" encounter - that is, not a cutscene.
    /// </summary>
    /// <param name="stop">The stop to check.</param>
    /// <returns>True if the stop is not a cutscene encounter, false otherwise.</returns>
    public static bool IsNonCutsceneEncounter(CampaignStop stop)
    {
        if (stop is EncounterCampaignStop encounter)
        {
            var map = encounter.EncounterProvider();
            return !map.IsConversation && !map.IsCutscene;
        }
        return false;
    }

    /// <summary>
    /// Helper to check if a given campaign stop is an encounter with a cutscene.
    /// </summary>
    /// <param name="stop">The stop to check.</param>
    /// <returns>True if the stop is a cutscene encounter, false otherwise.</returns>
    public static bool IsCutsceneEncounter(CampaignStop stop)
    {
        if (stop is EncounterCampaignStop encounter)
        {
            var map = encounter.EncounterProvider();
            return map.IsConversation || map.IsCutscene;
        }
        return false;
    }

    /// <summary>
    /// Create a copy of a "CampaignStop" object, with new index and spoiler values.
    /// </summary>
    /// <param name="input">The stop to copy.</param>
    /// <param name="index">The new index of the stop in the adventure path.</param>
    /// <param name="level">The current level of the campaign.</param>
    /// <param name="opensChapter">If the stop begins a chapter, what is the name of that chapter.</param>
    /// <param name="initialDawsnbury">Is this the initial dawnsbury stop.</param>
    /// <returns>A modified copy of the provided campaign stop.</returns>
    private CampaignStop CopyStop(CampaignStop input, int index, int level, string? opensChapter, bool initialDawsnbury = false)
    {
        CampaignStop newStop = input;

        // Have to use the correct constructor for the encounter type
        if (input is EncounterCampaignStop eStop)
        {
            // Get the encounter built in to the stop
            var encounter = eStop.EncounterProvider();

            // If this is the first time we do this, get the encoutner rewards.
            if (!HasProcessedCampaignStops)
            {
                lootPile.AddRange(FilterLoot(encounter.Rewards));
                goldPile.Add(encounter.RewardGold);
            }

            // If the stop is a cutscene, tell the tracker
            if (IsCutsceneEncounter(eStop))
                NotifyConversationStop(encounterCounter);

            // Make a new encounter provider with our changes and attach it to the stop
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
            newStop = new DawnsburyStop(dStop.ShopkeeperFlavorText, initialDawsnbury, level, dStop.DawnsburyUnderTheSea, dStop.Name)
                .WithCustomShop(dStop.CustomShop?.Name ?? dStop.Name, dStop.CustomShop?.Illustration ?? IllustrationName.Shopkeeper1, 
                    dStop.ShopkeeperFlavorText, false, null, (item) => Loot.ShouldBeIncludedInArchipelagoShop(item, Archipelago), null);

        // Keep the "opens chapter" stop for the stop we repalce
        newStop.OpensChapter = opensChapter;

        // Values we are changing
        newStop.Index = index;
        newStop.Spoiler = true;

        return newStop;
    }

    /// <summary>
    /// Convert a stop into placehoder stop, by making a narration stop with it's relevant flavor text.
    /// </summary>
    /// <param name="stopToReplace">The stop to replace.</param>
    /// <param name="index">The index of the new stop in the path.</param>
    /// <param name="opensChapter">What chapter (if any) does this top open.</param>
    /// <returns>A placholder representing the stop.</returns>
    protected static CampaignStop NewPlaceholderStop(CampaignStop stopToReplace, int index, string? opensChapter)
    {
        // Get the name and description to use
        string description = stopToReplace.Description;
        string title = stopToReplace.Name;

        // We are replacing a level up stop, we aren't leveling up, so remove that from the name and description
        if (stopToReplace is LevelUpStop lStop)
        {
            title = "Progress";
            var reason = typeof(LevelUpStop).GetField("levelUpReasoning", BindingFlags.NonPublic | BindingFlags.Instance);
            description = (reason?.GetValue(lStop) as string) ?? description;
        }

        // Make and return the replacement stop
        return new NarratorStop(title, description, null)
        {
            OpensChapter = opensChapter,
            Index = index,
            Spoiler = true,
        };
    }

    /// <summary>
    /// Wrapper for the encounter provider function to allow us to change and pre-bake loot and level values during path construction.
    /// Use either the archipelago wrapper, or the minimal wrapper, depending on how much we want to change it.
    /// </summary>
    /// <param name="encounterToCopy">The original encounter.</param>
    /// <param name="currentLevel">The campaign's current level.</param>
    /// <param name="index">The adventure path stop index of the encounter.</param>
    /// <returns>A modified EncounterProvider function.</returns>
    private Func<Encounter> RandomEncounterProviderWrapper(Encounter encounter, int currentLevel, int index)
    {
        return () =>
        {
            // Update the state tracker, as having loaded an encounter, we are no longer in a menu
            DawnsburyArchipelagoLoader.InApCampaignMenu = false;

            // Try to get a new copy of the encounter via the lookup, or just use the original one if we cant.
            //var encounter = RegisteredEncounters.RegisteredEncountersByType[encounterToCopy.GetType()].EncounterSample;
            //encounter ??= encounterToCopy; // debug, throw instead of catching the null

            // Override the default level to match where it landed in the shuffle
            //  Note: if automatic leveling is on, this gets overwritten later anyway 
            encounter.CharacterLevel = currentLevel;

            // Check if this is the last encounter (both for credits and because we check it later)
            encounter.IsFinalCampaignEncounter = index + 1 == CampaignLengthIncludingNoncombat;

            // Try to shuffle the encoutner loot
            if (Archipelago.ShuffleEncounterLoot)
            {
                var (gold, loot) = GetLoot(index);
                encounter.RewardGold = gold;
                encounter.Rewards.Clear();
                encounter.Rewards.AddRange(loot);
            }

            // Even if not shuffling, check if we should do randomizations and filtering on it
            else if (!Archipelago.DisallowRandomization)
            {
                var loot = FilterLoot(encounter.Rewards).ToList();
                encounter.Rewards.Clear();
                encounter.Rewards.AddRange(loot);
            }

            // Return the copied encounter
            return encounter;
        };
    }

    /// <summary>
    /// Helper to pick the shuffled reward gold and item loot for a specific encounter index.
    ///   For encounter n, grab the nth bucket of loot.
    /// </summary>
    /// <param name="index">The encounter index to fech loot for.s</param>
    /// <returns>An amount of gold, and a list of items to drop in the encounter.</returns>
    private (int, IEnumerable<Item>) GetLoot(int index)
    {
        // Break the randomized loot pile into equal groups and award a range that aligns with this index.
        int startIndex = lootPile.Count * index / encounterCounter;
        int nextIndex = lootPile.Count * (index + 1) / encounterCounter;
        var loot = lootPile.GetRange(startIndex, nextIndex - startIndex);
        return (goldPile[index], loot);
    }

    /// <summary>
    /// Debug method to data mine the encounter loot for an adventure
    /// </summary>
    /// <param name="loot">A list of drops for an encounter.</param>
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
            // If a seed was specified, get its hash to use for the RNG
            return new Random(HashSeed(seed));

        return new Random();
    }

    /// <summary>
    /// Make an int hash of a string seed, compatible with new Random()
    /// </summary>
    /// <param name="seed">A String seed to hash</param>
    /// <returns>A numerical hash of the given text.</returns>
    public static int HashSeed(string seed) => BitConverter.ToInt32(MD5.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>
    /// Get the campaign's starting level from the input paths
    /// </summary>
    private static int GetStartLevel(AdventurePath[] input) => input.Min(path => path.StartingLevel);
    
    /// <summary>
    /// Determine the campaign's ending level from the input paths
    /// </summary>
    private static int GetEndLevel(AdventurePath[] input) =>
        input.Min(path => path.StartingLevel) + // get the starting level
        input.SelectMany(path => path.CampaignStops).Count(stop => stop is LevelUpStop) + // plus every level up in the campaigns
        input.Length - 1; // plus one more level for each campaign beyond the first (since you level up between them)

    /// <summary>
    /// Quick wrapper to add randomizer explainer text to the initial narration stop. 
    ///     If this is not the first shuffle, this just returns a copy instead.
    /// </summary>
    private NarratorStop AddTextToStartOfNarrationStop(NarratorStop original, string? newName, string explainerText) => HasProcessedCampaignStops? 
        new(original.Name, original.Description, original.VoiceLine) { Index = 0 } : 
        new(original.Name + " - " + newName ?? original.Name, explainerText + "\n\n" + original.Description, original.VoiceLine) { Index = 0 };

    /// <summary>
    /// Override AdventurePath's default quick-start character sheet creator,
    ///   so that we may randomize the characters for relevant settings.
    /// </summary>
    /// <returns>4 Quick-Start Character sheets</returns>
    public override IEnumerable<CharacterSheet> CreateQuickstartCharacterSheets()
    {
        // Check if we aren't randomizing the builds
        if (!Archipelago.RandomizeBuilds)
            return base.CreateQuickstartCharacterSheets();

        // Select 4 random pregens
        var rng = MakeSeededRng(Archipelago.RngSeed); // always use the same rng for a given seed
        return [.. CharacterLibrary.Instance.PregenProfiles.OrderBy(_ => rng.Next()).Take(4)];
    }

    /// <summary>
    /// Get the explainer text for the initial narrator stop that describes the randomizer.
    /// </summary>
    private static string ExplainerText =>
@"In this modded adventure path, you will play through a version of this campaign with the help of your archipelago.
Your character will unlock levels, bonuses and loot as a result of your archipelago, and every encounter you clear will send someone an item.
Good Luck!";
}