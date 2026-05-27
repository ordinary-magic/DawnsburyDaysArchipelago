using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawnsbury.Auxiliary;
using Dawnsbury.Core.CombatActions;
using Dawnsbury.Core.Creatures;
using Dawnsbury.Core.Creatures.Parts;
using Dawnsbury.Core.Mechanics;
using Dawnsbury.Core.Mechanics.Core;
using Dawnsbury.Core.Mechanics.Enumerations;
using Dawnsbury.Core.Mechanics.Treasure;
using Dawnsbury.Display.Illustrations;
using Microsoft.Xna.Framework.Graphics;

namespace DawnsburyArchipelago;

/**
 * Class to define a character's current progressive bonuses.
 */
public class CharacterStatus(int level, int weaponPotency, int strikingRunes, int armorPotency, int resilientRunes, int skillBonus)
{
    public int Level { get; set; } = level;
    public int WeaponPotency { get; set; } = weaponPotency;
    public int Striking { get; set; } = strikingRunes;
    public int ArmorPotency { get; set; } = armorPotency;
    public int  Resilient { get; set; } =  resilientRunes;
    public int  SkillBonus { get; set; } =  skillBonus;

    // Static tracker for the status of the campaign heroes, using creature id's as keys
    public static ConcurrentDictionary<CreatureId, CharacterStatus> Heroes { get; } = [];

    // The default creature ID's of the party in dawnsbury days, in slot order
    public static readonly CreatureId[] CampaignHeroes = [CreatureId.Annacoesta, CreatureId.Scarlet, CreatureId.Tokdar, CreatureId.Saffi];

