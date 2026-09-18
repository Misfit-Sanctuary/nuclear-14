using Robust.Shared.GameStates;

namespace Content.Shared._Misfits.Weapons;

///     #Misfits Add - Marker for super-heavy weapons that can only be wielded and fired by characters with
///     high SPECIAL Strength or by characters wearing power armor.
///     Enforced by <see cref="N14SuperHeavyWeaponSystem"/>.
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class N14SuperHeavyWeaponComponent : Component
{
    ///     Minimum effective SPECIAL Strength required to handle the weapon without
    ///     power armor.
    [DataField, AutoNetworkedField]
    public int MinStrength = 7;
}
