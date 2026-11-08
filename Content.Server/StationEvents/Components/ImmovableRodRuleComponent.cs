using Content.Server.StationEvents.Events;
using Robust.Shared.Prototypes;

namespace Content.Server.StationEvents.Components;

[RegisterComponent, Access(typeof(ImmovableRodRule))]
public sealed partial class ImmovableRodRuleComponent : Component
{
    [DataField("rodPrototype")]
    public EntProtoId RodPrototype = "ImmovableRodKeepTilesStill";
}
