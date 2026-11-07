using Content.Shared.Damage.Prototypes;

namespace Content.Server.Vehicles;

[RegisterComponent, NetworkedComponent]
public sealed partial class DamageOnConsumeReagentComponent : Component
{
    [DataField]
    public int DmgPerInvalidReagent = 10;
    [DataField]
    public ProtoId<DamageTypePrototype> DmgType = "EngineDamage";
}
