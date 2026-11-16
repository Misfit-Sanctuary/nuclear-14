// #Misfits Add - Generic SPECIAL stat requirement gate for items
using Content.Shared._Misfits.Special;
using Robust.Shared.Localization;

namespace Content.Shared._Misfits.SpecialStats.Components;

/// Refuses wielding, use-in-hand and firing unless the user's effective SPECIAL stats meet every minimum.
/// Generalises the old super-heavy weapon marker, which hardcoded a single Strength check:
/// <code>
/// - type: N14SpecialStatRequirement
///   minimumStats:
///     Strength: 7
///   bypassWithPowerArmor: true
/// </code>
/// Values are compared against the clamped effective stat (1-10, neutral 5), so drug and clothing modifiers count.
[RegisterComponent]
public sealed partial class N14SpecialStatRequirementComponent : Component
{
    /// Minimum effective value required for each stat; every entry must be met.
    [DataField]
    public Dictionary<SpecialStat, int> MinimumStats = new();

    /// Wearing power armor bypasses the requirement entirely.
    [DataField]
    public bool BypassWithPowerArmor;

    /// Entities without a SPECIAL component read as the neutral 5; enable this to let them through regardless.
    [DataField]
    public bool AllowNonSpecialEntities;

    /// Popup shown when a requirement is not met; format arguments are stat and required.
    [DataField]
    public LocId Popup = "special-stat-requirement-too-low";
}
