// #Misfits Refactor - Server-side player-to-player war system.
// Handles GUI form submissions from clients (declare/ceasefire/warjoin) and the admin /forcewar command.
// Active war state is maintained here and broadcast to all clients on every change.
// Wars go through a 5-minute Pending phase (during which /warjoin is open) before becoming Active.
// Acceptance is optional - wars auto-activate after the Pending phase regardless of whether the target accepts.
// A war is bound to two character entities: the declarer and the declared-against (character-bound, not account-bound).
// /warjoin is still supported; certain factions auto-enlist their members on declaration.
// Only original 2 players can participate in raids during the war.
// Either original player can propose/accept ceasefire to end the war.

using System.Linq;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Server.Mind;
using Content.Server.Roles.Jobs;
using Content.Shared._Misfits.FactionWar;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.FactionWar;

/// <summary>
/// Manages player-to-player war declarations, ceasefires, and individual war participation.
/// Rules enforced here (all game-logic stays server-side):
///   - Any two different players can declare war on each other.
///   - Wars enter a 5-minute Pending phase before becoming Active (during which /warjoin is open).
///   - Acceptance is optional - wars auto-activate regardless of whether the target accepts.
///   - During Pending, any player may /warjoin on either side (except the original 2).
///   - Once Active, /warjoin is closed.
///   - Raids require both participants to be part of the original 2 players in the war.
///   - Only original 2 players can end the war (via ceasefire proposal).
///   - War history is logged for every event (declaration, acceptance, join, ceasefire).
/// </summary>
public sealed class FactionWarSystem : EntitySystem
{
    [Dependency] private readonly IAdminManager    _adminManager  = default!;
    [Dependency] private readonly IChatManager     _chat          = default!;
    [Dependency] private readonly IConsoleHost     _conHost       = default!;
    [Dependency] private readonly IPlayerManager   _playerManager = default!;
    [Dependency] private readonly IGameTiming      _gameTiming    = default!;
    [Dependency] private readonly JobSystem         _jobs          = default!;
    [Dependency] private readonly MindSystem        _minds         = default!;
    [Dependency] private readonly NpcFactionSystem  _npcFaction    = default!;
    [Dependency] private readonly SharedPopupSystem _popup         = default!;
    [Dependency] private readonly SharedStunSystem  _stun          = default!;

    // ── Constants ──────────────────────────────────────────────────────────

    /// <summary>Minimum elapsed round time before war can be declared.</summary>
    /// <summary>Minimum elapsed round time before war can be declared.</summary>
    private static readonly TimeSpan WarCooldownAfterRoundStart = TimeSpan.FromMinutes(0);

    /// <summary>How long a war stays in Pending before becoming Active.</summary>
    /// <summary>How long a war stays in Pending before becoming Active.</summary>
    private static readonly TimeSpan WarPrepDuration = TimeSpan.FromMinutes(5);

    /// <summary>Minimum word count for war reason/casus belli.</summary>
    private const int MinReasonWords = 5;

    /// <summary>Cooldown after a war ends before same player can declare again.</summary>
    private static readonly TimeSpan WarCooldownAfterEnd = TimeSpan.FromMinutes(10);

    /// <summary>Cooldown after a ceasefire rejection before the proposer can request again.</summary>
    private static readonly TimeSpan CeasefireCooldownAfterRejection = TimeSpan.FromMinutes(20);

    /// <summary>Factions that auto-enlist their members when a war is declared.</summary>
    private static readonly HashSet<string> AutoEnlistFactions = new()
    {
        "NCR",
        "CaesarLegion",
        "BrotherhoodOfSteel",
        "Tribal",
        "Vault",
        "Enclave",
        "ZAX",
    };

    /// <summary>Jobs that should never be auto-enlisted even if their faction is at war.</summary>
    private static readonly HashSet<string> AutoEnlistJobExemptions = new()
    {
        "NCRPrisoner",
        "CaesarLegionSlave",
        "CaesarLegionFrumentarii",
        "CaesarLegionCitizen",
        "CaesarLegionTrader",
        "NCRCitizen",
        "NCRTrader",
        "Townsperson",
        "BoSOffDuty",
    };

    /// <summary>Major factions — each can only be in ONE war at a time.</summary>
    private static readonly HashSet<string> MajorFactions = new()
    {
        "NCR",
        "CaesarLegion",
        "BrotherhoodOfSteel",
        "Enclave",
    };


    // ── State ──────────────────────────────────────────────────────────────

    private readonly Dictionary<string, PlayerWarEntry> _activeWars = new();
    private TimeSpan _roundStartTime;

    /// <summary>Activation times for pending wars. Key = WarKey (canonical player pair).</summary>
    private readonly Dictionary<string, TimeSpan> _warActivationTimes = new();

    /// <summary>
    /// War acceptance prompts pending from target players.
    /// Key = WarKey, Value = (expiry time, declared by player info).
    /// </summary>
    private readonly Dictionary<string, WarAcceptancePrompt> _pendingAcceptancePrompts = new();

    /// <summary>
    /// All war participants (original 2 + joiners + auto-enlisted), keyed by character entity.
    /// Character-bound: a new character (entity) is not party to a previous character's war.
    /// </summary>
    private readonly Dictionary<NetEntity, (string WarKey, byte Side)> _warParticipants = new();

    /// <summary>Per-player cooldown after a war ends. Key = player UID, Value = earliest next war time.</summary>
    private readonly Dictionary<string, TimeSpan> _playerWarCooldowns = new();

    /// <summary>Per-player cooldown after ceasefire rejection. Key = player UID, Value = earliest next request time.</summary>
    private readonly Dictionary<string, TimeSpan> _ceasefireCooldowns = new();

    /// <summary>Ceasefire proposals awaiting the other player's consent. Key = WarKey.</summary>
    private readonly Dictionary<string, CeasefireProposal> _pendingCeasefireProposals = new();

    /// <summary>
    /// Sessions that currently have the /war panel open.
    /// Panel data is only sent to these sessions on state change, avoiding O(N) broadcasts.
    /// </summary>
    private readonly HashSet<ICommonSession> _panelOpenSessions = new();

    /// <summary>Participants who have surrendered, keyed by character entity.</summary>
    private readonly HashSet<NetEntity> _surrenderedParticipants = new();

    /// <summary>
    /// #Misfits Add - Observer participants can see overlay tags but don't get one rendered on them.
    /// Used by admins to monitor wars without appearing as [ALLY]/[ENEMY].
    /// </summary>
    private readonly HashSet<NetEntity> _observerParticipants = new();

    // ── Performance gates ──────────────────────────────────────────────────

    private float _participantResyncAccumulator;
    private const float ParticipantResyncInterval = 30f;

    private float _warUpdateAccumulator;
    private const float WarUpdateInterval = 1.0f;

    private readonly List<PlayerWarEntry> _activatedScratch = new();

    // ── Lifecycle ──────────────────────────────────────────────────────────

