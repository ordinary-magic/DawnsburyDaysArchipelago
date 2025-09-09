using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Dawnsbury.Campaign.Path;
using Dawnsbury.Core;
using Dawnsbury.Core.Creatures;
using Dawnsbury.Core.Mechanics;
using Dawnsbury.Modding;
using Dawnsbury.Phases.Menus;
using HarmonyLib;

namespace DawnsburyArchipelago;

public class DawnsburyArchipelagoLoader
{
    // Save the randomized paths so we can reference them later
    public static AdventurePath? RandomDawnsburyDays { get; private set; }
    public static AdventurePath? ArchipelagoDawnsburyDays { get; private set; }

    /**
     * Main entry point into the mod.
     */
    [DawnsburyDaysModMainMethod]
    public static void LoadMod()
    {
        // Check if archipelago is available to determine if we should load the offline or online adventure path
        // TODO: test this swapping system
        if (ArchipelagoSetupMenu.TryConnectingToArchipelagoUsingCache())
        {
            // Randomize and load the dawnsbury days adventure (for archipelago)
            ArchipelagoDawnsburyDays = ArchipelagoPathRandomizer.ShufflePath(DawnsburyDaysAdventurePath.DawnsburyDaysPath);
            ModdedAdventurePaths.AllModdedPaths.Add(ArchipelagoDawnsburyDays);
        }
        else
        {
            // Randomize and load the dawnsbury days adventure (in offline mode)
            RandomDawnsburyDays = AdventurePathRandomizer.ShufflePath(DawnsburyDaysAdventurePath.DawnsburyDaysPath);
            ModdedAdventurePaths.AllModdedPaths.Add(RandomDawnsburyDays);
        }

        // Initialize the per-creature effects required by the mod
        ModManager.RegisterActionOnEachCreature(OnCreatureLoad);

        // Apply the harmony patch to add the archipelago button to the main menu
        Harmony harmony = new("Dawnsbury.Mods.ArchipelagoRandomizer");
        var dd_menu_draw = typeof(MainMenuPhase).GetMethod("Draw", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        harmony.Patch(dd_menu_draw, postfix: new HarmonyMethod(ArchipelagoSetupMenu.DrawArchipelagoButton));

        // Patch GameLoop.SpawnHero to allow us to change the spawn level
        var dd_spawnhero_tosheet = FindInternalMethod(typeof(GameLoop), "SpawnInitials", "SpawnHero");
        harmony.Patch(dd_spawnhero_tosheet, transpiler: new HarmonyMethod(SpawnHeroTranspiler));
    }

    /**
     * Deregister any currently active randomized adventure paths and replace them with a new archipelago path.
     */
    public static void SwapToArchipelagoRandomizedPath()
    {
        // Make sure we are actually connected first (not convinced we shouldnt just throw an error instead)
        if (ArchipelagoClient.Instance != null)
        {
            if (RandomDawnsburyDays != null)
            {
                ModdedAdventurePaths.AllModdedPaths.Remove(RandomDawnsburyDays);
                RandomDawnsburyDays = null;
            }
            if (ArchipelagoDawnsburyDays != null)
            {
                ModdedAdventurePaths.AllModdedPaths.Remove(ArchipelagoDawnsburyDays);
                ArchipelagoDawnsburyDays = null;
            }

            ArchipelagoDawnsburyDays = ArchipelagoPathRandomizer.ShufflePath(DawnsburyDaysAdventurePath.DawnsburyDaysPath);
            ModdedAdventurePaths.AllModdedPaths.Add(ArchipelagoDawnsburyDays);
        }
    }

    /**
     * Helper method to find a private method defined inside another method via reflection.
     */
    private static MethodInfo FindInternalMethod(Type parentClass, string outerMethod, string innerMethod)
    {
        // Compiled anonymous methods are attached to the class, and are something like: <OuterMethod>_InnerMethod
        //  thus, we can search all methods on the parent type for methods with names that contain both names. 
        var result = parentClass
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name.Contains(outerMethod) && m.Name.Contains(innerMethod));

        // If we cant find it, let the user know there is a problem
        if (result == null)
            throw new Exception("Could not find SpawnHero method in Game Loop - Has the game's code been updated?");

        return result;
    }

    /**
     * Harmony Transpiler method to find the ToCreature call in SpawnHeroes and let us affect its level argument.
     */
    private static List<CodeInstruction> SpawnHeroTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        // Define which method we are inserting (by defining a local variable we can use .Method instead of trying to bind to it by name)
        var insertedMethod = CharacterStatus.GetLevelForHeroIndex;

