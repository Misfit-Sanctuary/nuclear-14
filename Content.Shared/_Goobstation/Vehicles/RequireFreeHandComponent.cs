namespace Content.Shared.Vehicles;

[RegisterComponent, NetworkedComponent]
public sealed partial class RequireFreeHandComponent : Component
{
    [DataField("requiredHands")]
    public int RequiredHands = 0;
}