    public override void Initialize()
    {
        base.Initialize();

        // Admin-only: force-end a war between two players.
        _conHost.RegisterCommand(
            "warend",
            "Forcibly end an active war between two players.",
            "warend <player1_name_or_uid> <player2_name_or_uid>",
            WarEndCommand);

        // Admin-only: force-declare a war, bypassing cooldown and checks.
        _conHost.RegisterCommand(
            "forcewar",
            "Force-declare a war between two players (admin, bypasses cooldown).",
            "forcewar <player1_name_or_uid> <player2_name_or_uid> [reason...]",
            ForceWarCommand);

        // Admin-only: force a player into an existing war on a chosen side.
        _conHost.RegisterCommand(
            "forcewarjoin",
            "Force a player into the same war side as another player.",
            "forcewarjoin <target_username> <ally_username>",
            ForceWarJoinCommand,
            ForceWarJoinCompletion);

        // #Misfits Add - Admin-only: observe a war without appearing as [ALLY]/[ENEMY].
        _conHost.RegisterCommand(
            "forceobservewar",
            "Observe a war from a participant's perspective. See overlay tags without getting one yourself.",
            "forceobservewar <observer_username> <war_participant_username>",
            ForceObserveWarCommand,
            ForceObserveWarCompletion);

        // Receive GUI form submissions from clients.
        SubscribeNetworkEvent<FactionWarOpenPanelRequestEvent>(OnPanelRequest);
        SubscribeNetworkEvent<PlayerWarDeclareRequestEvent>(OnDeclareRequest);
        SubscribeNetworkEvent<PlayerWarCeasefireRequestEvent>(OnCeasefireRequest);

        // War acceptance/rejection.
        SubscribeNetworkEvent<PlayerWarAcceptEvent>(OnAcceptWar);
        SubscribeNetworkEvent<PlayerWarRejectEvent>(OnRejectWar);

        // Ceasefire proposal responses.
        SubscribeNetworkEvent<CeasefireAcceptedEvent>(OnAcceptCeasefireProposal);
        SubscribeNetworkEvent<CeasefireRejectedEvent>(OnRejectCeasefireProposal);

        // Warjoin panel & enlistment.
        SubscribeNetworkEvent<FactionWarJoinPanelRequestEvent>(OnWarJoinPanelRequest);
        SubscribeNetworkEvent<PlayerWarJoinRequestEvent>(OnWarJoinRequest);

        // Surrender.
        SubscribeNetworkEvent<PlayerWarSurrenderRequestEvent>(OnSurrenderRequest);

        // Admin force-war GUI.
        SubscribeNetworkEvent<PlayerWarForceRequestEvent>(OnForceWarRequest);
        SubscribeNetworkEvent<PlayerWarForceCeasefireRequestEvent>(OnForceCeasefireRequest);
        SubscribeNetworkEvent<PlayerWarForceObserveRequestEvent>(OnForceObserveRequest); // #Misfits Add

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<MindContainerComponent, MindAddedMessage>(OnMindAdded);
        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    // ── Tick: transition Pending → Active, handle timeouts, broadcast participants ──

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _warUpdateAccumulator += frameTime;
        if (_warUpdateAccumulator < WarUpdateInterval)
            return;
        _warUpdateAccumulator -= WarUpdateInterval;

        var now = _gameTiming.CurTime;

        // ── Ceasefire proposal timeouts (auto-accept on expiry) ──────────
        if (_pendingCeasefireProposals.Count > 0)
        {
            List<string>? expiredCeaseKeys = null;
            foreach (var (key, prop) in _pendingCeasefireProposals)
            {
                if (now >= prop.ExpiresAt)
                {
                    expiredCeaseKeys ??= new List<string>();
                    expiredCeaseKeys.Add(key);
                }
            }
            if (expiredCeaseKeys != null)
            {
                foreach (var key in expiredCeaseKeys)
                {
                    var prop = _pendingCeasefireProposals[key];
                    _pendingCeasefireProposals.Remove(key);
                    RemoveWar(prop.War);
                    _chat.DispatchServerAnnouncement(
                        $"CEASEFIRE ACCEPTED\n" +
                        $"No response was received in time. {prop.War.SideName1} and {prop.War.SideName2} have agreed to a ceasefire.",
                        Color.SkyBlue);
                }
            }
        }

        // ── Pending → Active transitions ──────────────────────────────────
        if (_warActivationTimes.Count > 0)
        {
            var activated = _activatedScratch;
            activated.Clear();

            foreach (var (key, activationTime) in _warActivationTimes.ToList())
            {
                if (now < activationTime)
                    continue;

                if (!_activeWars.TryGetValue(key, out var war))
                    continue;

                war.Phase = WarPhase.Active;
                _warActivationTimes.Remove(key);
                activated.Add(war);
            }

            if (activated.Count > 0)
            {
                BroadcastWarState();
                SendPanelDataToAll();

                // Ensure clients receive the participant mapping for newly-activated wars
                // so overlays will appear immediately even if no joiners have been sent.
                BroadcastParticipants();

                foreach (var war in activated)
                {
                    _chat.DispatchServerAnnouncement(
                        $"WAR HAS BEGUN\n" +
                        $"The conflict between {war.SideName1} and {war.SideName2} is now active!\n" +
                        $"(/warjoin) is now closed for this conflict.\n" +
                        $"The war will only end by ceasefire.",
                        Color.OrangeRed);
                }
            }
        }

        // ── Death-triggered ceasefire ─────────────────────────────────────
        // Check if either original war principal has died; end the war if so.
        // Polled here to avoid event subscription conflicts with other systems.
        /*foreach (var war in _activeWars.Values.ToList())
        {
            var e1 = GetEntity(war.DeclaredByEntity);
            var e2 = GetEntity(war.DeclaredAgainstEntity);

            EntityUid deadUid = default;
            string deadName = string.Empty;

            if (TryComp<MobStateComponent>(e1, out var ms1) && ms1.CurrentState == MobState.Dead)
            {
                deadUid = e1;
                deadName = war.DeclaredByCharacterName;
            }
            else if (TryComp<MobStateComponent>(e2, out var ms2) && ms2.CurrentState == MobState.Dead)
            {
                deadUid = e2;
                deadName = war.DeclaredAgainstCharacterName;
            }

            if (deadUid == default)
                continue;

            var side1 = war.SideName1;
            var side2 = war.SideName2;
            RemoveWar(war);

            _chat.DispatchServerAnnouncement(
                $"WAR ENDED\n" +
                $"{deadName} has died. The conflict between {side1} and {side2} is over.\n" +
                $"Any ongoing raids have been ended.",
                Color.Gray);
            break;
        }*/

        // Safety resync: re-broadcast participant dict every 30 s
        if (_activeWars.Count > 0)
        {
            _participantResyncAccumulator += WarUpdateInterval;
            if (_participantResyncAccumulator >= ParticipantResyncInterval)
            {
                _participantResyncAccumulator = 0f;
                BroadcastParticipants();
            }
        }
        else
        {
            _participantResyncAccumulator = 0f;
        }
    }

    // ── GUI: Panel data request ─────────────────────────────────────────

    private void OnPanelRequest(FactionWarOpenPanelRequestEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        _panelOpenSessions.Add(player);
        SendPanelData(player);
    }

    private void SendPanelData(ICommonSession player)
    {
        var data = new PlayerWarPanelDataEvent
        {
            ActiveWars = _activeWars.Values.ToList(),
            MyCharacterName = player.AttachedEntity is { } myEntity ? Name(myEntity) : null,
            MyWars = new List<PlayerWarEntry>(),
        };

        // Populate online players for targeting
        foreach (var session in _playerManager.Sessions)
        {
            if (session.Status != SessionStatus.InGame || session.UserId == player.UserId)
                continue;

            if (session.AttachedEntity is not { } entity)
                continue;

            data.OnlinePlayers.Add(new OnlinePlayerInfo
            {
                UserId = session.UserId,
                UserName = session.Name,
                CharacterName = Name(entity),
            });
        }
        data.OnlinePlayers.Sort((a, b) => string.Compare(a.CharacterName, b.CharacterName, StringComparison.Ordinal));

        // Check 30-minute cooldown.
        var elapsed = _gameTiming.CurTime - _roundStartTime;
        if (elapsed < WarCooldownAfterRoundStart)
        {
            var remaining = WarCooldownAfterRoundStart - elapsed;
            data.StatusMessage = $"War declarations are locked for the first 30 minutes. " +
                                 $"{remaining.Minutes}m {remaining.Seconds}s remaining.";
        }

        // Check per-player cooldown.
        if (data.StatusMessage == null && _playerWarCooldowns.TryGetValue(player.UserId.ToString(), out var cooldownEnd))
        {
            if (_gameTiming.CurTime < cooldownEnd)
            {
                var remaining = cooldownEnd - _gameTiming.CurTime;
                data.StatusMessage = $"Cooldown: {remaining.Minutes}m {remaining.Seconds}s remaining.";
            }
        }

        // Wars where this character is a participant (character-bound)
        if (player.AttachedEntity is { } panelCharacter)
        {
            var panelEntity = GetNetEntity(panelCharacter);
            foreach (var war in _activeWars.Values)
            {
                if (war.Participants.ContainsKey(panelEntity))
                    data.MyWars.Add(war);
            }
        }

        RaiseNetworkEvent(data, player);
    }

