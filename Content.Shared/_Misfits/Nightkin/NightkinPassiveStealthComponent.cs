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

    // how visible while cloaked but not moving. -1 = completely gone
    [DataField, AutoNetworkedField]
    public float StillVisibility = 0f;

    [DataField, AutoNetworkedField]
    public TimeSpan FadeInTime = TimeSpan.FromSeconds(1.5);

    [DataField, AutoNetworkedField]
    public TimeSpan FadeOutTime = TimeSpan.FromSeconds(2);

    [DataField, AutoNetworkedField]
    public string ActivateMessage = "Your Stealth Boy implant hums and you feel yourself fade from view.";

    [DataField, AutoNetworkedField]
    public string DeactivateMessage = "Your Stealth Boy implant powers down.";

    // recharge time after the cloak drops
    [DataField, AutoNetworkedField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(30);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField]
    public TimeSpan CooldownEndTime;
}

/// <summary>
/// Fired by the Nightkin innate stealth action.
/// </summary>
public sealed partial class ToggleNightkinStealthActionEvent : InstantActionEvent;

/// <summary>
/// Makes the holder steadier with guns. Below 1 = tighter spread / less recoil.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GunHandlingModifierComponent : Component
{
    [DataField, AutoNetworkedField]
    public float SpreadMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float RecoilMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float FireRateMultiplier = 1f;
}
