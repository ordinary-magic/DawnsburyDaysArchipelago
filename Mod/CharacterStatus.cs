using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dawnsbury.Audio;
using Dawnsbury.Auxiliary;
using Dawnsbury.Core;
using Dawnsbury.Core.Animations;
using Dawnsbury.Core.CharacterBuilder.FeatsDb.Common;
using Dawnsbury.Core.CharacterBuilder.FeatsDb.Kineticist.ImpulsesDb;
using Dawnsbury.Core.CharacterBuilder.FeatsDb.Spellbook;
using Dawnsbury.Core.CharacterBuilder.FeatsDb.TrueFeatDb;
using Dawnsbury.Core.CharacterBuilder.FeatsDb.TrueFeatDb.Specific;
using Dawnsbury.Core.CharacterBuilder.Spellcasting;
using Dawnsbury.Core.CombatActions;
using Dawnsbury.Core.Creatures;
using Dawnsbury.Core.Creatures.Parts;
using Dawnsbury.Core.Mechanics;
using Dawnsbury.Core.Mechanics.Core;
using Dawnsbury.Core.Mechanics.Damage;
using Dawnsbury.Core.Mechanics.Enumerations;
using Dawnsbury.Core.Mechanics.Targeting;
using Dawnsbury.Core.Mechanics.Targeting.Targets;
using Dawnsbury.Core.Mechanics.Treasure;
using Dawnsbury.Core.Possibilities;
using Dawnsbury.Core.Roller;
using Dawnsbury.Display.Controls.Portraits;
using Dawnsbury.Display.Illustrations;
using DawnsburyArchipelago.Data;

namespace DawnsburyArchipelago;

/**
 * Class to define a character's current progressive bonuses.
 */
