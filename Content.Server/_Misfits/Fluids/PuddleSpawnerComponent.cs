using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;


namespace Content.Server._Misfits.Fluids;

[RegisterComponent]
public sealed partial class PuddleSpawnerComponent : Component
{
    [DataField(required: true), ViewVariables(VVAccess.ReadOnly)]
    public ProtoId<ReagentPrototype> ReagentToSpawn = "blood";
    [DataField]
    public FixedPoint2 Amount = 5;
    [DataField]
    public TimeSpan Seconds = new TimeSpan(30);
    [DataField]
    public FixedPoint2 MaxAmount = 5;

    public Dictionary<EntityUid, FixedPoint2> TrackedAmount = new();
    public TimeSpan NextUpdate = new TimeSpan(30);
}
