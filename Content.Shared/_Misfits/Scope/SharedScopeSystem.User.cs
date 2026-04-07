// #Misfits Add - User-side scoping handlers (movement, stun, knockdown, pull, mob state)
using Content.Shared.Camera;
using Content.Shared.Mobs;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Stunnable;
using Robust.Shared.Containers;
using Robust.Shared.Player;

namespace Content.Shared._Misfits.Scope;

/// <summary>
/// Partial class handling events on the ScopingComponent (applied to the user).
/// Covers all conditions that should break scoping — movement, stun, knockdown,
/// being pulled, entering a container, disconnecting, or dying.
/// </summary>
public partial class SharedScopeSystem
{
    private void InitializeUser()
    {
        SubscribeLocalEvent<ScopingComponent, ComponentRemove>(OnRemove);
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
    /// When the ScopingComponent is removed, update the eye offset so the viewport resets.
    /// </summary>
    private void OnRemove(Entity<ScopingComponent> user, ref ComponentRemove args)
    {
        if (!TerminatingOrDeleted(user))
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
    /// Provides this system's eye offset additively when the engine queries total offset.
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
    /// Cleanly stops scoping from the user side — removes ScopingComponent
    /// and calls Unscope on the scope entity.
    /// </summary>
    private void UserStopScoping(Entity<ScopingComponent> ent)
    {
        var scope = ent.Comp.Scope;
        RemCompDeferred<ScopingComponent>(ent);

        if (TryComp(scope, out ScopeComponent? scopeComponent) && scopeComponent.User == ent)
            Unscope((scope.Value, scopeComponent));
    }
}
