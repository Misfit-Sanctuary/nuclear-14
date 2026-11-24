// #Cythisiax Added - Faction roster: lets faction leadership (e.g. DAGGER, the Chief Ranger,
// the Ranger Lead, Enclave officers) see every whitelisted member of their faction and recruit, promote, demote or
// discharge them, and set the oath shown to recruits. Membership is the persistent job
// whitelist, so every change writes to the database through JobWhitelistManager, exactly
// like the admin whitelist panel. Factions with extra recruitment rules hook in through
// FactionRosterRecruitAttemptEvent / FactionRosterRecruitedEvent.
// #Cythisiax Edited - the roster also lists anyone on duty in one of its jobs right now, whitelisted or
// not (NCR, Legion and BoS jobs open up by playtime too). Tracks can be run by jobs off the track
// (every BoS Head runs Initiate -> Squire) and can require a role first (BoS castes need Squire).
// Specialties can be limited to certain jobs (Head Knight -> Knight-Lancer, Priestess -> Mars roles,
// Elder -> Honor Guard). Rank managers (NCR / Enclave Commander+) can also move someone's rank pin,
// which FactionRankSystem saves on their character.

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using Content.Server.Administration.Logs;
using Content.Server.Database;
using Content.Server.EUI;
using Content.Server.Players.JobWhitelist;
using Content.Server._Misfits.Administration.WhitelistLogs;
using Content.Server._Misfits.FactionRank;
using Content.Shared.Actions;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.IdentityManagement;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared._Misfits.FactionRoster;
using Robust.Server.Player;
using Robust.Shared.ContentPack;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._Misfits.FactionRoster;

