using Robust.Shared.GameObjects;

namespace Content.Shared._Misfits.Radio;

/// <summary>
/// Marks a radio tower that must be activated before the public Wasteland
/// radio channel can be used.
/// </summary>
[RegisterComponent]
public sealed partial class RadioTowerComponent : Component
{
    /// <summary>
    /// Whether a player has brought this tower online during the current round.
    /// </summary>
    public bool Activated;
}
