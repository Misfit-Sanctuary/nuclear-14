using Robust.Shared.Prototypes;
using Content.Shared.Construction.Prototypes;

namespace Content.Server.Bed.Components
{
    [RegisterComponent]
    public sealed partial class StasisBedComponent : Component
    {
        [DataField]
        public float BaseMultiplier = 10f;

        /// <summary>
        ///     What the metabolic update rate will be multiplied by (higher = slower metabolism)
        /// </summary>
        [ViewVariables(VVAccess.ReadOnly)] // Writing is is not supported. ApplyMetabolicMultiplierEvent needs to be refactored first
        [DataField]
        public float Multiplier = 10f;

        [DataField]
        public ProtoId<MachinePartPrototype> MachinePartMetabolismModifier = "Capacitor";
    }
}
