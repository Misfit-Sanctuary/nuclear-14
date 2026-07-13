using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.Special.Prototypes;

[Prototype("specialTuning")]
public sealed partial class SpecialTuningPrototype : IPrototype
{
    // Code defaults keep the system usable in tests or when the tuning prototype
    // is not loaded. Production values should live in YAML.
    public static readonly SpecialTuningPrototype Fallback = new()
    {
        ID = "Fallback",
    };

    [IdDataField]
    public string ID { get; private set; } = default!;

    // Strength: melee output and carry handling.
    [DataField("strengthMeleeDamageMultiplierPerPoint")]
    public float StrengthMeleeDamageMultiplierPerPoint = 0.02f;

    [DataField("strengthUnarmedDamageMultiplierPerPoint")]
    public float StrengthUnarmedDamageMultiplierPerPoint = 0.03333333f;

    [DataField("strengthCarryPullSpeedMultiplierPerPoint")]
    public float StrengthCarryPullSpeedMultiplierPerPoint = 0.04f;

    [DataField("strengthThrowSpeedMultiplierPerPoint")]
    public float StrengthThrowSpeedMultiplierPerPoint = 0.06666667f;

    // Perception: ranged accuracy, heavy gun handling, mining speed, and fire delay.
    [DataField("perceptionSpreadMultiplierPerPoint")]
    public float PerceptionSpreadMultiplierPerPoint = 0.03333333f;

    [DataField("perceptionHeavyGunMultiplierPerPoint")]
    public float PerceptionHeavyGunMultiplierPerPoint = 0.01333333f;

    [DataField("perceptionMineDelayMultiplierPerPoint")]
    public float PerceptionMineDelayMultiplierPerPoint = 0.03333333f;

    [DataField("perceptionFireDelayMultiplierPerPoint")]
    public float PerceptionFireDelayMultiplierPerPoint = 0.01333333f;

    // Endurance: survivability, needs, stamina, and toxin resistance.
    [DataField("enduranceStaminaCritThresholdPerPoint")]
    public float EnduranceStaminaCritThresholdPerPoint = 4f;

    [DataField("enduranceHealthModifierPerPoint")]
    public float EnduranceHealthModifierPerPoint = 2.6666667f;

    [DataField("enduranceNeedDecayMultiplierPerPoint")]
    public float EnduranceNeedDecayMultiplierPerPoint = 0.016f;

    [DataField("enduranceStaminaRecoveryMultiplierPerPoint")]
    public float EnduranceStaminaRecoveryMultiplierPerPoint = 0.02666667f;

    [DataField("enduranceToxinDamageMultiplierPerPoint")]
    public float EnduranceToxinDamageMultiplierPerPoint = 0.02f;

    // Charisma: economy, loadout points, presentation, and leadership hooks.
    [DataField("charismaTradeMultiplierPerPoint")]
    public float CharismaTradeMultiplierPerPoint = 0.01333333f;

    [DataField("charismaWarcryRangeMultiplierPerPoint")]
    public float CharismaWarcryRangeMultiplierPerPoint = 0.006667f;

    [DataField("charismaWarcryDurationMultiplierPerPoint")]
    public float CharismaWarcryDurationMultiplierPerPoint = 0.006667f;

    [DataField("charismaWarcrySpeedMultiplierPerPoint")]
    public float CharismaWarcrySpeedMultiplierPerPoint = 0.006667f;

    [DataField("charismaNeutralFollowerMinimum")]
    public int CharismaNeutralFollowerMinimum = 8;

    // Intelligence: crafting/medical quality-of-life gates.
    [DataField("intelligenceLatheTimeMultiplierPerPoint")]
    public float IntelligenceLatheTimeMultiplierPerPoint = 0.06666667f;

    [DataField("intelligenceLatheMaterialUseMultiplierPerPoint")]
    public float IntelligenceLatheMaterialUseMultiplierPerPoint = 0.03333333f;

    // Agility: movement and general action speed.
    [DataField("agilityMovementSpeedMultiplierPerPoint")]
    public float AgilityMovementSpeedMultiplierPerPoint = 0.01f;

    [DataField("agilityActionDelayMultiplierPerPoint")]
    public float AgilityActionDelayMultiplierPerPoint = 0.01333333f;

    // Luck: critical hits and chance-based reward hooks.
    [DataField("luckCriticalChancePerPoint")]
    public float LuckCriticalChancePerPoint = 0.005f;

    [DataField("luckSingleShotCriticalChancePerPoint")]
    public float LuckSingleShotCriticalChancePerPoint = 0.03333333f;

    [DataField("luckCriticalDamageMultiplier")]
    public float LuckCriticalDamageMultiplier = 1.5f;

    [DataField("luckUnluckyDamageMultiplier")]
    public float LuckUnluckyDamageMultiplier = 0.5f;

    [DataField("luckLootChancePerPoint")]
    public float LuckLootChancePerPoint = 0.025f;
}
