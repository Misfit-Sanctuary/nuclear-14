// #Misfits Add - Shared types and network messages for the /raid request system.
// Players submit raid requests; admins approve or deny with comments via the bwoink panel's
// new "Raid Requests" tab. Decisions are broadcast to the requester (and faction) plus the
// target faction so everyone knows whether the raid is sanctioned.
using Content.Shared._Misfits.FactionWar;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.RaidRequest;

// ── Static configuration ──────────────────────────────────────────────────

/// <summary>
/// Eligibility configuration for the raid-request system. Lists factions that can submit raids.
/// </summary>
public static class RaidRequestConfig
{
    /// <summary>
    /// Factions where only the top-ranking online member may submit a raid request and the
    /// resulting decision is broadcast to the entire faction. Mirrors war-capable factions
    /// plus the Misfits-added minor factions that still operate as a group.
    /// </summary>
    public static readonly HashSet<string> FactionTierFactions = new()
    {
        "NCR", "BrotherhoodOfSteel", "CaesarLegion",
        "Townsfolk", "PlayerRaider",
        "Tribal", "Vault", "Followers",
        "Enclave", // #Misfits Add - Enclave remnant faction may submit faction-tier raid requests.
        "ZAX",
        "Eighties", // #Misfits Add - 80s biker gang may submit faction-tier raid requests.
    };

    /// <summary>
    /// Factions where any member may submit on their own behalf and the decision is delivered
    /// only to the individual requester (not the rest of the faction). Wastelanders are loners
    /// and do not coordinate as a group.
    /// </summary>
    public static readonly HashSet<string> IndividualTierFactions = new()
    {
        "Wastelander",
    };

    /// <summary>
    /// All NPC faction IDs that can submit a raid request (faction-tier ∪ individual-tier).
    /// </summary>
    public static readonly HashSet<string> AllEligibleFactionIds = BuildAllEligible();

    private static HashSet<string> BuildAllEligible()
    {
        var set = new HashSet<string>(FactionTierFactions);
        set.UnionWith(IndividualTierFactions);
        return set;
    }

    /// <summary>True if the canonical faction id is allowed to submit raid requests at all.</summary>
    public static bool IsEligible(string canonicalFaction) =>
        FactionTierFactions.Contains(canonicalFaction)
        || IndividualTierFactions.Contains(canonicalFaction);

    /// <summary>True if requests from this faction are individual-only (e.g. Wastelander).</summary>
    public static bool IsIndividualTier(string canonicalFaction) =>
        IndividualTierFactions.Contains(canonicalFaction);

    /// <summary>Display name for factions in the raid system.</summary>
    public static string FactionDisplayName(string canonicalFaction) => canonicalFaction switch
    {
        "Tribal"      => "Tribals",
        "Vault"       => "Vault Dwellers",
        "Followers"   => "Followers of the Apocalypse",
        "Wastelander" => "Wastelander",
        "Eighties"    => "80s",
        "ZAX"         => "Z.A.X",
        _             => canonicalFaction,
    };

    /// <summary>Minimum word count for the reason field (matches /war casus belli).</summary>
    public const int MinReasonWords = 5;
}

// ── Lifecycle types ───────────────────────────────────────────────────────

[Serializable, NetSerializable]
public enum RaidRequestStatus : byte
{
    /// <summary>Submitted, awaiting admin decision.</summary>
    Pending,
    /// <summary>Decision made: yes. 5-minute prep period; overlay tags NOT yet shown.</summary>
    Approved,
    /// <summary>Admin denied the raid.</summary>
    Denied,
    /// <summary>Round ended without an admin decision; flipped from Pending on cleanup.</summary>
    Unclaimed,
    /// <summary>Approved raid was concluded — either manually by an admin or by the 15-minute auto-expiry. Overlay tags removed.</summary>
    Concluded,
    /// <summary>Prep period elapsed; raid is live, [ALLY]/[ENEMY] overlay tags drawn, 15-minute auto-conclude timer running.</summary>
    Active,
}

