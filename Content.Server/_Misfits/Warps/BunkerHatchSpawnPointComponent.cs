// #Misfits Add - Possible spot for a surface bunker hatch.
namespace Content.Server._Misfits.Warps;

/// <summary>
/// A spot where a bunker hatch may appear. Mappers place a pool of these on the surface; at round
/// start <see cref="BunkerHatchSpawnSystem"/> picks a few of them at random and spawns a hatch on
/// each. The rest stay empty for the round.
/// </summary>
[RegisterComponent]
public sealed partial class BunkerHatchSpawnPointComponent : Component
{
    /// <summary>
    /// Which tunnel network a hatch spawned here belongs to. Each channel gets its own picks.
    /// </summary>
    [DataField]
    public string Channel = "bunker_tunnel";
}
