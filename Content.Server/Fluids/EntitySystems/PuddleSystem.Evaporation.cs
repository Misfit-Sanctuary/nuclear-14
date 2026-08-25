using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;

namespace Content.Server.Fluids.EntitySystems;

public sealed partial class PuddleSystem
{
    private static readonly TimeSpan EvaporationCooldown = TimeSpan.FromSeconds(3);

    private void OnEvaporationMapInit(Entity<EvaporationComponent> entity, ref MapInitEvent args)
    {
        entity.Comp.NextTick = _timing.CurTime + EvaporationCooldown;
    }

    private void UpdateEvaporation(EntityUid uid, Solution solution)
    {
        if (HasComp<EvaporationComponent>(uid))
        {
            return;
        }

        if (solution.GetTotalPrototypeQuantity(EvaporationReagents) > FixedPoint2.Zero)
        {
            var evaporation = AddComp<EvaporationComponent>(uid);
            evaporation.NextTick = _timing.CurTime + EvaporationCooldown;
            return;
        }

        RemComp<EvaporationComponent>(uid);
    }

    private void TickEvaporation()
    {
        var query = EntityQueryEnumerator<EvaporationComponent, PuddleComponent>();
        var xformQuery = GetEntityQuery<TransformComponent>();
        var curTime = _timing.CurTime;
        while (query.MoveNext(out var uid, out var evaporation, out var puddle))
        {
            if (evaporation.NextTick > curTime)
                continue;

            evaporation.NextTick += EvaporationCooldown;

            if (!_solutionContainerSystem.ResolveSolution(uid, puddle.SolutionName, ref puddle.Solution, out var puddleSolution))
                continue;

            var reagentTick = evaporation.EvaporationAmount * EvaporationCooldown.TotalSeconds;
			// only speed up the evaporation if there are reagents with FastEvaporation
			if(puddleSolution.GetTotalPrototypeQuantity(FastEvaporationReagents)  > FixedPoint2.Zero){
			reagentTick = reagentTick * 50; // change this number to change FastEvaporation speed
			}

            puddleSolution.SplitSolutionWithOnly(reagentTick, EvaporationReagents);

            // Despawn if we're done
            if (puddleSolution.Volume == FixedPoint2.Zero)
            {
                QueueDel(uid);
            }

            _solutionContainerSystem.UpdateChemicals(puddle.Solution.Value);
        }
    }
}
