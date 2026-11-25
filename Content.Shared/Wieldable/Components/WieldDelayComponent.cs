using Robust.Shared.GameStates;

namespace Content.Shared.Wieldable.Components;

    /// <summary>
    /// Ported for the purpose of slowing usage of heavy weapons. Like MGs, rocket launchers, and the likes. # Misfits
    /// </summary>

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(WieldDelaySystem))]
public sealed partial class WieldDelayComponent : Component
{
    /// <summary>
    /// Delay applied when item is picked up and when item is wielded, not when generally held.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan BaseDelay = TimeSpan.FromSeconds(0.4);

    /// <summary>
    /// Prevent firing while the wield delay is active.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool PreventFiring;
}
