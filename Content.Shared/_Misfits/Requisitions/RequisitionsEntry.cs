using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._Misfits.Requisitions;

[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class RequisitionsEntry
{
    [DataField]
    public LocId? Name;

    [DataField]
    public LocId? Description;

    [DataField]
    public SpriteSpecifier? Icon;

    [DataField(required: true)]
    public int Cost;

    [DataField]
    public int Stock = -1;

    [DataField(required: true)]
    public EntProtoId Crate;

    [DataField]
    public List<EntProtoId> Entities = new();

    [DataField]
    public Dictionary<string, int> Contents = new();
}

[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class RequisitionsSellEntry
{
    [DataField(required: true)]
    public EntProtoId Item;

    [DataField]
    public LocId? Name;

    [DataField]
    public int Value;

    [DataField]
    public List<EntProtoId> Exchange = new();
}

[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class RequisitionsBounty
{
    [DataField(required: true)]
    public string Id = string.Empty;

    [DataField]
    public LocId? Name;

    [DataField(required: true)]
    public EntProtoId Item;

    [DataField]
    public int Amount = 1;

    [DataField]
    public int Reward;

    [DataField]
    public EntProtoId? RewardCrate;

    [DataField]
    public bool Repeatable;
}
