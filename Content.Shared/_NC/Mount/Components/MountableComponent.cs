using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._NC.Mountable.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MountableComponent : Component
{
    /// <summary>
    /// Current Rider
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public EntityUid? Rider;

    /// <summary>
    /// Motion capability switch on mount
    /// </summary>
    [DataField("controlMovement")]
    public bool ControlMovement = true;

    /// <summary>
    /// Whether the current rider was granted control over this mount.
    /// </summary>
    [ViewVariables]
    public bool RiderControlsMovement;

    /// <summary>
    /// Whether this mount had NPC AI before rider control temporarily disabled it.
    /// </summary>
    [ViewVariables]
    public bool HadActiveNpcBeforeMount;
}
