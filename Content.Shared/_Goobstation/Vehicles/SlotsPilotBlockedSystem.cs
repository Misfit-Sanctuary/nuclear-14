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

    private void OnInsertAttempt(Entity<SlotsPilotBlockedComponent> ent, ref ItemSlotInsertAttemptEvent args)
    {
        var pilotComp = Comp<DriveOnBuckleComponent>(ent);
        var beingPiloted = pilotComp.Pilots.Count > 0;
        if (!beingPiloted || (args.User is not null && pilotComp.Pilots.Contains(args.User.Value)))
            return;

        if (ent.Comp.BlockedSlots.Contains(args.Slot.ID))
            args.Cancelled = true;
    }
    private void OnEjectAttempt(Entity<SlotsPilotBlockedComponent> ent, ref ItemSlotEjectAttemptEvent args)
    {
        var pilotComp = Comp<DriveOnBuckleComponent>(ent);
        var beingPiloted = pilotComp.Pilots.Count > 0;
        if (!beingPiloted || (args.User is not null && pilotComp.Pilots.Contains(args.User.Value)))
            return;

        if (ent.Comp.BlockedSlots.Contains(args.Slot.ID))
            args.Cancelled = true;
    }
}
