// #Misfits Add - Shared network messages for the faction war system.
// Syncs active war declarations between server and all connected clients,
// and carries GUI request/response traffic between client and server.
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.FactionWar;

// ── Shared static config (used by both client and server) ─────────────────

/// <summary>
/// Static faction configuration shared between the server war system and the client GUI.
/// Must live in the Shared project so both assemblies can read it without cross-referencing.
/// </summary>
public static class FactionWarConfig
{
    /// <summary>Factions that may participate in war declarations (selectable targets).</summary>
    public static readonly HashSet<string> WarCapableFactions = new()
    {
        "NCR", "BrotherhoodOfSteel", "CaesarLegion",
        "Townsfolk", "PlayerRaider",
    };

    /// <summary>
    /// Maps NPC faction IDs that should be treated as another war faction.
    /// e.g. "Rangers" members are treated as "NCR" for war purposes.
    /// </summary>
    public static readonly Dictionary<string, string> FactionAliases = new()
    {
        ["Rangers"] = "NCR",
    };

    /// <summary>
    /// All NPC faction IDs that can resolve to a war faction
    /// (either directly in <see cref="WarCapableFactions"/> or via <see cref="FactionAliases"/>).
    /// Used for IsMember iteration.
    /// </summary>
    public static readonly HashSet<string> AllWarFactionIds = new(WarCapableFactions)
    {
        "Rangers",
    };

    public static readonly Dictionary<string, string> FactionDisplayNames = new()
    {
        ["NCR"]                = "NCR",
        ["BrotherhoodOfSteel"] = "Brotherhood of Steel",
        ["CaesarLegion"]       = "Caesar's Legion",
        ["Townsfolk"]          = "Townsfolk",
        ["PlayerRaider"]       = "Raiders",
    };

    /// <summary>
    /// Resolves a raw NPC faction ID to its canonical war faction ID.
    /// e.g. "Rangers" → "NCR", "NCR" → "NCR".
    /// </summary>
    public static string ResolveWarFaction(string factionId) =>
        FactionAliases.TryGetValue(factionId, out var alias) ? alias : factionId;

    public static string FactionDisplayName(string factionId) =>
        FactionDisplayNames.TryGetValue(factionId, out var name) ? name : factionId;

    // #Misfits Add - Job prototype IDs exempt from the ally/enemy overlay.
    // Entities with these jobs won't appear in the participant dict, so no tag is rendered above them.
    // Used for spy roles like Frumentarii that should remain unidentifiable.
    public static readonly HashSet<string> OverlayExemptJobs = new()
    {
        "CaesarLegionFrumentarii",
    };
}

// ── Network message types ──────────────────────────────────────────────────

/// <summary>War lifecycle phase.</summary>
[Serializable, NetSerializable]
public enum WarPhase : byte
{
    /// <summary>War declared but not yet active. /warjoin is open.</summary>
    Pending,
    /// <summary>War is active. /warjoin is closed.</summary>
    Active,
}

/// <summary>
/// A single active war declaration, transmitted as part of <see cref="FactionWarStateUpdatedEvent"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class FactionWarEntry
{
    /// <summary>Faction prototype ID of the party that declared war.</summary>
    public string AggressorFaction = string.Empty;

    /// <summary>Faction prototype ID of the party war was declared upon.</summary>
    public string TargetFaction = string.Empty;

    /// <summary>Player-supplied justification for the war declaration.</summary>
    public string CasusBelli = string.Empty;

    /// <summary>In-world character name of the declaring player.</summary>
    public string DeclarerCharacterName = string.Empty;

    /// <summary>Localised job title of the declaring player at time of declaration.</summary>
    public string DeclarerJobName = string.Empty;

    /// <summary>Current phase of this war (Pending during 5-min prep, Active after).</summary>
    public WarPhase Phase = WarPhase.Pending;
}

/// <summary>
/// Server → all clients. Sent whenever the war state changes: declaration, ceasefire, or admin override.
/// Clients replace their entire local war list with <see cref="ActiveWars"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class FactionWarStateUpdatedEvent : EntityEventArgs
{
    public List<FactionWarEntry> ActiveWars = new();
}

// ── GUI request/response messages ─────────────────────────────────────────

/// <summary>
/// Client → server. Player opened the war panel and needs faction/eligibility data.
/// Server responds with <see cref="FactionWarPanelDataEvent"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class FactionWarOpenPanelRequestEvent : EntityEventArgs { }

/// <summary>
/// Server → requesting client. Pre-computed panel state including the player's faction,
/// eligible targets, and ceasefire options. All faction logic stays server-side because
/// NpcFactionMemberComponent.Factions is not synced to the client.
/// </summary>
[Serializable, NetSerializable]
public sealed class FactionWarPanelDataEvent : EntityEventArgs
{
    public string? MyFactionId;
    public string MyFactionDisplay = string.Empty;
    public List<FactionWarEntry> ActiveWars = new();
    public List<FactionWarTargetInfo> EligibleTargets = new();
    public List<FactionWarTargetInfo> CeasefireTargets = new();
    public string? StatusMessage;
}

