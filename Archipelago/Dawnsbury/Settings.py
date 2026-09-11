from dataclasses import dataclass

from Options import Choice, FreeText, Toggle, DefaultOnToggle, PerGameCommonOptions, Range # pyright: ignore[reportMissingImports]

# The absolute maximum number of bonus encoutnters we support, so we know for item/location generation.
MAX_BONUS_ENCOUNTERS_PER_LEVEL = 20

class EncounterShuffle(DefaultOnToggle):
    """Should the order of encounters within the campaign be randomized?"""
    display_name = "Encounter Shuffle"

class ShuffleLevelRestriction(Range):
    '''When shuffling encounters, how many levels above an encounter's level can its replacement be?
       eg, in "Dawnsbury Days", which goes from levels 1-4, if you set this to 3,
       you can encounter the final boss (a level 4 encounter) as the first mission.'''
    display_name = "Shuffle Level Restriction"
    range_start  = 0
    range_end    = 20
    default = 0

class IncludeFreeEncounters(Choice):
    '''When encounters are shuffled, should we include the Free Encounters in the selection?
       If so, should we include them only as padding for unfilled item drops, as randomly selected replacements for campaign encounters, 
       add some randomly chosen extras at each level, or increase the campaign length to include every possible encounter?
       Notes: "Every Encounter" might cause us to run out of drops, causing some encounters to not reward anything,
                and no padding might cause us to run out of encounters, causing drops to stack up at the end of a level module.
                By default, neither of these should happen if you have either (but not both) of item bonuses or basic locked actions enabled.'''
    display_name = "Include Free Encounters"
    option_none = 0
    option_as_padding = 1
    option_as_replacements = 2 # Implicitly includes padding
    option_some_extras = 3 # 2 Per level, does not replace existing ones. Added in addition to padding, but should it?
    option_every_encounter_possible = 4 # Use "extra" amount of locations, and make everything else un-rewarded
    default = 1

class IncludeExtremePlusEncounters(Toggle):
    '''If we include free encounters, should we include Extreme+ (or harder) ones?
        Caution: Extreme+ free encounters often assume you have custom built characters, and may be *very* hard.'''
    display_name = "Include Extreme+ Encounters"

class ExtraEncountersPerLevel(Range):
    '''If we have selected "Some Extra" or "Every Possible" as the encounter setting, how many extra encounters should we add for each level?
       Note: the randomizer will try to add as many random encounters as you select here, but if the game runs out of unique random
              encounters before we hit this number, the game will have to stack multiple drops onto the same encounter.'''
    display_name = "Extra Encounters Per Level"
    range_start  = 0
    range_end    = MAX_BONUS_ENCOUNTERS_PER_LEVEL
    default      = 2

class LootShuffle(DefaultOnToggle):
    """Should the encounter rewards (potions, scrolls, etc) be shuffled between encounters?"""
    display_name = "Loot Shuffle"

class LootRandomizer(Choice):
    """Should the encounter rewards (scrolls, runestones, weapons, etc) be changed by the randomizer?
       You can choose to make the new item match the original's type and/or level."""
    display_name = "Loot Randomizer"
    option_do_not_randomize = 0
    option_same_type_and_level = 1
    option_same_type = 2
    option_same_level = 3
    option_any_item_at_all = 4
    default = 1

class RandomQuickStartBuilds(Toggle):
    """This option will change the "Quick Start" setting to use a random build for each character.
       This will select from your pool of installed pregen characters, without any repeats."""
    display_name = "Random Starting Class"

class IncludeModContent(Toggle):
    """If you chose options which affect items, builds, or encounters, should the randomizer include modded content?
       WARNING: If enabled, changing which mods are installed during a run can cause issues."""
    display_name = "Include Modded Content"

class Campaign(Choice):
    '''Select which campaign(s) you want to play.
       If you include DLC, you must own that dlc or the campaign will not generate. Likewise for the roguelike campaign and the mod.
       Note: Loot & Encounter customization aren't supported in roguelike mode (its already a roguelike).'''
    display_name = "Campaign Selection"
    option_Dawnsbury_Days = 0
    option_Profane_Barrier = 1
    option_Dawnsbury_Days_and_Profane_Barrier = 2
    option_Good_Little_Children = 3
    option_All_Base_Game = 4
    option_Roguelike = 100
    default = 0

