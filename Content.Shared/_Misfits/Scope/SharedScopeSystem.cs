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
using Content.Shared.Toggleable;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Containers;

namespace Content.Shared._Misfits.Scope;

/// <summary>
/// Directional scoping system — when activated, shifts the player's viewport
/// in the cardinal direction they are facing, with configurable zoom and DoAfter delay.
/// Ported from RMC-14's Scoping system, adapted for N14 conventions.
/// </summary>
public abstract partial class SharedScopeSystem : EntitySystem
{
    [Dependency] private readonly ActionContainerSystem _actionContainer = default!;
    [Dependency] private readonly SharedActionsSystem _actionsSystem = default!;
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
        // User-side handlers (movement, stun, knockdown, etc.)
        InitializeUser();

        // Scope item lifecycle
        SubscribeLocalEvent<ScopeComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ScopeComponent, ComponentRemove>(OnShutdown);
        SubscribeLocalEvent<ScopeComponent, EntityTerminatingEvent>(OnScopeEntityTerminating);

        // Unscope when dropping or deselecting the item
        SubscribeLocalEvent<ScopeComponent, GotUnequippedHandEvent>(OnUnequip);
        SubscribeLocalEvent<ScopeComponent, HandDeselectedEvent>(OnDeselectHand);

        // Unwield check (if scope requires wielding)
        SubscribeLocalEvent<ScopeComponent, ItemUnwieldedEvent>(OnUnwielded);

        // Action grants
        SubscribeLocalEvent<ScopeComponent, GetItemActionsEvent>(OnGetActions);

        // Toggle and cycle actions
        SubscribeLocalEvent<ScopeComponent, ToggleActionEvent>(OnToggleAction);
        SubscribeLocalEvent<ScopeComponent, ScopeCycleZoomLevelEvent>(OnCycleZoomLevel);

        // In-hand activation (binoculars)
        SubscribeLocalEvent<ScopeComponent, ActivateInWorldEvent>(OnActivateInWorld);

        // Shooting while scoped — unscope if direction changed
        SubscribeLocalEvent<ScopeComponent, GunShotEvent>(OnGunShot);

        // DoAfter completion
        SubscribeLocalEvent<ScopeComponent, ScopeDoAfterEvent>(OnScopeDoAfter);