    /**
    * Progressivley improve the status of the weapon
    */
    public void IncrementProgressiveWeaponBonuses(bool canIssuePermanant)
    {
        // Weapon improvements always alternate between +1 and striking in game, so match that here
        if (WeaponPotency == Striking)
        {
            WeaponPotency++;
            if (canIssuePermanant) Loot.DropFundamentalRunestone(WeaponPotency, true, true);
        }
        else
        {
            Striking++;
            if (canIssuePermanant) Loot.DropFundamentalRunestone(Striking, false, true);
        }
    }

    
    /**
    * Progressivley improve the status of the armor
    */
    public void IncrementProgressiveArmorBonuses(bool canIssuePermanant)
    {
        // Armor improvements always alternate between +1 and resilient in game, so match that here
        if (ArmorPotency == Resilient)
        {
            ArmorPotency++;
            if (canIssuePermanant) Loot.DropFundamentalRunestone(ArmorPotency, true, false);
        }
        else
        {
            Resilient++;
            if (canIssuePermanant) Loot.DropFundamentalRunestone(Resilient, false, false);   
        }
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
    * Get this creature's armor bonuses (for qEffect.BonusToDefenses)
    */
    public Func<QEffect, CombatAction?, Defense, Bonus?> GetDefenseBonus()
    {
        return (qfSelf, combatAction, defense) =>
            {
                if (defense == Defense.AC && ArmorPotency > 0)
                {
                    // If we give an item bonus, it will be eaten by the armor's base bonus
                    //  Therefore, apply it as an untyped bonus instead, minus any existing rune bonus
                    int existing_item_bonus = qfSelf.Owner.Armor.Item?.ArmorProperties?.ItemBonus ?? 0;
                    if (ArmorPotency > existing_item_bonus)
                        return new Bonus(ArmorPotency - existing_item_bonus, BonusType.Untyped, "Archipelago");
                }
                
                else if (defense.IsSavingThrow() && Resilient > 0)
                    return new Bonus(Resilient, BonusType.Item, "Archipelago");

                return null;
            };
    }

    /**
    * Get this creature's skill bonuses (for qEffect.BonusToSkills)
    */
    public Func<Skill, Bonus?> GetSkillBonus()
    {
        return (skill) =>
            {
                if (SkillBonus > 0)
                    return new Bonus(SkillBonus, BonusType.Item, "Archipelago");
                return null;
            };
    }
    
    /**
    * Get this creature's perception bonuses (for qEffect.BonusToPerception)
    * Note: this is affected by the same types of items as skills, so its included in the same bonus
    */
    public Func<QEffect, Bonus?> GetPerceptionBonus()
    {
        return (qfSelf) =>
            {
                if (SkillBonus > 0)
                    return new Bonus(SkillBonus, BonusType.Item, "Archipelago");
                return null;
            };
    }

    /**
     * Apply archipelago item improvements to the characters
     *  returns true/false if the item is a permanent item drop
     */
    public static bool ApplyArchipelagoItem(int itemId, bool canIssuePermanant)
    {
        // Items are stored as pc1-level, pc2-level, pc3-level, pc4-level, pc1-weapon etc.
        //  Thus, we can divide by four, and use the remainder as a pc index and the quotent as an item index. 
        var type = (ArchipelagoClient.ApItemTypes) (itemId / 4);
        var affectedPc = Heroes[CampaignHeroes[itemId % 4]];
        lock (affectedPc)
        {
            switch (type)
            {
                case ArchipelagoClient.ApItemTypes.LevelUp:
                    affectedPc.Level++;
                    break;
                case ArchipelagoClient.ApItemTypes.WeaponImprovement:
                    affectedPc.IncrementProgressiveWeaponBonuses(canIssuePermanant);
                    break;
                case ArchipelagoClient.ApItemTypes.ArmorImprovement:
                    affectedPc.IncrementProgressiveArmorBonuses(canIssuePermanant);
                    break;
                case ArchipelagoClient.ApItemTypes.SkillImprovement:
                    affectedPc.SkillBonus++;
                    break;
            }
        }
        return IsItemTypeSavedInInventory(type);
    }

    /**
    * Helper method to identify if a provided item type would be saved in the character's inventory
    */
    public static bool IsItemTypeSavedInInventory(ArchipelagoClient.ApItemTypes type)
    {
        return ArchipelagoClient.InstancePotencyRunes && (
            type == ArchipelagoClient.ApItemTypes.ArmorImprovement ||
            type == ArchipelagoClient.ApItemTypes.WeaponImprovement
        );
    }

    /**
    * Helper method to Initialize all the Campaign Heroes quickly
    */
    public static void InitializeCampaignHeroes(int level, int weaponPotency, int strikingRunes, int armorPotency, int resilientRunes, int skillBonus)
    {
        foreach (var id in CampaignHeroes)
            Heroes[id] = new CharacterStatus(level, weaponPotency, strikingRunes, armorPotency, resilientRunes, skillBonus);
    }

    /**
    * Helper method to Initialize all the Campaign Heroes based on the provided archipelago metadata
    */
    public static void InitializeCampaignHeroes(Dictionary<string, object> slotData)
    {
        int start_level = Convert.ToInt32(slotData["start_level"]);
        int atk_bonus = Convert.ToInt32(slotData["start_atk_bonus"]);
        int armor_bonus = Convert.ToInt32(slotData["start_armor_bonus"]);
        int skill_bonus = Convert.ToInt32(slotData["start_skill_bonus"]);
        InitializeCampaignHeroes(start_level, 
            // Potency/Striking bonuses = Total bonus / 2, then round up or down respectivley
            (atk_bonus + 1) / 2, atk_bonus / 2,
            (armor_bonus + 1) / 2, armor_bonus / 2,
            skill_bonus);
    }

    /**
    * Helper method to Initialize all the Campaign Heroes with different placeholder values
    */
    public static void InitializeCampaignHeroesAsMock()
    {
        foreach (var i in Enumerable.Range(0, CampaignHeroes.Length))
            Heroes[CampaignHeroes[i]] = new CharacterStatus(1 + i, i / 2, i % 2, i / 2, i % 2, i % 2);
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
                    // Add the automatic item bonuses if we are in that configuration
                    if (!ArchipelagoClient.InstancePotencyRunes)
                    {
                        qfSelf.BonusToAttackRolls = Heroes[owner.CreatureId].GetAttackBonus();
                        qfSelf.IncreaseItemDamageDieCount = Heroes[owner.CreatureId].GetAttackDiceBonus();
                        qfSelf.BonusToDefenses = Heroes[owner.CreatureId].GetDefenseBonus();

                        // Upgrade any shields the user is holding
                        Loot.UpgradeShields(qfSelf.Owner, Heroes[owner.CreatureId].ArmorPotency);
                    }

                    qfSelf.BonusToSkills = Heroes[owner.CreatureId].GetSkillBonus();
                    qfSelf.BonusToPerception = Heroes[owner.CreatureId].GetPerceptionBonus();
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