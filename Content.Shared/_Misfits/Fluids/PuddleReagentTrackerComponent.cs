using Content.Shared.Chemistry.Reagent;


namespace Content.Shared._Misfits.Fluids;

[RegisterComponent]
public sealed partial class PuddleReagentTrackerComponent : Component
{
    [DataField(required: true), ViewVariables(VVAccess.ReadOnly)]
    public ProtoId<ReagentPrototype> ReagentId = "blood";

    public EntityUid Manager = EntityUid.Invalid;

}
