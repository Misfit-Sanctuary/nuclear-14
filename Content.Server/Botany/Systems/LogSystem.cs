using Content.Server.Botany.Components;
using Content.Server.Kitchen.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Random;
using Robust.Shared.Containers;
using Robust.Shared.Map;

namespace Content.Server.Botany.Systems;

public sealed class LogSystem : EntitySystem
{
    [Dependency] private readonly SharedHandsSystem _handsSystem = default!;
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;
    [Dependency] private readonly RandomHelperSystem _randomHelper = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LogComponent, InteractUsingEvent>(OnInteractUsing);
    }

    private void OnInteractUsing(EntityUid uid, LogComponent component, InteractUsingEvent args)
    {
        if (!HasComp<SharpComponent>(args.Used))
            return;

        // if in some container, try pick up, else just drop to world
        var inContainer = _containerSystem.IsEntityInContainer(uid);
        var pos = Transform(uid).Coordinates;

        for (var i = 0; i < component.SpawnCount; i++)
        {
            var plank = Spawn(component.SpawnedPrototype, pos);

            if (inContainer)
                _handsSystem.PickupOrDrop(args.User, plank);
            else
            {
                var xform = Transform(plank);
                _containerSystem.AttachParentToContainerOrGrid((plank, xform));
                xform.LocalRotation = 0;
                _randomHelper.RandomOffset(plank, 0.25f);
            }
        }

        QueueDel(uid);
    }

    /// Misfits
    public bool IsGrinderChoppable(EntityUid uid)
    {
        return TryComp<LogComponent>(uid, out var log) && log.GrinderChoppable;
    }

    /// Misifts
    public List<EntityUid> ChopAt(EntityUid uid, EntityCoordinates coordinates, LogComponent? component = null)
    {
        var spawned = new List<EntityUid>();

        if (!Resolve(uid, ref component))
            return spawned;

        for (var i = 0; i < component.SpawnCount; i++)
        {
            var plank = Spawn(component.SpawnedPrototype, coordinates);
            _randomHelper.RandomOffset(plank, 0.25f);
            spawned.Add(plank);
        }

        QueueDel(uid);
        return spawned;
    }
}