        // GunScopingComponent handlers (when scope is an attachment inside a gun)
        SubscribeLocalEvent<GunScopingComponent, GotUnequippedHandEvent>(OnGunUnequip);
        SubscribeLocalEvent<GunScopingComponent, HandDeselectedEvent>(OnGunDeselectHand);
        SubscribeLocalEvent<GunScopingComponent, ItemUnwieldedEvent>(OnGunUnwielded);
        SubscribeLocalEvent<GunScopingComponent, GunShotEvent>(OnGunGunShot);
    }

    /// <summary>
    /// Checks all scoping users each frame — if their facing direction changed,
    /// instantly redirect the scope view to the new cardinal direction.
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ScopingComponent>();
        while (query.MoveNext(out var userUid, out var scopingComp))
        {
            // Guard: must have a valid scope reference
            if (scopingComp.Scope is not { } scopeUid)
                continue;

            if (!TryComp<ScopeComponent>(scopeUid, out var scopeComp))
                continue;

            // Guard: must already have a direction set (scoping is active)
            if (scopeComp.ScopingDirection is not { } oldDir)
                continue;

            var currentDir = _transform.GetWorldRotation(userUid).GetCardinalDir();
            if (currentDir == oldDir)
                continue;

            // Direction changed — instantly redirect the scope view
            RedirectScope(userUid, (scopeUid, scopeComp), (userUid, scopingComp), currentDir);
        }
    }

    /// <summary>
    /// Instantly redirects an active scope to a new cardinal direction
    /// without requiring unscope/rescope or a DoAfter delay.
    /// </summary>
    protected void RedirectScope(
        EntityUid user,
        Entity<ScopeComponent> scope,
        Entity<ScopingComponent> scoping,
        Direction newDirection)
    {
        // Update stored direction on the scope
        scope.Comp.ScopingDirection = newDirection;

        // Recalculate eye offset for the new direction
        var newOffset = GetScopeOffset(scope, newDirection);
        scoping.Comp.EyeOffset = newOffset;

        // Push the new offset to the eye system
        UpdateOffset(user);

        // Network sync
        Dirty(scope);
        Dirty(scoping);

        // Let server reposition the PVS relay entity
        MoveRelay(scope, newOffset);
    }

    /// <summary>
    /// Virtual hook for server to reposition the PVS relay entity when scope direction changes.
    /// </summary>
    protected virtual void MoveRelay(Entity<ScopeComponent> scope, Vector2 newOffset)
    {
        // No-op in shared; overridden on server to reposition the relay entity.
    }

    #region Scope Lifecycle

    private void OnMapInit(Entity<ScopeComponent> ent, ref MapInitEvent args)
    {
        // Ensure the toggle action is stored in the action container
        if (ent.Comp.ScopingToggleAction != null)
            _actionContainer.EnsureAction(ent.Owner, ref ent.Comp.ScopingToggleActionEntity, ent.Comp.ScopingToggleAction);

        // Only add cycle action if multiple zoom levels exist
        if (ent.Comp.ZoomLevels.Count > 1)
            _actionContainer.EnsureAction(ent.Owner, ref ent.Comp.CycleZoomLevelActionEntity, ent.Comp.CycleZoomLevelAction);

        Dirty(ent.Owner, ent.Comp);
    }

    private void OnShutdown(Entity<ScopeComponent> ent, ref ComponentRemove args)
    {
        if (ent.Comp.User is not { } user)
            return;

        Unscope(ent);
        _actionsSystem.RemoveProvidedActions(user, ent.Owner);
    }

    private void OnScopeEntityTerminating(Entity<ScopeComponent> ent, ref EntityTerminatingEvent args)
    {
        Unscope(ent);
    }

    #endregion

    #region Hand Events

    private void OnUnequip(Entity<ScopeComponent> ent, ref GotUnequippedHandEvent args)
    {
        Unscope(ent);
    }

    private void OnDeselectHand(Entity<ScopeComponent> ent, ref HandDeselectedEvent args)
    {
        Unscope(ent);
    }

    private void OnUnwielded(Entity<ScopeComponent> ent, ref ItemUnwieldedEvent args)
    {
        if (ent.Comp.RequireWielding)
            Unscope(ent);
    }

    #endregion

    #region Actions

    private void OnGetActions(Entity<ScopeComponent> ent, ref GetItemActionsEvent args)
    {
        if (ent.Comp.ScopingToggleAction != null)
            args.AddAction(ref ent.Comp.ScopingToggleActionEntity, ent.Comp.ScopingToggleAction);

        if (ent.Comp.ZoomLevels.Count > 1)
            args.AddAction(ref ent.Comp.CycleZoomLevelActionEntity, ent.Comp.CycleZoomLevelAction);
    }

    private void OnToggleAction(Entity<ScopeComponent> ent, ref ToggleActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        ToggleScoping(ent, args.Performer);
    }

    private void OnCycleZoomLevel(Entity<ScopeComponent> scope, ref ScopeCycleZoomLevelEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        // Cycle to the next zoom level, wrapping around
        if (scope.Comp.CurrentZoomLevel >= scope.Comp.ZoomLevels.Count - 1)
            scope.Comp.CurrentZoomLevel = 0;
        else
            ++scope.Comp.CurrentZoomLevel;

        var zoomLevel = GetCurrentZoomLevel(scope);
        if (zoomLevel.Name != null)
        {
            _popup.PopupClient(
                Loc.GetString("n14-action-popup-scope-cycle-zoom", ("zoom", zoomLevel.Name)),
                args.Performer, args.Performer);
        }

        Dirty(scope);
    }

    private void OnActivateInWorld(Entity<ScopeComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex || !ent.Comp.UseInHand)
            return;

        args.Handled = true;
        ToggleScoping(ent, args.User);
    }

    #endregion

    #region Gun Shot

    private void OnGunShot(Entity<ScopeComponent> ent, ref GunShotEvent args)
    {
        // #Misfits Removed - direction change now handled live in Update() via RedirectScope.
        // Previously unscoped the user if their current cardinal direction differed from ScopingDirection.
    }

    #endregion

    #region DoAfter

    private void OnScopeDoAfter(Entity<ScopeComponent> ent, ref ScopeDoAfterEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (args.Cancelled)
        {
            DeleteRelay(ent, args.User);
            return;
        }

        var user = args.User;
        if (!CanScopePopup(ent, user))
        {
            DeleteRelay(ent, args.User);
            return;
        }

        ScopeIn(ent, user, args.Direction);
    }

    #endregion

    #region GunScopingComponent Handlers

    private void OnGunUnequip(Entity<GunScopingComponent> ent, ref GotUnequippedHandEvent args)
    {
        UnscopeGun(ent);
    }

    private void OnGunDeselectHand(Entity<GunScopingComponent> ent, ref HandDeselectedEvent args)
    {
        UnscopeGun(ent);
    }

    private void OnGunUnwielded(Entity<GunScopingComponent> ent, ref ItemUnwieldedEvent args)
    {
        UnscopeGun(ent);
    }

    private void OnGunGunShot(Entity<GunScopingComponent> ent, ref GunShotEvent args)
    {
        // #Misfits Removed - direction change now handled live in Update() via RedirectScope.
        // Previously unscoped if the gun user's direction differed from ScopingDirection.
    }

    #endregion

    #region Core Scoping Logic

    /// <summary>
    /// Validates that the user can scope, then starts a DoAfter in the user's facing direction.
    /// </summary>
    public virtual Direction? StartScoping(Entity<ScopeComponent> scope, EntityUid user)
    {
        if (!CanScopePopup(scope, user))
            return null;

        var cardinalDir = _transform.GetWorldRotation(user).GetCardinalDir();
        var ev = new ScopeDoAfterEvent(cardinalDir);
        var zoomLevel = GetCurrentZoomLevel(scope);
        var doAfter = new DoAfterArgs(EntityManager, user, zoomLevel.DoAfter, ev, scope, null, scope)
        {
            BreakOnMove = !zoomLevel.AllowMovement
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            return cardinalDir;

        return null;
    }

    /// <summary>
    /// Completes the scoping process — applies eye offset, zoom, and scope state.
    /// </summary>
    private void ScopeIn(Entity<ScopeComponent> scope, EntityUid user, Direction direction)
    {
        // If user is already scoping something else, unscope that first
        if (TryComp(user, out ScopingComponent? scoping))
            UserStopScoping((user, scoping));

        var zoomLevel = GetCurrentZoomLevel(scope);

        // Update scope state
        scope.Comp.User = user;
        scope.Comp.ScopingDirection = direction;
        Dirty(scope);

        // Apply ScopingComponent to user
        scoping = EnsureComp<ScopingComponent>(user);
        scoping.Scope = scope;
        scoping.AllowMovement = zoomLevel.AllowMovement;
        Dirty(user, scoping);

        // If this is a scope attachment inside a gun, mark the gun too
        if (scope.Comp.Attachment && TryGetActiveEntity(scope, out var active))
        {
            var gunScoping = EnsureComp<GunScopingComponent>(active);
            gunScoping.Scope = scope;
            Dirty(active, gunScoping);
        }

        // Calculate and apply eye offset in the scoped direction
        var targetOffset = GetScopeOffset(scope, direction);
        scoping.EyeOffset = targetOffset;

        // Show popup
        if (scope.Comp.ScopePopup != null)
        {
            var msgUser = Loc.GetString(scope.Comp.ScopePopup, ("scope", scope.Owner));
            _popup.PopupClient(msgUser, user, user);
        }

        // Set action toggle state and apply zoom
        _actionsSystem.SetToggled(scope.Comp.ScopingToggleActionEntity, true);
        _contentEye.SetZoom(user, Vector2.One * zoomLevel.Zoom, true);
        UpdateOffset(user);

        // Raise event for other systems to react
        var ev = new ScopedEvent(user, scope);
        RaiseLocalEvent(user, ref ev);
    }

    /// <summary>
    /// Removes scoping state from the scope, user, and gun (if attachment).
    /// </summary>
    public virtual bool Unscope(Entity<ScopeComponent> scope)
    {
        if (scope.Comp.User is not { } user)
            return false;

        RemCompDeferred<ScopingComponent>(user);

        // Clean up GunScopingComponent if this was an attachment scope
        if (scope.Comp.Attachment && TryGetActiveEntity(scope, out var active))
            RemCompDeferred<GunScopingComponent>(active);

        scope.Comp.User = null;
        scope.Comp.ScopingDirection = null;
        Dirty(scope);

        // Show unscope popup
        if (scope.Comp.UnScopePopup != null)
        {
            var msgUser = Loc.GetString(scope.Comp.UnScopePopup, ("scope", scope.Owner));
            _popup.PopupClient(msgUser, user, user);
        }

        // Reset action toggle and zoom
        _actionsSystem.SetToggled(scope.Comp.ScopingToggleActionEntity, false);
        _contentEye.ResetZoom(user);
        return true;
    }

    private void UnscopeGun(Entity<GunScopingComponent> gun)
    {
        if (TryComp(gun.Comp.Scope, out ScopeComponent? scope))
            Unscope((gun.Comp.Scope.Value, scope));
    }

    private void ToggleScoping(Entity<ScopeComponent> scope, EntityUid user)
    {
        // If user is already scoping, unscope
        if (HasComp<ScopingComponent>(user))
        {
            Unscope(scope);

            if (TryComp(user, out ScopingComponent? scoping))
                UserStopScoping((user, scoping));

            return;
        }

        StartScoping(scope, user);
    }

    #endregion

    #region Validation

    /// <summary>
    /// Checks all conditions for scoping and shows popup errors if invalid.
    /// </summary>
    private bool CanScopePopup(Entity<ScopeComponent> scope, EntityUid user)
    {
        var ent = scope.Owner;

        // For attachment scopes, resolve to the active (gun) entity
        if (scope.Comp.Attachment)
        {
            if (!TryGetActiveEntity(scope, out var activeEnt))
            {
                var msgError = Loc.GetString("n14-action-popup-scoping-must-attach", ("scope", scope.Owner));
                _popup.PopupClient(msgError, user, user);
                return false;
            }

            ent = activeEnt;
        }

        // Must be held in active hand (unless it's an attachment inside a container)
        if (!_hands.TryGetActiveItem(user, out var heldItem) ||
            (!scope.Comp.Attachment && heldItem != scope.Owner))
        {
            var msgError = Loc.GetString("n14-action-popup-scoping-user-must-hold", ("scope", ent));
            _popup.PopupClient(msgError, user, user);
            return false;
        }

        // Cannot scope while being pulled
        if (_pulling.IsPulled(user))
        {
            var msgError = Loc.GetString("n14-action-popup-scoping-user-must-not-pulled", ("scope", ent));
            _popup.PopupClient(msgError, user, user);
            return false;
        }

        // Cannot scope while inside a container
        if (_container.IsEntityInContainer(user))
        {
            var msgError = Loc.GetString("n14-action-popup-scoping-user-must-not-contained", ("scope", ent));
            _popup.PopupClient(msgError, user, user);
            return false;
        }

        // Must be wielded if required
        if (scope.Comp.RequireWielding &&
            TryComp(ent, out WieldableComponent? wieldable) &&
            !wieldable.Wielded)
        {
            var msgError = Loc.GetString("n14-action-popup-scoping-user-must-wield", ("scope", ent));
            _popup.PopupClient(msgError, user, user);
            return false;
        }

        return true;
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Resolves the "active entity" for an attachment scope.
    /// For non-attachment scopes, this is the scope entity itself.
    /// For attachment scopes, this is the gun (container owner) the scope is inside.
    /// </summary>
    private bool TryGetActiveEntity(Entity<ScopeComponent> scope, out EntityUid active)
    {
        if (!scope.Comp.Attachment)
        {
            active = scope;
            return true;
        }

        if (!_container.TryGetContainingContainer((scope, null), out var container) ||
            !HasComp<GunComponent>(container.Owner))
        {
            active = default;
            return false;
        }

        active = container.Owner;
        return true;
    }

    /// <summary>
    /// Calculates the eye offset vector for a given scope and direction.
    /// </summary>
    protected Vector2 GetScopeOffset(Entity<ScopeComponent> scope, Direction direction)
    {
        var zoomLevel = GetCurrentZoomLevel(scope);
        return direction.ToVec() * ((zoomLevel.Offset * zoomLevel.Zoom - 1) / 2);
    }

    /// <summary>
    /// Virtual hook for server to clean up the relay entity.
    /// </summary>
    protected virtual void DeleteRelay(Entity<ScopeComponent> scope, EntityUid? user)
    {
    }

    private ScopeZoomLevel GetCurrentZoomLevel(Entity<ScopeComponent> scope)
    {
        ValidateCurrentZoomLevel(scope);
        return scope.Comp.ZoomLevels[scope.Comp.CurrentZoomLevel];
    }

    /// <summary>
    /// Ensures the zoom level index is within bounds and provides fallback defaults.
    /// </summary>
    private void ValidateCurrentZoomLevel(Entity<ScopeComponent> scope)
    {
        var dirty = false;

        if (scope.Comp.ZoomLevels.Count <= 0)
        {
            scope.Comp.ZoomLevels = new List<ScopeZoomLevel>
            {
                new(null, 1f, 15, false, TimeSpan.FromSeconds(1))
            };
            dirty = true;
        }

        if (scope.Comp.CurrentZoomLevel >= scope.Comp.ZoomLevels.Count)
        {
            scope.Comp.CurrentZoomLevel = 0;
            dirty = true;
        }

        if (dirty)
            Dirty(scope);
    }

    /// <summary>
    /// Re-raises <see cref="GetEyeOffsetEvent"/> on the user to recompute total eye offset.
    /// </summary>
    private void UpdateOffset(EntityUid user)
    {
        var ev = new GetEyeOffsetEvent();
        RaiseLocalEvent(user, ref ev);
        _eye.SetOffset(user, ev.Offset);
    }

    #endregion
}
