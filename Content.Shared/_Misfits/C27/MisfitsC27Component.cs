namespace Content.Shared._Misfits.C27;

// #Misfits Add - Marker placed on every C-27 humanoid robot mob entity. Carries the per-species
// stat tunables (EMP damage, EMP stun duration) consumed by MisfitsC27EmpSystem on the server.
// Not networked — purely server-side EMP handling and config; everything visible to the client
// (movement speed, melee, immunities) is set by sibling components on the same entity.
[RegisterComponent]
public sealed partial class MisfitsC27Component : Component
{
    /// <summary>
    ///     Shock damage dealt by a full-strength pulse grenade. Weaker EMP sources scale down
    ///     from this ceiling using the same energy-based strength as other synthetic targets.
    /// </summary>
    [DataField]
    public float MaxEmpShockDamage = 100f;

    /// <summary>
    ///     Multiplier applied to external silicon repair do-afters, such as welder repairs
    ///     and cable-coil wire repairs.
    /// </summary>
    [DataField]
    public float SiliconRepairDelayMultiplier = 1f;

    /// <summary>
    ///     If true, the mob is also added to the EmpDisabled stun pool (forced to drop, can't
    ///     interact for the pulse duration). Spec calls for "possible PA-style stun" — opt-in
    ///     via this flag in case it proves too punishing in playtest.
    /// </summary>
    [DataField]
    public bool ApplyEmpStun = true;
}
