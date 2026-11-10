// #Misfits Add - Core shared scoping system (ported from RMC-14 Scoping)
using System.Numerics;
using Content.Shared.Actions;
using Content.Shared.Camera;
using Content.Shared.DoAfter;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Containers;

namespace Content.Shared._Misfits.Scope;

/// Directional scoping for any item with a ScopeComponent; the system is split into core (this file), actions and user partials.
public abstract partial class SharedScopeSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedContentEyeSystem _contentEye = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedEyeSystem _eye = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;
    [Dependency] protected readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        InitializeUser();
        InitializeActions();

        // Scope lifecycle
        SubscribeLocalEvent<ScopeComponent, ComponentRemove>(OnScopeShutdown);
        SubscribeLocalEvent<ScopeComponent, EntityTerminatingEvent>(OnScopeTerminating);

        // Unscope when dropping or deselecting the scoped item
        SubscribeLocalEvent<ScopeComponent, GotUnequippedHandEvent>(OnScopeUnequipped);
        SubscribeLocalEvent<ScopeComponent, HandDeselectedEvent>(OnScopeDeselected);
        SubscribeLocalEvent<ScopeComponent, ItemUnwieldedEvent>(OnScopeUnwielded);

        // In-hand activation (binoculars)
        SubscribeLocalEvent<ScopeComponent, ActivateInWorldEvent>(OnActivateInWorld);

        // DoAfter completion
        SubscribeLocalEvent<ScopeComponent, ScopeDoAfterEvent>(OnScopeDoAfter);
    }

    /// Redirects an active scope whenever the user's facing changes.
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ScopingComponent>();
        while (query.MoveNext(out var user, out var scoping))
        {
            if (scoping.Scope is not { } scopeUid ||
                !TryComp<ScopeComponent>(scopeUid, out var scopeComp) ||
                scopeComp.ScopingDirection is not { } oldDirection)
            {
                continue;
            }

            if (TerminatingOrDeleted(user) || TerminatingOrDeleted(scopeUid))
                continue;

            var currentDirection = _transform.GetWorldRotation(user).GetCardinalDir();
            if (currentDirection == oldDirection)
                continue;

            // Direction changed; instantly redirect the scope view.
            RedirectScope(user, (scopeUid, scopeComp), (user, scoping), currentDirection);
        }
    }

    /// Instantly redirects an active scope to a new cardinal direction, without rescoping or another DoAfter.
    protected void RedirectScope(
        EntityUid user,
        Entity<ScopeComponent> scope,
        Entity<ScopingComponent> scoping,
        Direction newDirection)
    {
        scope.Comp.ScopingDirection = newDirection;

        var newOffset = GetScopeOffset(scope, newDirection);
        scoping.Comp.EyeOffset = newOffset;

        UpdateOffset(user);

        Dirty(scope);
        Dirty(scoping);

        // Let the server reposition the PVS relay entity.
        MoveRelay(scope, newOffset);
    }

    #region Hooks

    /// Virtual hook for the server to spawn the PVS relay after scoping succeeds.
    protected virtual void OnScopeStarted(Entity<ScopeComponent> scope, EntityUid user, Vector2 offset)
    {
    }

    /// Virtual hook for the server to clean up the PVS relay when scoping ends.
    protected virtual void OnScopeEnded(Entity<ScopeComponent> scope, EntityUid user)
    {
    }

    /// Virtual hook for the server to reposition the PVS relay when the scope direction changes.
    protected virtual void MoveRelay(Entity<ScopeComponent> scope, Vector2 newOffset)
    {
    }

    /// Virtual hook for the server to delete the PVS relay.
    protected virtual void DeleteRelay(Entity<ScopeComponent> scope, EntityUid? user)
    {
    }

    #endregion

    #region Scope Lifecycle

    private void OnScopeShutdown(Entity<ScopeComponent> scope, ref ComponentRemove args)
    {
        if (scope.Comp.User is not { } user)
            return;

        Unscope(scope);

        if (!TerminatingOrDeleted(user) && !TerminatingOrDeleted(scope.Owner))
            _actions.RemoveProvidedActions(user, scope.Owner);
    }

    private void OnScopeTerminating(Entity<ScopeComponent> scope, ref EntityTerminatingEvent args)
    {
        Unscope(scope);
    }

    #endregion

    #region Hand Events

    private void OnScopeUnequipped(Entity<ScopeComponent> scope, ref GotUnequippedHandEvent args)
    {
        Unscope(scope);
    }

    private void OnScopeDeselected(Entity<ScopeComponent> scope, ref HandDeselectedEvent args)
    {
        Unscope(scope);
    }

    private void OnScopeUnwielded(Entity<ScopeComponent> scope, ref ItemUnwieldedEvent args)
    {
        if (scope.Comp.RequireWielding)
            Unscope(scope);
    }

    private void OnActivateInWorld(Entity<ScopeComponent> scope, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex || !scope.Comp.UseInHand)
            return;

        args.Handled = true;
        ToggleScoping(scope, args.User);
    }

    #endregion

    #region DoAfter

    private void OnScopeDoAfter(Entity<ScopeComponent> scope, ref ScopeDoAfterEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (args.Cancelled || !CanScope(scope, args.User, popup: true))
            return;

        ScopeIn(scope, args.User);
    }

    #endregion

    #region Core Scoping Logic

    /// Validates that the user can scope and starts the DoAfter; returns true if it started.
    public virtual bool StartScoping(Entity<ScopeComponent> scope, EntityUid user)
    {
        if (HasComp<ScopingComponent>(user) || !CanScope(scope, user, popup: true))
            return false;

        var zoomLevel = GetZoomLevel(scope);
        var doAfter = new DoAfterArgs(EntityManager, user, zoomLevel.DoAfter, new ScopeDoAfterEvent(), scope, null, scope)
        {
            BreakOnMove = !zoomLevel.AllowMovement,
        };

        return _doAfter.TryStartDoAfter(doAfter);
    }

    /// Completes scoping: applies the eye offset, zoom and scope state.
    private void ScopeIn(Entity<ScopeComponent> scope, EntityUid user)
    {
        if (TerminatingOrDeleted(user) || TerminatingOrDeleted(scope.Owner))
            return;

        // If the user is already scoping something else, stop that first.
        if (TryComp(user, out ScopingComponent? existing))
            UserStopScoping((user, existing));

        // That removal is deferred, so clear it now to keep the fresh session state below alive.
        if (HasComp<ScopingComponent>(user))
            RemComp<ScopingComponent>(user);

        var direction = _transform.GetWorldRotation(user).GetCardinalDir();
        var zoomLevel = GetZoomLevel(scope);

        scope.Comp.User = user;
        scope.Comp.ScopingDirection = direction;

        var scoping = EnsureComp<ScopingComponent>(user);
        scoping.Scope = scope;
        scoping.AllowMovement = zoomLevel.AllowMovement;

        var offset = GetScopeOffset(scope, direction);
        scoping.EyeOffset = offset;

        Dirty(scope);
        Dirty(user, scoping);

        if (scope.Comp.ScopePopup != null)
            PopupUser(scope, user, scope.Comp.ScopePopup);

        _actions.SetToggled(scope.Comp.ScopingToggleActionEntity, true);
        _contentEye.SetZoom(user, Vector2.One * zoomLevel.Zoom, true);
        UpdateOffset(user);

        OnScopeStarted(scope, user, offset);

        var ev = new ScopedEvent(user, scope);
        RaiseLocalEvent(user, ref ev);
    }

    /// Removes scoping state from the scope and the user.
    public virtual bool Unscope(Entity<ScopeComponent> scope)
    {
        if (scope.Comp.User is not { } user)
            return false;

        OnScopeEnded(scope, user);

        // Drop the eye offset now, before the deferred removal, so the view resets on this frame.
        ClearEyeOffset(user);

        RemCompDeferred<ScopingComponent>(user);

        scope.Comp.User = null;
        scope.Comp.ScopingDirection = null;

        if (!TerminatingOrDeleted(scope.Owner))
            Dirty(scope);

        if (scope.Comp.UnScopePopup != null && !TerminatingOrDeleted(user))
            PopupUser(scope, user, scope.Comp.UnScopePopup);

        _actions.SetToggled(scope.Comp.ScopingToggleActionEntity, false);
        _contentEye.ResetZoom(user);
        return true;
    }

    private void ToggleScoping(Entity<ScopeComponent> scope, EntityUid user)
    {
        if (scope.Comp.User == user)
        {
            Unscope(scope);
            return;
        }

        if (TryComp(user, out ScopingComponent? scoping))
        {
            UserStopScoping((user, scoping));
            return;
        }

        StartScoping(scope, user);
    }

    #endregion

    #region Validation

    /// Checks every condition required to scope, optionally showing a popup; also used on DoAfter completion.
    public bool CanScope(Entity<ScopeComponent> scope, EntityUid user, bool popup = false)
    {
        // The scoped item must be the active hand item.
        if (!_hands.TryGetActiveItem(user, out var held) || held != scope.Owner)
            return Fail(user, "n14-action-popup-scoping-user-must-hold", scope.Owner, popup);

        if (_pulling.IsPulled(user))
            return Fail(user, "n14-action-popup-scoping-user-must-not-pulled", scope.Owner, popup);

        if (_container.IsEntityInContainer(user))
            return Fail(user, "n14-action-popup-scoping-user-must-not-contained", scope.Owner, popup);

        if (scope.Comp.RequireWielding &&
            TryComp(scope.Owner, out WieldableComponent? wieldable) &&
            !wieldable.Wielded)
        {
            return Fail(user, "n14-action-popup-scoping-user-must-wield", scope.Owner, popup);
        }

        return true;
    }

    private bool Fail(EntityUid user, string message, EntityUid target, bool popup)
    {
        if (popup)
            _popup.PopupClient(Loc.GetString(message, ("scope", target)), user, user);

        return false;
    }

    #endregion

    #region Queries

    /// Whether the entity is currently looking through a scope.
    public bool IsScoped(EntityUid user)
    {
        return HasComp<ScopingComponent>(user);
    }

    /// Resolves the scope an entity is currently using, if any.
    public bool TryGetActiveScope(EntityUid user, out Entity<ScopeComponent> scope)
    {
        scope = default;

        if (!TryComp(user, out ScopingComponent? scoping) || scoping.Scope is not { } scopeUid)
            return false;

        if (!TryComp<ScopeComponent>(scopeUid, out var scopeComp))
            return false;

        scope = (scopeUid, scopeComp);
        return true;
    }

    #endregion

    #region Helpers

    /// Eye offset for a scope and direction; the offset is centered on the scaled viewport.
    protected Vector2 GetScopeOffset(Entity<ScopeComponent> scope, Direction direction)
    {
        var zoomLevel = GetZoomLevel(scope);
        return direction.ToVec() * ((zoomLevel.Offset * zoomLevel.Zoom - 1) / 2);
    }

    /// Returns the current zoom level, repairing an empty or out-of-range selection.
    public ScopeZoomLevel GetZoomLevel(Entity<ScopeComponent> scope)
    {
        var levels = scope.Comp.ZoomLevels;

        if (levels.Count == 0)
        {
            levels.Add(new ScopeZoomLevel(null, 1f, 15, false, TimeSpan.FromSeconds(1)));
            scope.Comp.CurrentZoomLevel = 0;
            Dirty(scope);
        }
        else if (scope.Comp.CurrentZoomLevel < 0 || scope.Comp.CurrentZoomLevel >= levels.Count)
        {
            scope.Comp.CurrentZoomLevel = Math.Clamp(scope.Comp.CurrentZoomLevel, 0, levels.Count - 1);
            Dirty(scope);
        }

        return levels[scope.Comp.CurrentZoomLevel];
    }

    private void PopupUser(Entity<ScopeComponent> scope, EntityUid user, string message)
    {
        _popup.PopupClient(Loc.GetString(message, ("scope", scope.Owner)), user, user);
    }

    /// Zeroes this system's eye offset contribution and recomputes the rest; call while ScopingComponent still exists.
    private void ClearEyeOffset(EntityUid user)
    {
        if (TerminatingOrDeleted(user))
            return;

        if (TryComp(user, out ScopingComponent? scoping))
        {
            scoping.EyeOffset = Vector2.Zero;
            Dirty(user, scoping);
        }

        UpdateOffset(user);
    }

    /// Re-raises GetEyeOffsetEvent on the user to recompute the total eye offset.
    private void UpdateOffset(EntityUid user)
    {
        var ev = new GetEyeOffsetEvent();
        RaiseLocalEvent(user, ref ev);
        _eye.SetOffset(user, ev.Offset);
    }

    #endregion
}
