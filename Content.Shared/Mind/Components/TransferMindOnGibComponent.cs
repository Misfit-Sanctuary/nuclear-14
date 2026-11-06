using Robust.Shared.Prototypes;
using Content.Shared.Tag;

namespace Content.Shared.Mind.Components;

[RegisterComponent]
public sealed partial class TransferMindOnGibComponent : Component
{
    [DataField("targetTag")]
    public ProtoId<TagPrototype> TargetTag = "MindTransferTarget";
}
