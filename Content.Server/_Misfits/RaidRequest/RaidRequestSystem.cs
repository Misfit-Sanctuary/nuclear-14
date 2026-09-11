// #Misfits Add - Server-side /raid request system. Players submit raid requests via the
// /raid panel; admins approve or deny them with comments through the bwoink panel's new
// Raid Requests tab. Decisions are broadcast to the requesting faction (or just the
// individual requester for Wastelander-tier requests) plus the target faction so all
// affected sides know whether a raid is sanctioned.
//
// Mirrors FactionWarSystem patterns:
//  - Server-side faction resolution because NpcFactionMemberComponent.Factions is not synced.
//  - Top-ranking online member detection via job weight for faction-tier submitters.
//  - In-memory per-round state cleared on RoundRestartCleanupEvent.

using System.Linq;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Server._Misfits.FactionWar;
using Content.Server._Misfits.Group;
using Content.Server.Mind;
using Content.Server.Roles.Jobs;
using Content.Shared.Chat;
using Content.Shared._Misfits.RaidRequest;
using Content.Shared._Misfits.Group;
using Content.Shared.GameTicking;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Misfits.RaidRequest;

public sealed class RaidRequestSystem : EntitySystem
{
    [Dependency] private readonly IAdminManager    _adminManager  = default!;
    [Dependency] private readonly IChatManager     _chat          = default!;
    [Dependency] private readonly JobSystem        _jobs          = default!;
    [Dependency] private readonly MindSystem       _minds         = default!;
    [Dependency] private readonly NpcFactionSystem _npcFaction    = default!;
    [Dependency] private readonly IPlayerManager   _playerManager = default!;
    [Dependency] private readonly IGameTiming      _gameTiming    = default!;
    [Dependency] private readonly FactionWarSystem _factionWar    = default!;
    [Dependency] private readonly GroupSystem      _groupSystem   = default!;

    /// <summary>All requests this round, keyed by sequential id.</summary>
    private readonly Dictionary<int, RaidRequestEntry> _requests = new();
    private int _nextId = 1;

    /// <summary>Admin sessions currently watching the Raid Requests tab. Updates pushed only to these.</summary>
    private readonly HashSet<ICommonSession> _subscribedAdmins = new();

    // #Misfits Add - Per-raid auto-expiry deadline. Approved faction-tier raids end automatically
    // 15 minutes after approval so the [ALLY]/[ENEMY] overlay tags don't linger forever.
    // Mirrors FactionWarSystem._warAutoEndTimes (TimeSpan-based deadline, not a float accumulator).
    private readonly Dictionary<int, TimeSpan> _raidAutoEndTimes = new();

    // #Misfits Add - Activation deadline for Approved (prep-phase) raids. When CurTime >= value,
    // the raid flips Approved → Active, the overlay turns on, and the auto-end timer starts.
    // Mirrors FactionWarSystem._warActivationTimes.
    private readonly Dictionary<int, TimeSpan> _raidActivationTimes = new();

    // #Misfits Add - Pending peer-approval prompts. Maps request id → the target faction
    // leader's UserId so we can reject impersonators when the popup decision returns.
    private readonly Dictionary<int, NetUserId> _pendingPeerPrompts = new();

    // #Misfits Add - Reused scratch buffer for auto-expiry sweep, mirrors FactionWarSystem pattern.
    private readonly List<RaidRequestEntry> _expiredRaidsScratch = new();

    // #Misfits Add - Reused scratch buffer for prep → active transition sweep.
    private readonly List<RaidRequestEntry> _activatedRaidsScratch = new();

    // #Misfits Add - Keep raid overlay exemptions aligned with expected spy behavior.
    private static readonly HashSet<string> OverlayExemptJobs = new()
    {
        "CaesarLegionFrumentarii",
    };

    /// <summary>How long an Approved raid stays active before auto-conclude wipes the overlay.</summary>
    private static readonly TimeSpan RaidAutoEndDuration = TimeSpan.FromMinutes(15);

    /// <summary>5-minute prep period between Approved decision and Active overlay activation.</summary>
    private static readonly TimeSpan RaidPrepDuration = TimeSpan.FromMinutes(5);

    // #Misfits Tweak - Safety resync: re-sends participant dict every 30 s to catch mid-round
    // entity spawns. All real state changes call BroadcastParticipants() directly; the old
    // 2-second timer was generating GC pressure via Filter.Broadcast() serialization to all clients.
    private float _participantResyncAccumulator;
    private const float ParticipantResyncInterval = 30f;

