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

    [DataField]
    public string KeenEyeAction = "ActionSpecialKeenEye";

    [DataField]
    public string RallyAction = "ActionSpecialRally";

    [DataField]
    public string LuckyBreakAction = "ActionSpecialLuckyBreak";

    public EntityUid? ChargeActionEntity;
    public EntityUid? ParryActionEntity;
    public EntityUid? CrippleActionEntity;
    public EntityUid? KeenEyeActionEntity;
    public EntityUid? RallyActionEntity;
    public EntityUid? LuckyBreakActionEntity;

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

    /// <summary>
    /// Stun applied to a mob the user collides with mid-charge.
    /// </summary>
    [DataField]
    public TimeSpan ChargeStaggerTime = TimeSpan.FromSeconds(0.5);

    /// <summary>
    /// Incoming damage multiplier while the charge lunge is in flight.
    /// </summary>
    [DataField]
    public float ChargeDamageMultiplier = 0.5f;

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

    /// <summary>
    /// Eye zoom while scoped with keen eye (>1 widens the view).
    /// </summary>
    [DataField]
    public float KeenEyeZoom = 1.8f;

    /// <summary>
    /// How long it takes to leave the keen eye stance.
    /// </summary>
    [DataField]
    public TimeSpan KeenEyeExitDelay = TimeSpan.FromSeconds(2);

    [DataField]
    public float RallyRange = 6f;

    [DataField]
    public TimeSpan RallyDuration = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Fractional movement speed bonus for rallied allies (0.2 = +20%).
    /// </summary>
    [DataField]
    public float RallySpeedBonus = 0.2f;

    [DataField]
    public int LuckyBreakBoost = 4;

    [DataField]
    public TimeSpan LuckyBreakDuration = TimeSpan.FromSeconds(6);
}
