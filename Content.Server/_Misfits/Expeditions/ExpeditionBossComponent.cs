using System;
using Content.Shared._Misfits.Expeditions;

namespace Content.Server._Misfits.Expeditions;

/// <summary>
/// Server-authoritative state for a generated expedition guardian. The component is
/// intentionally attached only to runtime expedition mobs, never to reusable
/// NPC prototypes or player creatures.
/// </summary>
[RegisterComponent]
public sealed partial class ExpeditionBossComponent : Component
{
    /// <summary>Immutable name generated for this boss instance.</summary>
    public string DisplayName = string.Empty;

    /// <summary>Entity table resolved at death, not at map generation.</summary>
    public string RewardTable = "N14ExpeditionBossReward";

    /// <summary>Stable table seed stored with the boss rather than rerolled on death.</summary>
    public int RewardSeed;

    /// <summary>Prevents duplicate rewards from repeated death/cleanup events.</summary>
    public bool RewardClaimed;

    /// <summary>True for the single planned objective guardian rather than a deep-room elite.</summary>
    public bool IsFinalGuardian;

    /// <summary>Health floor applied when this runtime promotion was configured.</summary>
    public int HealthFloor;

    /// <summary>Whether this boss receives the conservative regeneration modifier.</summary>
    public bool Regenerative;

    /// <summary>Next server time at which the regeneration modifier may heal.</summary>
    public TimeSpan NextRegen;
}
