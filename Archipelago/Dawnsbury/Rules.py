from BaseClasses import CollectionState, MultiWorld # pyright: ignore[reportMissingImports]
from ..generic.Rules import add_rule # type: ignore
from .CampaignData import get_chosen_campaign
from .Items import create_item, get_all_item_names, remove_excluded_item_names, GAME_COMPLETE
from .Locations import get_last_location_name
from .Settings import DawnsburyOptions
from .Regions import make_level_region_name

def set_rules(world: MultiWorld, player: int, options: DawnsburyOptions):
    '''Function used to tell the randomizer algorithm what rules our games randomization must follow.'''

    ### Victory Conditions ###
    # Create a special 'victory' item, and place it on the final encounter
    world.get_location(get_last_location_name(), player).place_locked_item(create_item(GAME_COMPLETE, player))
    world.completion_condition[player] = lambda state: state.has(GAME_COMPLETE, player)

    ### Region Access Rules ###
    campaign = get_chosen_campaign(options)

    # For every region exit in the campaign (1 fewer than the number of levels)
    for i in range(1, campaign.num_levels()):
        region = make_level_region_name(i + campaign.start_level) # Region name referrs to the next region
        entrance = world.get_entrance(region, player)
        
        # Add the level up rule for every region exit but the first
        if i > 1 and options.level_ups:
            # Require one level less than the level of the incoming region
            add_rule(entrance, get_required_level_rule(player, options, i - 1))
        
        # Add required action unlocks if that setting is enabled
        if options.locked_actions.value > 0 and options.per_character:
            add_rule(entrance, get_required_unlocks_rule(player, options, i))
    
    ### DEBUG ### - makes a cool uml diagram in the archipelago folder
    #from Utils import visualize_regions
    #visualize_regions(world.get_region("Menu", player), "dawnsbury.puml")

def get_required_level_rule(player: int, options: DawnsburyOptions, required_level_ups: int):
    '''Get a rule function to check if we have enough level ups to handle an encounter'''

    # Get a list of level up items for each character (eg. "Level Up (Tok'Dar)")
    level_up_items = list(filter(lambda s: "Level Up" in s, get_all_item_names()))

    # Filter either the "Party" or PC specific level-ups
    if options.per_character:
        level_up_items = [item for item in level_up_items if "Party" not in item]
    else:
        level_up_items = [item for item in level_up_items if "Party" in item]

    def rule(state: CollectionState) -> bool:
        # Ensure we have the requisite number of level ups for each character
        for name in level_up_items:
            if not state.has(name, player, required_level_ups):
                return False
        return True
        
    return rule

def get_required_unlocks_rule(player: int, options: DawnsburyOptions, region_num: int):
    '''Get a rule which requires at least some basic abilities to be unlocked.
        region_num indicates the region where these unlocks should be, ie, the region we are exiting for this rule (as an index, not a level).'''

    # Get the locking item names
    items = remove_excluded_item_names(get_all_item_names())
    attack = list(filter(lambda s: "Attack" in s, items))
    spell = list(filter(lambda s: "Cast Spell" in s, items))
    move = list(filter(lambda s: "Move" in s, items))
    interact = list(filter(lambda s: "Interact" in s, items))

    # List of abilities which are especially useful to award based on build assumptions
    especially_useful_abilities = ["Attack (Tok'Dar)", "Attack (Scarlet)", "Cast Spell (Annacoesta)", "Cast Spell (Saffi)"]

    if (region_num == 1):
        # If this is the first access rule, we want to try and force especially useful unlocks
        def rule(state: CollectionState) -> bool:
            # Check that we have at least 2 espeically useful abilities
            if not state.has_from_list(especially_useful_abilities, player, 2):
                return False
            
            # Check that someone can move
            if options.locked_actions.value == 2 and not state.has_any(move, player):
                return False
            
            # If we met every rule, return true
            return True
        
        return rule

    else:
        # Otherwise, create a generic access rule
        def rule(state: CollectionState) -> bool:

            # Check for a cumulative two attack/spell unlocks per region
            if not state.has_from_list(attack + spell, player, min(8, 2 * region_num)):
                return False
            
            # Check for a cumulative two interact/move unlocks (minus the free one)
            if options.locked_actions.value == 2 and not \
                    state.has_from_list(move + interact, player, min(7, (2 * region_num) - 1)):
                return False
            
            # Every condition was met
            return True
        
        return rule