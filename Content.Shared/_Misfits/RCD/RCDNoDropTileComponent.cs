using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Misfits.RCD;

/// <summary>
/// Tracks RCD-constructed tiles that should not return construction resources when removed.
/// </summary>
[RegisterComponent]
public sealed partial class RCDNoDropTileComponent : Component
{
    [DataField]
    public Dictionary<Vector2i, string> Tiles = new();
}
