using Dawnsbury.Audio;
using Dawnsbury.Auxiliary;
using Dawnsbury.Display;
using Dawnsbury.Display.Controls;
using Dawnsbury.Display.Illustrations;
using Dawnsbury.Display.Text;
using Dawnsbury.Phases.Popups;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;
using System.Linq;

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
    private string status = ArchipelagoClient.InstanceReady ? "Connected!" : "Not Connected";

    public ArchipelagoSetupMenu()
        : base(new Rectangle(Root.ScreenWidth / 2 - 700, Root.ScreenHeight / 2 - 450, 1400, 900))
    {
        // Try to pre-load cached connection info as a convenience
        var info = LoadCachedConnectionInfo();
        if (info != null)
        {
            serverTextbox.Text = info.Server;
            portTextbox.Text = $"{info.Port}";
            slotTextbox.Text = info.Slot;
            passwordTextbox.Text = info.Password;
        }
    }

    /*
     * Big draw method to make the archipelago settings menu
     * Note: menu layout designed by ai assistant because I dont have gui editing tools in vscode.
     */
    protected override void Draw(SpriteBatch sb, Game game, float elapsedSeconds)
    {
        base.Draw(sb, game, elapsedSeconds);

        // Window dimensions
        Rectangle windowRect = Window;
        int padding = 20;
        int buttonHeight = 60;
        int fieldHeight = 50;
        int fieldSpacing = 15;
        int statusHeight = 80;
        int buttonWidth = 200;

        // Calculate positions
        int currentY = windowRect.Y + padding;
        int fieldWidth = windowRect.Width - 2 * padding;
        int halfWidth = (fieldWidth - 10) / 2;

        // Title
        Rectangle titleRect = new(windowRect.X + padding, currentY, fieldWidth, 60);
        Writer.DrawString("{b}Archipelago Multiplayer Connection{/b}", titleRect, null,
                        BitmapFontGroup.Mia48Font, Writer.TextAlignment.Middle);
        currentY += 70;

        // Server field
        Writer.DrawString("Server Address:", new Rectangle(windowRect.X + padding, currentY, halfWidth, fieldHeight),
                        Color.Black, BitmapFontGroup.Mia32Font, Writer.TextAlignment.Left);
        serverTextbox.Draw(new Rectangle(windowRect.X + padding + halfWidth, currentY, halfWidth, fieldHeight));
        currentY += fieldHeight + fieldSpacing;

        // Port field
        Writer.DrawString("Port:", new Rectangle(windowRect.X + padding, currentY, halfWidth, fieldHeight),
                        Color.Black, BitmapFontGroup.Mia32Font, Writer.TextAlignment.Left);
        portTextbox.Draw(new Rectangle(windowRect.X + padding + halfWidth, currentY, halfWidth, fieldHeight));
        currentY += fieldHeight + fieldSpacing;

        // Slot name field
        Writer.DrawString("Slot Name:", new Rectangle(windowRect.X + padding, currentY, halfWidth, fieldHeight),
                        Color.Black, BitmapFontGroup.Mia32Font, Writer.TextAlignment.Left);
        slotTextbox.Draw(new Rectangle(windowRect.X + padding + halfWidth, currentY, halfWidth, fieldHeight));
        currentY += fieldHeight + fieldSpacing;

        // Password field
        Writer.DrawString("Password (Optional):", new Rectangle(windowRect.X + padding, currentY, halfWidth, fieldHeight),
                        Color.Black, BitmapFontGroup.Mia32Font, Writer.TextAlignment.Left);
        passwordTextbox.Draw(new Rectangle(windowRect.X + padding + halfWidth, currentY, halfWidth, fieldHeight));
        currentY += fieldHeight + fieldSpacing + 20;

        // Status display
        Rectangle statusRect = new(windowRect.X + padding, currentY, fieldWidth, statusHeight);
        Writer.DrawString(status, statusRect, ConnectionStatusColor(), BitmapFontGroup.Mia32Font, Writer.TextAlignment.Middle);
        currentY += statusHeight + 20;

        // Buttons
        UI.DrawUIButton(new Rectangle(windowRect.X + padding, currentY, buttonWidth, buttonHeight), "Connect",
            ConnectButton, Writer.TextAlignment.Middle);

        UI.DrawUIButton(new Rectangle(windowRect.X + windowRect.Width - padding - buttonWidth, currentY, buttonWidth, buttonHeight),
            "Close", delegate
            {
                Sfxs.Play(SfxName.Button);
                Root.PopFromPhase();
            }, Writer.TextAlignment.Middle);
    }

    /*
     * Event handler for the "Connect" button, parse the input and try to setup archipelago
     */
    private void ConnectButton()
    {
        // Check the port number
        int port = 0;
        try
        {
            port = int.Parse(portTextbox.Text);
        }
        catch (Exception) { }

        if (port <= 0 || port > 65535)
            status = $"Invalid Port: '{portTextbox.Text}'";
        else
            status = ConnectToArchipelago(new(serverTextbox.Text, port, slotTextbox.Text, passwordTextbox.Text));

        // Activate the Archipelago adventure on a successfull connection
        if (ArchipelagoClient.Instance != null)
            DawnsburyArchipelagoLoader.SwapToArchipelagoRandomizedPath();
    }

    /*
     * Try connecting to the archipelago client using provided connection info, returning the connection status
     */
    private static string ConnectToArchipelago(ApConnectionInfo connection)
    {
        ArchipelagoClient ap = new(connection);

        string? error = ap.ConnectArchipelago();
        if (error != null)
            return error;

        SaveConnectionInfo(connection);
        return "Connected";
    }

    /*
     * Short helper method to get the connection status based on if we are connected or not.
      */
    private static Color ConnectionStatusColor()
    {
        // Use blue bc its colorblind friendly, easeir to see, and reasonably archipelago coded.
        return ArchipelagoClient.InstanceReady ? Color.DarkBlue : Color.DarkRed;
    }

    // File path in game's config directory
    private static string ConnectionInfoPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DawnsburyDays",
        "archipelago_connection.cfg");

    
    /*
     * Save successfull connection info into a cache for future use
     */
    private static void SaveConnectionInfo(ApConnectionInfo connection)
    {
        try
        {
            // Create directory if needed
            var dir = Path.GetDirectoryName(ConnectionInfoPath);
            if (!Directory.Exists(dir) && dir != null) // != null is not needed, but it fixes a warning
                Directory.CreateDirectory(dir);

            // Write connection info
            File.WriteAllLines(ConnectionInfoPath, [
                $"server={connection.Server}",
                $"port={connection.Port}",
                $"slot={connection.Slot}",
                $"password={connection.Password}"
            ]);
        }
        catch (Exception) { }
    }

    /*
     * Attempt to load previous successful connection info from the cache file
     */
    private static ApConnectionInfo? LoadCachedConnectionInfo()
    {
        if (File.Exists(ConnectionInfoPath))
            try
            {
                var lines = File.ReadAllLines(ConnectionInfoPath);
                var dict = lines
                    .Where(line => line.Contains('='))
                    .Select(line => line.Split('=', 2))
                    .Where(parts => parts.Length == 2)
                    .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim());


                return new ApConnectionInfo(dict["server"], int.Parse(dict["port"]), dict["slot"], dict["password"]);
            }
            // So many things can throw here, missing fields, parse errors, but we kinda dont care except that we failed to get any info
            catch (Exception) { }
        return null;
    }

    /*
     * Static method to create the "Archipelago" button on the main menu to be patched in via harmony.
     */
    public static bool TryConnectingToArchipelagoUsingCache()
    {
        var connection = LoadCachedConnectionInfo();
        if (connection != null)
            ConnectToArchipelago(connection);
        return ArchipelagoClient.Instance != null;
    }

    /*
     * Static method to create the "Archipelago" button on the main menu to be patched in via harmony.
     */
    public static void DrawArchipelagoButton()
    {
        UI.DrawUIButton(new Rectangle(Root.ScreenWidth - 500, Root.ScreenHeight - 500, 400, 90), delegate (Rectangle r)
        {
            Primitives.DrawImage(new ModdedIllustration("archipelago_logo.png"), new Rectangle(r.X + 10 + 20, r.Y + 20, r.Height - 40, r.Height - 40), null, scale: true);
            Rectangle rectangle = new(r.X + r.Height + 30, r.Y, r.Width - r.Height - 30, r.Height);
            BitmapFontGroup mia48Font = BitmapFontGroup.Mia48Font;
            Writer.DrawString("Archipelago", rectangle, ConnectionStatusColor(), mia48Font, Writer.TextAlignment.Left);
        }, delegate
        {
            Sfxs.Play(SfxName.Button);
            Root.PushPhase(new ArchipelagoSetupMenu());
        }, enabled: true, menuTooltip: "Configure the Archipelago Randomizer Settings");
    }
}
