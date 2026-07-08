using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Packets;
using System.Linq;
using DawnsburyArchipelago.Data;
using Newtonsoft.Json.Linq;

namespace DawnsburyArchipelago;

public class ArchipelagoClient(ApConnectionInfo connection)
{
    // Singleton Instance (anti-pattern, but its easy) //
    public static ArchipelagoClient? Instance { get; set; }

    // Debug Property to enable simulated effects without an archipelago connection
    public static readonly bool MockArchipelago = false;

    private const int PROTOCOL_VERSION = 10400; // 1.04.00

    // Properties //
    public bool Ready { get; private set; } = false; // Is the client ready to go
    public static bool InstanceReady => Instance?.Ready ?? false; // static shortcut for ^
    private long apBaseIDOffset = 0; // Archipelago ids must have a unique range, so we start at the offset

    // Configuration Information //
    public string RngSeed { get; set; } = "";
    public bool UseRandomEncounterOrder { get; private set; } = false;
    public int MaxShuffleLevelDifference = 0;
    public ApCampaignChoice Campaign = ApCampaignChoice.DawnsburyDays;
    public ApFreeEncounterOptions IncludeFreeEncounters = ApFreeEncounterOptions.None;
    public bool IncludeExtremePlusFreeEncounters = false;
    public bool ShuffleEncounterLoot = false;
    public ApLootRandomization RandomizeEncounterLoot = ApLootRandomization.None;
    public ApItemBonusSettings ItemBonusSetting = ApItemBonusSettings.Automatic;
    public ApLockedActions ShouldLockActions = ApLockedActions.None;
    public bool ShouldIncludeMods = false;
    private int bonusLocationStart = 0;

    // State Data //
    public int EncountersCleared { get; set; } = 0; // TODO (Eventually): sync this with the server to allow resuming runs.
    public int BonusEncountersCleared { get; set; } = 0;
    public int ItemsRecieved { get; set; } = 0; // Total amount of items recieved from archipelago
    public int InventoryItemsSaved { get; set; } = 0; // Number of items recieved which are saved in the character inventories
    public int LocationCount => apSession.Locations.AllLocations.Count; // The total amount of locations we must check.
    public List<int> ExtraBonusEncounters { get; } = []; // List of encounters which should drop a "bonus" location, in addition to their normal check
    public List<int> StandaloneBonusEncoutners { get; } = []; // List of encounters which should drop a "bonus" location instead of their normal check
    public List<int> NoRewardEncounters { get; } = []; // List of encounters which do not award anything, and should be ignored by archipealgo

    // Constant Fields //
    private readonly ArchipelagoSession apSession = ArchipelagoSessionFactory.CreateSession(connection.Server, connection.Port);
    private readonly string slot = connection.Slot; // Archipelago server slot name
    private readonly string password = connection.Password; // Archipelago server password
    private readonly List<long> locationsToNotify = []; // Location checks performed while not online

    // Deathlink options //
    private DeathLinkService? DeathLink { get; set; }
    private static string TPKReason { get; set; } = "";
    private int deathLinkCounter = 0;
    private readonly int deathLinkAmount = 1;

    // Methods //
    /*
     * Try to connect to archipelago. Will return a string error message on failure, or null on success
     */
    public string? ConnectArchipelago()
    {
        var login = TryToConnect();

        if (login is LoginFailure)
        {
            // Try connecting twice (this works surprisingly often (the library is not good))
            login = TryToConnect();

            if (login is LoginFailure failure)
                return failure.Errors[0];
        }

        // Get the slot data from the login packet and initialize the randomizer
        InitializeRandomizer(((LoginSuccessful)login).SlotData);

        // Everything completed succesfully!
        return null;
    }

    /*
     * Try to create a new apSession to the server using the saved address and login information.
     */
    private LoginResult TryToConnect()
    {
        // Connect to the archipelago server
        try
        {
            // Login to the server
            return apSession.TryConnectAndLogin("Dawnsbury Days", slot,
                    ItemsHandlingFlags.AllItems, password: password);

        }
        catch (Exception e)
        {
            return new LoginFailure(e.Message);
        }
    }

