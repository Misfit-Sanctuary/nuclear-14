// #Cythisiax Added - Faction roster: network state and messages for the roster GUI
// and the recruitment consent dialog.

using Content.Shared.Eui;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.FactionRoster;

/// <summary>A member's standing on one rank track, with what the viewer may do about it.</summary>
[Serializable, NetSerializable]
public sealed record FactionRosterStanding(int Rank, bool CanPromote, bool CanDemote);

/// <summary>One whitelisted member of the faction.</summary>
[Serializable, NetSerializable]
public sealed record FactionRosterMember(
    NetUserId UserId,
    string UserName,
    string? CharacterName,
    bool Online,
    bool Leadership,
    bool Locked,
    List<FactionRosterStanding> Standings,
    List<string> Specialties);

/// <summary>A nearby player who can be offered recruitment.</summary>
[Serializable, NetSerializable]
public sealed record FactionRosterCandidate(NetEntity Entity, string Name, bool Pending);

[Serializable, NetSerializable]
public sealed record FactionRosterTrackInfo(string Name, List<string> Ranks);

[Serializable, NetSerializable]
public sealed class FactionRosterEuiState : EuiStateBase
{
    public readonly string Title;
    public readonly List<FactionRosterTrackInfo> Tracks;
    public readonly List<string> Specialties;
    public readonly List<FactionRosterMember> Members;
    public readonly List<FactionRosterCandidate> Candidates;
    public readonly bool Loading;
    public readonly string? Status;

    /// <summary>Current oath text, with [name] / [recruiter] placeholders.</summary>
    public readonly string Oath;
    public readonly bool OathIsCustom;
    public readonly bool CanEditOath;

    public FactionRosterEuiState(
        string title,
        List<FactionRosterTrackInfo> tracks,
        List<string> specialties,
        List<FactionRosterMember> members,
        List<FactionRosterCandidate> candidates,
        bool loading,
        string? status,
        string oath,
        bool oathIsCustom,
        bool canEditOath)
    {
        Title = title;
        Tracks = tracks;
        Specialties = specialties;
        Members = members;
        Candidates = candidates;
        Loading = loading;
        Status = status;
        Oath = oath;
        OathIsCustom = oathIsCustom;
        CanEditOath = canEditOath;
    }
}

[Serializable, NetSerializable]
public sealed class FactionRosterRefreshMessage : EuiMessageBase;

[Serializable, NetSerializable]
public sealed class FactionRosterPromoteMessage(NetUserId userId, int track) : EuiMessageBase
{
    public readonly NetUserId UserId = userId;
    public readonly int Track = track;
}

[Serializable, NetSerializable]
public sealed class FactionRosterDemoteMessage(NetUserId userId, int track) : EuiMessageBase
{
    public readonly NetUserId UserId = userId;
    public readonly int Track = track;
}

[Serializable, NetSerializable]
public sealed class FactionRosterDischargeMessage(NetUserId userId) : EuiMessageBase
{
    public readonly NetUserId UserId = userId;
}

[Serializable, NetSerializable]
public sealed class FactionRosterToggleSpecialtyMessage(NetUserId userId, string role) : EuiMessageBase
{
    public readonly NetUserId UserId = userId;
    public readonly string Role = role;
}

[Serializable, NetSerializable]
public sealed class FactionRosterRecruitMessage(NetEntity target) : EuiMessageBase
{
    public readonly NetEntity Target = target;
}

/// <summary>Leadership sets the oath; null restores the faction's default.</summary>
[Serializable, NetSerializable]
public sealed class FactionRosterSetOathMessage(string? oath) : EuiMessageBase
{
    public readonly string? Oath = oath;
}

/// <summary>Sent to the player being offered recruitment.</summary>
[Serializable, NetSerializable]
public sealed class FactionRecruitOfferEuiState(string factionName, string recruiterName, string offerText)
    : EuiStateBase
{
    public readonly string FactionName = factionName;
    public readonly string RecruiterName = recruiterName;
    public readonly string OfferText = offerText;
}

[Serializable, NetSerializable]
public sealed class FactionRecruitOfferDecisionMessage(bool accepted) : EuiMessageBase
{
    public readonly bool Accepted = accepted;
}
