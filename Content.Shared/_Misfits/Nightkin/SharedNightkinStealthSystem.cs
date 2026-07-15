// #Misfits Add - Shared Nightkin passive Stealth Boy implant behavior.
using Content.Shared._Misfits.StealthBoy;
using Content.Shared.Actions;
using Content.Shared.Nutrition.Components;
using Content.Shared.Popups;
using Content.Shared.Stealth.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Wieldable;
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
        // don't strand the raised hunger rate if the implant is removed mid-cloak
        RestoreCloakHunger(ent.Owner, ent.Comp);
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
        ApplyCloakHunger(ent.Owner, ent.Comp);
    }

    private void OnCloakEnded(Entity<NightkinPassiveStealthComponent> ent, ref StealthBoyCloakEndedEvent args)
    {
        ent.Comp.CooldownEndTime = _timing.CurTime + ent.Comp.Cooldown;
        RestoreCloakHunger(ent.Owner, ent.Comp);
        Dirty(ent.Owner, ent.Comp);
        // put it on the action too so the button shows the countdown
        _actions.SetCooldown(ent.Comp.ActionEntity, ent.Comp.Cooldown);
    }

    // staying cloaked drains hunger faster. multiply/divide (not store+restore) so
    // it composes cleanly with the Endurance decay tweak if that changes mid-cloak.
    private void ApplyCloakHunger(EntityUid uid, NightkinPassiveStealthComponent comp)
    {
        if (comp.HungerBumped || comp.CloakHungerMultiplier == 1f)
            return;

        if (!TryComp<HungerComponent>(uid, out var hunger))
            return;

        hunger.BaseDecayRate *= comp.CloakHungerMultiplier;
        hunger.ActualDecayRate *= comp.CloakHungerMultiplier;
        comp.HungerBumped = true;
        Dirty(uid, hunger);
    }

    private void RestoreCloakHunger(EntityUid uid, NightkinPassiveStealthComponent comp)
    {
        if (!comp.HungerBumped)
            return;

        comp.HungerBumped = false;

        if (!TryComp<HungerComponent>(uid, out var hunger))
            return;

        hunger.BaseDecayRate /= comp.CloakHungerMultiplier;
        hunger.ActualDecayRate /= comp.CloakHungerMultiplier;
        Dirty(uid, hunger);
    }

    protected abstract void ActivateNightkinStealth(EntityUid uid, NightkinPassiveStealthComponent component);

    protected abstract void DeactivateNightkinStealth(
        EntityUid uid,
        NightkinPassiveStealthComponent component,
        StealthBoyActiveComponent active);
}

// Applies GunHandlingModifierComponent to whatever gun the mob is holding.
// NOTE: has to be a directed subscription on the gun - RefreshModifiers raises
// the event directed with broadcast off, broadcast handlers never see it.
// Runs after WieldableSystem so we scale the post-wield-bonus spread. Scaling
// the base instead sends wielded rifles negative (base * 0.35 - flat wield
// bonus < 0) which inverts the clamp and sprays bullets everywhere.
public sealed class GunHandlingModifierSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GunComponent, GunRefreshModifiersEvent>(OnGunRefreshModifiers,
            after: [typeof(WieldableSystem)]);
    }

    private void OnGunRefreshModifiers(Entity<GunComponent> gun, ref GunRefreshModifiersEvent args)
    {
        var holder = Transform(gun.Owner).ParentUid;

        if (!TryComp<GunHandlingModifierComponent>(holder, out var handling))
            return;

        var min = Math.Max(0, (double) args.MinAngle * handling.SpreadMultiplier);
        var max = Math.Max(min, (double) args.MaxAngle * handling.SpreadMultiplier);
        var inc = Math.Max(0, (double) args.AngleIncrease * handling.SpreadMultiplier);

        args.MinAngle = new Angle(min);
        args.MaxAngle = new Angle(max);
        args.AngleIncrease = new Angle(inc);
        args.CameraRecoilScalar *= handling.RecoilMultiplier;

        if (args.FireRate > 0f)
            args.FireRate *= handling.FireRateMultiplier;
    }
}
