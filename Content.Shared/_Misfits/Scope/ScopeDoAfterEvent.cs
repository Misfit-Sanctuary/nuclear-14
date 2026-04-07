// #Misfits Add - DoAfter event for directional scoping (carries the cardinal direction)
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Scope;

/// <summary>
/// DoAfter event that fires when the scoping-in delay completes.
/// Carries the cardinal direction the user was facing when they started scoping.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class ScopeDoAfterEvent : SimpleDoAfterEvent
{
    /// <summary>
    /// The cardinal direction the user was facing when they initiated scoping.
    /// </summary>
    public Direction Direction;

    public ScopeDoAfterEvent(Direction direction)
    {
        Direction = direction;
    }
}
