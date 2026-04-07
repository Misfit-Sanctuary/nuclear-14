// #Misfits Add - Marker component applied to a gun when its scope attachment is being used
using Robust.Shared.GameStates;

namespace Content.Shared._Misfits.Scope;

/// <summary>
/// Applied to a gun entity when a user is scoping through an attachment scope
/// contained inside the gun. Tracks which scope entity is active so the gun
/// can respond to events like unequip/deselect/unwield and unscope accordingly.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedScopeSystem))]
public sealed partial class GunScopingComponent : Component
{
    /// <summary>
    /// The scope attachment entity inside this gun that is being scoped through.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Scope;
}
