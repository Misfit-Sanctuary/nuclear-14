using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.Events;

// A fork-owned random event: standalone pool, spawning at mapped anchors.
[RegisterComponent, Access(typeof(MisfitsEventSystem))]
public sealed partial class MisfitsEventComponent : Component
{
    // Relative chance of being picked by a weighted roll.
    [DataField]
    public float Weight = 10f;

    // Round minutes before this event may roll.
    [DataField]
    public int EarliestStart = 5;

    // Players that need to be online for this event to be eligible.
    [DataField]
    public int MinimumPlayers = 1;

    // Minutes before this event may roll again.
    [DataField]
    public int ReoccurrenceDelay = 20;

    // Runs per round; null means unlimited.
    [DataField]
    public int? MaxOccurrences;

    // How long the rule stays active before ending; spawned entities stay.
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(1);

    // Radius, in tiles, that spawns scatter in around the anchor.
    [DataField]
    public float SpawnRadius = 20f;

    // Fewest entities to spawn.
    [DataField]
    public int CountMin = 1;

    // Most entities to spawn.
    [DataField]
    public int CountMax = 1;

    // Weighted list of entities to spawn.
    [DataField]
    public List<MisfitsEventSpawnEntry> Spawns = new();

    // Above zero, everything is massed within this many tiles of one spot.
    [DataField]
    public float ClusterRadius;

    // Guaranteed extra spawns, used for ghost role spawners.
    [DataField]
    public List<MisfitsEventSpawnEntry> SpecialSpawns = new();

    // Fewest special spawns to place.
    [DataField]
    public int SpecialCountMin;

    // Most special spawns to place.
    [DataField]
    public int SpecialCountMax;

    // Optional locale string announced to everyone when it starts.
    [DataField]
    public string? Announcement;

    // Where the last run spawned; used by the ghost [Warp] chat link.
    [ViewVariables]
    public MapCoordinates? SpawnedAt;

    // How many entities the last run actually spawned.
    [ViewVariables]
    public int LastSpawnedCount;

    // Round time of the last run attempt, used to pick the newest run.
    [ViewVariables]
    public TimeSpan? LastRunAt;

    // Why the last run produced nothing, or null when it spawned.
    [ViewVariables]
    public string? LastFailure;

    // Name of the anchor the last run spawned at.
    [ViewVariables]
    public string? LastSpawnLocation;
}

[DataDefinition]
public sealed partial class MisfitsEventSpawnEntry
{
    [DataField]
    public EntProtoId Id = string.Empty;

    // Relative chance of this entry being picked.
    [DataField]
    public float Weight = 1f;
}

// Rolls a weighted event from the fork pool on a randomised interval.
[RegisterComponent, Access(typeof(MisfitsEventSchedulerSystem))]
public sealed partial class MisfitsEventSchedulerComponent : Component
{
    // Shortest delay in seconds between two automatic rolls.
    [DataField]
    public float MinInterval = 600f;

    // Longest delay in seconds between two automatic rolls.
    [DataField]
    public float MaxInterval = 1200f;

    // Delay in seconds before the first automatic roll.
    [DataField]
    public float FirstEventDelay = 600f;

    // Master switch for automatic rolls, toggled with the misfitevent command.
    [DataField]
    public bool Enabled = true;

    // Seconds left until the next roll.
    [ViewVariables(VVAccess.ReadWrite)]
    public float TimeUntilNextEvent;
}

// Mapper-placed point events spawn at; see MisfitsEventAnchor.
[RegisterComponent, Access(typeof(MisfitsEventSystem))]
public sealed partial class MisfitsEventAnchorComponent : Component
{
    // Tiles spawns may scatter in around this anchor; 0 uses the event's own spawnRadius.
    [DataField]
    public float Radius;
}