        // The Instruction Codes of the unchanged "SpawnHero" method
        var codes = new List<CodeInstruction>(instructions);
        for (int i = 0; i < codes.Count; i++)
        {
            // Look for the call to ToCreature
            if (codes[i].opcode == OpCodes.Callvirt && codes[i].operand is MethodInfo methodInfo && methodInfo.Name == "ToCreature")
            {
                // Walk backwards so we can replace the argument load operation
                for (int j = i; j >= 0; j--)
                {
                    // Search for the Characterlevel property get call
                    if (codes[j].opcode == OpCodes.Callvirt && (codes[j].operand.ToString()?.Contains("get_CharacterLevel") ?? false))
                    {
                        // Insert custom instructions directly after it
                        codes.InsertRange(j + 1,
                        [
                            // Add index to the stack so it can be our method's first argument
                            new CodeInstruction(OpCodes.Ldarg_1),

                            // Call our function to consume both arguments and give a new level value
                            new CodeInstruction(OpCodes.Call, insertedMethod.Method)
                        ]);

                        // Return the modified instruction set
                        return codes;
                    }
                }

                break; // We found the call to ToCreature, but not the rest of the match, so break and report the error
            }
        }

        // Our patch broke, throw an exception so the user knows
        throw new Exception("Could not patch SpawnHero with level adjustments - Has the game's code been updated?");
    }

    /**
     * Check to make sure we are in an active archipelago campaign (to not affect other gameplay)
     */
    public static bool IsArchipelagoCampaignActive()
    {
        // Check if the archipelago campaign exists (is connected) and if we are currently in it
        return ArchipelagoClient.MockArchipelago ||
            (ArchipelagoDawnsburyDays != null && CampaignState.Instance?.AdventurePath == ArchipelagoDawnsburyDays);
    }

    /**
     * Method to setup the archipelago connection qEffects on each pc at the start of each encounter.
     */
    public static void OnCreatureLoad(Creature pc)
    {
        // Only proceed if this creature is a pc and we are connected to archipelago
        if (IsArchipelagoCampaignActive() && pc.PersistentCharacterSheet != null)
        {
            // Add the qeffect to automatically adjusts stats as we progress
            pc.AddQEffect(CharacterStatus.GetProgressAdjustmentQEffect());

            // Setup the Battle Result QEffect
            pc.AddQEffect(GetEndOfBattleQEffect());

            // Setup the automatic tpk event
            pc.AddQEffect(GetDeatlinkCheckingQEffect());

            // Setup the Archipelago message queue.
            pc.AddQEffect(GetApLoggerQEffect());
        }
    }
    
        
    /**
     * Create a QEffect which will handle monitoring for/issuing TPK's for Archipelago's Deathlink
     */
    public static QEffect GetDeatlinkCheckingQEffect()
    {
        // remove any tpks that were sent while we were in a menu
        ArchipelagoClient.ClearDeathlinkRequest();

        return new QEffect()
        {
            // Check for pending TPK's at the start of each pc's turn
            StartOfYourEveryTurn = (qfSelf, owner) =>
            {
                return Task.Run(() =>
                {
                    var dlReason = ArchipelagoClient.GetAndClearDeathlinkRequest();
                    if (dlReason != "")
                        owner.Battle.EndTheGame(false, dlReason);
                });
            },
        };
    }

    /**
     * Create a QEffect to run our combat result callbacks.
     */
    public static QEffect GetEndOfBattleQEffect()
    {
        return new QEffect()
        {
            EndOfCombat = (qfSelf, wasVictory) =>
            {
                Task? result = Task.CompletedTask;

                // Only want to trigger this once, so check if we are the first pc in the party
                if (qfSelf.Owner.CreatureId == CharacterStatus.CampaignHeroes[0])
                {
                    if (wasVictory)
                        result = ArchipelagoClient.Instance?.SendNextEncounterLocation();
                    else
                        result = ArchipelagoClient.Instance?.SendDeathlink(qfSelf.Owner.Battle.VictoryReason);
                }
                return result ?? Task.CompletedTask;
            }
        };
    }

    /**
     * Create a QEffect to put archipelago messages in the combat log.
     */
    public static QEffect GetApLoggerQEffect()
    {
        return new QEffect()
        {
            StartOfYourEveryTurn = (qfSelf, owner) =>
            {
                return Task.Run(() =>
                {
                    // Empty all messages into the combat log
                    while (ArchipelagoClient.MessageQueue.TryDequeue(out string? message))
                        owner.Battle.Log(message, null, null, null, Microsoft.Xna.Framework.Color.Magenta);
                });
            }
        };
    }
}