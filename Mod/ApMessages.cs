using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Dawnsbury.Auxiliary;
using Dawnsbury.Core;
using Dawnsbury.Display.Notifications;
using Microsoft.Xna.Framework;

namespace DawnsburyArchipelago;

/**
 * Class which will handle various message display/logging functions for archipelago
 */
public class ApMessages
{
    // Message Queue //
    private static ConcurrentQueue<(string, Color)> MessageQueue { get; } = new();
    
    /*
    * Callback to process a message from the archipelago server
    */
    public static void OnApMessage(LogMessage message)
    {
        // Ignore items which are sent to us, as the item reciever will log them for us
        if (message is ItemSendLogMessage sMessage && 
                !sMessage.IsReceiverTheActivePlayer && sMessage.IsSenderTheActivePlayer)
            LogEvent(message.ToString(), Color.LightSeaGreen);
        
        else if (message is ChatLogMessage)
            LogEvent(message.ToString(), Color.LightBlue);

        else if (message is HintItemSendLogMessage hMessage && hMessage.IsRelatedToActivePlayer)
            LogEvent(message.ToString(), Color.LightCoral);
    }

    /*
    * Report an error to the user
    */
    public static void LogError(string message)
    {
        MessageQueue.Enqueue((message, Color.Red));
        MakeNewToast(message, Color.Red);  
    }

    /*
    * Report a non-error to the user
    */
    public static void LogEvent(string message, Color? color = null)
    {
        MessageQueue.Enqueue((message, color ?? Color.LightGreen));
        MakeNewToast(message, color ?? Color.LightGreen);
    }

    /*
    * Update the current battle's chat log with our message queue
    */
    public static void UpdateBattleChat(TBattle battle)
    {   
        // Empty all messages into the combat log
        while (MessageQueue.TryDequeue(out var message))
            battle.Log(message.Item1, null, null, null, message.Item2);
    }


    // All Active Toasts we are managing //
    public static readonly List<Toast> ActiveToasts = [];

    // Toast Size, as Defined in Dawnsbury.Display.Notifications.Toasts.cs
    private static readonly Point TOAST_SIZE = new(600, 150);

    // Reflective reference to the private Toast.Rectangle setter
    private static readonly MethodInfo? ToastRectangleSetter = 
        typeof(Toast).GetProperty(nameof(Toast.Rectangle), 
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty)
        ?.GetSetMethod(nonPublic: true);

    /*
    * Draw method to invoke to draw all our toasts
    */
    public static void DrawToasts(float elapsedSeconds)
    {
        // Draw nothing unless we are in an active campaign
        if (DawnsburyArchipelagoLoader.IsArchipelagoCampaignActive())
        {
            // Determine the position of the toasts (by top left corner)
            int y = 0;
            int x = DawnsburyArchipelagoLoader.InApCampaignMenu? 
                Root.ScreenWidth - TOAST_SIZE.X : 0;
            
            // Track the status of toasts as we iterate
            List<Toast> toastsToDeactivate = [];
            foreach(var toast in ActiveToasts)
            {
                // Move the draw rectangle via reflection
                ToastRectangleSetter!.Invoke(toast, [new Rectangle(x, y, TOAST_SIZE.X, TOAST_SIZE.Y)]);

                // Draw the toast at its new location
                toast.Draw(elapsedSeconds);
                
                // Queue up inactive toasts to be removed from the list
                if (toast.Opacity <= 0)
                    toastsToDeactivate.Add(toast);

                // Increase the y coordinate so the next toast is drawn below this one
                y += TOAST_SIZE.Y + 10;

                // Strop drawing if we have too many toasts to display
                if (y + TOAST_SIZE.Y > Root.ScreenHeight * 0.75)
                    break;
            }

            // Remove all toasts which have faded out
            if (toastsToDeactivate.Any(toast => !ActiveToasts.Remove(toast)))
                throw new InvalidOperationException("Failed to remove toast");
        }
    }

    /*
    * Method to make a new toast to appear on the screen
    */
    private static void MakeNewToast(string message, Color color)
    {
        // Create a toast of the given size. Its position will be set in the draw method
        ActiveToasts.Add(new Toast(message, color, Color.Black, new Rectangle(0, 0, TOAST_SIZE.X, TOAST_SIZE.Y)));
    }
}