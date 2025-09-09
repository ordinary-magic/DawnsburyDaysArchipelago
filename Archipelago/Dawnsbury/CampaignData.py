from typing import List, Tuple
from .Options import DawnsburyOptions

DEFAULT_CHARACTERS = ["Annacoesta", "Scarlet", "Tok'dar", "Saffi"]

class Campaign():
    def __init__(self, name: str, encounter_count: int, start_level: int, end_level: int, start_atk_bonus: int, end_atk_bonus: int, 
                 potion_loot: List[Tuple[str, int]], scroll_loot: List[Tuple[str, int]],
                 weapon_loot: List[Tuple[str, int]], tool_loot: List[Tuple[str, int]],
                 characters: str = DEFAULT_CHARACTERS):
        self.name = name
        self.characters = characters # This probably cant ever not be the default bc of how items are defined, but just in case its here
        self.num_encounters = encounter_count
        self.start_level = start_level
        self.end_level = end_level

        # Number of progressive weapon bonuses players start/end with (None, +1, Striking, +2 etc)
        self.start_atk_bonus = start_atk_bonus
        self.end_atk_bonus = end_atk_bonus

        # Armor bonuses not in dd, will add if i do dlc later

        # Loot awarded during the adventure, sorted by type. Includes the amount of each item.
        self.potion_loot = potion_loot
        self.scroll_loot = scroll_loot
        self.weapon_loot = weapon_loot
        self.tool_loot = tool_loot
        # possibly add a misc category later if needed (eg aeon stones)

    def get_all_campaign_drops(self, settings: DawnsburyOptions) -> tuple[list[str], list[str]]:
        '''Using the provided settings, filter and return a complete list of all items to be dropped in the campaign.
           Returns two lists: the first is items which need to be duplicated for each player, and the second is items that are as they are'''
        return self.get_per_character_drops(settings), self.get_singe_drops(settings)
        

    def get_per_character_drops(self, settings: DawnsburyOptions) -> List[str]:
        '''Return a list of drops which are per-character (eg Annocesta's Level Up)'''
        drops = []
        
        # Add the requisite number of level ups to the list
        drops += ["Level Up"] * (self.end_level - self.start_level)

        # Add Weapon rune increases
        drops += ["Weapon Upgrade"] * (self.end_atk_bonus - self.start_atk_bonus)
        
        return drops

    def get_singe_drops(self, settings: DawnsburyOptions) -> List[str]:
        '''Return a list of standard item drops (Eg. +1 Longsword)'''
        return [] # Currently we dont randomize any of these

    
    def get_maximum_amount_of_drops(self) -> int:
        '''Assuming the most generous settings, what is the maximum possible number of drops this campaign can yield'''
        return 4 * (self.end_atk_bonus - self.start_atk_bonus + self.end_level - self.start_level) +\
            len(self.potion_loot) + len(self.scroll_loot) + len(self.weapon_loot) + len(self.tool_loot)

def get_chosen_campaign(options: DawnsburyOptions) -> Campaign:
    '''Determine what campaign(s) are selected in the options.'''

    # Currently we only support GC, so just return that.
    return GoldenCandelabra

def make_campaign_metadata(options: DawnsburyOptions) -> dict[str, object]:
    '''Package the campaign metadata that the mod needs to run.'''
    campaign = get_chosen_campaign(options)
    return {
        'start_level': campaign.start_level,
        'end_level': campaign.end_level,
        'start_atk_bonus': campaign.start_atk_bonus,
        'num_encounters': campaign.num_encounters
    }

GoldenCandelabra: Campaign = Campaign(
    "The Quest for the Golden Candelabra", 21, 1, 4, 0, 2,
    [
        ("Healing Potion (Minor)", 3),
        ("Healing Potion (Lesser)", 13),
        ("Healing Potion (Moderate)", 3),
        ("Barkskin Potion", 2),
        ("Potion of Invisibility", 3),
        ("Bottled Omen", 4),
        ("Fluid Movement Elixr", 2),
    ],
    [
        ("Scroll of Burning Hands", 1),
        ("Scroll of Heal", 3),
        ("Scroll of Bless", 2),
        ("Scroll of Bane", 1),
        ("Scroll of Flaming Sphere", 2),
        ("Scroll of Summon Elemental", 2),
        ("Scroll of Summon Animal", 1),
        ("Scroll of Harm", 2),
        ("Scroll of Sudden Blight", 1),
        ("Scroll of Grease", 1),
        ("Scroll of Invisibility", 1),
        ("Scroll of Dimension Door", 1),
        ("Scroll of Slow", 1),
        ("Scroll of Resist Energy", 1),
    ],
    [
        ("Orc Necksplitter", 1),
        ("+1 Orc Necksplitter", 1),
        ("+1 Rapier", 1),
        ("+1 Striking Rapier", 1),
        ("+1 Longsword", 1),
        ("+1 Morningstar", 1),
        ("+1 Earthbreaker", 1),
        ("+1 Heavy Crossbow", 1),
        ("+1 Striking Shorbow", 1),
        ("+1 Sickle", 1),
        ("+1 Striking Greatsword", 1),
        ("+1 Kukri", 1),
        ("+1 Striking Trident", 2),
    ],
    [
        ("Expanded Healer's Tools", 1),
    ])

All_Campaigns: List[Campaign] = [
    GoldenCandelabra,
]