using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory.Events;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;

namespace Content.Shared._Misfits.C27;

// #Misfits Add - Shared system that gates equipping of C-27 armor / helmet items: only entities
// whose HumanoidAppearanceComponent.Species == "C27" can put them on. Mirrors the pattern used
// by PowerArmorProficiencySystem so prediction cancels the equip animation immediately on the
// client side.
public sealed class MisfitsC27ArmorSystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MisfitsC27ArmorComponent, BeingEquippedAttemptEvent>(OnEquipAttempt);
        SubscribeLocalEvent<MisfitsC27WeaponComponent, UseInHandEvent>(OnWeaponUseInHand, before: [typeof(WieldableSystem)]);
        SubscribeLocalEvent<MisfitsC27WeaponComponent, BeforeWieldEvent>(OnWeaponBeforeWield);
        SubscribeLocalEvent<MisfitsC27WeaponComponent, AttemptShootEvent>(OnWeaponAttemptShoot);
        // #Misfits Add - Present C-27 chassis as synthetic for identity purposes. Mirrors
        // SharedBorgSystem.OnTryGetIdentityShortInfo: when a C-27 is seen without ID, use the
        // entity name (e.g. "c-27 humanoid robot") instead of the default "old person" fallback.
        SubscribeLocalEvent<TryGetIdentityShortInfoEvent>(OnTryGetIdentityShortInfo);
    }

    private void OnTryGetIdentityShortInfo(TryGetIdentityShortInfoEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<HumanoidAppearanceComponent>(args.ForActor, out var humanoid))
            return;

        if (!IsC27Species(humanoid.Species))
            return;

        args.Title = Name(args.ForActor).Trim();
        args.Handled = true;
    }

    private void OnEquipAttempt(Entity<MisfitsC27ArmorComponent> item, ref BeingEquippedAttemptEvent args)
    {
        // Trophy / admin-spawned variants: skip the gate entirely.
        if (!item.Comp.RequiresC27Species)
            return;

        // Non-humanoids (e.g. animal spawns, dummies) trivially can't wear C-27 armor.
        if (!TryComp<HumanoidAppearanceComponent>(args.EquipTarget, out var humanoid))
        {
            args.Reason = "c27-armor-species-required";
            args.Cancel();
            return;
        }

        // Species ProtoId compares case-sensitively as a string.
        if (IsC27Species(humanoid.Species) &&
            (item.Comp.AllowedSpecies == null || item.Comp.AllowedSpecies.Contains(humanoid.Species)))
            return;

        args.Reason = "c27-armor-species-required";
        args.Cancel();
    }

    private void OnWeaponUseInHand(Entity<MisfitsC27WeaponComponent> item, ref UseInHandEvent args)
    {
        // Let already-wielded weapons be unwielded even if a non-C-27 somehow gets one.
        if (TryComp<WieldableComponent>(item, out var wieldable) && wieldable.Wielded)
            return;

        if (CanUseC27Weapon(args.User, item.Comp))
            return;

        _popup.PopupClient(Loc.GetString("c27-weapon-species-required"), item, args.User);
        args.Handled = true;
    }

    private void OnWeaponBeforeWield(Entity<MisfitsC27WeaponComponent> item, ref BeforeWieldEvent args)
    {
        if (CanUseC27Weapon(args.User, item.Comp))
            return;

        _popup.PopupClient(Loc.GetString("c27-weapon-species-required"), item, args.User);
        args.Cancel();
    }

    private void OnWeaponAttemptShoot(Entity<MisfitsC27WeaponComponent> item, ref AttemptShootEvent args)
    {
        if (CanUseC27Weapon(args.User, item.Comp))
            return;

        args.Message = Loc.GetString("c27-weapon-species-required");
        args.Cancelled = true;
    }

    private bool CanUseC27Weapon(EntityUid user, MisfitsC27WeaponComponent weapon)
    {
        if (!TryComp<HumanoidAppearanceComponent>(user, out var humanoid))
            return false;

        return IsC27Species(humanoid.Species) &&
               (weapon.AllowedSpecies == null || weapon.AllowedSpecies.Contains(humanoid.Species));
    }

    private static bool IsC27Species(string species)
    {
        return species == "C27"
            || species == "C27NCR"
            || species == "C27BoS"
            || species == "C27ZAX";
    }
}
