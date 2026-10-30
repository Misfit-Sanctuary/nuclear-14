using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
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

    /// Tile fire spawned across the impact point, if any.

    [DataField]
    public EntProtoId? FireTilePrototype;

    /// Diamond radius, in tiles, of the fire spawned by <see cref="FireTilePrototype"/>.

    [DataField]
    public int FireRange = 5;

    /// Smoke cloud released on impact, if any.

    [DataField]
    public MortarSmokePayload? Smoke;

    /// Electromagnetic pulse released on impact, if any.

    [DataField]
    public MortarEmpPayload? Emp;

    /// Prototype spawned at the impact point and lit at once, if any.

    [DataField]
    public EntProtoId? ImpactSpawn;

    /// Camera dropped on the impact point, if any.

    [DataField]
    public EntProtoId? ImpactCamera;

    /// Damage dealt to everything on the impact tile, if any.

    [DataField]
    public DamageSpecifier? KineticDamage;

    /// Whether the casing survives the impact to be collected and fired again.

    [DataField]
    public bool Reusable;
}

/// Parameters for the pulse a shell releases on impact.

[DataDefinition]
public sealed partial class MortarEmpPayload
{
    /// Radius, in tiles, of the pulse.

    [DataField]
    public float Range = 6f;

    /// Charge drained from every battery caught in the pulse.

    [DataField]
    public float EnergyConsumption = 2700000f;

    /// How long, in seconds, anything electronics-bearing stays dead.

    [DataField]
    public float DisableDuration = 30f;
}

/// Parameters for the smoke cloud a shell leaves behind.

[DataDefinition]
public sealed partial class MortarSmokePayload
{
    /// Smoke entity to spawn. Foam or a reagent cloud can be used here as well.

    [DataField]
    public EntProtoId Prototype = "Smoke";

    /// How long, in seconds, the cloud lasts once spread.

    [DataField]
    public float Duration = 30f;

    /// How many tiles the cloud spreads to.

    [DataField]
    public int Spread = 25;

    /// Reagents carried by the cloud, if any.

    [DataField]
    public Solution Solution = new();
}
