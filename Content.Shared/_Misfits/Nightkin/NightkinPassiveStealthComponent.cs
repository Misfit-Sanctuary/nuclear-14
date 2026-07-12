// #Misfits Add - Innate Nightkin Stealth Boy implant state.
using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Misfits.Nightkin;

/// <summary>
/// Grants Nightkin an innate toggleable Stealth Boy effect without requiring an item.
/// The actual cloak/exposure behavior is still owned by the shared Stealth Boy system.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NightkinPassiveStealthComponent : Component
{
    [DataField, AutoNetworkedField]
    public string Action = "ActionToggleNightkinStealth";

    [DataField, AutoNetworkedField]
    public EntityUid? ActionEntity;

    [DataField, AutoNetworkedField]
    public float Visibility = 0.3f;

    [DataField, AutoNetworkedField]
    public TimeSpan FadeInTime = TimeSpan.FromSeconds(1.5);

    [DataField, AutoNetworkedField]
    public TimeSpan FadeOutTime = TimeSpan.FromSeconds(2);

    [DataField, AutoNetworkedField]
    public string ActivateMessage = "Your Stealth Boy implant hums and you feel yourself fade from view.";

    [DataField, AutoNetworkedField]
    public string DeactivateMessage = "Your Stealth Boy implant powers down.";

    /// <summary>
    /// How long the implant needs to recharge after the cloak fully drops
    /// before it can be activated again.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Server time at which the implant has finished recharging.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField]
    public TimeSpan CooldownEndTime;
}

/// <summary>
/// Fired by the Nightkin innate stealth action.
/// </summary>
public sealed partial class ToggleNightkinStealthActionEvent : InstantActionEvent;
