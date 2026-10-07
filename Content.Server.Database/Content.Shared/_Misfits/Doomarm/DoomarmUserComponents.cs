// #Misfits Add - Temporary states put on a Doomarm user while an ability is running.

using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._Misfits.Doomarm;

/// <summary>
/// On the user while they are up on the level above during Orbital Descent.
/// Networked so the client also treats them as standing (not floating) and blocks attacks.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DoomarmAirborneComponent : Component
{
    [AutoNetworkedField]
    public EntityUid Arm;

    [AutoNetworkedField]
    public TimeSpan LandAt;

    /// <summary>
    /// The ground marker under them, on the level below. Server only.
    /// </summary>
    public EntityUid? Marker;

    /// <summary>
    /// The map they jumped up from and will land back on. Server only.
    /// </summary>
    public EntityUid GroundMap;

    /// <summary>
    /// Last world position whose ground tile was free to land on. Server only.
    /// </summary>
    public Vector2 LastSafePosition;
}

/// <summary>
/// On the user during the Groundbreaker leap. The slam happens when <see cref="LandAt"/> passes.
/// </summary>
[RegisterComponent]
public sealed partial class DoomarmHoppingComponent : Component
{
    public EntityUid Arm;

    public TimeSpan LandAt;
}

/// <summary>
/// On the user while flying through the air from Kinetic Discharge.
/// </summary>
[RegisterComponent]
public sealed partial class DoomarmLungingComponent : Component
{
    public EntityUid Arm;

    public Vector2 Direction;

    /// <summary>
    /// Only the first person hit takes the blow.
    /// </summary>
    public bool HasHit;

    /// <summary>
    /// Safety net: the lunge state is cleared by now even if the landing event never comes.
    /// </summary>
    public TimeSpan EndAt;
}
