using Content.Shared.Weapons.Reflect;

namespace Content.Server._Misfits.SpecialStats.Components;

/// <summary>
/// Present while a parry window is active. Stores the previous reflect
/// state so an existing innate ReflectComponent is restored, not clobbered.
/// </summary>
[RegisterComponent]
public sealed partial class SpecialParryActiveComponent : Component
{
    public TimeSpan EndTime;
    public TimeSpan StunTime;

    /// <summary>
    /// Melee attacker whose incoming damage should be negated. Set by the
    /// AttackedEvent handler and consumed by the DamageModifyEvent handler
    /// in the same melee-attack call stack.
    /// </summary>
    public EntityUid? PendingNegateAttacker;

    public bool HadReflect;
    public ReflectType PrevReflects;
    public float PrevProb;
    public Dictionary<ReflectType, float>? PrevProbByType;
}
