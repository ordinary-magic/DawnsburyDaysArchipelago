namespace DawnsburyArchipelago.Data;

/// <summary>
/// Enum detailing the campaign selection options for archipelago
/// </summary>
public enum ApCampaignChoice
{
    // Game Campaigns //
    DawnsburyDays,
    TheProfaneBarrier,
    DDandPB,
    GoodLittleChildren,
    All3,

    // Special Campaigns //
    Roguelike = 100,
}

public static class ApCampaignChoiceExtension
{
    /// <summary>
    /// Is this campaign choice an official one from the game or its dlc's (or else a combination of them)?
    /// </summary>
    /// <param name="choice">The campaign.</param>
    /// <returns>True if it is an official campaign (or combination of them), false if its a mod.</returns>
    public static bool IsGameCampaign(this ApCampaignChoice choice) => choice < ApCampaignChoice.Roguelike;
}