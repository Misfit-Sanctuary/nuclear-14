using Robust.Shared.Prototypes;
using Content.Shared.Construction.Prototypes;
using Content.Shared.MedicalScanner;
using Robust.Shared.Containers;

namespace Content.Server.Medical.Components
{
    [RegisterComponent]
    public sealed partial class MedicalScannerComponent : SharedMedicalScannerComponent
    {
        public const string ScannerPort = "MedicalScannerReceiver";
        public ContainerSlot BodyContainer = default!;
        public EntityUid? ConnectedConsole;

        [ViewVariables(VVAccess.ReadWrite)]
        public float CloningFailChanceMultiplier = 1f;

        public float MetemKarmaBonus = 0.25f;

        [DataField]
        public ProtoId<MachinePartPrototype> MachinePartCloningFailChance = "Capacitor";

        [DataField]
        public float PartRatingFailMultiplier = 0.75f;
    }
}
