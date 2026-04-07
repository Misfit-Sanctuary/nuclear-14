using Content.Shared.Damage;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

// #Misfits Add - Gun-side damage, fire cost, and hitscan override for energy weapons.
// Allows each weapon to control its own beam color and damage independently of the cell.
// The cell provides charge; the weapon determines what fires.

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
