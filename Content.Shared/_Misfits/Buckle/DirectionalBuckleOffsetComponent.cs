using System.Numerics;

namespace Content.Shared._Misfits.Buckle;

/// <summary>
/// Positions buckled entities with a different on screen offset for each cardinal
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DirectionalBuckleOffsetComponent : Component
{

    [DataField(tag: "offsets", required: true)]
    public Dictionary<Direction, Vector2>[] YamlOffsets = [];
    [ViewVariables(VVAccess.ReadOnly)]
    public Vector2[,] Offsets = default!;

    [DataField,AutoNetworkedField]
    public List<EntityUid> BuckleOffsetSlots = new();

}
