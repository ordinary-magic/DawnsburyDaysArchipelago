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
using Dawnsbury.Display.Notifications;
using Dawnsbury.Modding;
using Dawnsbury.Phases.Ingame;
using Dawnsbury.Phases.Menus;
using DawnsburyArchipelago.Data;
using HarmonyLib;

namespace DawnsburyArchipelago;

public class DawnsburyArchipelagoLoader
{
    // Save the randomized paths so we can reference them later
    public static ArchipelagoAdventurePath? ArchipelagoCampaign { get; private set; }

    /**
     * Main entry point into the mod.
     */
    [DawnsburyDaysModMainMethod]
    public static void LoadMod()
    {
        // Initialize the per-class effects required by the mod
        ModManager.RegisterActionOnEachCreature(OnCreatureLoad);
        ModManager.RegisterActionOnEachItem(Loot.AddApItemModifications);

        // Register the drawing modifications used by the mod
        //ModManager.Frontend.RegisterAtEndOfDrawFrame(ApMessages.DrawToasts); // doesnt work, since we dont get frame time
        ArchipelagoSetupMenu.RegisterArchipelagoButtonInModManager();

        // Finally, Setup the harmony patches
        LoadHarmony();
    }

    /**
     *  Initialize all the harmony patches used by the mod
     */
    public static void LoadHarmony()
    {
        // Create the harmony instance
        Harmony harmony = new("Dawnsbury.Mods.ArchipelagoRandomizer");

        // Patch GameLoop.SpawnHero to allow us to change the spawn level
        var dd_spawnhero_tosheet = FindInternalMethod(typeof(GameLoop), "SpawnInitials", "SpawnHero");
        harmony.Patch(dd_spawnhero_tosheet, transpiler: new HarmonyMethod(SpawnHeroTranspiler));

        // Patch CampaignMenuPhase.CreateViews to do things when we enter the campaign menu
        var dd_campaign_menu_create = typeof(CampaignMenuPhase).GetMethod(nameof(CampaignMenuPhase.CreateViewsWithKeepingIndex), BindingFlags.Public | BindingFlags.Instance);
        harmony.Patch(dd_campaign_menu_create, prefix: new HarmonyMethod(OnCampaignMenuLoad));

        // Patch Toasts.Draw to make it also call our toast method.
        var dd_toasts_draw = typeof(Toasts).GetMethod(nameof(Toasts.Draw), BindingFlags.Public | BindingFlags.Static);
        harmony.Patch(dd_toasts_draw, postfix: new HarmonyMethod(ApMessages.DrawToasts));
    }

    /**
     * Deregister any currently active randomized adventure paths and replace them with a new archipelago path.
     */
    public static void SwapToArchipelagoRandomizedPath()
    {
        // Make sure we are actually connected first (not convinced we shouldnt just throw an error instead)
        if (ArchipelagoClient.Instance is ArchipelagoClient client)
        {
            if (ArchipelagoCampaign != null)
            {
                ModdedAdventurePaths.AllModdedPaths.Remove(ArchipelagoCampaign);
                ArchipelagoCampaign = null;
            }

            // Load the archipelago campaign, using the correct settings
            AdventurePath[] paths = ArchipelagoClient.Instance.Campaign switch
            {
                // Game Adventure Paths //
                ApCampaignChoice.DawnsburyDays => [DawnsburyDaysAdventurePath.DawnsburyDaysPath],
                ApCampaignChoice.TheProfaneBarrier => [TryToLoadProfaneBarrierAdventurePath()],
                ApCampaignChoice.DDandPB => [
                        DawnsburyDaysAdventurePath.DawnsburyDaysPath,
                        TryToLoadProfaneBarrierAdventurePath(),
                    ],
                ApCampaignChoice.GoodLittleChildren => [TryToLoadGoodLittleChildrenPath()],
                ApCampaignChoice.All3 => [
                        DawnsburyDaysAdventurePath.DawnsburyDaysPath,
                        TryToLoadProfaneBarrierAdventurePath(),
                        TryToLoadGoodLittleChildrenPath(),
                    ],

                // Other Adventure Paths //
                ApCampaignChoice.Roguelike => [TryToLoadRoguelikePath()],
                
                _ => throw new InvalidOperationException(
                    $"Unknown Campaign: {ArchipelagoClient.Instance.Campaign}. Your mod might be out of date.")
            };

            // Make the randomizer
            ArchipelagoCampaign = new ArchipelagoAdventurePath(paths, client);
            
            // Setup the adventure path (roguelike processning must be deferred)
            if (client.Campaign != ApCampaignChoice.Roguelike)
                ArchipelagoCampaign.ProcessCampaignStops();

            // Finally, register the new path with the game
            ModdedAdventurePaths.AllModdedPaths.Add(ArchipelagoCampaign);
        }
    }

