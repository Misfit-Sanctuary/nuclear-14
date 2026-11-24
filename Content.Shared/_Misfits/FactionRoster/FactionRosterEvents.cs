// #Cythisiax Added - Faction roster: hooks that let a faction add its own recruitment
// rules and effects on top of the generic roster (e.g. Enclave's in-round recruit job).

namespace Content.Shared._Misfits.FactionRoster;

/// <summary>
/// Broadcast before a player is listed as / offered recruitment. Cancel to refuse.
/// </summary>
[ByRefEvent]
public record struct FactionRosterRecruitAttemptEvent(string RosterId, EntityUid Target, bool Cancelled = false);

/// <summary>
/// Broadcast after a recruit accepts the offer and the roster's recruit whitelists
/// have been granted.
/// </summary>
[ByRefEvent]
public readonly record struct FactionRosterRecruitedEvent(string RosterId, EntityUid Recruit, EntityUid Recruiter);
