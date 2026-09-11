from typing import Generator, List, Tuple
from .Settings import DawnsburyOptions

DEFAULT_CHARACTERS = ["Annacoesta", "Scarlet", "Tok'dar", "Saffi"]

class Campaign():
    def __init__(self, name: str, encounter_count: int,
                 start_level: int, end_level: int,
                 encounters_per_level: list[int],
                 characters: list[str] = DEFAULT_CHARACTERS,
                 allow_bonus_encounters: bool = True):
        self.name = name
        self.characters = characters # This probably cant ever not be the default bc of how items are defined, but just in case its here
        self.num_encounters = encounter_count
        self.start_level = start_level
        self.end_level = end_level

        # Number of progressive weapon bonuses players start/end with (None, +1, Striking, +2 etc)
        self.start_atk_bonus = get_attack_bonus_at_level(start_level)
        self.end_atk_bonus = get_attack_bonus_at_level(end_level)

        # Number of progressive armor bonuses players start/end with (None, +1, Resilient, +2 etc)
        self.start_armor_bonus = get_armor_bonus_at_level(start_level)
        self.end_armor_bonus = get_armor_bonus_at_level(end_level)

        # Number of progressive item skill check bonuses players start/end with
        self.start_skill_bonus = get_skill_bonus_at_level(start_level)
        self.end_skill_bonus = get_skill_bonus_at_level(end_level)

        # Number of progressive perception bonuses players start/end with
        self.start_perception_bonus = get_perception_bonus_at_level(start_level)
        self.end_perception_bonus = get_perception_bonus_at_level(end_level)

        # Save the encounter list
        self.encounters_per_level = encounters_per_level
        self.allow_bonus_encounters = allow_bonus_encounters

    def get_required_campaign_drops(self, settings: DawnsburyOptions) -> Tuple[List[str], List[str]]:
        '''Using the provided settings, filter and return a complete list of all required items to be dropped in the campaign.
           Returns two lists: the first is items which need to be duplicated for each player, and the second is items that are as they are'''
        per_character_drops = []
        
        # Add the requisite number of level ups to the list
        if (settings.level_ups):
            per_character_drops += ["Level Up"] * (self.end_level - self.start_level)

        # Check if the settings tell us to include automatic item bonuses
        if (settings.item_bonuses.value > 0):

            # Add Weapon rune increases
            per_character_drops += ["Weapon Upgrade"] * (self.end_atk_bonus - self.start_atk_bonus)

            # Add Armor rune increases
            per_character_drops += ["Armor Upgrade"] * (self.end_armor_bonus - self.start_armor_bonus)

        # Make all singleton drops (there currently are none)
        singleton_drops = []
        
        return (per_character_drops, singleton_drops)
    
    def filler_character_drop_generator(self, settings: DawnsburyOptions) -> Generator[str, None, None]:
        '''Using the provided settings, generate all the per-character filler items that can be dropped in the campaign.'''

        # Check if the settings tell us to include automatic item bonuses
        # TODO: figure out what to do with these in "manual" bonus runs
        if (settings.item_bonuses.value == 1):

            # Yield all Perception item increases
            for _ in range(0, self.end_perception_bonus - self.start_perception_bonus):
                yield "Perception Upgrade"

            # Yield all Skill item increases
            for _ in range(0, self.end_skill_bonus - self.start_skill_bonus):
                yield "Skill Upgrade"


        # Yield anything else, when added

    def filler_single_item_drop_genertator(self, settings: DawnsburyOptions) -> Generator[str, None, None]:
        '''Using the provided settings, generate all one-off items that can be dropped by the campaign'''

        # Currently none
        yield from []


    def num_levels(self):
        return self.end_level + 1 - self.start_level

def get_attack_bonus_at_level(level: int) -> int:
    '''Determine the number of progressive weapon bonuses players should have at this level (None, +1, Striking, +2 etc)'''
    if   level < 2:  return 0
    elif level < 4:  return 1
    elif level < 10: return 2
    elif level < 12: return 3
    elif level < 16: return 4
    elif level < 19: return 5
    else:            return 6

