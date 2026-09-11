using Robust.Shared.Map;

namespace Content.Shared._Misfits.Expeditions;

/// <summary>
/// Represents one board's expedition session on a (possibly shared) map.
/// Multiple sessions can exist on the same map if different boards roll
/// the same map path — each group keeps its own timer and return point.
/// </summary>
[DataDefinition]
public sealed partial class N14ExpeditionSession
{
    /// <summary>The board entity that launched this session.</summary>
    [DataField] public EntityUid SourceBoard;

    /// <summary>Coordinates to teleport this session's players back to.</summary>
    [DataField] public EntityCoordinates ReturnPoint;

    /// <summary>
    /// Safe hub coordinate for players who join this expedition after launch.
    /// This is deliberately recorded per session rather than inferred from the
    /// map centre: a procedural map can have several faction hubs.
    /// </summary>
    [DataField] public EntityCoordinates EntryPoint;

    /// <summary>Server time when this session expires.</summary>
    [DataField] public TimeSpan EndTime;

    /// <summary>Difficulty ID used for this session.</summary>
    [DataField] public string DifficultyId = string.Empty;

    // Per-session warning flags — only fire once each
    [DataField] public bool Warned5Min;
    [DataField] public bool Warned1Min;
    [DataField] public bool Warned30Sec;

    /// <summary>
    /// Last "minutes remaining" value announced in chat for this session.
    /// Set to -1 initially so the first check doesn't double-fire.
    /// </summary>
    [DataField] public int LastChatWarningMinutes = -1;

    /// <summary>Whether this session has been completed and its players extracted.</summary>
    [DataField] public bool Finished;

    /// <summary>
    /// Entities teleported in by this session.
    /// Used to identify which players belong to which board's group
    /// so force-extraction only pulls back the right people.
    /// </summary>
    [DataField] public HashSet<EntityUid> Players = new();
}

/// <summary>
/// Placed on the expedition map entity to track all active sessions using this map.
/// The map is only deleted once every session is finished and no players remain.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class N14ExpeditionComponent : Component
{
    /// <summary>
    /// The map file path that was loaded.
    /// Used to detect when a second board rolls the same map and should
    /// share the existing instance instead of loading a duplicate.
    /// </summary>
    [DataField]
    public string MapPath = string.Empty;

    /// <summary>
    /// The first grid entity (used for spawn coordinate resolution).
    /// </summary>
    [DataField]
    public EntityUid GridUid;

    /// <summary>
    /// All expedition sessions active on this map.
    /// Each represents one board's group with its own timer and return point.
    /// </summary>
    [DataField]
    public List<N14ExpeditionSession> Sessions = new();
}
