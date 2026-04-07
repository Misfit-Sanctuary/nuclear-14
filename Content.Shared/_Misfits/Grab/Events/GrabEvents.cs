// #Misfits Add - Events used by the grab stage escalation system
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Grab;

/// <summary>
/// Raised on the target (pulled) entity to attempt a grab stage escalation.
/// Cancel() to block the grab from happening (e.g. blocked by martial-arts override).
/// The grab system sets Cancel() when it successfully handles the grab so the melee system
/// skips the normal light attack.
/// </summary>
[ByRefEvent]
public struct GrabAttemptEvent
{
    public EntityUid Puller;
    public EntityUid Target;
    public bool Cancelled;

    public GrabAttemptEvent(EntityUid puller, EntityUid target)
    {
        Puller = puller;
        Target = target;
        Cancelled = false;
    }

    public void Cancel() => Cancelled = true;
}

/// <summary>
/// Raised on the target when a grab stage escalation is about to happen.
/// Handlers (e.g. martial arts) can override the resulting grab stage.
/// </summary>
[ByRefEvent]
public struct CheckGrabOverridesEvent
{
    public EntityUid Performer;
    public EntityUid Target;
    public GrabStage RequestedStage;
    /// <summary>If set by a handler, use this stage instead of the default.</summary>
    public GrabStage? OverrideStage;

    public CheckGrabOverridesEvent(EntityUid performer, EntityUid target, GrabStage requested)
    {
        Performer = performer;
        Target = target;
        RequestedStage = requested;
        OverrideStage = null;
    }
}

/// <summary>
/// Raised on the puller after a grab stage change is committed.
/// </summary>
public sealed class GrabStageChangedEvent : EntityEventArgs
{
    public EntityUid Puller;
    public EntityUid Pulled;
    public GrabStage OldStage;
    public GrabStage NewStage;

    public GrabStageChangedEvent(EntityUid puller, EntityUid pulled, GrabStage old, GrabStage newStage)
    {
        Puller = puller;
        Pulled = pulled;
        OldStage = old;
        NewStage = newStage;
    }
}

/// <summary>
/// Raised on both puller and pulled when the grab is fully released (returns to GrabStage.No).
/// </summary>
public sealed class GrabReleasedEvent : EntityEventArgs
{
    public EntityUid Puller;
    public EntityUid Pulled;
    public GrabStage WasStage;

    public GrabReleasedEvent(EntityUid puller, EntityUid pulled, GrabStage was)
    {
        Puller = puller;
        Pulled = pulled;
        WasStage = was;
    }
}
