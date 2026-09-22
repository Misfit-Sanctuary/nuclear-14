// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from Goob Station / Trauma Station

using Content.Server._Misfits.Factory.Filters;
using Content.Shared._Misfits.Factory;
using Content.Shared.DeviceLinking;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Factory;

public sealed partial class StorageBinSystem : EntitySystem
{
    [Dependency] private AutomationFilterSystem _filter = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedDeviceLinkSystem _device = default!;

    public const string ContainerId = "storagebase";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StorageBinComponent, ContainerIsInsertingAttemptEvent>(OnInsertAttempt);
        SubscribeLocalEvent<StorageBinComponent, EntInsertedIntoContainerMessage>(OnEntInserted);
        SubscribeLocalEvent<StorageBinComponent, EntRemovedFromContainerMessage>(OnEntRemoved);
    }

    private void OnInsertAttempt(Entity<StorageBinComponent> ent, ref ContainerIsInsertingAttemptEvent args)
    {
        if (args.Container.ID != ContainerId || _timing.ApplyingState)
            return;

        if (_filter.IsBlocked(_filter.GetSlot(ent), args.EntityUid))
            args.Cancel();
    }

    private void OnEntInserted(Entity<StorageBinComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != ContainerId)
            return;

        _device.InvokePort(ent.Owner, ent.Comp.InsertedPort);
    }

    private void OnEntRemoved(Entity<StorageBinComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != ContainerId)
            return;

        _device.InvokePort(ent.Owner, ent.Comp.RemovedPort);
    }
}
