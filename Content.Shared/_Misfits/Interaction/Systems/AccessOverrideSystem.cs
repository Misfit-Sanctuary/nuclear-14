



using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Vehicles;
using Robust.Shared.Containers;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._Misfits.Interaction;

public sealed partial class AccessOverrideSystem : EntitySystem
{

    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private InventorySystem _invent = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AccessOverrideComponent, AccessibleOverrideEvent>(OnAccessOverride);
        //SubscribeLocalEvent<StorageComponent, AccessibleOverrideEvent>(OnAccessVehicle);

        SubscribeLocalEvent<OpenSlotComponent, EntInsertedIntoContainerMessage>(OnInsertOpenSlot);

        SubscribeLocalEvent<AccessByParentComponent, EntGotRemovedFromContainerMessage>(OnRemoveAccessParentEnt);
        SubscribeLocalEvent<AccessByParentComponent, AccessibleOverrideEvent>(OnParentAccess);
    }
    private void OnParentAccess(Entity<AccessByParentComponent> ent, ref AccessibleOverrideEvent args)
    {
        args.Handled = true;
        args.Accessible = true;
    }
    private void OnInsertOpenSlot(Entity<OpenSlotComponent> ent, ref EntInsertedIntoContainerMessage ev)
    {

        if (!_invent.TryGetContainingSlot(ev.Entity, out var slot) ||
            !ent.Comp.OpenSlots.Contains(slot.Name))
            return;

        EnsureComp<AccessByParentComponent>(ev.Entity);

    }

    private void OnRemoveAccessParentEnt(Entity<AccessByParentComponent> ent, ref EntGotRemovedFromContainerMessage ev)
    {
        if (ent.Comp.DelCompOnRemove)
            RemCompDeferred(ev.Entity, ent.Comp);
    }

    private void OnAccessOverride(Entity<AccessOverrideComponent> entity, ref AccessibleOverrideEvent ev)
    {
        if (ev.Handled) return;
        ev.Handled = true;
        ev.Accessible = true;
    }

}
