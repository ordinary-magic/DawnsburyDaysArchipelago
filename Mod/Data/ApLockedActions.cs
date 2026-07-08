namespace DawnsburyArchipelago.Data;

/**
    * Enum defining which actions the player must unlock
    */ 
public enum ApLockedActions
{
    None,
    Standard,
    Extreme,
}

public static class ApLockedActionsExtension
{
    public static bool LockBasicActions(this ApLockedActions rule) =>
        rule == ApLockedActions.Standard ||
        rule == ApLockedActions.Extreme;
}