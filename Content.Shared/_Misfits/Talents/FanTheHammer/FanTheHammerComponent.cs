using Content.Shared.Actions;
using Content.Shared.StatusEffect;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.Talents.FanTheHammer;

[RegisterComponent, NetworkedComponent, Access(typeof(FanTheHammerSystem))]
public sealed partial class FanTheHammerActionComponent : Component
{
    [DataField]
    public ProtoId<StatusEffectPrototype> StatusEffect = "FanTheHammer";

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(10);

    [DataField]
    public float FireRateMultiplier = 2f;
}

[RegisterComponent, NetworkedComponent, Access(typeof(FanTheHammerSystem))]
[AutoGenerateComponentState]
public sealed partial class FanTheHammerComponent : Component
{
    [DataField, AutoNetworkedField]
    public float FireRateMultiplier = 2f;
}

public sealed partial class FanTheHammerActionEvent : InstantActionEvent;
