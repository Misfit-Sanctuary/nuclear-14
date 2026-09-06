using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Misfits.Expeditions;

/// <summary>
/// BUI key for the expedition board interface.
/// </summary>
[Serializable, NetSerializable]
public enum N14ExpeditionBoardUiKey : byte
{
    Key,
}

/// <summary>
/// BUI state sent from server → client when the expedition board UI is open.
/// </summary>
[Serializable, NetSerializable]
public sealed class N14ExpeditionBoardState : BoundUserInterfaceState
{
    /// <summary>Whether an expedition is currently active from this board.</summary>
    public readonly bool ExpeditionActive;

    /// <summary>When the current expedition ends (server time), null if none active.</summary>
    public readonly TimeSpan? ExpeditionEndTime;

    /// <summary>When the launch countdown finishes (server time), null if not counting down.</summary>
    public readonly TimeSpan? LaunchEndTime;

    /// <summary>Available difficulty tiers with metadata for display.</summary>
    public readonly List<N14ExpeditionTierInfo> Tiers;

    /// <summary>Whether the board is on cooldown between expeditions.</summary>
    public readonly bool OnCooldown;

    /// <summary>When the cooldown ends (server time).</summary>
    public readonly TimeSpan? CooldownEndTime;

    public N14ExpeditionBoardState(
        bool expeditionActive,
        TimeSpan? expeditionEndTime,
        TimeSpan? launchEndTime,
        List<N14ExpeditionTierInfo> tiers,
        bool onCooldown,
        TimeSpan? cooldownEndTime)
    {
        ExpeditionActive = expeditionActive;
        ExpeditionEndTime = expeditionEndTime;
        LaunchEndTime = launchEndTime;
        Tiers = tiers;
        OnCooldown = onCooldown;
        CooldownEndTime = cooldownEndTime;
    }
}

/// <summary>
/// Lightweight tier info for the client UI display.
/// </summary>
[Serializable, NetSerializable]
public sealed record N14ExpeditionTierInfo(
    string TierId,
    string Name,
    Color Color,
    int MapCount,
    float DurationMinutes);

/// <summary>
/// Message sent from client → server to launch an expedition at the chosen difficulty.
/// </summary>
[Serializable, NetSerializable]
public sealed class N14ExpeditionLaunchMessage : BoundUserInterfaceMessage
{
    public readonly string DifficultyId;

    public N14ExpeditionLaunchMessage(string difficultyId)
    {
        DifficultyId = difficultyId;
    }
}

/// <summary>
/// Placed on a chalkboard floor entity to enable the expedition board GUI.
/// Tracks pending launches, active expeditions, and cooldowns.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class N14ExpeditionBoardComponent : Component
{
    /// <summary>
    /// Launch directly from world interaction instead of opening the legacy board UI.
    /// Used by randomly placed expedition entrances.
    /// </summary>
    [DataField]
    public bool DirectLaunch;

    /// <summary>Difficulty pool launched by a direct world entrance.</summary>
    [DataField]
    public string DirectDifficultyId = "N14ExpeditionUnknownProcedural";

    /// <summary>
    /// Radius around the board to detect players for group teleportation.
    /// </summary>
    [DataField]
    public float GatherRadius = 4f;

    /// <summary>
    /// Seconds to count down before teleporting the group.
    /// </summary>
    [DataField]
    public float LaunchCountdownSeconds = 10f;

    /// <summary>
    /// Cooldown in seconds between expeditions from this board.
    /// </summary>
    [DataField]
    public float CooldownSeconds = 1800f;

    /// <summary>
    /// Server time when the pending launch will fire, null if idle.
    /// </summary>
    [AutoPausedField]
    [AutoNetworkedField]
    [DataField]
    public TimeSpan? PendingLaunchTime;

    /// <summary>
    /// Difficulty ID for the pending launch.
    /// </summary>
    [AutoNetworkedField]
    [DataField]
    public string? PendingDifficulty;

    /// <summary>
    /// EntityUid of the currently active expedition map, null if none.
    /// </summary>
    [DataField]
    public EntityUid? ActiveExpedition;

    /// <summary>
    /// Server time when cooldown ends, null if not on cooldown.
    /// </summary>
    [AutoPausedField]
    [AutoNetworkedField]
    [DataField]
    public TimeSpan? CooldownEnd;
}

/// <summary>
/// Marks a round-scoped surface entrance for tactical-map display.
/// </summary>
[RegisterComponent]
public sealed partial class N14ExpeditionEntranceLandmarkComponent : Component;
