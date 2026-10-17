using Content.Shared._Goobstation.Vehicles;
using Content.Shared.Containers.ItemSlots;


namespace Content.Shared.Vehicles;
/// <summary>
/// basically "can we start/stop engine" but dont care about how/why it came to that conclusion
///
/// Place for systems to attach their own check events and/or
/// base their logic on if a vehicle engine can be enabled/disabled
///
/// Ideally systems that need to assess if any vehicle can be turned on/off
/// just need to check results from raising CheckCanEngineOnEv and CheckCanEngineOffEv
/// this way systems do not need to explicitly care about what comps the vehicle uses/relies on
/// they can just freely make their own logic compatible with engine logic
/// </summary>

public abstract partial class VehicleEngineSystem : EntitySystem
{
    private void CheckEvents()
    {
        SubscribeLocalEvent<VehicleEngineComponent, ItemSlotInsertAttemptEvent>(OnTryInsertKey);
        SubscribeLocalEvent<VehicleEngineComponent, ItemSlotEjectAttemptEvent>(OnTryEjectKey);
    }

    private void OnTryInsertKey(Entity<VehicleEngineComponent> ent, ref ItemSlotInsertAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        var ev = new CheckCanEngineOnEv(ent);
        RaiseLocalEvent(ent, ref ev);
        args.Cancelled = !ev.CanTurnOn;
    }
    private void OnTryEjectKey(Entity<VehicleEngineComponent> ent, ref ItemSlotEjectAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        var ev = new CheckCanEngineOffEv(ent);
        RaiseLocalEvent(ent, ref ev);
        args.Cancelled = !ev.CanTurnOff;
    }
}
