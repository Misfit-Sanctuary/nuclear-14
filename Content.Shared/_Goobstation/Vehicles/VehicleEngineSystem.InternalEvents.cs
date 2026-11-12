using Content.Shared.Emp;
using Content.Shared.EntityEffects;
using Robust.Shared.Toolshed.Commands.Generic;


namespace Content.Shared.Vehicles;
/// <summary>
/// Basically where you can customize/define what stops an engine from turning on or off
///
/// Place to tie in components that deal with "internal" engine logic
///
/// </summary>

public abstract partial class VehicleEngineSystem : EntitySystem
{
    private void InternalLogic()
    {
                SubscribeLocalEvent<VehicleEngineTurnOnConditionsComponent, EngineInternalTryOnEvent>(OnTryEngineOn);
                SubscribeLocalEvent<DisabledEntityComponent, EngineInternalTryOnEvent>(OnTryDisabledEngineOn);
                SubscribeLocalEvent<EmpDisabledComponent, EngineInternalTryOnEvent>(OnTryEMPEngineOn);
    }

    private void OnTryDisabledEngineOn(Entity<DisabledEntityComponent> ent, ref EngineInternalTryOnEvent args)
    {
        args.Handled = true;
        args.CanTurnOn = false;
        _popup.PopupEntity("The engine hisses and whines terribly. It's never coming back on again", ent);
    }
    private void OnTryEMPEngineOn(Entity<EmpDisabledComponent> ent, ref EngineInternalTryOnEvent args)
    {
        args.Handled = true;
        args.CanTurnOn = false;
        _popup.PopupEntity("Engine doesnt have any power to start up", ent);
    }
    private void OnTryEngineOn(Entity<VehicleEngineTurnOnConditionsComponent> ent, ref EngineInternalTryOnEvent args)
    {
        if (args.Handled)
            return;
        var entArgs = new EntityEffectBaseArgs(ent,_entMan);

        foreach (var con in ent.Comp.ConditionPopups)
        {
            args.CanTurnOn = AllPassed(con.Conditions, entArgs);
            if (args.CanTurnOn)
                continue;
            _popup.PopupEntity(Loc.GetString(con.FailMsg), ent);
            return;
        }
    }
    private static bool AllPassed(ICondition[] conditions, EntityEffectBaseArgs args)
    {
        return conditions.Length != 0 && conditions.All(con => con.Condition(args));
    }
}
