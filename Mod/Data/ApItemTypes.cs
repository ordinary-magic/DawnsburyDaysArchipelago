namespace DawnsburyArchipelago.Data;

 /// <summary>
 ///  Enum defining the item types we get from the archipelago server that can be per-character.
///     for each of these, there are 5 sub items - one for each PC - eg. Level Up (Annacoesta)
///     and one for the group - eg. Level Up
///   Note: Previously, there were only 4 of each of these, so we must check the protocol version when using them. 
 /// </summary>
public enum ApPerCharacterItemTypes
{
    // Per character items (5 each) //
    LevelUp,
    WeaponImprovement,
    ArmorImprovement,
    SkillImprovement,
    PerceptionImprovement,
    Attack,
    Cast,
    Move,
    Interact,
    END, // not used by the protocol, but a handy reference for spacing
}

/// <summary>
/// Items from archipelago which are not character specific.
/// This enum is for the preivous version of the list, before the protocol changed.
/// </summary>
public enum ApSingletonItemTypesv1
{
    // Singleton Items //

    // Note: "Game Complete" item is obsoleted in more recent versions of the v1 protocol
    GAME_COMPLETE = 36, // start immediatley after the end of the per-character list (was 9 entries, 4 each in this version)
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

/// <summary>
/// Items from archipelago which are not character specific.
/// This enum is for the current version of the list, and starts much later.
/// </summary>
public enum ApSingletonItemTypes
{
    // not-real spacing item. start well after the previous list.
    START = 999,

    GAME_COMPLETE, // obsolete, but still used by ap, so we add it for spacing
    LootBag,
    
    END,
}

/// <summary>
/// Items from archipelago which are "trap" items.
/// </summary>
public enum ApTrapItemTypes
{
    // not-real spacing item. start well after the previous list.
    START = 1999,
    
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
    PoisonDartTrap,

    END,
}


/// <summary>
/// Items from archipelago which are "boon" items.
/// </summary>
public enum ApBoonItemTypes
{
    // not-real spacing item. start well after the previous list.
    START = 2999,
    
    BoonOfHealth,
    BoonOfFastHealing,
    DivineBlessing,
    BoonOfLuck,
    BoonOfAlacrity,
    SupernaturalBattelcry,
    ForestsProtection,
    BoonOfImmortality,
    BoonOfInvulnerability,

    END,
}

public static class ApItemTypeExtensions
{
    /// <summary>
    /// Extension method to try to convert an archipealgo item id (int) to a per character item, accounting for protocol version
    /// </summary>
    /// <param name="id">The integer id of the ap item (after subtracting the base id)</param>
    /// <param name="protocolVersion">The protocol version sent by the server (for converting)</param>
    /// <param name="type">The type of the item found.</param>
    /// <param name="slpot">The party slot the item belogns to (1-4), or 0 for everyone.</param>
    /// <returns>True/false if the item is a per character item.</returns>
    public static bool IsPerCharacterItem(this int id, int protocolVersion, out ApPerCharacterItemTypes type, out int slot)
    {
        // default to the newer version
        type = (ApPerCharacterItemTypes) (id / 5);
        slot = id % 5;

        // Invalid item
        if (id < 0)
            return false;

        // Check if we're on the new protocol version, and the id is on the list.
        if (!protocolVersion.IsOld() && id < (int)ApPerCharacterItemTypes.END * 5)
            return true;

        // Old version, check if it's less than the singleton items
        if (protocolVersion.IsOld() && id < (int)ApSingletonItemTypesv1.GAME_COMPLETE)
        {
            // four per slot in old vesrsion
            type = (ApPerCharacterItemTypes) (id / 4); 
            slot = (id % 4) + 1; // incrmenet by 1 since "everyone" is 0 now
            return true;
        }

        return false;
    }
    
    /// <summary>
    /// Extension method to try to convert an archipealgo item id (int) to a singleton item
    /// </summary>
    /// <param name="id">The integer id of the ap item (after subtracting the base id)</param>
    /// <param name="protocolVersion">The protocol version sent by the server (for converting)</param>
    /// <returns>The singleton item type, or null if wasn't one. </returns>
    public static ApSingletonItemTypes? ConvertToSingletonItem(this int id, int protocolVersion)
    {
        // start by just casting it
        var result = (ApSingletonItemTypes) id;

        // Check if we're on the new protocol version, and the id is in a valid range
        if (!protocolVersion.IsOld() && result > ApSingletonItemTypes.START && result < ApSingletonItemTypes.END)
            return result;

        // If old protocl version, we must convert it first
        if (protocolVersion.IsOld() && id == (int)ApSingletonItemTypesv1.LootBag)
            return ApSingletonItemTypes.LootBag; // only one possibility

        return null; // unknown or non-singleton item
    }

    /// <summary>
    /// Extension method to try to convert an archipealgo item id (int) to a trap item type
    /// </summary>
    /// <param name="id">The integer id of the ap item (after subtracting the base id)</param>
    /// <param name="protocolVersion">The protocol version sent by the server (for converting)</param>
    /// <returns>The trap item type, or null if wasn't one. </returns>
    public static ApTrapItemTypes? ConvertToTrapItem(this int id, int protocolVersion)
    {
        // start by just casting it
        var result = (ApTrapItemTypes) id;

        // Check if we're on the new protocol version, and the id is in a valid range
        if (!protocolVersion.IsOld() && result > ApTrapItemTypes.START && result < ApTrapItemTypes.END)
            return result;

        // If old protocl version, we must convert it first
        if (protocolVersion.IsOld() && id >= (int)ApSingletonItemTypesv1.ClumsyTrap && id <= (int)ApSingletonItemTypesv1.FlashpowderTrap)
            return (ApTrapItemTypes)(id - (int)ApSingletonItemTypesv1.ClumsyTrap + (int)ApTrapItemTypes.START + 1);

        return null; // unknown or non-singleton item
    }
    
    /// <summary>
    /// Extension method to try to convert an archipealgo item id (int) to a trap item type
    /// </summary>
    /// <param name="id">The integer id of the ap item (after subtracting the base id)</param>
    /// <returns>The boon item type, or null if wasn't one. </returns>
    public static ApBoonItemTypes? ConvertToBoonItem(this int id)
    {
        // start by just casting it
        var result = (ApBoonItemTypes) id;

        // Check if we're on the new protocol version, and the id is in a valid range
        if (result > ApBoonItemTypes.START && result < ApBoonItemTypes.END)
            return result;

        return null; // unknown or non-singleton item
    }

    // is it an old protocol version
    private static bool IsOld(this int protocolVersion) => protocolVersion < 10600;
}