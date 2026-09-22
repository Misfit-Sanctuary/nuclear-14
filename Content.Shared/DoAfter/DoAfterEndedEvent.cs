namespace Content.Shared.DoAfter;

/// <summary>
/// Raised on a DoAfters user whenever any DoAfter they were running ends, successfully or not.
/// </summary>
[ByRefEvent]
public readonly record struct DoAfterEndedEvent(EntityUid User, EntityUid? Target, bool Cancelled);
