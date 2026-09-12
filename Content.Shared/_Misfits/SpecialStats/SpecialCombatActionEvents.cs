using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.SpecialStats;

/// <summary>
/// Dashes the user toward the clicked location. Granted at Agility 8+.
/// </summary>
public sealed partial class SpecialChargeActionEvent : WorldTargetActionEvent;

/// <summary>
/// Opens a short parry window reflecting projectiles and negating melee. Granted at Endurance 8+.
/// </summary>
public sealed partial class SpecialParryActionEvent : InstantActionEvent;

/// <summary>
/// Melee strike that heavily slows the target. Granted at Strength 8+.
/// </summary>
public sealed partial class SpecialCrippleActionEvent : EntityTargetActionEvent;

/// <summary>
/// Toggles a zoomed-out scoped stance that immobilizes the user. Granted at Perception 8+.
/// </summary>
public sealed partial class SpecialKeenEyeActionEvent : InstantActionEvent;

/// <summary>
/// Do-after fired when the user finishes leaving the keen eye stance.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class SpecialKeenEyeStopDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
/// Buffs the movement speed of the user and nearby same-faction allies. Granted at Charisma 8+.
/// </summary>
public sealed partial class SpecialRallyActionEvent : InstantActionEvent;

/// <summary>
/// Temporarily boosts the user's Luck. Granted at Luck 8+.
/// </summary>
public sealed partial class SpecialLuckyBreakActionEvent : InstantActionEvent;
