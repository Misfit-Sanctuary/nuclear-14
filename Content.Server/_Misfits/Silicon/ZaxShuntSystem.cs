using Content.Server.NPC.HTN;
using Content.Server.Silicons.StationAi;
using Content.Server.Ghost.Roles.Components;
using Content.Shared._Misfits.Silicon;
using Content.Shared.Actions;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Silicons.StationAi;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Player;

namespace Content.Server._Misfits.Silicon;

/// <summary>
/// [Changed by MisfitsCrew/Operator] Moves a Z.A.X core mind into an NPC chassis using mind visitation. Visitation
/// preserves the original brain as the authoritative return destination and avoids
/// replacing any mind which may already own a player-controlled Z.A.X chassis.
/// </summary>
public sealed class ZaxShuntSystem : EntitySystem
{
    // [Changed by MisfitsCrew/Operator] Section: temporary actions make a visited NPC behave as the active Z.A.X controller.
    private static readonly string[] CommandActions =
    {
        "ActionStationAiSelectNpc",
        "ActionStationAiClearNpcSelection",
        "ActionStationAiMoveNpcs",
        "ActionStationAiFormationMoveNpcs",
        "ActionStationAiMoveAndAttackNpcs",
        "ActionStationAiEngageNpcs",
        "ActionStationAiHoldNpcs",
        "ActionZaxReturnToCore",
    };

    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedPowerReceiverSystem _power = default!;
    [Dependency] private readonly StationAiNpcCommandSystem _commands = default!;
    [Dependency] private readonly StationAiSystem _stationAi = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StationAiNpcCommanderComponent, ZaxShuntActionEvent>(OnShunt);
        SubscribeLocalEvent<ZaxShuntedComponent, ZaxReturnToCoreActionEvent>(OnReturn);
        SubscribeLocalEvent<ZaxShuntedComponent, EntityTerminatingEvent>(OnTerminating);
        SubscribeLocalEvent<ZaxShuntedComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<ZaxCoreComponent, EntityTerminatingEvent>(OnCoreTerminating);
        SubscribeLocalEvent<StationAiHeldComponent, EntityTerminatingEvent>(OnBrainTerminating);
    }

    // [Changed by MisfitsCrew/Operator] Section: shunt entry moves control state and disables core-only vision/ghost takeover.
    private void OnShunt(Entity<StationAiNpcCommanderComponent> ent, ref ZaxShuntActionEvent args)
    {
        // [Changed by MisfitsCrew/Operator] Validate power, core identity, occupancy, ownership, life state, and visibility server-side.
        if (args.Handled ||
            !TryComp(ent.Owner, out StationAiHeldComponent? held) ||
            !_commands.TryGetCore((ent.Owner, held), out var core) ||
            !HasComp<ZaxCoreComponent>(core.Value.Owner) ||
            !_power.IsPowered(core.Value.Owner) ||
            !CanShuntInto(ent.Owner, args.Target))
        {
            return;
        }

        args.Handled = true;
        if (!_mind.TryGetMind(ent.Owner, out var mindId, out _))
            return;

        _commands.ReleaseNpcForShunt(ent.Owner, args.Target);

        var targetCommander = EnsureComp<StationAiNpcCommanderComponent>(args.Target);
        CopyCommandState(ent.Comp, targetCommander, args.Target);
        Dirty(args.Target, targetCommander);
        ent.Comp.SelectedNpcs.Clear();
        ent.Comp.PendingMoveTargets.Clear();
        ent.Comp.MoveTargetPreviews.Clear();
        Dirty(ent);

        var shunted = EnsureComp<ZaxShuntedComponent>(args.Target);
        shunted.Brain = ent.Owner;
        shunted.Core = core.Value.Owner;
        shunted.RestoreGhostTakeover = HasComp<GhostTakeoverAvailableComponent>(args.Target);
        if (shunted.RestoreGhostTakeover)
            RemComp<GhostTakeoverAvailableComponent>(args.Target);
        foreach (var prototype in CommandActions)
        {
            EntityUid? action = null;
            if (_actions.AddAction(args.Target, ref action, prototype) && action != null)
                shunted.GrantedActions.Add(action.Value);
        }

        _stationAi.SuspendVisionSubscriptions(ent.Owner);
        _mind.Visit(mindId, args.Target);
        _popup.PopupEntity(Loc.GetString("zax-shunt-entered", ("unit", Name(args.Target))), args.Target, args.Target);
    }

    private void OnReturn(Entity<ZaxShuntedComponent> ent, ref ZaxReturnToCoreActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        ReturnToCore(ent);
    }

    // [Changed by MisfitsCrew/Operator] Section: lifecycle handlers guarantee cleanup on body, core, or brain loss.
    private void OnTerminating(Entity<ZaxShuntedComponent> ent, ref EntityTerminatingEvent args)
    {
        ReturnToCore(ent);
    }

    private void OnMobStateChanged(Entity<ZaxShuntedComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            ReturnToCore(ent);
    }

    private void OnCoreTerminating(Entity<ZaxCoreComponent> ent, ref EntityTerminatingEvent args)
    {
        ReturnMatchingShunts(ent.Owner, null);
    }

    private void OnBrainTerminating(Entity<StationAiHeldComponent> ent, ref EntityTerminatingEvent args)
    {
        ReturnMatchingShunts(null, ent.Owner);
    }

    private void ReturnMatchingShunts(EntityUid? core, EntityUid? brain)
    {
        var query = EntityQueryEnumerator<ZaxShuntedComponent>();
        while (query.MoveNext(out var uid, out var shunted))
        {
            if ((core == null || shunted.Core == core) && (brain == null || shunted.Brain == brain))
                ReturnToCore((uid, shunted));
        }
    }

    private void ReturnToCore(Entity<ZaxShuntedComponent> ent)
    {
        // [Changed by MisfitsCrew/Operator] Make cleanup idempotent across death, deletion, core, and brain teardown events.
        if (ent.Comp.Returning)
            return;

        ent.Comp.Returning = true;
        var brainExists = Exists(ent.Comp.Brain);
        var hasVisitingMind = TryComp(ent.Owner, out VisitingMindComponent? visiting) && visiting.MindId != null;
        if (TryComp(ent.Owner, out StationAiNpcCommanderComponent? bodyCommander))
        {
            if (brainExists && TryComp(ent.Comp.Brain, out StationAiNpcCommanderComponent? brainCommander))
            {
                CopyCommandState(bodyCommander, brainCommander);
                Dirty(ent.Comp.Brain, brainCommander);
            }

            bodyCommander.SelectedNpcs.Clear();
            bodyCommander.PendingMoveTargets.Clear();
            bodyCommander.MoveTargetPreviews.Clear();
            Dirty(ent.Owner, bodyCommander);
        }

        foreach (var action in ent.Comp.GrantedActions)
            _actions.RemoveAction(ent.Owner, action);

        ent.Comp.GrantedActions.Clear();
        if (hasVisitingMind && brainExists)
            _mind.UnVisit(visiting!.MindId!.Value);

        if (brainExists)
            _stationAi.ResumeVisionSubscriptions(ent.Comp.Brain);

        if (ent.Comp.RestoreGhostTakeover &&
            TryComp(ent.Owner, out MobStateComponent? state) &&
            _mobState.IsAlive(ent.Owner, state) &&
            (!TryComp(ent.Owner, out MindContainerComponent? mindContainer) || !mindContainer.HasMind))
        {
            EnsureComp<GhostTakeoverAvailableComponent>(ent.Owner);
        }

        RemCompDeferred<ZaxShuntedComponent>(ent.Owner);
        RemCompDeferred<StationAiNpcCommanderComponent>(ent.Owner);

        if (brainExists)
            _popup.PopupEntity(Loc.GetString("zax-shunt-returned"), ent.Comp.Brain, ent.Comp.Brain);
    }

    // [Changed by MisfitsCrew/Operator] Section: target validation and command-state transfer preserve mind/HTN ownership.
    private bool CanShuntInto(EntityUid brain, EntityUid target)
    {
        // [Changed by MisfitsCrew/Operator] Restrict direct control to living, mindless, locally visible Z.A.X NPCs.
        if (Deleted(target) ||
            HasComp<ActorComponent>(target) ||
            HasComp<ZaxShuntedComponent>(target) ||
            HasComp<VisitingMindComponent>(target) ||
            HasComp<MindContainerComponent>(target) && Comp<MindContainerComponent>(target).HasMind ||
            !HasComp<ZaxUnitComponent>(target) ||
            !TryComp(target, out HTNComponent? _) ||
            !TryComp(target, out MobStateComponent? state) ||
            !_mobState.IsAlive(target, state) ||
            !_commands.CanSee(brain, Transform(target).Coordinates) ||
            !_commands.CanTakeDirectControl(brain, target))
        {
            return false;
        }

        return true;
    }

    private static void CopyCommandState(
        StationAiNpcCommanderComponent source,
        StationAiNpcCommanderComponent destination,
        EntityUid? exclude = null)
    {
        destination.SelectedNpcs.Clear();
        foreach (var selected in source.SelectedNpcs)
        {
            if (selected != exclude && destination.SelectedNpcs.Count < destination.MaxSelected)
                destination.SelectedNpcs.Add(selected);
        }

        destination.PendingMoveTargets.Clear();
        destination.MoveTargetPreviews.Clear();
    }
}