/// <summary>
/// A faction target entry used in <see cref="FactionWarPanelDataEvent"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class FactionWarTargetInfo
{
    public string Id = string.Empty;
    public string DisplayName = string.Empty;
}

/// <summary>
/// Client → server. Player submits the Declare War form from the GUI panel.
/// Server validates rank, one-war rule, etc. and responds with <see cref="FactionWarCommandResultEvent"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class FactionWarDeclareRequestEvent : EntityEventArgs
{
    public string TargetFaction = string.Empty;
    public string CasusBelli    = string.Empty;
}

/// <summary>
/// Client → server. Player requests a ceasefire with a faction they are currently at war with.
/// </summary>
[Serializable, NetSerializable]
public sealed class FactionWarCeasefireRequestEvent : EntityEventArgs
{
    public string TargetFaction = string.Empty;
}

/// <summary>
/// Server → the requesting client only. Delivers success/failure feedback to the GUI panel.
/// </summary>
[Serializable, NetSerializable]
public sealed class FactionWarCommandResultEvent : EntityEventArgs
{
    public bool   Success = false;
    public string Message = string.Empty;
}

// ── /warjoin network messages ─────────────────────────────────────────────

/// <summary>
/// Client → server. Player opened the warjoin panel and needs pending-war data.
/// Server responds with <see cref="FactionWarJoinPanelDataEvent"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class FactionWarJoinPanelRequestEvent : EntityEventArgs { }

/// <summary>
/// Server → the requesting client. Pre-computed data for the warjoin panel.
/// </summary>
[Serializable, NetSerializable]
public sealed class FactionWarJoinPanelDataEvent : EntityEventArgs
{
    public List<FactionWarEntry> PendingWars = new();
    public bool AlreadyInFaction;
    public string? AlreadyJoinedSide;
    public string? StatusMessage;

    // #Misfits Add - Fields for faction-wide enlistment UI.
    /// <summary>True if the player is the highest-ranking online member of their war-capable faction.</summary>
    public bool IsTopRanking;
    /// <summary>The player's canonical war-faction ID (null if unaffiliated or Wastelander).</summary>
    public string? MyWarFactionId;
}

/// <summary>
/// Client → server. Player requests to join a war on a specific side.
/// </summary>
[Serializable, NetSerializable]
public sealed class FactionWarJoinRequestEvent : EntityEventArgs
{
    public string AggressorFaction = string.Empty;
    public string TargetFaction    = string.Empty;
    public string ChosenSide       = string.Empty;

    // #Misfits Add - When true, the top-ranking player enlists all online faction members at once.
    public bool FactionWide;
}

/// <summary>
/// Server → the requesting client only. Warjoin-specific result feedback.
/// </summary>
[Serializable, NetSerializable]
public sealed class FactionWarJoinResultEvent : EntityEventArgs
{
    public bool   Success = false;
    public string Message = string.Empty;
}

/// <summary>
/// Server → all clients. Broadcast whenever the individual war-participant list changes.
/// Maps each participant entity to the faction side they are fighting for.
/// </summary>
[Serializable, NetSerializable]
public sealed class FactionWarParticipantsUpdatedEvent : EntityEventArgs
{
    public Dictionary<NetEntity, string> Participants = new();
}

// ── /forcewar admin network messages ──────────────────────────────────────

/// <summary>
/// Client → server. Admin requests to force-declare a war between two factions.
/// Bypasses round-start cooldown, post-war cooldown, and rank checks.
/// </summary>
[Serializable, NetSerializable]
public sealed class FactionWarForceRequestEvent : EntityEventArgs
{
    public string AggressorFaction = string.Empty;
    public string TargetFaction    = string.Empty;
    public string CasusBelli       = string.Empty;
}

/// <summary>
/// Server → the requesting admin client. Result feedback for the forcewar GUI.
/// </summary>
[Serializable, NetSerializable]
public sealed class FactionWarForceResultEvent : EntityEventArgs
{
    public bool   Success = false;
    public string Message = string.Empty;

    /// <summary>True when this result is for a ceasefire action, false for a declare action.</summary>
    public bool   IsCeasefire = false;
}

// ── Admin force-ceasefire network messages ─────────────────────────────────

/// <summary>
/// Client → server. Admin requests to forcibly end an active war.
/// </summary>
[Serializable, NetSerializable]
public sealed class FactionWarForceCeasefireRequestEvent : EntityEventArgs
{
    public string AggressorFaction = string.Empty;
    public string TargetFaction    = string.Empty;
}