/// <summary>
/// One raid request as seen by clients. All fields are populated server-side; clients only read.
/// </summary>
[Serializable, NetSerializable]
public sealed class RaidRequestEntry
{
    public int Id;

    // Requester info (snapshotted at submit time so it survives the requester disconnecting)
    public NetUserId RequesterUserId;
    public string RequesterUserName       = string.Empty;
    public string RequesterCharacterName  = string.Empty;
    public string RequesterJob            = string.Empty;

    /// <summary>Canonical faction id of the requester (e.g. "NCR", "Wastelander").</summary>
    public string RequesterFaction = string.Empty;

    /// <summary>True if this is an individual-tier request (Wastelander) — only the requester is notified, not the whole faction.</summary>
    public bool IsIndividual;

    /// <summary>Canonical faction id of the raid target.</summary>
    public string TargetFaction = string.Empty;

    /// <summary>Optional free-text location description.</summary>
    public string LocationNotes = string.Empty;

    /// <summary>Required reason / casus belli (≥ 5 words).</summary>
    public string Reason = string.Empty;

    /// <summary>Wall-clock UTC timestamp of submission (used for client-side display).</summary>
    public DateTime CreatedAtUtc;

    public RaidRequestStatus Status = RaidRequestStatus.Pending;

    // Decision metadata (populated when an admin approves/denies)
    public string? AdminUserName;
    public string? AdminComment;
    public DateTime? DecidedAtUtc;

    // Conclusion metadata (populated when an Approved raid is ended manually or by auto-expiry)
    public DateTime? ConcludedAtUtc;
    /// <summary>Admin who ended the raid, or "Auto-Expiry" when the 15-minute timer ran out.</summary>
    public string? ConcludedByAdmin;

    /// <summary>Associated war key (Player1_Uid_Player2_Uid) if raid was initiated during a war; null if raid exists outside of war context.</summary>
    public string? AssociatedWarId;
}

// ── Network messages: requester ↔ server ──────────────────────────────────

/// <summary>Client → server: open the raid request panel; server replies with panel data.</summary>
[Serializable, NetSerializable]
public sealed class RaidRequestOpenPanelMsg : EntityEventArgs { }

/// <summary>Server → requester: pre-computed panel state (faction, eligibility, target list, history).</summary>
[Serializable, NetSerializable]
public sealed class RaidRequestPanelDataMsg : EntityEventArgs
{
    /// <summary>Canonical faction id of the requester, null if not in any eligible faction.</summary>
    public string? MyFactionId;
    public string MyFactionDisplay = string.Empty;
    /// <summary>True if this faction is individual-tier (Wastelander).</summary>
    public bool MyFactionIsIndividualTier;
    /// <summary>True if the requester may submit (top-ranking for faction-tier, always for individual-tier).</summary>
    public bool CanSubmit;
    /// <summary>If <see cref="CanSubmit"/> is false this explains why for the UI.</summary>
    public string? IneligibleReason;

    /// <summary>Selectable target factions (faction-tier set, minus self).</summary>
    public List<RaidRequestTargetInfo> TargetFactions = new();

    /// <summary>This player's own raid requests this round (lets them see their pending submissions).</summary>
    public List<RaidRequestEntry> MyRequests = new();
}

[Serializable, NetSerializable]
public sealed class RaidRequestTargetInfo
{
    public string Id          = string.Empty;
    public string DisplayName = string.Empty;
}

/// <summary>Client → server: submit a new raid request.</summary>
[Serializable, NetSerializable]
public sealed class RaidRequestSubmitMsg : EntityEventArgs
{
    public string TargetFaction = string.Empty;
    public string LocationNotes = string.Empty;
    public string Reason        = string.Empty;
}

/// <summary>Server → requester: result of a submit attempt.</summary>
[Serializable, NetSerializable]
public sealed class RaidRequestSubmitResultMsg : EntityEventArgs
{
    public bool   Success;
    public string Message = string.Empty;
}

// ── Network messages: admin ↔ server ──────────────────────────────────────

/// <summary>Client → server: admin opened (or refreshed) the raid requests tab; server starts pushing updates.</summary>
[Serializable, NetSerializable]
public sealed class RaidRequestAdminSubscribeMsg : EntityEventArgs { }

