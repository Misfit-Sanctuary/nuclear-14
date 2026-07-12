// #Misfits Add - Shared Nightkin passive Stealth Boy implant behavior.
using Content.Shared._Misfits.StealthBoy;
using Content.Shared.Actions;
using Content.Shared.Popups;
using Content.Shared.Stealth.Components;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._Misfits.Nightkin;

public abstract class SharedNightkinStealthSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NightkinPassiveStealthComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<NightkinPassiveStealthComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<NightkinPassiveStealthComponent, ToggleNightkinStealthActionEvent>(OnToggleAction);
        // The cloak can end without the toggle (incapacitation, forced removal),
        // so the recharge starts from the active component's shutdown, not the action.
        SubscribeLocalEvent<StealthBoyActiveComponent, ComponentShutdown>(OnActiveShutdown);
    }

    private void OnMapInit(Entity<NightkinPassiveStealthComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent.Owner, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnShutdown(Entity<NightkinPassiveStealthComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
    }

    private void OnToggleAction(Entity<NightkinPassiveStealthComponent> ent, ref ToggleNightkinStealthActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (TryComp<StealthBoyActiveComponent>(ent.Owner, out var active))
        {
            DeactivateNightkinStealth(ent.Owner, ent.Comp, active);
            return;
        }

        // Recharge gate only applies to activation — dropping the cloak is always allowed.
        if (_timing.CurTime < ent.Comp.CooldownEndTime)
        {
            if (_net.IsServer)
            {
                var remaining = (int) Math.Ceiling((ent.Comp.CooldownEndTime - _timing.CurTime).TotalSeconds);
                _popup.PopupEntity($"Your Stealth Boy implant is still recharging ({remaining}s).", ent.Owner, ent.Owner);
            }
            return;
        }

        if (HasComp<StealthComponent>(ent.Owner))
        {
            if (_net.IsServer)
                _popup.PopupEntity("You are already cloaked.", ent.Owner, ent.Owner);
            return;
        }

        ActivateNightkinStealth(ent.Owner, ent.Comp);
    }

    /// <summary>
    /// Starts the implant recharge once the cloak has fully ended, regardless of
    /// why it ended (manual toggle, incapacitation, or expiry). The action entity
    /// gets the same cooldown so the button shows the recharge countdown.
    /// </summary>
    private void OnActiveShutdown(Entity<StealthBoyActiveComponent> ent, ref ComponentShutdown args)
    {
        if (Terminating(ent))
            return;

        if (!TryComp<NightkinPassiveStealthComponent>(ent.Owner, out var passive))
            return;

        passive.CooldownEndTime = _timing.CurTime + passive.Cooldown;
        Dirty(ent.Owner, passive);
        _actions.SetCooldown(passive.ActionEntity, passive.Cooldown);
    }

    protected abstract void ActivateNightkinStealth(EntityUid uid, NightkinPassiveStealthComponent component);

    protected abstract void DeactivateNightkinStealth(
        EntityUid uid,
        NightkinPassiveStealthComponent component,
        StealthBoyActiveComponent active);
}
