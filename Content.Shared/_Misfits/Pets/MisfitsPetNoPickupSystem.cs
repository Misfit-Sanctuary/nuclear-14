
using Content.Shared._Misfits.Silicon;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Item;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;

namespace Content.Shared._Misfits.Pets;

/// <summary>
/// Prevents pet mobs with <see cref="MisfitsPetNoPickupComponent"/> from picking up weapons.
/// They cannot receive or pick up those, while still able to pick the rest up.
/// </summary>
public sealed class MisfitsPetNoPickupSystem : EntitySystem
{
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MisfitsPetNoPickupComponent,
            PickupAttemptEvent>(OnPickupAttempt);

        SubscribeLocalEvent<MisfitsPetNoPickupComponent,
            ContainerIsInsertingAttemptEvent>(OnContainerInsertAttempt);
    }

    private void OnPickupAttempt(
        EntityUid uid,
        MisfitsPetNoPickupComponent component,
        PickupAttemptEvent args)
    {
        if (TryComp<RobotWeaponPickupBlacklistComponent>(
                uid, out var blacklist) &&
            blacklist.Blacklist != null)
            return;

        args.Cancel();
    }

    private void OnContainerInsertAttempt(
        EntityUid uid,
        MisfitsPetNoPickupComponent component,
        ContainerIsInsertingAttemptEvent args)
    {
        if (!_hands.TryGetHand(uid, args.Container.ID, out _))
            return;

        if (TryComp<RobotWeaponPickupBlacklistComponent>(
                uid, out var blacklist) &&
            blacklist.Blacklist != null)
        {
            if (_whitelist.IsBlacklistPass(
                    blacklist.Blacklist, args.EntityUid))
                args.Cancel();

            return;
        }

        args.Cancel();
    }
}
