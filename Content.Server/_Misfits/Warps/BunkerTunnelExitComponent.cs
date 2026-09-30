// #Misfits Add - Marker entity the bunker hatch can drop outsiders at.
namespace Content.Server._Misfits.Warps;

/// <summary>
/// A spot in the mines where the surface bunker hatch can drop a non-Enclave player. Mappers place
/// these in the map editor; every trip picks one at random. Adding another landing spot means
/// placing another marker, not changing code.
/// </summary>
[RegisterComponent]
public sealed partial class BunkerTunnelExitComponent : Component
{
    /// <summary>
    /// Which tunnel network this exit belongs to. Only hatches on the same channel can arrive here.
    /// </summary>
    [DataField]
    public string Channel = "bunker_tunnel";
}
