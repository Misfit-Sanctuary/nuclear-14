// #Misfits Add - One end of the bunker tunnel teleporter (surface hatch or tunnel door).
namespace Content.Server._Misfits.Warps;

/// <summary>
/// Marks an entity as one end of the bunker tunnel teleporter.
/// Unlike WarperComponent this stores no destination id on disk. The destination is worked out when
/// a player uses it, so a pair works the moment both halves exist, spawned in any order, with no
/// mapping or admin setup.
/// <para>
/// Enclave members going down a hatch arrive at the tunnel door in the base. Outsiders land in the
/// mines, or get lost in the tunnels and come back out of the same hatch. The door only opens for the Enclave,
/// and lets them pick which hatch to come out of.
/// </para>
/// </summary>
[RegisterComponent]
public sealed partial class BunkerTeleporterComponent : Component
{
    /// <summary>
    /// True for the surface hatch, false for the tunnel door. A teleporter only ever looks for the
    /// opposite kind, so two hatches never lead to each other for the Enclave.
    /// </summary>
    [DataField]
    public bool IsSurface;

    /// <summary>
    /// Which tunnel network this belongs to. Both halves ship with the same default, which is why an
    /// admin-spawned pair links instantly. Mappers can set a different channel to run separate
    /// networks that ignore each other.
    /// </summary>
    [DataField]
    public string Channel = "bunker_tunnel";

    /// <summary>
    /// Name shown for this hatch in the tunnel door menu and on the tac map, e.g. "Hatch A".
    /// Set by the round-start spawner; admin-spawned hatches get a fallback name.
    /// </summary>
    [DataField]
    public string? Label;

    /// <summary>
    /// Chance an outsider going down this hatch gets lost in the tunnels instead of landing in the
    /// mines: they are held in the dark for <see cref="OutsiderLostTime"/>, then climb back out of
    /// this same hatch. Rolled fresh every trip.
    /// </summary>
    [DataField]
    public float OutsiderLostChance = 0.15f;

    /// <summary>
    /// How long an outsider stays lost in the tunnels before they come back out.
    /// </summary>
    [DataField]
    public TimeSpan OutsiderLostTime = TimeSpan.FromSeconds(25);

    /// <summary>
    /// After someone comes back out from being lost, they can't get lost again for this long. Going
    /// down any hatch in that time drops them in the mines like normal. Per person, not per hatch.
    /// </summary>
    [DataField]
    public TimeSpan OutsiderLostCooldown = TimeSpan.FromSeconds(45);
}
