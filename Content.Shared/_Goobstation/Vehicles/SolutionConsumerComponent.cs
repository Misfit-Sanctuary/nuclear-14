using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;


namespace Content.Shared.Vehicles;

[RegisterComponent, NetworkedComponent]
public sealed partial class SolutionConsumerComponent : Component
{
    [DataField]
    public string FuelSolution = "motorbikeFuel";

    [DataField]
    public List<ProtoId<ReagentPrototype>> AllowedReagents = new() {"WeldingFuel"};

    [DataField]
    public FixedPoint2 FuelUsePerSecond = FixedPoint2.New(0.13f);
}

[Serializable, NetSerializable]
public enum MotorbikeVisuals : byte
{
    Burning,
}

[Serializable, NetSerializable]
public sealed partial class MotorbikeRefuelDoAfterEvent : SimpleDoAfterEvent;