    // ── GUI: Declare War ───────────────────────────────────────────────────

    private void OnDeclareRequest(PlayerWarDeclareRequestEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;

        if (player.Status != SessionStatus.InGame || player.AttachedEntity is not { } playerEntity)
        {
            SendResult(player, false, "You must be in-game to declare war.");
            return;
        }

        // #Misfits Fix - Ghosts cannot declare war
        if (HasComp<GhostComponent>(playerEntity))
        {
            SendResult(player, false, "Ghosts cannot declare war.");
            return;
        }

        if (msg.TargetPlayer == player.UserId)
        {
            SendResult(player, false, "You cannot declare war on yourself.");
            return;
        }

        var elapsed = _gameTiming.CurTime - _roundStartTime;
        if (elapsed < WarCooldownAfterRoundStart)
        {
            var remaining = WarCooldownAfterRoundStart - elapsed;
            SendResult(player, false,
                $"War declarations are locked for the first 30 minutes. {remaining.Minutes}m {remaining.Seconds}s remaining.");
            return;
        }

        var reason = msg.Reason.Trim();
        if (reason.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < MinReasonWords)
        {
            SendResult(player, false, $"War reason must be at least {MinReasonWords} words.");
            return;
        }

        var now = _gameTiming.CurTime;
        var myUidStr = player.UserId.ToString();
        if (_playerWarCooldowns.TryGetValue(myUidStr, out var myCooldown) && now < myCooldown)
        {
            var rem = myCooldown - now;
            SendResult(player, false,
                $"Cooldown: {rem.Minutes}m {rem.Seconds}s remaining.");
            return;
        }

        // Check if target exists and is online with an active character
        if (!TryGetSessionForPlayer(msg.TargetPlayer, out var targetSession))
        {
            SendResult(player, false, "Target player is not online.");
            return;
        }

        if (targetSession.AttachedEntity is not { } targetEntity)
        {
            SendResult(player, false, "Target player is not yet in-game.");
            return;
        }

        // #Misfits Fix - Cannot declare war on ghosts
        if (HasComp<GhostComponent>(targetEntity))
        {
            SendResult(player, false, "Cannot declare war on ghosts.");
            return;
        }

        // Character-bound: check by entity, not account
        var playerNetEntity = GetNetEntity(playerEntity);
        var targetNetEntity = GetNetEntity(targetEntity);

        if (_warParticipants.ContainsKey(playerNetEntity))
        {
            SendResult(player, false, "You are already in a war.");
            return;
        }

        if (_warParticipants.ContainsKey(targetNetEntity))
        {
            SendResult(player, false, "Target player is already in a war.");
            return;
        }

        // #Misfits Add - Same-faction war check
        TryGetAutoEnlistFaction(playerEntity, out var declarerFaction);
        TryGetAutoEnlistFaction(targetEntity, out var targetFaction);

        if (declarerFaction != null
            && targetFaction != null
            && string.Equals(declarerFaction, targetFaction, StringComparison.OrdinalIgnoreCase))
        {
            SendResult(player, false, "Your faction cannot wage war on itself.");
            return;
        }

        // #Misfits Add - Major faction already-in-war check
        foreach (var faction in new[] { declarerFaction, targetFaction })
        {
            if (faction == null || !MajorFactions.Contains(faction))
                continue;

            if (IsFactionInAnyWar(faction))
            {
                SendResult(player, false, $"{faction} is already committed to a war.");
                return;
            }
        }

        // Create war entry (entity-bound: war key derived from character entities)
        var declaredAgainstCharacterName = Name(targetEntity);

        var warEntry = new PlayerWarEntry
        {
            DeclaredByPlayer = player.UserId,
            DeclaredByEntity = playerNetEntity,
            DeclaredByCharacterName = Name(playerEntity),
            DeclaredByJobName = "Unknown",
            DeclaredAgainstPlayer = msg.TargetPlayer,
            DeclaredAgainstEntity = targetNetEntity,
            DeclaredAgainstCharacterName = declaredAgainstCharacterName,
            SideName1 = msg.SideName1.Trim(),
            SideName2 = string.IsNullOrWhiteSpace(declaredAgainstCharacterName)
                ? "Player 2's Side"
                : $"{declaredAgainstCharacterName}'s Side",
            Reason = reason,
            Phase = WarPhase.Pending,
        };

        // Add original participants with their sides (entity-keyed)
        warEntry.Participants[playerNetEntity] = 1;
        warEntry.Participants[targetNetEntity] = 2;

        // Track originals in _warParticipants for fast O(1) "already in war" checks
        var warKey = warEntry.WarKey;
        _warParticipants[playerNetEntity] = (warKey, 1);
        _warParticipants[targetNetEntity] = (warKey, 2);

        // Add history event
        warEntry.History.Add(new WarHistoryEvent
        {
            EventType = WarHistoryEventType.Declared,
            OccurredAtUtc = DateTime.UtcNow,
            ActorUserId = player.UserId,
            ActorUserName = player.Name,
            ActorCharacterName = warEntry.DeclaredByCharacterName,
            Details = $"Declared war on {warEntry.DeclaredAgainstCharacterName}. Reason: {reason}"
        });

        _activeWars[warKey] = warEntry;

        // Set activation time - war will auto-activate after Pending phase
        _warActivationTimes[warKey] = now + WarPrepDuration;

        AutoEnlistFactionMembers(warEntry, playerEntity, targetEntity);

        // Send acceptance prompt to target (optional - allows them to name their side)
        RaiseNetworkEvent(
            new WarAcceptancePromptEvent
            {
                DeclaredByPlayer = player.UserId,
                DeclaredByCharacterName = warEntry.DeclaredByCharacterName,
                SideName1 = warEntry.SideName1,
                Reason = reason,
            },
            targetSession);

        // Track the prompt for reference (but no timeout - war will activate regardless)
        _pendingAcceptancePrompts[warKey] = new WarAcceptancePrompt
        {
            ExpiresAt = now + WarPrepDuration,
            DeclaredByCharacterName = warEntry.DeclaredByCharacterName,
            DeclaredByPlayer = player.UserId,
        };

        BroadcastWarState();
        SendPanelDataToAll();

        _chat.DispatchServerAnnouncement(
            $"WAR DECLARED\n" +
            $"{warEntry.DeclaredByCharacterName} has declared war on {warEntry.DeclaredAgainstCharacterName}!\n" +
            $"Reason: \"{reason}\"\n\n" +
            $"War begins in 5 minutes. Use /warjoin to pick a side.",
            Color.OrangeRed);

        SendResult(player, true, $"War declared. Begins in 5 minutes.");
    }

    // ── War acceptance/rejection ───────────────────────────────────────────

    private void OnAcceptWar(PlayerWarAcceptEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;

        if (player.Status != SessionStatus.InGame || player.AttachedEntity is not { } acceptEntity)
        {
            SendResult(player, false, "You must be in-game.");
            return;
        }

        // Find war where this character entity is the declared-against target
        var acceptNetEntity = GetNetEntity(acceptEntity);
        PlayerWarEntry? war = null;
        foreach (var w in _activeWars.Values)
        {
            if (w.DeclaredAgainstEntity == acceptNetEntity && w.Phase == WarPhase.Pending)
            {
                war = w;
                break;
            }
        }

        if (war == null)
        {
            SendResult(player, false, "No pending war found.");
            return;
        }

        var now = _gameTiming.CurTime;

        // Accept war
        war.SideName2 = msg.SideName2.Trim();

        // Add history event
        war.History.Add(new WarHistoryEvent
        {
            EventType = WarHistoryEventType.Accepted,
            OccurredAtUtc = DateTime.UtcNow,
            ActorUserId = player.UserId,
            ActorUserName = player.Name,
            ActorCharacterName = player.AttachedEntity is { } acceptedEntity ? Name(acceptedEntity) : string.Empty,
            Details = $"Accepted war and named side: {war.SideName2}"
        });

        _pendingAcceptancePrompts.Remove(war.WarKey);
        BroadcastWarState();
        SendPanelDataToAll();

        _chat.DispatchServerAnnouncement(
            $"WAR ACCEPTED\n" +
            $"{Name(player.AttachedEntity ?? EntityUid.Invalid)} has accepted the war and named their side!\n" +
            $"{war.SideName1} vs {war.SideName2}\n\n" +
            $"War begins in 5 minutes. Use (/warjoin) to choose a side.",
            Color.OrangeRed);

        SendResult(player, true, "Side named. War begins in 5 minutes.");
    }

