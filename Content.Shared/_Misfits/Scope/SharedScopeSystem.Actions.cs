// #Misfits Add - Scope actions: granting, toggling and zoom cycling
using System.Numerics;
using Content.Shared.Actions;
using Content.Shared.Toggleable;

namespace Content.Shared._Misfits.Scope;

/// Action plumbing for scopes; the action entities live in the scope's own action container, so their events are raised there.
public abstract partial class SharedScopeSystem
{
    [Dependency] private readonly ActionContainerSystem _actionContainer = default!;

    private void InitializeActions()
    {
        // Create the action entities once, when the library is initialised.
        SubscribeLocalEvent<ScopeComponent, MapInitEvent>(OnScopeMapInit);

        // Hand them to whoever is holding the item.
        SubscribeLocalEvent<ScopeComponent, GetItemActionsEvent>(OnScopeGetActions);

        SubscribeLocalEvent<ScopeComponent, ToggleActionEvent>(OnToggleAction);
        SubscribeLocalEvent<ScopeComponent, ScopeCycleZoomLevelEvent>(OnCycleZoomLevel);
    }

    private void OnScopeMapInit(Entity<ScopeComponent> scope, ref MapInitEvent args)
    {
        // The toggle action is always created; the cycle action only with more than one zoom level.
        _actionContainer.EnsureAction(scope.Owner, ref scope.Comp.ScopingToggleActionEntity, scope.Comp.ScopingToggleAction.Id);

        if (scope.Comp.ZoomLevels.Count > 1)
            _actionContainer.EnsureAction(scope.Owner, ref scope.Comp.CycleZoomLevelActionEntity, scope.Comp.CycleZoomLevelAction.Id);

        Dirty(scope.Owner, scope.Comp);
    }

    /// Grants the scope actions while the scoped item is held in hand.
    private void OnScopeGetActions(Entity<ScopeComponent> scope, ref GetItemActionsEvent args)
    {
        if (!args.InHands)
            return;

        args.AddAction(scope.Comp.ScopingToggleActionEntity);

        if (scope.Comp.ZoomLevels.Count > 1)
            args.AddAction(scope.Comp.CycleZoomLevelActionEntity);
    }

    private void OnToggleAction(Entity<ScopeComponent> scope, ref ToggleActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        ToggleScoping(scope, args.Performer);
    }

    private void OnCycleZoomLevel(Entity<ScopeComponent> scope, ref ScopeCycleZoomLevelEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        CycleZoomLevel(scope, args.Performer);
    }

    /// Advances to the next zoom level, wrapping around, and applies it immediately if the user is scoped.
    public void CycleZoomLevel(Entity<ScopeComponent> scope, EntityUid? user = null)
    {
        var count = scope.Comp.ZoomLevels.Count;
        if (count <= 1)
            return;

        scope.Comp.CurrentZoomLevel = (scope.Comp.CurrentZoomLevel + 1) % count;
        var zoomLevel = GetZoomLevel(scope);

        if (zoomLevel.Name != null && user is { } popupUser)
        {
            _popup.PopupClient(
                Loc.GetString("n14-action-popup-scope-cycle-zoom", ("zoom", zoomLevel.Name)),
                popupUser,
                popupUser);
        }

        if (scope.Comp.User is { } scopedUser &&
            scope.Comp.ScopingDirection is { } direction &&
            TryComp(scopedUser, out ScopingComponent? scoping))
        {
            scoping.AllowMovement = zoomLevel.AllowMovement;

            var offset = GetScopeOffset(scope, direction);
            scoping.EyeOffset = offset;

            _contentEye.SetZoom(scopedUser, Vector2.One * zoomLevel.Zoom, true);
            UpdateOffset(scopedUser);
            MoveRelay(scope, offset);

            Dirty(scopedUser, scoping);
        }

        Dirty(scope);
    }
}
