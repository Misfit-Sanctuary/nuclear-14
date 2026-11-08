// #Misfits Add - Server-side scope system with PVS relay entity (ported from RMC-14, refactored)
using System.Numerics;
using Content.Shared._Misfits.Scope;
using Robust.Server.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._Misfits.Scope;

/// <summary>
/// Server-side scope system. Spawns a relay entity at the scoped offset position so that
/// the PVS loads chunks at the target location for the scoping player. Without this,
/// distant tiles would not be sent to the client when looking far away.
/// </summary>
public sealed class ScopeSystem : SharedScopeSystem
{
    [Dependency] private readonly ViewSubscriberSystem _viewSubscriber = default!;

    /// <summary>
    /// Spawns the PVS relay once scoping has actually completed, so a cancelled or
    /// interrupted DoAfter never leaves an orphaned relay behind.
    /// </summary>
    protected override void OnScopeStarted(Entity<ScopeComponent> scope, EntityUid user, Vector2 offset)
    {
        // A previous session on the same scope may still own a relay.
        DeleteRelay(scope, user);

        if (!TryComp(user, out ActorComponent? actor))
            return;

        var relay = SpawnAtPosition(null, Transform(user).Coordinates.Offset(offset));
        scope.Comp.RelayEntity = relay;
        _viewSubscriber.AddViewSubscriber(relay, actor.PlayerSession);
    }

    protected override void OnScopeEnded(Entity<ScopeComponent> scope, EntityUid user)
    {
        DeleteRelay(scope, user);
    }

    /// <summary>
    /// Removes the relay entity and its view subscription.
    /// </summary>
    protected override void DeleteRelay(Entity<ScopeComponent> scope, EntityUid? user)
    {
        if (scope.Comp.RelayEntity is not { } relay)
            return;

        scope.Comp.RelayEntity = null;

        if (TryComp(user, out ActorComponent? actor))
            _viewSubscriber.RemoveViewSubscriber(relay, actor.PlayerSession);

        if (!TerminatingOrDeleted(relay))
            QueueDel(relay);
    }

    /// <summary>
    /// Repositions the relay when the user changes facing while scoped. The subscription is
    /// per-entity UID, so no re-subscription is needed.
    /// </summary>
    protected override void MoveRelay(Entity<ScopeComponent> scope, Vector2 newOffset)
    {
        if (scope.Comp.RelayEntity is not { } relay || scope.Comp.User is not { } user)
            return;

        _transform.SetCoordinates(relay, Transform(user).Coordinates.Offset(newOffset));
    }
}
