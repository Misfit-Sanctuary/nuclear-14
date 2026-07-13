using Content.Shared.Movement.Systems;

namespace Content.Shared._Misfits.C27;

public sealed class SharedZaxC27RepairModeSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ZaxC27RepairModeActiveComponent, RefreshMovementSpeedModifiersEvent>(OnActiveRefreshSpeed);
        SubscribeLocalEvent<ZaxC27RepairModeTransitionComponent, RefreshMovementSpeedModifiersEvent>(OnTransitionRefreshSpeed);
    }

    private void OnActiveRefreshSpeed(EntityUid uid, ZaxC27RepairModeActiveComponent component,
        RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(0f, 0f, bypassImmunity: true);
    }

    private void OnTransitionRefreshSpeed(EntityUid uid, ZaxC27RepairModeTransitionComponent component,
        RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(0f, 0f, bypassImmunity: true);
    }
}
