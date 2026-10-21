using Content.Shared._Goobstation.Vehicles;
using Content.Shared.Containers.ItemSlots;


namespace Content.Shared.Vehicles;


public sealed class SlotsPilotBlockedSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<SlotsPilotBlockedComponent, ItemSlotInsertAttemptEvent>(OnInsertAttempt);
        SubscribeLocalEvent<SlotsPilotBlockedComponent, ItemSlotEjectAttemptEvent>(OnEjectAttempt);
    }
    //
    private void OnInsertAttempt(Entity<SlotsPilotBlockedComponent> ent, ref ItemSlotInsertAttemptEvent args)
    {
       if(args.Cancelled) return;

       if (TryBlock(ent, Comp<DriveOnBuckleComponent>(ent), args.Slot.ID,args.User))
           args.Cancelled = true;
    }
    private void OnEjectAttempt(Entity<SlotsPilotBlockedComponent> ent, ref ItemSlotEjectAttemptEvent args)
    {
        if(args.Cancelled) return;

        if (TryBlock(ent, Comp<DriveOnBuckleComponent>(ent), args.Slot.ID,args.User))
            args.Cancelled = true;
    }
    private static bool TryBlock(Entity<SlotsPilotBlockedComponent> ent, DriveOnBuckleComponent driveComp, string? slotId, EntityUid? user)
    {
        if (user is null || slotId is null || driveComp.Pilots.Count <= 0 || driveComp.Pilots.Contains(user.Value))
            return false;
        return ent.Comp.BlockedSlots.Contains(slotId);

    }
}
