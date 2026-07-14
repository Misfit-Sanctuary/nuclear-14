using Content.Shared.Damage;
using Content.Shared.Mobs;

namespace Content.Server.Silicon.WeldingHealable;

[RegisterComponent]
public sealed partial class WeldingHealableComponent : Component
{
    [DataField]
    public HashSet<MobState>? AllowedStates;

    [DataField]
    public DamageSpecifier? Damage;
}

