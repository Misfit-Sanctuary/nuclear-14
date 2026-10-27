using Content.Shared.Damage;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

// #Misfits Add - Gun-side damage, fire cost, and hitscan override for energy weapons.
// Allows each weapon to control its own beam color and damage independently of the cell.
// The cell provides charge; the weapon determines what fires.
// Also holds the gun-side firing modifiers: hitscan spread and armor piercing.

namespace Content.Shared._Misfits.Weapons;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GunDamageBonusComponent : Component
{
    /// <summary>
    /// If set, the gun uses this hitscan prototype instead of whatever the cell provides.
    /// This controls beam visuals AND base damage — the cell only supplies charge.
    /// </summary>
    [DataField("hitscanProtoOverride"), AutoNetworkedField]
    public string? HitscanProtoOverride;

    /// <summary>
    /// Flat bonus damage added on top of the hitscan's base damage.
    /// Applied server-side when the hitscan hits a target.
    /// </summary>
    [DataField("bonusDamage"), AutoNetworkedField]
    public DamageSpecifier? BonusDamage;

    /// <summary>
    /// Multiplier applied to the inserted cell's FireCost.
    /// 1.0 = normal, 2.0 = double cost (half the shots).
    /// Applied when a cell is inserted; restored when ejected.
    /// </summary>
    [DataField("fireCostMultiplier"), AutoNetworkedField]
    public float FireCostMultiplier = 1.0f;

    /// <summary>
    /// Tracks the original FireCost of the currently inserted cell
    /// so it can be restored on ejection.
    /// </summary>
    [ViewVariables]
    public float? OriginalFireCost;
}

/// #Misfits Add - Gives a hitscan weapon shotgun behaviour by firing several rays in a spread.
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class HitscanSpreadComponent : Component
{
    /// Total angle the rays are spread across, in degrees.
    [DataField, AutoNetworkedField]
    public Angle Spread = Angle.FromDegrees(15);

    /// How many rays are fired, each deals the hitscan's full damage.
    [DataField, AutoNetworkedField]
    public int Count = 3;
}

/// #Misfits Add - Lets a weapon cut through part of the armour its shots hit.
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ArmorPiercingComponent : Component
{
    /// Fraction of the target's worn armour ignored, 0.5 ignores half of it.
    [DataField, AutoNetworkedField]
    public float Piercing = 0.5f;
}
