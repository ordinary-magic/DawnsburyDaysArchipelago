using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawnsbury.Core.CombatActions;
using Dawnsbury.Core.Creatures;
using Dawnsbury.Core.Creatures.Parts;
using Dawnsbury.Core.Mechanics;
using Dawnsbury.Core.Mechanics.Core;
using Dawnsbury.Core.Mechanics.Enumerations;
using Dawnsbury.Core.Mechanics.Treasure;
using Dawnsbury.Display.Illustrations;

namespace DawnsburyArchipelago;

/**
 * Class to define a character's current progressive bonuses.
 * Note: Currently only supports DD campaign bonuses (effects from items level 1 to 4)
 */
public class CharacterStatus(int level, int weaponPotency, int strikingRunes)
{
    public int Level { get; set; } = level;
    public int WeaponPotency { get; set; } = weaponPotency;
    public int Striking { get; set; } = strikingRunes;

    // Static tracker for the status of the campaign heroes, using creature id's as keys
    public static ConcurrentDictionary<CreatureId, CharacterStatus> Heroes { get; } = [];

    // The default creature ID's of the party in dawnsbury days
    public static readonly CreatureId[] CampaignHeroes = [CreatureId.Annacoesta, CreatureId.Scarlet, CreatureId.Tokdar, CreatureId.Saffi];

    /**
    * Progressivley improve the status of the weapon
    */
    public void IncrementProgressiveWeaponBonuses()
    {
        // Weapon improvements always alternate between +1 and striking in game, so match that here
        if (WeaponPotency == Striking)
            WeaponPotency++;
        else
            Striking++;
    }

    /**
    * Get this creature's attack roll bonus (for qEffect.BonusToAttackRolls)
    */
    public Func<QEffect, CombatAction, Creature?, Bonus?> GetAttackBonus()
    {
        return (qfSelf, combatAction, defender) =>
            {
                if (WeaponPotency > 0)
                    if (combatAction.HasTrait(Trait.Attack) && combatAction.Item != null && (combatAction.Item.HasTrait(Trait.Weapon) || combatAction.Item.HasTrait(Trait.Unarmed) || combatAction.HasTrait(Trait.Impulse)))
                    {
                        return new Bonus(WeaponPotency, BonusType.Item, "Archipelago");
                    }

                return null;
            };
    }

    /**
     * Get this creature's weapon damage dice bonus (for qEffect.IncreaseItemDamageDieCount)
     */
    public Func<QEffect, Item, bool> GetAttackDiceBonus()
    {
        return (qfSelf, item) =>
            {
                // This seems to only be able to increase it one step, so it'll probably change if the game gets updated more
                return (item.WeaponProperties?.DamageDieCount - 1) < Striking;
            };
    }

    /**
     * Apply archipelago item improvements to the characters
     */
    public static void ApplyArchipelagoItem(int itemId)
    {
        // Items are stored as pc1-level, pc2-level, pc3-level, pc4-level, pc1-weapon etc.
        //  Thus, we can divide by four, and use the remainder as a pc index and the quotent as an item index. 
        var affectedPc = Heroes[CampaignHeroes[itemId % 4]];
        lock (affectedPc)
        {
            switch ((ArchipelagoClient.ApItemTypes)(itemId / 4))
            {
                case ArchipelagoClient.ApItemTypes.LevelUp:
                    affectedPc.Level++;
                    break;
                case ArchipelagoClient.ApItemTypes.WeaponImprovement:
                    affectedPc.IncrementProgressiveWeaponBonuses();
                    break;
            }
        }
    }

    /**
    * Helper method to Initialize all the Campaign Heroes quickly
    */
    public static void InitializeCampaignHeroes(int level, int weaponPotency, int strikingRunes)
    {
        foreach (var id in CampaignHeroes)
            Heroes[id] = new CharacterStatus(level, weaponPotency, strikingRunes);
    }

    /**
    * Helper method to Initialize all the Campaign Heroes based on the provided archipelago metadata
    */
    public static void InitializeCampaignHeroes(Dictionary<string, object> slotData)
    {
        int start_level = Convert.ToInt32(slotData["start_level"]);
        int atk_bonus = Convert.ToInt32(slotData["start_atk_bonus"]);
        InitializeCampaignHeroes(start_level, (atk_bonus + 1) / 2, atk_bonus / 2);
    }

    /**
    * Helper method to Initialize all the Campaign Heroes with different placeholder values
    */
    public static void InitializeCampaignHeroesAsMock()
    {
        foreach (var i in Enumerable.Range(0, CampaignHeroes.Length))
            Heroes[CampaignHeroes[i]] = new CharacterStatus(1 + i, i / 2, i % 2);
    }

    /**
    * Helper method to create a QEffect to adjust bonuses at the start of the character's turn.
    */
    public static QEffect GetProgressAdjustmentQEffect()
    {
        return new QEffect("Archipelago", "Your stats are being adjusted as a result of your Archipelago's progress!",
            ExpirationCondition.Never, null, new ModdedIllustration("archipelago_logo.png"))
        {
            // Apply the adjustement at the start of turn to keep it as up to date as possible
            StartOfYourEveryTurn = (qfSelf, owner) =>
            {
                // Some friendly npc's seem to have persistent sheets, but we only want to do this for pcs
                if (CampaignHeroes.Contains(owner.CreatureId))
                {
                    qfSelf.BonusToAttackRolls = Heroes[owner.CreatureId].GetAttackBonus();
                    qfSelf.IncreaseItemDamageDieCount = Heroes[owner.CreatureId].GetAttackDiceBonus();
                }
                else // Debug
                    qfSelf.Owner.Battle.Log($"Non-Hero progress adjustment: {owner.CreatureId}");

                return Task.CompletedTask;
            },
        };
    }

    /**
    * Get the current level of the campaign hero who corresponds to the provided index.
    * If archipelago is not enabled, will just return the input value instead.
    * This is called by our patched version of the SpawnHero method during combat setup
    */
    public static int GetLevelForHeroIndex(int original, int index)
    {
        if (index > 3 || index < 0) // Debug (checking arguemnt order)
            throw new InvalidOperationException($"Bad Arguments to GetLevelForHeroIndex index={index} level={original}");

        if (DawnsburyArchipelagoLoader.IsArchipelagoCampaignActive())
            return Heroes[CampaignHeroes[index]].Level;
        else
            return original;
    }
}