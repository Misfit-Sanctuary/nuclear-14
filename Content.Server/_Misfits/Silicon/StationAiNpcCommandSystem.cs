using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
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
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Player;

namespace Content.Server._Misfits.Silicon;

/// <summary>
/// [Changed by MisfitsCrew/Operator] Coordinates Station AI selection and order control for ZAX NPC units.
/// </summary>
public sealed class StationAiNpcCommandSystem : EntitySystem
{
    private const string MoveRoot = "StationAiOrderedMoveCompound";
    private const string EngageRoot = "StationAiOrderedEngageCompound";
    private const string HoldRoot = "StationAiOrderedHoldCompound";
    private const float MoveRange = 0.75f;
    private const float FormationSpacing = 1.25f;

    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly NPCSystem _npc = default!;
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;
    [Dependency] private readonly HTNSystem _htn = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedPowerReceiverSystem _power = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly StationAiVisionSystem _vision = default!;

    private EntityQuery<BroadphaseComponent> _broadphaseQuery;
    private EntityQuery<MapGridComponent> _gridQuery;

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
        SubscribeLocalEvent<StationAiNpcCommanderComponent, StationAiEngageSelectedNpcsActionEvent>(OnEngageSelected);
        SubscribeLocalEvent<StationAiNpcCommanderComponent, StationAiHoldSelectedNpcsActionEvent>(OnHoldSelected);
        SubscribeLocalEvent<StationAiNpcCommanderComponent, ComponentShutdown>(OnCommanderShutdown);
        SubscribeLocalEvent<ZaxUnitComponent, DamageChangedEvent>(OnZaxDamaged);
        SubscribeLocalEvent<StationAiCommandedNpcComponent, MobStateChangedEvent>(OnCommandedNpcMobStateChanged);
        SubscribeLocalEvent<StationAiCommandedNpcComponent, EntityTerminatingEvent>(OnCommandedNpcTerminating);
        SubscribeLocalEvent<StationAiCommandedNpcComponent, ComponentShutdown>(OnCommandedNpcShutdown);
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
        ApplyMove(ent, args.Target, formation: false);
    }

    private void OnFormationMoveSelected(Entity<StationAiNpcCommanderComponent> ent, ref StationAiFormationMoveSelectedNpcsActionEvent args)
    {
        if (args.Handled || !ValidateAi(ent.Owner) || !CanSee(ent.Owner, args.Target))
            return;

        args.Handled = true;
        ApplyMove(ent, args.Target, formation: true);
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
            args.Origin is not {} attacker ||
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

        // [Changed by MisfitsCrew/Operator] Lets ZAX units retaliate when attacked unless the Station AI has ordered hold.
        if (TryComp(ent.Owner, out StationAiCommandedNpcComponent? commanded) && commanded.HoldingCommand)
        {
            ClearForcedHostiles(ent.Owner, all: true);
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

    private void ApplyMove(Entity<StationAiNpcCommanderComponent> ent, EntityCoordinates target, bool formation)
    {
        var index = 0;
        var selected = GetValidSelectedNpcs(ent);
        var count = selected.Count;

        foreach (var (npc, htn) in selected)
        {
            var moveTarget = formation
                ? target.Offset(GetFormationOffset(index++, count))
                : target;

            // [Changed by MisfitsCrew/Operator] Applies either direct or formation offsets as HTN follow targets for selected ZAX units.
            PrepareOrder(npc, ent.Owner, htn, MoveRoot);
            ClearOrderBlackboard(htn);
            _npc.SetBlackboard(npc, NPCBlackboard.FollowTarget, moveTarget, htn);
            _npc.SetBlackboard(npc, "FollowCloseRange", MoveRange, htn);
            _npc.SetBlackboard(npc, "FollowRange", MoveRange, htn);
            Replan(npc, htn);
        }
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
        _npc.SleepNPC(uid, htn);
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

        ent.Comp.SelectedNpcs.Clear();
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
            commanded.ForcedHostile is not {} hostile)
        {
            return;
        }

        _npcFaction.DeAggroEntity((uid, factionException), hostile);
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

    private static Vector2 GetFormationOffset(int index, int count)
    {
        if (count <= 1)
            return Vector2.Zero;

        var width = (int) MathF.Ceiling(MathF.Sqrt(count));
        var row = index / width;
        var column = index % width;
        var usedInRow = Math.Min(width, count - row * width);
        var x = (column - (usedInRow - 1) / 2f) * FormationSpacing;
        var y = -row * FormationSpacing;
        return new Vector2(x, y);
    }
}
