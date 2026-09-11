using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.Areas;

[RegisterComponent]
[Access(typeof(AreaSystem), typeof(AreasCommand))]
public sealed partial class AreaGridComponent : Component
{
    [DataField]
    public Dictionary<Vector2i, EntProtoId<AreaComponent>> Areas = new();

    [DataField]
    public Dictionary<EntProtoId<AreaComponent>, EntityUid> AreaEntities = new();
}
