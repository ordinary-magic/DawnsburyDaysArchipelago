from BaseClasses import Item, ItemClassification
from itertools import product
from typing import Dict, List

from .CampaignData import get_chosen_campaign, DEFAULT_CHARACTERS
from .Options import DawnsburyOptions

# All id's need to be unique across all games, thus, we define an starting offset value
# Note: they seem to have removed the uniqueness requirement in more recent versions.
BASE_OFFSET = 0x02400
GAME_COMPLETE = 'All Encounters Clear!'

class DawnsburyItem(Item):
    game = "Dawnsbury Days"

    def __init__(self, name: str, id: int, progression: bool, player: int = None):
        super(DawnsburyItem, self).__init__(
            name,
            ItemClassification.progression if progression else ItemClassification.useful,
            id if id >= 0 else None,
            player
        )

# Lists of all items that archipelago can award.
singleton_items: List[str] = [
    GAME_COMPLETE
]
per_character_items: List[str] = [
    "Level Up",
    "Weapon Upgrade",
    #"Weapon Potency Rune", # Eventually, can have both of these and switch based on settings
    #"Weapon Striking Rune",
    "Armor Upgrade"
]

def expand_per_character_items() -> List[str]:
   return [make_character_item_name(item, name) for item, name in product(per_character_items, DEFAULT_CHARACTERS)]

def get_all_item_names() -> List[str]:
    return expand_per_character_items() + singleton_items

def make_character_item_name(item: str, character: str) -> str:
    '''Define a common design for a character specific item name'''
    return item + ' (' + character + ')'

# Kinda all of our items are progression items rn. Will change if we include random reward loot.
progression_items: List[str] = get_all_item_names()

def ap_get_all_items() -> Dict[str, int]:
    '''Get the code for every possible item in the multiworld'''
    return get_cached_item_directory()

_dd_item_cache = {}
def get_cached_item_directory() -> Dict[str, int]:
    '''Caching wrapper function so we dont have to constantly regenerate this directory'''
    global _dd_item_cache
    if not _dd_item_cache:
        _dd_item_cache = {name: (id+BASE_OFFSET) for id, name in enumerate(get_all_item_names())}
    return _dd_item_cache

def create_item(name: str, player: int) -> DawnsburyItem:
    '''Create a single item upon request by the server'''
    if name in get_cached_item_directory():
        return DawnsburyItem(name, get_cached_item_directory()[name], name in progression_items, player)
    else: # Not a real item, make it an "event" item instead
         return DawnsburyItem(name, -1, True, player)

def create_items_for_each_character(item_name: str, characters: List[str], player: int) -> List[DawnsburyItem]:
    '''Create a set of dawnsbury items of the input type for each character in the provided list'''
    return [create_item(make_character_item_name(item_name, character), player) for character in characters]

def create_items(player: int, options: DawnsburyOptions) -> List[DawnsburyItem]:
    '''Preare a list of items to include in the randomizer based on the selected customization options'''
    per_character, single = get_chosen_campaign(options).get_all_campaign_drops(options)
    
    all_drops = [create_item(item, player) for item in single]
    for item_name in per_character:
        all_drops += create_items_for_each_character(item_name, DEFAULT_CHARACTERS, player)

    return all_drops
