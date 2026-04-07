// #Misfits Add - Event raised when scoping completes successfully
using Robust.Shared.GameObjects;

namespace Content.Shared._Misfits.Scope;

/// <summary>
/// Raised directed on the user entity when they finish scoping in.
/// </summary>
[ByRefEvent]
public record struct ScopedEvent(EntityUid User, EntityUid Scope);
