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

/// <summary>
/// #Misfits Change
/// Heats solution containers on any entity that reports as hot/lit via IsHotEvent,
/// allowing normal minTemp chemistry to run inside heated containers.
///
/// Query-inversion rationale: iterating every SolutionContainerManagerComponent
/// means touching thousands of entities every half-second. Instead we enumerate
/// the 7 "can be hot" component types — typically only 10-50 entities total —
/// and check whether each of those happens to also carry a solution container.
/// This is an order-of-magnitude reduction in entities visited per update tick.
/// </summary>
public sealed class HeatedCigaretteReactionSystem : EntitySystem
{
    [Dependency] private readonly SolutionContainerSystem _solution = default!;

    private const float UpdateInterval = 0.5f;
    private const float HeatedSolutionTemperature = 450f;
    private float _accumulator;

    // Cached query for solution containers — resolved once in Initialize so we
    // can call TryGetComponent cheaply inside the hot-entity loops below.
    private EntityQuery<SolutionContainerManagerComponent> _solutionQuery;

    // Reused each update to deduplicate entities that carry more than one
    // hot-capable component (e.g. a thruster that is also flammable).
    private readonly HashSet<EntityUid> _visited = new();

    public override void Initialize()
    {
        base.Initialize();
        _solutionQuery = GetEntityQuery<SolutionContainerManagerComponent>();
    }

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
        ProcessHotQuery(EntityQueryEnumerator<IgnitionSourceComponent>());
        ProcessHotQuery(EntityQueryEnumerator<AlwaysHotComponent>());
        ProcessHotQuery(EntityQueryEnumerator<FlammableComponent>());
        ProcessHotQuery(EntityQueryEnumerator<ItemToggleHotComponent>());
        ProcessHotQuery(EntityQueryEnumerator<MatchstickComponent>());
        ProcessHotQuery(EntityQueryEnumerator<SmokableComponent>());
        ProcessHotQuery(EntityQueryEnumerator<ThrusterComponent>());
    }

    /// <summary>
    /// Iterates entities with the given hot-capable component, skipping already-visited UIDs,
    /// then checks IsHotEvent and heats any attached solution containers.
    /// </summary>
    private void ProcessHotQuery<T>(EntityQueryEnumerator<T> enumerator)
        where T : IComponent
    {
        while (enumerator.MoveNext(out var uid, out _))
        {
            // Skip if already handled via a different hot-capable component.
            if (!_visited.Add(uid))
                continue;

            // Skip if this entity has no solution container — nothing to heat.
            if (!_solutionQuery.TryGetComponent(uid, out var manager))
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