public class CharacterStatus(int level, int weaponPotency, int strikingRunes, int armorPotency, int resilientRunes, int skillBonus, int perceptionBonus,
    bool canStrike = true, bool canSpell = true, bool canStride = true, bool canInteract = true)
{
    public int Level { get; set; } = level;
    public int WeaponPotency { get; set; } = weaponPotency;
    public int Striking { get; set; } = strikingRunes;
    public int ArmorPotency { get; set; } = armorPotency;
    public int  Resilient { get; set; } =  resilientRunes;
    public int  SkillBonus { get; set; } =  skillBonus;
    public int  PerceptionBonus { get; set; } =  perceptionBonus;
    public bool CanStrike { get; set; } = canStrike;
    public bool CanCastSpells { get; set; } = canSpell;
    public bool CanStride { get; set; } = canStride;
    public bool CanInteract { get; set; } = canInteract;

    // Static tracker for the status of the campaign heroes, using creature id's as keys
    public static ConcurrentDictionary<CreatureId, CharacterStatus> Heroes { get; } = [];

    // The default creature ID's of the party in dawnsbury days, in slot order
    public static readonly CreatureId[] CampaignHeroes = [CreatureId.Annacoesta, CreatureId.Scarlet, CreatureId.Tokdar, CreatureId.Saffi];
    public static int PartyMaxLevel => Heroes.Max(hero => hero.Value.Level);
    public static int PartyMinLevel => Heroes.Min(hero => hero.Value.Level);

    // The queue of traps and boons to "award" to the player
    public static readonly ConcurrentQueue<ApTrapItemTypes> PendingTraps = [];
    public static readonly ConcurrentQueue<ApBoonItemTypes> PendingBoons = [];

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
    public static Func<QEffect, CombatAction, Creature?, Bonus?> GetAttackBonus()
    {
        return (qfSelf, combatAction, defender) =>
            {
                int bonus = TryGetCharacterStatus(qfSelf.Owner)?.WeaponPotency ?? 0;
                if (bonus > 0)
                    if (combatAction.HasTrait(Trait.Attack) && combatAction.Item != null && 
                        (combatAction.Item.HasTrait(Trait.Weapon) || combatAction.Item.HasTrait(Trait.Unarmed) || combatAction.HasTrait(Trait.Impulse)))
                    {
                        return new Bonus(bonus, BonusType.Item, "Archipelago", true);
                    }

                return null;
            };
    }

    /**
     * Get this creature's weapon damage dice bonus (for qEffect.IncreaseItemDamageDieCount)
     */
    public static Func<QEffect, Item, bool> GetAttackDiceBonus()
    {
        return (qfSelf, item) =>
            {
                int bonus = TryGetCharacterStatus(qfSelf.Owner)?.Striking ?? 0;
                // This seems to only be able to increase it one step, so it'll probably change if the game gets updated more
                return (item.WeaponProperties?.DamageDieCount - 1) < bonus;
            };
    }
    
    /**
    * Get this creature's armor bonuses (for qEffect.BonusToDefenses)
    */
    public static Func<QEffect, CombatAction?, Defense, Bonus?> GetDefenseBonus()
    {
        return (qfSelf, combatAction, defense) =>
            {
                var status = TryGetCharacterStatus(qfSelf.Owner);
                if (defense == Defense.AC && status is not null && status.ArmorPotency > 0)
                {
                    // If we give an item bonus, it will be eaten by the armor's base bonus
                    //  Therefore, apply it as an untyped bonus instead, minus any existing rune bonus
                    int existing_item_bonus = qfSelf.Owner.Armor.Item?.ArmorProperties?.ItemBonus ?? 0;
                    if (status.ArmorPotency > existing_item_bonus)
                        return new Bonus(status.ArmorPotency - existing_item_bonus, BonusType.Untyped, "Archipelago", true);
                }
                
                else if (defense.IsSavingThrow() && status is not null && status.Resilient > 0)
                    return new Bonus(status.Resilient, BonusType.Item, "Archipelago", true);

                return null;
            };
    }

    /**
    * Get this creature's skill bonuses (for qEffect.BonusToSkills)
    *   Will only improve skills the owner is trained in
    */
    public static Func<Skill, Bonus?> GetSkillBonus(Creature owner)
    {
        return (skill) =>
            {
                var bonus = TryGetCharacterStatus(owner)?.SkillBonus ?? 0;
                if (bonus > 0 && owner.Skills.IsTrained(skill))
                    return new Bonus(bonus, BonusType.Item, "Archipelago", true);
                return null;
            };
    }
    
    /**
    * Get this creature's perception bonuses (for qEffect.BonusToPerception)
    */
    public static Func<QEffect, Bonus?> GetPerceptionBonus()
    {
        return (qfSelf) =>
            {
                var bonus = TryGetCharacterStatus(qfSelf.Owner)?.PerceptionBonus ?? 0;
                if (bonus > 0)
                    return new Bonus(bonus, BonusType.Item, "Archipelago", true);
                return null;
            };
    }

    /**
    * Get this creature's iniative bonuses (for qEffect.BonusToInitiative)
    *   Generally, this is a perception roll, so just use that as a shorthand.
    */
    public static Func<QEffect, Bonus?> GetInitiativeBonus()
    {
        return (qfSelf) =>
        {
            var bonus = TryGetCharacterStatus(qfSelf.Owner)?.PerceptionBonus ?? 0;
            if (bonus > 0)
                return new Bonus(bonus, BonusType.Item, "Archipelago", true);
            return null;
        };
    }

    /**
     * Apply archipelago item improvements to the characters
     *  returns true/false if the item is a permanent item drop
     */
    public static bool ApplyCharacterUpgradeItem(ApPerCharacterItemTypes type, int slot, bool canIssuePermanant)
    {
        // Slot 0 affects everyone, slot 1-4 are the pcs
        var affectedPcs = (slot == 0)? Heroes.Values : [Heroes[CampaignHeroes[slot-1]]];
        foreach (var affectedPc in affectedPcs)
            
            lock (affectedPc) // (why did I do this? idk what race condition it was avoiding)
            {
                switch (type)
                {
                    case ApPerCharacterItemTypes.LevelUp:
                        affectedPc.Level++;
                        break;
                    case ApPerCharacterItemTypes.WeaponImprovement:
                        affectedPc.IncrementProgressiveWeaponBonuses(canIssuePermanant);
                        break;
                    case ApPerCharacterItemTypes.ArmorImprovement:
                        affectedPc.IncrementProgressiveArmorBonuses(canIssuePermanant);
                        break;
                    case ApPerCharacterItemTypes.SkillImprovement:
                        affectedPc.SkillBonus++;
                        break;
                    case ApPerCharacterItemTypes.PerceptionImprovement:
                        affectedPc.PerceptionBonus++;
                        break;
                    case ApPerCharacterItemTypes.Attack:
                        affectedPc.CanStrike = true;
                        break;
                    case ApPerCharacterItemTypes.Cast:
                        affectedPc.CanCastSpells = true;
                        break;
                    case ApPerCharacterItemTypes.Move:
                        affectedPc.CanStride = true;
                        break;
                    case ApPerCharacterItemTypes.Interact:
                        affectedPc.CanInteract = true;
                        break;
                }
            }
        return IsItemTypeSavedInInventory(type);
    }

    /**
    * Helper method to identify if a provided item type would be saved in the character's inventory
    */
    public static bool IsItemTypeSavedInInventory(ApPerCharacterItemTypes type)
    {
        return ArchipelagoClient.Instance?.ItemBonusSetting == ApItemBonusSettings.Manual && (
            type == ApPerCharacterItemTypes.ArmorImprovement ||
            type == ApPerCharacterItemTypes.WeaponImprovement
        );
    }

    /**
    * Helper method to Initialize all the Campaign Heroes quickly
    */
    public static void InitializeCampaignHeroes(int level, int weaponPotency, int strikingRunes, int armorPotency, int resilientRunes, int skillBonus, int perception, bool lockBasic, bool lockAll)
    {
        foreach (var id in CampaignHeroes)
            Heroes[id] = new CharacterStatus(level, 
                weaponPotency, strikingRunes, armorPotency, resilientRunes, skillBonus, perception,
                !lockBasic, !lockBasic, !lockAll, !lockAll);
    }

    /**
    * Helper method to Initialize all the Campaign Heroes based on the provided archipelago metadata
    */    
    public static void InitializeCampaignHeroes(int start_level, int atk_bonus, int armor_bonus, int skill_bonus, int perception, ApLockedActions locking)
    {
        InitializeCampaignHeroes(start_level, 
            // Potency/Striking bonuses = Total bonus / 2, then round up or down respectivley
            (atk_bonus + 1) / 2, atk_bonus / 2,
            (armor_bonus + 1) / 2, armor_bonus / 2,
            skill_bonus, perception,
            // Determine how restricte the action locking should be
            locking.LockBasicActions(), locking == ApLockedActions.Extreme);
    }

    /**
    * Helper method to Initialize all the Campaign Heroes with different placeholder values
    */
    public static void InitializeCampaignHeroesAsMock()
    {
        foreach (var i in Enumerable.Range(0, CampaignHeroes.Length))
            Heroes[CampaignHeroes[i]] = new CharacterStatus(1 + i, i / 2, i % 2, i / 2, i % 2, i % 2, i / 2);
    }

    /**
    * Helper method to create a QEffect to adjust item bonuses at the start of the character's turn.
    */
    public static QEffect GetAutomaticItemBonusQEffect()
    {
        return new QEffect("Archipelago - Item Bonus", "Your stats are being adjusted as a result of your Archipelago's progress!",
            ExpirationCondition.Never, null, new ModdedIllustration("archipelago_logo.png"))
        {
            StateCheck = (qfSelf) =>
            {
                // We only want to do this for pcs
                if (CampaignHeroes.Contains(qfSelf.Owner.CreatureId))
                    // Because this bonus func doesnt include a reference to the qeffect, we must update it here instead.
                    qfSelf.BonusToSkills = GetSkillBonus(qfSelf.Owner);
            },

            // Must check shields at start of turn because otherwise the state check timing adds
            //   a new shield to your hand when you try to drop the one you are holding.
            StartOfYourEveryTurn = (qfSelf, owner) =>
            {
                // We only want to do this for pcs
                if (CampaignHeroes.Contains(owner.CreatureId))

                    // Upgrade any shields the user is holding
                    Loot.UpgradeShields(owner, Heroes[owner.CreatureId].ArmorPotency);

                return Task.CompletedTask;
            },

            // Apply the ap bonuses
            // Todo: ABP has a qeffect that forces weapons to use a specific dice amount on attack
            //   at a minimum, we need this once we get the next campaign in order to support Greater Striking Runes
            //   but, i suspect we can backport it to force attack dice/potency to scale down too.
            BonusToAttackRolls = GetAttackBonus(),
            IncreaseItemDamageDieCount = GetAttackDiceBonus(),
            BonusToDefenses = GetDefenseBonus(),
            BonusToPerception = GetPerceptionBonus(),
            BonusToInitiative = GetInitiativeBonus(), // Seems to show up on the character sheet, which is cool
        };
    }

    /**
    * Helper method to create a QEffect which will prevent a character from taking certain actions.
    */
    public static QEffect GetActionLockingQEffect()
    {
        return new QEffect("Archipelago - Action Locks", "Your actions are being restricted by archipelago!",
            ExpirationCondition.Never, null, new ModdedIllustration("archipelago_logo.png"))
        {
            PreventTakingAction = action =>
            {
                // If the "caster" is subject to the archipelago
                var id = action.Owner.CreatureId;
                if (CampaignHeroes.Contains(id))
                {
                    // Check if the action is any type of spell
                    //  Do this manually, because "CountsAsSpell" checks for things we dont care to include
                    if ((action.HasTrait(Trait.Spell) || action.HasTrait(Trait.Impulse) || action.HasTrait(Trait.Spellstrike))
                        && (action.CastFromScroll == null)) // Let people cast with scrolls & equipment
                        
                        // If "cast spell" is disallowed
                        if (!Heroes[id].CanCastSpells)

                            // Return a blocking reason
                            return $"{action.Owner.Name} has not unlocked Spells!";

                    // Check if the action is any type of strike
                    if ((action.HasTrait(Trait.Attack) && !action.HasTrait(Trait.Spell)) || action.HasTrait(Trait.Spellstrike))
                        if (!Heroes[id].CanStrike)
                            return $"{action.Owner.Name} has not unlocked Strikes!";

                    // Check if the action is any type of movement, excluding "step"
                    if (action.HasTrait(Trait.Move) && action.ActionId != ActionId.Step)
                        if (!Heroes[id].CanStride)
                            return $"{action.Owner.Name} has not unlocked Stride!";

                    // Check if the action is an interact or manipulate action
                    if ((action.HasTrait(Trait.Interact) || action.HasTrait(Trait.Manipulate))
                            && !action.HasTrait(Trait.Spell) && action.Name != "Tantrum")
                        if (!Heroes[id].CanInteract)
                            return $"{action.Owner.Name} has not unlocked Interact & Manipulate!";

                    // Todo: consider additional options
                }

                // No restrictions found
                return null;
            },

            // Provide an alternate action if the character cant do anything
            ProvideMainAction = qfSelf =>
            {
                var id = qfSelf.Owner.CreatureId;
                if (CampaignHeroes.Contains(id))
                    if (!Heroes[id].CanCastSpells && !Heroes[id].CanStrike)
                        return new ActionPossibility(
                            new CombatAction(qfSelf.Owner, IllustrationName.Demoralize, "Tantrum", [Trait.Auditory, Trait.Sonic, Trait.Manipulate], 
                                "Frustrated at your inability to act, you shout at an enemy, dealing sonic damage equal to your level to one creature with a basic Fortitude save against either your Class or Spell DC.", Target.Ranged(12))
                                .WithActionCost(2)
                                .WithEffectOnEachTarget((spell, caster, target, result) =>
                                    {
                                        var damage = new KindedDamage(new SimpleDiceFormula(caster.Level, Dice.D1), DamageKind.Sonic);
                                        return CommonSpellEffects.DealBasicDamage(spell, caster, target, result, damage);
                                    })
                                .WithSavingThrow(new SavingThrow(Defense.Fortitude, qfSelf.Owner.ClassOrSpellDC()))
                                // Todo: should change this to be the actual va instead of anna/tokdar
                                .WithSoundEffect(qfSelf.Owner.HasTrait(Trait.Female)? SfxName.RageFemale1 : SfxName.RageMale1)
                                .WithProjectileCone(IllustrationName.Demoralize, 8, ProjectileKind.Cone)
                        );
                
                // Character can act normally, no need for the replacement action
                return null;
            }
        };
    }
    
    
    /// <summary>
    /// Get the QEffect which will apply trap effects to the target at the start of their turn.
    /// </summary>
    /// <returns>The QEffect</returns>
    public static QEffect GetTrapApplicationQEffect()
    {
        // Do not give this an image, we dont want it listed on the token.
        return new QEffect("Archipelago - Traps", "Watch out for Traps!", ExpirationCondition.Never, null)
            {
                StartOfYourPrimaryTurn = (qfSelf, owner) =>
                {                    
                    // Try to pop a trap off of the queue
                    if (PendingTraps.TryDequeue(out var trap))
                    {
                        // Get the name of the enum type, and then insert spaces. eg, Items.DoomTrap -> "Doom Trap"
                        string name = Regex.Replace(Enum.GetName(trap) ?? "", "([A-Z])", " $1").Trim();
                        if (name != "")
                            ApMessages.SendMessageInBattle(owner.Battle, $"{owner.Name} Triggers the {name}!");

                        // Special case effects
                        if (ApTrapItemTypes.TripTrap == trap)
                            return owner.FallProne();

                        if (ApTrapItemTypes.ButterfingersTrap == trap)
                            owner.HeldItems.ToArray().ForEach(owner.DropItem);

                        if (ApTrapItemTypes.ExplosiveTrap == trap)
                        {
                            // Create an explosion action to damage the caster and everyone adjancent
                            var aoe = Target.Emanation(1);
                            CombatAction explosion = new CombatAction(owner, IllustrationName.Fireball, "Explosive Trap", 
                                [Trait.Trap, Trait.Fire, Trait.DoesNotProvoke], "You triggered a trap!", aoe)
                                .WithActionCost(0)
                                .WithEffectOnEachTarget((spell, caster, target, result) =>
                                    {
                                        // 1d6 * level fire damage
                                        var damage = new KindedDamage(new SimpleDiceFormula(owner.Level, Dice.D6), DamageKind.Fire);
                                        return CommonSpellEffects.DealBasicDamage(spell, caster, target, result, damage);
                                    })
                                .WithSavingThrow(new SavingThrow(Defense.Reflex, 14 + owner.Level)) // How to get Level DC? It's close anyway
                                .WithProjectileCone(new VfxStyle(20, ProjectileKind.Cone, IllustrationName.Fireball))
                                .WithSoundEffect(SfxName.Fireball);
                            
                            // Execute the trap action and return its task
                            return owner.Battle.GameLoop.FullCast(explosion, ChosenTargets.AutoconfirmEmanation(aoe));
                        }

                        if(ApTrapItemTypes.WarpTrap == trap)
                        {
                            // Starting at a 10 square range and moving inward, try to find a valid tile to teleport the pc to
                            var legalTiles = Target.TileYouCanSeeAndTeleportTo(10).GetLegalTargetTiles(owner);
                            for (int i=10; i>0; --i)
                            {
                                // Try to pick a random legal tile at the specified range
                                var selectedTile = owner.Battle.Map.AllTiles
                                    .Where(tile => tile.DistanceTo(owner) == i)
                                    .Where(tile => legalTiles.Contains(tile))
                                    .ToList().GetRandomForAi();

                                // If we found one, teleport to it.
                                if (selectedTile != null)
                                {
                                    Sfxs.Play(SfxName.Abjuration);
                                    return CommonSpellEffects.Teleport(owner, selectedTile);
                                }
                            }
                        }

                        // For traps which make well known QEffects, determine which to apply
                        QEffect? trapEffect = trap switch
                        {
                            ApTrapItemTypes.ClumsyTrap => QEffect.Clumsy(2).WithExpirationAtStartOfOwnerTurn(),
                            ApTrapItemTypes.EnfeeblingTrap => QEffect.Enfeebled(2).WithExpirationAtStartOfOwnerTurn(),
                            ApTrapItemTypes.StupifyingTrap => QEffect.Stupefied(2).WithExpirationAtStartOfOwnerTurn(),
                            ApTrapItemTypes.FearTrap => QEffect.Frightened(2), // Automatically expires over time
                            ApTrapItemTypes.SickeningTrap => QEffect.Sickened(2, 15), // Must Retch
                            ApTrapItemTypes.SkipTurnTrap => QEffect.Stunned(3), // automatically expires after skipping your turn
                            ApTrapItemTypes.GlueTrap => QEffect.Immobilized().WithExpirationAtStartOfOwnerTurn(),
                            ApTrapItemTypes.DoomTrap => QEffect.Doomed(1), // Does not expire until end of battle
                            ApTrapItemTypes.FlashpowderTrap => QEffect.Blinded().WithExpirationAtStartOfOwnerTurn(),
                            ApTrapItemTypes.PoisonDartTrap => QEffect.PersistentDamage(new SimpleDiceFormula(owner.Level, Dice.D1), DamageKind.Poison),
                            _ => null
                        };

                        // If we have a QEffect trap, apply it to the token.
                        if (trapEffect != null)
                            owner.AddQEffect(trapEffect);
                    }

                    return Task.CompletedTask;
                }
            };
    }
    
    /// <summary>
    /// Get the QEffect which will apply boon effects to the target at the start of their turn.
    /// </summary>
    /// <returns>The Qeffect</returns>
    public static QEffect GetBoonApplicationQEffect()
    {
        // Do not give this an image, we dont want it listed on the token.
        return new QEffect("Archipelago - Boons", "Watch out for Boons!", ExpirationCondition.Never, null)
            {
                StartOfYourPrimaryTurn = (qfSelf, owner) =>
                {                    
                    // Try to pop a trap off of the queue
                    if (PendingBoons.TryDequeue(out var boon))
                    {
                        // Get the name of the enum type, and then insert spaces. eg, Items.DoomTrap -> "Doom Trap"
                        string name = Regex.Replace(Enum.GetName(boon) ?? "", "([A-Z])", " $1").Trim();
                        if (name != "")
                            ApMessages.SendMessageInBattle(owner.Battle, $"{owner.Name} Recieved the {name}!");

                        // Special case effects
                        if (ApBoonItemTypes.BoonOfHealth == boon)
                            return owner.HealAsync(new SimpleDiceFormula(owner.Level, Dice.D6), 
                                new CombatAction(owner, IllustrationName.Heal, "Boon of Health", [Trait.Healing], "You have been healed!", Target.Self()));

                        if (ApBoonItemTypes.DivineBlessing == boon)
                        {
                            owner.AddQEffect(new QEffect("Divine Blessing", 
                                "You are blessed, granting you a +1 status bonus to all your checks and DC's for the duration of this combat.", ExpirationCondition.Never, null, IllustrationName.Bless)
                            {
                                CountsAsBeneficialToSource = true,
                                CountsAsABuff = true,
                                BonusToAllChecksAndDCs = (qfSelf) => new Bonus(1, BonusType.Status, "Divine Blessing", true),
                            });
                            return Task.CompletedTask;
                        }

                        if (ApBoonItemTypes.BoonOfLuck == boon)
                        {
                            // Copied from True Strike
                            owner.AddQEffect(new QEffect("True Strike", "The next time you make an attack roll before the end of your turn, roll the attack twice and use the better result. The attack ignores circumstance penalties to the attack roll and any flat check required due to the target being concealed or hidden.", ExpirationCondition.ExpiresAtEndOfSourcesTurn, owner, IllustrationName.TrueStrike)
                            {
                                CountsAsBeneficialToSource = true,
                                CountsAsABuff = true,
                                Id = QEffectId.TrueStrike,
                                DoNotShowUpOverhead = true,
                                ProvideFortuneEffectForActiveRolls = (_, action, _) => (!action.HasTrait(Trait.Attack)) ? null : "True Strike",
                                AfterYouMakeAttackRoll = delegate (QEffect qfSelf, CheckBreakdownResult result)
                                {
                                    qfSelf.ExpiresAt = ExpirationCondition.Immediately;
                                    qfSelf.Owner.RemoveAllQEffects(qff => qff == qfSelf);
                                }
                            });
                            return Task.CompletedTask;
                        }

                        if (ApBoonItemTypes.SupernaturalBattelcry == boon)
                        {
                            // do an aoe demoralize
                            var demoralize = CommonCombatActions.Demoralize(owner).WithActionCost(0).WithNewTarget(Target.EnemiesOnlyEmanation(6));
                            return owner.Battle.GameLoop.FullCast(demoralize, ChosenTargets.AutoconfirmEmanation(demoralize.Target));
                        }

                        if (ApBoonItemTypes.ForestsProtection == boon)
                        {
                            // Make a timber sentinel at the closest free tile
                            DifficultSpells.CreateProtectorTree(owner, owner.MaximumSpellRank, owner.Space.CenterTile, timberSentinel: true);
                            return Task.CompletedTask;
                        }


                        // For traps which make well known QEffects, determine which to apply
                        QEffect? boonEffect = boon switch
                        {
                            ApBoonItemTypes.BoonOfFastHealing => QEffect.FastHealing(owner.Level),
                            ApBoonItemTypes.BoonOfAlacrity => QEffect.StatusSpeedIncrease(2),
                            ApBoonItemTypes.BoonOfImmortality => QEffect.CannotGoBelow1HP().WithExpirationAtEndOfOwnersNextTurn(),
                            ApBoonItemTypes.BoonOfInvulnerability => QEffect.DamageResistanceAllExcept(owner.Level, []).WithExpirationAtEndOfOwnersNextTurn(),
                            _ => null
                        };

                        // If we have a QEffect trap, apply it to the token.
                        if (boonEffect != null)
                            owner.AddQEffect(boonEffect);
                    }

                    return Task.CompletedTask;
                }
            };
    }

    /**
    * Get the current level of the campaign hero who corresponds to the provided index.
    * If archipelago is not enabled, will just return the input value instead.
    * If archipelago.ManualLevelUps is false, will return the input value.
    * This is called by our patched version of the SpawnHero method during combat setup
    */
    public static int GetLevelForHeroIndex(int original, int index)
    {
        if (index > 3 || index < 0) // Debug (checking arguemnt order)
            throw new InvalidOperationException($"Bad Arguments to GetLevelForHeroIndex index={index} level={original}");

        if (DawnsburyArchipelagoLoader.IsArchipelagoCampaignActive() && (ArchipelagoClient.Instance?.ApLevelUps ?? false))
            return Heroes[CampaignHeroes[index]].Level;
        else
            return original;
    }

    /**
     * Try to resolve a character's name into their archipelago status
     */
    public static CharacterStatus? TryGetCharacterStatus(Creature creature)
    {
        var id = creature.CreatureId;
        if (CampaignHeroes.Contains(id))
            return Heroes[id];

        // If we are in a menu, we won't have an id.
        if (id == CreatureId.None && creature.PersistentCharacterSheet is {} sheet)
            return Loot.TryGetCharacterStatus(sheet);

        // Not a PC
        return null;
    }
}