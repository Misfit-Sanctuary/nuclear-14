using Content.Shared.Movement.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._Misfits.Warcry;

/// <summary>
/// Shared system that applies warcry speed modifiers on both client and server,
/// ensuring client-side movement prediction correctly accounts for the buff.
/// </summary>
public sealed class SharedWarcryBuffSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WarcryBuffComponent, ComponentStartup>(OnBuffStartup);
        SubscribeLocalEvent<WarcryBuffComponent, ComponentRemove>(OnBuffRemove);
        SubscribeLocalEvent<WarcryBuffComponent, RefreshMovementSpeedModifiersEvent>(OnBuffRefreshSpeed);
    }

    private void OnBuffStartup(EntityUid uid, WarcryBuffComponent component, ComponentStartup args)
    {
        _movementSpeed.RefreshMovementSpeedModifiers(uid);
    }

    private void OnBuffRemove(EntityUid uid, WarcryBuffComponent component, ComponentRemove args)
    {
        if (TerminatingOrDeleted(uid))
            return;

        // Refresh speed so the modifier is removed.
        // The handler below will skip applying the modifier because ExpiresAt <= CurTime.
        _movementSpeed.RefreshMovementSpeedModifiers(uid);
    }

    private void OnBuffRefreshSpeed(EntityUid uid, WarcryBuffComponent component, RefreshMovementSpeedModifiersEvent args)
    {
        // #Misfits Fix - During ComponentRemove the component is still registered,
        // so this handler fires even when the buff is being removed. Check expiry
        // to avoid re-applying the speed bonus on removal. ExpiresAt == Zero means
        // the component was just created and the server hasn't set the expiry yet,
        // so treat it as active.
        if (component.ExpiresAt != TimeSpan.Zero && component.ExpiresAt <= _timing.CurTime)
            return;

        var speedModifier = 1f + component.SpeedBonus;
        args.ModifySpeed(speedModifier, speedModifier);
    }
}
