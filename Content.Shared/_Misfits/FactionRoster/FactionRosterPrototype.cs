// #Cythisiax Added - Faction roster: data-driven description of a faction whose
// membership is tracked through persistent job whitelists. Faction leadership uses
// the roster GUI to recruit, promote, demote and discharge members, and to set the
// oath shown to recruits.

using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.FactionRoster;

[Prototype("factionRoster")]
public sealed partial class FactionRosterPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Faction name shown as the roster window title and in the recruitment offer.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>
    /// Default oath / offer text shown to recruits, used until leadership sets their own.
    /// Several entries are shown in order, separated by a blank line (e.g. a contract then
    /// an oath). Each receives <c>$name</c> (the recruit) and <c>$recruiter</c>.
    /// </summary>
    [DataField(required: true)]
    public List<LocId> OfferText = new();

    /// <summary>
    /// Independent rank ladders (e.g. Military and Science), each lowest rank first.
    /// Promotion grants the next rank on a track; demotion removes the member's highest.
    /// </summary>
    [DataField(required: true)]
    public List<FactionRosterTrack> Tracks = new();

    /// <summary>Side roles (e.g. a medic or pilot) that can be assigned regardless of rank.</summary>
    [DataField]
    public List<ProtoId<JobPrototype>> Specialties = new();

    /// <summary>
    /// Command roles that sit above every track (e.g. DAGGER). Holders have full authority
    /// over every track, so they can promote to and demote from the top rank. Their own
    /// rows are listed under the role's name but only admins can change them.
    /// </summary>
    [DataField]
    public List<ProtoId<JobPrototype>> Protected = new();

    /// <summary>Jobs that may edit the oath. Empty means the <see cref="Protected"/> roles.</summary>
    [DataField]
    public List<ProtoId<JobPrototype>> OathEditors = new();

    // #Cythisiax Added - who may remove people from the faction entirely
    /// <summary>
    /// Jobs (besides command roles) that may discharge members or strip a track's lowest
    /// rank. Everyone else can only demote people down to the lowest rank.
    /// </summary>
    [DataField]
    public List<ProtoId<JobPrototype>> Dischargers = new();

    /// <summary>
    /// Jobs that may open the roster. A holder whose job sits on a track may only manage
    /// ranks below their own on that track.
    /// </summary>
    [DataField(required: true)]
    public List<ProtoId<JobPrototype>> AuthorizedJobs = new();

    /// <summary>Whitelists granted when a recruit accepts. May be empty when a faction
    /// handles recruitment itself through <see cref="FactionRosterRecruitedEvent"/>.</summary>
    [DataField]
    public List<ProtoId<JobPrototype>> RecruitGrants = new();

    /// <summary>How close (in tiles) a player must stand to be offered recruitment.</summary>
    [DataField]
    public float RecruitRange = 3f;
}

[DataDefinition]
public sealed partial class FactionRosterTrack
{
    /// <summary>Short label shown next to the track's promote/demote buttons.</summary>
    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public List<ProtoId<JobPrototype>> Ranks = new();

    // #Cythisiax Added - officer ranks that only command roles may promote to or demote from
    /// <summary>
    /// If set, this rank and every rank above it on the track can only be granted or
    /// removed by command roles (Protected), e.g. only the Enclave Commander makes officers.
    /// </summary>
    [DataField]
    public ProtoId<JobPrototype>? CommandOnlyFrom;
}
