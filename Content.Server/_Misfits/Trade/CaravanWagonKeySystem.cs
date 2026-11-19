using Robust.Shared.Containers;

namespace Content.Server._Misfits.Trade;

// #Misfits Add - the wagon follows its key: anchored and immovable without it, free to drag with it.
public sealed class CaravanWagonKeySystem : EntitySystem
{
    private const string KeySlot = "key_slot";

    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CaravanWagonKeyComponent, EntInsertedIntoContainerMessage>(OnKeyInserted);
        SubscribeLocalEvent<CaravanWagonKeyComponent, EntRemovedFromContainerMessage>(OnKeyRemoved);
    }

    private void OnKeyInserted(Entity<CaravanWagonKeyComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == KeySlot)
            _transform.Unanchor(ent.Owner);
    }

    private void OnKeyRemoved(Entity<CaravanWagonKeyComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == KeySlot)
            _transform.AnchorEntity(ent.Owner);
    }
}
