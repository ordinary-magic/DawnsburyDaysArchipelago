from dataclasses import dataclass

from Options import FreeText, Toggle, DefaultOnToggle, PerGameCommonOptions, Visibility

# If we eventually add loot drops to the archipelago randomization then the pool will need to be configured here: 
# would need to add options for what loot is included, progressive bonuses vs drops, level up together or seperate, etc.
# Can also add options for 'whammy' type drops that temporarily debuff the player too

class EncounterShuffle(DefaultOnToggle):
    """Should the order of encounters within the campaign be randomized?"""
    display_name = "Encounter Shuffle"

class LootShuffle(DefaultOnToggle):
    """Should the encounter rewards be shuffled?"""
    display_name = "Loot Shuffle"

class GoldenCandelabra(DefaultOnToggle):
    '''Placeholder: Does this archipelago include the golden candelabra campaign.
    TODO: Should eventually be replaced with a more robust campaign selection menu.'''
    visibility = Visibility.none # Do not show the option

class DeathLink(Toggle):
    """If enabled, losing an encounter will kill all other deathlink players, and other players can cause you to wipe."""
    display_name = "Death Link"
    
class Seed(FreeText):
    """An arbitraty piece of text to use as a seed for the randomization. (Leave blank to use a random seed)"""
    display_name = "RNG Seed"

@dataclass
class DawnsburyOptions(PerGameCommonOptions):
    encounter_shuffle: EncounterShuffle
    loot_shuffle: LootShuffle
    deathlink: DeathLink
    rng_seed: Seed # "seed" is an undocumented, already used field name in archipealgo, so we must use rng_seed instead.

def make_option_slot_data(options: DawnsburyOptions):
    '''Extract and format relevant slot data to send the client from a set of options'''
    return {
        'encounter_shuffle': options.encounter_shuffle.value,
        'loot_shuffle': options.loot_shuffle.value,
        'deathlink': options.deathlink.value,
        'rng_seed': options.rng_seed.current_key,
    }