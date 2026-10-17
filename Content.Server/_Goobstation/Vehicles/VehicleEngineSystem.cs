using Content.Server.Emp;
using Content.Shared.Vehicles;


namespace Content.Server.Vehicles;


public sealed partial class VehicleEngineSystem : Shared.Vehicles.VehicleEngineSystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<VehicleEngineComponent, EmpPulseEvent>(OnEMP);
    }

    private void OnEMP(Entity<VehicleEngineComponent> ent, ref EmpPulseEvent args)
    {
        args.Affected = true;
        args.Disabled = true;
        RaiseLocalEvent(ent, ref _engineOffEvent);
    }

}
