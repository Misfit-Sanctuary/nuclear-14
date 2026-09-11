using Robust.Shared.GameStates;

namespace Content.Shared._Misfits.Weapons.Throwable;

/// <summary>
/// An impact grenade starts safe. Using it in hand arms it without starting a fuse.
/// The server triggers it once when a subsequent thrown impact or stop occurs.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ImpactGrenadeComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Armed;

    // Only thrown grenades may detonate when their physics body settles.
    [ViewVariables]
    public bool WasThrown;

    [ViewVariables]
    public EntityUid? Thrower;

    // A rider's grenade starts inside their own mount; ignore that launch overlap.
    [ViewVariables]
    public EntityUid? ThrowerMount;

    // Collision and throw completion can happen together; never detonate twice.
    [ViewVariables]
    public bool Triggered;
}
