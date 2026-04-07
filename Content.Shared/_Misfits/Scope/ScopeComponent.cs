// #Misfits Add - Directional scope component (ported from RMC-14 Scoping system)
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Scope;

/// <summary>
/// Gives an entity (gun, binoculars, or attachment item) a directional scope
/// that shifts the user's viewport forward in the direction they are facing.
/// Supports configurable zoom levels, DoAfter delay, and directional locking.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedScopeSystem))]
public sealed partial class ScopeComponent : Component
{
    /// <summary>
    /// The currently selected zoom level index into <see cref="ZoomLevels"/>.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int CurrentZoomLevel;

    /// <summary>
    /// List of available zoom levels. Cycling action is granted when count > 1.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<ScopeZoomLevel> ZoomLevels = new()
    {
        new ScopeZoomLevel(null, 1f, 15, false, TimeSpan.FromSeconds(1))
    };

    /// <summary>
    /// The entity currently scoping through this item.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public EntityUid? User;

    /// <summary>
    /// Action prototype for toggling the scope on/off.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntProtoId? ScopingToggleAction = "N14ActionToggleScope";

    /// <summary>
    /// Runtime: the spawned toggle action entity.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? ScopingToggleActionEntity;

    /// <summary>
    /// Action prototype for cycling zoom levels (only used when ZoomLevels.Count > 1).
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntProtoId CycleZoomLevelAction = "N14ActionCycleZoomLevel";

    /// <summary>
    /// Runtime: the spawned cycle zoom action entity.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? CycleZoomLevelActionEntity;

    /// <summary>
    /// If true, the weapon must be wielded before scoping is allowed.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool RequireWielding;

    /// <summary>
    /// If true, the scope can be activated by using it in hand (ActivateInWorld).
    /// Used for standalone items like binoculars.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool UseInHand;

    /// <summary>
    /// The cardinal direction the user is currently scoped toward.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Direction? ScopingDirection;

    /// <summary>
    /// Server-side relay entity spawned at the scoped offset position for PVS.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? RelayEntity;

    /// <summary>
    /// If true, this is a scope attachment inside a gun's container.
    /// The scope action only works when the hosting gun is held.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Attachment;

    /// <summary>
    /// Popup message shown when scoping in. Null to suppress.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? ScopePopup = "n14-action-popup-scoping-user";

    /// <summary>
    /// Popup message shown when unscoping. Null to suppress.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? UnScopePopup = "n14-action-popup-scoping-stopping-user";
}
