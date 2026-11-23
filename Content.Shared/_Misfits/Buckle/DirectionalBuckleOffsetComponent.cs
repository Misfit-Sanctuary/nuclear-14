using System.Numerics;

namespace Content.Shared._Misfits.Buckle;

/// <summary>
/// Positions buckled entities with a different on screen offset for each cardinal
/// NOTE diagonals are rotated. I have not figured out how to correct that yet
///
/// Offsets ordered by strap slots and then ordered by direction(from 0-7)
/// first index is the "spot" in the car(first is usually driver, then shotgun, passnger ect...)
/// second index is number representing a direction the car faces(south is 0, SouthEast 1 ect...) <see cref="Direction"=>
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DirectionalBuckleOffsetComponent : Component
{
    /// <summary>
    /// Directional Offsets based on 8 cardinal directions(north, south ect...) so includes diagonals
    /// dictionary to make yaml easier
    /// </summary>
    [DataField(tag: "offsets", required: true, readOnly: true)]
    public Dictionary<Direction, Vector2>[] YamlOffsets = [];
    /// <summary>
    /// Actual place where the system retrieves the offsets to set positions.
    /// needs to be a list and not a dict to preserve ordering
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public Vector2[,] Offsets = default!;
    /// <summary>
    /// Represents the state of a list of strap "slots" with a directional offset
    /// it is either empty(InvalidEntityUid) or full(some entUid)
    /// We find the 1st index for Offsets this way
    /// </summary>
    [DataField,AutoNetworkedField]
    public List<EntityUid> BuckleOffsetSlots = new();

    [DataField]
    public bool EnableLerping = true;

}
