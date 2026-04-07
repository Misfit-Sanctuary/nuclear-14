// #Misfits Add - Server-side faction war system.
// Handles GUI form submissions from clients (declare/ceasefire/warjoin) and the admin /warend command.
// Active war state is maintained here and broadcast to all clients on every change.
// Wars go through a 5-minute Pending phase (during which /warjoin is open) before becoming Active.

using System.Linq;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Server.Mind;
using Content.Server.Roles.Jobs;
using Content.Shared._Misfits.FactionWar;
using Content.Shared.GameTicking;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Enums;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.FactionWar;

/// <summary>
/// Manages player-driven faction war declarations, ceasefires, and individual war enlistment.
/// Rules enforced here (all game-logic stays server-side):
///   - One war per faction at a time (blocking both as aggressor and as target).
///   - Only the highest job-weight online member of the declaring faction may act.
///   - Wars enter a 5-minute Pending phase before becoming Active.
///   - During Pending, non-faction players may /warjoin on either side.
///   - /warend is admin-only and ends any specific war immediately.
/// </summary>
public sealed class FactionWarSystem : EntitySystem
{
    [Dependency] private readonly IAdminManager    _adminManager  = default!;
    [Dependency] private readonly IChatManager     _chat          = default!;
    [Dependency] private readonly IConsoleHost     _conHost       = default!;
    [Dependency] private readonly JobSystem        _jobs          = default!;
    [Dependency] private readonly MindSystem       _minds         = default!;
    [Dependency] private readonly NpcFactionSystem _npcFaction    = default!;
    [Dependency] private readonly IPlayerManager   _playerManager = default!;
    [Dependency] private readonly IGameTiming      _gameTiming    = default!;

    // ── Constants ──────────────────────────────────────────────────────────

    /// <summary>Minimum elapsed round time before war can be declared.</summary>
    private static readonly TimeSpan WarCooldownAfterRoundStart = TimeSpan.FromMinutes(30);

    /// <summary>How long a war stays in Pending before becoming Active.</summary>
    private static readonly TimeSpan WarPrepDuration = TimeSpan.FromMinutes(5);

    /// <summary>Minimum word count for casus belli.</summary>
    private const int MinCasusBelliWords = 5;

    /// <summary>Cooldown after a war ends before the same faction can declare again.</summary>
    private static readonly TimeSpan WarCooldownAfterEnd = TimeSpan.FromMinutes(10);

    // #Misfits Add - Maximum duration an active war can last before auto-ceasefire.
    private static readonly TimeSpan WarMaxDuration = TimeSpan.FromMinutes(30);

    // ── State ──────────────────────────────────────────────────────────────

    private readonly List<FactionWarEntry> _activeWars = new();
    private TimeSpan _roundStartTime;

    /// <summary>Activation times for pending wars. Key = WarKey(aggressor, target).</summary>
    private readonly Dictionary<string, TimeSpan> _warActivationTimes = new();

    /// <summary>
    /// Individual players who enlisted via /warjoin.
    /// Key = player UserId, Value = (warKey, side faction ID).
    /// </summary>
    private readonly Dictionary<NetUserId, (string WarKey, string Side)> _warParticipants = new();

    /// <summary>Per-faction cooldown after a war ends. Key = faction ID, Value = earliest next war time.</summary>
    private readonly Dictionary<string, TimeSpan> _factionWarCooldowns = new();

    // #Misfits Add - Auto-ceasefire deadline for active wars. Key = WarKey, Value = expiry time.
    private readonly Dictionary<string, TimeSpan> _warAutoEndTimes = new();

    /// <summary>Interval between periodic participant broadcasts (keeps overlay fresh).</summary>
    private static readonly TimeSpan ParticipantBroadcastInterval = TimeSpan.FromSeconds(2);
    private TimeSpan _nextParticipantBroadcast;

    // #Misfits Tweak - Gate Update() to 1 Hz. The body only walks in-memory lists (_activeWars,
    // _warActivationTimes) and delegates to BroadcastParticipants which has its own TimeSpan
    // gate. 1 s resolution for war-phase transitions is imperceptible.
    private float _warUpdateAccumulator;
    private const float WarUpdateInterval = 1.0f;

    /// <summary>
    /// Sessions that currently have the /war panel open.
    /// Panel data is only sent to these sessions on state change, avoiding O(N) broadcasts.
    /// </summary>
    private readonly HashSet<ICommonSession> _panelOpenSessions = new();

    // ── Lifecycle ──────────────────────────────────────────────────────────

