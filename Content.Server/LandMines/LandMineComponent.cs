// #Misfits Add - added Armed state and updated component doc
using Robust.Shared.Audio;

namespace Content.Server.LandMines;

[RegisterComponent]
public sealed partial class LandMineComponent : Component
{
    /// <summary>
    /// Sound played when a foot lands on the mine pressure plate (before triggering).
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier? Sound;

    /// <summary>
    /// Whether the mine is currently armed and will detonate on step-off.
    /// Mines must be anchored before they can be armed.
    /// </summary>
    [DataField]
    public bool Armed;

    /// <summary>
    /// Whether this mine may be armed while it is loose or held. Hawkins devices use this so
    /// they can be armed first and then placed on the ground without wrenching them down.
    /// Ordinary landmines still require anchoring.
    /// </summary>
    [DataField]
    public bool AllowUnanchoredArming;

    /// <summary>
    /// Restricts pressure activation to motorbikes and characters currently wearing power armor.
    /// This is intentionally explicit rather than mass-based: carried inventories and physics
    /// mass are not reliable measures of whether a target is an armored vehicle.
    /// </summary>
    [DataField]
    public bool HeavyTargetsOnly;

    /// <summary>
    /// #Misfits Add - if set, knocks down the tripper for this duration on trigger.
    /// Used by the concussion mine to floor targets without killing/delimbing.
    /// </summary>
    [DataField]
    public TimeSpan? KnockdownDuration;
}
