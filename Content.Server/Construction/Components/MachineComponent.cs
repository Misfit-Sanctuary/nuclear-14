using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server.Construction.Components
{
    [RegisterComponent, ComponentProtoName("Machine")]
    public sealed partial class MachineComponent : Component
    {
        [DataField("board")]
        public EntProtoId? BoardPrototype { get; private set; }

        [ViewVariables]
        public Container BoardContainer = default!;
        [ViewVariables]
        public Container PartContainer = default!;
    }

    /// <summary>
    /// The different types of scaling that are available for machine upgrades
    /// </summary>
    public enum MachineUpgradeScalingType : byte
    {
        Linear,
        Exponential
    }
}
