namespace Content.Shared.Vehicles;

[RegisterComponent, NetworkedComponent]
public sealed partial class VehicleEngineTurnOnConditionsComponent : Component
{
    [DataField(required: true)]
    public ConditionalPopups[] ConditionPopups = [];
}
