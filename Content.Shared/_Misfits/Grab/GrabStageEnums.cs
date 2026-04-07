// #Misfits Add - Grab stage system: escalating grab states layered on top of the pull system
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Grab;

/// <summary>
/// Stages of a grab escalation. No → Soft → Hard → Suffocate.
/// The puller escalates by attacking the entity they are already pulling while in combat mode.
/// </summary>
[Serializable, NetSerializable]
public enum GrabStage : byte
{
    No = 0,
    Soft = 1,
    Hard = 2,
    Suffocate = 3,
}

/// <summary>
/// Whether a grab is escalating or de-escalating.
/// </summary>
[Serializable, NetSerializable]
public enum GrabStageDirection : byte
{
    Increase,
    Decrease,
}

/// <summary>
/// Result of a grabbed entity attempting to resist/escape a grab.
/// </summary>
[Serializable, NetSerializable]
public enum GrabResistResult : byte
{
    TooSoon,   // Cooldown not elapsed
    Failed,    // RNG check failed
    Succeeded, // Escaped
}

/// <summary>
/// Result of trying to initiate a grab escalation.
/// </summary>
[Serializable, NetSerializable]
public enum GrabAttemptResult : byte
{
    Succeeded,
    OnCooldown,
    Failed,
}
