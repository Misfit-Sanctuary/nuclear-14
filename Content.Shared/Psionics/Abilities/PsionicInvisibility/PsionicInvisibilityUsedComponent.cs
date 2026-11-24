using Robust.Shared.Prototypes;
namespace Content.Shared.Abilities.Psionics
{
    [RegisterComponent]
    public sealed partial class PsionicInvisibilityUsedComponent : Component
    {
        public static readonly EntProtoId PsionicInvisibilityUsedActionPrototype = "ActionPsionicInvisibilityUsed";
        [DataField("psionicInvisibilityUsedActionId")]
        public EntProtoId? PsionicInvisibilityUsedActionId = "ActionPsionicInvisibilityUsed";

        [DataField("psionicInvisibilityUsedActionEntity")]
        public EntityUid? PsionicInvisibilityUsedActionEntity;
    }
}
