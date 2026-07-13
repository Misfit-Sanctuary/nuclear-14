// #Misfits Add - Applied to a player while their Stealth Boy is active.
// Tracks the cloak end time and current opacity phase for smooth fading.
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Misfits.StealthBoy;

/// <summary>
/// Applied to the player entity (not the item) while actively cloaked.
/// Tracks the timer and target stealth intensity for the shared stealth system.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class StealthBoyActiveComponent : Component
{
    /// <summary>
    /// When the cloak expires (server time).
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField]
    public TimeSpan EndTime;

    /// <summary>
    /// When the cloak was activated (for fade-in interpolation).
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField]
    public TimeSpan StartTime;

    /// <summary>
    /// Target visibility while the Stealth Boy is fully active.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float TargetVisibility = -1f;

    /// <summary>
    /// Fade-in duration copied from StealthBoyComponent at activation.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan FadeInTime = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// Whether the cloak is in fade-out phase (device duration expired but still fading).
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool FadingOut;

    /// <summary>
    /// When the fade-out started.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField]
    public TimeSpan FadeOutStart;

    /// <summary>
    /// True once steady-state visibility has been applied after fade-in.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool FadeInComplete;

    /// <summary>
    /// Fade-out duration.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan FadeOutTime = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Popup shown when the cloak fully fades out.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string ReappearMessage = "You reappear as the Stealth Boy power fades.";

    /// <summary>
    /// Visibility the user sinks to while standing still. Only does anything
    /// when set below TargetVisibility (nightkin implant uses this).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float StillVisibility = 1f;
}
