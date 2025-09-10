from .CampaignData import make_campaign_metadata
from .Items import BASE_OFFSET, create_item, create_items, ap_get_all_items, DawnsburyItem
from .Locations import location_resolver_cache
from .Options import make_option_slot_data, DawnsburyOptions
from .Regions import create_regions
from .Rules import set_rules
from ..AutoWorld import World

class DawnsburyWorld(World):
    """
    Dawnsbury Days
    """
    options_dataclass = DawnsburyOptions
    options: DawnsburyOptions
    game = "Dawnsbury Days"
    settings: None
    topology_present = False
    required_client_version = (0, 3, 7)

    # Required name/id resolution dicts for the superclass
    item_name_to_id = ap_get_all_items()
    location_name_to_id = location_resolver_cache

    def create_item(self, name: str) -> DawnsburyItem:
        return create_item(name, self.player)

    def create_items(self):
        self.multiworld.itempool += create_items(self.player, self.options)

    def create_regions(self):
        self.multiworld.regions += create_regions(self.multiworld, self.player, self.options)

    def set_rules(self):
        set_rules(self.multiworld, self.player, self.options)

    def fill_slot_data(self) -> dict:
        slot_data = make_option_slot_data(self.options)
        slot_data.update(make_campaign_metadata(self.options))
        slot_data['base_offset'] = BASE_OFFSET
        slot_data['version'] = 10100 # 1.1.0 (2 digits per)
        return slot_data
