using Content.Shared.Alert;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.PowerArmor;

/// <summary>
///     Gives power armor a separate HP pool (integrity) that absorbs incoming
///     damage before it reaches the wearer.
///
///     While integrity is above zero, <see cref="BleedthroughRatio"/> of each
///     hit (default 10%) bleeds through to the player and the rest is absorbed
///     by the armor's own HP pool.
///
///     When integrity reaches zero the armor enters a <b>broken</b> state:
///     <list type="bullet">
///       <item>
///         <see cref="Content.Shared.Armor.ArmorComponent"/> is <b>NOT removed</b> —
///         damage coefficients still apply.
///       </item>
///       <item>
///         Only <see cref="BrokenBleedthroughRatio"/> (20% default) of
///         post-coefficient damage reaches the wearer (no HP absorption).
///       </item>
///       <item>
///         A <see cref="PowerArmorBrokenComponent"/> is added to the wearer,
///         cutting their movement speed to 10% of normal.
///       </item>
///     </list>
///
///     Repair with a welder to restore full absorption and remove the speed debuff.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PowerArmorIntegrityComponent : Component
{
    /// <summary>
    ///     Maximum integrity (HP) the armor can have. Tracks how much total
    ///     damage the armor can absorb before breaking.
    /// </summary>
    [DataField, AutoNetworkedField]
    public FixedPoint2 MaxIntegrity = 200;

    /// <summary>
    ///     Fraction of post-coefficient damage that bleeds through to the
    ///     wearer while integrity is above zero (0.0–1.0).
    ///     Default 0.100 = 10% bleedthrough — player feels hits but is
    ///     effectively protected until the armor breaks.
    ///     The remaining (1 − BleedthroughRatio) is absorbed by the armor HP pool.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float BleedthroughRatio = 0.100f;

    /// <summary>
    ///     Fraction of post-coefficient damage that reaches the wearer when
    ///     the armor is in the broken state (integrity = 0).
    ///     Default 0.20 = 20% — wearer is still partially protected but
    ///     the remaining 80% is not absorbed anywhere (no HP pool left).
    ///     Combined with the ArmorComponent coefficients still being active,
    ///     the wearer takes coefficient × 0.20 of raw damage.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float BrokenBleedthroughRatio = 0.20f;

    /// <summary>
    ///     When integrity drops to zero the armor is broken and provides no
    ///     absorption. The wearer takes full damage until the suit is repaired.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Broken;

    /// <summary>
    ///     Stores the <see cref="DamageModifierSet"/> from
    ///     <see cref="Content.Shared.Armor.ArmorComponent"/> while the suit is
    ///     broken.
    ///     <para>
    ///         Retained for serialization compatibility — <b>no longer used at runtime</b>.
    ///         ArmorComponent is no longer removed when integrity hits zero, so this
    ///         cache is never written. Kept to avoid breaking existing saved state.
    ///     </para>
    /// </summary>
    [DataField]
    public DamageModifierSet? CachedArmorModifiers;

    /// <summary>
    ///     Alert prototype shown on the wearer's HUD while the armor is equipped.
    ///     Severity scales with remaining integrity (higher = healthier).
    /// </summary>
    [DataField]
    public ProtoId<AlertPrototype> IntegrityAlert = "PowerArmorIntegrity";

    /// <summary>
    ///     Number of severity levels for the HUD alert (matches the icon count
    ///     in the alert prototype).
    /// </summary>
    [DataField]
    public int AlertLevels = 5;
}

/// <summary>
///     Placed on the <b>wearer</b> (not the armor item) while a power armor
///     suit is actively worn. Allows external systems (e.g. a friend with a
///     welder) to forward interactions through the player directly to the
///     armor entity sitting in their inventory.
/// </summary>
[RegisterComponent]
public sealed partial class PowerArmorWornComponent : Component
{
    /// <summary>The armor item currently equipped by this entity.</summary>
    [DataField]
    public EntityUid Armor;
}
