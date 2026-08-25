using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Requisitions.Prototypes;

[Prototype]
public sealed partial class RequisitionsRewardPoolPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public List<RequisitionsRewardItemEntry> Items = new();
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class RequisitionsRewardItemEntry
{
    [DataField(required: true)]
    public EntProtoId Item;

    [DataField(required: true)]
    public float Cost;

    [DataField]
    public int MinAmount = 1;

    [DataField]
    public int MaxAmount = 1;

    [DataField]
    public float Weight = 1f;
}
