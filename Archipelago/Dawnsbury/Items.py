import random

from BaseClasses import Item, ItemClassification # pyright: ignore[reportMissingImports]
from itertools import product, islice
from typing import Dict, Generator, List

from .CampaignData import Campaign, get_chosen_campaign, DEFAULT_CHARACTERS
from .Settings import DawnsburyOptions

# All id's need to be unique across all games, thus, we define an starting offset value
# Note: they seem to have removed the uniqueness requirement in more recent versions.
BASE_OFFSET = 0x02400
GAME_COMPLETE = 'All Encounters Clear!'

class DawnsburyItem(Item):
    game = "Dawnsbury Days"

    def __init__(self, name: str, id: int, player: int = -1):
        super(DawnsburyItem, self).__init__(
            name,
            classify_item(name),
            id if id >= 0 else None,
            player
        )

# Lists of all items that archipelago can award.
singleton_items: List[str] = [
    GAME_COMPLETE,
    "Loot Bag"
]

trap_items: List[str] = [
    "Clumsy Trap",
    "Enfeebling Trap",
    "Stupifying Trap",
    "Trip Trap",
    "Fear Trap",
    "Sickening Trap",
    "Skip-Turn Trap",
    "Glue Trap",
    "Butterfingers Trap",
    "Explosive Trap",
    "Doom Trap",
    "Warp Trap",
    "Flashpowder Trap",
]

per_character_items: List[str] = [
    "Level Up",
    "Weapon Upgrade",
    "Armor Upgrade",
    "Skill Upgrade",
    "Perception Upgrade",
    "Attack",
    "Cast Spell",
    "Move",
    "Interact",
]

def get_all_item_names() -> List[str]:
    '''Get a list of every single item name that we can generatre'''
    return expand_per_character_items() + singleton_items + trap_items

def expand_per_character_items() -> List[str]:
   '''Create the names of all possible per-character items'''
   return make_character_item_names(per_character_items, DEFAULT_CHARACTERS)

def make_character_item_names(items: List[str], characters: List[str]) -> List[str]:
   '''Expand the input list of item names to a list of item names for each character'''
   return [make_character_item_name(item, name) for item, name in product(items, characters)]

def make_character_item_name(item: str, character: str) -> str:
    '''Define a common design for a character specific item name'''
    return item + ' (' + character + ')'

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
        return DawnsburyItem(name, get_cached_item_directory()[name], player)
    else: # Not a real item, make it an "event" item instead
         return DawnsburyItem(name, -1, player)

def create_items_for_each_character(item_name: str, characters: List[str], player: int) -> List[DawnsburyItem]:
    '''Create a set of dawnsbury items of the input type for each character in the provided list'''
    return [create_item(make_character_item_name(item_name, character), player) for character in characters]

# Items which are not included in a specific run
excluded_items: List[str] = [
    # Must give a random "interact" each run at the start to prevent softlocks
    make_character_item_name("Interact", random.choice(DEFAULT_CHARACTERS))
]

def remove_excluded_items(input: List[DawnsburyItem]) -> List[DawnsburyItem]:
    '''Remove banned items from the item pool'''
    return [i for i in input if i.name not in excluded_items]

def remove_excluded_item_names(input: List[str]) -> List[str]:
    '''Remove banned item names from the item name pool'''
    return [i for i in input if i not in excluded_items]

def get_excluded_item_ids() -> List[int]:
    '''Get a list of archieplago id codes representing all the excluded items'''
    id_lookup = get_cached_item_directory()
    return [id_lookup[name] for name in excluded_items]

def create_items(player: int, options: DawnsburyOptions) -> List[DawnsburyItem]:
    '''Preare a list of items to include in the randomizer based on the selected customization options'''
    
    # Determine the campaign and make the required items
    campaign = get_chosen_campaign(options)
    all_items = make_required_items(campaign, player, options)

    # Determine how many additional items we need
    num_items = determine_amount_of_items(campaign, options)
    still_needed = num_items - len(all_items)

    # Trim some items, and complain about it in the log
    if (still_needed < 0):
        print(f"Warning: Generated {-still_needed} too many required items, trimming drop list.")
        return all_items[:num_items]

    # Check if we must generate filler items
    if (still_needed > 0):
        print(f"Generating {still_needed} filler items for dawnsbury days.")
        all_items += make_filler_items(campaign, still_needed, player, options)

    return all_items

def make_required_items(campaign: Campaign, player:int, options: DawnsburyOptions) -> List[DawnsburyItem]:
    '''Using the provided options, create all non-optional items'''

    # Determine what types of items and how many we need from the campaign
    per_character, single = campaign.get_required_campaign_drops(options)

    # Add any campaign-independent drops
    per_character += get_settings_required_per_character_drops(options)
    single += get_settings_required_single_drops(options)

    # Expand the type lists into the acutal items
    required_drops = [create_item(item, player) for item in single]
    for item_name in per_character:
        required_drops += create_items_for_each_character(item_name, DEFAULT_CHARACTERS, player)

    # Remove any excluded items
    required_drops = remove_excluded_items(required_drops)

    return required_drops

