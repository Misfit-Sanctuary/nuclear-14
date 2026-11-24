using Robust.Shared.Prototypes;
using Content.Shared.Construction.Components;
using Content.Shared.Construction.Prototypes;
using Robust.Shared.Containers;

namespace Content.Server.Construction.Components
{
    [RegisterComponent]
    public sealed partial class MachineFrameComponent : Component
    {
        public const string PartContainerName = "machine_parts";
        public const string BoardContainerName = "machine_board";

        [ViewVariables]
        public bool HasBoard => BoardContainer?.ContainedEntities.Count != 0;

        [DataField("progress")]
        public Dictionary<ProtoId<MachinePartPrototype>, int> Progress = new();

        [ViewVariables]
        public readonly Dictionary<string, int> MaterialProgress = new();

        [ViewVariables]
        public readonly Dictionary<string, int> ComponentProgress = new();

        [ViewVariables]
        public readonly Dictionary<string, int> TagProgress = new();

        [DataField("requirements")]
        public Dictionary<ProtoId<MachinePartPrototype>, int> Requirements = new();

        [ViewVariables]
        public Dictionary<string, int> MaterialRequirements = new();

        [ViewVariables]
        public Dictionary<string, GenericPartInfo> ComponentRequirements = new();

        [ViewVariables]
        public Dictionary<string, GenericPartInfo> TagRequirements = new();

        [ViewVariables]
        public Container BoardContainer = default!;

        [ViewVariables]
        public Container PartContainer = default!;
    }
}
