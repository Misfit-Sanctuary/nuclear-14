using Robust.Shared.Prototypes;
using Content.Shared.Construction.Prototypes;

namespace Content.Server.Power.Components
{

    [RegisterComponent]
    public sealed partial class UpgradeBatteryComponent : Component
    {
        /// <summary>
        ///     The machine part that affects the power capacity.
        /// </summary>
        [DataField]
        public ProtoId<MachinePartPrototype> MachinePartPowerCapacity = "PowerCell";

        /// <summary>
        ///     The machine part rating is raised to this power when calculating power gain
        /// </summary>
        [DataField]
        public float MaxChargeMultiplier = 2f;

        /// <summary>
        ///     Power gain scaling
        /// </summary>
        [DataField]
        public float BaseMaxCharge = 8000000;
    }
}
