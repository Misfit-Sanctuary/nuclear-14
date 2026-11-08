using Content.Server.Administration.Logs;
using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Shared._Misfits.Fluids;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Effects;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Fluids.Components;
using Content.Shared.Friction;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.StepTrigger.Systems;
using Robust.Server.Audio;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;


namespace Content.Server._Misfits.Fluids;


public sealed partial class PuddleSpawnerSystem: EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionSys = default!;
    [Dependency] private SharedPuddleSystem _puddleSys = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PuddleReagentTrackerComponent,SolutionContainerChangedEvent>(OnSolutionChange);
        SubscribeLocalEvent<PuddleSpawnerComponent,UpdateSolutionSpawnerEvent>(OnSolutionUpdate);
    }

    private void OnSolutionChange(Entity<PuddleReagentTrackerComponent> ent, ref SolutionContainerChangedEvent ev)
    {
        var amount = ev.Solution.GetTotalPrototypeQuantity(ent.Comp.ReagentId);
        var relayEvent= new UpdateSolutionSpawnerEvent(ent, amount);
        RaiseLocalEvent(ent.Comp.Manager, ref relayEvent);
    }
    public void OnSolutionUpdate(Entity<PuddleSpawnerComponent> ent, ref UpdateSolutionSpawnerEvent ev)
    {
        if (ev.Amount <= 0)
        {
            ent.Comp.TrackedAmount.Remove(ev.Tracker);
            return;
        }

        if (!ent.Comp.TrackedAmount.TryAdd(ev.Tracker, ev.Amount))
            ent.Comp.TrackedAmount[ev.Tracker] = ev.Amount;
    }
    private const ushort TickRate = 10;
    public override void Update(float frameTime)
    {
        if (_timing.CurTick.Value % TickRate != 0)
            return;

        var spawnerQuery = EntityQueryEnumerator<PuddleSpawnerComponent>();
        while (spawnerQuery.MoveNext(out var uid, out var spawner))
        {
            if (spawner.NextUpdate > _timing.CurTime || spawner.MaxAmount <= spawner.TrackedAmount.Values.Sum())
                continue;

            var sol = new Solution(spawner.ReagentToSpawn, spawner.Amount);

            if (!_puddleSys.TrySpillAt(Transform(uid).Coordinates, sol, out var puddle, false))
                continue;
            if (spawner.TrackedAmount.TryAdd(puddle, spawner.Amount))
            {
                var comp = EnsureComp<PuddleReagentTrackerComponent>(puddle);
                comp.Manager = uid;
                comp.ReagentId = spawner.ReagentToSpawn;
            }

            spawner.NextUpdate = _timing.CurTime + spawner.Seconds;
        }


    }
}
[ByRefEvent]
public readonly record struct UpdateSolutionSpawnerEvent(EntityUid Tracker, FixedPoint2 Amount);
