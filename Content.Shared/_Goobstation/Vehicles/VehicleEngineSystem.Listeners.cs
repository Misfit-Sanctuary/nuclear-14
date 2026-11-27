using Content.Shared.Containers.ItemSlots;
using Content.Shared.Destructible;


namespace Content.Shared.Vehicles;
/// <summary>
/// events that the vehicleEngine comp reacts to itself
/// so handlers
/// </summary>
public abstract partial class VehicleEngineSystem : EntitySystem
{
    private void VehicleHandlers()
    {

        SubscribeLocalEvent<VehicleEngineComponent, BreakageEventArgs>(OnBreak);
        //SubscribeLocalEvent<VehicleEngineComponent, ComponentAdd>(OnComponentAdd);
        SubscribeLocalEvent<VehicleEngineComponent, ItemSlotEjectedEvent>(OnEject);
        SubscribeLocalEvent<VehicleEngineComponent, ItemSlotInsertedEvent>(OnInsertKey);

    }

    // emp event is handled serverside
    // private void OnEMP(Entity<VehicleEngineComponent> ent, ref EmpPulseEvent args)
    private void OnBreak(Entity<VehicleEngineComponent> ent, ref BreakageEventArgs args)
    {
        EnsureComp<DisabledEntityComponent>(ent);
        ForceEngineOff(ent);
    }

    private void OnEject(Entity<VehicleEngineComponent> ent, ref ItemSlotEjectedEvent args)
    {
        TryTurnOffEngine(ent, args.User);
    }

    private void OnInsertKey(Entity<VehicleEngineComponent> ent, ref ItemSlotInsertedEvent args)
    {
        TryTurnOnEngine(ent, args.User);
    }



}
