using Content.Shared.Dataset;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

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

    // Only these anchor kinds are used; empty accepts any anchor.
    [DataField]
    public List<MisfitsEventAnchorKind> AnchorKinds = new();

    // Groups that arrive later in the event, still around the same anchor.
    [DataField]
    public List<MisfitsEventWave> Waves = new();

    // Whole saved maps placed on the anchor before the spawns arrive, e.g. a raider fort.
    [DataField]
    public List<MisfitsEventStructure> Structures = new();

    // One random radio frequency per team for this run, so roles taken at different moments share a net.
    [ViewVariables]
    public Dictionary<string, int> TeamFrequencies = new();

    // One line from this dataset is rolled per run and told to every role the event places,
    // e.g. the crime the law event's criminal is wanted for.
    [DataField]
    public ProtoId<LocalizedDatasetPrototype>? Briefing;

    // The line rolled for this run; null when the event has no briefing.
    [ViewVariables]
    public string? BriefingText;

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

    // Leaves the picking pool once spawned, so a nest and its guardian both arrive.
    [DataField]
    public bool Unique;

    // How many of this entry are placed when it is picked.
    [DataField]
    public int Count = 1;
}

// A saved map file dropped whole onto the round's map, so an event can bring its own building.
[DataDefinition]
public sealed partial class MisfitsEventStructure
{
    // Map file under Resources, e.g. /Maps/_Misfits/Events/RaiderFort.yml; the file's origin lands on the anchor.
    [DataField]
    public ResPath Path = default!;

    // Chance it is placed at all; 1 for always.
    [DataField]
    public float Chance = 1f;
}

// A group of spawns that arrives some seconds after the event starts.
[DataDefinition]
public sealed partial class MisfitsEventWave
{
    // Seconds after the event starts.
    [DataField]
    public float Delay = 60f;

    // Fewest entities to spawn.
    [DataField]
    public int CountMin = 1;

    // Most entities to spawn.
    [DataField]
    public int CountMax = 1;

    // Weighted list of entities to spawn.
    [DataField]
    public List<MisfitsEventSpawnEntry> Spawns = new();

    // Optional locale string announced when this wave arrives.
    [DataField]
    public string? Announcement;
}

// What kind of place an anchor sits in, so events can ask for town or wilderness.
public enum MisfitsEventAnchorKind : byte
{
    Any = 0,
    Town,
    Wilderness,
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

// A gecko nest: fed meat or limbs, it grows another gecko ghost role for the pack.
[RegisterComponent, Access(typeof(MisfitsGeckoNestSystem))]
public sealed partial class MisfitsGeckoNestComponent : Component
{
    // Meat or limbs needed per gecko ghost role.
    [DataField]
    public int FeedCost = 2;

    // Ghost role spawner left behind each time the nest has been fed enough.
    [DataField]
    public EntProtoId Spawner = "MisfitsSpawnPointGeckoFire";

    // Offerings fed since the last gecko was raised.
    [ViewVariables(VVAccess.ReadWrite)]
    public int Fed;

    // Spawners this nest has grown, removed along with the nest.
    [ViewVariables]
    public List<EntityUid> Spawners = new();
}

// Asks the player whose mind lands in this mob what it should be called.
[RegisterComponent, Access(typeof(MisfitsNamePromptSystem))]
public sealed partial class MisfitsNamePromptComponent : Component
{
    // Longest name the prompt accepts.
    [DataField]
    public int MaxLength = 32;
}

// Mapper-placed point events spawn at; see MisfitsEventAnchor.
[RegisterComponent, Access(typeof(MisfitsEventSystem))]
public sealed partial class MisfitsEventAnchorComponent : Component
{
    // Tiles spawns may scatter in around this anchor; 0 uses the event's own spawnRadius.
    [DataField]
    public float Radius;

    // Where this anchor is, so town events do not land in the wastes, and the reverse.
    [DataField]
    public MisfitsEventAnchorKind Kind = MisfitsEventAnchorKind.Any;
}