    private void OnRejectWar(PlayerWarRejectEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;

        if (player.Status != SessionStatus.InGame || player.AttachedEntity is not { } rejectEntity)
        {
            SendResult(player, false, "You must be in-game.");
            return;
        }

        // Find war where this character entity is the declared-against target
        var rejectNetEntity = GetNetEntity(rejectEntity);
        PlayerWarEntry? war = null;
        foreach (var w in _activeWars.Values)
        {
            if (w.DeclaredAgainstEntity == rejectNetEntity && w.Phase == WarPhase.Pending)
            {
                war = w;
                break;
            }
        }

        if (war == null)
        {
            SendResult(player, false, "No pending war found.");
            return;
        }

        _activeWars.Remove(war.WarKey);
        _warActivationTimes.Remove(war.WarKey);
        _pendingAcceptancePrompts.Remove(war.WarKey);

        // Clean up _warParticipants for both originals
        _warParticipants.Remove(war.DeclaredByEntity);
        _warParticipants.Remove(war.DeclaredAgainstEntity);

        BroadcastWarState();
        SendPanelDataToAll();

        _chat.DispatchServerAnnouncement(
            $"WAR REJECTED\n" +
            $"{Name(rejectEntity)} rejected the war declaration.",
            Color.Gray);

        SendResult(player, true, "War declaration rejected.");
    }

    // ── GUI: Ceasefire ────────────────────────────────────────────────────

    private void OnCeasefireRequest(PlayerWarCeasefireRequestEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;

        if (player.Status != SessionStatus.InGame || player.AttachedEntity is not { } ceaseEntity)
        {
            SendResult(player, false, "You must be in-game.");
            return;
        }

        // Only the original character entities can propose ceasefire (character-bound)
        var ceaseNetEntity = GetNetEntity(ceaseEntity);
        PlayerWarEntry? war = null;
        foreach (var w in _activeWars.Values)
        {
            if ((w.DeclaredByEntity == ceaseNetEntity || w.DeclaredAgainstEntity == ceaseNetEntity) &&
                w.Phase == WarPhase.Active)
            {
                war = w;
                break;
            }
        }

        if (war == null)
        {
            SendResult(player, false, "You are not in an active war as an original participant.");
            return;
        }

        var now = _gameTiming.CurTime;
        if (_ceasefireCooldowns.TryGetValue(player.UserId.ToString(), out var ceaseCooldown) && now < ceaseCooldown)
        {
            var remaining = ceaseCooldown - now;
            SendResult(player, false, $"Ceasefire cooldown: {remaining.Minutes}m {remaining.Seconds}s remaining.");
            return;
        }

        var warKey = war.WarKey;

        if (_pendingCeasefireProposals.ContainsKey(warKey))
        {
            SendResult(player, false, "A ceasefire is already being negotiated for this war.");
            return;
        }

        // Notify the other original player's account (they may have respawned, but still receive the prompt)
        var otherPlayer = war.DeclaredByEntity == ceaseNetEntity ? war.DeclaredAgainstPlayer : war.DeclaredByPlayer;

        var proposal = new CeasefireProposal
        {
            War = war,
            ProposingPlayer = player.UserId,
            ProposingPlayerName = Name(ceaseEntity),
            ExpiresAt = now + TimeSpan.FromMinutes(5),
        };

        _pendingCeasefireProposals[warKey] = proposal;
        SendPanelDataToAll();

        if (TryGetSessionForPlayer(otherPlayer, out var otherSession))
        {
            RaiseNetworkEvent(
                new CeasefireProposalEvent
                {
                    ProposingPlayer = player.UserId,
                    ProposingPlayerName = proposal.ProposingPlayerName,
                },
                otherSession);
        }

        _chat.DispatchServerAnnouncement(
            $"CEASEFIRE PROPOSED\n" +
            $"{proposal.ProposingPlayerName} proposes a ceasefire.\n" +
            $"The other party has 5 minutes to respond.",
            Color.SkyBlue);

        SendResult(player, true, "Ceasefire proposed. Awaiting response.");
    }

    private void OnAcceptCeasefireProposal(CeasefireAcceptedEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;

        if (player.Status != SessionStatus.InGame || player.AttachedEntity is not { } acceptCeaseEntity)
        {
            SendResult(player, false, "You must be in-game.");
            return;
        }

        // Only the original character entity can accept ceasefire (character-bound)
        var acceptCeaseNetEntity = GetNetEntity(acceptCeaseEntity);
        PlayerWarEntry? war = null;
        foreach (var w in _activeWars.Values)
        {
            if ((w.DeclaredByEntity == acceptCeaseNetEntity || w.DeclaredAgainstEntity == acceptCeaseNetEntity) &&
                w.Phase == WarPhase.Active)
            {
                war = w;
                break;
            }
        }

        if (war == null)
        {
            SendResult(player, false, "You are not in an active war as an original participant.");
            return;
        }

        var warKey = war.WarKey;

        if (!_pendingCeasefireProposals.TryGetValue(warKey, out var proposal))
        {
            SendResult(player, false, "No ceasefire proposal for this war.");
            return;
        }

        if (proposal.ProposingPlayer == player.UserId)
        {
            SendResult(player, false, "You proposed this ceasefire. Wait for the other party.");
            return;
        }

        _pendingCeasefireProposals.Remove(warKey);
        RemoveWar(war);

        _chat.DispatchServerAnnouncement(
            $"CEASEFIRE\n" +
            $"{war.SideName1} and {war.SideName2} have agreed to cease hostilities.",
            Color.SkyBlue);

        SendResult(player, true, "Ceasefire accepted. The conflict has ended.");
    }

    private void OnRejectCeasefireProposal(CeasefireRejectedEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;

        if (player.Status != SessionStatus.InGame || player.AttachedEntity is not { } rejectCeaseEntity)
        {
            SendResult(player, false, "You must be in-game.");
            return;
        }

        // Only the original character entity can reject ceasefire (character-bound)
        var rejectCeaseNetEntity = GetNetEntity(rejectCeaseEntity);
        PlayerWarEntry? war = null;
        foreach (var w in _activeWars.Values)
        {
            if ((w.DeclaredByEntity == rejectCeaseNetEntity || w.DeclaredAgainstEntity == rejectCeaseNetEntity) &&
                w.Phase == WarPhase.Active)
            {
                war = w;
                break;
            }
        }

        if (war == null)
        {
            SendResult(player, false, "You are not in an active war as an original participant.");
            return;
        }

        var warKey = war.WarKey;

        if (!_pendingCeasefireProposals.TryGetValue(warKey, out var proposal))
        {
            SendResult(player, false, "No ceasefire proposal for this war.");
            return;
        }

        if (proposal.ProposingPlayer == player.UserId)
        {
            SendResult(player, false, "You proposed this ceasefire. Wait for the other party.");
            return;
        }

        _ceasefireCooldowns[proposal.ProposingPlayer.ToString()] = _gameTiming.CurTime + CeasefireCooldownAfterRejection;
        _pendingCeasefireProposals.Remove(warKey);
        SendPanelDataToAll();

        _chat.DispatchServerAnnouncement(
            $"CEASEFIRE REJECTED\n" +
            $"The ceasefire proposal was rejected. The war continues.",
            Color.OrangeRed);

        SendResult(player, true, "Ceasefire rejected. The war continues.");
    }

    // ── GUI: Warjoin panel data ────────────────────────────────────────────

