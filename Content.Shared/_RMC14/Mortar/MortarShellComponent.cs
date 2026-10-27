using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Mortar;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedMortarSystem))]
public sealed partial class MortarShellComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan LoadDelay = TimeSpan.FromSeconds(1.5);

    [DataField, AutoNetworkedField]
    public TimeSpan TravelDelay = TimeSpan.FromSeconds(4.5);

    [DataField, AutoNetworkedField]
    public TimeSpan ImpactWarningDelay = TimeSpan.FromSeconds(2.5);

    [DataField, AutoNetworkedField]
    public TimeSpan ImpactDelay = TimeSpan.FromSeconds(4.5);

    /// <summary>
    ///     Tile fire spawned across the impact point, if any. Shells that leave this null only
    ///     deliver their <c>Explosive</c> payload. This stands in for RMC's TileFireOnTrigger,
    ///     which this fork does not have.
    /// </summary>
    [DataField]
    public EntProtoId? FireTilePrototype;

    /// <summary>
    ///     Diamond radius, in tiles, of the fire spawned by <see cref="FireTilePrototype"/>.
    /// </summary>
    [DataField]
    public int FireRange = 5;
}
