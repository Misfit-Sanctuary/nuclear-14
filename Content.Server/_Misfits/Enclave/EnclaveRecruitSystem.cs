// #Misfits Add - Per-round Enclave recruitment system.
// #Cythisiax Edited - Recruitment now goes through the faction roster GUI (Recruit tab),
// which Enclave command officers open from their action bar; the old right-click
// "Recruit" verb and Enclave consent dialog were removed. This system keeps the
// Enclave-specific part: accepted recruits are assigned the EnclaveRecruit job so their
// time counts toward Enclave department role timers, are implanted with a micro-bomb,
// and lose the recruitment on death or round restart.

using Content.Server._Misfits.FactionRoster;
using Content.Shared._Misfits.Enclave;
using Content.Shared._Misfits.FactionRoster;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.Enclave;

public sealed class EnclaveRecruitSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedJobSystem _jobs = default!;
    [Dependency] private readonly SharedRoleSystem _roles = default!;
    [Dependency] private readonly SharedMindSystem _minds = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly EnclaveMicroBombSystem _microBombs = default!;

    /// <summary>The literal job assigned to accepted recruits.</summary>
    private const string EnclaveRecruitJobId = "EnclaveRecruit";

    /// <summary>The faction roster this system handles recruitment for.</summary>
    public const string EnclaveRosterId = "Enclave";

    public override void Initialize()
    {
        base.Initialize();

        // #Cythisiax Edited - recruitment comes from the faction roster instead of a verb.
        SubscribeLocalEvent<FactionRosterRecruitAttemptEvent>(OnRecruitAttempt);
        SubscribeLocalEvent<FactionRosterRecruitedEvent>(OnRecruited);
        SubscribeLocalEvent<EnclaveRecruiterComponent, ComponentStartup>(OnRecruiterStartup);

        // Remove recruitment on death
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);

        // Clean up all recruitments on round restart
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    /// <summary>
    /// #Cythisiax Edited - Admin bypass: hand the holder the Enclave roster without the job check.
    /// </summary>
    private void OnRecruiterStartup(Entity<EnclaveRecruiterComponent> ent, ref ComponentStartup args)
    {
        var roster = EnsureComp<FactionRosterComponent>(ent);
        roster.Roster = EnclaveRosterId;
        roster.IgnoreJobRequirement = true;
    }

    /// <summary>Players already recruited this round can't be offered again.</summary>
    private void OnRecruitAttempt(ref FactionRosterRecruitAttemptEvent ev)
    {
        if (ev.RosterId != EnclaveRosterId)
            return;

        if (TryComp<MindContainerComponent>(ev.Target, out var mind) &&
            mind.Mind is { } mindId &&
            (HasComp<EnclaveRecruitMindComponent>(mindId) || _jobs.MindHasJobWithId(mindId, EnclaveRecruitJobId)))
        {
            ev.Cancelled = true;
        }
    }

    private void OnRecruited(ref FactionRosterRecruitedEvent ev)
    {
        if (ev.RosterId == EnclaveRosterId)
            ApplyRecruitment(ev.Recruit);
    }

    /// <summary>
    /// Remove recruitment when the player dies.
    /// </summary>
    private void OnMobStateChanged(MobStateChangedEvent ev)
    {
        if (ev.NewMobState != MobState.Dead)
            return;

        RemoveRecruitment(ev.Target);
    }

    /// <summary>
    /// Clean up all EnclaveRecruitMindComponents on round restart.
    /// </summary>
    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        var query = EntityQueryEnumerator<EnclaveRecruitMindComponent>();
        while (query.MoveNext(out var uid, out var recruit))
        {
            RestorePreviousJob(uid, recruit);
            RemComp<EnclaveRecruitMindComponent>(uid);
        }
    }

    /// <summary>
    /// Apply the recruitment after the target accepted the roster's offer.
    /// </summary>
    private void ApplyRecruitment(EntityUid target)
    {
        if (!TryComp<MindContainerComponent>(target, out var targetMind) || targetMind.Mind is not { } mindId)
            return;

        if (HasComp<EnclaveRecruitMindComponent>(mindId))
            return;

        ProtoId<JobPrototype>? previousJob = null;
        if (_jobs.MindTryGetJob(mindId, out var currentJob, out _))
        {
            previousJob = currentJob.Prototype;
            _roles.MindRemoveRole<JobComponent>(mindId);
        }

        var recruit = AddComp<EnclaveRecruitMindComponent>(mindId);
        recruit.PreviousJob = previousJob;
        _roles.MindAddRole(mindId, new JobComponent { Prototype = EnclaveRecruitJobId });
        _microBombs.Implant(target);

        // #Cythisiax Removed - recruiter/recruit popups; the faction roster sends its own.
    }

    /// <summary>
    /// Remove recruitment and restore the player's former job on death.
    /// </summary>
    private void RemoveRecruitment(EntityUid body)
    {
        if (!TryComp<MindContainerComponent>(body, out var mindContainer) || !mindContainer.HasMind)
            return;

        var mindId = mindContainer.Mind.Value;

        if (!TryComp<EnclaveRecruitMindComponent>(mindId, out var recruit))
            return;

        RestorePreviousJob(mindId, recruit);
        RemComp<EnclaveRecruitMindComponent>(mindId);

        // Notify the player they are no longer recruited
        if (_minds.TryGetSession(mindId, out _))
        {
            _popup.PopupEntity(
                Loc.GetString("enclave-recruit-lost"),
                body,
                body,
                PopupType.MediumCaution);
        }
    }

    private void RestorePreviousJob(EntityUid mindId, EnclaveRecruitMindComponent recruit)
    {
        // Do not overwrite a job that another system deliberately assigned
        // after recruitment.
        if (!_jobs.MindHasJobWithId(mindId, EnclaveRecruitJobId))
            return;

        _roles.MindRemoveRole<JobComponent>(mindId);

        if (recruit.PreviousJob is { } previousJob && _prototypes.HasIndex(previousJob))
            _roles.MindAddRole(mindId, new JobComponent { Prototype = previousJob }, silent: true);
    }

    // #Cythisiax Removed - IsEnclaveMember: who may recruit is now the Enclave roster's
    // authorizedJobs (Junior Officer, Senior Officer, Commander, Reformer).
}
