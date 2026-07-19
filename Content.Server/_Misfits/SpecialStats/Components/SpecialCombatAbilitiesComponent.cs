namespace Content.Server._Misfits.SpecialStats.Components;

/// <summary>
/// Tracks combat ability actions granted by high physical SPECIAL stats,
/// plus the tuning values for those abilities.
/// </summary>
[RegisterComponent]
public sealed partial class SpecialCombatAbilitiesComponent : Component
{
    [DataField]
    public int Threshold = 8;

    [DataField]
    public string ChargeAction = "ActionSpecialCharge";

    [DataField]
    public string ParryAction = "ActionSpecialParry";

    [DataField]
    public string CrippleAction = "ActionSpecialCripple";

    public EntityUid? ChargeActionEntity;
    public EntityUid? ParryActionEntity;
    public EntityUid? CrippleActionEntity;

    /// <summary>
    /// Maximum dash distance in tiles.
    /// </summary>
    [DataField]
    public float ChargeRange = 5f;

    /// <summary>
    /// Dash velocity passed to the throwing system.
    /// </summary>
    [DataField]
    public float ChargeSpeed = 15f;

    [DataField]
    public TimeSpan ParryWindow = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Stun applied to a melee attacker whose hit was parried.
    /// </summary>
    [DataField]
    public TimeSpan ParryStunTime = TimeSpan.FromSeconds(1.5);

    [DataField]
    public TimeSpan CrippleDuration = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Walk/sprint speed multiplier while crippled (0.25 = 75% slow).
    /// </summary>
    [DataField]
    public float CrippleSpeedMultiplier = 0.25f;
}
