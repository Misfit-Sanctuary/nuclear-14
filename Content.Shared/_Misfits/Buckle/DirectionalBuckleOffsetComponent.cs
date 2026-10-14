using System.Numerics;

namespace Content.Shared._Misfits.Buckle;

/// <summary>
/// Positions buckled entities with a different on screen offset for each cardinal
/// </summary>
[RegisterComponent]
public sealed partial class DirectionalBuckleOffsetComponent : Component
{
    [DataField]
    public Dictionary<Direction, Vector2> Offsets = new();
}
