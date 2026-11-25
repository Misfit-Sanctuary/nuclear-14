// #Misfits Change Add: Marker component placed on power armor item entities.
// Used by CarryingSystem to prevent non-power-armor users from picking up power-armor wearers.
// Dragging / pulling mechanics are unaffected.

using Robust.Shared.GameStates;

namespace Content.Shared._Misfits.PowerArmor;

/// <summary>
///     Marker component for Nuclear-14 power armor items (T-45, T-51, T-60, X-01, X-02, etc.).
///     When a character has this component's parent item equipped in the outerClothing slot,
///     they are considered "in power armor" for gameplay purposes such as carry restrictions.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class N14PowerArmorComponent : Component
{
    /// <summary>
    ///     When true (default), equipping this armor requires <see cref="PowerArmorProficiencyComponent"/>
    ///     (granted by the N14PowerArmorTraining trait). Set to false for salvaged/stripped-down
    ///     variants that any soldier can wear without servo training.
    /// </summary>
    // #Misfits Add - allows salvaged PA variants to bypass the proficiency gate
    [DataField, AutoNetworkedField]
    public bool RequiresProficiency = true;
}

// #Cythisiax Added - Identify pilotable power armor that uses the wearer's hands and training.
[RegisterComponent]
public sealed partial class MechPowerArmorComponent : Component
{
}
