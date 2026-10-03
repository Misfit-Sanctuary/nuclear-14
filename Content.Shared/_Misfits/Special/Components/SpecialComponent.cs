using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Misfits.Special.Components;

/// <summary>
/// Runtime SPECIAL values for a character.
/// Base values come from the character profile; temporary modifiers are updated through SharedSpecialSystem.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), AutoGenerateComponentPause]
public sealed partial class SpecialComponent : Component
{
    // Base values are persistent character stats. Use SharedSpecialSystem to
    // mutate them so clamping, networking, and change events stay consistent.
    [DataField, AutoNetworkedField]
    public int BaseStrength = SpecialProfile.DefaultValue;

    [DataField, AutoNetworkedField]
    public int BasePerception = SpecialProfile.DefaultValue;

    [DataField, AutoNetworkedField]
    public int BaseEndurance = SpecialProfile.DefaultValue;

    [DataField, AutoNetworkedField]
    public int BaseCharisma = SpecialProfile.DefaultValue;

    [DataField, AutoNetworkedField]
    public int BaseIntelligence = SpecialProfile.DefaultValue;

    [DataField, AutoNetworkedField]
    public int BaseAgility = SpecialProfile.DefaultValue;

    [DataField, AutoNetworkedField]
    public int BaseLuck = SpecialProfile.DefaultValue;

    // Temporary modifiers are the networked sum of active timed/source entries.
    // Individual entries are tracked by SharedSpecialSystem.
    [DataField, AutoNetworkedField]
    public int TemporaryStrengthModifier;

    [DataField, AutoNetworkedField]
    public int TemporaryPerceptionModifier;

    [DataField, AutoNetworkedField]
    public int TemporaryEnduranceModifier;

    [DataField, AutoNetworkedField]
    public int TemporaryCharismaModifier;

    [DataField, AutoNetworkedField]
    public int TemporaryIntelligenceModifier;

    [DataField, AutoNetworkedField]
    public int TemporaryAgilityModifier;

    [DataField, AutoNetworkedField]
    public int TemporaryLuckModifier;

    // These cache reversible side effects applied by stat-specific systems.
    // They prevent repeated recalculation from stacking the same adjustment.
    [DataField]
    public float AppliedStaminaCritThresholdModifier;

    [DataField]
    public float AppliedHealthThresholdModifier;

    [DataField]
    public float AppliedHungerDecayMultiplier = 1f;

    [DataField]
    public float AppliedThirstDecayMultiplier = 1f;

    [DataField]
    public float AppliedStaminaRecoveryMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public FixedPoint2 SecondWindAppliedBonus;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan SecondWindActiveUntil;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan SecondWindCooldownUntil;
}