    /**
     * Try to load the profane barrier adventure path if the dll is installed.
     */
    private static AdventurePath TryToLoadProfaneBarrierAdventurePath()
    {
        var path = LoadDlcAdventurePath("Dawnsbury.TheProfaneBarrier", "Dawnsbury.Dlc.TheProfaneBarrierAdventurePath", "ProfaneBarrierPath");
        return path ??
            throw new InvalidOperationException("Could not load \"The Profane Barrier\" for Archipelago: DLC is not installed.");
    }

    private static AdventurePath TryToLoadGoodLittleChildrenPath()
    {
        var path = LoadDlcAdventurePath("Dawnsbury.GoodLittleChildrenNeverGrowUp", "Dawnsbury.Dlc.GoodLittleChildrenAdventurePath", "GoodLittleChildrenPath");
        return path ??
            throw new InvalidOperationException("Could not load \"Good Little Children Never Grow Up\" for Archipelago: DLC is not installed.");
    }

    private static AdventurePath TryToLoadRoguelikePath()
    {
        return ModdedAdventurePaths.AllModdedPaths.FirstOrDefault(path => path.Id == "RoguelikeMode") ??
            throw new InvalidOperationException("Could not load \"Roguelike Mode\" for Archipelago: Mod is not installed.");
    }

