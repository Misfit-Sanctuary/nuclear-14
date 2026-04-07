using Content.Shared.Actions;
using Content.Shared.Speech;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.Warcry;

/// <summary>
/// Grants an innate warcry action that buffs nearby allies in a target department.
/// </summary>
[RegisterComponent]
public sealed partial class WarcryComponent : Component
{
    /// <summary>
    /// The action prototype granted to the entity.
    /// </summary>
    [DataField]
    public EntProtoId<InstantActionComponent> Action = "ActionTribalWarcry";

    /// <summary>
    /// The spawned action entity granted to the wearer.
    /// </summary>
    [DataField]
    public EntityUid? ActionEntity;

    /// <summary>
    /// The primary department to buff when the warcry is used.
    /// </summary>
    [DataField(required: true)]
    public string TargetDepartment = string.Empty;

    /// <summary>
    /// Optional exact job whitelist allowed to activate this action.
    /// </summary>
    [DataField]
    public HashSet<string>? ActivatorJobs;

    /// <summary>
    /// Radius around the caster affected by the warcry.
    /// </summary>
    [DataField]
    public float Range = 6f;

    /// <summary>
    /// Flat fractional movement bonus applied while buffed.
    /// For example, 0.15 grants a 15% walk and sprint speed increase.
    /// </summary>
    [DataField]
    public float SpeedBonus = 0.30f; // #Misfits Tweak - reduced default from 0.50, was too strong

    /// <summary>
    /// How long the buff lasts on affected allies.
    /// </summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Color used by the persistent client overlay.
    /// </summary>
    [DataField]
    public Color OverlayColor = Color.Red;

    /// <summary>
    /// The speech verb used for the activation shout.
    /// </summary>
    [DataField]
    public ProtoId<SpeechVerbPrototype> SpeechVerb = "MisfitsTribalWarcry";

    /// <summary>
    /// Localization key prefix for the activation line spoken by the caster.
    /// When <see cref="WarcryMessageCount"/> is greater than 1, the system will
    /// pick a random key in the form "{prefix}-N".
    /// </summary>
    [DataField]
    public string WarcryMessage = "warcry-message-tribal";

    /// <summary>
    /// Number of localized shout variants available for <see cref="WarcryMessage"/>.
    /// </summary>
    [DataField]
    public int WarcryMessageCount = 1;

    /// <summary>
    /// Localization key for the popup shown to buffed allies.
    /// </summary>
    [DataField]
    public string BuffPopup = "warcry-popup-buffed-tribal";

    /// <summary>
    /// Whether nearby observers should receive a caution-styled popup.
    /// </summary>
    [DataField]
    public bool CautionPopup;
}