    /*
     * Initialize the Randomizer and attach the relevant archipelago update callbacks.
     */
    private void InitializeRandomizer(Dictionary<string, object> slotData)
    {
        // Check if the version matches (handle old versions of the code)
        int serverVersion = Convert.ToInt32(slotData.GetValueOrDefault("version") ?? 0);
        if (serverVersion < PROTOCOL_VERSION)
            ApMessages.LogError($"Archipelago Version Mismatch: Your Archipelago Server is out of date; some features might not work correctly.");
        else if (serverVersion > PROTOCOL_VERSION)
            ApMessages.LogError($"Archipelago Version Mismatch: Your Game Mod is out of date; some features might not work correctly.");

        // Archipelago requires unique keys across all games, so we solve this by defining a base offset for items/locations
        apBaseIDOffset = Convert.ToInt64(slotData["base_offset"]);

        // get the custom rng seed, or default to the archipelago server's seed
        RngSeed = Convert.ToString(slotData["rng_seed"]) ?? apSession.RoomState.Seed;
        if (RngSeed == "") RngSeed = apSession.RoomState.Seed;

        // get the randomization settings
        UseRandomEncounterOrder = Convert.ToBoolean(slotData["encounter_shuffle"]);
        ShuffleEncounterLoot = Convert.ToBoolean(slotData["loot_shuffle"]);

        // Settings which weren't in the first version need default values in case of version mismatch
        Campaign = (ApCampaignChoice) Convert.ToInt32(slotData.GetValueOrDefault("campaign") ?? Campaign);
        InitializeVersionedOptions(slotData, serverVersion);

        // Initialize the character's status
        int start_level = Convert.ToInt32(slotData["start_level"]);
        int atk_bonus = Convert.ToInt32(slotData["start_atk_bonus"]);
        int armor_bonus = Convert.ToInt32(slotData.GetValueOrDefault("start_armor_bonus") ?? 0); // new metadata
        int skill_bonus = Convert.ToInt32(slotData.GetValueOrDefault("start_skill_bonus") ?? 0); // new metadata
        int perception = Convert.ToInt32(slotData.GetValueOrDefault("start_perception_bonus") ?? skill_bonus); // new metadata
        CharacterStatus.InitializeCampaignHeroes(start_level, atk_bonus, armor_bonus, skill_bonus, perception, ShouldLockActions);

        // Check for "free" unlocks
        CheckForAutomaticUnlocks(slotData);

        // Setup the deathlink service if its enabled
        if (Convert.ToBoolean(slotData["deathlink"]))
        {
            //deathLinkAmount = Convert.ToInt32(slotData["dl_amount"]);
            deathLinkCounter = deathLinkAmount;
            DeathLink = apSession.CreateDeathLinkService();
            DeathLink.EnableDeathLink();
            DeathLink.OnDeathLinkReceived += dl =>
            {
                if (dl.Source != apSession.Players.ActivePlayer.Name)
                    lock (TPKReason)
                        TPKReason = dl.Cause;
            };
        }

        // Add our message handling
        apSession.MessageLog.OnMessageReceived += ApMessages.OnApMessage;

        // Add an error logger to watch for if the socket is closed
        apSession.Socket.SocketClosed += reason => ApMessages.LogError("Lost Connection to Server: " + reason);

        // Initialize the callback
        apSession.Items.ItemReceived += NewItemRecieved;

        // Initialize the data storage if it doesnt already have a value
        apSession.DataStorage[Scope.Slot, "encounters_cleared"].Initialize(0);
        apSession.DataStorage[Scope.Slot, "bonus_encounters_cleared"].Initialize(0);
        apSession.DataStorage[Scope.Slot, "inventory_items_saved"].Initialize(0);

        // Load saved progress from the server
        EncountersCleared = apSession.DataStorage[Scope.Slot, "encounters_cleared"];
        BonusEncountersCleared = apSession.DataStorage[Scope.Slot, "bonus_encounters_cleared"];
        InventoryItemsSaved = apSession.DataStorage[Scope.Slot, "inventory_items_saved"];
        CatchUpToOldItems();

        // If we are successfully configured, we are the relevant instance
        Instance = this;
        Ready = true;
    }