public sealed class FactionRosterSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IResourceManager _resources = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly EuiManager _eui = default!;
    [Dependency] private readonly JobWhitelistManager _jobWhitelist = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedJobSystem _jobs = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly WhitelistLogSystem _whitelistLog = default!;
    [Dependency] private readonly FactionRankSystem _factionRank = default!; // #Cythisiax Added - rank pins

    /// <summary>Longest oath leadership may save.</summary>
    public const int MaxOathLength = 8000;

    private static readonly ResPath OathPath = new("/faction_roster_oaths.json");

    /// <summary>Cached whitelist holders per roster: user -> (last seen name, held roles).</summary>
    private readonly Dictionary<string, Dictionary<NetUserId, RosterRecord>> _cache = new();

    /// <summary>Rosters with a database load in flight.</summary>
    private readonly HashSet<string> _loading = new();

    private readonly HashSet<FactionRosterEui> _openUis = new();

    /// <summary>Players with a recruitment offer currently on their screen.</summary>
    private readonly HashSet<NetUserId> _pendingOffers = new();

    /// <summary>Leadership-written oaths per roster, persisted across rounds.</summary>
    private Dictionary<string, string> _oaths = new();

    public sealed class RosterRecord(string userName)
    {
        public string UserName = userName;
        public readonly HashSet<string> Roles = new();

        // #Cythisiax Added - the roster job they're playing this round, which may not be whitelisted
        public string? ActiveJob;

        public HashSet<string> Held()
        {
            if (ActiveJob == null || Roles.Contains(ActiveJob))
                return Roles;

            return new HashSet<string>(Roles) { ActiveJob };
        }
    }

    /// <summary>
    /// What the viewer may manage: ranks below Limits[track] on each track. Command roles
    /// (e.g. DAGGER) sit above every track and may manage all of it.
    /// </summary>
    private readonly record struct Authority(bool Command, int[] Limits, bool CanEditOath, bool CanDischarge); // #Cythisiax Edited - discharge permission

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FactionRosterComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<FactionRosterComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<FactionRosterComponent, OpenFactionRosterActionEvent>(OnOpenRoster);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);

        LoadOaths();
    }

    private void OnMapInit(Entity<FactionRosterComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnShutdown(Entity<FactionRosterComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent, ent.Comp.ActionEntity);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        // Whitelists may have been edited by admins between rounds; reload on next open.
        _cache.Clear();
        _pendingOffers.Clear();
    }

    private void OnOpenRoster(Entity<FactionRosterComponent> ent, ref OpenFactionRosterActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (!_player.TryGetSessionByEntity(args.Performer, out var session))
            return;

        if (!_proto.TryIndex(ent.Comp.Roster, out var roster) || !IsAuthorized(args.Performer, roster))
        {
            _popup.PopupEntity(Loc.GetString("faction-roster-not-authorized"), args.Performer, args.Performer);
            return;
        }

        // One roster window per player.
        foreach (var ui in _openUis.Where(u => u.Player == session).ToList())
            ui.Close();

        _eui.OpenEui(new FactionRosterEui(this, roster.ID), session);
    }

    #region Authority

    /// <summary>
    /// True if <paramref name="user"/> may open <paramref name="roster"/>: they must hold the
    /// roster component for it, be conscious, and hold one of its authorized jobs.
    /// </summary>
    public bool IsAuthorized(EntityUid user, FactionRosterPrototype roster)
    {
        if (!TryComp<FactionRosterComponent>(user, out var comp) || comp.Roster != roster.ID)
            return false;

        if (_mobState.IsIncapacitated(user))
            return false;

        if (comp.IgnoreJobRequirement)
            return true;

        return CurrentJob(user) is { } job && roster.AuthorizedJobs.Contains(job);
    }

    private string? CurrentJob(EntityUid body)
    {
        if (!TryComp<MindContainerComponent>(body, out var container) || container.Mind is not { } mindId)
            return null;

        return _jobs.MindTryGetJob(mindId, out _, out var job) ? job.ID : null;
    }

    private Authority GetAuthority(FactionRosterPrototype roster, ICommonSession viewer)
    {
        var job = viewer.AttachedEntity is { } body ? CurrentJob(body) : null;
        // #Cythisiax Edited - an admin-granted roster (IgnoreJobRequirement) keeps full reach
        var command = job != null && roster.Protected.Contains(job)
                      || viewer.AttachedEntity is { } holder
                      && TryComp<FactionRosterComponent>(holder, out var rosterComp)
                      && rosterComp.IgnoreJobRequirement
                      && rosterComp.Roster == roster.ID;
        var limits = roster.Tracks.Select(t => t.Ranks.Count).ToArray();

        // Officers sitting on a track can only manage the ranks beneath them there.
        // #Cythisiax Edited - and only their own track: an Enclave officer can't touch the
        // science track, and the Head Scientist can't touch the military one.
        // #Cythisiax Edited - a command role that also tops a track (Enclave Commander) still
        // can't touch its own rank there; only a higher command role (Reformer) can.
        for (var t = 0; t < roster.Tracks.Count; t++)
        {
            var index = job == null ? -1 : roster.Tracks[t].Ranks.IndexOf(job);
            if (index >= 0)
                limits[t] = index;
            else if (!command && (job == null || !roster.Tracks[t].Managers.Contains(job))) // #Cythisiax Edited - track managers run the whole track
                limits[t] = 0;
        }

        // #Cythisiax Added - ranks from CommandOnlyFrom upward are reserved for command roles
        if (!command)
        {
            for (var t = 0; t < roster.Tracks.Count; t++)
            {
                if (roster.Tracks[t].CommandOnlyFrom is not { } cutoff)
                    continue;

                var cutoffIndex = roster.Tracks[t].Ranks.IndexOf(cutoff);
                if (cutoffIndex >= 0)
                    limits[t] = Math.Min(limits[t], cutoffIndex);
            }
        }

        var oathEditors = roster.OathEditors.Count > 0 ? roster.OathEditors : roster.Protected;
        var canEditOath = job != null && oathEditors.Contains(job);

        // #Cythisiax Added - only command roles and listed dischargers can remove people outright
        var canDischarge = command || job != null && roster.Dischargers.Contains(job);

        return new Authority(command, limits, canEditOath, canDischarge);
    }

    public bool CanEditOath(FactionRosterPrototype roster, ICommonSession viewer)
    {
        return GetAuthority(roster, viewer).CanEditOath;
    }

    #endregion

    #region Roster data

    public void RegisterUi(FactionRosterEui ui) => _openUis.Add(ui);

    public void UnregisterUi(FactionRosterEui ui) => _openUis.Remove(ui);

    /// <summary>Reload the roster's whitelist holders from the database.</summary>
    public async void LoadRoster(FactionRosterPrototype roster)
    {
        if (!_loading.Add(roster.ID))
            return;

        DirtyUis(roster.ID);

        try
        {
            var roles = AllRoles(roster).Select(r => r.Id).ToList();
            var rows = await _db.GetJobWhitelistHolders(roles);

            var cache = new Dictionary<NetUserId, RosterRecord>();
            foreach (var row in rows)
            {
                var userId = new NetUserId(row.UserId);
                if (!cache.TryGetValue(userId, out var record))
                {
                    record = new RosterRecord(row.UserName);
                    cache[userId] = record;
                }

                record.Roles.Add(row.RoleId);
            }

            _cache[roster.ID] = cache;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load faction roster {roster.ID}: {e}");
        }
        finally
        {
            _loading.Remove(roster.ID);
            DirtyUis(roster.ID);
        }
    }

    // #Cythisiax Added - pull in everyone on duty in a roster job this round, whitelisted or not
    private Dictionary<NetUserId, RosterRecord>? MergeActiveMembers(FactionRosterPrototype roster)
    {
        if (!_cache.TryGetValue(roster.ID, out var cache))
            return null;

        foreach (var record in cache.Values)
            record.ActiveJob = null;

        var roles = AllRoles(roster).Select(r => r.Id).ToHashSet();
        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity is not { } body || CurrentJob(body) is not { } job || !roles.Contains(job))
                continue;

            if (!cache.TryGetValue(session.UserId, out var record))
            {
                record = new RosterRecord(session.Name);
                cache[session.UserId] = record;
            }

            record.ActiveJob = job;
        }

        return cache;
    }

    private void DirtyUis(string rosterId)
    {
        foreach (var ui in _openUis)
        {
            if (!ui.IsShutDown && ui.RosterId == rosterId)
                ui.StateDirty();
        }
    }

    public static IEnumerable<ProtoId<JobPrototype>> AllRoles(FactionRosterPrototype roster)
    {
        return roster.Tracks.SelectMany(t => t.Ranks)
            .Concat(roster.Specialties)
            .Concat(roster.Protected)
            .Distinct();
    }

    /// <summary>Index of the member's highest rank on a track, or -1 if they hold none.</summary>
    private static int RankIndex(FactionRosterTrack track, IReadOnlyCollection<string> roles)
    {
        for (var i = track.Ranks.Count - 1; i >= 0; i--)
        {
            if (roles.Contains(track.Ranks[i].Id))
                return i;
        }

        return -1;
    }

    private static bool IsCommand(FactionRosterPrototype roster, IReadOnlyCollection<string> roles)
    {
        return roster.Protected.Any(p => roles.Contains(p.Id));
    }

    // #Cythisiax Added - command roles that aren't a rank on any track
    private static bool IsTracklessCommand(FactionRosterPrototype roster, IReadOnlyCollection<string> roles)
    {
        return roster.Protected.Any(p => roles.Contains(p.Id) && !roster.Tracks.Any(t => t.Ranks.Contains(p)));
    }

    /// <summary>
    /// Self, command roles, and anyone at or above the viewer's own rank on a track
    /// are out of the viewer's reach.
    /// </summary>
    private static bool IsLocked(
        FactionRosterPrototype roster,
        Authority authority,
        ICommonSession viewer,
        NetUserId target,
        RosterRecord record)
    {
        // #Cythisiax Edited - only command roles that sit above every track (DAGGER, Reformer) are
        // untouchable; a command role ranked on a track (Commander) follows normal rank limits.
        if (target == viewer.UserId || IsTracklessCommand(roster, record.Held()))
            return true;

        var held = record.Held();
        for (var t = 0; t < roster.Tracks.Count; t++)
        {
            if (RankIndex(roster.Tracks[t], held) >= authority.Limits[t])
                return true;
        }

        return false;
    }

    /// <summary>True if the member holds roster roles outside the given track.</summary>
    private static bool HoldsRolesOutside(FactionRosterPrototype roster, int track, RosterRecord record)
    {
        var trackRanks = roster.Tracks[track].Ranks.Select(r => r.Id).ToHashSet();
        return record.Held().Any(r => !trackRanks.Contains(r));
    }

    public FactionRosterEuiState BuildState(FactionRosterPrototype roster, ICommonSession viewer, string? status)
    {
        var authority = GetAuthority(roster, viewer);
        var candidates = viewer.AttachedEntity is { } user
            ? BuildCandidates(roster, user)
            : new List<FactionRosterCandidate>();

        return new FactionRosterEuiState(
            Loc.GetString(roster.Name),
            roster.Tracks.Select(t => new FactionRosterTrackInfo(
                Loc.GetString(t.Name),
                t.Ranks.Select(r => r.Id).ToList())).ToList(),
            roster.Specialties.Select(r => r.Id).ToList(),
            BuildMembers(roster, authority, viewer),
            candidates,
            _loading.Contains(roster.ID),
            status,
            GetOathTemplate(roster),
            _oaths.ContainsKey(roster.ID),
            authority.CanEditOath,
            authority.CanDischarge);
    }

    private List<FactionRosterMember> BuildMembers(FactionRosterPrototype roster, Authority authority, ICommonSession viewer)
    {
        var members = new List<FactionRosterMember>();
        if (MergeActiveMembers(roster) is not { } cache) // #Cythisiax Edited - include on-duty members
            return members;

        foreach (var (userId, record) in cache)
        {
            var held = record.Held();
            if (held.Count == 0)
                continue;

            string? characterName = null;
            var online = _player.TryGetSessionById(userId, out var session);

            // #Cythisiax Edited - roster lists only members playing one of this faction's jobs
            // this round; whitelisted players in the lobby, ghosting or on another faction's job
            // don't show up.
            if (record.ActiveJob == null || !online || session!.AttachedEntity is not { } body)
                continue;

            characterName = Name(body);

            var locked = IsLocked(roster, authority, viewer, userId, record);

            var standings = new List<FactionRosterStanding>();
            for (var t = 0; t < roster.Tracks.Count; t++)
            {
                var track = roster.Tracks[t];
                var rank = RankIndex(track, held);
                standings.Add(new FactionRosterStanding(
                    rank,
                    !locked && rank + 1 < authority.Limits[t] && MeetsRequirement(roster, track, rank, held), // #Cythisiax Edited - track prerequisite
                    // #Cythisiax Edited - removing the lowest rank needs discharge permission, unless they fall back to the track's prerequisite
                    !locked && rank >= 0 && (rank > 0 || FallsBackTo(roster, track, held) || authority.CanDischarge && HoldsRolesOutside(roster, t, record))));
            }

            // #Cythisiax Added - rank pin, for pin factions
            string? pinRank = null;
            var canPromotePin = false;
            var canDemotePin = false;
            if (session.AttachedEntity is { } pinBody &&
                _factionRank.TryGetPinStanding(pinBody, out var pinName, out var pinIndex, out var pinCount))
            {
                pinRank = pinName;
                var canManage = CanManagePins(roster, viewer) && userId != viewer.UserId && !IsTracklessCommand(roster, held);
                canPromotePin = canManage && pinIndex + 1 < pinCount;
                canDemotePin = canManage && pinIndex > 0;
            }

            members.Add(new FactionRosterMember(
                userId,
                record.UserName,
                characterName,
                online,
                IsCommand(roster, held),
                roster.Protected.Where(p => held.Contains(p.Id)).Select(p => p.Id).ToList(),
                locked,
                standings,
                roster.Specialties.Where(s => held.Contains(s.Id)).Select(s => s.Id).ToList(),
                roster.Specialties.Where(s => CanToggleSpecialty(roster, authority, viewer, userId, record, s.Id)).Select(s => s.Id).ToList(),
                pinRank,
                canPromotePin,
                canDemotePin));
        }

        return members;
    }

    /// <summary>Living, connected players near the viewer who are not yet in the faction.</summary>
    private List<FactionRosterCandidate> BuildCandidates(FactionRosterPrototype roster, EntityUid user)
    {
        var candidates = new List<FactionRosterCandidate>();
        var coords = _transform.GetMapCoordinates(user);

        foreach (var target in _lookup.GetEntitiesInRange<MindContainerComponent>(coords, roster.RecruitRange))
        {
            if (target.Owner == user || !CanBeRecruited(roster, target.Owner, out var session))
                continue;

            candidates.Add(new FactionRosterCandidate(
                GetNetEntity(target.Owner),
                Identity.Name(target.Owner, EntityManager),
                _pendingOffers.Contains(session.UserId)));
        }

        return candidates.OrderBy(c => c.Name).ToList();
    }

    private bool CanBeRecruited(
        FactionRosterPrototype roster,
        EntityUid target,
        [NotNullWhen(true)] out ICommonSession? session)
    {
        session = null;
        if (!_mobState.IsAlive(target))
            return false;

        if (!_player.TryGetSessionByEntity(target, out session))
            return false;

        // Already holds a role on this roster, by whitelist or by current job.
        var currentJob = CurrentJob(target);
        foreach (var role in AllRoles(roster))
        {
            if (role.Id == currentJob || _jobWhitelist.IsWhitelisted(session.UserId, role))
                return false;
        }

        var ev = new FactionRosterRecruitAttemptEvent(roster.ID, target);
        RaiseLocalEvent(ref ev);
        return !ev.Cancelled;
    }

    #endregion

    #region Actions

    public string? Promote(FactionRosterPrototype roster, ICommonSession actor, NetUserId target, int track)
    {
        if (!TryGetEditable(roster, actor, target, track, out var record, out var authority, out var error))
            return error;

        var ranks = roster.Tracks[track].Ranks;
        var rank = RankIndex(roster.Tracks[track], record.Held());
        if (rank + 1 >= authority.Limits[track])
            return Loc.GetString("faction-roster-status-max-rank", ("name", DisplayName(target, record)));

        // #Cythisiax Added - e.g. a BoS caste needs Squire first
        if (!MeetsRequirement(roster, roster.Tracks[track], rank, record.Held()))
            return Loc.GetString("faction-roster-status-requires",
                ("name", DisplayName(target, record)), ("rank", JobName(roster.Tracks[track].Requires!.Value)));

        var newRole = ranks[rank + 1];
        Grant(roster, actor, target, record, newRole, "Promoted");
        Notify(target, Loc.GetString("faction-roster-notify-promoted", ("rank", JobName(newRole))));
        return Loc.GetString("faction-roster-status-promoted",
            ("name", DisplayName(target, record)), ("rank", JobName(newRole)));
    }

    public string? Demote(FactionRosterPrototype roster, ICommonSession actor, NetUserId target, int track)
    {
        if (!TryGetEditable(roster, actor, target, track, out var record, out var authority, out var error)) // #Cythisiax Edited - need authority for the discharge check
            return error;

        var ranks = roster.Tracks[track].Ranks;
        var rank = RankIndex(roster.Tracks[track], record.Held());
        if (rank < 0 || rank == 0 && !HoldsRolesOutside(roster, track, record))
            return Loc.GetString("faction-roster-status-min-rank", ("name", DisplayName(target, record)));

        // #Cythisiax Added - stripping the lowest rank removes them from the track; that needs discharge permission,
        // unless they drop back to the track's prerequisite (a BoS Paladin back to Squire)
        if (rank == 0 && !authority.CanDischarge && !FallsBackTo(roster, roster.Tracks[track], record.Held()))
            return Loc.GetString("faction-roster-status-discharge-denied");

        Revoke(roster, actor, target, record, [ranks[rank]], "Demoted");

        if (rank == 0)
        {
            Notify(target, Loc.GetString("faction-roster-notify-track-removed", ("rank", JobName(ranks[0]))));
            return Loc.GetString("faction-roster-status-track-removed",
                ("name", DisplayName(target, record)), ("rank", JobName(ranks[0])));
        }

        // Ranks are cumulative, but someone granted a higher rank directly by an admin
        // may lack the one below; make sure demotion lands them on it.
        var lower = ranks[rank - 1];
        if (!record.Roles.Contains(lower.Id))
            Grant(roster, actor, target, record, lower, "Demoted");

        Notify(target, Loc.GetString("faction-roster-notify-demoted", ("rank", JobName(lower))));
        return Loc.GetString("faction-roster-status-demoted",
            ("name", DisplayName(target, record)), ("rank", JobName(lower)));
    }

    public string? Discharge(FactionRosterPrototype roster, ICommonSession actor, NetUserId target)
    {
        if (!TryGetEditable(roster, actor, target, null, out var record, out var authority, out var error)) // #Cythisiax Edited - need authority for the discharge check
            return error;

        // #Cythisiax Added - only command roles and listed dischargers may remove people from the faction
        if (!authority.CanDischarge)
            return Loc.GetString("faction-roster-status-discharge-denied");

        var held = roster.Tracks.SelectMany(t => t.Ranks)
            .Concat(roster.Specialties)
            .Where(r => record.Roles.Contains(r.Id))
            .Distinct()
            .ToList();

        if (held.Count == 0)
            return null;

        Revoke(roster, actor, target, record, held, "Discharged");
        Notify(target, Loc.GetString("faction-roster-notify-discharged", ("faction", Loc.GetString(roster.Name))));
        return Loc.GetString("faction-roster-status-discharged", ("name", DisplayName(target, record)));
    }

    public string? ToggleSpecialty(FactionRosterPrototype roster, ICommonSession actor, NetUserId target, string role)
    {
        var specialty = roster.Specialties.FirstOrDefault(s => s.Id == role);
        if (specialty == default)
            return null;

        // #Cythisiax Edited - specialty rules decide who may toggle it, instead of the usual rank reach
        var authority = GetAuthority(roster, actor);
        if (MergeActiveMembers(roster) is not { } cache || !cache.TryGetValue(target, out var record))
            return Loc.GetString("faction-roster-status-not-member");

        if (!CanToggleSpecialty(roster, authority, actor, target, record, role))
        {
            return roster.SpecialtyRules.TryGetValue(role, out var rule) && rule.Requires is { } needs && !HoldsOrAbove(roster, record.Held(), needs.Id)
                ? Loc.GetString("faction-roster-status-requires", ("name", DisplayName(target, record)), ("rank", JobName(needs)))
                : Loc.GetString("faction-roster-status-locked", ("name", DisplayName(target, record)));
        }

        if (record.Roles.Contains(role))
        {
            // Removing their only role would silently drop them off the roster; that's Discharge.
            if (record.Held().Count == 1)
                return Loc.GetString("faction-roster-status-min-rank", ("name", DisplayName(target, record)));

            Revoke(roster, actor, target, record, [specialty], "Revoked specialty");
            Notify(target, Loc.GetString("faction-roster-notify-specialty-revoked", ("role", JobName(specialty))));
            return Loc.GetString("faction-roster-status-specialty-revoked",
                ("name", DisplayName(target, record)), ("role", JobName(specialty)));
        }

        Grant(roster, actor, target, record, specialty, "Granted specialty");
        Notify(target, Loc.GetString("faction-roster-notify-specialty-granted", ("role", JobName(specialty))));
        return Loc.GetString("faction-roster-status-specialty-granted",
            ("name", DisplayName(target, record)), ("role", JobName(specialty)));
    }

    private bool TryGetEditable(
        FactionRosterPrototype roster,
        ICommonSession actor,
        NetUserId target,
        int? track,
        out RosterRecord record,
        out Authority authority,
        out string? error)
    {
        record = default!;
        error = null;
        authority = GetAuthority(roster, actor);

        if (track is { } t && (t < 0 || t >= roster.Tracks.Count))
            return false;

        if (MergeActiveMembers(roster) is not { } cache || !cache.TryGetValue(target, out record!)) // #Cythisiax Edited - include on-duty members
        {
            error = Loc.GetString("faction-roster-status-not-member");
            return false;
        }

        if (target == actor.UserId)
        {
            error = Loc.GetString("faction-roster-status-self");
            return false;
        }

        if (IsLocked(roster, authority, actor, target, record))
        {
            error = Loc.GetString("faction-roster-status-locked", ("name", DisplayName(target, record)));
            return false;
        }

        return true;
    }

    // #Cythisiax Added - holding a rank counts if they hold it or anything above it on its track
    // (someone on duty as a Senior Knight without whitelists still counts as a Knight)
    private static bool HoldsOrAbove(FactionRosterPrototype roster, HashSet<string> held, string role)
    {
        if (held.Contains(role))
            return true;

        foreach (var track in roster.Tracks)
        {
            var index = track.Ranks.FindIndex(r => r.Id == role);
            if (index >= 0 && RankIndex(track, held) >= index)
                return true;
        }

        return false;
    }

    // #Cythisiax Added - track prerequisite: only checked for the track's first rank
    private static bool MeetsRequirement(FactionRosterPrototype roster, FactionRosterTrack track, int rank, HashSet<string> held)
    {
        return rank >= 0 || track.Requires is not { } needs || HoldsOrAbove(roster, held, needs.Id);
    }

    // #Cythisiax Added - losing the track's first rank just drops them back to its prerequisite
    private static bool FallsBackTo(FactionRosterPrototype roster, FactionRosterTrack track, HashSet<string> held)
    {
        return track.Requires is { } needs && HoldsOrAbove(roster, held, needs.Id);
    }

    // #Cythisiax Added - NCR / Enclave Commander+ (or an admin-granted roster) can move rank pins
    private bool CanManagePins(FactionRosterPrototype roster, ICommonSession viewer)
    {
        if (viewer.AttachedEntity is not { } body)
            return false;

        if (TryComp<FactionRosterComponent>(body, out var comp) && comp.IgnoreJobRequirement && comp.Roster == roster.ID)
            return true;

        return CurrentJob(body) is { } job && roster.RankManagers.Contains(job);
    }

    // #Cythisiax Added - a specialty rule names who may hand it out; without one the usual rank reach applies
    private bool CanToggleSpecialty(
        FactionRosterPrototype roster,
        Authority authority,
        ICommonSession viewer,
        NetUserId target,
        RosterRecord record,
        string role)
    {
        if (target == viewer.UserId || IsTracklessCommand(roster, record.Held()))
            return false;

        if (!roster.SpecialtyRules.TryGetValue(role, out var rule))
            return !IsLocked(roster, authority, viewer, target, record);

        if (rule.Requires is { } needs && !HoldsOrAbove(roster, record.Held(), needs.Id) && !record.Roles.Contains(role))
            return false;

        if (authority.Command)
            return true;

        var job = viewer.AttachedEntity is { } body ? CurrentJob(body) : null;
        return job != null && rule.Managers.Contains(job);
    }

    // #Cythisiax Added - move a member's rank pin one step; FactionRankSystem saves it on their character
    public string? ChangePinRank(FactionRosterPrototype roster, ICommonSession actor, NetUserId target, bool promote)
    {
        if (!CanManagePins(roster, actor))
            return Loc.GetString("faction-roster-status-pin-denied");

        if (target == actor.UserId)
            return Loc.GetString("faction-roster-status-self");

        if (MergeActiveMembers(roster) is not { } cache || !cache.TryGetValue(target, out var record))
            return Loc.GetString("faction-roster-status-not-member");

        if (IsTracklessCommand(roster, record.Held()))
            return Loc.GetString("faction-roster-status-locked", ("name", DisplayName(target, record)));

        if (!_player.TryGetSessionById(target, out var session) || session.AttachedEntity is not { } body)
            return Loc.GetString("faction-roster-status-not-member");

        if (!_factionRank.TryStepPinRank(body, session, promote, out var newRank))
        {
            return Loc.GetString(promote ? "faction-roster-status-max-rank" : "faction-roster-status-min-pin",
                ("name", DisplayName(target, record)));
        }

        LogChange(roster, actor, record, target, promote ? "Promoted pin" : "Demoted pin", newRank);
        Notify(target, Loc.GetString(promote ? "faction-roster-notify-promoted" : "faction-roster-notify-demoted", ("rank", newRank)));
        DirtyUis(roster.ID);

        return Loc.GetString(promote ? "faction-roster-status-promoted" : "faction-roster-status-demoted",
            ("name", DisplayName(target, record)), ("rank", newRank));
    }

    private void Grant(
        FactionRosterPrototype roster,
        ICommonSession actor,
        NetUserId target,
        RosterRecord record,
        ProtoId<JobPrototype> role,
        string verb)
    {
        _jobWhitelist.AddWhitelist(target, role);
        record.Roles.Add(role.Id);
        LogChange(roster, actor, record, target, verb, role.Id);
        DirtyUis(roster.ID);
    }

    private void Revoke(
        FactionRosterPrototype roster,
        ICommonSession actor,
        NetUserId target,
        RosterRecord record,
        List<ProtoId<JobPrototype>> roles,
        string verb)
    {
        foreach (var role in roles)
        {
            _jobWhitelist.RemoveWhitelist(target, role);
            record.Roles.Remove(role.Id);
        }

        LogChange(roster, actor, record, target, verb, string.Join(", ", roles));
        DirtyUis(roster.ID);
    }

    private void LogChange(
        FactionRosterPrototype roster,
        ICommonSession actor,
        RosterRecord record,
        NetUserId target,
        string verb,
        string roles)
    {
        var faction = Loc.GetString(roster.Name);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{actor:actor} ({faction} roster) {verb.ToLowerInvariant()} {record.UserName:subject} ({target}): {roles}");

        // Shows up in the admin whitelist log panel alongside admin-made changes.
        _whitelistLog.AddEntry(
            verb,
            $"{actor.Name} ({faction} roster)",
            record.UserName,
            roles);
    }

    /// <summary>Popup for the affected member, if they are in the round right now.</summary>
    private void Notify(NetUserId target, string message)
    {
        if (_player.TryGetSessionById(target, out var session) && session.AttachedEntity is { } body)
            _popup.PopupEntity(message, body, body, PopupType.Medium);
    }

    /// <summary>In-character name if the member is in the round, otherwise their account name.</summary>
    private string DisplayName(NetUserId target, RosterRecord record)
    {
        return _player.TryGetSessionById(target, out var session) && session.AttachedEntity is { } body
            ? Name(body)
            : record.UserName;
    }

    public string JobName(ProtoId<JobPrototype> job)
    {
        return _proto.TryIndex(job, out var proto) ? proto.LocalizedName : job.Id;
    }

    #endregion

    #region Oath

    /// <summary>The oath as leadership edits it, with [name] / [recruiter] placeholders.</summary>
    public string GetOathTemplate(FactionRosterPrototype roster)
    {
        return _oaths.TryGetValue(roster.ID, out var custom) ? custom : GetDefaultOath(roster);
    }

    private string GetDefaultOath(FactionRosterPrototype roster)
    {
        return BuildDefaultOath(roster, "[name]", "[recruiter]");
    }

    private string RenderOath(FactionRosterPrototype roster, string recruiter, string name)
    {
        return _oaths.TryGetValue(roster.ID, out var custom)
            ? custom.Replace("[name]", name).Replace("[recruiter]", recruiter)
            : BuildDefaultOath(roster, name, recruiter);
    }

    private string BuildDefaultOath(FactionRosterPrototype roster, string name, string recruiter)
    {
        return string.Join("\n\n", roster.OfferText.Select(text =>
            Loc.GetString(text, ("name", name), ("recruiter", recruiter)).Trim()));
    }

    /// <summary>Leadership saves a new oath; empty (or the default text) restores the default.</summary>
    public string? SetOath(FactionRosterPrototype roster, ICommonSession actor, string? text)
    {
        if (!CanEditOath(roster, actor))
            return Loc.GetString("faction-roster-status-oath-denied");

        var faction = Loc.GetString(roster.Name);
        text = text?.Trim().Replace("\r\n", "\n");

        if (string.IsNullOrEmpty(text) || text == GetDefaultOath(roster))
        {
            if (!_oaths.Remove(roster.ID))
                return Loc.GetString("faction-roster-status-oath-reset");

            SaveOaths();
            _adminLog.Add(LogType.Action, LogImpact.Low,
                $"{actor:actor} reset the {faction} recruitment oath to default");
            DirtyUis(roster.ID);
            return Loc.GetString("faction-roster-status-oath-reset");
        }

        if (text.Length > MaxOathLength)
            return Loc.GetString("faction-roster-status-oath-too-long", ("max", MaxOathLength));

        _oaths[roster.ID] = text;
        SaveOaths();
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{actor:actor} set the {faction} recruitment oath: {text}");
        DirtyUis(roster.ID);
        return Loc.GetString("faction-roster-status-oath-saved");
    }

    private void LoadOaths()
    {
        try
        {
            if (!_resources.UserData.Exists(OathPath))
                return;

            _oaths = JsonSerializer.Deserialize<Dictionary<string, string>>(
                         _resources.UserData.ReadAllText(OathPath))
                     ?? new Dictionary<string, string>();
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load faction roster oaths: {e}");
        }
    }

    private void SaveOaths()
    {
        try
        {
            _resources.UserData.WriteAllText(OathPath,
                JsonSerializer.Serialize(_oaths, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            Log.Error($"Failed to save faction roster oaths: {e}");
        }
    }

    #endregion

    #region Recruitment

    public string? OfferRecruitment(FactionRosterPrototype roster, EntityUid user, EntityUid target)
    {
        if (!CanBeRecruited(roster, target, out var targetSession))
            return Loc.GetString("faction-roster-status-cannot-recruit");

        // Must still be standing near the recruiter when the offer goes out.
        var userCoords = _transform.GetMapCoordinates(user);
        var targetCoords = _transform.GetMapCoordinates(target);
        if (userCoords.MapId != targetCoords.MapId ||
            (userCoords.Position - targetCoords.Position).Length() > roster.RecruitRange + 0.5f)
        {
            return Loc.GetString("faction-roster-status-too-far");
        }

        if (!_pendingOffers.Add(targetSession.UserId))
            return Loc.GetString("faction-roster-status-pending");

        var faction = Loc.GetString(roster.Name);
        var recruiterName = Identity.Name(user, EntityManager);
        var targetName = Identity.Name(target, EntityManager);
        var targetUserId = targetSession.UserId;
        _player.TryGetSessionByEntity(user, out var recruiterSession);

        _eui.OpenEui(new FactionRecruitOfferEui(
                faction,
                recruiterName,
                RenderOath(roster, recruiterName, targetName),
                () => AcceptRecruitment(roster, user, recruiterSession, target, targetUserId, targetName),
                () => DeclineRecruitment(roster, user, targetUserId, targetName)),
            targetSession);

        DirtyUis(roster.ID);
        return Loc.GetString("faction-roster-status-offer-sent", ("name", targetName));
    }

    private void AcceptRecruitment(
        FactionRosterPrototype roster,
        EntityUid user,
        ICommonSession? actor,
        EntityUid target,
        NetUserId targetUserId,
        string targetName)
    {
        _pendingOffers.Remove(targetUserId);

        // Re-validate: the target may have died or been recruited while the dialog was open.
        if (!Exists(target) || !CanBeRecruited(roster, target, out var session) || session.UserId != targetUserId)
        {
            DirtyUis(roster.ID);
            return;
        }

        if (roster.RecruitGrants.Count > 0 && actor != null)
        {
            if (!_cache.TryGetValue(roster.ID, out var cache))
            {
                cache = new Dictionary<NetUserId, RosterRecord>();
                _cache[roster.ID] = cache;
            }

            if (!cache.TryGetValue(targetUserId, out var record))
            {
                record = new RosterRecord(session.Name);
                cache[targetUserId] = record;
            }

            foreach (var grant in roster.RecruitGrants)
                Grant(roster, actor, targetUserId, record, grant, "Recruited");
        }

        // Faction-specific recruitment effects (e.g. Enclave's in-round recruit job).
        var ev = new FactionRosterRecruitedEvent(roster.ID, target, user);
        RaiseLocalEvent(ref ev);

        var faction = Loc.GetString(roster.Name);
        Notify(targetUserId, roster.RecruitGrants.Count > 0
            ? Loc.GetString("faction-roster-notify-recruited",
                ("faction", faction), ("rank", JobName(roster.RecruitGrants[0])))
            : Loc.GetString("faction-roster-notify-recruited-plain", ("faction", faction)));

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(user):actor} recruited {ToPrettyString(target):subject} into the {faction}");

        if (Exists(user))
        {
            _popup.PopupEntity(
                Loc.GetString("faction-roster-recruit-accepted", ("name", targetName), ("faction", faction)),
                user,
                user,
                PopupType.Medium);
        }

        DirtyUis(roster.ID);
    }

    private void DeclineRecruitment(FactionRosterPrototype roster, EntityUid user, NetUserId targetUserId, string targetName)
    {
        _pendingOffers.Remove(targetUserId);
        DirtyUis(roster.ID);

        if (Exists(user))
        {
            _popup.PopupEntity(
                Loc.GetString("faction-roster-recruit-declined", ("name", targetName)),
                user,
                user,
                PopupType.MediumCaution);
        }
    }

    #endregion
}
