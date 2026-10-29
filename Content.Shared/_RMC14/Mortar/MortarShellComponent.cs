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

    /// <summary>
    ///     Smoke cloud released on impact, if any. Utility shells carrying this usually leave
    ///     <c>Explosive</c> off entirely, so a smoke screen can't double as a free HE shell.
    /// </summary>
    [DataField]
    public MortarSmokePayload? Smoke;

    /// <summary>
    ///     Prototype spawned at the impact point, if any. Expendable lights spawned this way are
    ///     lit immediately, which is how the illumination shell works.
    /// </summary>
    [DataField]
    public EntProtoId? ImpactSpawn;
}

/// <summary>
///     Parameters for the smoke cloud a mortar shell leaves behind.
/// </summary>
[DataDefinition]
public sealed partial class MortarSmokePayload
{
    /// <summary>
    ///     Smoke entity to spawn. Foam or a reagent cloud can be used here as well.
    /// </summary>
    [DataField]
    public EntProtoId Prototype = "Smoke";

    /// <summary>
    ///     How long, in seconds, the cloud lasts once spread.
    /// </summary>
    [DataField]
    public float Duration = 30f;

    /// <summary>
    ///     How many tiles the cloud spreads to.
    /// </summary>
    [DataField]
    public int Spread = 25;
}
