using Content.Shared.Chemistry.Reagent;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Requisitions.Prototypes;

[Prototype]
public sealed partial class RequisitionsRequestPoolPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public List<RequisitionsItemRequestEntry> Items = new();

    [DataField]
    public List<RequisitionsMaterialRequestEntry> Materials = new();

    [DataField]
    public List<RequisitionsReagentRequestEntry> Reagents = new();
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class RequisitionsItemRequestEntry
{
    [DataField(required: true)]
    public EntProtoId Item;

    [DataField]
    public int MinAmount = 1;

    [DataField]
    public int MaxAmount = 1;

    [DataField(required: true)]
    public float ValuePerUnit;

    [DataField]
    public float Weight = 1f;
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class RequisitionsMaterialRequestEntry
{
    [DataField(required: true)]
    public ProtoId<StackPrototype> Material;

    [DataField]
    public int MinAmount = 1;

    [DataField]
    public int MaxAmount = 1;

    [DataField(required: true)]
    public float ValuePerUnit;

    [DataField]
    public float Weight = 1f;

    [DataField]
    public bool DirectBudget;
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class RequisitionsReagentRequestEntry
{
    [DataField(required: true)]
    public ProtoId<ReagentPrototype> Reagent;

    [DataField]
    public int MinAmount = 1;

    [DataField]
    public int MaxAmount = 1;

    [DataField(required: true)]
    public float ValuePerUnit;

    [DataField]
    public float Weight = 1f;
}