    private void OnWarJoinPanelRequest(FactionWarJoinPanelRequestEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        var data = new PlayerWarJoinPanelDataEvent();

        var panelEntity = player.AttachedEntity is { } ppe ? GetNetEntity(ppe) : default;

        // List pending wars this character can join (character-bound check via entity)
        data.AvailableWars = _activeWars.Values
            .Where(w => w.Phase == WarPhase.Pending && !w.Participants.ContainsKey(panelEntity))
            .ToList();

        // List active wars for reference
        data.ActiveWars = _activeWars.Values
            .Where(w => w.Phase == WarPhase.Active)
            .ToList();

        // Check if this character is already in a war
        data.AlreadyInWar = panelEntity != default && _warParticipants.ContainsKey(panelEntity);

        if (data.AvailableWars.Count == 0 && !data.AlreadyInWar)
            data.StatusMessage = "No pending wars to join.";

        RaiseNetworkEvent(data, player);
    }

    // ── GUI: Warjoin enlistment ────────────────────────────────────────────

    private void OnWarJoinRequest(PlayerWarJoinRequestEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;

        if (player.Status != SessionStatus.InGame || player.AttachedEntity is not { } joinerEntity)
        {
            SendJoinResult(player, false, "You must be in-game.");
            return;
        }

        var joinerNetEntity = GetNetEntity(joinerEntity);

        if (_warParticipants.ContainsKey(joinerNetEntity))
        {
            SendJoinResult(player, false, "You have already joined a war.");
            return;
        }

        // Find the war by its key (entity-based key from the client)
        if (!_activeWars.TryGetValue(msg.WarKey, out var war))
        {
            SendJoinResult(player, false, "That war is not in a joinable state.");
            return;
        }

        if (war.Phase != WarPhase.Pending)
        {
            SendJoinResult(player, false, "That war is not pending.");
            return;
        }

        if (msg.ChosenSide != 1 && msg.ChosenSide != 2)
        {
            SendJoinResult(player, false, "Invalid side selection.");
            return;
        }

        // #Misfits Add - Major faction join restriction: no 3rd major stacking
        if (TryGetAutoEnlistFaction(joinerEntity, out var joinerFaction)
            && MajorFactions.Contains(joinerFaction))
        {
            var majorsOnSide1 = 0;
            var majorsOnSide2 = 0;
            foreach (var (pe, _) in war.Participants)
            {
                var puid = GetEntity(pe);
                if (!Exists(puid))
                    continue;
                if (!TryGetAutoEnlistFaction(puid, out var pf))
                    continue;
                if (!MajorFactions.Contains(pf))
                    continue;

                if (war.Participants[pe] == 1)
                    majorsOnSide1++;
                else
                    majorsOnSide2++;
            }

            if (majorsOnSide1 > 0 && majorsOnSide2 > 0)
            {
                _chat.DispatchServerMessage(player, "This war already involves two major factions. You cannot join as a third.");
                SendJoinResult(player, false, "This war already involves two major factions.");
                return;
            }

            var joiningSide = msg.ChosenSide;
            if ((joiningSide == 1 && majorsOnSide1 > 0) || (joiningSide == 2 && majorsOnSide2 > 0))
            {
                _chat.DispatchServerMessage(player, "Another major faction is already on that side of the war.");
                SendJoinResult(player, false, "Another major faction is already on that side.");
                return;
            }
        }

        // Add character entity to participants (character-bound)
        war.Participants[joinerNetEntity] = msg.ChosenSide;
        _warParticipants[joinerNetEntity] = (msg.WarKey, msg.ChosenSide);

        war.History.Add(new WarHistoryEvent
        {
            EventType = WarHistoryEventType.PlayerJoined,
            OccurredAtUtc = DateTime.UtcNow,
            ActorUserId = player.UserId,
            ActorUserName = player.Name,
            ActorCharacterName = Name(joinerEntity),
            Details = $"Joined on side {msg.ChosenSide}"
        });

        BroadcastParticipants();

        var sideName = msg.ChosenSide == 1 ? war.SideName1 : war.SideName2;
        SendJoinResult(player, true, $"You have joined the war on the side of {sideName}.");
    }

    // ── Surrender ───────────────────────────────────────────────────────────

    private void OnSurrenderRequest(PlayerWarSurrenderRequestEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;

        if (player.Status != SessionStatus.InGame || player.AttachedEntity is not { } surrEntity)
        {
            _chat.DispatchServerMessage(player, "You must be in-game to surrender.");
            return;
        }

        var surrNetEntity = GetNetEntity(surrEntity);

        // Check if this entity is a war participant in an active war
        if (!_warParticipants.TryGetValue(surrNetEntity, out var participantEntry))
        {
            _chat.DispatchServerMessage(player, "You are not in a war.");
            return;
        }

        if (!_activeWars.TryGetValue(participantEntry.WarKey, out var war) || war.Phase != WarPhase.Active)
        {
            _chat.DispatchServerMessage(player, "The war is not active.");
            return;
        }

        // Already surrendered
        if (_surrenderedParticipants.Contains(surrNetEntity))
        {
            _chat.DispatchServerMessage(player, "You have already surrendered.");
            return;
        }

        // Mark as surrendered
        _surrenderedParticipants.Add(surrNetEntity);

        // Drop all held weapons
        RaiseLocalEvent(surrEntity, new DropHandItemsEvent());

        // Paralyze them (stun only) - they can still see, hear, and type in chat
        _stun.TryStun(surrEntity, TimeSpan.FromMinutes(15), true);

        // Broadcast updated participant info so overlays update
        BroadcastParticipants();

        // Local flavortext popup only — no server-wide announcement
        _popup.PopupEntity("You have surrendered. You are now at your enemy's mercy.", surrEntity, surrEntity);

        // Send a direct server message to the surrendering player's chatbox
        _chat.DispatchServerMessage(player, "You have surrendered. You are now at your enemy's mercy.");

        // Notify all other participants in the same war that this player surrendered
        var surrName = Name(surrEntity);
        foreach (var (otherNetEntity, _) in war.Participants)
        {
            if (otherNetEntity == surrNetEntity)
                continue;

            var otherUid = GetEntity(otherNetEntity);
            if (!TryComp<ActorComponent>(otherUid, out var actor))
                continue;

            _chat.DispatchServerMessage(actor.PlayerSession, $"{surrName} has surrendered. They are now at your mercy.");
        }
    }

    // ── Admin commands ─────────────────────────────────────────────────────

    private void WarEndCommand(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is { } player && !_adminManager.IsAdmin(player))
        {
            shell.WriteError("You must be an admin.");
            return;
        }

        if (args.Length < 2)
        {
            shell.WriteError("Usage: warend <player1_uid_or_name> <player2_uid_or_name>");
            return;
        }

        var player1Id = ResolvePlayerIdentifier(args[0]);
        var player2Id = ResolvePlayerIdentifier(args[1]);

        if (player1Id == null || player2Id == null)
        {
            shell.WriteError("One or both players not found.");
            return;
        }

        // Find war involving both players (iterate since key is entity-based)
        PlayerWarEntry? war = null;
        foreach (var w in _activeWars.Values)
        {
            if ((w.DeclaredByPlayer == player1Id || w.DeclaredAgainstPlayer == player1Id) &&
                (w.DeclaredByPlayer == player2Id || w.DeclaredAgainstPlayer == player2Id))
            {
                war = w;
                break;
            }
        }

        if (war == null)
        {
            shell.WriteError("War not found.");
            return;
        }

