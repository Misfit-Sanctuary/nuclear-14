namespace Content.Server._Misfits.Hawkins;

/// <summary>
/// Adds a short-ranged anti-materiel hit when a Hawkins device is triggered.
/// The normal Explosive component remains responsible for blast damage.
/// </summary>
[RegisterComponent]
public sealed partial class HawkinsShapedChargeComponent : Component
{
    /// <summary>Radius in which armor or a motorbike receives the focused hit.</summary>
    [DataField]
    public float Range = 1.25f;

    /// <summary>Integrity damage applied directly to a worn power-armor item.</summary>
    [DataField]
    public float ArmorDamage = 150f;

    /// <summary>Structural damage applied directly to a motorbike.</summary>
    [DataField]
    public float VehicleDamage = 150f;
}
