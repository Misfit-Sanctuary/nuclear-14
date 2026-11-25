using Robust.Shared.Prototypes;
using Content.Shared.Construction.Prototypes;

namespace Content.Server.Xenoarchaeology.Equipment.Components;

/// <summary>
/// This is used for a machine that biases
/// an artifact placed on it to move up/down
/// </summary>
[RegisterComponent]
public sealed partial class TraversalDistorterComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite)]
    public float BiasChance;

    [DataField]
    public float BaseBiasChance = 0.7f;

    [DataField]
    public ProtoId<MachinePartPrototype> MachinePartBiasChance = "Manipulator";

    [DataField]
    public float PartRatingBiasChance = 1.1f;

    [ViewVariables(VVAccess.ReadWrite)]
    public BiasDirection BiasDirection = BiasDirection.Up;

    public TimeSpan NextActivation = default!;
    public TimeSpan ActivationDelay = TimeSpan.FromSeconds(1);
}

public enum BiasDirection : byte
{
    Up, //Towards depth 0
    Down, //Away from depth 0
}
