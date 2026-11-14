using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Events;

// Actions the admin events tab can ask the server to perform.
public enum MisfitsEventAdminAction : byte
{
    Roll,
    Run,
    SetInterval,
    SetNextRoll,
    SetAutoEnabled,
    SetCvarEnabled,

    // Teleport the requesting admin to where this event last spawned.
    WarpToLastSpawn,
}

// Client to server: request an events state snapshot.
[Serializable, NetSerializable]
public sealed class MisfitsEventsStateRequest : EntityEventArgs;

// Client to server: perform an admin action on the event pool.
[Serializable, NetSerializable]
public sealed class MisfitsEventAdminActionRequest : EntityEventArgs
{
    public MisfitsEventAdminAction Action { get; set; }
    public string EventId { get; set; } = string.Empty;
    public float Min { get; set; } = 600f;
    public float Max { get; set; } = 1200f;
    public float Seconds { get; set; }
    public bool Enabled { get; set; }
}

// One event in an admin state snapshot.
[Serializable, NetSerializable]
public struct MisfitsEventInfo
{
    public string Id;
    public float Weight;
    public bool Eligible;

    // Whether this event recorded a spawn point this round.
    public bool HasSpawn;

    // Where it spawned, so "far away" can be told apart from "nothing".
    public float SpawnX;
    public float SpawnY;

    // Name of the map entrance it spawned at, when it has one.
    public string SpawnLocation;

    // How many entities it actually spawned there.
    public int SpawnedCount;

    // Why the last run produced nothing, or empty when it was fine.
    public string Failure;
}

// Server to client: current event state for the admin tab.
[Serializable, NetSerializable]
public sealed class MisfitsEventsStateEvent : EntityEventArgs
{
    public List<MisfitsEventInfo> Events = new();
    public bool SchedulerRunning;
    public bool SchedulerEnabled;
    public bool CvarEnabled;
    public float MinInterval = 600f;
    public float MaxInterval = 1200f;
    public float NextRoll;
    public string Message = string.Empty;
}

// Server to ghost: an event started, with the rule entity holding the spawn location.
[Serializable, NetSerializable]
public sealed class MisfitsEventGhostAnnouncement : EntityEventArgs
{
    public string Text = string.Empty;
    public NetEntity Rule;
}

// Client to server: a ghost clicked the [Warp] chat link.
[Serializable, NetSerializable]
public sealed class MisfitsEventWarpRequest : EntityEventArgs
{
    public NetEntity Rule;

    public MisfitsEventWarpRequest()
    {
    }

    public MisfitsEventWarpRequest(NetEntity rule)
    {
        Rule = rule;
    }
}
