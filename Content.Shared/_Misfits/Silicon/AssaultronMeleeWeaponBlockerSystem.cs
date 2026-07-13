using Content.Shared.Popups;
using Content.Shared.Weapons.Melee;
using Content.Shared.Wieldable;

namespace Content.Shared._Misfits.Silicon;

public sealed class AssaultronMeleeWeaponBlockerSystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MeleeWeaponComponent, BeforeWieldEvent>(OnBeforeWield);
    }

    private void OnBeforeWield(EntityUid uid, MeleeWeaponComponent component, BeforeWieldEvent args)
    {
        if (!TryComp<AssaultronMeleeWeaponBlockerComponent>(args.User, out var blocker))
            return;

        args.Cancel();
        _popup.PopupClient(blocker.Popup, args.User, args.User);
    }
}