        RemoveWar(war);
        shell.WriteLine("War ended.");
    }

    private void ForceWarCommand(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is { } player && !_adminManager.IsAdmin(player))
        {
            shell.WriteError("You must be an admin.");
            return;
        }

        if (args.Length < 2)
        {
            shell.WriteError("Usage: forcewar <player1_uid_or_name> <player2_uid_or_name> [reason...]");
            return;
        }

        var player1Id = ResolvePlayerIdentifier(args[0]);
        var player2Id = ResolvePlayerIdentifier(args[1]);

        if (player1Id == null || player2Id == null || player1Id == player2Id)
        {
            shell.WriteError("Invalid player combination.");
            return;
        }

        var reason = args.Length > 2 ? string.Join(" ", args.Skip(2)) : "Admin-forced war";

        if (!TryGetSessionForPlayer(player1Id.Value, out var p1Sess) || p1Sess.AttachedEntity is not { } p1Entity ||
            !TryGetSessionForPlayer(player2Id.Value, out var p2Sess) || p2Sess.AttachedEntity is not { } p2Entity)
        {
            shell.WriteError("One or both players are not currently in-game with a character.");
            return;
        }

        var p1NetEntity = GetNetEntity(p1Entity);
        var p2NetEntity = GetNetEntity(p2Entity);

        var warEntry = new PlayerWarEntry
        {
            DeclaredByPlayer = player1Id.Value,
            DeclaredByEntity = p1NetEntity,
            DeclaredByCharacterName = Name(p1Entity),
            DeclaredAgainstPlayer = player2Id.Value,
            DeclaredAgainstEntity = p2NetEntity,
            DeclaredAgainstCharacterName = Name(p2Entity),
            SideName1 = "Side 1",
            SideName2 = "Side 2",
            Reason = reason,
            Phase = WarPhase.Pending,
        };

        warEntry.Participants[p1NetEntity] = 1;
        warEntry.Participants[p2NetEntity] = 2;

        var warKey = warEntry.WarKey;
        _warParticipants[p1NetEntity] = (warKey, 1);
        _warParticipants[p2NetEntity] = (warKey, 2);
        _activeWars[warKey] = warEntry;
        _warActivationTimes[warKey] = _gameTiming.CurTime + WarPrepDuration;

        AutoEnlistFactionMembers(warEntry, p1Entity, p2Entity);

        BroadcastWarState();
        SendPanelDataToAll();

        shell.WriteLine($"War forced between {Name(p1Entity)} and {Name(p2Entity)}.");
    }

    private void ForceWarJoinCommand(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is { } player && !_adminManager.IsAdmin(player))
        {
            shell.WriteError("You must be an admin.");
            return;
        }

        if (args.Length < 2)
        {
            shell.WriteError("Usage: forcewarjoin <target_username> <ally_username>");
            return;
        }

        var targetId = ResolvePlayerIdentifier(args[0]);
        var allyId = ResolvePlayerIdentifier(args[1]);

        if (targetId == null || allyId == null)
        {
            shell.WriteError("Target or ally player not found.");
            return;
        }

        if (!TryGetSessionForPlayer(targetId.Value, out var targetSession) ||
            targetSession.AttachedEntity is not { } targetEntity)
        {
            shell.WriteError("Target player is not currently in-game with a character.");
            return;
        }

        if (!TryGetSessionForPlayer(allyId.Value, out var allySession) ||
            allySession.AttachedEntity is not { } allyEntity)
        {
            shell.WriteError("Ally player is not currently in-game with a character.");
            return;
        }

        var allyNetEntity = GetNetEntity(allyEntity);
        if (!_warParticipants.TryGetValue(allyNetEntity, out var allyEntry) ||
            !_activeWars.TryGetValue(allyEntry.WarKey, out var war))
        {
            shell.WriteError("Ally player is not in a war.");
            return;
        }

        var side = allyEntry.Side;
        var targetNetEntity = GetNetEntity(targetEntity);
        if (_warParticipants.TryGetValue(targetNetEntity, out var oldEntry) &&
            _activeWars.TryGetValue(oldEntry.WarKey, out var oldWar))
        {
            oldWar.Participants.Remove(targetNetEntity);
        }

        war.Participants[targetNetEntity] = side;
        _warParticipants[targetNetEntity] = (war.WarKey, side);
        _surrenderedParticipants.Remove(targetNetEntity);

        war.History.Add(new WarHistoryEvent
        {
            EventType = WarHistoryEventType.PlayerJoined,
            OccurredAtUtc = DateTime.UtcNow,
            ActorUserId = targetSession.UserId,
            ActorUserName = targetSession.Name,
            ActorCharacterName = Name(targetEntity),
            Details = $"Admin-forced onto side {side}"
        });

        BroadcastParticipants();
        BroadcastWarState();
        SendPanelDataToAll();

        var sideName = side == 1 ? war.SideName1 : war.SideName2;
        _chat.DispatchServerMessage(targetSession, $"An admin has placed you in the war on the side of {sideName}.");
        shell.WriteLine($"Forced {Name(targetEntity)} into the war on the side of {sideName}.");
    }

    private CompletionResult ForceWarJoinCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(CompletionHelper.SessionNames(players: _playerManager), "<target username>"),
            2 => CompletionResult.FromHintOptions(CompletionHelper.SessionNames(players: _playerManager), "<ally username>"),
            _ => CompletionResult.Empty
        };
    }

    // #Misfits Add - Admin command to observe a war without appearing as [ALLY]/[ENEMY].
    private void ForceObserveWarCommand(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is { } player && !_adminManager.IsAdmin(player))
        {
            shell.WriteError("You must be an admin.");
            return;
        }

        if (args.Length < 2)
        {
            shell.WriteError("Usage: forceobservewar <observer_username> <war_participant_username>");
            return;
        }

        var observerId = ResolvePlayerIdentifier(args[0]);
        var participantId = ResolvePlayerIdentifier(args[1]);

        if (observerId == null || participantId == null)
        {
            shell.WriteError("Observer or participant player not found.");
            return;
        }

        if (!TryGetSessionForPlayer(observerId.Value, out var observerSession) ||
            observerSession.AttachedEntity is not { } observerEntity)
        {
            shell.WriteError("Observer player is not currently in-game with a character.");
            return;
        }

        if (!TryGetSessionForPlayer(participantId.Value, out var participantSession) ||
            participantSession.AttachedEntity is not { } participantEntity)
        {
            shell.WriteError("Participant player is not currently in-game with a character.");
            return;
        }

        var participantNetEntity = GetNetEntity(participantEntity);
        if (!_warParticipants.TryGetValue(participantNetEntity, out var participantEntry) ||
            !_activeWars.TryGetValue(participantEntry.WarKey, out var war))
        {
            shell.WriteError("Participant player is not in a war.");
            return;
        }

        var observerNetEntity = GetNetEntity(observerEntity);

        // Remove from any existing war first.
        if (_warParticipants.TryGetValue(observerNetEntity, out var oldEntry) &&
            _activeWars.TryGetValue(oldEntry.WarKey, out var oldWar))
        {
            oldWar.Participants.Remove(observerNetEntity);
        }

        var side = participantEntry.Side;

        // Track as observer - can see overlay tags but won't get one rendered.
        war.Participants[observerNetEntity] = side;
        _warParticipants[observerNetEntity] = (war.WarKey, side);
        _observerParticipants.Add(observerNetEntity);
        _surrenderedParticipants.Remove(observerNetEntity);

        war.History.Add(new WarHistoryEvent
        {
            EventType = WarHistoryEventType.PlayerJoined,
            OccurredAtUtc = DateTime.UtcNow,
            ActorUserId = observerSession.UserId,
            ActorUserName = observerSession.Name,
            ActorCharacterName = Name(observerEntity),
            Details = $"Admin-forced observer on side {side}"
        });

        BroadcastParticipants();
        BroadcastWarState();
        SendPanelDataToAll();

        var sideName = side == 1 ? war.SideName1 : war.SideName2;
        _chat.DispatchServerMessage(observerSession,
            $"You are now observing the war from {sideName}'s perspective. You can see overlay tags but others cannot see yours.");
        shell.WriteLine($"Forced {Name(observerEntity)} to observe the war from {sideName}'s side.");
    }

    private CompletionResult ForceObserveWarCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(CompletionHelper.SessionNames(players: _playerManager), "<observer username>"),
            2 => CompletionResult.FromHintOptions(CompletionHelper.SessionNames(players: _playerManager), "<war participant username>"),
            _ => CompletionResult.Empty
        };
    }

    private void OnForceWarRequest(PlayerWarForceRequestEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;

        if (!_adminManager.IsAdmin(player))
        {
            RaiseNetworkEvent(new FactionWarForceResultEvent { Success = false, Message = "Admin only." }, player);
            return;
        }

        if (!TryGetSessionForPlayer(msg.Player1, out var fw1Sess) || fw1Sess.AttachedEntity is not { } fw1Entity ||
            !TryGetSessionForPlayer(msg.Player2, out var fw2Sess) || fw2Sess.AttachedEntity is not { } fw2Entity)
        {
            RaiseNetworkEvent(new FactionWarForceResultEvent { Success = false, Message = "One or both players are not in-game." }, player);
            return;
        }

        var fw1NetEntity = GetNetEntity(fw1Entity);
        var fw2NetEntity = GetNetEntity(fw2Entity);

        var warEntry = new PlayerWarEntry
        {
            DeclaredByPlayer = msg.Player1,
            DeclaredByEntity = fw1NetEntity,
            DeclaredByCharacterName = Name(fw1Entity),
            DeclaredAgainstPlayer = msg.Player2,
            DeclaredAgainstEntity = fw2NetEntity,
            DeclaredAgainstCharacterName = Name(fw2Entity),
            SideName1 = msg.SideName1,
            SideName2 = msg.SideName2,
            Reason = msg.Reason,
            Phase = WarPhase.Pending,
        };

        warEntry.Participants[fw1NetEntity] = 1;
        warEntry.Participants[fw2NetEntity] = 2;

        var warKey = warEntry.WarKey;
        _warParticipants[fw1NetEntity] = (warKey, 1);
        _warParticipants[fw2NetEntity] = (warKey, 2);
        _activeWars[warKey] = warEntry;
        _warActivationTimes[warKey] = _gameTiming.CurTime + WarPrepDuration;

        AutoEnlistFactionMembers(warEntry, fw1Entity, fw2Entity);

        BroadcastWarState();
        SendPanelDataToAll();

        RaiseNetworkEvent(new FactionWarForceResultEvent { Success = true, Message = "War forced." }, player);
    }

    private void OnForceCeasefireRequest(PlayerWarForceCeasefireRequestEvent msg, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;

        if (!_adminManager.IsAdmin(player))
        {
            RaiseNetworkEvent(new FactionWarForceResultEvent { Success = false, Message = "Admin only.", IsCeasefire = true }, player);
            return;
        }

        // Find war involving both accounts (iterate since key is entity-based)
        PlayerWarEntry? fcWar = null;
        foreach (var w in _activeWars.Values)
        {
            if ((w.DeclaredByPlayer == msg.Player1 || w.DeclaredAgainstPlayer == msg.Player1) &&
                (w.DeclaredByPlayer == msg.Player2 || w.DeclaredAgainstPlayer == msg.Player2))
            {
                fcWar = w;
                break;
            }
        }

        if (fcWar == null)
        {
            RaiseNetworkEvent(new FactionWarForceResultEvent { Success = false, Message = "War not found.", IsCeasefire = true }, player);
            return;
        }

        RemoveWar(fcWar);
        RaiseNetworkEvent(new FactionWarForceResultEvent { Success = true, Message = "War ended.", IsCeasefire = true }, player);
    }

    // #Misfits Add - Admin GUI observe handler. The admin clicking the button IS the observer.
    private void OnForceObserveRequest(PlayerWarForceObserveRequestEvent msg, EntitySessionEventArgs args)
    {
        var obsSess = args.SenderSession;

        if (!_adminManager.IsAdmin(obsSess))
        {
            RaiseNetworkEvent(new FactionWarForceObserveResultEvent { Success = false, Message = "Admin only." }, obsSess);
            return;
        }

        if (obsSess.AttachedEntity is not { } obsEntity)
        {
            RaiseNetworkEvent(new FactionWarForceObserveResultEvent { Success = false, Message = "You must be in-game with a character." }, obsSess);
            return;
        }

        if (!TryGetSessionForPlayer(msg.Participant, out var partSess) || partSess.AttachedEntity is not { } partEntity)
        {
            RaiseNetworkEvent(new FactionWarForceObserveResultEvent { Success = false, Message = "Participant player is not in-game." }, obsSess);
            return;
        }

        var partNetEntity = GetNetEntity(partEntity);
        if (!_warParticipants.TryGetValue(partNetEntity, out var partEntry) ||
            !_activeWars.TryGetValue(partEntry.WarKey, out var war))
        {
            RaiseNetworkEvent(new FactionWarForceObserveResultEvent { Success = false, Message = "Participant is not in an active war." }, obsSess);
            return;
        }

        var obsNetEntity = GetNetEntity(obsEntity);

        // Remove observer from any existing war first.
        if (_warParticipants.TryGetValue(obsNetEntity, out var oldEntry) &&
            _activeWars.TryGetValue(oldEntry.WarKey, out var oldWar))
        {
            oldWar.Participants.Remove(obsNetEntity);
        }

        var side = partEntry.Side;
        war.Participants[obsNetEntity] = side;
        _warParticipants[obsNetEntity] = (war.WarKey, side);
        _observerParticipants.Add(obsNetEntity);
        _surrenderedParticipants.Remove(obsNetEntity);

        war.History.Add(new WarHistoryEvent
        {
            EventType = WarHistoryEventType.PlayerJoined,
            OccurredAtUtc = DateTime.UtcNow,
            ActorUserId = obsSess.UserId,
            ActorUserName = obsSess.Name,
            ActorCharacterName = Name(obsEntity),
            Details = $"Admin-forced observer on side {side}"
        });

        BroadcastParticipants();
        BroadcastWarState();
        SendPanelDataToAll();

        var sideName = side == 1 ? war.SideName1 : war.SideName2;
        _chat.DispatchServerMessage(obsSess,
            $"You are now observing the war from {sideName}'s perspective. You can see overlay tags but others cannot see yours.");
        RaiseNetworkEvent(new FactionWarForceObserveResultEvent
        {
            Success = true,
            Message = $"Now observing the war from {sideName}'s side.",
        }, obsSess);
    }

    // ── Round lifecycle ────────────────────────────────────────────────────

    private void OnRoundRestart(RoundRestartCleanupEvent _)
    {
        _activeWars.Clear();
        _warActivationTimes.Clear();
        _warParticipants.Clear();
        _surrenderedParticipants.Clear();
        _playerWarCooldowns.Clear();
        _ceasefireCooldowns.Clear();
        _pendingCeasefireProposals.Clear();
        _pendingAcceptancePrompts.Clear();
        _panelOpenSessions.Clear();
        _participantResyncAccumulator = 0f;
        _roundStartTime = _gameTiming.CurTime;
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
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
                new FactionWarStateUpdatedEvent { ActiveWars = _activeWars.Values.ToList() },
                e.Session);
        }

        if (_warParticipants.Count > 0)
            SendParticipantsTo(e.Session);

        // Late-join auto-enlist: if this character belongs to a war faction, join the ongoing war
        if (e.Session.AttachedEntity is { } spawnedEntity)
            AutoLateJoinWarMember(spawnedEntity);
    }

    // Fires at the moment a player mind is attached to a body — entity is valid here, unlike PlayerStatusChanged.
    private void OnMindAdded(EntityUid uid, MindContainerComponent component, MindAddedMessage args) =>
        AutoLateJoinWarMember(uid);

    private void AutoLateJoinWarMember(EntityUid entity)
    {
        if (!TryGetAutoEnlistFaction(entity, out var faction) || IsAutoEnlistJobExempt(entity))
            return;

        var netEntity = GetNetEntity(entity);
        if (_warParticipants.ContainsKey(netEntity))
            return;

        var anyJoined = false;

        foreach (var war in _activeWars.Values)
        {
            // Skip same-faction wars — no auto-enlist for those
            if (war.Side1FactionId != null && war.Side2FactionId != null &&
                string.Equals(war.Side1FactionId, war.Side2FactionId, StringComparison.OrdinalIgnoreCase))
                continue;

            byte side;
            if (string.Equals(faction, war.Side1FactionId, StringComparison.OrdinalIgnoreCase))
                side = 1;
            else if (string.Equals(faction, war.Side2FactionId, StringComparison.OrdinalIgnoreCase))
                side = 2;
            else
                continue;

            if (war.Participants.ContainsKey(netEntity))
                continue;

            if (IsEntityInOtherWar(netEntity, war.WarKey))
                continue;

            war.Participants[netEntity] = side;
            _warParticipants[netEntity] = (war.WarKey, side);
            anyJoined = true;
            break; // A character can only be in one war at a time
        }

        if (anyJoined)
        {
            BroadcastParticipants();
            BroadcastWarState();
            SendPanelDataToAll();
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void RemoveWar(PlayerWarEntry war)
    {
        _activeWars.Remove(war.WarKey);
        _warActivationTimes.Remove(war.WarKey);
        _pendingCeasefireProposals.Remove(war.WarKey);
        _pendingAcceptancePrompts.Remove(war.WarKey);

        // Set per-player cooldown for original 2 players
        var cooldownEnd = _gameTiming.CurTime + WarCooldownAfterEnd;
        _playerWarCooldowns[war.DeclaredByPlayer.ToString()] = cooldownEnd;
        _playerWarCooldowns[war.DeclaredAgainstPlayer.ToString()] = cooldownEnd;

        // Remove all participants (entity-keyed)
        var toRemove = _warParticipants
            .Where(kvp => kvp.Value.WarKey == war.WarKey)
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var entity in toRemove)
        {
            _warParticipants.Remove(entity);
            _surrenderedParticipants.Remove(entity);
            _observerParticipants.Remove(entity); // #Misfits Add - clean up observer tracking
        }

        war.History.Add(new WarHistoryEvent
        {
            EventType = WarHistoryEventType.Concluded,
            OccurredAtUtc = DateTime.UtcNow,
            ActorUserId = null,
            ActorUserName = "System",
            ActorCharacterName = "System",
            Details = "War concluded"
        });

        BroadcastWarState();
        BroadcastParticipants();
        SendPanelDataToAll();
    }

    private void BroadcastWarState()
    {
        RaiseNetworkEvent(
            new FactionWarStateUpdatedEvent { ActiveWars = _activeWars.Values.ToList() },
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

    private void SendPanelDataToAll()
    {
        _panelOpenSessions.RemoveWhere(s => s.Status != SessionStatus.InGame);
        foreach (var session in _panelOpenSessions)
            SendPanelData(session);
    }

    private Dictionary<NetEntity, FactionWarParticipantInfo> BuildParticipantDict()
    {
        var dict = new Dictionary<NetEntity, FactionWarParticipantInfo>();

        // All participants (originals + joiners + auto-enlisted) are stored in war.Participants
        // with their character entity as the key — no session lookup required.
        foreach (var war in _activeWars.Values)
        {
            if (war.Phase != WarPhase.Active)
                continue;

            foreach (var (netEntity, side) in war.Participants)
            {
                if (dict.ContainsKey(netEntity))
                    continue;

                if (!Exists(GetEntity(netEntity)))
                    continue;

                dict[netEntity] = new FactionWarParticipantInfo
                {
                    Side = side,
                    WarKey = war.WarKey,
                    Surrendered = _surrenderedParticipants.Contains(netEntity),
                    IsObserver = _observerParticipants.Contains(netEntity), // #Misfits Add
                };
            }
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

    private void AutoEnlistFactionMembers(PlayerWarEntry war, EntityUid? side1Entity, EntityUid? side2Entity)
    {
        string? side1Faction = null, side2Faction = null;

        if (side1Entity != null && TryGetAutoEnlistFaction(side1Entity.Value, out var f1))
            side1Faction = f1;

        if (side2Entity != null && TryGetAutoEnlistFaction(side2Entity.Value, out var f2))
            side2Faction = f2;

        // Store faction IDs on the war for late-join detection
        war.Side1FactionId = side1Faction;
        war.Side2FactionId = side2Faction;

        // Same-faction war: no auto-enlist (e.g. NCR vs NCR — let them settle it themselves)
        if (side1Faction != null && side2Faction != null &&
            string.Equals(side1Faction, side2Faction, StringComparison.OrdinalIgnoreCase))
            return;

        if (side1Faction != null)
            AutoEnlistFactionMembers(war, side1Faction, 1);

        if (side2Faction != null)
            AutoEnlistFactionMembers(war, side2Faction, 2);
    }

    private void AutoEnlistFactionMembers(PlayerWarEntry war, string factionId, byte side)
    {
        foreach (var session in _playerManager.Sessions)
        {
            if (session.Status != SessionStatus.InGame || session.AttachedEntity is not { } entity)
                continue;

            if (!_npcFaction.IsMember(entity, factionId))
                continue;

            if (IsAutoEnlistJobExempt(entity))
                continue;

            var netEntity = GetNetEntity(entity);

            if (war.Participants.ContainsKey(netEntity))
                continue;

            if (IsEntityInOtherWar(netEntity, war.WarKey))
                continue;

            war.Participants[netEntity] = side;
            _warParticipants[netEntity] = (war.WarKey, side);
        }
    }

    private bool TryGetAutoEnlistFaction(EntityUid entity, out string factionId)
    {
        foreach (var faction in AutoEnlistFactions)
        {
            if (_npcFaction.IsMember(entity, faction))
            {
                factionId = faction;
                return true;
            }
        }

        factionId = string.Empty;
        return false;
    }

    private bool IsAutoEnlistJobExempt(EntityUid entity)
    {
        if (!_minds.TryGetMind(entity, out var mindId, out _))
            return false;

        if (!_jobs.MindTryGetJob(mindId, out _, out var proto))
            return false;

        return AutoEnlistJobExemptions.Contains(proto.ID);
    }

    private bool IsEntityInOtherWar(NetEntity entity, string currentWarKey)
    {
        return _warParticipants.TryGetValue(entity, out var entry) && entry.WarKey != currentWarKey;
    }

    /// <summary>
    /// Checks if any participant in any active or pending war belongs to the given faction.
    /// </summary>
    private bool IsFactionInAnyWar(string factionId)
    {
        foreach (var war in _activeWars.Values)
        {
            foreach (var (entity, _) in war.Participants)
            {
                var uid = GetEntity(entity);
                if (Exists(uid) && _npcFaction.IsMember(uid, factionId))
                    return true;
            }
        }
        return false;
    }

    public bool HasWar(string warKey)
    {
        return _activeWars.ContainsKey(warKey);
    }

    public bool TryGetActiveWarForOriginalParticipant(NetEntity entity, out PlayerWarEntry war)
    {
        foreach (var entry in _activeWars.Values)
        {
            if (entry.Phase != WarPhase.Active)
                continue;

            if (entry.DeclaredByEntity != entity && entry.DeclaredAgainstEntity != entity)
                continue;

            war = entry;
            return true;
        }

        war = null!;
        return false;
    }

    private bool TryGetSessionForPlayer(NetUserId userId, out ICommonSession session)
    {
        session = null!;
        foreach (var s in _playerManager.Sessions)
        {
            if (s.UserId == userId)
            {
                session = s;
                return true;
            }
        }
        return false;
    }

    private NetUserId? ResolvePlayerIdentifier(string input)
    {
        foreach (var session in _playerManager.Sessions)
        {
            if (session.UserId.ToString().Equals(input, StringComparison.OrdinalIgnoreCase))
                return session.UserId;

            if (session.Name.Equals(input, StringComparison.OrdinalIgnoreCase))
                return session.UserId;
        }

        return null;
    }

    // ── Inner types ────────────────────────────────────────────────────────

    private sealed class WarAcceptancePrompt
    {
        public TimeSpan ExpiresAt;
        public string DeclaredByCharacterName = string.Empty;
        public NetUserId DeclaredByPlayer;
    }

    private sealed class CeasefireProposal
    {
        public PlayerWarEntry War = null!;
        public NetUserId ProposingPlayer;
        public string ProposingPlayerName = string.Empty;
        public TimeSpan ExpiresAt;
    }
}
