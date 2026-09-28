// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from Goob Station / Trauma Station

using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;

namespace Content.Shared._Misfits.Factory.Slots;

/// <summary>
/// An automated solution that can be used by liquid pumps.
/// </summary>
public sealed partial class AutomatedSolution : AutomationSlot
{
    [DataField(required: true)]
    public string SolutionName;

    private Entity<SolutionComponent>? _solution;

    private SharedSolutionContainerSystem _solutionSys;

    public override void Initialize()
    {
        base.Initialize();

        _solutionSys = EntMan.System<SharedSolutionContainerSystem>();
    }

    public override Entity<SolutionComponent>? GetSolution()
    {
        if (_solution != null)
            return _solution;

        if (_solutionSys.TryGetSolution(Owner, SolutionName, out _solution, true))
            return _solution;

        throw new InvalidOperationException($"Entity {EntMan.ToPrettyString(Owner)} had no solution {SolutionName} for automation!");
    }
}
