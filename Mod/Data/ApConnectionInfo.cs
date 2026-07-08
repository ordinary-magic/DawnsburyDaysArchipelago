namespace DawnsburyArchipelago.Data;

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
