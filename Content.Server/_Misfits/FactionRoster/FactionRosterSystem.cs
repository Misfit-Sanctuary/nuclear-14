// #Cythisiax Added - Faction roster: lets faction leadership (e.g. DAGGER, the Chief Ranger,
// the Ranger Lead, Enclave officers) see every whitelisted member of their faction and recruit, promote, demote or
// discharge them, and set the oath shown to recruits. Membership is the persistent job
// whitelist, so every change writes to the database through JobWhitelistManager, exactly
// like the admin whitelist panel. Factions with extra recruitment rules hook in through
// FactionRosterRecruitAttemptEvent / FactionRosterRecruitedEvent.

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using Content.Server.Administration.Logs;
using Content.Server.Database;
using Content.Server.EUI;
using Content.Server.Players.JobWhitelist;
using Content.Server._Misfits.Administration.WhitelistLogs;
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
    }

    /// <summary>
    /// What the viewer may manage: ranks below Limits[track] on each track. Command roles
    /// (e.g. DAGGER) sit above every track and may manage all of it.
    /// </summary>
    private readonly record struct Authority(bool Command, int[] Limits, bool CanEditOath);

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
        if (!command)
        {
            for (var t = 0; t < roster.Tracks.Count; t++)
            {
                var index = job == null ? -1 : roster.Tracks[t].Ranks.IndexOf(job);
                limits[t] = Math.Max(index, 0);
            }
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

        return new Authority(command, limits, canEditOath);
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
        if (target == viewer.UserId || IsCommand(roster, record.Roles))
            return true;

        for (var t = 0; t < roster.Tracks.Count; t++)
        {
            if (RankIndex(roster.Tracks[t], record.Roles) >= authority.Limits[t])
                return true;
        }

        return false;
    }

    /// <summary>True if the member holds roster roles outside the given track.</summary>
    private static bool HoldsRolesOutside(FactionRosterPrototype roster, int track, RosterRecord record)
    {
        var trackRanks = roster.Tracks[track].Ranks.Select(r => r.Id).ToHashSet();
        return record.Roles.Any(r => !trackRanks.Contains(r));
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
            authority.CanEditOath);
    }

    private List<FactionRosterMember> BuildMembers(FactionRosterPrototype roster, Authority authority, ICommonSession viewer)
    {
        var members = new List<FactionRosterMember>();
        if (!_cache.TryGetValue(roster.ID, out var cache))
            return members;

        foreach (var (userId, record) in cache)
        {
            if (record.Roles.Count == 0)
                continue;

            string? characterName = null;
            var online = _player.TryGetSessionById(userId, out var session);

            // #Cythisiax Added - roster lists only members who are connected right now
            if (!online)
                continue;

            if (session!.AttachedEntity is { } body)
                characterName = Name(body);

            var locked = IsLocked(roster, authority, viewer, userId, record);

            var standings = new List<FactionRosterStanding>();
            for (var t = 0; t < roster.Tracks.Count; t++)
            {
                var rank = RankIndex(roster.Tracks[t], record.Roles);
                standings.Add(new FactionRosterStanding(
                    rank,
                    !locked && rank + 1 < authority.Limits[t],
                    !locked && rank >= 0 && (rank > 0 || HoldsRolesOutside(roster, t, record))));
            }

            members.Add(new FactionRosterMember(
                userId,
                record.UserName,
                characterName,
                online,
                IsCommand(roster, record.Roles),
                roster.Protected.Where(p => record.Roles.Contains(p.Id)).Select(p => p.Id).ToList(),
                locked,
                standings,
                roster.Specialties.Where(s => record.Roles.Contains(s.Id)).Select(s => s.Id).ToList()));
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
        var rank = RankIndex(roster.Tracks[track], record.Roles);
        if (rank + 1 >= authority.Limits[track])
            return Loc.GetString("faction-roster-status-max-rank", ("name", DisplayName(target, record)));

        var newRole = ranks[rank + 1];
        Grant(roster, actor, target, record, newRole, "Promoted");
        Notify(target, Loc.GetString("faction-roster-notify-promoted", ("rank", JobName(newRole))));
        return Loc.GetString("faction-roster-status-promoted",
            ("name", DisplayName(target, record)), ("rank", JobName(newRole)));
    }

    public string? Demote(FactionRosterPrototype roster, ICommonSession actor, NetUserId target, int track)
    {
        if (!TryGetEditable(roster, actor, target, track, out var record, out _, out var error))
            return error;

        var ranks = roster.Tracks[track].Ranks;
        var rank = RankIndex(roster.Tracks[track], record.Roles);
        if (rank < 0 || rank == 0 && !HoldsRolesOutside(roster, track, record))
            return Loc.GetString("faction-roster-status-min-rank", ("name", DisplayName(target, record)));

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
        if (!TryGetEditable(roster, actor, target, null, out var record, out _, out var error))
            return error;

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

        if (!TryGetEditable(roster, actor, target, null, out var record, out _, out var error))
            return error;

        if (record.Roles.Contains(role))
        {
            // Removing their only role would silently drop them off the roster; that's Discharge.
            if (record.Roles.Count == 1)
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

        if (!_cache.TryGetValue(roster.ID, out var cache) || !cache.TryGetValue(target, out record!))
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
