using Content.Shared.Hands;
using Content.Shared.Interaction.Events;
using Content.Shared.Timing;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Wieldable.Components;

namespace Content.Shared.Wieldable;

public sealed class WieldDelaySystem : EntitySystem
{
    [Dependency] private readonly UseDelaySystem _useDelay = default!;

    private const string WieldDelayId = "WieldDelay";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WieldDelayComponent, GotEquippedHandEvent>(OnEquippedHand);
        SubscribeLocalEvent<WieldDelayComponent, ItemWieldedEvent>(OnItemWielded);
        SubscribeLocalEvent<WieldDelayComponent, ItemUnwieldedEvent>(OnItemUnwielded);
        SubscribeLocalEvent<WieldDelayComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<WieldDelayComponent, ShotAttemptedEvent>(OnShotAttempted);
    }

    private void OnEquippedHand(
        EntityUid uid,
        WieldDelayComponent component,
        ref GotEquippedHandEvent args)
    {
        StartDelay(uid, component);
    }

    private void OnItemWielded(
        EntityUid uid,
        WieldDelayComponent component,
        ref ItemWieldedEvent args)
    {
        StartDelay(uid, component);
    }

    private void OnItemUnwielded(
        EntityUid uid,
        WieldDelayComponent component,
        ItemUnwieldedEvent args)
    {
        if (TryComp<UseDelayComponent>(uid, out var useDelay))
            _useDelay.CancelDelay((uid, useDelay), WieldDelayId);
    }

    private void OnUseInHand(
        EntityUid uid,
        WieldDelayComponent component,
        UseInHandEvent args)
    {
        if (!TryComp<UseDelayComponent>(uid, out var useDelay) ||
            !_useDelay.IsDelayed((uid, useDelay), WieldDelayId))
        {
            return;
        }

        args.Handled = true;
    }

    private void OnShotAttempted(
        EntityUid uid,
        WieldDelayComponent component,
        ref ShotAttemptedEvent args)
    {
        if (!component.PreventFiring ||
            !TryComp<UseDelayComponent>(uid, out var useDelay))
        {
            return;
        }

        if (_useDelay.IsDelayed((uid, useDelay), WieldDelayId))
            args.Cancel();
    }

    private void StartDelay(
        EntityUid uid,
        WieldDelayComponent component)
    {
        _useDelay.SetLength(uid, component.BaseDelay, WieldDelayId);
        _useDelay.TryResetDelay(uid, id: WieldDelayId);
    }
}
