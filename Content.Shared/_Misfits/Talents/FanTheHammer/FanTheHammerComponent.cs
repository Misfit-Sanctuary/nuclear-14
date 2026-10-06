using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Misfits.Talents.FanTheHammer;

[RegisterComponent, NetworkedComponent, Access(typeof(FanTheHammerSystem))]
[AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class FanTheHammerComponent : Component
{
    [DataField]
    public EntProtoId Action = "ActionFanTheHammer";

    [DataField, AutoNetworkedField]
    public EntityUid? ActionEntity;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(10);

    [DataField]
    public float FireRateMultiplier = 2f;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan? ActiveUntil;
}

public sealed partial class FanTheHammerActionEvent : InstantActionEvent;
