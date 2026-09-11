using Content.Shared._Misfits.Genetics.Mutations;
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

    /// <summary>
    /// When set, only a genetics disk carrying a mutation of this rarity counts toward the
    /// request. A blank disk is the same prototype as a researched one, so without this a
    /// lathe-printed blank would complete the bounty.
    /// </summary>
    [DataField]
    public MutationRarity? MutationRarity;
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
