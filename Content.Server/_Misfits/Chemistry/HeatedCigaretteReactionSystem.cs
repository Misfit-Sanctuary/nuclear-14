using Content.Server.Atmos.Components;
using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Server.IgnitionSource;
using Content.Server.Shuttles.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Nutrition.Components;
using Content.Shared.Smoking.Components;
using Content.Shared.Temperature;
using Content.Shared.Temperature.Components;

namespace Content.Server._Misfits.Chemistry;

// #Cythisiax Edited - describe the joined query used to limit periodic hot-solution work.
/// <summary>
/// #Misfits Change
/// Heats solution containers on any entity that reports as hot/lit via IsHotEvent,
/// allowing normal minTemp chemistry to run inside heated containers.
///
/// #Cythisiax Edited - intersect hot-capable components with solution managers so broad
/// component types do not cause unrelated entities to be visited every half-second.
/// </summary>
public sealed class HeatedCigaretteReactionSystem : EntitySystem
{
    [Dependency] private readonly SolutionContainerSystem _solution = default!;

    private const float UpdateInterval = 0.5f;
    private const float HeatedSolutionTemperature = 450f;
    private float _accumulator;

    // Reused each update to deduplicate entities that carry more than one
    // hot-capable component (e.g. a thruster that is also flammable).
    private readonly HashSet<EntityUid> _visited = new();

    public override void Update(float frameTime)
    {
        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;
        _accumulator = 0f;

        _visited.Clear();

        // Enumerate the (small) set of entities that can possibly be hot.
        // Each query targets one hot-capable component type; the shared _visited
        // HashSet prevents processing the same entity twice.
        // #Cythisiax Edited - intersect with solution containers before visiting broad types such as Flammable.
        ProcessHotQuery(EntityQueryEnumerator<IgnitionSourceComponent, SolutionContainerManagerComponent>());
        ProcessHotQuery(EntityQueryEnumerator<AlwaysHotComponent, SolutionContainerManagerComponent>());
        ProcessHotQuery(EntityQueryEnumerator<FlammableComponent, SolutionContainerManagerComponent>());
        ProcessHotQuery(EntityQueryEnumerator<ItemToggleHotComponent, SolutionContainerManagerComponent>());
        ProcessHotQuery(EntityQueryEnumerator<MatchstickComponent, SolutionContainerManagerComponent>());
        ProcessHotQuery(EntityQueryEnumerator<SmokableComponent, SolutionContainerManagerComponent>());
        ProcessHotQuery(EntityQueryEnumerator<ThrusterComponent, SolutionContainerManagerComponent>());
    }

    /// <summary>
    /// Iterates entities with the given hot-capable component, skipping already-visited UIDs,
    /// then checks IsHotEvent and heats any attached solution containers.
    /// </summary>
    private void ProcessHotQuery<T>(EntityQueryEnumerator<T, SolutionContainerManagerComponent> enumerator)
        where T : IComponent
    {
        // #Cythisiax Edited - process only entities that have both a hot-capable component and a solution manager.
        while (enumerator.MoveNext(out var uid, out _, out var manager))
        {
            // Skip if already handled via a different hot-capable component.
            if (!_visited.Add(uid))
                continue;

            // Ask the entity whether it is currently hot/lit.
            var isHotEvent = new IsHotEvent();
            RaiseLocalEvent(uid, isHotEvent, true);
            if (!isHotEvent.IsHot)
                continue;

            // Heat every solution that is below the target temperature.
            foreach (var (_, solutionEnt) in _solution.EnumerateSolutions((uid, manager)))
            {
                if (solutionEnt.Comp.Solution.Temperature >= HeatedSolutionTemperature)
                    continue;

                solutionEnt.Comp.Solution.Temperature = HeatedSolutionTemperature;
                _solution.UpdateChemicals(solutionEnt);
            }
        }
    }
}
