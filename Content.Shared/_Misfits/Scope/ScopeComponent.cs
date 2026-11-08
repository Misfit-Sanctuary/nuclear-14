// #Misfits Add - Directional scope component (ported from RMC-14 Scoping)
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.Scope;

/// Add it to any prototype, for example:
/// - type: Scope
///   requireWielding: true
///   zoomLevels:
///   - name: 5x
///     zoom: 1.2
///     offset: 16
///     allowMovement: false
///     doAfter: 0.7
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedScopeSystem))]
public sealed partial class ScopeComponent : Component
{
    /// Index into ZoomLevels of the currently selected zoom level.
    [DataField, AutoNetworkedField]
    public int CurrentZoomLevel;

    /// Available zoom levels; the cycle action is only granted when there is more than one.
    [DataField, AutoNetworkedField]
    public List<ScopeZoomLevel> ZoomLevels = new()
    {
        new ScopeZoomLevel(null, 1f, 15, false, TimeSpan.FromSeconds(1)),
    };

    /// The entity currently looking through this scope.
    [ViewVariables, AutoNetworkedField]
    public EntityUid? User;

    /// Action prototype used to toggle the scope.
    [DataField]
    public EntProtoId ScopingToggleAction = "N14ActionToggleScope";

    /// Runtime: the toggle action entity, created when the scope is initialised.
    [DataField, AutoNetworkedField]
    public EntityUid? ScopingToggleActionEntity;

    /// Action prototype used to cycle zoom levels; only granted with multiple zoom levels.
    [DataField]
    public EntProtoId CycleZoomLevelAction = "N14ActionCycleZoomLevel";

    /// Runtime: the cycle action entity, created when the scope is initialised.
    [DataField, AutoNetworkedField]
    public EntityUid? CycleZoomLevelActionEntity;

    /// If true, the item must be wielded before scoping is allowed.
    [DataField]
    public bool RequireWielding;

    /// If true, the scope can also be toggled by using the item in hand (binoculars).
    [DataField]
    public bool UseInHand;

    /// The cardinal direction the user is scoped toward; null while not scoping.
    [DataField, AutoNetworkedField]
    public Direction? ScopingDirection;

    /// Server-only PVS relay spawned at the scoped offset so distant chunks reach the scoping player.
    [ViewVariables]
    public EntityUid? RelayEntity;

    /// Popup shown when scoping in; null suppresses it.
    [DataField]
    public string? ScopePopup = "n14-action-popup-scoping-user";

    /// Popup shown when scoping out; null suppresses it.
    [DataField]
    public string? UnScopePopup = "n14-action-popup-scoping-stopping-user";
}
