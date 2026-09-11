using Dawnsbury.Audio;
using Dawnsbury.Auxiliary;
using Dawnsbury.Display;
using Dawnsbury.Display.Controls;
using Dawnsbury.Display.Illustrations;
using Dawnsbury.Display.Text;
using Dawnsbury.IO;
using Dawnsbury.Modding;
using Dawnsbury.Phases.Popups;
using DawnsburyArchipelago.Data;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DawnsburyArchipelago;

/*
 * Archipelago configuration submenu which we will insert into the game's main menu screen. 
 * TODO (eventually): add a disconnect button
 */
public class ArchipelagoSetupMenu : WindowPhase
{
    private readonly Textbox serverTextbox = new() { PlaceholderText = "archipelago.gg" };
    private readonly Textbox portTextbox = new() { PlaceholderText = "38281" };
    private readonly Textbox slotTextbox = new() { PlaceholderText = "Annacoesta" };
    private readonly Textbox passwordTextbox = new() { PlaceholderText = "" };
    private static string Status => ArchipelagoClient.InstanceReady? "Connected!" : "Not Connected";
    private string error = "";
    private bool isMouseInRectangle = false;
    private static bool TriedLoadingCache = false;

    public ArchipelagoSetupMenu()
        : base(new Rectangle(Root.ScreenWidth / 2 - 450, Root.ScreenHeight / 2 - 255, 900, 510))
    {
        InitializeMenuFromCache(serverTextbox, portTextbox, slotTextbox, passwordTextbox);
    }

    /*
     * Big draw method to make the archipelago settings menu
     */
    protected override void Draw(SpriteBatch sb, Game game, float elapsedSeconds)
    {
        base.Draw(sb, game, elapsedSeconds);

        // Window dimensions
        int padding = 20;
        int labelWidth = 250;
        int rowHeight = 50;
        int buttonHeight = 60;
        int buttonWidth = 200;

        // Check if the mouse is inside the window
        isMouseInRectangle = Root.IsMouseOver(Window);

        // Calculate positions
        int currentY = Window.Y + padding;
        int paddedWidth = Window.Width - (2 * padding);
        int textBoxWidth = paddedWidth - labelWidth - padding;

        // Title
        Rectangle titleRect = new(Window.X + padding, currentY, paddedWidth, rowHeight);
        Writer.DrawString("{b}Archipelago Connection{/b}", titleRect, null,
                        BitmapFontGroup.Mia48Font, Writer.TextAlignment.Middle);
        currentY += rowHeight + padding;

        // Server field
        Writer.DrawString("Server Address:", new Rectangle(Window.X + padding, currentY, labelWidth, rowHeight),
                        Color.Black, BitmapFontGroup.Mia32Font, Writer.TextAlignment.Left);
        serverTextbox.Draw(new Rectangle(Window.X + padding + labelWidth, currentY, textBoxWidth, rowHeight));
        currentY += rowHeight + padding;

        // Port field
        Writer.DrawString("Port:", new Rectangle(Window.X + padding, currentY, labelWidth, rowHeight),
                        Color.Black, BitmapFontGroup.Mia32Font, Writer.TextAlignment.Left);
        portTextbox.Draw(new Rectangle(Window.X + padding + labelWidth, currentY, textBoxWidth, rowHeight));
        currentY += rowHeight + padding;

        // Slot name field
        Writer.DrawString("Slot Name:", new Rectangle(Window.X + padding, currentY, labelWidth, rowHeight),
                        Color.Black, BitmapFontGroup.Mia32Font, Writer.TextAlignment.Left);
        slotTextbox.Draw(new Rectangle(Window.X + padding + labelWidth, currentY, textBoxWidth, rowHeight));
        currentY += rowHeight + padding;

        // Password field
        Writer.DrawString("Password:", new Rectangle(Window.X + padding, currentY, labelWidth, rowHeight),
                        Color.Black, BitmapFontGroup.Mia32Font, Writer.TextAlignment.Left);
        passwordTextbox.Draw(new Rectangle(Window.X + padding + labelWidth, currentY, textBoxWidth, rowHeight));
        currentY += rowHeight + padding;

        // Error Status Display
        Rectangle errorRect = new(Window.X + padding, currentY, Window.Width - (2 * padding), rowHeight);
        Writer.DrawString(error, errorRect, Color.DarkRed, BitmapFontGroup.Mia32Font, Writer.TextAlignment.Middle);
        currentY += rowHeight;

        // Connect Button - Todo: Capture Enter Button
        UI.DrawUIButton(new Rectangle(Window.X + padding, currentY, buttonWidth, buttonHeight), "Connect",
            ConnectButton, Writer.TextAlignment.Middle);

        // Status Display - Conneted/Disconnected
        var statusLeftAnchor = Window.X + (Window.Width / 2) - (textBoxWidth / 2);
        Rectangle statusRect = new(statusLeftAnchor, currentY, textBoxWidth, buttonHeight);
        Writer.DrawString(Status, statusRect, ConnectionStatusColor(), BitmapFontGroup.Mia32Font, Writer.TextAlignment.Middle);

        // Close Button - Todo: Capture ESC or clicks outside the window also?
        UI.DrawUIButton(new Rectangle(Window.X + Window.Width - padding - buttonWidth, currentY, buttonWidth, buttonHeight),
            "Close", CloseButton, Writer.TextAlignment.Middle);
    }