def get_settings_required_per_character_drops(options: DawnsburyOptions) -> List[str]:
    '''Check if there are any campaign-indepenedent drops to include'''
    drops = []

    # Check the settings for action unlocks
    if (options.locked_actions == 1 or options.locked_actions == 2):
        drops += ["Attack", "Cast Spell",]

    # Check if we are in "extreme" mode
    if (options.locked_actions == 2):
        drops += ["Move", "Interact",]

    return drops

def get_settings_required_single_drops(options: DawnsburyOptions) -> List[str]:
    '''Check if there are any campaign-indepenedent drops to include.'''
    return [] # there are none as yet

def count_required_items(campaign: Campaign, options: DawnsburyOptions) -> int:
    '''Count how many required drops are in a given campaign'''

    # Get campaign drops
    per_character, single = campaign.get_required_campaign_drops(options)
    
    # Add any campaign-independent drops
    per_character += get_settings_required_per_character_drops(options)
    single += get_settings_required_single_drops(options)

    # Expand the list, and remove any excluded items
    all_drops = single + make_character_item_names(per_character, DEFAULT_CHARACTERS)
    all_drops = remove_excluded_item_names(all_drops)

    # Count them
    return len(all_drops)

def make_filler_items(campaign: Campaign, amount: int, player: int, options: DawnsburyOptions) -> List[DawnsburyItem]:
    '''Make the required filler items to bulk out a campaign'''

    # If "traps" is on, replace all filler items with traps instead
    filler = list(generate_traps(player, min(options.traps.value, amount)))

    # Try to get some per-character filler items
    remaining = amount - len(filler)
    filler += list(islice(generate_campagin_character_filler(campaign, options, player), remaining))
    
    # Add campaign filler items
    remaining = amount - len(filler)
    filler += list(generate_campaign_item_filler(campaign, remaining, options, player)) 

    # Add Non-Campaign filler
    remaining = amount - len(filler)
    filler += list(islice(generate_non_campaign_filler(player), remaining))

    # TODO: we must remove excluded items from the filler pool, if ever there are any.

    # Return the filler
    return filler

def generate_traps(player: int, max_amount: int) -> Generator[DawnsburyItem, None, None]:
    '''Generator to create trap items up to a maximum amount'''
    for _ in range(max_amount):
        yield create_item(random.choice(trap_items), player)

def generate_non_campaign_filler(player: int) -> Generator[DawnsburyItem, None, None]:
    '''Generator to create filler items indefinitley'''
    while True:
        yield create_item("Loot Bag", player)

def generate_campagin_character_filler(campaign: Campaign, options: DawnsburyOptions, player: int) -> Generator[DawnsburyItem, None, None]:
    '''Try to generate per-character filler items, in sets of 4.'''
    
    # These are generated in sets of 4, so we must yield from the resultant array
    for item in campaign.filler_character_drop_generator(options):
        items = create_items_for_each_character(item, campaign.characters, player)
        random.shuffle(items) # Shuffle the list, in case we get cut off early
        yield from items

def generate_campaign_item_filler(campaign: Campaign, max_amount: int, options: DawnsburyOptions, player: int) -> Generator[DawnsburyItem, None, None]:
    '''Try to generate non-character filler items, not exceeding max_amount.'''

    for item in islice(campaign.filler_single_item_drop_genertator(options), max_amount):
        yield create_item(item, player)

def determine_amount_of_items(campaign: Campaign, options: DawnsburyOptions) -> int:
    '''For a given generation, determine how many items to include'''

    num_encounters = campaign.num_encounters - 1 # "Final" encounter has its own special item
    required_items = count_required_items(campaign, options)

    # If we have an "Extra" encounter setting, we must include extra filler items
    if (options.include_free_encounters == 3 or options.include_free_encounters == 4 ):    
        filler = (campaign.num_levels() * options.extra_filler_amount.value)
        return max(num_encounters + filler, required_items)
    
    # Otherwise, simply return the greater of the required item and encounter counts
    return max(num_encounters, required_items)


# Items which are 'required' to progress the game
progression_items = [
    GAME_COMPLETE,
    "Level Up",
    "Attack",
    "Cast Spell",
    "Move",
    "Interact",
]

# Items which are 'useful' to progress the game
useful_items = [
    "Level Up",
    "Weapon Upgrade",
    "Armor Upgrade",
    "Skill Upgrade",
    "Attack",
    "Cast Spell",
    "Move",
    "Interact",
]

def classify_item(name: str) -> ItemClassification:
    '''Categorize a given item into archipealgo terms, such as progression, useful, or filler'''

    # By default, items are filler (0)
    type = ItemClassification.filler # 0

    # Check if the name contains the name of any item type in the progression list
    #  eg, if "Level Up (Annacoesta)" contains "Level Up"
    if any(map(lambda x: x in name, progression_items)):
        type = type | ItemClassification.progression # 1
    
    # Liekwise for "useful" item types
    if any(map(lambda x: x in name, useful_items)):
        type = type | ItemClassification.useful # 2
    
    # "Trap" items all have "Trap" in their name
    if "Trap" in name:
        type = type | ItemClassification.trap # 4

    return type
