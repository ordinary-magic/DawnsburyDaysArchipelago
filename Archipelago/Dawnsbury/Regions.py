from typing import List
from BaseClasses import Entrance, MultiWorld, Region
from .CampaignData import Campaign, get_chosen_campaign
from .Locations import get_locations
from .Options import DawnsburyOptions
from .Rules import get_required_level_rule

# Note: This must exist, even if its just one region with everything in it.
# This could be done by chapter instead, but regions typically represent locks
# which you need items to get through, and thats not super applicable here.

# TODO: decide how to handle campaign selection.

def create_regions(world: MultiWorld, player: int, options: DawnsburyOptions) -> List[Region]:
    '''Create Archipelago Regions for the campagins'''

    # Special menu region is required, and all campaigns must connect here to start 
    menu = Region('Menu', player, world)
    regions = {'Menu': menu}

    # Create the campaign region
    campaign = get_chosen_campaign(options)
    new_region = create_region(campaign, player, world)
    regions[campaign.name] = new_region
    link_regions(player, menu, campaign.name, new_region, 0)

    # Return the regions
    return regions.values()

# Create a single region object with the specified parameters
def create_region(campaign: Campaign, player: int, world: MultiWorld):
    region = Region(campaign.name, player, world)
    region.locations = get_locations(campaign, region, player)
    return region

# Link two regions together
def link_regions(player: int, region1: Region, connection_name: str, region2: Region, required_level_ups: int):
    exit = Entrance(player, connection_name, region1)
    if required_level_ups > 0:
        exit.access_rule = get_required_level_rule(player, required_level_ups)
    region1.exits.append(exit)
    exit.connect(region2)