    // #Misfits Add - 1 Hz update gate; nothing here is sub-second sensitive.
    private float _updateAccumulator;
    private const float UpdateInterval = 1.0f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<RaidRequestOpenPanelMsg>(OnOpenPanel);
        SubscribeNetworkEvent<RaidRequestSubmitMsg>(OnSubmit);
        SubscribeNetworkEvent<RaidRequestAdminSubscribeMsg>(OnAdminSubscribe);
        SubscribeNetworkEvent<RaidRequestDecisionMsg>(OnAdminDecision);
        // #Misfits Add - Admin manual end (mirrors decision flow).
        SubscribeNetworkEvent<RaidRequestEndMsg>(OnAdminEndRaid);
        // #Misfits Add - Target faction leader's peer-approval response.
        SubscribeNetworkEvent<RaidRequestPeerDecisionMsg>(OnPeerDecision);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);

        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    // #Misfits Tweak - 30-second safety resync only; real state changes call BroadcastParticipants()
    // directly so the overlay responds immediately without waiting for the tick.
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _updateAccumulator += frameTime;
        if (_updateAccumulator < UpdateInterval)
            return;
        _updateAccumulator -= UpdateInterval;

        // #Misfits Add - Prep → Active sweep: flip approved raids whose 5-min prep ran out.
        // Mirrors FactionWarSystem.Update's pending-war activation sweep.
        if (_raidActivationTimes.Count > 0)
        {
            var now = _gameTiming.CurTime;
            var activated = _activatedRaidsScratch;
            activated.Clear();

            foreach (var (id, activateAt) in _raidActivationTimes)
            {
                if (now < activateAt)
                    continue;
                if (_requests.TryGetValue(id, out var entry) && entry.Status == RaidRequestStatus.Approved)
                    activated.Add(entry);
            }

            foreach (var entry in activated)
                ActivateRaid(entry);
        }

        // #Misfits Add - Auto-expiry sweep: conclude any active raid that has hit its 15-minute deadline.
        // Mirrors FactionWarSystem's _warAutoEndTimes pattern (TimeSpan deadline + reused scratch list).
        if (_raidAutoEndTimes.Count > 0)
        {
            var now = _gameTiming.CurTime;
            var expired = _expiredRaidsScratch;
            expired.Clear();

            foreach (var (id, endTime) in _raidAutoEndTimes)
            {
                if (now < endTime)
                    continue;
                if (_requests.TryGetValue(id, out var entry) && entry.Status == RaidRequestStatus.Active)
                    expired.Add(entry);
            }

            foreach (var entry in expired)
                ConcludeRaid(entry, "Auto-Expiry");
        }

        if (!HasAnyApprovedFactionTierRaid())
        {
            _participantResyncAccumulator = 0f;
            return;
        }

        _participantResyncAccumulator += UpdateInterval;
        if (_participantResyncAccumulator < ParticipantResyncInterval)
            return;

        _participantResyncAccumulator = 0f;
        BroadcastParticipants();
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        if (e.NewStatus == SessionStatus.Disconnected)
        {
            _subscribedAdmins.Remove(e.Session);
            return;
        }

        // Send current raid-participant state to newly-connected players so the overlay is
        // correct immediately rather than waiting up to 30 s for the safety resync.
        if (e.NewStatus == SessionStatus.InGame && HasAnyApprovedFactionTierRaid())
            SendParticipantsTo(e.Session);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        // Mark anything still pending as Unclaimed for the (now-stale) admin view, then drop everything.
        foreach (var entry in _requests.Values)
        {
            if (entry.Status == RaidRequestStatus.Pending)
                entry.Status = RaidRequestStatus.Unclaimed;
        }
        BroadcastListToAdmins();

        _requests.Clear();
        _nextId = 1;
        _subscribedAdmins.Clear();
        // #Misfits Add - Drop auto-expiry deadlines so they don't fire next round.
        _raidAutoEndTimes.Clear();
        // #Misfits Add - Clear prep activation timers and outstanding peer prompts too.
        _raidActivationTimes.Clear();
        _pendingPeerPrompts.Clear();

        // Push an empty participant dict so any client overlay caches from the previous round are wiped.
        BroadcastParticipants();
        _participantResyncAccumulator = 0f;
    }

    // ── Requester: open panel ───────────────────────────────────────────────

    private void OnOpenPanel(RaidRequestOpenPanelMsg _msg, EntitySessionEventArgs args) // #Misfits Fix - renamed _ to _msg to avoid out _ discard ambiguity
    {
        var session = args.SenderSession;
        var data = new RaidRequestPanelDataMsg
        {
            MyFactionDisplay = "(none)",
        };

        string? canonicalFaction = null;
        if (session.AttachedEntity is { } playerEntity
            && TryGetEligibleFaction(playerEntity, out var resolved))
        {
            canonicalFaction = resolved;
            data.MyFactionId               = canonicalFaction;
            data.MyFactionDisplay          = RaidRequestConfig.FactionDisplayName(canonicalFaction);
            data.MyFactionIsIndividualTier = RaidRequestConfig.IsIndividualTier(canonicalFaction);

            if (!_factionWar.IsParticipantInActiveWar(GetNetEntity(playerEntity)))
            {
                data.IneligibleReason = "You must be participating in an active war to submit a raid request.";
            }
            // Eligibility: individual-tier always allowed; faction-tier requires top rank.
            else if (data.MyFactionIsIndividualTier)
            {
                data.CanSubmit = true;
            }
            else if (_minds.TryGetMind(playerEntity, out var mindId, out _))
            {
                var myWeight  = GetJobWeight(mindId);
                var topWeight = GetFactionTopWeight(canonicalFaction);
                if (myWeight > 0 && myWeight >= topWeight)
                {
                    data.CanSubmit = true;
                }
                else
                {
                    var topHolder = GetFactionTopJobHolder(canonicalFaction);
                    data.IneligibleReason =
                        $"Only the highest-ranking {data.MyFactionDisplay} member online may submit raid requests. Outranked by: {topHolder}.";
                }
            }
            else
            {
                data.IneligibleReason = "You have no mind entity.";
            }
        }
        else
        {
            data.IneligibleReason = "You are not a member of any raid-eligible faction.";
        }

        // Target list: faction-tier set, minus self, plus formed player groups.
        foreach (var f in RaidRequestConfig.FactionTierFactions)
        {
            if (f == canonicalFaction)
                continue;
            data.TargetOptions.Add(new RaidRequestTargetInfo
            {
                Kind        = RaidRequestTargetKind.Faction,
                Id          = f,
                DisplayName = RaidRequestConfig.FactionDisplayName(f),
            });
        }
        foreach (var group in _groupSystem.GetRaidTargets())
        {
            data.TargetOptions.Add(new RaidRequestTargetInfo
            {
                Kind        = RaidRequestTargetKind.Group,
                Id          = group.GroupId.ToString(),
                DisplayName = $"{group.GroupName} ({group.Members.Count})",
            });
        }
        data.TargetOptions.Sort((a, b) =>
        {
            var byKind = a.Kind.CompareTo(b.Kind);
            return byKind != 0
                ? byKind
                : string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal);
        });

        // Player's own requests this round (so they can see their pending submissions).
        foreach (var entry in _requests.Values)
        {
            if (entry.RequesterUserId == session.UserId)
                data.MyRequests.Add(entry);
        }

        RaiseNetworkEvent(data, session);
    }

    // ── Requester: submit ──────────────────────────────────────────────────

    private void OnSubmit(RaidRequestSubmitMsg msg, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;

        if (session.Status != SessionStatus.InGame || session.AttachedEntity is not { } playerEntity)
        {
            SendSubmitResult(session, false, "You must be in-game to submit a raid request.");
            return;
        }

        if (!TryGetEligibleFaction(playerEntity, out var canonicalFaction))
        {
            SendSubmitResult(session, false, "You are not in a raid-eligible faction.");
            return;
        }

        if (!_factionWar.IsParticipantInActiveWar(GetNetEntity(playerEntity)))
        {
            SendSubmitResult(session, false,
                "You must be participating in an active war to request a raid.");
            return;
        }

        var targetKind    = msg.TargetKind;
        var targetId      = msg.TargetId.Trim();
        var location      = (msg.LocationNotes ?? string.Empty).Trim();
        var reason        = (msg.Reason ?? string.Empty).Trim();

        string targetDisplayName;
        switch (targetKind)
        {
            case RaidRequestTargetKind.Faction:
                if (!RaidRequestConfig.FactionTierFactions.Contains(targetId))
                {
                    SendSubmitResult(session, false, $"'{targetId}' is not a valid raid target.");
                    return;
                }
                targetDisplayName = RaidRequestConfig.FactionDisplayName(targetId);
                break;
            case RaidRequestTargetKind.Group:
                if (!int.TryParse(targetId, out var targetGroupId)
                    || !_groupSystem.TryGetRaidTargetInfo(targetGroupId, out var targetGroup))
                {
                    SendSubmitResult(session, false, $"'{targetId}' is not a valid raid target.");
                    return;
                }
                targetDisplayName = targetGroup.GroupName;
                break;
            default:
                SendSubmitResult(session, false, $"'{targetId}' is not a valid raid target.");
                return;
        }

        if (targetKind == RaidRequestTargetKind.Faction && targetId == canonicalFaction)
        {
            SendSubmitResult(session, false, "You cannot request a raid on your own faction.");
            return;
        }

        if (reason.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < RaidRequestConfig.MinReasonWords)
        {
            SendSubmitResult(session, false,
                $"Reason must be at least {RaidRequestConfig.MinReasonWords} words.");
            return;
        }

        // Clamp free-text inputs so a malicious client can't ship megabytes through chat.
        if (location.Length > 256) location = location[..256];
        if (reason.Length   > 1024) reason   = reason[..1024];

        var isIndividual = RaidRequestConfig.IsIndividualTier(canonicalFaction);

        // Top-rank check for faction-tier submitters.
        if (!isIndividual)
        {
            if (!_minds.TryGetMind(playerEntity, out var mindId, out _))
            {
                SendSubmitResult(session, false, "You have no mind entity.");
                return;
            }
            var myWeight  = GetJobWeight(mindId);
            var topWeight = GetFactionTopWeight(canonicalFaction);
            if (myWeight <= 0 || myWeight < topWeight)
            {
                var topHolder = GetFactionTopJobHolder(canonicalFaction);
                SendSubmitResult(session, false,
                    $"Only the highest-ranking online member may submit. Outranked by: {topHolder}.");
                return;
            }
        }

        // Duplicate check: block a second Pending request from the same submitter (or the same
        // faction for faction-tier) toward the same target.
        foreach (var existing in _requests.Values)
        {
            if (existing.Status != RaidRequestStatus.Pending)
                continue;
            if (existing.TargetKind != targetKind || existing.TargetId != targetId)
                continue;

            var sameSubmitter =
                isIndividual
                    ? existing.RequesterUserId == session.UserId
                    : existing.RequesterFaction == canonicalFaction;

            if (sameSubmitter)
            {
                SendSubmitResult(session, false,
                    "There is already a pending raid request for this target. Wait for an admin decision.");
                return;
            }
        }

        // Build entry
        var jobName = _minds.TryGetMind(playerEntity, out var jobMind, out _)
            ? _jobs.MindTryGetJobName(jobMind)
            : string.Empty;

        var entry = new RaidRequestEntry
        {
            Id                     = _nextId++,
            RequesterUserId        = session.UserId,
            RequesterUserName      = session.Name,
            RequesterCharacterName = Name(playerEntity),
            RequesterJob           = jobName,
            RequesterFaction       = canonicalFaction,
            IsIndividual           = isIndividual,
            TargetKind             = targetKind,
            TargetId               = targetId,
            TargetDisplayName      = targetDisplayName,
            LocationNotes          = location,
            Reason                 = reason,
            CreatedAtUtc           = DateTime.UtcNow,
            Status                 = RaidRequestStatus.Pending,
        };

        _requests[entry.Id] = entry;

        SendSubmitResult(session, true,
            $"Raid request submitted (#{entry.Id}). Awaiting admin review.");

        // Push to subscribed admins.
        BroadcastEntryToAdmins(entry);

        // Quiet admin chat ping so admins not on the tab still notice.
        var who = isIndividual
            ? $"{entry.RequesterCharacterName} ({session.Name})"
            : $"{RaidRequestConfig.FactionDisplayName(canonicalFaction)} (via {entry.RequesterCharacterName})";
        _chat.SendAdminAnnouncement(
            $"[RaidRequest #{entry.Id}] {who} → {GetTargetDisplayName(entry)}: \"{reason}\"");

        // #Misfits Add - Faction-tier raids also prompt the highest-ranking online member of the
        // TARGET faction. They can YES/NO directly, bypassing admin review while still publishing
        // into the same RaidRequest history. If nobody qualifies (e.g. nobody online), request stays
        // Pending and admins decide via the bwoink tab.
        if (!isIndividual)
            TrySendPeerPrompt(entry);
    }

    // ── Admin: subscribe ───────────────────────────────────────────────────

    private void OnAdminSubscribe(RaidRequestAdminSubscribeMsg _, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (!_adminManager.IsAdmin(session))
            return;

        _subscribedAdmins.Add(session);
        SendListTo(session);
    }

    // ── Admin: approve/deny decision ───────────────────────────────────────

    private void OnAdminDecision(RaidRequestDecisionMsg msg, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (!_adminManager.IsAdmin(session))
        {
            SendDecisionResult(session, msg.RequestId, false, "Admin permission required.");
            return;
        }

        if (!_requests.TryGetValue(msg.RequestId, out var entry))
        {
            SendDecisionResult(session, msg.RequestId, false, "Request not found (round may have ended).");
            return;
        }

        if (entry.Status != RaidRequestStatus.Pending)
        {
            SendDecisionResult(session, msg.RequestId, false,
                $"Request already decided ({entry.Status}).");
            return;
        }

        var comment = (msg.Comment ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(comment))
        {
            SendDecisionResult(session, msg.RequestId, false, "A comment is required.");
            return;
        }
        if (comment.Length > 1024) comment = comment[..1024];

        // #Misfits Tweak - Delegate to shared helper so peer-approval and admin paths apply
        // identical side-effects (timer, broadcast, announcement, admin-tab push).
        ApplyDecision(entry, msg.Approve, session.Name, comment);

        SendDecisionResult(session, msg.RequestId, true,
            entry.Status == RaidRequestStatus.Approved ? "Raid approved (5-minute prep)." : "Raid denied.");
    }

    /// <summary>
    /// #Misfits Add - Shared decision side-effects, used by both admin Approve/Deny and the
    /// target-faction leader's peer-approval path. Sets status + audit metadata, schedules the
    /// 5-minute prep activation (on Approve), pushes the entry update to admin tabs, broadcasts
    /// the decision announcement to affected factions, and records a permanent admin-channel line.
    /// Note: the [ALLY]/[ENEMY] overlay is NOT activated here \u2014 that happens in <see cref="ActivateRaid"/>
    /// once the prep timer elapses.
    /// </summary>
    private void ApplyDecision(RaidRequestEntry entry, bool approve, string decidedByName, string comment)
    {
        entry.Status        = approve ? RaidRequestStatus.Approved : RaidRequestStatus.Denied;
        entry.AdminUserName = decidedByName;
        entry.AdminComment  = comment;
        entry.DecidedAtUtc  = DateTime.UtcNow;

        // Whichever path decided first wins; any pending peer-approval popup must be invalidated
        // so a late click from the target leader gets a clean rejection rather than double-firing.
        _pendingPeerPrompts.Remove(entry.Id);

        // Faction-tier approvals enter a 5-minute prep window before the overlay turns on.
        // Individual (Wastelander) approvals never drive the overlay, so no timers needed for them.
        if (approve && !entry.IsIndividual)
        {
            _raidActivationTimes[entry.Id] = _gameTiming.CurTime + RaidPrepDuration;

            // Only the involved sides receive the prep clock.
            DispatchRaidAnnouncement(entry,
                $"INCOMING RAID — {RaidRequestConfig.FactionDisplayName(entry.RequesterFaction)} → " +
                $"{GetTargetDisplayName(entry)}.\n" +
                $"Raid may begin in 5 minutes. Prepare accordingly.",
                Color.OrangeRed);
        }

        BroadcastEntryToAdmins(entry);
        BroadcastDecisionAnnouncement(entry);

        // Permanent admin-channel record.
        var verb = approve ? "APPROVED" : "DENIED";
        _chat.SendAdminAnnouncement(
            $"[RaidRequest #{entry.Id}] {decidedByName} {verb}: " +
            $"{RaidRequestConfig.FactionDisplayName(entry.RequesterFaction)} → " +
            $"{GetTargetDisplayName(entry)}. Remarks: {comment}");
    }

    /// <summary>
    /// #Misfits Add - Promotes an Approved raid to Active when the 5-minute prep elapses.
    /// Starts the 15-minute auto-conclude timer, turns the [ALLY]/[ENEMY] overlay on, and
    /// announces start to the server. Mirrors FactionWarSystem's Pending\u2192Active transition.
    /// </summary>
    private void ActivateRaid(RaidRequestEntry entry)
    {
        entry.Status = RaidRequestStatus.Active;

        _raidActivationTimes.Remove(entry.Id);
        _raidAutoEndTimes[entry.Id] = _gameTiming.CurTime + RaidAutoEndDuration;

        BroadcastEntryToAdmins(entry);
        // Overlay-participants dict now includes this raid's factions \u2014 [ALLY]/[ENEMY] tags appear.
        BroadcastParticipants();

        DispatchRaidAnnouncement(entry,
            $"RAID HAS BEGUN — {RaidRequestConfig.FactionDisplayName(entry.RequesterFaction)} vs " +
            $"{GetTargetDisplayName(entry)}.\n" +
            $"Engagement window: 15 minutes.",
            Color.Red);

        _chat.SendAdminAnnouncement(
            $"[RaidRequest #{entry.Id}] ACTIVE: " +
            $"{RaidRequestConfig.FactionDisplayName(entry.RequesterFaction)} \u2192 " +
            $"{GetTargetDisplayName(entry)}.");
    }

    // ── Admin: end an approved raid (manual conclude) ──────────────────────
    // #Misfits Add - Mirrors OnAdminDecision; only valid against Approved raids.
    private void OnAdminEndRaid(RaidRequestEndMsg msg, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (!_adminManager.IsAdmin(session))
        {
            SendEndResult(session, msg.RequestId, false, "Admin permission required.");
            return;
        }

        if (!_requests.TryGetValue(msg.RequestId, out var entry))
        {
            SendEndResult(session, msg.RequestId, false, "Request not found (round may have ended).");
            return;
        }

        if (entry.Status != RaidRequestStatus.Approved && entry.Status != RaidRequestStatus.Active)
        {
            SendEndResult(session, msg.RequestId, false,
                $"Only Approved or Active raids can be ended (current status: {entry.Status}).");
            return;
        }

        ConcludeRaid(entry, session.Name);
        SendEndResult(session, msg.RequestId, true, "Raid concluded.");
    }

    // ── Peer-faction approval (target leader bypasses admin) ───────────────

    /// <summary>
    /// #Misfits Add - Locates the highest-ranking online member of the target faction or group and sends
    /// them a peer-approval popup. No-op when no eligible leader is online; the request then
    /// remains Pending for admin review.
    /// </summary>
    private void TrySendPeerPrompt(RaidRequestEntry entry)
    {
        ICommonSession? topSession = null;
        var topWeight = 0;

        if (entry.TargetKind == RaidRequestTargetKind.Group)
        {
            if (!TryGetTargetGroupInfo(entry, out var targetGroup))
                return;

            foreach (var member in targetGroup.Members.OrderByDescending(m => (int)m.Role))
            {
                var memberEntity = GetEntity(member.Entity);
                if (!Exists(memberEntity) || !TryComp<ActorComponent>(memberEntity, out var actor))
                    continue;
                if (actor.PlayerSession.Status != SessionStatus.InGame)
                    continue;
                if (!_minds.TryGetMind(memberEntity, out var mindId, out _))
                    continue;

                var w = GetJobWeight(mindId);
                if (w <= 0 || w <= topWeight)
                    continue;

                topWeight = w;
                topSession = actor.PlayerSession;
            }
        }
        else
        {
            foreach (var session in EnumerateFactionMembers(entry.TargetId))
            {
                if (session.AttachedEntity is not { } ent)
                    continue;
                if (!_minds.TryGetMind(ent, out var mindId, out _))
                    continue;

                var w = GetJobWeight(mindId);
                if (w <= 0 || w <= topWeight)
                    continue;

                topWeight = w;
                topSession = session;
            }
        }

        if (topSession == null)
            return;

        _pendingPeerPrompts[entry.Id] = topSession.UserId;
        RaiseNetworkEvent(new RaidRequestPeerPromptMsg { Entry = entry }, topSession);
    }

    private void OnPeerDecision(RaidRequestPeerDecisionMsg msg, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;

        if (!_requests.TryGetValue(msg.RequestId, out var entry))
        {
            SendPeerResult(session, msg.RequestId, false, "Request not found (round may have ended).");
            return;
        }

        // Whoever decided first wins; reject late peer decisions cleanly.
        if (entry.Status != RaidRequestStatus.Pending)
        {
            _pendingPeerPrompts.Remove(entry.Id);
            SendPeerResult(session, msg.RequestId, false,
                $"This request has already been decided ({entry.Status}).");
            return;
        }

        // Verify the responder is the same target leader we sent the prompt to. Blocks any
        // attempt by a different client to spoof the decision message.
        if (!_pendingPeerPrompts.TryGetValue(msg.RequestId, out var expectedUserId)
            || expectedUserId != session.UserId)
        {
            SendPeerResult(session, msg.RequestId, false,
                "You are not authorized to decide this request.");
            return;
        }

        // #Misfits Fix - Re-verify current target membership. A player who died and respawned
        // into a different faction or group since the prompt was sent must not be allowed to decide —
        // they would be approving/denying a raid that affects their new side (conflict of interest).
        if (session.AttachedEntity is not { } currentEntity
            || !IsEntityInTarget(currentEntity, entry))
        {
            _pendingPeerPrompts.Remove(entry.Id);
            SendPeerResult(session, msg.RequestId, false,
                "You are no longer a member of the target faction or group and cannot decide this request. " +
                "Another leader or an admin will be notified.");
            // Re-route to whoever is now highest-ranking in the target faction or group; no-op if nobody qualifies.
            TrySendPeerPrompt(entry);
            return;
        }

        var comment = (msg.Comment ?? string.Empty).Trim();
        if (comment.Length > 1024) comment = comment[..1024];
        if (string.IsNullOrWhiteSpace(comment))
            comment = msg.Approve ? "(no comment)" : "(no reason given)";

        var charName = session.AttachedEntity is { } responderEnt
            ? Name(responderEnt)
            : session.Name;
        var decidedByName = $"{charName} (Target Leader)";

        ApplyDecision(entry, msg.Approve, decidedByName, comment);
        SendPeerResult(session, msg.RequestId, true, msg.Approve ? "Raid approved." : "Raid denied.");
    }

    private void SendPeerResult(ICommonSession session, int requestId, bool success, string message) =>
        RaiseNetworkEvent(
            new RaidRequestPeerDecisionResultMsg { RequestId = requestId, Success = success, Message = message },
            session);

    /// <summary>
    /// #Misfits Add - Concludes an approved raid: sets status, clears the auto-expiry deadline,
    /// rebroadcasts the overlay-participants dict (so [ALLY]/[ENEMY] tags vanish if no other
    /// approved raids remain), pushes the entry update to subscribed admin UIs, and announces
    /// the conclusion to admins. Used by both the manual end-raid path and the auto-expiry sweep.
    /// </summary>
    private void ConcludeRaid(RaidRequestEntry entry, string concludedBy)
    {
        entry.Status           = RaidRequestStatus.Concluded;
        entry.ConcludedAtUtc   = DateTime.UtcNow;
        entry.ConcludedByAdmin = concludedBy;

        _raidAutoEndTimes.Remove(entry.Id);
        // Also drop any pending prep-activation so a still-Approved raid won't auto-flip after end.
        _raidActivationTimes.Remove(entry.Id);
        // And invalidate any outstanding peer-approval popup for this raid.
        _pendingPeerPrompts.Remove(entry.Id);

        BroadcastEntryToAdmins(entry);
        BroadcastParticipants();
        BroadcastConclusionAnnouncement(entry);

        _chat.SendAdminAnnouncement(
            $"[RaidRequest #{entry.Id}] CONCLUDED ({concludedBy}): " +
            $"{RaidRequestConfig.FactionDisplayName(entry.RequesterFaction)} → " +
            $"{GetTargetDisplayName(entry)}.");
    }

    private void SendEndResult(ICommonSession session, int requestId, bool success, string message) =>
        RaiseNetworkEvent(
            new RaidRequestEndResultMsg { RequestId = requestId, Success = success, Message = message },
            session);

    // ── Decision broadcast ────────────────────────────────────────────────

    /// <summary>
    /// Sends <see cref="RaidRequestDecisionAnnouncementMsg"/> to:
    ///   * Individual-tier (Wastelander): ONLY the requesting player. No target-side broadcast —
    ///     this matches the spec ("only the requestor"); a lone wastelander doesn't get to put
    ///     a whole faction on alert just by asking.
    ///   * Faction-tier: every online member of both the requester faction AND the target faction.
    ///   * Group target: every online member of the targeted group.
    /// </summary>
    private void BroadcastDecisionAnnouncement(RaidRequestEntry entry)
    {
        var notified = new HashSet<NetUserId>();
        var chatLine = BuildDecisionChatLine(entry);

        if (entry.IsIndividual)
        {
            // Individual-tier: requester only, no target alert.
            if (TryGetSession(entry.RequesterUserId, out var requesterSession))
                NotifyOne(requesterSession, entry, isTargetSide: false, chatLine, notified);
            return;
        }

        // Faction-tier requester side: all online members of the requester faction.
        foreach (var session in EnumerateFactionMembers(entry.RequesterFaction))
            NotifyOne(session, entry, isTargetSide: false, chatLine, notified);

        // Target side: all online members of the raided faction or group.
        foreach (var session in EnumerateTargetMembers(entry))
            NotifyOne(session, entry, isTargetSide: true, chatLine, notified);
    }

    /// <summary>
    /// Sends both a popup-driving network event and a chat-window system message to one recipient.
    /// Uses <paramref name="notified"/> to dedupe so a player on both the requester and target faction
    /// (which shouldn't really happen, but be safe) doesn't get the message twice.
    /// </summary>
    private void NotifyOne(
        ICommonSession session,
        RaidRequestEntry entry,
        bool isTargetSide,
        string chatLine,
        HashSet<NetUserId> notified)
    {
        if (!notified.Add(session.UserId))
            return;

        RaiseNetworkEvent(
            new RaidRequestDecisionAnnouncementMsg { Entry = entry, IsTargetSide = isTargetSide },
            session);

        // Server system message in the recipient's chat — visible even if they miss the popup.
        _chat.DispatchServerMessage(session, chatLine);
    }

    private string BuildDecisionChatLine(RaidRequestEntry entry)
    {
        var verb = entry.Status == RaidRequestStatus.Approved ? "APPROVED" : "DENIED";
        var whoFrom = entry.IsIndividual
            ? entry.RequesterCharacterName
            : RaidRequestConfig.FactionDisplayName(entry.RequesterFaction);
        var whoTo = GetTargetDisplayName(entry);
        var loc = string.IsNullOrWhiteSpace(entry.LocationNotes) ? "" : $" at {entry.LocationNotes}";
        var admin = entry.AdminUserName ?? "(unknown)";
        var remarks = entry.AdminComment ?? "";
        return $"[Raid Request #{entry.Id} {verb}] {whoFrom} → {whoTo}{loc}\n" +
               $"Reason: {entry.Reason}\n" +
               $"Admin remarks ({admin}): {remarks}";
    }

    /// <summary>
    /// Sends the mandatory raid-over acknowledgement to every online member of both involved
    /// sides. Individual-tier raids notify only their requester, matching their participation
    /// and decision-notification scope.
    /// </summary>
    private void BroadcastConclusionAnnouncement(RaidRequestEntry entry)
    {
        var notified = new HashSet<NetUserId>();

        if (entry.IsIndividual)
        {
            if (TryGetSession(entry.RequesterUserId, out var requesterSession))
                NotifyRaidConcluded(requesterSession, entry, notified);
            return;
        }

        foreach (var session in EnumerateFactionMembers(entry.RequesterFaction))
            NotifyRaidConcluded(session, entry, notified);

        foreach (var session in EnumerateTargetMembers(entry))
            NotifyRaidConcluded(session, entry, notified);
    }

    private void NotifyRaidConcluded(
        ICommonSession session,
        RaidRequestEntry entry,
        HashSet<NetUserId> notified)
    {
        if (!notified.Add(session.UserId))
            return;

        RaiseNetworkEvent(new RaidRequestConcludedAnnouncementMsg { Entry = entry }, session);
        _chat.DispatchServerMessage(session,
            $"Raid #{entry.Id} is over. Combat authorization between " +
            $"{RaidRequestConfig.FactionDisplayName(entry.RequesterFaction)} and " +
            $"{GetTargetDisplayName(entry)} has ended.");
    }

    private string GetTargetDisplayName(RaidRequestEntry entry) => entry.TargetKind switch
    {
        RaidRequestTargetKind.Faction => RaidRequestConfig.FactionDisplayName(entry.TargetId),
        RaidRequestTargetKind.Group   => string.IsNullOrWhiteSpace(entry.TargetDisplayName) ? $"Group {entry.TargetId}" : entry.TargetDisplayName,
        _                             => entry.TargetDisplayName,
    };

    private bool TryGetTargetGroupInfo(RaidRequestEntry entry, out GroupRaidTargetInfo info)
    {
        info = default;
        if (entry.TargetKind != RaidRequestTargetKind.Group)
            return false;
        if (!int.TryParse(entry.TargetId, out var groupId))
            return false;

        return _groupSystem.TryGetRaidTargetInfo(groupId, out info);
    }

    private IEnumerable<ICommonSession> EnumerateTargetMembers(RaidRequestEntry entry)
    {
        if (entry.TargetKind == RaidRequestTargetKind.Group)
        {
            if (!TryGetTargetGroupInfo(entry, out var group))
                yield break;

            foreach (var member in group.Members)
            {
                var memberEntity = GetEntity(member.Entity);
                if (!Exists(memberEntity) || !TryComp<ActorComponent>(memberEntity, out var actor))
                    continue;
                if (actor.PlayerSession.Status != SessionStatus.InGame)
                    continue;
                yield return actor.PlayerSession;
            }

            yield break;
        }

        foreach (var session in EnumerateFactionMembers(entry.TargetId))
            yield return session;
    }

    /// <summary>
    /// Sends a server-style announcement only to the sides involved in a raid. Individual-tier
    /// raids remain private to their requester; faction and group raids notify both sides.
    /// </summary>
    private void DispatchRaidAnnouncement(RaidRequestEntry entry, string message, Color color)
    {
        var recipients = new Dictionary<NetUserId, ICommonSession>();

        if (entry.IsIndividual)
        {
            if (TryGetSession(entry.RequesterUserId, out var requester))
                recipients.TryAdd(requester.UserId, requester);
        }
        else
        {
            foreach (var session in EnumerateFactionMembers(entry.RequesterFaction))
                recipients.TryAdd(session.UserId, session);

            foreach (var session in EnumerateTargetMembers(entry))
                recipients.TryAdd(session.UserId, session);
        }

        if (recipients.Count == 0)
            return;

        var wrappedMessage = Loc.GetString(
            "chat-manager-server-wrap-message",
            ("message", FormattedMessage.EscapeText(message)));

        _chat.ChatMessageToMany(
            ChatChannel.Server,
            message,
            wrappedMessage,
            EntityUid.Invalid,
            hideChat: false,
            recordReplay: true,
            recipients.Values.Select(session => session.Channel),
            color);
    }

    private bool IsEntityInTarget(EntityUid entity, RaidRequestEntry entry)
    {
        return entry.TargetKind switch
        {
            RaidRequestTargetKind.Faction => IsEntityInFaction(entity, entry.TargetId),
            RaidRequestTargetKind.Group   => TryGetTargetGroupInfo(entry, out var group) &&
                                             group.Members.Any(member => member.Entity == GetNetEntity(entity)),
            _                             => false,
        };
    }

    private string GetTargetSideKey(RaidRequestEntry entry) => entry.TargetKind switch
    {
        RaidRequestTargetKind.Faction => $"faction:{entry.TargetId}",
        RaidRequestTargetKind.Group   => $"group:{entry.TargetId}",
        _                             => entry.TargetId,
    };

    // ── Admin sync helpers ─────────────────────────────────────────────────

    private void BroadcastListToAdmins()
    {
        if (_subscribedAdmins.Count == 0)
            return;
        var list = _requests.Values.OrderByDescending(e => e.Id).ToList();
        var msg = new RaidRequestAdminListMsg { Requests = list };
        // Snapshot to avoid modifying the set while iterating if a session vanishes mid-broadcast.
        foreach (var session in _subscribedAdmins.ToArray())
            RaiseNetworkEvent(msg, session);
    }

    private void SendListTo(ICommonSession session)
    {
        var list = _requests.Values.OrderByDescending(e => e.Id).ToList();
        RaiseNetworkEvent(new RaidRequestAdminListMsg { Requests = list }, session);
    }

    private void BroadcastEntryToAdmins(RaidRequestEntry entry)
    {
        if (_subscribedAdmins.Count == 0)
            return;
        var msg = new RaidRequestAdminUpdateMsg { Entry = entry };
        foreach (var session in _subscribedAdmins.ToArray())
            RaiseNetworkEvent(msg, session);
    }

    private void SendSubmitResult(ICommonSession session, bool success, string message) =>
        RaiseNetworkEvent(new RaidRequestSubmitResultMsg { Success = success, Message = message }, session);

    private void SendDecisionResult(ICommonSession session, int requestId, bool success, string message) =>
        RaiseNetworkEvent(
            new RaidRequestDecisionResultMsg { RequestId = requestId, Success = success, Message = message },
            session);

    // ── Faction enumeration / rank helpers (mirror FactionWarSystem) ───────

    /// <summary>
    /// Resolves the player's eligible faction id, if any.
    /// </summary>
    private bool TryGetEligibleFaction(EntityUid entity, out string canonicalFaction)
    {
        // Eighties members also carry the generic PlayerRaider and Wastelander factions.
        // Resolve their gang marker first so raids are attributed to the Eighties instead
        // of the broader raider faction.
        if (_npcFaction.IsMember(entity, "Eighties"))
        {
            canonicalFaction = "Eighties";
            return true;
        }

        // First check faction-tier (prefer those over Wastelander when a player is in both).
        foreach (var f in RaidRequestConfig.AllEligibleFactionIds)
        {
            if (RaidRequestConfig.IsIndividualTier(f))
                continue;
            if (_npcFaction.IsMember(entity, f))
            {
                canonicalFaction = f;
                if (RaidRequestConfig.IsEligible(canonicalFaction))
                    return true;
            }
        }
        foreach (var f in RaidRequestConfig.IndividualTierFactions)
        {
            if (_npcFaction.IsMember(entity, f))
            {
                canonicalFaction = f;
                return true;
            }
        }
        canonicalFaction = string.Empty;
        return false;
    }

    // #Misfits Add - True if the entity is currently a member of canonicalFaction or any alias that resolves to it.
    private bool IsEntityInFaction(EntityUid entity, string canonicalFaction)
    {
        return _npcFaction.IsMember(entity, canonicalFaction);
    }

    /// <summary>Yields all online sessions whose attached entity belongs to <paramref name="canonicalFaction"/> (or its aliases).</summary>
    private IEnumerable<ICommonSession> EnumerateFactionMembers(string canonicalFaction)
    {
        var query = EntityQueryEnumerator<NpcFactionMemberComponent, ActorComponent>();
        while (query.MoveNext(out var entity, out _, out var actor))
        {
            if (actor.PlayerSession.Status != SessionStatus.InGame)
                continue;

            if (_npcFaction.IsMember(entity, canonicalFaction))
                yield return actor.PlayerSession;
        }
    }

    private int GetFactionTopWeight(string canonicalFaction)
    {
        var top = 0;
        var query = EntityQueryEnumerator<NpcFactionMemberComponent, ActorComponent>();
        while (query.MoveNext(out var entity, out _, out var actor))
        {
            if (actor.PlayerSession.Status != SessionStatus.InGame)
                continue;

            if (!_npcFaction.IsMember(entity, canonicalFaction))
                continue;

            if (!_minds.TryGetMind(entity, out var mindId, out _))
                continue;
            var w = GetJobWeight(mindId);
            if (w > top) top = w;
        }
        return top;
    }

    private string GetFactionTopJobHolder(string canonicalFaction)
    {
        var topWeight = 0;
        var topName   = "Unknown";
        var query = EntityQueryEnumerator<NpcFactionMemberComponent, ActorComponent>();
        while (query.MoveNext(out var entity, out _, out var actor))
        {
            if (actor.PlayerSession.Status != SessionStatus.InGame)
                continue;

            if (!_npcFaction.IsMember(entity, canonicalFaction))
                continue;

            if (!_minds.TryGetMind(entity, out var mindId, out _))
                continue;
            var w = GetJobWeight(mindId);
            if (w > topWeight)
            {
                topWeight = w;
                topName   = _jobs.MindTryGetJobName(mindId);
            }
        }
        return topName;
    }

    private int GetJobWeight(EntityUid mindId) =>
        _jobs.MindTryGetJob(mindId, out _, out var proto) ? proto.Weight : 0;

    private bool TryGetSession(NetUserId userId, out ICommonSession session)
    {
        foreach (var s in _playerManager.Sessions)
        {
            if (s.UserId == userId) { session = s; return true; }
        }
        session = default!;
        return false;
    }

    public bool IsFactionUnderActiveRaid(string factionId)
    {
        foreach (var entry in _requests.Values)
        {
            if (entry.Status == RaidRequestStatus.Active
                && entry.TargetKind == RaidRequestTargetKind.Faction
                && entry.TargetId == factionId)
                return true;
        }
        return false;
    }

    // ── Overlay participants ──────────────────────────────────────────────

    /// <summary>
    /// True if at least one Active faction-tier raid is currently on the books.
    /// Approved (prep) raids do NOT count — the overlay only fires once a raid is Active.
    /// Individual (Wastelander) raids are excluded from the overlay entirely.
    /// </summary>
    private bool HasAnyApprovedFactionTierRaid()
    {
        foreach (var entry in _requests.Values)
        {
            if (entry.Status == RaidRequestStatus.Active && !entry.IsIndividual)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Builds the NetEntity → side-key dict for every online player whose attached entity
    /// belongs to a faction or group involved in any approved non-individual raid. Mirrors
    /// FactionWarSystem.BuildParticipantDict so the client can merge both dicts cleanly.
    /// Honors the same OverlayExemptJobs list (Frumentarii, etc.) used by the war overlay.
    /// </summary>
    private Dictionary<NetEntity, string> BuildRaidParticipantDict()
    {
        var dict = new Dictionary<NetEntity, string>();

        // Collect every faction/group side involved in an approved non-individual raid.
        var raidFactions = new HashSet<string>();
        var raidGroups = new Dictionary<int, HashSet<NetEntity>>();
        foreach (var entry in _requests.Values)
        {
            if (entry.Status != RaidRequestStatus.Active || entry.IsIndividual)
                continue;
            raidFactions.Add(entry.RequesterFaction);
            if (entry.TargetKind == RaidRequestTargetKind.Faction)
            {
                raidFactions.Add(entry.TargetId);
                continue;
            }

            if (!TryGetTargetGroupInfo(entry, out var targetGroup))
                continue;

            var members = new HashSet<NetEntity>();
            foreach (var member in targetGroup.Members)
                members.Add(member.Entity);
            raidGroups[targetGroup.GroupId] = members;
        }

        if (raidFactions.Count == 0 && raidGroups.Count == 0)
            return dict;

        // Walk every faction-bearing entity and tag it with the canonical raid side.
        var query = EntityQueryEnumerator<NpcFactionMemberComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            // Skip overlay-exempt jobs (e.g. Frumentarii spies) for parity with the war overlay.
            if (_minds.TryGetMind(uid, out var mindId, out _)
                && _jobs.MindTryGetJob(mindId, out _, out var proto)
                && OverlayExemptJobs.Contains(proto.ID))
                continue;

            foreach (var fId in raidFactions)
            {
                if (!_npcFaction.IsMember(uid, fId))
                    continue;

                dict[GetNetEntity(uid)] = fId;
                break; // first match wins
            }
        }

        // Group sides are keyed by their target group id because they are not NPC factions.
        foreach (var (groupId, members) in raidGroups)
        {
            var sideKey = $"group:{groupId}";
            foreach (var member in members)
                dict[member] = sideKey;
        }

        return dict;
    }

    /// <summary>Broadcasts the current raid-participant dict to all clients. Always sends —
    /// an empty dict tells clients to clear their raid-overlay cache (no approved raids).</summary>
    private void BroadcastParticipants()
    {
        RaiseNetworkEvent(
            new RaidRequestParticipantsUpdatedMsg { Participants = BuildRaidParticipantDict() },
            Filter.Broadcast());
    }

    /// <summary>Sends the current raid-participant dict to a single session (e.g. on connect).</summary>
    private void SendParticipantsTo(ICommonSession session)
    {
        RaiseNetworkEvent(
            new RaidRequestParticipantsUpdatedMsg { Participants = BuildRaidParticipantDict() },
            session);
    }
}