    public override void Initialize()
    {
        base.Initialize();

        // Admin-only: force-end a war from the server console.
        _conHost.RegisterCommand(
            "warend",
            "Forcibly end an active war between two factions.",
            "warend <aggressorFactionId> <targetFactionId>",
            WarEndCommand);

        // Admin-only: force-declare a war, bypassing 30-min cooldown and rank checks.
        _conHost.RegisterCommand(
            "forcewar",
            "Force-declare a war between two factions (admin, bypasses cooldown/rank).",
            "forcewar <aggressorFactionId> <targetFactionId> [casusBelli...]",
            ForceWarCommand);

        // Receive GUI form submissions from clients.
        SubscribeNetworkEvent<FactionWarOpenPanelRequestEvent>(OnPanelRequest);
        SubscribeNetworkEvent<FactionWarDeclareRequestEvent>(OnDeclareRequest);
        SubscribeNetworkEvent<FactionWarCeasefireRequestEvent>(OnCeasefireRequest);

        // Warjoin panel & enlistment.
        SubscribeNetworkEvent<FactionWarJoinPanelRequestEvent>(OnWarJoinPanelRequest);
        SubscribeNetworkEvent<FactionWarJoinRequestEvent>(OnWarJoinRequest);

        // Admin force-war GUI.
        SubscribeNetworkEvent<FactionWarForceRequestEvent>(OnForceWarRequest);
        SubscribeNetworkEvent<FactionWarForceCeasefireRequestEvent>(OnForceCeasefireRequest);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    // ── Tick: transition Pending → Active, auto-ceasefire, participant broadcast ─

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // #Misfits Tweak - 1 Hz gate; war phase transitions are seconds-scale.
        _warUpdateAccumulator += frameTime;
        if (_warUpdateAccumulator < WarUpdateInterval)
            return;
        _warUpdateAccumulator -= WarUpdateInterval;

        var now = _gameTiming.CurTime;

        // Periodically re-broadcast the full participant dict so the client
        // overlay stays in sync as entities spawn, move, or leave.
        if (_activeWars.Count > 0)
        {
            if (now >= _nextParticipantBroadcast)
            {
                _nextParticipantBroadcast = now + ParticipantBroadcastInterval;
                BroadcastParticipants();
            }
        }

        // ── Pending → Active transitions ──────────────────────────────
        if (_warActivationTimes.Count > 0)
        {
            var activated = new List<FactionWarEntry>();

            foreach (var war in _activeWars)
            {
                if (war.Phase != WarPhase.Pending)
                    continue;

                var key = WarKey(war);
                if (!_warActivationTimes.TryGetValue(key, out var activationTime))
                    continue;

                if (now < activationTime)
                    continue;

                war.Phase = WarPhase.Active;
                _warActivationTimes.Remove(key);
                activated.Add(war);

                // #Misfits Add - Start the 30-minute auto-ceasefire countdown.
                _warAutoEndTimes[key] = now + WarMaxDuration;
            }

            if (activated.Count > 0)
            {
                BroadcastWarState();
                SendPanelDataToAll();

                foreach (var war in activated)
                {
                    var aggDisplay = FactionDisplayName(war.AggressorFaction);
                    var tgtDisplay = FactionDisplayName(war.TargetFaction);

                    _chat.DispatchServerAnnouncement(
                        $"WAR HAS BEGUN\n" +
                        $"The conflict between {aggDisplay} and {tgtDisplay} is now active!\n" +
                        $"(/warjoin) is now closed for this conflict.\n" +
                        $"Auto-ceasefire in 30 minutes.",
                        Color.OrangeRed);
                }
            }
        }

        // ── Auto-ceasefire expiry check ───────────────────────────────
        // #Misfits Add - End wars that have exceeded WarMaxDuration.
        if (_warAutoEndTimes.Count > 0)
        {
            var expired = new List<FactionWarEntry>();

            foreach (var war in _activeWars)
            {
                if (war.Phase != WarPhase.Active)
                    continue;

                var key = WarKey(war);
                if (!_warAutoEndTimes.TryGetValue(key, out var endTime))
                    continue;

                if (now >= endTime)
                    expired.Add(war);
            }

            foreach (var war in expired)
            {
                var aggDisplay = FactionDisplayName(war.AggressorFaction);
                var tgtDisplay = FactionDisplayName(war.TargetFaction);

                _chat.DispatchServerAnnouncement(
                    Loc.GetString("faction-war-auto-ceasefire-announcement",
                        ("aggressor", aggDisplay),
                        ("target", tgtDisplay)),
                    Color.SkyBlue);

                _chat.SendAdminAnnouncement(
                    $"[FactionWar] Auto-ceasefire triggered: {aggDisplay} vs {tgtDisplay} (30-minute limit).");

                RemoveWar(war);
            }
        }
    }

    // ── GUI: Panel data request ─────────────────────────────────────────

    private void OnPanelRequest(FactionWarOpenPanelRequestEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        _panelOpenSessions.Add(player); // Track that this session has the panel open.
        SendPanelData(player);
    }