    /*
    * Configure options whose data type or format was added or changed in later versions
    */
    private void InitializeVersionedOptions(Dictionary<string, object> slotData, int serverVersion)
    {
        if (serverVersion >= 10400)
        {
            ItemBonusSetting  = (ApItemBonusSettings) Convert.ToInt32(slotData["item_bonuses"]);
            ShouldLockActions = (ApLockedActions) Convert.ToInt32(slotData["locked_actions"]);
            ShouldIncludeMods = Convert.ToBoolean(slotData["mod_content"]);
            MaxShuffleLevelDifference = Convert.ToInt32(slotData["shuffle_level"]);
            IncludeFreeEncounters = (ApFreeEncounterOptions) Convert.ToInt32(slotData["include_free_encounters"]);
            IncludeExtremePlusFreeEncounters = Convert.ToBoolean(slotData["include_extreme_encounters"]);
            RandomizeEncounterLoot = (ApLootRandomization) Convert.ToInt32(slotData["loot_randomizer"]);
            
            // "Bonus" items are new as of this version. We dont have to set it in previous versions because it wont come up.
            long id = apSession.Locations.GetLocationIdFromName(apSession.Players.ActivePlayer.Game , "Bonus #1");
            bonusLocationStart = (int)(id - apBaseIDOffset); // We want it in item space, not location space
        }
        else
        {
            // Shuffle Level Difference used to be an enum, which we must convert
            var shuffleDifficulty = (ApEncounterDifficulty) Convert.ToInt32(slotData.GetValueOrDefault("shuffle_difficulty") ?? 0);
            MaxShuffleLevelDifference = shuffleDifficulty switch
            {
                ApEncounterDifficulty.Balanced => 1,
                ApEncounterDifficulty.Difficult => 20,
                _ => 0
            };
        }
        
        if (serverVersion == 10300)
        { 
            // In version 1.3.0, this was a boolean with a different name
            bool potency = Convert.ToBoolean(slotData.GetValueOrDefault("potency_runes"));
            ItemBonusSetting = potency? ApItemBonusSettings.Manual : ApItemBonusSettings.Automatic;
        }

        // Anyhting not listed will simply use its default value
    }

    /*
    * Check for any items which are unlocked automatically
    */
    private void CheckForAutomaticUnlocks(Dictionary<string, object> slotData)
    {
        // TODO: need to lookup how to deserialize the json object here
        //var items = (long[]) (slotData.GetValueOrDefault("excluded_items") ?? Array.Empty<long>());

        // Only proceed if we actually have any excluded items
        if (!slotData.TryGetValue("excluded_items", out object? value))
            return;

        var items = JArray.FromObject(value).Values<long>();

        // Check for who (if anybody) has the starter interact
        if (ShouldLockActions == ApLockedActions.Extreme)
            foreach (var id in items)
            {
                int localId = (int) (id - apBaseIDOffset);
                if (localId / 4 == (int) ApPerCharacterItemTypes.Interact)
                {
                    ApMessages.LogEvent($"Got a Starter Item - Interact!");
                    CharacterStatus.ApplyCharacterUpgradeItem(localId, false);
                }
            }
    }

    /*
    * Callback to process an item received event from the server
    */
    private async void NewItemRecieved(ReceivedItemsHelper helper)
    {
        // Process all pending items
        while (helper.PeekItem() is ItemInfo item)
        {
            // Pop the item off of the queue
            helper.DequeueItem();

            // Skip processing if we already have this item
            if (helper.Index <= ItemsRecieved) continue;
            
            // Increment the item count
            ItemsRecieved++;

            // Process the new item
            await GiveArchipelagoItem(item);
        }
    }

