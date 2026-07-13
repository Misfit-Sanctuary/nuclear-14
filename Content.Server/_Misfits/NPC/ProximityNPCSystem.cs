using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._Misfits.CCVar;
using Content.Shared._Misfits.NPC;
using Content.Shared.Movement.Components;
/// Misfit Change: sound handling moved to NPCSystem wake and sleep methods
// using Content.Shared.Audio;
// using Content.Shared.Sound;
// using Content.Shared.Sound.Components;
// using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Content.Shared.Mobs; // Misfits Add: To check MobState so crit NPCs dont move
using Content.Shared.Mobs.Components; // Misfits Add: To check MobState so crit NPCs dont move

namespace Content.Server._Misfits.NPC;

/// <summary>
/// Keeps NPCs with <see cref="ProximityNPCComponent"/> asleep until a player enters
/// their wake radius, then re-sleeps them when all players leave.
///
/// Why: Wendover is ~8000×4190 tiles. Running HTN planning on every creature at all
/// times saturates server CPU long before the player pop limit matters. By sleeping
/// distant NPCs we get near-zero per-tick cost for the majority of the map's fauna.
///
/// How it differs from RMC-14: RMC wakes xenonids when the dropship lands on-planet.
/// We instead perform a periodic spatial query against connected player positions,
/// which works for an always-on-grid (no vessel/space) game mode.
///
/// Performance: Uses a work-queue pattern — every <c>_checkInterval</c> seconds it
/// snapshots all proximity NPCs, then processes a small batch each tick until done.
/// This spreads the spatial query cost evenly across ticks and prevents a single
/// burst from blowing the tick budget and causing movement rubberbanding.
///
/// InputMover optimisation: Sleeping NPCs also have <see cref="InputMoverComponent"/>
/// removed so <c>SharedMoverController.UpdateBeforeSolve</c> skips them entirely.
/// At 3 physics substeps/tick this eliminates ~4500 HandleMobMovement calls/tick for
/// 1500 sleeping NPCs. The component is re-added before waking.
/// </summary>
public sealed partial class ProximityNPCSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly NPCSystem _npc = default!;
    /// Misfit Change: sound handling moved to <see cref="NPCSystem.SleepNPC"/> <see cref="NPCSystem.WakeNPC"/>
    // [Dependency] private readonly SharedAmbientSoundSystem _ambient = default!;
    // [Dependency] private readonly SharedEmitSoundSystem _emitSound = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private float _accumulator;
    private float _checkInterval;

    // Work-queue: snapshot of NPCs to check, processed across multiple ticks.
    private readonly List<EntityUid> _pending = new();
    private int _pendingIndex;
    private int _budgetPerTick;

    // Reused across calls to avoid allocating a new HashSet per NPC per scan.
    private readonly HashSet<Entity<ActorComponent>> _playerBuffer = new();

    private EntityQuery<TransformComponent> _xformQuery;
    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_config, PerformanceCVars.ProximityNPCCheckInterval, v => _checkInterval = v, true);

        // Subscribe AFTER HTNSystem so our sleep call overrides HTN's default WakeNPC on map init.
        SubscribeLocalEvent<ProximityNPCComponent, MapInitEvent>(OnMapInit,
            after: [typeof(HTNSystem)]);

        // Safety: if an admin ghost-possesses a sleeping NPC, ensure it can accept input.
        SubscribeLocalEvent<ProximityNPCComponent, PlayerAttachedEvent>(OnPlayerAttached);
        _xformQuery = GetEntityQuery<TransformComponent>();
    }

    private void OnMapInit(Entity<ProximityNPCComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.StartAsleep)
            _npc.SleepNPC(ent);
        /// Misfit Change: moved redundancy to <see cref="NPCSystem.SleepNPC"/>
    }

    /// <summary>
    /// If a player possesses an NPC that had its InputMoverComponent stripped while
    /// sleeping, re-add it so the player can actually move.
    /// </summary>
    private void OnPlayerAttached(Entity<ProximityNPCComponent> ent, ref PlayerAttachedEvent args)
    {
        EnsureComp<InputMoverComponent>(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // If pending work remains from a previous snapshot, keep processing.
        if (_pendingIndex < _pending.Count)
        {
            ProcessBatch();
            return;
        }

        // No pending work — wait for the next check interval.
        _accumulator += frameTime;
        if (_accumulator < _checkInterval)
            return;
        _accumulator -= _checkInterval;

        // Take a new snapshot of all proximity NPCs to spread across coming ticks.
        _pending.Clear();
        var query = EntityQueryEnumerator<ProximityNPCComponent>();
        while (query.MoveNext(out var uid, out _))
            _pending.Add(uid);

        if (_pending.Count == 0)
            return;

        _pendingIndex = 0;

        // Budget: spread evenly so all NPCs are checked within one interval.
        // e.g. 500 NPCs / (5s × 30 tick/s) = ~3.3 → 4 per tick.
        var ticksAvailable = _checkInterval * _timing.TickRate;
        _budgetPerTick = Math.Max(1, (int) Math.Ceiling(_pending.Count / ticksAvailable));

        ProcessBatch();
    }
    // TODO: I need to Rework this (maybe whole system) so it
    //       has other systems do these checks
    //       and is less likely to cause issues and unintended stuff
    //       for maintainability sake - John Keiser
    /// <summary>
    /// Processes up to <see cref="_budgetPerTick"/> NPCs from the pending queue.
    /// Each NPC gets a single spatial query to determine if any player is nearby.
    /// </summary>
    private void ProcessBatch()
    {
        var end = Math.Min(_pendingIndex + _budgetPerTick, _pending.Count);

        for (var i = _pendingIndex; i < end; i++)
        {
            var uid = _pending[i];
            // Misfit Change: TryComp(uid, out TransformComponent xform) -> XformQuery.TryGetComponent(uid, out var xform)
            // Changed due to code warning using generic trycomp to get transformComp
            if (!TryComp(uid, out ProximityNPCComponent? prox) ||
                !_xformQuery.TryGetComponent(uid, out var xform))
                continue;
            if (xform.MapID == MapId.Nullspace)
                continue;
            if (!TryComp(uid, out MobStateComponent? state)) continue;
            // #Misfits Fix — skip player-possessed mobs entirely. HTNSystem already
            // sleeps the AI on PlayerAttachedEvent; re-waking it here would re-enable
            // hostile NPC behaviour while a player/admin is in control.
            if (HasComp<ActorComponent>(uid))
                continue;


            // Misfit Change: Refactor for readability and added conditionals
            //                to prevent AI moving while crit
            //                players and player pets unaffected

            var mapPos = _transform.GetMapCoordinates(uid, xform);
            bool inRange = HasPlayerWithin(mapPos, prox.WakeRange);
            bool awake = _npc.IsAwake(uid);

            if (awake && !inRange && !HasComp<RecruitedFollowerComponent>(uid))
            {
                _npc.SleepNPC(uid);
            }
            /// NPCs unable to move while crit even if <see cref="MobStateComponent"/> says otherwise
            else if (state.CurrentState == MobState.Alive && !awake && inRange)
            {
                _npc.WakeNPC(uid);
            }
        }

        _pendingIndex = end;
    }

    /// <summary>
    /// Returns true if at least one player-controlled entity is within <paramref name="range"/>
    /// tiles of <paramref name="pos"/> on the same map.
    /// </summary>
    private bool HasPlayerWithin(MapCoordinates pos, float range)
    {
        // ActorComponent is the marker for a session-controlled entity.
        // Use the overload that populates a reusable HashSet to avoid per-call heap allocation.
        _playerBuffer.Clear();
        _lookup.GetEntitiesInRange(pos, range, _playerBuffer);
        return _playerBuffer.Count > 0;
    }
}
