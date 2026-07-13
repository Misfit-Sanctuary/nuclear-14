using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Misfits.Robot;

/// <summary>
/// Adds a charge-up phase before the Assaultron's weapon fires.
/// On first shot attempt the system cancels the shot and starts a charge timer.
/// After the charge completes the next shot attempt succeeds. A cooldown then
/// prevents further shots for a configured duration.
/// </summary>
[RegisterComponent] // #Misfits Fix - Removed NetworkedComponent: no AutoGenerateComponentState → MissingMetadataException
public sealed partial class AssaultronBeamChargeComponent : Component
{
    /// <summary>How long the charge-up phase lasts before the weapon can fire.</summary>
    [DataField]
    public float ChargeDuration = 2f;

    /// <summary>Cooldown after a successful shot before charging can begin again.
    /// Combined with ChargeDuration this determines the total cycle time (ROF).</summary>
    [DataField]
    public float CooldownDuration = 3f;

    /// <summary>Locale key for the emote broadcast when charging starts.</summary>
    [DataField]
    public string ChargeEmoteLocale = "assaultron-charge-emote";

    /// <summary>Locale key for the emote broadcast when the weapon fires.</summary>
    [DataField]
    public string FireEmoteLocale = "assaultron-beam-fire-emote";

    /// <summary>Minimum seconds between repeated emote broadcasts.
    /// Prevents chat spam when continuously firing.</summary>
    [DataField]
    public float EmoteCooldown = 20f;

    /// <summary>Amount of charge drained from the robot's cell_slot battery
    /// each time the weapon fires. 0 = no drain.</summary>
    [DataField]
    public float FireDrainCharge = 360f;

    /// <summary>ItemSlot ID to pull the battery from.</summary>
    [DataField]
    public string CellSlotId = "cell_slot";

    // --- Runtime state (not serialised) ---

    public bool IsCharging;
    public bool ReadyToFire;
    public TimeSpan ChargeEndTime;
    public TimeSpan CooldownEndTime;

    /// <summary>
    /// Tracks whether the emitter enabled combat mode as a targeting indicator.
    /// </summary>
    public bool ForcedCombatMode;

    /// <summary>Next time a charge emote is allowed to broadcast.</summary>
    public TimeSpan NextChargeEmoteTime;
    /// <summary>Next time a fire emote is allowed to broadcast.</summary>
    public TimeSpan NextFireEmoteTime;
}

/// <summary>Raised directed on the Assaultron weapon when charge-up begins. Server handles emote.</summary>
[ByRefEvent]
public record struct AssaultronChargeStartedEvent(EntityUid User, string EmoteLocale);

/// <summary>Raised directed on the Assaultron weapon when the beam/projectile fires. Server handles emote.</summary>
[ByRefEvent]
public record struct AssaultronBeamFiredEvent(EntityUid User, string EmoteLocale);

/// <summary>
/// Raised directed when charge-up has completed and the next shot is about to be allowed.
/// Server-only systems can cancel to enforce checks that shared code cannot perform.
/// </summary>
[ByRefEvent]
public record struct AssaultronBeamPreFireCheckEvent(bool Cancelled = false);
