// #Cythisiax Added - Faction MOTD: data-driven description of a faction whose leadership
// can post a "message of the day" that members see when they spawn in and via /factionmotd.
// Membership comes from the member's job; anyone whose job is not covered by a factionMotd
// (Wastelanders, FEV Mutants, Great Khans, ...) is not in a faction and gets nothing.

using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.FactionMotd;

[Prototype("factionMotd")]
public sealed partial class FactionMotdPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Faction name shown in the MOTD window header and chat notices.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>Accent colour for the faction name in the window and chat notices.</summary>
    [DataField]
    public Color Color = Color.White;

    /// <summary>Every job in these departments is a member of the faction.</summary>
    [DataField]
    public List<ProtoId<DepartmentPrototype>> Departments = new();

    /// <summary>Extra member jobs outside <see cref="Departments"/> (e.g. faction-aligned C-27 chassis).</summary>
    [DataField]
    public List<ProtoId<JobPrototype>> Jobs = new();

    /// <summary>Jobs inside <see cref="Departments"/> that are not members (prisoners, slaves).</summary>
    [DataField]
    public List<ProtoId<JobPrototype>> ExcludedJobs = new();

    /// <summary>Player-facing faction leadership. Only these jobs may write the MOTD; they are always members.</summary>
    [DataField(required: true)]
    public List<ProtoId<JobPrototype>> Leadership = new();
}
