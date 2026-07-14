using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.Requisitions.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedRequisitionsSystem))]
public sealed partial class RequisitionsInvoiceComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Reward = 100;

    [DataField]
    public EntProtoId PaperOutput = "N14PaperRequisitionInvoice";

    [DataField]
    public string? RequiredStamp = "paper_stamp-approve";
}
