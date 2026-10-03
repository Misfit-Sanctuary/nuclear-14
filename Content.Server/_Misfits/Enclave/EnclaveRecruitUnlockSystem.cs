// #Misfits Add - Auto-grants Enclave entry-job whitelists to recruits once they
// bank the required EnclaveRecruit playtime.
// #Cythisiax Added - Implements the automatic unlock at 4 hours that was missing:
// the recruit system only tracked playtime via the EnclaveRecruit job; nothing
// ever granted the job whitelists when the threshold was reached.

using Content.Server.Players.JobWhitelist;
using Content.Server.Players.PlayTimeTracking;
using Content.Shared._Misfits.Enclave;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Popups;
using Content.Shared.Roles;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Enclave;

/// <summary>
/// Watches Enclave recruits' playtime and grants the EnclaveEnlisted,
/// EnclaveJuniorScientist and EnclaveOffDuty job whitelists once the EnclaveRecruit tracker
/// reaches the unlock threshold. Whitelists are persisted in the database, so
/// the unlock survives death and round restarts even though recruitment resets.
/// </summary>
public sealed class EnclaveRecruitUnlockSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly PlayTimeTrackingManager _playTime = default!;
    [Dependency] private readonly JobWhitelistManager _jobWhitelist = default!;
    [Dependency] private readonly SharedMindSystem _minds = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    /// <summary>Playtime tracker used by the EnclaveRecruit job.</summary>
    private const string EnclaveRecruitTrackerId = "EnclaveRecruit";

    /// <summary>Jobs unlocked for a recruit that hits the playtime threshold.</summary>
    private static readonly ProtoId<JobPrototype>[] UnlockJobs =
    [
        "EnclaveEnlisted",
        "EnclaveJuniorScientist",
        "EnclaveOffDuty", // #Misfits Add - same tier as Enlisted
    ];

    /// <summary>
    /// Recruit playtime required before the jobs unlock (4 hours). Keep in sync
    /// with the 14400s department time requirement on the entry job prototypes.
    /// </summary>
    private static readonly TimeSpan UnlockTime = TimeSpan.FromHours(4);

    /// <summary>How often to poll tracker times while any recruit is active.</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    private TimeSpan _nextCheck;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextCheck)
            return;

        _nextCheck = now + CheckInterval;
        CheckForUnlocks();
    }

    private void CheckForUnlocks()
    {
        var query = EntityQueryEnumerator<EnclaveRecruitMindComponent>();
        while (query.MoveNext(out var mindId, out var recruit))
        {
            if (recruit.JobsGranted)
                continue;

            if (!_minds.TryGetSession(mindId, out var session))
                continue;

            if (!_playTime.TryGetTrackerTime(session, EnclaveRecruitTrackerId, out var time))
                continue;

            if (time.Value < UnlockTime)
                continue;

            GrantUnlocks(mindId, session);
            recruit.JobsGranted = true;
        }
    }

    private void GrantUnlocks(EntityUid mindId, ICommonSession session)
    {
        foreach (var job in UnlockJobs)
            _jobWhitelist.AddWhitelist(session.UserId, job);

        // Let the recruit know their service earned them the entry jobs.
        if (TryComp<MindComponent>(mindId, out var mind) && mind.CurrentEntity is { } body)
        {
            _popup.PopupEntity(
                Loc.GetString("enclave-recruit-unlocked"),
                body,
                body,
                PopupType.Medium);
        }
    }
}