    /*
     * The component's update method, called every frame. We use it to handle misc inputs.
     */
    protected override void Update(Game game, float elapsedSeconds)
    {
        // Update the other components too
        base.Update(game, elapsedSeconds);

        // If Escape key is pressed, close this window
        if (Root.WasKeyPressed(Keys.Escape))
            CloseButton();

        // If the user clicks out of the window, close it
        else if (Root.WasMouseLeftClick && !isMouseInRectangle)
            CloseButton();

        // If enter is pressed, connect.
        else if (Root.WasKeyPressed(Keys.Enter))
            ConnectButton();
    }

    /*
     * Event handler for the "Close" button, which closes the menu.
     */
    private void CloseButton()
    {
        Sfxs.Play(SfxName.Button);
        Root.PopFromPhase();
    }

    /*
     * Event handler for the "Connect" button, parse the input and try to setup archipelago
     */
    private void ConnectButton()
    {
        // Play the button sfx
        Sfxs.Play(SfxName.Button);

        // Pull values from the textboxes, falling back to placeholder values if the input is empty and it has one
        var serverText = (serverTextbox.Text == "")? serverTextbox.PlaceholderText ?? "" : serverTextbox.Text;
        var portText = (portTextbox.Text == "")? portTextbox.PlaceholderText ?? "" : portTextbox.Text;
        var slotText = (slotTextbox.Text == "")? slotTextbox.PlaceholderText ?? "" : slotTextbox.Text;

        // Check the port number
        int port = 0;
        try
        {
            port = int.Parse(portText);
        }
        catch (Exception) { }

        if (port <= 0 || port > 65535)
            error = $"Invalid Port: '{portTextbox.Text}'";
        else
            error = ConnectToArchipelago(new(serverText, port, slotText, passwordTextbox.Text));

        // Activate the Archipelago adventure on a successful connection
        if (ArchipelagoClient.Instance != null)
        {
            DawnsburyArchipelagoLoader.SwapToArchipelagoRandomizedPath();
            CloseButton(); // Finally, close the connection window.
        }
    }

    /*
     * Try connecting to the archipelago client using provided connection info, returning the connection status
     */
    private static string ConnectToArchipelago(ApConnectionInfo connection)
    {
        try
        {
            // Try connecting to the archipelago
            ArchipelagoClient ap = new(connection);
            string? error = ap.ConnectArchipelago();
            if (error != null)
                return error;
        }
        catch (Exception e)
        {
            // Catch thrown errors (should only be parse expeptions in the constructor)
            ApMessages.LogError(e.Message);
            return e.Message;
        }

        SaveConnectionInfo(connection);
        return "";
    }

    /*
     * Short helper method to get the connection status based on if we are connected or not.
      */
    private static Color ConnectionStatusColor()
    {
        // Use blue bc its colorblind friendly, easeir to see, and reasonably archipelago coded.
        return ArchipelagoClient.InstanceReady ? Color.DarkBlue : Color.DarkRed;
    }

    // Path to the cache file, in %Appdata%/Dawnsbury
    private static string ConnectionCacheFilePath =>
        Path.Combine(LocalDataStore.StorageFolder, "archipelago_connection.cfg");

