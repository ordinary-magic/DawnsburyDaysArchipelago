from BaseClasses import CollectionState, MultiWorld
from .Items import create_item, GAME_COMPLETE
from .Locations import get_last_location
from .Options import DawnsburyOptions

def set_rules(world: MultiWorld, player: int, options: DawnsburyOptions):
    '''Function used to tell the randomizer algorithm what rules our games randomization must follow.'''

    ### Required Unlock Rules ###
    # We dont currently enforce any requirements for loot/levels in the generator algorithm,
    #  but if we wanted to, for instance, add expected levels to encounters we would put them here.

    ### Victory Conditions ###
    # Create a special 'victory' item, and place it on the final encounter
    world.get_location(get_last_location(options), player).place_locked_item(create_item(GAME_COMPLETE, player))
    world.completion_condition[player] = lambda state: state.has(GAME_COMPLETE, player)

    ### DEBUG ### - makes a cool uml diagram in the archipelago folder
    #from Utils import visualize_regions
    #visualize_regions(world.get_region("Menu", player), "dawnsbury.puml")

# Note: the wrappers are needed because afaik we need to be able to reference the player id to check items
# Note2: Cant do this unless archipelago is randomizing the level order, so not used currently.
def get_required_level_rule(player: int, required_level_ups: int):
    '''Get a rule function to check if we have enough level ups to handle an encounter'''
    
    def rule(state: CollectionState) -> bool:
        return state.has("Level Up", player, required_level_ups)
    return rule
