using Content.Shared._Misfits.PowerArmor;
using Content.Shared._Misfits.Special;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;

namespace Content.Shared._Misfits.Weapons;

///     #Misfits Add - Gates super-heavy weapons so they can only be wielded and fired by
///     characters with sufficient SPECIAL Strength (<see cref="N14SuperHeavyWeaponComponent.MinStrength"/>)
///     or by characters wearing power armor (including salvaged variants).
///     Runs shared so client prediction cancels the wield/fire attempt immediately.
public sealed class N14SuperHeavyWeaponSystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedSpecialSystem _special = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<N14SuperHeavyWeaponComponent, UseInHandEvent>(OnUseInHand, before: [typeof(WieldableSystem)]);
        SubscribeLocalEvent<N14SuperHeavyWeaponComponent, BeforeWieldEvent>(OnBeforeWield);
        SubscribeLocalEvent<N14SuperHeavyWeaponComponent, AttemptShootEvent>(OnAttemptShoot);
    }

    private void OnUseInHand(Entity<N14SuperHeavyWeaponComponent> ent, ref UseInHandEvent args)
    {
        if (TryComp<WieldableComponent>(ent, out var wieldable) && wieldable.Wielded)
            return;

        if (CanUse(args.User, ent.Comp))
            return;

        _popup.PopupClient(Loc.GetString("super-heavy-weapon-too-weak"), ent, args.User);
        args.Handled = true;
    }

    private void OnBeforeWield(Entity<N14SuperHeavyWeaponComponent> ent, ref BeforeWieldEvent args)
    {
        if (CanUse(args.User, ent.Comp))
            return;

        _popup.PopupClient(Loc.GetString("super-heavy-weapon-too-weak"), ent, args.User);
        args.Cancel();
    }

    private void OnAttemptShoot(Entity<N14SuperHeavyWeaponComponent> ent, ref AttemptShootEvent args)
    {
        if (CanUse(args.User, ent.Comp))
            return;

        args.Message = Loc.GetString("super-heavy-weapon-too-weak");
        args.Cancelled = true;
    }

    private bool CanUse(EntityUid user, N14SuperHeavyWeaponComponent weapon)
    {
        // Power armor (and salvaged power armor) servos handle the weight entirely.
        if (HasComp<PowerArmorWornComponent>(user))
            return true;

        return _special.HasRequirement(user, SpecialStat.Strength, weapon.MinStrength);
    }
}