    /*
     * Give an archipelago item to the player
     * Is asynchronous incase GiveItem is blocked so as to not softlock the archipelago client.
     */
    private async Task<bool> GiveArchipelagoItem(ItemInfo item, bool canIssuePermanant = true)
    {
        int id = GetItemId(item);
        bool permanant = false;

        // Check if it's per-character upgrade item
        if (id < (int)ApPerCharacterItemTypes.END * 4)
        {
            ApMessages.LogEvent($"Got {item.ItemName} from {item.Player.Name}!");
            permanant = await Task.Run(() =>
                CharacterStatus.ApplyCharacterUpgradeItem(id, canIssuePermanant));
        }
            
        // Check if its a loot bag
        else if (id == (int) ApSingletonItemTypes.LootBag)
        {
            // Dont drop duplicate loot bags
            if (canIssuePermanant)
            {
                ApMessages.LogEvent($"{item.Player.Name} found some Loot!");
                Loot.AwardLootBag(ShouldIncludeMods, ItemBonusSetting == ApItemBonusSettings.None);
            }
            permanant = true;
        }

        // Check if its a trap item
        else if (id >= (int) ApSingletonItemTypes.ClumsyTrap)
        {
            // Traps are not saved in your inventory, but they behave similarly in that
            //    we want them to only trigger once. This mostly accomplishes that.
            if (canIssuePermanant)
            {
                ApMessages.LogEvent($"{item.Player.Name} triggerd a {item.ItemName}!");
                CharacterStatus.PendingTraps.Enqueue((ApSingletonItemTypes) id);
            }
            permanant = true;
        }

        // Update our state tracking
        if (permanant && canIssuePermanant) 
            InventoryItemsSaved++;
        
        return permanant;
    }

    /*
     * Catch up to all items we recieved before this instance started running
     */
    private async void CatchUpToOldItems()
    {
        // Clear all pending item updates
        List<ItemInfo> catchupList = [];
        while (apSession.Items.DequeueItem() is ItemInfo item)
            catchupList.Add(item);

        // Count how many items we found
        ItemsRecieved = catchupList.Count;
        int savedItemsDiscovered = 0;

        // Process the item
        foreach (var item in catchupList)
            if (await GiveArchipelagoItem(item, savedItemsDiscovered > InventoryItemsSaved))
                savedItemsDiscovered++;
    }

    // Get the integer id of an archipelago item
    private int GetItemId(ItemInfo item) => (int)(item.ItemId - apBaseIDOffset);

    /*
     * Tell archipelago that we cleared the next encounter
     */
    public async Task SendNextEncounterLocation()
    {
        // Award any additional bonus locations which are stacked onto this encounter
        foreach (int _ in ExtraBonusEncounters.Where(id => id == EncountersCleared))
            await SendNextBonusEncounterLocation();

        // If this location has a standalone bonus encounter, we award it instead of the next regular encounter
        if (StandaloneBonusEncoutners.Contains(EncountersCleared))
        {
            IncrementEncounterCount();
            await SendNextBonusEncounterLocation();
        }

        // If this is an encounter which does not award anything, then just increment the count and move on
        else if (NoRewardEncounters.Contains(EncountersCleared))
            IncrementEncounterCount();

        // Otherwise, send the normal location check
        else
            await SendNextBaseEncounterLocation();
    }

    /**
     * Tell archiepalgo that we cleared a bonus encounter
     */
    private Task SendNextBonusEncounterLocation()
    {
        apSession.DataStorage[Scope.Slot, "bonus_encounters_cleared"] = BonusEncountersCleared + 1;
        return SendLocationCheck(bonusLocationStart + BonusEncountersCleared++);
    }

