using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Examine;
using Robust.Shared.Prototypes;
using System.Linq;

namespace Content.Server._Nuclear14.Chemistry;

public sealed class ChemistTraitSystem : EntitySystem
{
    private const int IntelligenceChemExamineThreshold = 10;

    [Dependency] private readonly SharedSolutionContainerSystem _solutionSystem = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SharedSpecialSystem _special = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SolutionContainerManagerComponent, ExaminedEvent>(OnExamineSolutionContainer);
    }

    private void OnExamineSolutionContainer(Entity<SolutionContainerManagerComponent> entity, ref ExaminedEvent args)
    {
        if (_special.GetEffective(args.Examiner, SpecialStat.Intelligence) < IntelligenceChemExamineThreshold)
            return;

        var reagentLines = new List<string>();
        foreach (var (_, solutionEntity) in _solutionSystem.EnumerateSolutions((entity.Owner, (SolutionContainerManagerComponent?) entity.Comp)))
        {
            var solution = solutionEntity.Comp.Solution;
            if (solution.Volume <= 0)
                continue;

            var sorted = solution.GetReagentPrototypes(_prototype)
                .OrderByDescending(pair => pair.Value.Value)
                .ThenBy(pair => pair.Key.LocalizedName);

            foreach (var (proto, quantity) in sorted)
            {
                reagentLines.Add(Loc.GetString("scannable-solution-chemical",
                    ("type", proto.LocalizedName),
                    ("color", proto.SubstanceColor.ToHexNoAlpha()),
                    ("amount", quantity)));
            }
        }

        if (reagentLines.Count == 0)
            return;

        using (args.PushGroup(nameof(ChemistTraitSystem)))
        {
            args.PushMarkup(Loc.GetString("scannable-solution-main-text"));
            foreach (var line in reagentLines)
                args.PushMarkup(line);
        }
    }
}
