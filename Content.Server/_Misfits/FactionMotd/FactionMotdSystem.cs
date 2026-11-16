// #Cythisiax Added - Faction MOTD: player-facing faction leadership (NCR Commander, Legate,
// Elder, Overseer, ...) write a message of the day for their faction through /factionmotd.
// Members see it in a window when they spawn in and can reopen it with /factionmotd.
// Membership is resolved from the player's current job against factionMotd prototypes, so
// jobs no faction claims (Wastelanders, FEV Mutants, ...) have no MOTD and cannot write one.
// MOTDs persist across rounds in server user data, like the faction roster oaths.

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Roles.Jobs;
using Content.Shared._Misfits.FactionMotd;
using Robust.Server.Player;
using Robust.Shared.ContentPack;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Misfits.FactionMotd;

public sealed class FactionMotdSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IResourceManager _resources = default!;
    [Dependency] private readonly SharedJobSystem _jobs = default!;
    [Dependency] private readonly SharedMindSystem _minds = default!;

    private static readonly ResPath SavePath = new("/faction_motds.json");

    /// <summary>Minimum time between two saves by the same player.</summary>
    private static readonly TimeSpan SaveCooldown = TimeSpan.FromSeconds(5);

    /// <summary>Saved MOTDs per factionMotd prototype ID.</summary>
    private Dictionary<string, MotdEntry> _motds = new();

    /// <summary>Job ID → factionMotd it belongs to. Rebuilt on prototype reload.</summary>
    private readonly Dictionary<string, FactionMotdPrototype> _memberJobs = new();

    /// <summary>Leadership job ID → factionMotd it may edit.</summary>
    private readonly Dictionary<string, FactionMotdPrototype> _leaderJobs = new();

    private readonly Dictionary<NetUserId, TimeSpan> _lastSave = new();

    public sealed class MotdEntry
    {
        public string Text { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }
    }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<FactionMotdOpenRequestMsg>(OnOpenRequest);
        SubscribeNetworkEvent<FactionMotdSetMsg>(OnSetRequest);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawned);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

        BuildJobMaps();
        Load();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<FactionMotdPrototype>() || args.WasModified<Content.Shared.Roles.DepartmentPrototype>())
            BuildJobMaps();
    }

    private void BuildJobMaps()
    {
        _memberJobs.Clear();
        _leaderJobs.Clear();

        // Sorted so a job claimed by two factions resolves the same way every time.
        foreach (var motd in _proto.EnumeratePrototypes<FactionMotdPrototype>().OrderBy(p => p.ID))
        {
            var excluded = motd.ExcludedJobs.Select(j => j.Id).ToHashSet();

            foreach (var deptId in motd.Departments)
            {
                if (!_proto.TryIndex(deptId, out var dept))
                    continue;

                foreach (var job in dept.Roles)
                {
                    if (!excluded.Contains(job.Id))
                        _memberJobs.TryAdd(job.Id, motd);
                }
            }

            foreach (var job in motd.Jobs)
                _memberJobs.TryAdd(job.Id, motd);

            // Leadership always belongs to the faction it leads, overriding any other claim.
            foreach (var job in motd.Leadership)
            {
                _memberJobs[job.Id] = motd;
                _leaderJobs.TryAdd(job.Id, motd);
            }
        }
    }

    // ── Membership ─────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the faction (and leadership) of the player's current character from its job.
    /// Ghosts and jobless characters are not in a faction.
    /// </summary>
    private bool TryGetFaction(ICommonSession session,
        [NotNullWhen(true)] out FactionMotdPrototype? faction,
        out bool leader,
        out string jobName)
    {
        faction = null;
        leader = false;
        jobName = string.Empty;

        if (session.Status != SessionStatus.InGame
            || session.AttachedEntity is not { } entity
            || HasComp<GhostComponent>(entity)
            || !_minds.TryGetMind(entity, out var mindId, out _)
            || !_jobs.MindTryGetJob(mindId, out _, out var job))
        {
            return false;
        }

        jobName = job.LocalizedName;
        return TryGetFactionForJob(job.ID, out faction, out leader);
    }

    private bool TryGetFactionForJob(string jobId,
        [NotNullWhen(true)] out FactionMotdPrototype? faction,
        out bool leader)
    {
        leader = _leaderJobs.ContainsKey(jobId);
        return _memberJobs.TryGetValue(jobId, out faction);
    }

    // ── Spawn popup ────────────────────────────────────────────────────────

    private void OnPlayerSpawned(PlayerSpawnCompleteEvent args)
    {
        if (args.JobId == null || !TryGetFactionForJob(args.JobId, out var faction, out var leader))
            return;

        var hasMotd = _motds.TryGetValue(faction.ID, out var entry) && entry.Text.Length > 0;

        if (hasMotd)
        {
            RaiseNetworkEvent(BuildState(faction, leader, onSpawn: true), args.Player);
            _chat.DispatchServerMessage(args.Player,
                Loc.GetString("faction-motd-chat-spawn", ("faction", FactionMarkup(faction))));
        }
        else if (leader)
        {
            // Nudge leadership when their faction has nothing posted yet.
            _chat.DispatchServerMessage(args.Player,
                Loc.GetString("faction-motd-chat-leader-empty", ("faction", FactionMarkup(faction))));
        }
    }

    // ── Window ─────────────────────────────────────────────────────────────

    private void OnOpenRequest(FactionMotdOpenRequestMsg msg, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;

        if (!TryGetFaction(session, out var faction, out var leader, out _))
        {
            RaiseNetworkEvent(new FactionMotdStateMsg { InFaction = false }, session);
            return;
        }

        RaiseNetworkEvent(BuildState(faction, leader, onSpawn: false), session);
    }

    private void OnSetRequest(FactionMotdSetMsg msg, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;

        if (!TryGetFaction(session, out var faction, out var leader, out var jobName))
        {
            RaiseNetworkEvent(new FactionMotdStateMsg { InFaction = false }, session);
            return;
        }

        if (!leader)
        {
            SendStatus(session, faction, false, Loc.GetString("faction-motd-status-denied"), leader: false);
            return;
        }

        var now = _timing.RealTime;
        if (_lastSave.TryGetValue(session.UserId, out var last) && now - last < SaveCooldown)
        {
            SendStatus(session, faction, true, Loc.GetString("faction-motd-status-cooldown"));
            return;
        }

        var text = (msg.Text ?? string.Empty).Replace("\r\n", "\n").Trim();
        if (text.Length > FactionMotdConfig.MaxLength)
        {
            SendStatus(session, faction, true,
                Loc.GetString("faction-motd-status-too-long", ("max", FactionMotdConfig.MaxLength)));
            return;
        }

        _lastSave[session.UserId] = now;
        var factionName = Loc.GetString(faction.Name);
        var characterName = Name(session.AttachedEntity!.Value);

        if (text.Length == 0)
        {
            if (!_motds.Remove(faction.ID))
            {
                SendStatus(session, faction, true, Loc.GetString("faction-motd-status-cleared"));
                return;
            }

            Save();
            _adminLog.Add(LogType.Action, LogImpact.Low,
                $"{session:actor} cleared the {factionName} faction MOTD");
            SendStatus(session, faction, true, Loc.GetString("faction-motd-status-cleared"));
            return;
        }

        if (_motds.TryGetValue(faction.ID, out var existing) && existing.Text == text)
        {
            SendStatus(session, faction, true, Loc.GetString("faction-motd-status-unchanged"));
            return;
        }

        _motds[faction.ID] = new MotdEntry
        {
            Text = text,
            Author = $"{characterName} ({jobName})",
            UpdatedAt = DateTime.UtcNow,
        };
        Save();

        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{session:actor} set the {factionName} faction MOTD: {text}");

        SendStatus(session, faction, true, Loc.GetString("faction-motd-status-saved"));
        NotifyMembers(faction, session, characterName);
    }

    /// <summary>Tells every other online member of the faction that the MOTD changed.</summary>
    private void NotifyMembers(FactionMotdPrototype faction, ICommonSession author, string authorName)
    {
        var line = Loc.GetString("faction-motd-chat-updated",
            ("faction", FactionMarkup(faction)),
            ("author", FormattedMessage.EscapeText(authorName)));

        foreach (var session in _player.Sessions)
        {
            if (session == author)
                continue;

            if (!TryGetFaction(session, out var memberFaction, out _, out _) || memberFaction.ID != faction.ID)
                continue;

            _chat.DispatchServerMessage(session, line);
        }
    }

    private void SendStatus(ICommonSession session, FactionMotdPrototype faction, bool success, string status, bool leader = true)
    {
        var state = BuildState(faction, leader, onSpawn: false);
        state.Status = status;
        state.StatusIsError = !success;
        RaiseNetworkEvent(state, session);
    }

    private FactionMotdStateMsg BuildState(FactionMotdPrototype faction, bool leader, bool onSpawn)
    {
        var state = new FactionMotdStateMsg
        {
            InFaction = true,
            FactionName = Loc.GetString(faction.Name),
            FactionColor = faction.Color,
            CanEdit = leader,
            OnSpawn = onSpawn,
        };

        if (_motds.TryGetValue(faction.ID, out var entry))
        {
            state.Text = entry.Text;
            state.Author = entry.Author;
            state.UpdatedAt = entry.UpdatedAt;
        }

        return state;
    }

    private string FactionMarkup(FactionMotdPrototype faction)
    {
        return $"[color={faction.Color.ToHex()}]{FormattedMessage.EscapeText(Loc.GetString(faction.Name))}[/color]";
    }

    // ── Persistence ────────────────────────────────────────────────────────

    private void Load()
    {
        try
        {
            if (!_resources.UserData.Exists(SavePath))
                return;

            _motds = JsonSerializer.Deserialize<Dictionary<string, MotdEntry>>(
                         _resources.UserData.ReadAllText(SavePath))
                     ?? new Dictionary<string, MotdEntry>();
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load faction MOTDs: {e}");
        }
    }

    private void Save()
    {
        try
        {
            _resources.UserData.WriteAllText(SavePath,
                JsonSerializer.Serialize(_motds, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            Log.Error($"Failed to save faction MOTDs: {e}");
        }
    }
}
