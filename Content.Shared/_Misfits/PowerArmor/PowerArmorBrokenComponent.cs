// #Misfits Add - Placed on the wearer when power armor integrity is fully depleted.
// Applies movement speed penalty; armor coefficients remain active.
using Robust.Shared.GameStates;

namespace Content.Shared._Misfits.PowerArmor;

/// <summary>
///     Placed on the <b>wearer</b> entity (not the armor item) when their worn power armor's
///     integrity is fully depleted (<see cref="PowerArmorIntegrityComponent.Broken"/> = true).
///
///     While present:
///     <list type="bullet">
///       <item>The armor's <c>ArmorComponent</c> is <b>NOT</b> removed — all damage coefficients still apply.</item>
///       <item>
///         Incoming damage is further reduced to <see cref="PowerArmorIntegrityComponent.BrokenBleedthroughRatio"/>
///         of post-coefficient values (20% by default). 80% is neither absorbed nor dealt — the
///         broken plates still partially deflect the hit.
///       </item>
///       <item>
///         Walk and sprint speed are multiplied by <see cref="SpeedModifier"/> — default 0.10
///         (10% of normal speed, i.e. 90% cut). The wearer can barely move.
///       </item>
///     </list>
///
///     Removed automatically when:
///     <list type="bullet">
///       <item>Armor integrity is repaired above 0 (welder interaction).</item>
///       <item>The broken armor is unequipped.</item>
///     </list>
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PowerArmorBrokenComponent : Component
{
    /// <summary>
    ///     Walk and sprint speed multiplier applied while this component is active.
    ///     Default 0.40 = 40% of normal speed (60% penalty).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float SpeedModifier = 0.40f;
}
