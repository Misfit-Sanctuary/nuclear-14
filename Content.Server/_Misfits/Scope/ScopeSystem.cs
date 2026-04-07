// #Misfits Add - Server-side scope system with PVS relay entity (ported from RMC-14)
using System.Numerics;
using Content.Shared._Misfits.Scope;
using Robust.Server.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._Misfits.Scope;

/// <summary>
/// Server-side scope system. Spawns a relay entity at the scoped offset position
/// so that the PVS system loads chunks at the target location for the scoping player.
/// Without this, distant tiles would not be sent to the client when looking far away.
/// </summary>
public sealed class ScopeSystem : SharedScopeSystem
{
    [Dependency] private readonly ViewSubscriberSystem _viewSubscriber = default!;

    /// <summary>
    /// Overrides shared StartScoping to spawn a relay entity at the offset position
    /// and subscribe the player's viewport to it for PVS chunk loading.
    /// </summary>
    public override Direction? StartScoping(Entity<ScopeComponent> scope, EntityUid user)
    {
        if (base.StartScoping(scope, user) is not { } direction)
            return null;

        scope.Comp.User = user;

        // Spawn invisible relay entity at the offset position for PVS
        if (TryComp(user, out ActorComponent? actor))
        {
            var coords = Transform(user).Coordinates;
            var offset = GetScopeOffset(scope, direction);

            // Spawn a blank entity at the offset — this expands the player's PVS view
            scope.Comp.RelayEntity = SpawnAtPosition(null, coords.Offset(offset));
            _viewSubscriber.AddViewSubscriber(scope.Comp.RelayEntity.Value, actor.PlayerSession);
        }

        return direction;
    }

    /// <summary>
    /// Overrides shared Unscope to clean up the relay entity and remove PVS subscription.
    /// </summary>
    public override bool Unscope(Entity<ScopeComponent> scope)
    {
        var user = scope.Comp.User;

        if (!base.Unscope(scope))
            return false;

        DeleteRelay(scope, user);
        return true;
    }

    /// <summary>
    /// Removes the PVS relay entity and unsubscribes the player's viewport.
    /// </summary>
    protected override void DeleteRelay(Entity<ScopeComponent> scope, EntityUid? user)
    {
        if (scope.Comp.RelayEntity is not { } relay)
            return;

        scope.Comp.RelayEntity = null;

        // Remove PVS subscription before deleting the relay
        if (TryComp(user, out ActorComponent? actor))
            _viewSubscriber.RemoveViewSubscriber(relay, actor.PlayerSession);

        if (!TerminatingOrDeleted(relay))
            QueueDel(relay);
    }

    /// <summary>
    /// Repositions the PVS relay entity when the user changes facing direction while scoped.
    /// PVS subscription is per-entity-UID, so no re-subscription needed — it follows the entity.
    /// </summary>
    protected override void MoveRelay(Entity<ScopeComponent> scope, Vector2 newOffset)
    {
        if (scope.Comp.RelayEntity is not { } relay)
            return;

        if (scope.Comp.User is not { } user)
            return;

        // Reposition the relay at the new offset relative to the user
        var userCoords = Transform(user).Coordinates;
        _transform.SetCoordinates(relay, userCoords.Offset(newOffset));
    }
}
