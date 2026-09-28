using System.Numerics;

namespace Content.Server._Misfits.Weapons.Throwable;

/// <summary>
/// Marks a bottle as able to receive a cloth wick and records which solution becomes its fuel.
/// </summary>
[RegisterComponent]
public sealed partial class MolotovConvertibleComponent : Component
{
    /// <summary>
    /// The bottle's solution-container ID.
    /// </summary>
    [DataField]
    public string Solution = "drink";

    /// <summary>
    /// Per-bottle adjustment for aligning the wick with the neck of its sprite.
    /// Values are in world units; a 32-pixel sprite uses 1/32 unit per pixel.
    /// </summary>
    [DataField]
    public Vector2 WickOffset;
}
