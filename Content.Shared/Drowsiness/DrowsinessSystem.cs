using Robust.Shared.Prototypes;
using Content.Shared.StatusEffect;

namespace Content.Shared.Drowsiness;

public abstract class SharedDrowsinessSystem : EntitySystem
{
    public static readonly ProtoId<StatusEffectPrototype> DrowsinessKey = "Drowsiness";
}
