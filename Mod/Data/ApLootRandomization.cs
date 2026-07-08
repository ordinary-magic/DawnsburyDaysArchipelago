namespace DawnsburyArchipelago.Data;

/**
    * Enum defining the archipelago loot randomization settings
    */ 
public enum ApLootRandomization
{
    None,
    SameTypeAndLevel,
    SameType,
    SameLevel,
    Any,
}

public static class ApLootRandomizationExtensions {
    public static bool KeepLevel(this ApLootRandomization rule) =>
        rule == ApLootRandomization.SameLevel ||
        rule == ApLootRandomization.SameTypeAndLevel;

    public static bool KeepType(this ApLootRandomization rule) =>
        rule == ApLootRandomization.SameType ||
        rule == ApLootRandomization.SameTypeAndLevel;
}