using Content.Server._Misfits.CombatMode;
using Content.Server.Radio;
using Content.Server.Radio.Components;
using Content.Shared._Misfits.Silicon;
using Content.Shared.CombatMode;
using Content.Shared.Hands.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Weapons.Melee;

namespace Content.Server._Misfits.Silicon;

public sealed class SiliconInternalRadioSystem : EntitySystem
{
    [Dependency] private readonly SharedCombatModeSystem _combat = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SiliconInternalRadioComponent, RadioSendAttemptEvent>(OnRadioSendAttempt);
        SubscribeLocalEvent<SiliconInternalRadioComponent, RadioReceiveAttemptEvent>(OnRadioReceiveAttempt);
        SubscribeLocalEvent<SiliconInternalRadioComponent, GetDefaultRadioChannelEvent>(OnGetDefaultRadioChannel);
        SubscribeLocalEvent<AssaultronMeleeWeaponBlockerComponent, CombatModeActivatedEvent>(OnAssaultronCombatModeActivated);
    }

    private void OnRadioSendAttempt(EntityUid uid, SiliconInternalRadioComponent component, ref RadioSendAttemptEvent args)
    {
        if (!CanUseRadio(uid))
            args.Cancelled = true;
    }

    private void OnRadioReceiveAttempt(EntityUid uid, SiliconInternalRadioComponent component, ref RadioReceiveAttemptEvent args)
    {
        if (!CanUseRadio(uid))
            args.Cancelled = true;
    }

    private void OnGetDefaultRadioChannel(EntityUid uid, SiliconInternalRadioComponent component, GetDefaultRadioChannelEvent args)
    {
        if (TryComp<EncryptionKeyHolderComponent>(uid, out var keyHolder))
            args.Channel ??= keyHolder.DefaultChannel;
    }

    private bool CanUseRadio(EntityUid uid)
    {
        return !TryComp<MobStateComponent>(uid, out var state) || state.CurrentState == MobState.Alive;
    }

    private void OnAssaultronCombatModeActivated(EntityUid uid, AssaultronMeleeWeaponBlockerComponent component, CombatModeActivatedEvent args)
    {
        if (!TryComp<HandsComponent>(uid, out var hands) ||
            hands.ActiveHandEntity is not { } held ||
            !HasComp<MeleeWeaponComponent>(held))
        {
            return;
        }

        _combat.SetInCombatMode(uid, false);
        _popup.PopupEntity(component.Popup, uid, uid);
    }
}
