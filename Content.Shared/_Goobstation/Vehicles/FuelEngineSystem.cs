using Content.Shared.Vehicles;


namespace Content.Shared._Goobstation.Vehicles;


public sealed class FuelEngineSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FuelEngineComponent, EngineOffEvent>(OnEngineOff);
        SubscribeLocalEvent<FuelEngineComponent, EngineOnEvent>(OnEngineOn);
        // move to engineFuel System
        SubscribeLocalEvent<FuelEngineComponent,SolutionConsumedEv>(OnEngineFuelTick);
        //SubscribeLocalEvent<FuelEngineComponent, InputRelayTrySetEv>(OnTryEngineFuelOn, after: new[] { typeof(VehicleEngineSystem) });
    }

    private void OnEngineOff(Entity<FuelEngineComponent> ent, ref EngineOffEvent args)
    {
        RemComp<ActiveSolutionConsumerComponent>(ent);
    }
    private void OnEngineOn(Entity<FuelEngineComponent> ent, ref EngineOnEvent args)
    {
        EnsureComp<ActiveSolutionConsumerComponent>(ent);
    }
    private void OnEngineFuelTick(Entity<FuelEngineComponent> ent, ref SolutionConsumedEv ev)
    {
        if (ev.SolutionSource.Comp.Solution.Volume > 0)
            return;
        var evEngine = new EngineOffEvent();
        RaiseLocalEvent(ent, ref evEngine);
    }
}
