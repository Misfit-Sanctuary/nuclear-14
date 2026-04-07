// #Misfits Add - Marker component applied to the user entity while they are actively scoped in
using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._Misfits.Scope;

/// <summary>
/// Applied to an entity (player) when they are actively scoping through a <see cref="ScopeComponent"/>.
/// Tracks the eye offset and movement allowance for the current scope session.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedScopeSystem))]
public sealed partial class ScopingComponent : Component
{
    /// <summary>
    /// The scope entity this user is looking through.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public EntityUid? Scope;

    /// <summary>
    /// The computed eye offset applied to this user's viewport.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public Vector2 EyeOffset;

    /// <summary>
    /// If true, movement does not break scoping.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public bool AllowMovement;
}
