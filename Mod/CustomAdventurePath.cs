using System;
using System.Collections.Generic;
using Dawnsbury.Campaign.Path;
using Dawnsbury.Campaign.Path.CampaignStops;
using Dawnsbury.Core.CharacterBuilder;
using Dawnsbury.Core.CharacterBuilder.Selections.Selected;
using Dawnsbury.Phases.Menus.StoryMode;

namespace DawnsburyArchipelago;

/// <summary>
/// Class defining an adventure path for a customized campaign.
/// Will likely be obsoleted at some point, but its a useful shim.
/// </summary>
public class CustomAdventurePath(
        string id, string name, string description, int startingLevel, int startingShopLevel, List<CampaignStop> campaignStops):
    AdventurePath(id, name, description, startingLevel, startingShopLevel, campaignStops)
{
    // Generator so we make them after everything loads
    public Func<List<CharacterSheet>?> CustomHeroes { get; set; } = () => null; 

    public static void NewCampaignStatePrefix(ref List<CharacterSheet> heroes, ChooseStartPhase __instance)
    {
        // If we are a customized path, overwrite the thingy.
        if (__instance.AdventurePath is CustomAdventurePath campaign)

            // If the custom list exists
            if (campaign.CustomHeroes() is List<CharacterSheet> custom)
            {
                // Replace the input heroes
                heroes = custom;

                // Remake the identities, since we cant set AdventurePath.EnforcesCharacterNames
                for (int i = 0; i < 4; i++)
                {
                    CampaignTraits campaignTraits = CampaignTraits.CreateFromSlot(i);
                    heroes[i].CampaignTraits = campaignTraits;
                    heroes[i].SelectedFeats["Root:Identity"] = new IdentitySelectedChoice
                    {
                        Alignment = campaignTraits.RecommendedAlignment,
                        Illustration = campaignTraits.DefaultIllustration,
                        Name = campaignTraits.Name,
                        VoiceGender = campaignTraits.Gender
                    };
                }
            }
    }
}