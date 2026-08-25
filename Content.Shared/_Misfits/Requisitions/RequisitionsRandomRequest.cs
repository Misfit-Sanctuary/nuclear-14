using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Requisitions;

[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class RequisitionsRandomSlot
{
    [DataField]
    public RequisitionsRandomRequest? Request;

    [DataField]
    public TimeSpan NextRollAt;
}

[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class RequisitionsRandomRequest
{
    [DataField]
    public List<RequisitionsRandomRequestTarget> Targets = new();

    [DataField]
    public int Score;

    [DataField]
    public bool IsHard;

    [DataField]
    public bool DirectBudget;

    [DataField]
    public Dictionary<string, int> RewardItems = new();

    [DataField]
    public TimeSpan RerollAvailableAt;
}

[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class RequisitionsRandomRequestTarget
{
    [DataField]
    public bool IsReagent;

    [DataField(required: true)]
    public string TargetId = string.Empty;

    [DataField]
    public int Amount = 1;

    [DataField]
    public int Progress;
}
