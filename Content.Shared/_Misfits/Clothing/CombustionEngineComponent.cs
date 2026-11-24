using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Clothing;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CombustionEngineComponent : Component
{
    [DataField]
    public string FuelSolution = "fuel";

    [DataField]
    public ProtoId<ReagentPrototype> FuelReagent = "WeldingFuel";

    [DataField]
    public FixedPoint2 FuelUsePerSecond = FixedPoint2.New(0.5f);

    [DataField]
    public SoundSpecifier RefillSound = new SoundPathSpecifier("/Audio/Effects/refill.ogg");

    [ViewVariables]
    public float FuelAccumulator;

    [ViewVariables, AutoNetworkedField]
    public bool EngineRunning = false;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class DelayedExplosionOnBreakComponent : Component
{
    [DataField]
    public float Delay = 3f;

    [DataField]
    public bool Triggered;
}

[Serializable, NetSerializable]
public enum CombustionEngineVisualLayers : byte
{
    Body,
}

[Serializable, NetSerializable]
public enum CombustionEngineVisuals : byte
{
    Running,
}