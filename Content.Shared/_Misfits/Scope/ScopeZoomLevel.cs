// #Misfits Add - Configurable zoom level record for directional scoping system (ported from RMC-14)
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Scope;

/// <summary>
/// Describes a single zoom level configuration for a scope.
/// Scopes with multiple zoom levels can be cycled via action.
/// </summary>
[DataRecord, Serializable, NetSerializable]
public partial record struct ScopeZoomLevel(
    /// <summary>
    /// Display name shown in the popup when cycling through zoom levels.
    /// Null for scopes with only one zoom level.
    /// </summary>
    string? Name,

    /// <summary>
    /// Camera zoom multiplier when scoped in. 1.0 = normal zoom.
    /// </summary>
    float Zoom,

    /// <summary>
    /// How many tiles to offset the user's view by when scoping in a cardinal direction.
    /// </summary>
    float Offset,

    /// <summary>
    /// If true, the user can move while scoped without breaking the scope.
    /// </summary>
    bool AllowMovement,

    /// <summary>
    /// How long the DoAfter takes to scope in at this zoom level.
    /// </summary>
    TimeSpan DoAfter
);
