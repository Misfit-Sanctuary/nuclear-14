// #Misfits Add - DoAfter event for directional scoping
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Scope;

/// Fires when the scoping-in delay completes; the direction is resolved from the user's facing at completion.
[Serializable, NetSerializable]
public sealed partial class ScopeDoAfterEvent : SimpleDoAfterEvent;