    private void SendPanelData(ICommonSession player)
    {
        var data = new FactionWarPanelDataEvent
        {
            ActiveWars = new List<FactionWarEntry>(_activeWars),
            MyFactionDisplay = Loc.GetString("faction-war-no-faction"),
        };

        if (player.AttachedEntity is { } playerEntity)
        {
            if (TryGetWarFaction(playerEntity, out var factionId))
            {
                data.MyFactionId      = factionId;
                data.MyFactionDisplay = FactionDisplayName(factionId);
            }
        }

        // Check 30-minute cooldown.
        var elapsed = _gameTiming.CurTime - _roundStartTime;
        if (elapsed < WarCooldownAfterRoundStart)
        {
            var remaining = WarCooldownAfterRoundStart - elapsed;
            data.StatusMessage = $"War declarations are locked for the first 30 minutes. " +
                                 $"{remaining.Minutes}m {remaining.Seconds}s remaining.";
        }

        // Compute factions already at war.
        var factionsAtWar = new HashSet<string>();
        foreach (var w in _activeWars)
        {
            factionsAtWar.Add(w.AggressorFaction);
            factionsAtWar.Add(w.TargetFaction);
        }

        // Eligible targets: war-capable, not self, not already in a war.
        foreach (var f in FactionWarConfig.WarCapableFactions)
        {
            if (f == data.MyFactionId || factionsAtWar.Contains(f))
                continue;

            data.EligibleTargets.Add(new FactionWarTargetInfo
            {
                Id          = f,
                DisplayName = FactionDisplayName(f),
            });
        }

        data.EligibleTargets.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal));

        // Ceasefire targets: wars involving player's faction (both pending and active).
        if (data.MyFactionId != null)
        {
            foreach (var war in _activeWars)
            {
                if (war.AggressorFaction == data.MyFactionId)
                    data.CeasefireTargets.Add(new FactionWarTargetInfo { Id = war.TargetFaction, DisplayName = FactionDisplayName(war.TargetFaction) });
                else if (war.TargetFaction == data.MyFactionId)
                    data.CeasefireTargets.Add(new FactionWarTargetInfo { Id = war.AggressorFaction, DisplayName = FactionDisplayName(war.AggressorFaction) });
            }
        }

        RaiseNetworkEvent(data, player);
    }

    // ── GUI: Declare War ───────────────────────────────────────────────────

    private void OnDeclareRequest(FactionWarDeclareRequestEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;

        if (player.Status != SessionStatus.InGame ||
            player.AttachedEntity is not { } playerEntity)
        {
            SendResult(player, false, "You must be in-game to declare war.");
            return;
        }

        // 30-minute round-start cooldown.
        var elapsed = _gameTiming.CurTime - _roundStartTime;
        if (elapsed < WarCooldownAfterRoundStart)
        {
            var remaining = WarCooldownAfterRoundStart - elapsed;
            SendResult(player, false,
                $"War declarations are locked for the first 30 minutes. {remaining.Minutes}m {remaining.Seconds}s remaining.");
            return;
        }

        var targetFactionId = msg.TargetFaction.Trim();
        var casusBelli      = msg.CasusBelli.Trim();

        if (casusBelli.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < MinCasusBelliWords)
        {
            SendResult(player, false, $"Casus belli must be at least {MinCasusBelliWords} words.");
            return;
        }

        if (!TryGetWarFaction(playerEntity, out var myFactionId))
        {
            SendResult(player, false, "You are not a member of any war-capable faction.");
            return;
        }

        // Per-faction cooldown after a previous war ended.
        var now = _gameTiming.CurTime;
        if (_factionWarCooldowns.TryGetValue(myFactionId, out var myCooldown) && now < myCooldown)
        {
            var rem = myCooldown - now;
            SendResult(player, false,
                $"Your faction recently ended a war. Cooldown: {rem.Minutes}m {rem.Seconds}s remaining.");
            return;
        }
        if (_factionWarCooldowns.TryGetValue(targetFactionId, out var tgtCooldown) && now < tgtCooldown)
        {
            var rem = tgtCooldown - now;
            SendResult(player, false,
                $"{FactionDisplayName(targetFactionId)} recently ended a war. Cooldown: {rem.Minutes}m {rem.Seconds}s remaining.");
            return;
        }

        if (!FactionWarConfig.WarCapableFactions.Contains(targetFactionId))
        {
            SendResult(player, false, $"'{targetFactionId}' is not a valid faction.");
            return;
        }

        if (targetFactionId == myFactionId)
        {
            SendResult(player, false, "You cannot declare war on your own faction.");
            return;
        }

        if (IsFactionInWar(myFactionId))
        {
            SendResult(player, false, "Your faction is already at war. Declare a ceasefire first.");
            return;
        }

        if (IsFactionInWar(targetFactionId))
        {
            SendResult(player, false,
                $"{FactionDisplayName(targetFactionId)} is already engaged in a war.");
            return;
        }

        if (!_minds.TryGetMind(playerEntity, out var mindId, out _))
        {
            SendResult(player, false, "You have no mind entity.");
            return;
        }

        var myWeight  = GetJobWeight(mindId);
        var topWeight = GetFactionTopWeight(myFactionId);
        if (myWeight < topWeight)
        {
            // Include rank info so the player knows who outranks them.
            var myJob = _jobs.MindTryGetJobName(mindId);
            var topHolder = GetFactionTopJobHolder(myFactionId);
            SendResult(player, false,
                $"Only the highest-ranking member online can declare war. " +
                $"Your rank: {myJob} (weight {myWeight}). " +
                $"Outranked by: {topHolder} (weight {topWeight}).");
            return;
        }

        var entry = new FactionWarEntry
        {
            AggressorFaction      = myFactionId,
            TargetFaction         = targetFactionId,
            CasusBelli            = casusBelli,
            DeclarerCharacterName = Name(playerEntity),
            DeclarerJobName       = _jobs.MindTryGetJobName(mindId),
            Phase                 = WarPhase.Pending,
        };

        _activeWars.Add(entry);

        // Start the 5-minute preparation timer.
        var warKey = WarKey(entry);
        _warActivationTimes[warKey] = _gameTiming.CurTime + WarPrepDuration;

        BroadcastWarState();
        SendPanelDataToAll();

        var aggressorDisplay = FactionDisplayName(myFactionId);
        var targetDisplay    = FactionDisplayName(targetFactionId);

        _chat.DispatchServerAnnouncement(
            $"WAR DECLARED\n" +
            $"{aggressorDisplay} has declared war on {targetDisplay}!\n" +
            $"Casus Belli: \"{casusBelli}\"\n" +
            $"{entry.DeclarerCharacterName}, {entry.DeclarerJobName}\n\n" +
            $"War begins in 5 minutes. Use (/warjoin) to choose a side.",
            Color.OrangeRed);

        _chat.SendAdminAnnouncement(
            $"[FactionWar] {player.Name} ({entry.DeclarerCharacterName}) declared war:" +
            $" {aggressorDisplay} vs {targetDisplay}. Casus: {casusBelli}");

        SendResult(player, true,
            $"War declared on {targetDisplay}. War begins in 5 minutes. /warjoin is open.");
    }

    // ── GUI: Ceasefire ─────────────────────────────────────────────────────

    private void OnCeasefireRequest(FactionWarCeasefireRequestEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;

        if (player.Status != SessionStatus.InGame ||
            player.AttachedEntity is not { } playerEntity)
        {
            SendResult(player, false, "You must be in-game to declare a ceasefire.");
            return;
        }

        var targetFactionId = msg.TargetFaction.Trim();

        if (!TryGetWarFaction(playerEntity, out var myFactionId))
        {
            SendResult(player, false, "You are not a member of any war-capable faction.");
            return;
        }

        var war = _activeWars.FirstOrDefault(w =>
            (w.AggressorFaction == myFactionId && w.TargetFaction == targetFactionId) ||
            (w.TargetFaction    == myFactionId && w.AggressorFaction == targetFactionId));

        if (war == null)
        {
            SendResult(player, false,
                $"No active war found between your faction and {FactionDisplayName(targetFactionId)}.");
            return;
        }

        if (!_minds.TryGetMind(playerEntity, out var ceaseMindId, out _))
        {
            SendResult(player, false, "You have no mind entity.");
            return;
        }

        var ceaseMyWeight  = GetJobWeight(ceaseMindId);
        var ceaseTopWeight = GetFactionTopWeight(myFactionId);
        if (ceaseMyWeight < ceaseTopWeight)
        {
            var myJob = _jobs.MindTryGetJobName(ceaseMindId);
            var topHolder = GetFactionTopJobHolder(myFactionId);
            SendResult(player, false,
                $"Only the highest-ranking member online can declare a ceasefire. " +
                $"Your rank: {myJob} (weight {ceaseMyWeight}). " +
                $"Outranked by: {topHolder} (weight {ceaseTopWeight}).");
            return;
        }

        RemoveWar(war);

        var aggressorDisplay = FactionDisplayName(war.AggressorFaction);
        var targetDisplay    = FactionDisplayName(war.TargetFaction);
        var charName         = Name(playerEntity);
        var jobName          = _jobs.MindTryGetJobName(ceaseMindId);

        _chat.DispatchServerAnnouncement(
            $"CEASEFIRE\n" +
            $"{aggressorDisplay} and {targetDisplay} have agreed to a ceasefire.\n" +
            $"However, escalation and tensions still exist.",
            Color.SkyBlue);

        _chat.SendAdminAnnouncement(
            $"[FactionWar] {player.Name} ({charName}) called ceasefire:" +
            $" {aggressorDisplay} vs {targetDisplay}");

        SendResult(player, true,
            $"Ceasefire declared. The conflict with {targetDisplay} has ended.");
    }

    // ── GUI: Warjoin panel data ────────────────────────────────────────────

    private void OnWarJoinPanelRequest(FactionWarJoinPanelRequestEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        var data = new FactionWarJoinPanelDataEvent();

        // #Misfits Change - Faction members can now individually warjoin. Populate
        // IsTopRanking / MyWarFactionId so the client knows whether to show faction-wide enlist.
        if (player.AttachedEntity is { } playerEntity && TryGetWarFaction(playerEntity, out var playerFaction))
        {
            data.MyWarFactionId = playerFaction;

            if (_minds.TryGetMind(playerEntity, out var mindId, out _))
            {
                var myWeight  = GetJobWeight(mindId);
                var topWeight = GetFactionTopWeight(playerFaction);
                data.IsTopRanking = myWeight > 0 && myWeight >= topWeight;
            }
        }

        if (_warParticipants.TryGetValue(player.UserId, out var info))
            data.AlreadyJoinedSide = info.Side;

        data.PendingWars = _activeWars.Where(w => w.Phase == WarPhase.Pending).ToList();

        if (data.PendingWars.Count == 0 && data.AlreadyJoinedSide == null)
            data.StatusMessage = Loc.GetString("faction-war-join-no-pending");

        RaiseNetworkEvent(data, player);
    }

    // ── GUI: Warjoin enlistment ────────────────────────────────────────────

    private void OnWarJoinRequest(FactionWarJoinRequestEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;

        if (player.Status != SessionStatus.InGame ||
            player.AttachedEntity is not { } playerEntity)
        {
            SendJoinResult(player, false, "You must be in-game to join a war.");
            return;
        }

        // #Misfits Change - Removed the AlreadyInFaction rejection. Faction members can now
        // individually warjoin. The old block was:
        // if (TryGetWarFaction(playerEntity, out var existingFaction))
        //     SendJoinResult(player, false, "You are already a member of...");

        if (_warParticipants.ContainsKey(player.UserId))
        {
            SendJoinResult(player, false, "You have already joined a war.");
            return;
        }

        var war = _activeWars.FirstOrDefault(w =>
            w.AggressorFaction == msg.AggressorFaction &&
            w.TargetFaction == msg.TargetFaction &&
            w.Phase == WarPhase.Pending);

        if (war == null)
        {
            SendJoinResult(player, false, "That war is not in a joinable state (must be pending).");
            return;
        }

        if (msg.ChosenSide != war.AggressorFaction && msg.ChosenSide != war.TargetFaction)
        {
            SendJoinResult(player, false, "Invalid side selection.");
            return;
        }

        // #Misfits Add - Faction-wide enlistment: top-ranking member enlists all online faction members.
        if (msg.FactionWide)
        {
            HandleFactionWideJoin(player, playerEntity, war, msg.ChosenSide);
            return;
        }

        // Individual join path (available to everyone).
        var warKey = WarKey(war);
        _warParticipants[player.UserId] = (warKey, msg.ChosenSide);
        BroadcastParticipants();

        var sideName = FactionDisplayName(msg.ChosenSide);
        var charName = Name(playerEntity);

        SendJoinResult(player, true,
            $"You have joined the war on the side of {sideName}. You are now KOS to the enemy.");

        _chat.SendAdminAnnouncement(
            $"[FactionWar] {player.Name} ({charName}) joined war " +
            $"{FactionDisplayName(msg.AggressorFaction)} vs {FactionDisplayName(msg.TargetFaction)} " +
            $"on the side of {sideName}");
    }

    /// <summary>
    /// Enlists all online members of the sender's war-capable faction into a pending war.
    /// Only the highest-ranking online member may trigger this.
    /// Wastelanders are never auto-enlisted.
    /// </summary>
    private void HandleFactionWideJoin(
        ICommonSession player,
        EntityUid playerEntity,
        FactionWarEntry war,
        string chosenSide)
    {
        // Verify the sender is in a war-capable faction (not Wastelander).
        if (!TryGetWarFaction(playerEntity, out var myFaction))
        {
            SendJoinResult(player, false, "You are not in a war-capable faction.");
            return;
        }

        // Verify sender is the top-ranking online member.
        if (!_minds.TryGetMind(playerEntity, out var senderMind, out _))
        {
            SendJoinResult(player, false, "You have no mind entity.");
            return;
        }

        var senderWeight = GetJobWeight(senderMind);
        var topWeight = GetFactionTopWeight(myFaction);
        if (senderWeight < topWeight)
        {
            SendJoinResult(player, false,
                "Only the highest-ranking online member can enlist the entire faction.");
            return;
        }

        // Build the set of NPC faction IDs to iterate (canonical + aliases).
        var factionIds = new List<string> { myFaction };
        foreach (var (raw, canonical) in FactionWarConfig.FactionAliases)
        {
            if (canonical == myFaction)
                factionIds.Add(raw);
        }

        var warKey = WarKey(war);
        var enlisted = 0;

        // Iterate all online faction members and add them to _warParticipants.
        var query = EntityQueryEnumerator<NpcFactionMemberComponent, ActorComponent>();
        while (query.MoveNext(out var entity, out _, out var actor))
        {
            if (actor.PlayerSession.Status != SessionStatus.InGame)
                continue;

            var isMember = false;
            foreach (var fid in factionIds)
            {
                if (_npcFaction.IsMember(entity, fid))
                {
                    isMember = true;
                    break;
                }
            }
            if (!isMember)
                continue;

            var userId = actor.PlayerSession.UserId;

            // Skip if already enlisted in any war.
            if (_warParticipants.ContainsKey(userId))
                continue;

            _warParticipants[userId] = (warKey, chosenSide);
            enlisted++;
        }

        BroadcastParticipants();

        var sideName = FactionDisplayName(chosenSide);
        var factionDisplay = FactionDisplayName(myFaction);
        var charName = Name(playerEntity);

        SendJoinResult(player, true,
            $"Enlisted {enlisted} members of {factionDisplay} on the side of {sideName}.");

        _chat.DispatchServerAnnouncement(
            $"FACTION ENLISTMENT\n" +
            $"{charName} has enlisted all of {factionDisplay} into the war " +
            $"({FactionDisplayName(war.AggressorFaction)} vs {FactionDisplayName(war.TargetFaction)}) " +
            $"on the side of {sideName}!",
            Color.Orange);

        _chat.SendAdminAnnouncement(
            $"[FactionWar] {player.Name} ({charName}) faction-wide enlisted {factionDisplay} " +
            $"({enlisted} members) in war {FactionDisplayName(war.AggressorFaction)} vs " +
            $"{FactionDisplayName(war.TargetFaction)} on the side of {sideName}");
    }

    // ── /warend (admin only) ───────────────────────────────────────────────

    private void WarEndCommand(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is { } player && !_adminManager.IsAdmin(player))
        {
            shell.WriteError("You must be an admin to use this command.");
            return;
        }

        if (args.Length < 2)
        {
            shell.WriteError("Usage: warend <aggressorFactionId> <targetFactionId>");
            return;
        }

        var aggressorId = args[0].Trim();
        var targetId    = args[1].Trim();

        var war = _activeWars.FirstOrDefault(w =>
            w.AggressorFaction == aggressorId && w.TargetFaction == targetId);

        if (war == null)
        {
            shell.WriteError(
                $"No active war found: {FactionDisplayName(aggressorId)} vs {FactionDisplayName(targetId)}.");
            return;
        }

        RemoveWar(war);

        var adminName        = shell.Player?.Name ?? "Server";
        var aggressorDisplay = FactionDisplayName(aggressorId);
        var targetDisplay    = FactionDisplayName(targetId);

        _chat.DispatchServerAnnouncement(
            $"WAR ENDED BY COMMAND\n" +
            $"The conflict between {aggressorDisplay} and {targetDisplay} has been resolved.",
            Color.LightGray);

        _chat.SendAdminAnnouncement(
            $"[FactionWar] Admin {adminName} ended war: {aggressorDisplay} vs {targetDisplay}");
    }

    /// <summary>
    /// Admin-only /forcewar: declares a war between two factions, bypassing the 30-minute
    /// round-start cooldown and rank/faction-membership checks. Useful for testing.
    /// </summary>
    private void ForceWarCommand(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is { } player && !_adminManager.IsAdmin(player))
        {
            shell.WriteError("You must be an admin to use this command.");
            return;
        }

        if (args.Length < 2)
        {
            shell.WriteError("Usage: forcewar <aggressorFactionId> <targetFactionId> [casusBelli...]");
            return;
        }

        var aggressorId = args[0].Trim();
        var targetId    = args[1].Trim();

        // Validate both are war-capable factions.
        if (!FactionWarConfig.WarCapableFactions.Contains(aggressorId))
        {
            shell.WriteError($"'{aggressorId}' is not a war-capable faction. Valid: {string.Join(", ", FactionWarConfig.WarCapableFactions)}");
            return;
        }
        if (!FactionWarConfig.WarCapableFactions.Contains(targetId))
        {
            shell.WriteError($"'{targetId}' is not a war-capable faction. Valid: {string.Join(", ", FactionWarConfig.WarCapableFactions)}");
            return;
        }
        if (aggressorId == targetId)
        {
            shell.WriteError("Aggressor and target cannot be the same faction.");
            return;
        }
        if (IsFactionInWar(aggressorId))
        {
            shell.WriteError($"{FactionDisplayName(aggressorId)} is already in a war.");
            return;
        }
        if (IsFactionInWar(targetId))
        {
            shell.WriteError($"{FactionDisplayName(targetId)} is already in a war.");
            return;
        }

        // Optional casus belli from remaining args, default if omitted.
        var casus = args.Length > 2
            ? string.Join(" ", args.Skip(2))
            : "Admin-forced war (testing)";

        var adminName = shell.Player?.Name ?? "Server";

        var entry = new FactionWarEntry
        {
            AggressorFaction      = aggressorId,
            TargetFaction         = targetId,
            CasusBelli            = casus,
            DeclarerCharacterName = adminName,
            DeclarerJobName       = "Admin",
            Phase                 = WarPhase.Pending,
        };

        _activeWars.Add(entry);

        // Start the 5-minute preparation timer.
        var warKey = WarKey(entry);
        _warActivationTimes[warKey] = _gameTiming.CurTime + WarPrepDuration;

        BroadcastWarState();
        SendPanelDataToAll();

        var aggDisplay = FactionDisplayName(aggressorId);
        var tgtDisplay = FactionDisplayName(targetId);

        _chat.DispatchServerAnnouncement(
            $"WAR DECLARED\n" +
            $"{aggDisplay} has declared war on {tgtDisplay}!\n" +
            $"Casus Belli: \"{casus}\"\n" +
            $"{adminName}, Admin\n\n" +
            $"War begins in 5 minutes. Use (/warjoin) to choose a side.",
            Color.OrangeRed);

        _chat.SendAdminAnnouncement(
            $"[FactionWar] Admin {adminName} force-declared war: {aggDisplay} vs {tgtDisplay}. Casus: {casus}");

        shell.WriteLine($"War declared: {aggDisplay} vs {tgtDisplay} (pending 5 min).");
    }

    // ── GUI: Admin Force War ───────────────────────────────────────────────

    /// <summary>
    /// Handles the admin Force War GUI request. Same logic as ForceWarCommand but
    /// receives input from the client GUI and sends result feedback back.
    /// </summary>
    private void OnForceWarRequest(FactionWarForceRequestEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;

        // Admin check — reject non-admins silently.
        if (!_adminManager.IsAdmin(player))
        {
            RaiseNetworkEvent(new FactionWarForceResultEvent
                { Success = false, Message = "You must be an admin to use this." }, player);
            return;
        }

        var aggressorId = msg.AggressorFaction.Trim();
        var targetId    = msg.TargetFaction.Trim();
        var casus       = msg.CasusBelli.Trim();

        if (string.IsNullOrEmpty(casus))
            casus = "Admin-forced war (testing)";

        if (!FactionWarConfig.WarCapableFactions.Contains(aggressorId))
        {
            RaiseNetworkEvent(new FactionWarForceResultEvent
                { Success = false, Message = $"'{aggressorId}' is not a war-capable faction." }, player);
            return;
        }
        if (!FactionWarConfig.WarCapableFactions.Contains(targetId))
        {
            RaiseNetworkEvent(new FactionWarForceResultEvent
                { Success = false, Message = $"'{targetId}' is not a war-capable faction." }, player);
            return;
        }
        if (aggressorId == targetId)
        {
            RaiseNetworkEvent(new FactionWarForceResultEvent
                { Success = false, Message = "Aggressor and target cannot be the same faction." }, player);
            return;
        }
        if (IsFactionInWar(aggressorId))
        {
            RaiseNetworkEvent(new FactionWarForceResultEvent
                { Success = false, Message = $"{FactionDisplayName(aggressorId)} is already in a war." }, player);
            return;
        }
        if (IsFactionInWar(targetId))
        {
            RaiseNetworkEvent(new FactionWarForceResultEvent
                { Success = false, Message = $"{FactionDisplayName(targetId)} is already in a war." }, player);
            return;
        }

        var adminName = player.Name;

        var entry = new FactionWarEntry
        {
            AggressorFaction      = aggressorId,
            TargetFaction         = targetId,
            CasusBelli            = casus,
            DeclarerCharacterName = adminName,
            DeclarerJobName       = "Admin",
            Phase                 = WarPhase.Pending,
        };

        _activeWars.Add(entry);

        var warKey = WarKey(entry);
        _warActivationTimes[warKey] = _gameTiming.CurTime + WarPrepDuration;

        BroadcastWarState();
        SendPanelDataToAll();

        var aggDisplay = FactionDisplayName(aggressorId);
        var tgtDisplay = FactionDisplayName(targetId);

        _chat.DispatchServerAnnouncement(
            $"WAR DECLARED\n" +
            $"{aggDisplay} has declared war on {tgtDisplay}!\n" +
            $"Casus Belli: \"{casus}\"\n" +
            $"{adminName}, Admin\n\n" +
            $"War begins in 5 minutes. Use (/warjoin) to choose a side.",
            Color.OrangeRed);

        _chat.SendAdminAnnouncement(
            $"[FactionWar] Admin {adminName} force-declared war: {aggDisplay} vs {tgtDisplay}. Casus: {casus}");

        RaiseNetworkEvent(new FactionWarForceResultEvent
            { Success = true, Message = $"War declared: {aggDisplay} vs {tgtDisplay} (pending 5 min)." }, player);
    }

    /// <summary>
    /// Handles the admin Force Ceasefire GUI request. Ends any active/pending war immediately.
    /// </summary>
    private void OnForceCeasefireRequest(FactionWarForceCeasefireRequestEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;

        if (!_adminManager.IsAdmin(player))
        {
            RaiseNetworkEvent(new FactionWarForceResultEvent
                { Success = false, Message = "You must be an admin to use this.", IsCeasefire = true }, player);
            return;
        }

        var aggressorId = msg.AggressorFaction.Trim();
        var targetId    = msg.TargetFaction.Trim();

        var war = _activeWars.FirstOrDefault(w =>
            w.AggressorFaction == aggressorId && w.TargetFaction == targetId);

        if (war == null)
        {
            RaiseNetworkEvent(new FactionWarForceResultEvent
                { Success = false, Message = $"No active war found between those factions.", IsCeasefire = true }, player);
            return;
        }

        RemoveWar(war);

        var adminName = player.Name;
        var aggDisplay = FactionDisplayName(aggressorId);
        var tgtDisplay = FactionDisplayName(targetId);

        _chat.DispatchServerAnnouncement(
            $"WAR ENDED BY COMMAND\n" +
            $"The conflict between {aggDisplay} and {tgtDisplay} has been resolved.",
            Color.LightGray);

        _chat.SendAdminAnnouncement(
            $"[FactionWar] Admin {adminName} force-ended war: {aggDisplay} vs {tgtDisplay}");

        RaiseNetworkEvent(new FactionWarForceResultEvent
            { Success = true, Message = $"War ended: {aggDisplay} vs {tgtDisplay}.", IsCeasefire = true }, player);
    }

    // ── Round lifecycle ────────────────────────────────────────────────────

    private void OnRoundRestart(RoundRestartCleanupEvent _)
    {
        _activeWars.Clear();
        _warActivationTimes.Clear();
        _warAutoEndTimes.Clear(); // #Misfits Add - Clear auto-ceasefire timers on round restart.
        _warParticipants.Clear();
        _factionWarCooldowns.Clear();
        _panelOpenSessions.Clear();
        _nextParticipantBroadcast = TimeSpan.Zero;
        _roundStartTime = _gameTiming.CurTime;
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        // Clean up panel tracking when a player disconnects.
        if (e.NewStatus == SessionStatus.Disconnected)
        {
            _panelOpenSessions.Remove(e.Session);
            return;
        }

        if (e.NewStatus != SessionStatus.InGame)
            return;

        if (_activeWars.Count > 0)
        {
            RaiseNetworkEvent(
                new FactionWarStateUpdatedEvent { ActiveWars = new List<FactionWarEntry>(_activeWars) },
                e.Session);
        }

        if (_warParticipants.Count > 0)
            SendParticipantsTo(e.Session);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Removes a war and all associated state (timer, participants), then broadcasts.
    /// </summary>
    private void RemoveWar(FactionWarEntry war)
    {
        _activeWars.Remove(war);

        var warKey = WarKey(war);
        _warActivationTimes.Remove(warKey);

        // #Misfits Add - Cancel auto-ceasefire timer if one was running.
        _warAutoEndTimes.Remove(warKey);

        // Set per-faction cooldown so neither side can immediately re-declare.
        var cooldownEnd = _gameTiming.CurTime + WarCooldownAfterEnd;
        _factionWarCooldowns[war.AggressorFaction] = cooldownEnd;
        _factionWarCooldowns[war.TargetFaction]    = cooldownEnd;

        // Remove all participants enlisted for this specific war.
        var toRemove = _warParticipants
            .Where(kvp => kvp.Value.WarKey == warKey)
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var userId in toRemove)
            _warParticipants.Remove(userId);

        BroadcastWarState();
        BroadcastParticipants();
        SendPanelDataToAll();
    }

    private void BroadcastWarState()
    {
        RaiseNetworkEvent(
            new FactionWarStateUpdatedEvent { ActiveWars = new List<FactionWarEntry>(_activeWars) },
            Filter.Broadcast());
    }

    private void SendResult(ICommonSession session, bool success, string message)
    {
        RaiseNetworkEvent(
            new FactionWarCommandResultEvent { Success = success, Message = message },
            session);
    }

    private void SendJoinResult(ICommonSession session, bool success, string message)
    {
        RaiseNetworkEvent(
            new FactionWarJoinResultEvent { Success = success, Message = message },
            session);
    }

    /// <summary>
    /// Sends panel data only to sessions that have the /war panel open,
    /// avoiding expensive per-player faction checks for everyone on the server.
    /// </summary>
    private void SendPanelDataToAll()
    {
        // Remove stale sessions before iterating.
        _panelOpenSessions.RemoveWhere(s => s.Status != SessionStatus.InGame);

        foreach (var session in _panelOpenSessions)
        {
            SendPanelData(session);
        }
    }

    /// <summary>
    /// Builds a NetEntity → side dictionary from ALL war-relevant entities:
    /// NPC faction members in active-war factions AND individual /warjoin participants.
    /// This is needed because NpcFactionMemberComponent.Factions is NOT synced to clients,
    /// so the client overlay cannot check faction membership itself.
    /// </summary>
    private Dictionary<NetEntity, string> BuildParticipantDict()
    {
        var dict = new Dictionary<NetEntity, string>();

        // Collect the set of faction IDs currently at war.
        var warFactions = new HashSet<string>();
        foreach (var war in _activeWars)
        {
            warFactions.Add(war.AggressorFaction);
            warFactions.Add(war.TargetFaction);
            // Include aliases (e.g. Rangers → NCR) so those members are found too.
            foreach (var (raw, canonical) in FactionWarConfig.FactionAliases)
            {
                if (canonical == war.AggressorFaction || canonical == war.TargetFaction)
                    warFactions.Add(raw);
            }
        }

        // Pass 1: All NPC faction members whose faction is involved in a war.
        var factionQuery = EntityQueryEnumerator<NpcFactionMemberComponent>();
        while (factionQuery.MoveNext(out var uid, out _))
        {
            // #Misfits Add - Skip entities whose job is in the overlay-exempt list (e.g. Frumentarii spies).
            if (_minds.TryGetMind(uid, out var exemptMind, out _)
                && _jobs.MindTryGetJob(exemptMind, out _, out var exemptProto)
                && FactionWarConfig.OverlayExemptJobs.Contains(exemptProto.ID))
                continue;

            foreach (var fId in warFactions)
            {
                if (!_npcFaction.IsMember(uid, fId))
                    continue;

                // Resolve alias to canonical war faction (e.g. Rangers → NCR).
                var canonical = FactionWarConfig.ResolveWarFaction(fId);
                dict[GetNetEntity(uid)] = canonical;
                break; // first match wins
            }
        }

        // Pass 2: Individual /warjoin participants (may overlap with pass 1 — their side wins).
        var sessionByUserId = new Dictionary<NetUserId, ICommonSession>();
        foreach (var session in _playerManager.Sessions)
            sessionByUserId[session.UserId] = session;

        foreach (var (userId, (_, side)) in _warParticipants)
        {
            if (!sessionByUserId.TryGetValue(userId, out var session))
                continue;
            if (session.AttachedEntity is not { } entity)
                continue;
            dict[GetNetEntity(entity)] = side;
        }

        return dict;
    }

    private void BroadcastParticipants()
    {
        var dict = BuildParticipantDict();
        RaiseNetworkEvent(
            new FactionWarParticipantsUpdatedEvent { Participants = dict },
            Filter.Broadcast());
    }

    private void SendParticipantsTo(ICommonSession session)
    {
        var dict = BuildParticipantDict();
        RaiseNetworkEvent(
            new FactionWarParticipantsUpdatedEvent { Participants = dict },
            session);
    }

    private bool IsFactionInWar(string factionId) =>
        _activeWars.Any(w => w.AggressorFaction == factionId || w.TargetFaction == factionId);

    private bool TryGetWarFaction(EntityUid entity, out string factionId)
    {
        factionId = string.Empty;
        // Check all NPC faction IDs (including aliases like Rangers) and resolve to canonical war faction.
        foreach (var f in FactionWarConfig.AllWarFactionIds)
        {
            if (_npcFaction.IsMember(entity, f))
            {
                factionId = FactionWarConfig.ResolveWarFaction(f);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Gets the highest job weight among online members of a war faction,
    /// including members of any alias factions (e.g. Rangers when factionId is NCR).
    /// </summary>
    private int GetFactionTopWeight(string factionId)
    {
        // Build the set of NPC faction IDs to check (canonical + any aliases).
        var factionIds = new List<string> { factionId };
        foreach (var (raw, canonical) in FactionWarConfig.FactionAliases)
        {
            if (canonical == factionId)
                factionIds.Add(raw);
        }

        var top = 0;
        var query = EntityQueryEnumerator<NpcFactionMemberComponent, ActorComponent>();
        while (query.MoveNext(out var entity, out _, out var actor))
        {
            if (actor.PlayerSession.Status != SessionStatus.InGame)
                continue;

            var isMember = false;
            foreach (var fid in factionIds)
            {
                if (_npcFaction.IsMember(entity, fid))
                {
                    isMember = true;
                    break;
                }
            }
            if (!isMember)
                continue;

            if (!_minds.TryGetMind(entity, out var mindId, out _))
                continue;
            var w = GetJobWeight(mindId);
            if (w > top)
                top = w;
        }
        return top;
    }

    private int GetJobWeight(EntityUid mindId) =>
        _jobs.MindTryGetJob(mindId, out _, out var proto) ? proto.Weight : 0;

    /// <summary>
    /// Returns the job name of the highest-weight online member of the given faction.
    /// Used for diagnostic messages when a player is blocked from declaring war/ceasefire.
    /// </summary>
    private string GetFactionTopJobHolder(string factionId)
    {
        var factionIds = new List<string> { factionId };
        foreach (var (raw, canonical) in FactionWarConfig.FactionAliases)
        {
            if (canonical == factionId)
                factionIds.Add(raw);
        }

        var topWeight = 0;
        var topName = "Unknown";
        var query = EntityQueryEnumerator<NpcFactionMemberComponent, ActorComponent>();
        while (query.MoveNext(out var entity, out _, out var actor))
        {
            if (actor.PlayerSession.Status != SessionStatus.InGame)
                continue;

            var isMember = false;
            foreach (var fid in factionIds)
            {
                if (_npcFaction.IsMember(entity, fid))
                {
                    isMember = true;
                    break;
                }
            }
            if (!isMember)
                continue;

            if (!_minds.TryGetMind(entity, out var mindId, out _))
                continue;
            var w = GetJobWeight(mindId);
            if (w > topWeight)
            {
                topWeight = w;
                topName = _jobs.MindTryGetJobName(mindId);
            }
        }
        return topName;
    }

    public static string FactionDisplayName(string factionId) =>
        FactionWarConfig.FactionDisplayName(factionId);

    private static string WarKey(FactionWarEntry war) =>
        $"{war.AggressorFaction}|{war.TargetFaction}";
}
