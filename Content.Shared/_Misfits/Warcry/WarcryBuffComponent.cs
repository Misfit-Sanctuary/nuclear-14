using Robust.Shared.GameStates;

namespace Content.Shared._Misfits.Warcry;

/// <summary>
/// Temporary movement-speed buff applied by a warcry.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WarcryBuffComponent : Component
{
    /// <summary>
    /// Flat fractional movement bonus while the buff is active.
    /// For example, 0.15 grants a 15% walk and sprint speed increase.
    /// Defaults to 0 so Math.Max in WarcrySystem always picks the config value.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float SpeedBonus; // #Misfits Fix - was 0.50, silently overriding Legion's 0.30 via Math.Max

    /// <summary>
    /// When the buff expires.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan ExpiresAt;
}