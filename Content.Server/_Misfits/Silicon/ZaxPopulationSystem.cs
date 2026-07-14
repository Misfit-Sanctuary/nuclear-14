using Content.Server.Lathe.Components;
using Content.Server.Spawners.Components;
using Content.Server.Station.Components;
using Content.Server.Station.Events;
using Content.Server.Station.Systems;
using Content.Shared._Misfits.C27;
using Content.Shared._Misfits.Silicon;
using Content.Shared.Lathe;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Prototypes;
using Content.Shared.Research.Prototypes;
using Content.Shared.Silicons.StationAi;
using Robust.Server.Containers;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.Silicon;

/// <summary>
/// [Changed by MisfitsCrew/Operator] Provides server authority for the global Z.A.X chassis limits. A queued or currently
/// printing chassis reserves a slot so multiple foundries cannot over-queue the cap.
/// </summary>
public sealed class ZaxPopulationSystem : EntitySystem
{
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly ContainerSystem _containers = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly StationJobsSystem _stationJobs = default!;
    [Dependency] private readonly StationSystem _station = default!;

    private const string CoreJob = "ZaxCore";
    private const string C27PlayerJob = "C27ZAX";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ZaxMachineFoundryComponent, LatheQueueAttemptEvent>(OnQueueAttempt);
        SubscribeLocalEvent<ZaxMachineFoundryComponent, LatheFinishedPrintingEvent>(OnFinishedPrinting);
        SubscribeLocalEvent<ZaxCoreComponent, MapInitEvent>(OnCoreMapInit);
        SubscribeLocalEvent<ZaxCoreComponent, EntInsertedIntoContainerMessage>(OnCoreContentsChanged);
        SubscribeLocalEvent<ZaxCoreComponent, EntRemovedFromContainerMessage>(OnCoreContentsChanged);
        SubscribeLocalEvent<ContainerSpawnPointComponent, EntityTerminatingEvent>(OnCoreTerminating);
        SubscribeLocalEvent<StationPostInitEvent>(OnStationPostInit);
    }

    private void OnCoreMapInit(Entity<ZaxCoreComponent> ent, ref MapInitEvent args) => RefreshCoreJobSlots(ent.Owner);

    private void OnCoreContentsChanged(Entity<ZaxCoreComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == StationAiCoreComponent.Container)
            RefreshCoreJobSlots(ent.Owner);
    }

    private void OnCoreContentsChanged(Entity<ZaxCoreComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == StationAiCoreComponent.Container)
            RefreshCoreJobSlots(ent.Owner);
    }

    private void OnCoreTerminating(Entity<ContainerSpawnPointComponent> ent, ref EntityTerminatingEvent args)
    {
        if (!HasComp<ZaxCoreComponent>(ent.Owner) || ent.Comp.Job != CoreJob)
            return;

        if (FindJobsStation(ent.Owner) is { } station)
            SetCoreJobSlots(station, ent.Owner);
    }

    private void OnStationPostInit(ref StationPostInitEvent args) => SetCoreJobSlots(args.Station.Owner);

    /// <summary>
    /// Keeps Z.A.X Core availability equal to the number of empty job-spawn cores. This makes
    /// mapped and admin-spawned cores advertise themselves without exposing phantom lobby slots.
    /// </summary>
    private void RefreshCoreJobSlots(EntityUid core)
    {
        if (FindJobsStation(core) is { } station)
            SetCoreJobSlots(station);
    }

    private void SetCoreJobSlots(EntityUid station, EntityUid? excludedCore = null)
    {
        if (!TryComp<StationJobsComponent>(station, out var stationJobs))
            return;

        var available = 0;
        var query = EntityQueryEnumerator<ZaxCoreComponent, ContainerSpawnPointComponent>();
        while (query.MoveNext(out var uid, out _, out var spawnPoint))
        {
            if (uid == excludedCore || spawnPoint.Job != CoreJob || FindJobsStation(uid) != station)
                continue;

            if (_containers.TryGetContainer(uid, spawnPoint.ContainerId, out var container) && container.Count == 0)
                available++;
        }

        if (!_stationJobs.TrySetJobSlots(station, CoreJob, available, available, createSlot: true, stationJobs: stationJobs))
            Log.Error($"Could not synchronize {CoreJob} slots for station {ToPrettyString(station)}.");
    }

    private EntityUid? FindJobsStation(EntityUid core)
    {
        var owning = _station.GetOwningStation(core);
        if (owning != null && HasComp<StationJobsComponent>(owning.Value))
            return owning;

        var query = EntityQueryEnumerator<StationJobsComponent>();
        return query.MoveNext(out var station, out _) ? station : null;
    }

    // [Changed by MisfitsCrew/Operator] Section: live chassis accounting used by the foundry capacity rules.
    /// <summary>
    /// [Changed by MisfitsCrew/Operator] Returns the number of active non-C-27 Z.A.X NPC/ghost-role chassis.
    /// </summary>
    public int GetActiveUnitCount()
    {
        var count = 0;
        var query = EntityQueryEnumerator<ZaxLinkedUnitComponent, ZaxUnitComponent>();
        while (query.MoveNext(out var uid, out _, out _))
        {
            if (HasComp<MisfitsC27Component>(uid) || !OccupiesSlot(uid))
                continue;

            count++;
        }

        return count;
    }

    /// <summary>
    /// [Changed by MisfitsCrew/Operator] Returns the number of active Z.A.X-linked C-27 chassis.
    /// </summary>
    public int GetActiveC27Count()
    {
        var count = 0;
        var query = EntityQueryEnumerator<ZaxLinkedUnitComponent, MisfitsC27Component>();
        while (query.MoveNext(out var uid, out _, out _))
        {
            if (OccupiesSlot(uid))
                count++;
        }

        return count;
    }

    /// <summary>
    /// Returns the number of living player chassis in a shared chassis family.
    /// </summary>
    public int GetActivePlayerChassisCount(ZaxPlayerChassisKind kind)
    {
        var count = 0;
        var query = EntityQueryEnumerator<ZaxPlayerChassisComponent>();
        while (query.MoveNext(out var uid, out var chassis))
        {
            if (chassis.Kind == kind && OccupiesSlot(uid))
                count++;
        }

        return count;
    }

    // [Changed by MisfitsCrew/Operator] Section: queue reservations prevent bulk and multi-foundry cap bypasses.
    private void OnQueueAttempt(Entity<ZaxMachineFoundryComponent> ent, ref LatheQueueAttemptEvent args)
    {
        if (ent.Comp.PlayerJobRecipes.TryGetValue(args.Recipe.ID, out var playerJob) &&
            ent.Comp.PlayerJobCaps.TryGetValue(playerJob, out var playerCap))
        {
            if (!TryGetPlayerChassisKind(playerJob, out var kind))
            {
                Log.Error($"Z.A.X Foundry player cap for {playerJob} has no chassis-family mapping.");
                args.Cancelled = true;
                return;
            }

            var playerActive = GetActivePlayerChassisCount(kind);
            var available = CountAvailablePlayerSlots(playerJob);
            var playerReserved = CountReservedPlayerJob(playerJob);
            if ((long) playerActive + available + playerReserved < playerCap)
                return;

            args.Cancelled = true;
            var playerMessage = Loc.GetString("zax-foundry-player-cap-reached", ("cap", playerCap));
            if (args.Actor is { } playerActor && Exists(playerActor))
                _popup.PopupEntity(playerMessage, ent.Owner, playerActor, PopupType.SmallCaution);
            else
                _popup.PopupEntity(playerMessage, ent.Owner);
            return;
        }

        // [Changed by MisfitsCrew/Operator] Reserve queued/current builds globally so bulk or parallel foundries cannot exceed caps.
        if (!TryClassifyResult(args.Recipe, out var isUnit, out var isC27) || (!isUnit && !isC27))
            return;

        var reserved = CountReserved(isC27);
        var active = isC27 ? GetActiveC27Count() : GetActiveUnitCount();
        var cap = isC27 ? ent.Comp.MaxActiveC27s : ent.Comp.MaxActiveUnits;
        // An unclaimed player C-27 slot represents a fabricated chassis and consumes
        // the same global C-27 capacity as a living or queued chassis.
        var availableC27Slots = isC27 ? CountAvailablePlayerSlots(C27PlayerJob) : 0;
        if ((long) active + reserved + availableC27Slots < cap)
            return;

        args.Cancelled = true;
        var message = isC27
            ? Loc.GetString("zax-foundry-c27-cap-reached", ("cap", cap))
            : Loc.GetString("zax-foundry-unit-cap-reached", ("cap", cap));

        if (args.Actor is { } actor && Exists(actor))
            _popup.PopupEntity(message, ent.Owner, actor, PopupType.SmallCaution);
        else
            _popup.PopupEntity(message, ent.Owner);
    }

    private void OnFinishedPrinting(Entity<ZaxMachineFoundryComponent> ent, ref LatheFinishedPrintingEvent args)
    {
        if (!ent.Comp.PlayerJobRecipes.TryGetValue(args.Recipe.ID, out var job))
            return;

        // Nuclear-14 maps use a job-bearing station entity even when the foundry itself is off-station.
        var query = EntityQueryEnumerator<StationJobsComponent>();
        if (!query.MoveNext(out var station, out _))
        {
            Log.Error($"Z.A.X foundry could not open {job}: no station jobs entity exists.");
            return;
        }

        if (!_stationJobs.TryAdjustJobSlot(station, job, 1, createSlot: true, clamp: true))
        {
            Log.Error($"Z.A.X foundry could not open late-join slot for {job}.");
            return;
        }

        QueueDel(args.Result);
        _popup.PopupEntity(Loc.GetString("zax-foundry-player-slot-opened"), ent.Owner);
    }

    private int CountReserved(bool c27)
    {
        var count = 0;
        var query = EntityQueryEnumerator<ZaxMachineFoundryComponent, LatheComponent>();
        while (query.MoveNext(out _, out _, out var lathe))
        {
            if (lathe.CurrentRecipe != null &&
                TryClassifyResult(lathe.CurrentRecipe, out var currentUnit, out var currentC27) &&
                (c27 ? currentC27 : currentUnit))
            {
                count++;
            }

            foreach (var recipe in lathe.Queue)
            {
                if (TryClassifyResult(recipe, out var queuedUnit, out var queuedC27) &&
                    (c27 ? queuedC27 : queuedUnit))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private int CountReservedPlayerJob(string job)
    {
        var count = 0;
        var query = EntityQueryEnumerator<ZaxMachineFoundryComponent, LatheComponent>();
        while (query.MoveNext(out _, out var foundry, out var lathe))
        {
            if (lathe.CurrentRecipe != null &&
                foundry.PlayerJobRecipes.TryGetValue(lathe.CurrentRecipe.ID, out var currentJob) &&
                currentJob == job)
            {
                count++;
            }

            foreach (var recipe in lathe.Queue)
            {
                if (foundry.PlayerJobRecipes.TryGetValue(recipe.ID, out var queuedJob) && queuedJob == job)
                    count++;
            }
        }

        return count;
    }

    private int CountAvailablePlayerSlots(string job)
    {
        var count = 0;
        var query = EntityQueryEnumerator<StationJobsComponent>();
        while (query.MoveNext(out var station, out var stationJobs))
        {
            if (!_stationJobs.GetJobs(station, stationJobs).TryGetValue(job, out var slots))
                continue;

            // An unlimited player-chassis job has already exceeded every finite cap.
            if (slots == null)
                return int.MaxValue;

            count += (int) slots.Value;
        }

        return count;
    }

    private static bool TryGetPlayerChassisKind(string job, out ZaxPlayerChassisKind kind)
    {
        switch (job)
        {
            case "ZaxSecuritron":
                kind = ZaxPlayerChassisKind.Securitron;
                return true;
            case "ZaxAssaultron":
                kind = ZaxPlayerChassisKind.Assaultron;
                return true;
            case "ZaxProtectron":
                kind = ZaxPlayerChassisKind.Protectron;
                return true;
            case "ZaxMrGutsy":
                kind = ZaxPlayerChassisKind.MrGutsy;
                return true;
            case "ZaxMrHandy":
                kind = ZaxPlayerChassisKind.MrHandy;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private bool TryClassifyResult(LatheRecipePrototype recipe, out bool unit, out bool c27)
    {
        unit = false;
        c27 = false;
        if (recipe.Result is not { } result ||
            !_prototypes.TryIndex<EntityPrototype>(result, out var prototype))
        {
            return false;
        }

        c27 = prototype.HasComponent<MisfitsC27Component>();
        unit = prototype.HasComponent<ZaxLinkedUnitComponent>() &&
            prototype.HasComponent<ZaxUnitComponent>() &&
            !c27;
        return true;
    }

    private bool OccupiesSlot(EntityUid uid)
    {
        return !Deleted(uid) &&
            (!TryComp(uid, out MobStateComponent? mobState) || !_mobState.IsDead(uid, mobState));
    }
}
