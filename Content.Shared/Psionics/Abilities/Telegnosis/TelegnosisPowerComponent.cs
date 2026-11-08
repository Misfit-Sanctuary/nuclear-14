using Content.Shared.Actions;
using Robust.Shared.Prototypes;


namespace Content.Shared.Abilities.Psionics
{
    [RegisterComponent]
    public sealed partial class TelegnosisPowerComponent : Component
    {
        [DataField("prototype")]
        public string Prototype = "MobObserverTelegnostic";
        public InstantActionComponent? TelegnosisPowerAction = null;
        public static readonly EntProtoId TelegnosisActionPrototype = "ActionTelegnosis";
        [DataField("telegnosisActionId")]
        public EntProtoId? TelegnosisActionId = "ActionTelegnosis";

        [DataField("telegnosisActionEntity")]
        public EntityUid? TelegnosisActionEntity;
    }
}