class LockedActions(Choice):
    """If enabled, the archipelago will force you to unlock the ability to perform certain actions, such as Strike or Cast a Spell.
       Standard mode locks only Strike & Cast a Spell (and related activities), while extreme mode also includes Move & Interact/Manipulate actions.
       Notes: Kineticest Impulses count as spells. Spells with attack rolls do not require unlocking strikes, but Spellstrikes require both.
            Using scrolls/items to cast spells is unlocked with interact, not spellcasting, and likewise for strikes with other consumables (like bombs).
            Archipelago will give you at least one attack and spell upgrade by the end of the first scenario, and someone will also unlock movement+interact in extreme mode. 
            You can always Step, and if a character cannot attack or cast spells, they are given a special \"Tantrum\" action to help prevent softlocks."""
    display_name = "Locked Actions"
    option_off = 0
    option_standard = 1
    option_extreme = 2
    default = 0

class ItemBonuses(Choice):
    """If enabled, the archipelago drops will include item bonuses for characters weapons/armor/skill checks according to the campaign's level range."""
    """Automatic (default) awards these immediatley in combat, while Manual drops them as runestones which must be equipped later instead."""
    display_name = "Item Bonus Mode"
    option_none = 0
    option_automatic = 1
    option_manual = 2
    default = 1

class LevelUps(DefaultOnToggle):
    """If enabled, character level ups are awarded by the archipelago, instead of at the end of chapters."""
    display_name = "Level-Ups"

class PerCharacter(DefaultOnToggle):
    """If enabled, each unlocks/bonus drops are awarded to each PC individually (4 each). If not, upgrades affect every PC at once.
        Note: this quarters the amount of items, so you probably want to add more traps or everything will just be loot bags."""
    display_name = "Per-Character Drops"

class DeathLink(Toggle):
    """If enabled, losing an encounter will kill all other deathlink players, and other players can cause you to wipe."""
    display_name = "Death Link"

class Hardcore(Toggle):
    """If enabled, losing an encounter will force you to restart the campaign."""
    display_name = "Hardcore Mode"

class Traps(Range):
    """For unfilled locations, we can put \"Trap\" items instead of filler loot bags or skill bonuses.
        This setting determine the maximum amount to include."""
    display_name = "Maxmimum Trap Amount"
    range_start  = 0
    range_end    = 1000
    default      = 0

class Seed(FreeText):
    """An arbitraty piece of text to use as a seed for the randomization. (Leave blank to use a random seed)"""
    display_name = "RNG Seed"

@dataclass
class DawnsburyOptions(PerGameCommonOptions):
    encounter_shuffle: EncounterShuffle
    shuffle_level: ShuffleLevelRestriction
    include_free_encounters: IncludeFreeEncounters
    include_extreme_encounters: IncludeExtremePlusEncounters
    extra_filler_amount: ExtraEncountersPerLevel
    loot_shuffle: LootShuffle
    loot_randomizer: LootRandomizer
    random_builds: RandomQuickStartBuilds
    mod_content : IncludeModContent
    campaign: Campaign
    locked_actions: LockedActions
    item_bonuses: ItemBonuses
    level_ups: LevelUps
    per_character: PerCharacter
    deathlink: DeathLink
    hardcore: Hardcore
    traps: Traps
    rng_seed: Seed # "seed" is an undocumented, already used field name in archipealgo, so we must use rng_seed instead.

def make_option_slot_data(options: DawnsburyOptions):
    '''Extract and format relevant slot data to send the client from a set of options'''
    return {
        'encounter_shuffle': options.encounter_shuffle.value,
        'shuffle_level': options.shuffle_level.value,
        'include_free_encounters': options.include_free_encounters.value,
        'include_extreme_encounters': options.include_extreme_encounters.value,
        'loot_shuffle': options.loot_shuffle.value,
        'loot_randomizer': options.loot_randomizer.value,
        'random_builds': options.random_builds.value,
        'mod_content' : options.mod_content.value,
        'campaign': options.campaign.value,
        'locked_actions': options.locked_actions.value,
        'item_bonuses': options.item_bonuses.value,
        'level_ups': options.level_ups.value,
        'deathlink': options.deathlink.value,
        'hardcore': options.hardcore.value,
        'rng_seed': options.rng_seed.current_key,
    }
