using Content.Shared.Actions;

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