    /*
     * Save successfull connection info into a cache for future use
     */
    private static void SaveConnectionInfo(ApConnectionInfo connection)
    {
        try
        {
            // Create directory if needed
            var dir = Path.GetDirectoryName(ConnectionCacheFilePath);
            if (!Directory.Exists(dir) && dir != null) // != null is not needed, but it fixes a warning
                Directory.CreateDirectory(dir);

            // Write connection info
            File.WriteAllLines(ConnectionCacheFilePath, [
                $"server={connection.Server}",
                $"port={connection.Port}",
                $"slot={connection.Slot}",
                $"password={connection.Password}"
            ]);
        }
        catch (Exception) { }
    }

    /*
     * Attempt to read the cache file as a dictionary
     */
    private static Dictionary<string, string> TryToReadCacheFile()
    {
        if (File.Exists(ConnectionCacheFilePath))
            try
            {
                var lines = File.ReadAllLines(ConnectionCacheFilePath);
                var dict = lines
                    .Where(line => line.Contains('='))
                    .Select(line => line.Split('=', 2))
                    .Where(parts => parts.Length == 2)
                    .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim());

                return dict;
            }
            // So many things can throw here, missing fields, parse errors, but we kinda dont care except that we failed to get any info
            catch (Exception) { }
        return [];
    }

    /*
     * Attempt to load previous successful connection info from the cache file
     */
    private static ApConnectionInfo? GetCachedConnectionInfo()
    {
        var dict = TryToReadCacheFile();
        try
        {
            return new ApConnectionInfo(dict["server"], int.Parse(dict["port"]), dict["slot"], dict["password"]);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    private static void InitializeMenuFromCache(Textbox server, Textbox port, Textbox slot, Textbox password)
    {
        // Try to pre-load cached connection info as a convenience
        var cache = TryToReadCacheFile();

        // Must explicitly set "" on cache miss, as setting text to null will throw an exception
        server.Text = cache.GetValueSafe("server") ?? "";
        port.Text = cache.GetValueSafe("port") ?? "";
        slot.Text = cache.GetValueSafe("slot") ?? "";
        password.Text = cache.GetValueSafe("password") ?? "";
    }

    /*
     * Try to connect to archipelago with our cached connection info.
     */
    public static bool TryConnectingToArchipelagoUsingCache()
    {
        var connection = GetCachedConnectionInfo();
        if (connection != null)
            ConnectToArchipelago(connection);
        return ArchipelagoClient.Instance != null;
    }

    /*
     * Static method to draw the "Archipelago" button on the main menu.
     */
    public static void DrawArchipelagoButton(Rectangle at)
    {
        // The first time this menu loads, try to setup the archipelago connection from the cache.
        if (!TriedLoadingCache)
            Task.Run(() =>
            {
                try
                {
                    if (TryConnectingToArchipelagoUsingCache())
                        DawnsburyArchipelagoLoader.SwapToArchipelagoRandomizedPath();
                } catch (Exception e)
                {
                    using var logfile = new StreamWriter("errordump.txt");
                    logfile.Write(e.Message + "\n" + e.StackTrace);
                }
            });
        TriedLoadingCache = true;

        // Then, draw the button as normal
        Rectangle logoRect = new(at.X + 10 + 20, at.Y + 20, at.Height - 40, at.Height - 40);
        Primitives.DrawImage(new ModdedIllustration("archipelago_logo.png"), logoRect, null, scale: true);

        Rectangle textRect = new(at.X + at.Height + 30, at.Y, at.Width - at.Height - 30, at.Height);
        Writer.DrawString("Archipelago", textRect, ConnectionStatusColor(), BitmapFontGroup.Mia48Font, Writer.TextAlignment.Left);
    }

    /*
     * Method to call when the main menu "archipelago" button is clicked.
     */
    public static void DoArchipelagoButtonAction()
    {
        Sfxs.Play(SfxName.Button);
        Root.PushPhase(new ArchipelagoSetupMenu());
    }

    // Tooltip for the main menu button
    public static string ARCHIPELAGO_BUTTON_TOOLTIP
        => "Configure the Archipelago Randomizer Settings";

    /*
     * Static method to setup the archipelago button with the mod manager
     */
    public static void RegisterArchipelagoButtonInModManager() 
        => ModManager.Frontend.RegisterMainMenuRightSideButton(
            DrawArchipelagoButton, DoArchipelagoButtonAction, ARCHIPELAGO_BUTTON_TOOLTIP);
}
