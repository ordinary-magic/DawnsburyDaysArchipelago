namespace DawnsburyArchipelago.Data;

/**
 * Enum defining the item types we get from the archipelago server per-character.
 *   for each of these, there are 4 sub items - eg. Level Up (Annacoesta)
 */
public enum ApPerCharacterItemTypes
{
    // Per character items (4 each) //
    LevelUp,
    WeaponImprovement,
    ArmorImprovement,
    SkillImprovement,
    PerceptionImprovement,
    Attack,
    Cast,
    Move,
    Interact,
    END // Not real, just used for dynamically updaing the list length
}

/**
 * Enum defining the item types from the archipelago server, for which there is only one item.
 */
public enum ApSingletonItemTypes
{
    // Singleton Items //

    // Note: "Game Complete" item is obsoleted
    GAME_COMPLETE = ApPerCharacterItemTypes.END * 4, // start immediatley after the end of the last list
    LootBag,

    // Trap Items //
    ClumsyTrap,
    EnfeeblingTrap,
    StupifyingTrap,
    TripTrap,
    FearTrap,
    SickeningTrap,
    SkipTurnTrap,
    GlueTrap,
    ButterfingersTrap,
    ExplosiveTrap,
    DoomTrap,
    WarpTrap,
    FlashpowderTrap,
}