def get_armor_bonus_at_level(level: int) -> int:
    '''Determine the number of progressive weapon bonuses players should have at this level (None, +1, Reslilent, +2 etc)'''
    if   level < 6:  return 0 # Should be 5, but this way it drops during profane barrier
    elif level < 8:  return 1
    elif level < 11: return 2
    elif level < 14: return 3
    elif level < 18: return 4
    elif level < 20: return 5
    else:            return 6

def get_skill_bonus_at_level(level: int) -> int:
    '''Determine the item bonus to skills the players should have at this level'''
    # Note: Abp gives one extra bonus at threshold levels, selected like a feat. we jsut do a global bonus insetad.
    if   level < 3:  return 0
    elif level < 9:  return 1
    elif level < 17: return 2
    else:            return 3

def get_perception_bonus_at_level(level: int) -> int:
    '''Determine the item bonus to perception the players should have at this level'''
    if   level < 7:  return 0
    elif level < 13: return 1
    elif level < 19: return 2
    else:            return 3

def get_chosen_campaign(options: DawnsburyOptions) -> Campaign:
    '''Determine what campaign(s) are selected in the options.'''
    if options.campaign.value < 100:
        return Game_Campaigns[options.campaign.value]
    else:
        return Other_Campaigns[options.campaign.value - 100]

def make_campaign_metadata(options: DawnsburyOptions) -> dict[str, object]:
    '''Package the campaign metadata that the mod needs to run.'''
    campaign = get_chosen_campaign(options)
    return {
        'start_level': campaign.start_level,
        'end_level': campaign.end_level,
        'start_atk_bonus': campaign.start_atk_bonus,
        'start_armor_bonus': campaign.start_armor_bonus,
        'start_skill_bonus': campaign.start_skill_bonus,
        'start_perception_bonus': campaign.start_perception_bonus,
        'end_atk_bonus': campaign.end_atk_bonus,
        'end_armor_bonus': campaign.end_armor_bonus,
        'end_skill_bonus': campaign.end_skill_bonus,
        'end_perception_bonus': campaign.end_perception_bonus,
        'num_encounters': campaign.num_encounters
    }

def merge_campaings(campaigns: list[Campaign]) -> Campaign:
    '''Given multiple campaigns, combine their metadata into a single merged campaign'''

    # Merge the metadata appropriateley to its type
    name = " and ".join(map(lambda c: c.name, campaigns)) # Campaign 1 and Campaign 2
    characters = campaigns[0].characters
    num_encounters = sum(map(lambda c: c.num_encounters, campaigns))
    start_level = min(map(lambda c: c.start_level, campaigns))
    end_level = max(map(lambda c: c.end_level, campaigns))

    # Merge the encounter list
    encounters_per_level = []
    for campaign in campaigns:
        encounters_per_level += campaign.encounters_per_level

    # Create and return the resultant campaign
    return Campaign(name, num_encounters, start_level, end_level, encounters_per_level, characters)

def get_max_encoutners_and_levels() -> tuple[int, int]:
    '''Get the maximum number of encounters and levels in the supported campaigns'''
    max_encounters, max_levels = (0, 0)
    for campaign in Game_Campaigns + Other_Campaigns:
        max_encounters = max(campaign.num_encounters, max_encounters)
        max_levels = max(campaign.num_levels(), max_levels)
    return max_encounters, max_levels

### Game Campaigns ###
DawnsburyDays: Campaign = Campaign(
    "Dawnsbury Days", 21, 1, 4, [5, 6, 5, 5])

ProfaneBarrier: Campaign = Campaign(
    "The Profane Barrier", 24, 5, 8, [7, 5, 7, 5])

GoodLittleChildren: Campaign = Campaign(
    "Good Little Children", 5, 9, 9, [5]) # cant do much with this by iteself, but who knows

Game_Campaigns: List[Campaign] = [
    DawnsburyDays,
    ProfaneBarrier,
    merge_campaings([DawnsburyDays, ProfaneBarrier]),
    GoodLittleChildren,
    merge_campaings([DawnsburyDays, ProfaneBarrier, GoodLittleChildren])
]

### Other Campaigns ###
RoguelikeMode: Campaign = Campaign(
    "Roguelike Mode", 28, 1, 8, [4, 4, 4, 3, 4, 4, 4, 1], allow_bonus_encounters=False)

Other_Campaigns: List[Campaign] = [
    RoguelikeMode
]