using Content.Shared.Destructible;
using Content.Shared.Emp;
using Content.Shared.EntityEffects;
using Robust.Shared.Toolshed.Commands.Generic;


namespace Content.Shared.Vehicles;
/// <summary>
/// These are handlers that directly determine if an engine turns on/off
/// Seperate from "requestEngineOn/Off" to keep it independent from implementation that
/// serve as "outer" triggers that only signal/request for an engine to turn on.
/// They do not decide if the engine will turn on/off and they can choose to care abt that
/// Ie just because we can insert a key, doesn't mean the engine should turn on/off
/// </summary>

public abstract partial class VehicleEngineSystem : EntitySystem
{
    private EntityEffectBaseArgs _entArgs = default!;
    private void EngineConditions()
    {

        SubscribeLocalEvent<VehicleEngineComponent, EngineTryOnEvent>(OnTryEngineOn);
        SubscribeLocalEvent<DisabledEntityComponent, EngineTryOnEvent>(OnTryDisabledEngineOn);
        SubscribeLocalEvent<EmpDisabledComponent, EngineTryOnEvent>(OnTryEMPEngineOn);
    }


    private void OnTryDisabledEngineOn(Entity<DisabledEntityComponent> ent, ref EngineTryOnEvent args)
    {
        args.Handled = true;
        args.CanTurnOn = false;
        _popup.PopupEntity("The engine hisses and whines terribly. It's never coming back on again", ent);
    }
    private void OnTryEMPEngineOn(Entity<EmpDisabledComponent> ent, ref EngineTryOnEvent args)
    {
        args.Handled = true;
        args.CanTurnOn = false;
        _popup.PopupEntity("Engine doesnt have any power to start up", ent);
    }

    private void OnTryEngineOn(Entity<VehicleEngineComponent> ent, ref EngineTryOnEvent args)
    {
        if (ent.Comp.Conditions.Length <= 0 || args.Handled)
            return;

        _entArgs.TargetEntity = ent;
        var conList = ent.Comp.Conditions;
        // just dont use the LINQ expression to avoid delegate allocaiton
        foreach (var con in conList)
            if (!con.Condition(_entArgs))
            {
                args.CanTurnOn = false;
                args.Handled = true;
                return;
            }
        args.CanTurnOn = true;
    }

}
