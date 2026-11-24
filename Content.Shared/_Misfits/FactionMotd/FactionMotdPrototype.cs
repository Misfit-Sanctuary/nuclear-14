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

    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public Color Color = Color.White;

    [DataField]
    public List<ProtoId<DepartmentPrototype>> Departments = new();

    [DataField]
    public List<ProtoId<JobPrototype>> Jobs = new();

    [DataField]
    public List<ProtoId<JobPrototype>> ExcludedJobs = new();

    [DataField(required: true)]
    public List<ProtoId<JobPrototype>> Leadership = new();
}
