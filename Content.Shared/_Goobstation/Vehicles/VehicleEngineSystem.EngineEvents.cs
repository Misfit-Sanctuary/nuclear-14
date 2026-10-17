using Content.Shared.Containers.ItemSlots;


namespace Content.Shared.Vehicles;
/// <summary>
/// events that the vehicleEngine comp reacts to itself
/// These are where events that directly act on the engine's functionality should go to
/// </summary>
public abstract partial class VehicleEngineSystem : EntitySystem
{
    private void DirectEngineEvents()
    {
        SubscribeLocalEvent<VehicleEngineComponent, ItemSlotEjectedEvent>(OnEject);
        SubscribeLocalEvent<VehicleEngineComponent, ItemSlotInsertedEvent>(OnInsertKey);
        SubscribeLocalEvent<EngineNeedsFuelComponent,SolutionConsumedEv>(OnEngineFuelTick);
    }
    protected static EngineOffEvent _engineOffEvent = new();
    protected static EngineOnEvent _engineOnEvent = new();
    // emp event is handled serverside
    // private void OnEMP(Entity<VehicleEngineComponent> ent, ref EmpPulseEvent args)

    private void OnEject(Entity<VehicleEngineComponent> ent, ref ItemSlotEjectedEvent args) => RaiseLocalEvent(ent, ref _engineOffEvent);
    private void OnInsertKey(Entity<VehicleEngineComponent> ent, ref ItemSlotInsertedEvent args) => RaiseLocalEvent(ent, ref _engineOnEvent);

    private void OnEngineFuelTick(Entity<EngineNeedsFuelComponent> ent, ref SolutionConsumedEv ev)
    {
        if (ev.SolutionSource.Comp.Solution.Volume > 0)
            return;

        RaiseLocalEvent(ent, ref _engineOffEvent);
    }

}