    /**
     * Check if a user has an assembly installed, and get a type from it
     */
    private static Type? GetTypeByReflection(string assemblyName, string className)
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == assemblyName);
        return assembly?.GetType(className);
    }

    /**
     * Check if a user has a dlc assembly installed, and extract the requested adventure path if they do
     */
    private static AdventurePath? LoadDlcAdventurePath(string dllName, string className, string propertyName)
    {
        // Try to get the dlc class
        var type = GetTypeByReflection(dllName, className);
        if (type != null)
        {
            // Search for the instance in the type
            var instanceField = type.GetField(propertyName, BindingFlags.Public | BindingFlags.Static);
            if (instanceField != null)
                return (AdventurePath?)instanceField.GetValue(null);
                
            throw new InvalidOperationException($"Could not find '{propertyName}' in class '{className}'");
        }
        
        throw new InvalidOperationException($"Could not find '{className}' in dll '{dllName}'");
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
    public static bool IsArchipelagoCampaignActive(bool allowMock = true)
    {
        // Check if the archipelago campaign exists (is connected) and if we are currently in it
        return (ArchipelagoClient.MockArchipelago && allowMock) ||
            (ArchipelagoCampaign != null && CampaignState.Instance?.AdventurePath == ArchipelagoCampaign);
    }

    /**
     * Method to setup the archipelago connection qEffects on each pc at the start of each encounter.
     */
    public static void OnCreatureLoad(Creature creature)
    {
        // Only proceed if this creature is a pc and we are connected to archipelago
        if (IsArchipelagoCampaignActive() && creature.PersistentCharacterSheet != null)
        {
            if (ArchipelagoClient.Instance is ArchipelagoClient client)
            {
                // Create the automatic item bonus QEffect if needed
                if (client.ItemBonusSetting == ApItemBonusSettings.Automatic)
                    creature.AddQEffect(CharacterStatus.GetAutomaticItemBonusQEffect());

                // Create the action locking QEffect if needed
                if (client.ShouldLockActions.LockBasicActions())
                    creature.AddQEffect(CharacterStatus.GetActionLockingQEffect());
            }

            // Setup the Battle Result QEffect
            creature.AddQEffect(GetEndOfBattleQEffect());

            // Setup the automatic tpk event
            creature.AddQEffect(GetDeatlinkCheckingQEffect());
            
            // Create the QEffects to process trap & boon events
            creature.AddQEffect(CharacterStatus.GetTrapApplicationQEffect());
            creature.AddQEffect(CharacterStatus.GetBoonApplicationQEffect());
        }

        // Do the following on every characer when we are connected, regardless of if they are a pc
        if (IsArchipelagoCampaignActive())
        {
            // Setup the Archipelago message queue.
            creature.AddQEffect(GetApLoggerQEffect());
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

                    // DEBUG - Auto win an encounter
                    //owner.Battle.EndTheGame(true, "Cheating");
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
                    // Try to catch up on any pending loot drops as we return to the campaign menu
                    Loot.TryToAwardPendingLoot();

                    var encounter = qfSelf.Owner.Battle.Encounter;

                    // If this was the final mission and we won, win the game
                    if (encounter.IsFinalCampaignEncounter && wasVictory)
                        result = ArchipelagoClient.Instance?.BeatGame();

                    // Otherwise, if we won, send the next location check
                    else if (wasVictory)
                        result = ArchipelagoClient.Instance?.SendNextEncounterLocation();

                    else
                        // On a loss, try to send a deathlink
                        result = ArchipelagoClient.Instance?.SendDeathlink(qfSelf.Owner.Battle.VictoryReason);

                    // If the player lost the battle and we are supposed to reset, set the pending reset flag.
                    // Note: this flag can be cleared if they "Restart Encounter" and win before returning to the menu
                    //       but I dont have a way of stopping them from restarting it, so its better that it
                    //       can be reset than that they can redo it and still lose later (i think)
                    PendingHardcoreReset = !wasVictory && (ArchipelagoClient.Instance?.HardcoreMode ?? false); 
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
                Task.Run(() => ApMessages.UpdateBattleChat(owner.Battle))
        };
    }

    // Flag to indicate if we are currently in the Ap campaign's menu
    public static bool InApCampaignMenu {get; set;} = false;

    /// <summary>
    /// Prefix patch for CampaignMenuPhase.CreateViewsWithKeepingIndex
    /// </summary>
    public static void OnCampaignMenuLoad()
    {
        if (IsArchipelagoCampaignActive(false))
        {
            // Award loot first (it might get wiped out)
            Loot.TryToAwardPendingLoot();

            // Check if we must reset progress
            TryToDoHardcoreModeReset();

            // Check for roguelike mode's post initialization stuff
            TryToInitializeAPRoguelikeCampaign(false);

            // Set our flags
            InApCampaignMenu = true;
            ApMessages.ReadyForToasts = true;
        }
    }

    // Flag to indicate if we should reset the run when we next load the menu
    public static bool PendingHardcoreReset {get; private set;} = false;

    /// <summary>
    /// Delete and remake the campaign, as punishment for losing in hardcore mode.
    /// </summary>
    public static void TryToDoHardcoreModeReset()
    {
        if (IsArchipelagoCampaignActive(false) && PendingHardcoreReset)
        {
            // Reset progress to the first campaign stop (nulls are checked by IsActive method)
            ArchipelagoClient.Instance!.ResetEncounterProgress();
            CampaignState.Instance!.CurrentStopIndex = 0;

            // Regenerate the campaign
            if (ArchipelagoClient.Instance?.Campaign == ApCampaignChoice.Roguelike)
                TryToInitializeAPRoguelikeCampaign(true);
            else
                ArchipelagoCampaign!.ProcessCampaignStops();

            // Todo: This does not reset the inventory, should I add that?

            // Reset is complete.
            PendingHardcoreReset = false;
        }
    }

    /// <summary>
    /// Roguelike campaign is designed to be initialized with the campaign menu load,
    /// so we need a method to do additional setup here.
    /// </summary>
    /// <param name="forcedReset">Should we reset the adventure path, or should we only generate if we haven't yet done so.</param>
    public static void TryToInitializeAPRoguelikeCampaign(bool forcedReset = false)
    {
        var state = CampaignState.Instance;
        var archipelago = ArchipelagoClient.Instance;

        // Check that we are beginning a roguelike ap campaign which hasnt been randomized before
        if (ArchipelagoCampaign is not null
            && state?.AdventurePath is AdventurePath path
            && archipelago?.Campaign == ApCampaignChoice.Roguelike
            && (!ArchipelagoCampaign.HasProcessedCampaignStops || forcedReset))
        {
            // Setup the rng seed. (Either via the run's seed, or a permutation of the last one.)
            string seed = forcedReset? state.Tags["seed"] + "_RESET" : archipelago.RngSeed;
            state.Tags["seed"] = ArchipelagoAdventurePath.HashSeed(seed).ToString();

            // Other required metadata (not used, but are expected by generator and it will crash without them)
            state.Tags["deaths"] = "0";
            state.Tags["restarts"] = "0";

            // Find Roguelike Mode's GenerateRun method and invoke it to build the campaign encounters
            var harmonyPatches = GetTypeByReflection("Dawnsbury.Mods.Creatures.RoguelikeMode", "Dawnsbury.Mods.Creatures.RoguelikeMode.Patches.HarmonyPatches");
            var rm_generate_run = (harmonyPatches?.GetMethod("GenerateRun", BindingFlags.NonPublic | BindingFlags.Static))
                ?? throw new InvalidOperationException("Could not initialize the Roguelike Mode campaign for archipelago. Has the mod's code changed?");
            rm_generate_run.Invoke(null, [state]);

            // Now that we have the actual encounters, patch them in the randomizer for patching
            ArchipelagoCampaign.OriginalCampaignStops = path.CampaignStops;
            ArchipelagoCampaign.ProcessCampaignStops();
        }
    }
}