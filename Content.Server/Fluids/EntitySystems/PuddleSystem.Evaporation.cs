using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;

namespace Content.Server.Fluids.EntitySystems;

public sealed partial class PuddleSystem
{

    private void OnEvaporationMapInit(Entity<EvaporationComponent> entity, ref MapInitEvent args)
    {
        if(!TryComp<PuddleComponent>(entity, out var comp) || comp.Solution is null)
            return;

        foreach (var evapReagent in comp.Solution.Value.Comp.Solution.GetReagentPrototypes(_prototypeManager))
            entity.Comp.EvaporationAmount += evapReagent.Key.EvaporationRate;
    }

    private void UpdateEvaporation(EntityUid uid, Solution solution)
    {
        if (HasComp<EvaporationComponent>(uid))
            return;

        // Misfits changed: previous check counted every reagent in a puddle and added up its value if it was an evaporation reagent.
        //                  so we did a O(n*v) operation each time. n being evaporationRegeants and v being regeants in the puddle
        //                  new check is still worst case O(n*v) for puddles with no evaporation chems but at least exits early
        //                  should be changed in the future with a better refactor if puddles are still an issue
        if (solution.Contents.Any(x => EvaporationReagents.Contains(x.Reagent.Prototype.Id)))
        {
            AddComp<EvaporationComponent>(uid);
            return;
        }

        RemComp<EvaporationComponent>(uid);
    }



    private void TickEvaporation()
    {
        var query = EntityQueryEnumerator<EvaporationComponent, PuddleComponent>();

        while (query.MoveNext(out var uid, out var evaporation, out var puddle))
        {
            if (!_solutionContainerSystem.ResolveSolution(uid, puddle.SolutionName, ref puddle.Solution, out var puddleSolution))
                continue;

            puddleSolution.SplitSolutionWithOnly(evaporation.EvaporationAmount, EvaporationReagents);
            // container is
            if (puddleSolution.Volume == FixedPoint2.Zero)
                QueueDel(uid);

            _solutionContainerSystem.UpdateChemicals(puddle.Solution.Value);
        }
    }
}
