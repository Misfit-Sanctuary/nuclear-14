using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.StatusEffect;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Shared._Misfits.Talents.FanTheHammer;

public sealed class FanTheHammerSystem : EntitySystem
{
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly StatusEffectsSystem _statusEffects = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FanTheHammerActionComponent, FanTheHammerActionEvent>(OnAction);
        SubscribeLocalEvent<FanTheHammerComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<RevolverAmmoProviderComponent, GunRefreshModifiersEvent>(OnGunRefreshModifiers);
    }

    private void OnAction(Entity<FanTheHammerActionComponent> ent, ref FanTheHammerActionEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;

        if (!_gun.TryGetGun(user, out var gunUid, out _) || !HasComp<RevolverAmmoProviderComponent>(gunUid))
        {
            _popup.PopupClient(Loc.GetString("fan-the-hammer-no-revolver"), user, user);
            return;
        }

        if (!_statusEffects.TryAddStatusEffect<FanTheHammerComponent>(user, ent.Comp.StatusEffect, ent.Comp.Duration, true)
            || !TryComp<FanTheHammerComponent>(user, out var active))
            return;

        args.Handled = true;

        active.FireRateMultiplier = ent.Comp.FireRateMultiplier;
        Dirty(user, active);
        RefreshHeldGuns(user);

        _popup.PopupPredicted(
            Loc.GetString("fan-the-hammer-start-self"),
            Loc.GetString("fan-the-hammer-start-others", ("user", user)),
            user,
            user);
    }

    private void OnShutdown(Entity<FanTheHammerComponent> ent, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(ent))
            RefreshHeldGuns(ent);
    }

    private void OnGunRefreshModifiers(Entity<RevolverAmmoProviderComponent> ent, ref GunRefreshModifiersEvent args)
    {
        var holder = Transform(ent.Owner).ParentUid;

        if (!TryComp<FanTheHammerComponent>(holder, out var fan) || fan.LifeStage > ComponentLifeStage.Running)
            return;

        if (!_hands.IsHolding(holder, ent.Owner))
            return;

        args.FireRate *= fan.FireRateMultiplier;
        args.ForceFullAuto = true;
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
