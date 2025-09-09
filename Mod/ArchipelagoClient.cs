using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.Packets;
using System.Linq;

namespace DawnsburyArchipelago;

/*
 * Archipelago connection helper class
 */
public class ApConnectionInfo(string server, int port, string slot, string password)
{
    public string Server { get; set; } = server;
    public int Port { get; set; } = port;
    public string Slot { get; set; } = slot;
    public string Password { get; set; } = password;
}

public class ArchipelagoClient(ApConnectionInfo connection)
{
    // Singleton Instance (anti-pattern, but its easy) //
    public static ArchipelagoClient? Instance { get; set; }

    // Debug Property to enable simulated effects without an archipelago connection
    public static readonly bool MockArchipelago = false;

    // Enum defining the item types we get from the server
    public enum ApItemTypes
    {
        LevelUp,
        WeaponImprovement,
        ArmorImprovement
    }

    // Properties //
    public bool Ready { get; private set; } = false; // Is the client ready to go
    public static bool InstanceReady => Instance?.Ready ?? false; // static shortcut for ^

    // Configuration Information //
    public string RngSeed { get; set; } = "";
    public bool UseRandomEncounterOrder { get; private set; } = false;
    public bool ShuffleEncounterLoot { get; private set; } = false;
    private long apBaseIDOffset = 0; // Archipelago ids must have a unique range, so we start at the offset

    // State Data //
    public int EncountersCleared { get; set; } = 0; // TODO (Eventually): sync this with the server to allow resuming runs. 

    // Constant Fields //
    private readonly ArchipelagoSession apSession = ArchipelagoSessionFactory.CreateSession(connection.Server, connection.Port);
    private readonly string slot = connection.Slot; // Archipelago server slot name
    private readonly string password = connection.Password; // Archipelago server password
    private readonly List<long> locationsToNotify = []; // Location checks performed while not online

    // Deathlink options //
    private DeathLinkService? DeathLink { get; set; }
    private static string TPKReason { get; set; } = "";
    private int deathLinkCounter = 0;
    private int deathLinkAmount = 1;

    // Message Queue //
    public static ConcurrentQueue<string> MessageQueue { get; } = new();

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
        // Archipelago requires unique keys across all games, so we solve this by defining a base offset for items/locations
        apBaseIDOffset = Convert.ToInt64(slotData["base_offset"]);

        // get the rng seed
        RngSeed = Convert.ToString(slotData["rng_seed"]) ?? RngSeed;

        // get the randomization settings
        UseRandomEncounterOrder = Convert.ToBoolean(slotData["encounter_shuffle"]);
        ShuffleEncounterLoot = Convert.ToBoolean(slotData["loot_shuffle"]);

        // Initialize the character's status
        CharacterStatus.InitializeCampaignHeroes(slotData);

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
        apSession.MessageLog.OnMessageReceived += OnMessage;

        // Initialize the callback
        apSession.Items.ItemReceived += NewItemRecieved;

        // Initialize the data storage if it doesnt already have a value
        apSession.DataStorage[Scope.Slot, "encounters_cleared"].Initialize(0);

        // Load saved progress from the server
        EncountersCleared = apSession.DataStorage[Scope.Slot, "encounters_cleared"];
        CatchUpToOldItems();

        // If we are successfully configured, we are the relevant instance
        Instance = this;
        Ready = true;
    }

    /*
    * Callback to process a message from the server
    */
    private void OnMessage(LogMessage message)
    {
        // Ignore everything but sent/recieved items, as they are really spammy
        if (message is ItemSendLogMessage)
            MessageQueue.Enqueue(message.ToString());
    }

    /*
    * Callback to process an item received event from the server
    */
    private void NewItemRecieved(ReceivedItemsHelper helper)
    {
        GiveArchipelagoItem(helper.PeekItem());
        helper.DequeueItem();
    }

    /*
     * Give an archipelago item to the player
     * Is asynchronous incase GiveItem is blocked so as to not softlock the archipelago client.
     */
    private async void GiveArchipelagoItem(ItemInfo item)
    {
        int id = (int)(item.ItemId - apBaseIDOffset);
        MessageQueue.Enqueue($"Got {item.ItemName} from {item.Player.Name}!");
        await Task.Run(() => CharacterStatus.ApplyArchipelagoItem(id));
    }

    private void CatchUpToOldItems()
    {
        foreach (var item in apSession.Items.AllItemsReceived)
            GiveArchipelagoItem(item);
    }

    /*
     * Tell archipelago that we cleared the next encounter
     */
    public Task SendNextEncounterLocation()
    {
        apSession.DataStorage[Scope.Slot, "encounters_cleared"] = EncountersCleared + 1;
        return SendLocationCheck(EncountersCleared++);
    }

    /*
     * Tell archipelago that we cleared an encounter
     */
    public Task SendLocationCheck(int locationId)
    {
        Task result = Task.CompletedTask; // Default task that does nothing
        locationsToNotify.Add(locationId + apBaseIDOffset);
        if (apSession != null)
        {
            try
            {
                // Save the awaitable as a task for the parent to wait on.
                // Do snapshotting in case the check in takes time, so that we dont accidentally clear the list incorrectly.
                //   Archipeligo will ignore duplicate location updates, so we dont need to worry about that condition.
                result = Task.Run(async () =>
                {
                    var snapshot = locationsToNotify.ToList();
                    await apSession.Locations.CompleteLocationChecksAsync([.. snapshot]);
                    locationsToNotify.RemoveAll(location => snapshot.Contains(location));

                    // After updating the locations, we can check the game's state
                    CheckIfGameBeaten();
                });
            }
            catch (Exception e)
            {
                MessageQueue.Enqueue($"Couldn't Connect to Archipelago: {e.Message}");
            }
        }
        return result;
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
    private void CheckIfGameBeaten()
    {
        // Game is compelete once every encounter is cleared
        if (apSession.Locations.AllMissingLocations.Count == 0)
            apSession.Socket.SendPacket(new StatusUpdatePacket
            {
                Status = ArchipelagoClientState.ClientGoal
            });
    }
}