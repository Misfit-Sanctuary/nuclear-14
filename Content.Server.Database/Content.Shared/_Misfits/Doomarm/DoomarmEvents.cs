// #Misfits Add - Doomarm events.

using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Doomarm;

/// <summary>
/// Kinetic Discharge: charge up, then lunge at the clicked spot.
/// </summary>
public sealed partial class DoomarmLungeActionEvent : WorldTargetActionEvent;

/// <summary>
/// Groundbreaker: leap toward the clicked spot and slam the 3x3 where you land.
/// </summary>
public sealed partial class DoomarmSlamActionEvent : WorldTargetActionEvent;

/// <summary>
/// Orbital Descent: go up a z-level, steer, crash down on a 5x5.
/// </summary>
public sealed partial class DoomarmDescentActionEvent : InstantActionEvent;

/// <summary>
/// Finishes the Kinetic Discharge charge-up. Carries the clicked spot as <see cref="NetCoordinates"/>
/// because <see cref="EntityCoordinates"/> can't be network-serialized.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class DoomarmLungeDoAfterEvent : SimpleDoAfterEvent
{
    [DataField]
    public NetCoordinates TargetCoordinates;

    public DoomarmLungeDoAfterEvent() { }

    public DoomarmLungeDoAfterEvent(NetCoordinates target)
    {
        TargetCoordinates = target;
    }
}

/// <summary>
/// Server → clients: play the Groundbreaker hop animation on this entity.
/// </summary>
[Serializable, NetSerializable]
public sealed class DoomarmHopEvent : EntityEventArgs
{
    public NetEntity User;
    public float Duration;

    public DoomarmHopEvent(NetEntity user, float duration)
    {
        User = user;
        Duration = duration;
    }
}
