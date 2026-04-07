using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared.Tiles;

/// <summary>
/// Stores the original subfloor turf under constructed floor tiles so deconstruction can restore it.
/// </summary>
[RegisterComponent]
public sealed partial class PreservedTileUnderlayComponent : Component
{
    [DataField]
    public Dictionary<Vector2i, string> Underlays = new();
}