from typing import List
from BaseClasses import Entrance, MultiWorld, Region # pyright: ignore[reportMissingImports]
from .CampaignData import Campaign, get_chosen_campaign
from .Locations import get_locations_for_regions
from .Settings import DawnsburyOptions

# Note: This must exist, even if its just one region with everything in it.
# This could be done by chapter instead, but regions typically represent locks
# which you need items to get through, and thats not super applicable here.

# TODO: try out level locked regions for progression balancing (really hard with dynamic encounters)

def create_regions(world: MultiWorld, player: int, options: DawnsburyOptions) -> List[Region]:
    '''Create Archipelago Regions for the campagins'''

    # Special menu region is required, and all campaigns must connect here to start 
    menu = Region('Menu', player, world)
    regions = [menu]

    # Create the campaign region
    campaign = get_chosen_campaign(options)
    regions += create_level_regions(campaign, player, options, world, menu)

    # Return the regions
    return regions

def create_level_regions(campaign: Campaign, player: int, options: DawnsburyOptions, world: MultiWorld, menu: Region) -> list[Region]:
    '''Create a set of regions, representing each campaign level'''
    regions = []
    previous_region = menu
    for level in range(campaign.start_level, campaign.end_level + 1):
        # Create and link the region
        name = make_level_region_name(level)
        region = Region(name, player, world)
        
        # Add a link to the region (require level ups by the end of the region which uses them, eg, level 2 by the end of act 2)
        link_regions(player, previous_region, name, region)
        previous_region = region

        # Save it in the list
        regions += [region]

    # Fill out the locations for eacn region
    get_locations_for_regions(campaign, regions, player, options)

    return regions

def make_level_region_name(level: int) -> str:
    '''Make a region name for a given level'''
    return "Level %s" % level

# Link two regions together
def link_regions(player: int, region1: Region, connection_name: str, region2: Region):
    exit = Entrance(player, connection_name, region1)
    region1.exits.append(exit)
    exit.connect(region2)