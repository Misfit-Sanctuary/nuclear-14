using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Misfits.NCRRankPin;

/// <summary>
/// Defines rank pin progression for a group of NCR jobs.
/// NCRRankPinSpawnSystem reads these at round start and gives the appropriate pin
/// directly into the player's hand based on their accumulated playtime.
/// </summary>
[Prototype("ncrRankPinProgression")]
public sealed partial class NCRRankPinProgressionPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Jobs this progression applies to.</summary>
    [DataField(required: true)]
    public List<ProtoId<JobPrototype>> Jobs = new();

    /// <summary>
    /// When <see cref="DepartmentTracker"/> is false: a playtime tracker ID (e.g. "NCRNCO").
    /// When <see cref="DepartmentTracker"/> is true: a DepartmentPrototype ID (e.g. "NCR")
    /// whose member job times are summed together.
    /// </summary>
    [DataField(required: true)]
    public string Tracker = string.Empty;

    /// <summary>If true, sum all role tracker times in the department named by <see cref="Tracker"/>.</summary>
    [DataField]
    public bool DepartmentTracker;

    /// <summary>
    /// Whether the earned pin is handed directly to the player when they spawn. Rank-tier vendor factions can
    /// disable this while retaining progression data for titles and catalog auditing.
    /// </summary>
    [DataField]
    public bool SpawnOnRoundstart = true;

    /// <summary>
    /// Thresholds in ascending order of <see cref="NCRRankPinThreshold.Min"/>.
    /// The system picks the highest threshold the player meets.
    /// </summary>
    [DataField(required: true)]
    public List<NCRRankPinThreshold> Thresholds = new();
}

[DataDefinition]
public sealed partial class NCRRankPinThreshold
{
    /// <summary>Minimum seconds required (0 = always available).</summary>
    [DataField]
    public int Min;

    /// <summary>Entity prototype to spawn as the rank pin.</summary>
    [DataField(required: true)]
    public EntProtoId Entity;
}
