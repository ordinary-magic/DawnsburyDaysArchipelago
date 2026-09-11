from typing import Dict, List
from math import ceil, floor
from BaseClasses import Location, Region # pyright: ignore[reportMissingImports]
from .CampaignData import Campaign, get_max_encoutners_and_levels
from .Items import BASE_OFFSET, GAME_COMPLETE, determine_amount_of_items
from .Settings import DawnsburyOptions, MAX_BONUS_ENCOUNTERS_PER_LEVEL

class DawnsburyLocation(Location):
    game: str = "Dawnsbury Days"

    def __init__(self, player, name, code, region):
        super().__init__(player, name, code, region)

# TODO: This does not need to be static, instead we can make it after determining the game's location list
# Start w game clear location, then increment for each encounter drop
def make_location_cache() -> Dict[str, int]:
    '''Get the ap code for all possible locations in the game'''

    # We need a unique location for every encounter in a run.
    # Fortunatley, we only do one campaign per run so we only need enough to cover the biggest campaign
    max_encounters, max_levels = get_max_encoutners_and_levels()
    
    # Now make a list of Battle #3 type names for each encounter to save as the resolver cache
    result = {}
    for i in range(0, max_encounters):
        result[get_encounter_name(i+1)] = i + BASE_OFFSET

    # Make "bonus" encounters
    for i in range(0, max_levels * MAX_BONUS_ENCOUNTERS_PER_LEVEL):
        result[get_bonus_encounter_name(i+1)] = i + max_encounters + BASE_OFFSET

    # Make the last encounter
    result[get_last_location_name()] = len(result)

    #print([name for name in result.keys()])
    return result

def get_encounter_name(number: int) -> str:
    '''Standardized encounter name generator'''
    return 'Battle #%s' % number

def get_bonus_encounter_name(number: int) -> str:
    '''Standardized encounter name generator'''
    return 'Bonus #%s' % number

# Alt implementation, for level-based regions.
def get_locations_for_regions(campaign: Campaign, regions: List[Region], player: int, options: DawnsburyOptions) -> None:
    count = 0
    locations = [] # Save the region locations to a list so we can access them later

    # Get all the base game locations
    for level in range(0, campaign.num_levels()):
        region = regions[level]
        locations.append([])

        for num in range(count, count + campaign.encounters_per_level[level]): # [0, N-1]
            loc = make_region_location(player, num+1, region, False)
            locations[level].append(loc)

        count += campaign.encounters_per_level[level]

    # Determine how many missing encounters to make and add them to the list
    # Front load them: eg, across 4 levels, 6 bonus encounters would do this: [2,2,2,0]
    bonus = get_location_count(campaign, options) - count
    bonus_per_level = ceil(bonus / campaign.num_levels())
    for i in range(0, bonus):
        level = floor(i / bonus_per_level)
        loc = make_region_location(player, i+1, regions[level], True)
        locations[level].append(loc)

    # Replace the last encounter in the last region with the final location
    locations[-1][-1] = get_last_location(regions[-1], player)

    # Save the collated locations to their corresponding region
    for level in range(0, campaign.num_levels()):
        regions[level].locations = locations[level]

def make_region_location(player: int, number: int, region: Region, isBonus: bool) -> DawnsburyLocation:
    '''Construct a location and attach it to a given region.'''
    name = get_bonus_encounter_name(number) if isBonus else get_encounter_name(number) 
    location = DawnsburyLocation(player, name, location_resolver_cache[name], region)
    #region.locations.append(location)
    return location

def get_last_location_name() -> str:
    '''Get the final encounter in the randomizer.'''
    return GAME_COMPLETE

def get_last_location(last_region: Region, player: int) -> DawnsburyLocation:
    '''Get the final encounter in the randomizer.'''
    name = get_last_location_name()
    code = location_resolver_cache[name]
    return DawnsburyLocation(player, name, code, last_region)

def get_location_count(campaign: Campaign, options: DawnsburyOptions):
    '''Get the amount of locations in a given set of options'''
    # Note: since we include the "final boss" location too, we have 1 more
    return determine_amount_of_items(campaign, options) + 1

location_resolver_cache = make_location_cache()