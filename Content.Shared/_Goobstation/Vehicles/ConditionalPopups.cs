using JetBrains.Annotations;
namespace Content.Shared.Vehicles;


[UsedImplicitly]
public sealed partial class ConditionalPopups : IPopup
{
    [DataField]
    public ICondition[] Conditions = [];

    [DataField]
    public string FailMsg = "";
    public string FailPopup() => FailMsg;
    [DataField]
    public string SuccessMsg = "";

    public string SucceedPopup() => SuccessMsg;
}
