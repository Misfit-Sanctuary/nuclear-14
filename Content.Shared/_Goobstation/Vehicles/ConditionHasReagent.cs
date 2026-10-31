using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityEffects;


namespace Content.Shared.Vehicles;


public sealed partial class ConditionHasReagent : ICondition
{
    [DataField(required: true)]
    public string ContainerName = "";

    [DataField(required: true)]
    public ProtoId<ReagentPrototype> Reagent = "";
    public bool Condition(EntityEffectBaseArgs args)
    {

        return args.EntityManager.EntitySysManager.GetEntitySystem<SharedSolutionContainerSystem>().
            TryGetSolution(args.TargetEntity,ContainerName, out var solution) &&
            solution.Value.Comp.Solution.ContainsPrototype(Reagent.Id);
    }
}
