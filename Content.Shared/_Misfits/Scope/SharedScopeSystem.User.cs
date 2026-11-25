// #Misfits Add - User-side scoping handlers (movement, stun, knockdown, pull, mob state)
using System.Numerics;
using Content.Shared.Camera;
using Content.Shared.Mobs;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Stunnable;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Shared._Misfits.Scope;

/// <summary>
/// Handles events on the <see cref="ScopingComponent"/> applied to the user. Every condition
/// that should break scoping movement, stun, knockdown, being pulled, entering a container,
/// disconnecting or dying  funnels into <see cref="UserStopScoping"/>.
/// </summary>
public partial class SharedScopeSystem
{
    private void InitializeUser()
    {
        SubscribeLocalEvent<ScopingComponent, ComponentRemove>(OnRemove);
        SubscribeLocalEvent<ScopingComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ScopingComponent, MoveInputEvent>(OnMoveInput);
        SubscribeLocalEvent<ScopingComponent, PullStartedMessage>(OnPullStarted);
        SubscribeLocalEvent<ScopingComponent, EntParentChangedMessage>(OnParentChanged);
        SubscribeLocalEvent<ScopingComponent, ContainerGettingInsertedAttemptEvent>(OnInsertAttempt);
        SubscribeLocalEvent<ScopingComponent, EntityTerminatingEvent>(OnEntityTerminating);
        SubscribeLocalEvent<ScopingComponent, GetEyeOffsetEvent>(OnGetEyeOffset);
        SubscribeLocalEvent<ScopingComponent, PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<ScopingComponent, KnockedDownEvent>(OnKnockedDown);
        SubscribeLocalEvent<ScopingComponent, StunnedEvent>(OnStunned);
        SubscribeLocalEvent<ScopingComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    /// <summary>
    /// Scoping was removed without going through <see cref="Unscope"/>
    /// </summary>
    private void OnRemove(Entity<ScopingComponent> user, ref ComponentRemove args)
    {
        // Removal is the last chance to drop this system's eye offset contribution.
        if (!TerminatingOrDeleted(user))
        {
            user.Comp.EyeOffset = Vector2.Zero;
            UpdateOffset(user);
        }

        // The component pair is written together, so the scope this user names is the one to
        // release; Unscope returns straight away if it has already been released.
        if (user.Comp.Scope is { } scopeUid && TryComp<ScopeComponent>(scopeUid, out var scope))
        {
            DebugTools.Assert(scope.User is null || scope.User == user,
                "ScopingComponent points at a scope that is in use by someone else.");

            Unscope((scopeUid, scope));
        }
    }

    /// <summary>
    /// Once the component is gone, its eye offset is no longer contributed by
    /// <see cref="OnGetEyeOffset"/>, so recompute the user's total offset.
    /// </summary>
    private void OnShutdown(Entity<ScopingComponent> user, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(user))
            return;

        // The component can still be dispatched GetEyeOffsetEvent while it shuts down, so drop
        // its contribution before the total is recomputed.
        user.Comp.EyeOffset = Vector2.Zero;
        UpdateOffset(user);
    }

    /// <summary>
    /// Movement input breaks scoping unless the zoom level allows movement.
    /// </summary>
    private void OnMoveInput(Entity<ScopingComponent> ent, ref MoveInputEvent args)
    {
        if (!args.HasDirectionalMovement)
            return;

        if (!ent.Comp.AllowMovement)
            UserStopScoping(ent);
    }

    /// <summary>
    /// Being pulled by another entity breaks scoping.
    /// </summary>
    private void OnPullStarted(Entity<ScopingComponent> ent, ref PullStartedMessage args)
    {
        if (args.PulledUid != ent.Owner)
            return;

        UserStopScoping(ent);
    }

    /// <summary>
    /// Parent change (e.g. entering a vehicle) breaks scoping.
    /// </summary>
    private void OnParentChanged(Entity<ScopingComponent> ent, ref EntParentChangedMessage args)
    {
        UserStopScoping(ent);
    }

    /// <summary>
    /// Being inserted into a container breaks scoping.
    /// </summary>
    private void OnInsertAttempt(Entity<ScopingComponent> ent, ref ContainerGettingInsertedAttemptEvent args)
    {
        UserStopScoping(ent);
    }

    /// <summary>
    /// Entity being deleted breaks scoping.
    /// </summary>
    private void OnEntityTerminating(Entity<ScopingComponent> ent, ref EntityTerminatingEvent args)
    {
        UserStopScoping(ent);
    }

    /// <summary>
    /// Provides this system's eye offset additively when the engine queries the total offset.
    /// </summary>
    private void OnGetEyeOffset(Entity<ScopingComponent> ent, ref GetEyeOffsetEvent args)
    {
        args.Offset += ent.Comp.EyeOffset;
    }

    /// <summary>
    /// Player disconnecting breaks scoping.
    /// </summary>
    private void OnPlayerDetached(Entity<ScopingComponent> ent, ref PlayerDetachedEvent args)
    {
        UserStopScoping(ent);
    }

    /// <summary>
    /// Being knocked down breaks scoping.
    /// </summary>
    private void OnKnockedDown(Entity<ScopingComponent> ent, ref KnockedDownEvent args)
    {
        UserStopScoping(ent);
    }

    /// <summary>
    /// Being stunned breaks scoping.
    /// </summary>
    private void OnStunned(Entity<ScopingComponent> ent, ref StunnedEvent args)
    {
        UserStopScoping(ent);
    }

    /// <summary>
    /// Entering crit or dead state breaks scoping.
    /// </summary>
    private void OnMobStateChanged(Entity<ScopingComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive)
            return;

        UserStopScoping(ent);
    }

    /// <summary>
    /// Cleanly stops scoping from the user side removes <see cref="ScopingComponent"/> and
    /// calls <see cref="Unscope"/> on the scope entity.
    /// </summary>
    private void UserStopScoping(Entity<ScopingComponent> ent)
    {
        var scope = ent.Comp.Scope;
        RemCompDeferred<ScopingComponent>(ent);

        // The component pair is written together, so the scope this user names is the one to
        // release; Unscope returns straight away if it has already been released.
        if (scope is not { } scopeUid ||
            !TryComp(scopeUid, out ScopeComponent? scopeComponent))
        {
            return;
        }

        DebugTools.Assert(scopeComponent.User is null || scopeComponent.User == ent,
            "ScopingComponent points at a scope that is in use by someone else.");

        Unscope((scopeUid, scopeComponent));
    }
}
