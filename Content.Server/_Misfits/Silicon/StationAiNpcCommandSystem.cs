using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Server.Ghost.Roles.Components;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Server.Silicons.StationAi;
using Content.Shared._Misfits.C27;
using Content.Shared._Misfits.Silicon;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Silicons.StationAi;
using Content.Shared.StationAi;
using Content.Shared.UserInterface;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Player;
using Robust.Server.GameObjects;

namespace Content.Server._Misfits.Silicon;

/// <summary>
/// [Changed by MisfitsCrew/Operator] Coordinates Station AI selection and order control for ZAX NPC units.
/// </summary>
public sealed class StationAiNpcCommandSystem : EntitySystem
{
    private const string MoveRoot = "StationAiOrderedMoveCompound";
    private const string MoveAndAttackRoot = "StationAiOrderedMoveAndAttackCompound";
    private const string EngageRoot = "StationAiOrderedEngageCompound";
    private const string HoldRoot = "StationAiOrderedHoldCompound";
    private const float MoveRange = 0.75f;
    private const float MoveRangeSquared = MoveRange * MoveRange;

    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly NPCSystem _npc = default!;
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;
    [Dependency] private readonly HTNSystem _htn = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedPowerReceiverSystem _power = default!;
    [Dependency] private readonly StationAiSystem _stationAi = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly StationAiVisionSystem _vision = default!;

    private EntityQuery<BroadphaseComponent> _broadphaseQuery;
    private EntityQuery<MapGridComponent> _gridQuery;
    private readonly Dictionary<EntityUid, List<TrackedMoveTarget>> _activeMoveOrders = new();

    public override void Initialize()
    {
        base.Initialize();

        _broadphaseQuery = GetEntityQuery<BroadphaseComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();

        // [Changed by MisfitsCrew/Operator] Registers Station AI command actions and lifecycle cleanup for commanded ZAX units.
        SubscribeLocalEvent<StationAiNpcCommanderComponent, StationAiSelectNpcActionEvent>(OnSelectNpc);
        SubscribeLocalEvent<StationAiNpcCommanderComponent, StationAiClearNpcSelectionActionEvent>(OnClearSelection);
        SubscribeLocalEvent<StationAiNpcCommanderComponent, StationAiMoveSelectedNpcsActionEvent>(OnMoveSelected);
        SubscribeLocalEvent<StationAiNpcCommanderComponent, StationAiFormationMoveSelectedNpcsActionEvent>(OnFormationMoveSelected);
        SubscribeLocalEvent<StationAiNpcCommanderComponent, StationAiMoveAndAttackSelectedNpcsActionEvent>(OnMoveAndAttackSelected);
        SubscribeLocalEvent<StationAiNpcCommanderComponent, StationAiEngageSelectedNpcsActionEvent>(OnEngageSelected);
        SubscribeLocalEvent<StationAiNpcCommanderComponent, StationAiHoldSelectedNpcsActionEvent>(OnHoldSelected);
        SubscribeLocalEvent<StationAiNpcCommanderComponent, ComponentShutdown>(OnCommanderShutdown);
        SubscribeLocalEvent<ZaxUnitComponent, DamageChangedEvent>(OnZaxDamaged);
        SubscribeLocalEvent<StationAiCommandedNpcComponent, MobStateChangedEvent>(OnCommandedNpcMobStateChanged);
        SubscribeLocalEvent<StationAiCommandedNpcComponent, EntityTerminatingEvent>(OnCommandedNpcTerminating);
        SubscribeLocalEvent<StationAiCommandedNpcComponent, ComponentShutdown>(OnCommandedNpcShutdown);

        Subs.BuiEvents<StationAiNpcCommanderComponent>(ZaxLinkedUnitsUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnLinkedUnitsOpened);
            subs.Event<ZaxLinkedUnitsRefreshMessage>(OnLinkedUnitsRefresh);
            subs.Event<ZaxLinkedUnitsWarpMessage>(OnLinkedUnitsWarp);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_activeMoveOrders.Count == 0)
            return;

