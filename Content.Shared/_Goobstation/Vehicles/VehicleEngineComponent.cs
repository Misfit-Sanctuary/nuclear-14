using Content.Shared.EntityEffects;


namespace Content.Shared.Vehicles;

[RegisterComponent, NetworkedComponent]
public sealed partial class VehicleEngineComponent : Component
{
    [DataField]
    public bool RequestOnByDefault = true;
    [DataField]
    public bool RequestOffByDefault = true;
    [DataField]
    public EntityEffectCondition[] Conditions = [];
}
