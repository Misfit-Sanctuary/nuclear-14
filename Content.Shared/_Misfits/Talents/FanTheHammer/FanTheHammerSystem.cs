using Content.Shared.Actions;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._Misfits.Talents.FanTheHammer;

public sealed class FanTheHammerSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FanTheHammerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<FanTheHammerComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<FanTheHammerComponent, FanTheHammerActionEvent>(OnAction);
        SubscribeLocalEvent<RevolverAmmoProviderComponent, GunRefreshModifiersEvent>(OnGunRefreshModifiers);
    }

    private void OnMapInit(Entity<FanTheHammerComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent.Owner, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnShutdown(Entity<FanTheHammerComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);

        if (ent.Comp.ActiveUntil == null)
            return;

        ent.Comp.ActiveUntil = null;
        RefreshHeldGuns(ent.Owner);
    }

    private void OnAction(Entity<FanTheHammerComponent> ent, ref FanTheHammerActionEvent args)
    {
        if (args.Handled)
            return;

        if (!_gun.TryGetGun(ent.Owner, out var gunUid, out _) || !HasComp<RevolverAmmoProviderComponent>(gunUid))
        {
            _popup.PopupClient(Loc.GetString("fan-the-hammer-no-revolver"), ent.Owner, ent.Owner);
            return;
        }

        args.Handled = true;

        ent.Comp.ActiveUntil = _timing.CurTime + ent.Comp.Duration;
        Dirty(ent);
        RefreshHeldGuns(ent.Owner);

        _popup.PopupPredicted(
            Loc.GetString("fan-the-hammer-start-self"),
            Loc.GetString("fan-the-hammer-start-others", ("user", ent.Owner)),
            ent.Owner,
            ent.Owner);
    }

    private void OnGunRefreshModifiers(Entity<RevolverAmmoProviderComponent> ent, ref GunRefreshModifiersEvent args)
    {
        var holder = Transform(ent.Owner).ParentUid;

        if (!TryComp<FanTheHammerComponent>(holder, out var fan) || fan.ActiveUntil == null)
            return;

        if (!_hands.IsHolding(holder, ent.Owner))
            return;

        args.FireRate *= fan.FireRateMultiplier;
        args.ForceFullAuto = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<FanTheHammerComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.ActiveUntil is not { } until || curTime < until)
                continue;

            comp.ActiveUntil = null;
            Dirty(uid, comp);
            RefreshHeldGuns(uid);

            _popup.PopupClient(Loc.GetString("fan-the-hammer-end"), uid, uid);
        }
    }

    private void RefreshHeldGuns(EntityUid uid)
    {
        foreach (var held in _hands.EnumerateHeld(uid))
        {
            if (TryComp<GunComponent>(held, out var gun))
                _gun.RefreshModifiers((held, gun));
        }
    }
}