        foreach (var (commanderUid, targets) in _activeMoveOrders.ToArray())
        {
            if (!TryComp(commanderUid, out StationAiNpcCommanderComponent? commander))
            {
                _activeMoveOrders.Remove(commanderUid);
                continue;
            }

            if (targets.Count > 0 && !IsMoveOrderComplete(targets))
                continue;

            ClearMoveTargetState((commanderUid, commander));
        }
    }

    private void OnSelectNpc(Entity<StationAiNpcCommanderComponent> ent, ref StationAiSelectNpcActionEvent args)
    {
        if (args.Handled || !ValidateAi(ent.Owner) || !CanSee(ent.Owner, Transform(args.Target).Coordinates))
            return;

        args.Handled = true;

        PruneSelection(ent);

        if (ent.Comp.SelectedNpcs.Remove(args.Target))
        {
            RestoreNpc(args.Target, ent.Owner);
            ClearMoveTargetState(ent);
            Dirty(ent);
            return;
        }

        if (ent.Comp.SelectedNpcs.Count >= ent.Comp.MaxSelected)
        {
            _popup.PopupEntity(Loc.GetString("station-ai-npc-command-selection-full"), ent.Owner, ent.Owner, PopupType.SmallCaution);
            return;
        }

        if (!TryGetCommandableNpc(args.Target, ent.Owner, out var htn))
            return;

        EnsureCommandedNpc(args.Target, ent.Owner, htn);
        ent.Comp.SelectedNpcs.Add(args.Target);
        ClearMoveTargetState(ent);
        Dirty(ent);
    }

    private void OnClearSelection(Entity<StationAiNpcCommanderComponent> ent, ref StationAiClearNpcSelectionActionEvent args)
    {
        if (args.Handled || !ValidateAi(ent.Owner))
            return;

        args.Handled = true;
        ClearSelection(ent);
    }

    private void OnMoveSelected(Entity<StationAiNpcCommanderComponent> ent, ref StationAiMoveSelectedNpcsActionEvent args)
    {
        if (args.Handled || !ValidateAi(ent.Owner) || !CanSee(ent.Owner, args.Target))
            return;

        args.Handled = true;

        var selected = GetValidSelectedNpcs(ent);
        if (selected.Count == 0)
        {
            ClearMoveTargetState(ent);
            SendMoveTargetingFinished(ent.Owner);
            return;
        }

        if (ent.Comp.PendingMoveTargets.Count >= selected.Count)
            ent.Comp.PendingMoveTargets.Clear();

        _activeMoveOrders.Remove(ent.Owner);
        ent.Comp.PendingMoveTargets.Add(GetNetCoordinates(args.Target));
        ent.Comp.MoveTargetPreviews.Clear();
        ent.Comp.MoveTargetPreviews.AddRange(ent.Comp.PendingMoveTargets);
        Dirty(ent);

        if (ent.Comp.PendingMoveTargets.Count < selected.Count)
            return;

        var targets = ent.Comp.PendingMoveTargets
            .Take(selected.Count)
            .Select(EntityManager.GetCoordinates)
            .ToList();

        ApplyMoveTargets(ent.Owner, selected, AssignMoveTargetsByNearest(selected, targets), MoveRoot);
        ent.Comp.PendingMoveTargets.Clear();
        Dirty(ent);
        SendMoveTargetingFinished(ent.Owner);
    }

    private void OnFormationMoveSelected(Entity<StationAiNpcCommanderComponent> ent, ref StationAiFormationMoveSelectedNpcsActionEvent args)
    {
        if (args.Handled || !ValidateAi(ent.Owner) || !CanSee(ent.Owner, args.Target))
            return;

        args.Handled = true;
        var selected = GetValidSelectedNpcs(ent);
        var targets = GetFormationMoveTargets(selected, args.Target);

        SetMoveTargetPreviews(ent, targets);
        ApplyMoveTargets(ent.Owner, selected, targets, MoveRoot);
    }

    private void OnMoveAndAttackSelected(Entity<StationAiNpcCommanderComponent> ent, ref StationAiMoveAndAttackSelectedNpcsActionEvent args)
    {
        if (args.Handled || !ValidateAi(ent.Owner) || !CanSee(ent.Owner, args.Target))
            return;

        args.Handled = true;
        ClearMoveTargetState(ent);
        ApplyMove(ent, args.Target, formation: false, MoveAndAttackRoot);
    }

    private void OnEngageSelected(Entity<StationAiNpcCommanderComponent> ent, ref StationAiEngageSelectedNpcsActionEvent args)
    {
        if (args.Handled ||
            !ValidateAi(ent.Owner) ||
            !CanSee(ent.Owner, Transform(args.Target).Coordinates) ||
            !HasComp<MobStateComponent>(args.Target) ||
            !_mobState.IsAlive(args.Target))
        {
            return;
        }

        args.Handled = true;
        ClearMoveTargetState(ent);
        foreach (var (npc, htn) in GetValidSelectedNpcs(ent))
        {
            PrepareOrder(npc, ent.Owner, htn, EngageRoot);
            ClearOrderBlackboard(htn);
            _npc.SetBlackboard(npc, NPCBlackboard.CurrentOrderedTarget, args.Target, htn);
            _npcFaction.AggroEntity(npc, args.Target);
            Comp<StationAiCommandedNpcComponent>(npc).ForcedHostile = args.Target;
            Replan(npc, htn);
        }
    }

    private void OnHoldSelected(Entity<StationAiNpcCommanderComponent> ent, ref StationAiHoldSelectedNpcsActionEvent args)
    {
        if (args.Handled || !ValidateAi(ent.Owner))
            return;

        args.Handled = true;
        ClearMoveTargetState(ent);
        foreach (var (npc, htn) in GetValidSelectedNpcs(ent))
        {
            PrepareOrder(npc, ent.Owner, htn, HoldRoot);
            ClearOrderBlackboard(htn);
            ClearForcedHostiles(npc, all: true);
            Replan(npc, htn);
        }
    }

    private void OnZaxDamaged(Entity<ZaxUnitComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased ||
            args.Origin is not { } attacker ||
            attacker == ent.Owner ||
            Deleted(attacker) ||
            !HasComp<MobStateComponent>(attacker) ||
            !_mobState.IsAlive(attacker) ||
            !TryComp(ent.Owner, out HTNComponent? htn) ||
            !TryComp(ent.Owner, out MobStateComponent? mobState) ||
            !_mobState.IsAlive(ent.Owner, mobState))
        {
            return;
        }

        if (IsZaxFriendlyFire(ent.Owner, attacker))
        {
            ClearMutualZaxAggro(ent.Owner, attacker);
            return;
        }

        // [Changed by MisfitsCrew/Operator] Hold order is absolute: ZAX units do not retaliate while holding.
        if (TryComp(ent.Owner, out StationAiCommandedNpcComponent? commanded) && commanded.HoldingCommand)
        {
            ClearForcedHostiles(ent.Owner, all: true);
            return;
        }

        if (!CanZaxRetaliateAgainst(ent.Owner, attacker))
        {
            ClearSpecificHostile(ent.Owner, attacker);
            return;
        }

        if (htn.Plan != null)
            _htn.ShutdownPlan(htn);

        ClearOrderBlackboard(htn);
        ClearForcedHostiles(ent.Owner);
        htn.RootTask.Task = EngageRoot;
        _npc.SetBlackboard(ent.Owner, NPCBlackboard.CurrentOrderedTarget, attacker, htn);
        _npcFaction.AggroEntity(ent.Owner, attacker);

        if (commanded != null)
            commanded.ForcedHostile = attacker;

        Replan(ent.Owner, htn);
    }

    private void OnCommanderShutdown(Entity<StationAiNpcCommanderComponent> ent, ref ComponentShutdown args)
    {
        ClearSelection(ent);
    }

    private void OnCommandedNpcMobStateChanged(Entity<StationAiCommandedNpcComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        // [Changed by MisfitsCrew/Operator] Drops dead units from selections so later AI orders can target surviving ZAX units.
        ReleaseDeadOrDeletedNpc(ent.Owner);
        RemCompDeferred<StationAiCommandedNpcComponent>(ent.Owner);
    }

    private void OnCommandedNpcTerminating(Entity<StationAiCommandedNpcComponent> ent, ref EntityTerminatingEvent args)
    {
        ReleaseDeadOrDeletedNpc(ent.Owner);
    }

    private void OnCommandedNpcShutdown(Entity<StationAiCommandedNpcComponent> ent, ref ComponentShutdown args)
    {
        ReleaseDeadOrDeletedNpc(ent.Owner);
    }

    private void OnLinkedUnitsOpened(Entity<StationAiNpcCommanderComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!ValidateAi(ent.Owner))
            return;

        UpdateLinkedUnitsUi(ent.Owner);
    }

    private void OnLinkedUnitsRefresh(Entity<StationAiNpcCommanderComponent> ent, ref ZaxLinkedUnitsRefreshMessage args)
    {
        if (args.Actor != ent.Owner || !ValidateAi(ent.Owner))
            return;

        UpdateLinkedUnitsUi(ent.Owner);
    }

    private void OnLinkedUnitsWarp(Entity<StationAiNpcCommanderComponent> ent, ref ZaxLinkedUnitsWarpMessage args)
    {
        if (args.Actor != ent.Owner ||
            !ValidateAi(ent.Owner) ||
            !TryGetEntity(args.Target, out var target) ||
            !TryGetLinkedUnit(target.Value, out _))
        {
            return;
        }

        if (!TryComp(ent.Owner, out StationAiHeldComponent? held) ||
            !TryGetCore((ent.Owner, held), out var core))
        {
            _popup.PopupEntity(Loc.GetString("zax-linked-units-warp-failed"), ent.Owner, ent.Owner, PopupType.SmallCaution);
            return;
        }

        _stationAi.SwitchRemoteEntityMode(core.Value, true);

        if (core.Value.Comp.RemoteEntity == null)
        {
            _popup.PopupEntity(Loc.GetString("zax-linked-units-warp-failed"), ent.Owner, ent.Owner, PopupType.SmallCaution);
            return;
        }

        _transform.SetCoordinates(core.Value.Comp.RemoteEntity.Value, Transform(target.Value).Coordinates);
        _popup.PopupEntity(Loc.GetString("zax-linked-units-warped", ("unit", Name(target.Value))), ent.Owner, ent.Owner);
        UpdateLinkedUnitsUi(ent.Owner);
    }

    private void ApplyMove(
        Entity<StationAiNpcCommanderComponent> ent,
        EntityCoordinates target,
        bool formation,
        string rootTask = MoveRoot)
    {
        var selected = GetValidSelectedNpcs(ent);
        var targets = formation
            ? GetFormationMoveTargets(selected, target)
            : Enumerable.Repeat(target, selected.Count).ToList();

        ApplyMoveTargets(ent.Owner, selected, targets, rootTask);
    }

    private void ApplyMoveTargets(
        EntityUid commander,
        List<(EntityUid Uid, HTNComponent Htn)> selected,
        List<EntityCoordinates> targets,
        string rootTask)
    {
        var activeMoveTargets = rootTask == MoveRoot
            ? new List<TrackedMoveTarget>()
            : null;

        for (var i = 0; i < selected.Count && i < targets.Count; i++)
        {
            var (npc, htn) = selected[i];
            var moveTarget = targets[i];

            // [Changed by MisfitsCrew/Operator] Applies direct or preserved formation offsets as HTN follow targets for selected ZAX units.
            PrepareOrder(npc, commander, htn, rootTask);
            ClearOrderBlackboard(htn);
            ClearForcedHostiles(npc, all: true);
            _npc.SetBlackboard(npc, NPCBlackboard.FollowTarget, moveTarget, htn);
            _npc.SetBlackboard(npc, "FollowCloseRange", MoveRange, htn);
            _npc.SetBlackboard(npc, "FollowRange", MoveRange, htn);
            Replan(npc, htn);
            activeMoveTargets?.Add(new TrackedMoveTarget(npc, GetNetCoordinates(moveTarget)));
        }

        TrackActiveMoveOrder(commander, activeMoveTargets);
    }

    private List<EntityCoordinates> AssignMoveTargetsByNearest(
        List<(EntityUid Uid, HTNComponent Htn)> selected,
        List<EntityCoordinates> targets)
    {
        if (targets.Count == 0)
            return new List<EntityCoordinates>();

        var remaining = selected.Select((entry, index) => index).ToList();
        var assigned = new EntityCoordinates[selected.Count];

        foreach (var target in targets)
        {
            if (remaining.Count == 0)
                break;

            var selectedIndex = FindNearestSelectedIndex(remaining, selected, target);
            assigned[selectedIndex] = target;
            remaining.Remove(selectedIndex);
        }

        foreach (var index in remaining)
            assigned[index] = targets[Math.Min(index, targets.Count - 1)];

        return assigned.ToList();
    }

    private int FindNearestSelectedIndex(
        List<int> remaining,
        List<(EntityUid Uid, HTNComponent Htn)> selected,
        EntityCoordinates target)
    {
        var targetMap = target.ToMap(EntityManager, _transform);
        var bestIndex = remaining[0];
        var bestDistance = float.MaxValue;

        foreach (var index in remaining)
        {
            var npcMap = _transform.GetMapCoordinates(selected[index].Uid);
            var distance = npcMap.MapId == targetMap.MapId
                ? Vector2.DistanceSquared(npcMap.Position, targetMap.Position)
                : float.MaxValue;

            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestIndex = index;
        }

        return bestIndex;
    }

    private void SetMoveTargetPreviews(Entity<StationAiNpcCommanderComponent> ent, List<EntityCoordinates> targets)
    {
        ent.Comp.PendingMoveTargets.Clear();
        ent.Comp.MoveTargetPreviews.Clear();

        foreach (var target in targets)
            ent.Comp.MoveTargetPreviews.Add(GetNetCoordinates(target));

        Dirty(ent);
    }

    private void ClearMoveTargetState(Entity<StationAiNpcCommanderComponent> ent)
    {
        _activeMoveOrders.Remove(ent.Owner);

        if (ent.Comp.PendingMoveTargets.Count == 0 && ent.Comp.MoveTargetPreviews.Count == 0)
            return;

        ent.Comp.PendingMoveTargets.Clear();
        ent.Comp.MoveTargetPreviews.Clear();
        Dirty(ent);
    }

    private void TrackActiveMoveOrder(EntityUid commander, List<TrackedMoveTarget>? targets)
    {
        if (targets == null)
            return;

        if (targets.Count == 0)
        {
            if (TryComp(commander, out StationAiNpcCommanderComponent? commanderComp))
                ClearMoveTargetState((commander, commanderComp));
            else
                _activeMoveOrders.Remove(commander);

            return;
        }

        _activeMoveOrders[commander] = targets;
    }

    private bool IsMoveOrderComplete(List<TrackedMoveTarget> targets)
    {
        foreach (var target in targets)
        {
            if (!IsMoveTargetComplete(target))
                return false;
        }

        return true;
    }

    private bool IsMoveTargetComplete(TrackedMoveTarget target)
    {
        if (Deleted(target.Npc) ||
            !TryComp(target.Npc, out MobStateComponent? mobState) ||
            !_mobState.IsAlive(target.Npc, mobState))
        {
            return true;
        }

        var npcMap = _transform.GetMapCoordinates(target.Npc);
        var targetMap = EntityManager.GetCoordinates(target.Target).ToMap(EntityManager, _transform);
        return npcMap.MapId == targetMap.MapId &&
            Vector2.DistanceSquared(npcMap.Position, targetMap.Position) <= MoveRangeSquared;
    }

    private void SendMoveTargetingFinished(EntityUid commander)
    {
        if (TryComp<ActorComponent>(commander, out var actor))
            RaiseNetworkEvent(new StationAiNpcMoveTargetingFinishedEvent(), actor.PlayerSession);
    }

    private List<(EntityUid Uid, HTNComponent Htn)> GetValidSelectedNpcs(Entity<StationAiNpcCommanderComponent> ent)
    {
        var selected = new List<(EntityUid, HTNComponent)>();
        var stale = PruneSelection(ent);

        foreach (var uid in ent.Comp.SelectedNpcs)
        {
            if (!TryGetCommandableNpc(uid, ent.Owner, out var htn))
                continue;

            selected.Add((uid, htn));
        }

        if (stale)
            Dirty(ent);

        return selected;
    }

    private bool PruneSelection(Entity<StationAiNpcCommanderComponent> ent)
    {
        var stale = new List<EntityUid>();

        foreach (var uid in ent.Comp.SelectedNpcs)
        {
            if (!TryGetCommandableNpc(uid, ent.Owner, out _))
            {
                stale.Add(uid);
                continue;
            }
        }

        foreach (var uid in stale)
            ent.Comp.SelectedNpcs.Remove(uid);

        return stale.Count > 0;
    }

    private bool TryGetCommandableNpc(EntityUid uid, EntityUid commander, [NotNullWhen(true)] out HTNComponent? htn)
    {
        htn = null;

        if (Deleted(uid) ||
            HasComp<ActorComponent>(uid) ||
            !HasComp<ZaxUnitComponent>(uid) ||
            !TryComp(uid, out htn) ||
            !TryComp(uid, out MobStateComponent? mobState) ||
            !_mobState.IsAlive(uid, mobState))
        {
            return false;
        }

        return !TryComp(uid, out StationAiCommandedNpcComponent? commanded) || IsSameCommander(commander, commanded);
    }

    private List<EntityCoordinates> GetFormationMoveTargets(
        List<(EntityUid Uid, HTNComponent Htn)> selected,
        EntityCoordinates target)
    {
        var targets = new List<EntityCoordinates>(selected.Count);
        var offsets = GetCurrentFormationOffsets(selected, target);

        for (var i = 0; i < selected.Count && i < offsets.Count; i++)
            targets.Add(OffsetTargetInMap(target, offsets[i]));

        return targets;
    }

    private List<Vector2> GetCurrentFormationOffsets(
        List<(EntityUid Uid, HTNComponent Htn)> selected,
        EntityCoordinates target)
    {
        var offsets = new List<Vector2>(selected.Count);
        if (selected.Count <= 1)
        {
            if (selected.Count == 1)
                offsets.Add(Vector2.Zero);

            return offsets;
        }

        var targetMap = target.ToMap(EntityManager, _transform);
        var positions = new List<Vector2>(selected.Count);
        var center = Vector2.Zero;

        foreach (var (uid, _) in selected)
        {
            var mapCoordinates = _transform.GetMapCoordinates(uid);
            if (mapCoordinates.MapId != targetMap.MapId)
            {
                positions.Add(targetMap.Position);
                center += targetMap.Position;
                continue;
            }

            positions.Add(mapCoordinates.Position);
            center += mapCoordinates.Position;
        }

        center /= selected.Count;

        foreach (var position in positions)
            offsets.Add(position - center);

        return offsets;
    }

    private EntityCoordinates OffsetTargetInMap(EntityCoordinates target, Vector2 offset)
    {
        var mapTarget = target.ToMap(EntityManager, _transform);
        return EntityCoordinates.FromMap(_mapManager, new MapCoordinates(mapTarget.Position + offset, mapTarget.MapId));
    }

    private bool ValidateAi(EntityUid uid)
    {
        if (!TryComp(uid, out StationAiHeldComponent? held) ||
            !TryGetCore((uid, held), out var core))
        {
            return false;
        }

        SharedApcPowerReceiverComponent? receiver = null;
        return _power.IsPowered((core.Value.Owner, receiver));
    }

    private void UpdateLinkedUnitsUi(EntityUid commander)
    {
        _ui.SetUiState(commander, ZaxLinkedUnitsUiKey.Key, BuildLinkedUnitsState());
    }

    private ZaxLinkedUnitsBoundUserInterfaceState BuildLinkedUnitsState()
    {
        var units = new List<ZaxLinkedUnitEntry>();
        var query = EntityQueryEnumerator<ZaxLinkedUnitComponent, TransformComponent, MetaDataComponent>();

        while (query.MoveNext(out var uid, out _, out var xform, out var meta))
        {
            if (!TryGetLinkedUnit(uid, out var kind))
                continue;

            var mapCoords = _transform.GetMapCoordinates(uid, xform);
            var location = $"{MathF.Round(mapCoords.Position.X)}, {MathF.Round(mapCoords.Position.Y)}";
            units.Add(new ZaxLinkedUnitEntry(
                GetNetEntity(uid),
                meta.EntityName,
                kind,
                location,
                GetNetCoordinates(xform.Coordinates)));
        }

        return new ZaxLinkedUnitsBoundUserInterfaceState(units
            .OrderBy(unit => unit.Kind)
            .ThenBy(unit => unit.Name)
            .ToArray());
    }

    private bool TryGetLinkedUnit(EntityUid uid, out ZaxLinkedUnitKind kind)
    {
        kind = ZaxLinkedUnitKind.Npc;

        if (Deleted(uid) ||
            !HasComp<ZaxLinkedUnitComponent>(uid) ||
            (TryComp(uid, out MobStateComponent? mobState) && _mobState.IsDead(uid, mobState)))
        {
            return false;
        }

        if (HasComp<ActorComponent>(uid))
        {
            kind = ZaxLinkedUnitKind.Player;
            return true;
        }

        if (HasComp<ZaxUnitComponent>(uid))
        {
            kind = ZaxLinkedUnitKind.Npc;
            return true;
        }

        if (HasComp<GhostRoleComponent>(uid) || HasComp<GhostTakeoverAvailableComponent>(uid))
        {
            kind = ZaxLinkedUnitKind.GhostRole;
            return true;
        }

        kind = ZaxLinkedUnitKind.Npc;
        return true;
    }

    private bool TryGetCore(
        Entity<StationAiHeldComponent> ai,
        [NotNullWhen(true)] out Entity<StationAiCoreComponent>? core)
    {
        core = null;

        if (!_container.TryGetContainingContainer((ai.Owner, null, null), out var container) ||
            container.ID != StationAiCoreComponent.Container ||
            !TryComp(container.Owner, out StationAiCoreComponent? coreComp))
        {
            return false;
        }

        core = (container.Owner, coreComp);
        return true;
    }

    private bool CanSee(EntityUid ai, EntityCoordinates coordinates)
    {
        if (!TryGetCore((ai, Comp<StationAiHeldComponent>(ai)), out var core))
            return false;

        var targetMap = coordinates.ToMap(EntityManager, _transform);
        if (!TryGetGrid(coordinates, targetMap, out var gridUid, out var grid) ||
            !_broadphaseQuery.TryComp(gridUid, out var broadphase))
        {
            return false;
        }

        if (Transform(core.Value.Owner).GridUid != gridUid)
            return false;

        var targetTile = _map.LocalToTile(gridUid, grid, coordinates);
        lock (_vision)
        {
            return _vision.IsAccessible((gridUid, broadphase, grid), targetTile);
        }
    }

    private bool TryGetGrid(
        EntityCoordinates coordinates,
        MapCoordinates mapCoordinates,
        out EntityUid gridUid,
        [NotNullWhen(true)] out MapGridComponent? grid)
    {
        gridUid = EntityUid.Invalid;
        grid = null;

        if (_gridQuery.TryComp(coordinates.EntityId, out grid))
        {
            gridUid = coordinates.EntityId;
            return true;
        }

        var resolvedGrid = _transform.GetGrid(coordinates);
        if (resolvedGrid != null && _gridQuery.TryComp(resolvedGrid.Value, out grid))
        {
            gridUid = resolvedGrid.Value;
            return true;
        }

        return _mapManager.TryFindGridAt(mapCoordinates, out gridUid, out grid);
    }

    private void PrepareOrder(EntityUid uid, EntityUid commander, HTNComponent htn, string rootTask)
    {
        // [Changed by MisfitsCrew/Operator] Mirrors the existing NPC follower lifecycle so HTN orders restart movement reliably.
        var commanded = EnsureCommandedNpc(uid, commander, htn);
        commanded.HoldingCommand = rootTask == HoldRoot;
        _npc.SleepNPC(uid, htn, removeSound: false);
        htn.RootTask.Task = rootTask;
    }

    private void RestoreNpc(EntityUid uid, EntityUid commander)
    {
        if (!TryComp(uid, out StationAiCommandedNpcComponent? commanded) || !IsSameCommander(commander, commanded))
            return;

        if (TryComp(uid, out HTNComponent? htn))
        {
            if (htn.Plan != null)
                _htn.ShutdownPlan(htn);

            ClearOrderBlackboard(htn);
            ClearForcedHostiles(uid, all: true);

            if (!string.IsNullOrEmpty(commanded.OriginalRootTask))
                htn.RootTask.Task = commanded.OriginalRootTask;

            Replan(uid, htn);
        }

        RemComp<StationAiCommandedNpcComponent>(uid);
    }

    private void ClearSelection(Entity<StationAiNpcCommanderComponent> ent)
    {
        foreach (var uid in new List<EntityUid>(ent.Comp.SelectedNpcs))
            RestoreNpc(uid, ent.Owner);

        _activeMoveOrders.Remove(ent.Owner);
        ent.Comp.SelectedNpcs.Clear();
        ent.Comp.PendingMoveTargets.Clear();
        ent.Comp.MoveTargetPreviews.Clear();
        Dirty(ent);
    }

    private void ClearOrderBlackboard(HTNComponent htn)
    {
        htn.Blackboard.Remove<EntityCoordinates>(NPCBlackboard.FollowTarget);
        htn.Blackboard.Remove<EntityCoordinates>(NPCBlackboard.MovementTarget);
        htn.Blackboard.Remove<EntityCoordinates>(NPCBlackboard.OwnerCoordinates);
        htn.Blackboard.Remove<EntityUid>(NPCBlackboard.CurrentOrderedTarget);
        htn.Blackboard.Remove<EntityUid>("Target");
        htn.Blackboard.Remove<EntityCoordinates>("TargetCoordinates");
        htn.Blackboard.Remove<float>("IdleTime");
        htn.Blackboard.Remove<EntityCoordinates>("FollowIdleTarget");
        htn.Blackboard.Remove<PathResultEvent>("TargetPathfind");
        htn.Blackboard.Remove<PathResultEvent>(NPCBlackboard.PathfindKey);
    }

    private StationAiCommandedNpcComponent EnsureCommandedNpc(EntityUid uid, EntityUid commander, HTNComponent htn)
    {
        // [Changed by MisfitsCrew/Operator] Stores both brain and core ownership to survive Station AI relay identity changes.
        var commanded = EnsureComp<StationAiCommandedNpcComponent>(uid);
        commanded.Commander = commander;
        commanded.CommanderCore = TryGetCommandingCoreUid(commander, out var coreUid)
            ? coreUid
            : EntityUid.Invalid;

        if (string.IsNullOrEmpty(commanded.OriginalRootTask))
            commanded.OriginalRootTask = htn.RootTask.Task;

        return commanded;
    }

    private bool IsSameCommander(EntityUid commander, StationAiCommandedNpcComponent commanded)
    {
        if (commanded.Commander == commander)
            return true;

        return commanded.CommanderCore.IsValid() &&
            TryGetCommandingCoreUid(commander, out var coreUid) &&
            commanded.CommanderCore == coreUid;
    }

    private bool TryGetCommandingCoreUid(EntityUid commander, out EntityUid coreUid)
    {
        coreUid = EntityUid.Invalid;

        if (!TryComp(commander, out StationAiHeldComponent? held) ||
            !TryGetCore((commander, held), out var core))
        {
            return false;
        }

        coreUid = core.Value.Owner;
        return true;
    }

    private void ClearForcedHostiles(EntityUid uid, bool all = false)
    {
        if (!TryComp(uid, out FactionExceptionComponent? factionException))
        {
            return;
        }

        if (all)
        {
            foreach (var currentHostile in new List<EntityUid>(factionException.Hostiles))
                _npcFaction.DeAggroEntity((uid, factionException), currentHostile);

            if (TryComp(uid, out StationAiCommandedNpcComponent? allCommanded))
                allCommanded.ForcedHostile = null;

            return;
        }

        if (!TryComp(uid, out StationAiCommandedNpcComponent? commanded) ||
            commanded.ForcedHostile is not { } hostile)
        {
            return;
        }

        _npcFaction.DeAggroEntity((uid, factionException), hostile);
        commanded.ForcedHostile = null;
    }

    private bool IsZaxFriendlyFire(EntityUid uid, EntityUid attacker)
    {
        return HasComp<ZaxUnitComponent>(uid) && HasComp<ZaxUnitComponent>(attacker);
    }

    private bool CanZaxRetaliateAgainst(EntityUid uid, EntityUid attacker)
    {
        if (HasComp<ActorComponent>(attacker))
            return true;

        if (HasComp<MisfitsC27Component>(attacker) ||
            !HasComp<NpcFactionMemberComponent>(attacker))
        {
            return false;
        }

        return _npcFaction.IsEntityHostile(uid, attacker);
    }

    private void ClearMutualZaxAggro(EntityUid uid, EntityUid other)
    {
        ClearSpecificHostile(uid, other);
        ClearSpecificHostile(other, uid);
    }

    private void ClearSpecificHostile(EntityUid uid, EntityUid target)
    {
        if (!TryComp(uid, out FactionExceptionComponent? factionException))
            return;

        _npcFaction.DeAggroEntity((uid, factionException), target);

        if (TryComp(uid, out StationAiCommandedNpcComponent? commanded) && commanded.ForcedHostile == target)
            commanded.ForcedHostile = null;
    }

    private void ReleaseDeadOrDeletedNpc(EntityUid npc)
    {
        // [Changed by MisfitsCrew/Operator] Removes stale NPC references from every AI commander selection during death/deletion cleanup.
        var query = EntityQueryEnumerator<StationAiNpcCommanderComponent>();
        while (query.MoveNext(out var commanderUid, out var commander))
        {
            if (!commander.SelectedNpcs.Remove(npc))
                continue;

            Dirty(commanderUid, commander);
        }

        ClearForcedHostiles(npc, all: true);
    }

    private void Replan(EntityUid uid, HTNComponent htn)
    {
        _htn.Replan(htn);
        EnsureComp<InputMoverComponent>(uid);
        _npc.WakeNPC(uid, htn);
    }

    private readonly record struct TrackedMoveTarget(EntityUid Npc, NetCoordinates Target);
}
