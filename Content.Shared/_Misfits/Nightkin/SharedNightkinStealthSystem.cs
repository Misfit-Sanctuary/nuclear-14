// #Misfits Add - Shared Nightkin passive Stealth Boy implant behavior.
using Content.Shared._Misfits.StealthBoy;
using Content.Shared.Actions;
using Content.Shared.Popups;
using Content.Shared.Stealth.Components;
using Content.Shared.Weapons.Ranged.Events;
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
        // cooldown starts when the cloak fully ends, catches crit/forced decloak too
        SubscribeLocalEvent<NightkinPassiveStealthComponent, StealthBoyCloakEndedEvent>(OnCloakEnded);
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

        // only gate turning it ON, decloaking is always allowed
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

    private void OnCloakEnded(Entity<NightkinPassiveStealthComponent> ent, ref StealthBoyCloakEndedEvent args)
    {
        ent.Comp.CooldownEndTime = _timing.CurTime + ent.Comp.Cooldown;
        Dirty(ent.Owner, ent.Comp);
        // put it on the action too so the button shows the countdown
        _actions.SetCooldown(ent.Comp.ActionEntity, ent.Comp.Cooldown);
    }

    protected abstract void ActivateNightkinStealth(EntityUid uid, NightkinPassiveStealthComponent component);

    protected abstract void DeactivateNightkinStealth(
        EntityUid uid,
        NightkinPassiveStealthComponent component,
        StealthBoyActiveComponent active);
}

// Applies GunHandlingModifierComponent to whatever gun the mob is holding.
// Same event as SpecialPerceptionSystem so it stacks with Perception.
public sealed class GunHandlingModifierSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GunRefreshModifiersEvent>(OnGunRefreshModifiers);
    }

    private void OnGunRefreshModifiers(ref GunRefreshModifiersEvent args)
    {
        var holder = Transform(args.Gun.Owner).ParentUid;

        if (!TryComp<GunHandlingModifierComponent>(holder, out var handling))
            return;

        args.MinAngle = new Angle((double) args.MinAngle * handling.SpreadMultiplier);
        args.MaxAngle = new Angle((double) args.MaxAngle * handling.SpreadMultiplier);
        args.AngleIncrease = new Angle((double) args.AngleIncrease * handling.SpreadMultiplier);
        args.CameraRecoilScalar *= handling.RecoilMultiplier;

        if (args.FireRate > 0f)
            args.FireRate *= handling.FireRateMultiplier;
    }
}
