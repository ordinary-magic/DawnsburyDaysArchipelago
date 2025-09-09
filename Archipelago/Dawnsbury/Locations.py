from typing import Dict, List
from BaseClasses import Location, Region
from .CampaignData import Campaign, All_Campaigns, get_chosen_campaign
from .Items import BASE_OFFSET
from .Options import DawnsburyOptions

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
    max_encounters = 0
    for campaign in All_Campaigns:
        if campaign.num_encounters > max_encounters:
            max_encounters = campaign.num_encounters
    
    # Now make a list of Battle #3 type names for each encounter to save as the resolver cache
    result = {}
    for i in range(0,max_encounters):
        result[get_encounter_name(i+1)] = i + BASE_OFFSET
    return result

def get_encounter_name(number: int) -> str:
    '''Standardized encounter name generator'''
    return 'Battle #%s' % number

def get_locations(campaign: Campaign, region: Region, player: int) -> List[DawnsburyLocation]:
    '''Prepare a list of properly formatted archipelago Location objects for the corresponding region'''
    locations = []
    for i in range(1, campaign.num_encounters+1): # 1-n (inclusive)
        name = get_encounter_name(i)
        locations.append(DawnsburyLocation(player, name, location_resolver_cache[name], region))
    return locations

def get_last_location(options: DawnsburyOptions):
    '''Get the final encounter in the randomizer.'''
    return get_encounter_name(get_chosen_campaign(options).num_encounters)

location_resolver_cache = make_location_cache()