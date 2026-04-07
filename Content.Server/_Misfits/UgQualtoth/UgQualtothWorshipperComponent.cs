// #Misfits Add — Component for a tribal character who worships Ug-Qualtoth.
// Tracks devotion accumulation, current transformation stage, and in-prayer state.

using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Misfits.UgQualtoth;

/// <summary>
/// Applied to a tribal entity that has begun worshipping Ug-Qualtoth.
/// Tracks devotion points and transformation stage (0 = untouched, 4 = full abomination).
/// </summary>
[RegisterComponent]
public sealed partial class UgQualtothWorshipperComponent : Component
{
    // ── Devotion tracking ───────────────────────────────────────────────────

    /// <summary>Total devotion accumulated by this worshipper.</summary>
    [DataField]
    public float Devotion = 0f;

    /// <summary>Current transformation stage (0–4).</summary>
    [DataField]
    public int Stage = 0;

    // ── Prayer cooldown ─────────────────────────────────────────────────────

    /// <summary>
    /// Game time when the worshipper may next pray. Null means praying is allowed immediately.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan? NextPrayAllowedAt;

    /// <summary>Cooldown between completed prayers.</summary>
    [DataField]
    public TimeSpan PrayCooldown = TimeSpan.FromMinutes(5);

    /// <summary>Devotion gained per successful prayer.</summary>
    [DataField]
    public float PrayDevotionGain = 5f;

    // ── In-prayer state (set by UgQualtothSystem, not persisted across rounds) ──

    /// <summary>
    /// Game time when the current prayer started. Null when not praying.
    /// Set the moment the DoAfter begins; cleared on completion or cancellation.
    /// </summary>
    [DataField]
    public TimeSpan? PrayingStartedAt;

    /// <summary>
    /// How many timed flavor-text messages have been sent during the current prayer (0–3).
    /// Reset to 0 whenever a new prayer begins.
    /// </summary>
    [DataField]
    public int PrayFlavoursSent;

    // ── Proximity ambient message cooldown ──────────────────────────────────

    /// <summary>
    /// Game time before the next proximity ambient message may be shown.
    /// Null = may show immediately on first approach.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan? NextProximityMessageAt;

    /// <summary>Cooldown between proximity ambient messages.</summary>
    [DataField]
    public TimeSpan ProximityCooldown = TimeSpan.FromMinutes(2);

    // ── Stage thresholds ────────────────────────────────────────────────────

    [DataField] public float Stage1Threshold = 20f;
    [DataField] public float Stage2Threshold = 60f;
    [DataField] public float Stage3Threshold = 120f;
    [DataField] public float Stage4Threshold = 250f;

    // ── Stage 4 species change ──────────────────────────────────────────────

    /// <summary>Species prototype ID to switch the worshipper into at Stage 4.</summary>
    [DataField]
    public string AbominationSpecies = "UgQualtothAbomination";

    // ── Speed bonuses per stage (stacked) ───────────────────────────────────

    [DataField]
    public float SpeedBonusPerStage = 0.10f;
}