    /**
     * Tell archiepalgo that we cleared a regular encounter
     */
    private async Task SendNextBaseEncounterLocation()
    {
        int standaloneBonuses = StandaloneBonusEncoutners.Count(id => id < EncountersCleared);
        int noRewardCount = NoRewardEncounters.Count(id => id < EncountersCleared);
        await SendLocationCheck(EncountersCleared - standaloneBonuses - noRewardCount);
        IncrementEncounterCount();
    }

    /**
     * Update the count of clered encoutners in archipelago
     */
    private void IncrementEncounterCount()
    {
        EncountersCleared++;
        apSession.DataStorage[Scope.Slot, "encounters_cleared"] = EncountersCleared;
    }

    /*
     * Tell archipelago that we cleared an encounter
     */
    public Task SendLocationCheck(int locationId) => SendLocationChecks([locationId]);
    
    /*
     * Tell archipelago that we cleared a number of encounters
     */
    public Task SendLocationChecks(int[] locationIds)
    {
        // Convert the locations to ap locations, ignoring invalid or duplicate locations
        //   Note: while duplicates dont matter, we filter out the "last" location on the server, so it wont know about it.
        locationsToNotify.AddRange(locationIds
            .Select(id => id + apBaseIDOffset)
            .Where(apSession.Locations.AllMissingLocations.Contains)
        );

        if (apSession != null)
        {
            try
            {
                // Save the awaitable as a task for the parent to wait on.
                // Do snapshotting in case the check in takes time, so that we dont accidentally clear the list incorrectly.
                //   Archipeligo will ignore duplicate location updates, so we dont need to worry about that condition.
                return Task.Run(async () =>
                {
                    var snapshot = locationsToNotify.ToList();
                    await apSession.Locations.CompleteLocationChecksAsync([.. snapshot]);
                    locationsToNotify.RemoveAll(location => snapshot.Contains(location));
                });
            }
            catch (Exception e)
            {
                ApMessages.LogError($"Couldn't Connect to Archipelago: {e.Message}");
            }
        }
        return Task.CompletedTask;
    }

    /*
     * Send the server a deathlink
     */
    public Task SendDeathlink(string? reason = null)
    {
        var name = apSession.Players.ActivePlayer.Name;
        string cause = reason ?? $"{name} lost a battle";
        if (deathLinkCounter++ >= deathLinkAmount)
        {
            deathLinkCounter = 0;
            var dl = new DeathLink(name, cause);
            return Task.Run(() => DeathLink?.SendDeathLink(dl));
        }
        return Task.CompletedTask;
    }

    /*
     * Check if there is a pending deathlink, returning its cause and clearing it.
     */
    public static string GetAndClearDeathlinkRequest()
    {
        lock (TPKReason)
        {
            var result = TPKReason;
            TPKReason = "";
            return result;
        }
    }

    /*
     * Clear the pending deathlink, without checking it.
     */
    public static void ClearDeathlinkRequest()
    {
        lock (TPKReason)
            TPKReason = "";
    }

    /*
     * Check if the game is complete, and notify archipelago if it is
     */
    public Task BeatGame()
    {
        // Send the "Won the game" status update
        apSession.Socket.SendPacket(new StatusUpdatePacket
        {
            Status = ArchipelagoClientState.ClientGoal
        });

        // Check all missing locations (all encounters cleared)
        return apSession.Locations.CompleteLocationChecksAsync([..
            apSession.Locations.AllMissingLocations
        ]);
    }
    
    /*
     * Get specific slot data from archipelago, or the default value if that fails
     */
    public T? GetSlotData<T>(string id)
    {
        try
        {
            return apSession.DataStorage[Scope.Slot, id].To<T>();
        }
        catch (Exception)
        {
            return default;
        }
    }

    /*
     * Save the inventory state to the archipelago server's data storage
     */
    public void SaveInventory()
    {
        apSession.DataStorage[Scope.Slot, "inventory_items_saved"] = InventoryItemsSaved;
    }
}