/// <summary>Server → subscribed admin: full snapshot of all raid requests this round.</summary>
[Serializable, NetSerializable]
public sealed class RaidRequestAdminListMsg : EntityEventArgs
{
    public List<RaidRequestEntry> Requests = new();
}

/// <summary>Server → all subscribed admins: a single request was created or updated.</summary>
[Serializable, NetSerializable]
public sealed class RaidRequestAdminUpdateMsg : EntityEventArgs
{
    public RaidRequestEntry Entry = new();
}

/// <summary>Client → server: admin approves or denies a request with a comment.</summary>
[Serializable, NetSerializable]
public sealed class RaidRequestDecisionMsg : EntityEventArgs
{
    public int    RequestId;
    public bool   Approve;
    public string Comment = string.Empty;
}

/// <summary>Server → admin: result of a decision attempt (e.g. validation failure).</summary>
[Serializable, NetSerializable]
public sealed class RaidRequestDecisionResultMsg : EntityEventArgs
{
    public int    RequestId;
    public bool   Success;
    public string Message = string.Empty;
}

/// <summary>Client → server: admin manually ends an approved raid (clears [ALLY]/[ENEMY] tags).</summary>
[Serializable, NetSerializable]
public sealed class RaidRequestEndMsg : EntityEventArgs
{
    public int RequestId;
}

/// <summary>Server → admin: result of an end-raid attempt. Reuses the decision-result UI path.</summary>
[Serializable, NetSerializable]
public sealed class RaidRequestEndResultMsg : EntityEventArgs
{
    public int    RequestId;
    public bool   Success;
    public string Message = string.Empty;
}

// ── Peer-faction approval (target leader bypasses admin) ──────────────────

/// <summary>
/// Server → highest-ranking online member of the TARGET faction when a faction-tier raid is
/// submitted. Carries the full entry so the popup window can render context. If no eligible
/// leader is online this is never sent and the request stays Pending for admin review.
/// </summary>
[Serializable, NetSerializable]
public sealed class RaidRequestPeerPromptMsg : EntityEventArgs
{
    public RaidRequestEntry Entry = new();
}

/// <summary>Client → server: target faction leader’s YES/NO with optional remark.</summary>
[Serializable, NetSerializable]
public sealed class RaidRequestPeerDecisionMsg : EntityEventArgs
{
    public int    RequestId;
    public bool   Approve;
    public string Comment = string.Empty;
}

/// <summary>Server → leader: result feedback for the popup (e.g. “already decided”).</summary>
[Serializable, NetSerializable]
public sealed class RaidRequestPeerDecisionResultMsg : EntityEventArgs
{
    public int    RequestId;
    public bool   Success;
    public string Message = string.Empty;
}

// ── Network messages: server → faction (decision broadcast) ───────────────

/// <summary>
/// Server → affected players (requester + their faction for faction-tier; just requester for
/// individual-tier; plus all members of the target faction). Carries the full decided entry so
/// the client can render the popup + chat banner.
/// </summary>
[Serializable, NetSerializable]
public sealed class RaidRequestDecisionAnnouncementMsg : EntityEventArgs
{
    public RaidRequestEntry Entry = new();

    /// <summary>True if the recipient is on the target faction's side (different popup tint).</summary>
    public bool IsTargetSide;
}

// ── Network messages: server → all clients (overlay participants) ─────────

/// <summary>
/// Server → all clients. Periodic broadcast of every entity participating in an
/// approved faction-tier raid, mirroring <c>FactionWarParticipantsUpdatedEvent</c>.
/// Each value is the canonical faction side ID for that entity (requester or target).
/// Used by the AllyTagOverlay to draw [ALLY]/[ENEMY] labels for raid participants
/// when no war is active. Individual (Wastelander) raids are intentionally excluded
/// — they don't put a whole faction on alert.
/// </summary>
[Serializable, NetSerializable]
public sealed class RaidRequestParticipantsUpdatedMsg : EntityEventArgs
{
    public Dictionary<NetEntity, string> Participants = new();
}
