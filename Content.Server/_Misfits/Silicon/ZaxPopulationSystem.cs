using Content.Server.Lathe.Components;
using Content.Shared._Misfits.C27;
using Content.Shared._Misfits.Silicon;
using Content.Shared.Lathe;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Prototypes;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.Silicon;

/// <summary>
/// [Changed by MisfitsCrew/Operator] Provides server authority for the global Z.A.X chassis limits. A queued or currently
/// printing chassis reserves a slot so multiple foundries cannot over-queue the cap.
/// </summary>
public sealed class ZaxPopulationSystem : EntitySystem
{
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ZaxMachineFoundryComponent, LatheQueueAttemptEvent>(OnQueueAttempt);
    }

    // [Changed by MisfitsCrew/Operator] Section: live chassis accounting used by the foundry capacity rules.
    /// <summary>
    /// [Changed by MisfitsCrew/Operator] Returns the number of active Z.A.X-linked chassis, excluding C-27s.
    /// </summary>
    public int GetActiveUnitCount()
    {
        var count = 0;
        var query = EntityQueryEnumerator<ZaxLinkedUnitComponent>();
        while (query.MoveNext(out var uid, out _))
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

    // [Changed by MisfitsCrew/Operator] Section: queue reservations prevent bulk and multi-foundry cap bypasses.
    private void OnQueueAttempt(Entity<ZaxMachineFoundryComponent> ent, ref LatheQueueAttemptEvent args)
    {
        // [Changed by MisfitsCrew/Operator] Reserve queued/current builds globally so bulk or parallel foundries cannot exceed caps.
        if (!TryClassifyResult(args.Recipe, out var isUnit, out var isC27) || (!isUnit && !isC27))
            return;

        var reserved = CountReserved(isC27);
        var active = isC27 ? GetActiveC27Count() : GetActiveUnitCount();
        var cap = isC27 ? ent.Comp.MaxActiveC27s : ent.Comp.MaxActiveUnits;
        if (active + reserved < cap)
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
        unit = prototype.HasComponent<ZaxLinkedUnitComponent>() && !c27;
        return true;
    }

    private bool OccupiesSlot(EntityUid uid)
    {
        return !Deleted(uid) &&
            (!TryComp(uid, out MobStateComponent? mobState) || !_mobState.IsDead(uid, mobState));
    }
}
