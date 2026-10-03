using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Robust.Shared.Maths;

namespace Content.Shared._Misfits.SpecialStats;

/// <summary>
/// Applies Endurance health bonuses when S.P.E.C.I.A.L. changes.
/// </summary>
public sealed class SpecialEnduranceSystem : EntitySystem
{
    [Dependency] private readonly SharedSpecialSystem _special = default!;
    [Dependency] private readonly MobThresholdSystem _thresholds = default!;

    private static readonly MobState[] HealthThresholdStates =
    [
        MobState.SoftCritical,
        MobState.Critical,
        MobState.Dead,
    ];

    private static readonly string[] EnduranceResistedDamageTypes =
    [
        "Poison",
        "Radiation",
        "Caustic",
        "Cellular",
    ];

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SpecialComponent, SpecialChangedEvent>(OnSpecialChanged);
        SubscribeLocalEvent<SpecialComponent, SpecialStatsReadyEvent>(OnStatsReady);
        SubscribeLocalEvent<SpecialComponent, SpecialShutdownEvent>(OnSpecialShutdown);
        SubscribeLocalEvent<SpecialComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<SpecialComponent, ModifySlowOnDamageSpeedEvent>(OnModifySlowOnDamageSpeed);
    }

    private void OnModifySlowOnDamageSpeed(Entity<SpecialComponent> ent, ref ModifySlowOnDamageSpeedEvent args)
    {
        // ignores damage slowdown entirely
        if (SharedSpecialSecondWindSystem.IsActive(ent.Comp))
        {
            args.Speed = 1f;
            return;
        }

        var tuning = _special.GetTuning();
        var modifier = _special.GetCurvedEffectModifier(
            ent.Owner,
            SpecialStat.Endurance,
            -tuning.EnduranceDamageSlowdownMultiplierPerPoint,
            ent.Comp);
        var slowdown = (1f - args.Speed) * MathF.Max(0f, 1f + modifier);

        args.Speed = MathF.Max(0.1f, 1f - slowdown);
    }

    private void OnSpecialChanged(Entity<SpecialComponent> ent, ref SpecialChangedEvent args)
    {
        ApplyEndurance(ent, false);
    }

    private void OnStatsReady(Entity<SpecialComponent> ent, ref SpecialStatsReadyEvent args)
    {
        ApplyEndurance(ent, false);
    }

    private void OnSpecialShutdown(Entity<SpecialComponent> ent, ref SpecialShutdownEvent args)
    {
        ApplyEndurance(ent, true);
    }

    private void OnDamageModify(Entity<SpecialComponent> ent, ref DamageModifyEvent args)
    {
        if (!args.Damage.AnyPositive())
            return;

        var tuning = _special.GetTuning();
        var modifier = _special.GetCurvedEffectModifier(
            ent.Owner,
            SpecialStat.Endurance,
            -tuning.EnduranceToxinDamageMultiplierPerPoint,
            ent.Comp);
        var multiplier = MathF.Max(0.1f, 1f + modifier);

        if (MathHelper.CloseTo(multiplier, 1f))
            return;

        var damage = new DamageSpecifier(args.Damage);
        var modified = false;

        foreach (var type in EnduranceResistedDamageTypes)
        {
            if (!damage.DamageDict.TryGetValue(type, out var value) || value <= FixedPoint2.Zero)
                continue;

            damage.DamageDict[type] = value * multiplier;
            modified = true;
        }

        if (modified)
            args.Damage = damage;
    }

    private void ApplyEndurance(Entity<SpecialComponent> ent, bool reset)
    {
        ClearLegacyStaminaModifier(ent);

        ApplyHealthThresholds(ent, reset);
        ApplyHungerDecay(ent, reset);
        ApplyThirstDecay(ent, reset);
        ApplyStaminaRecovery(ent, reset);
    }

    private void ApplyHealthThresholds(Entity<SpecialComponent> ent, bool reset)
    {
        if (!TryComp<MobThresholdsComponent>(ent.Owner, out var thresholds))
            return;

        var tuning = _special.GetTuning();
        var desired = reset
            ? 0f
            : _special.GetCurvedEffectModifier(
                ent.Owner,
                SpecialStat.Endurance,
                tuning.EnduranceHealthModifierPerPoint,
                ent.Comp);
        var adjustment = desired - ent.Comp.AppliedHealthThresholdModifier;

        if (MathHelper.CloseTo(adjustment, 0f))
            return;

        foreach (var state in HealthThresholdStates)
        {
            var threshold = _thresholds.GetThresholdForState(ent.Owner, state, thresholds);
            if (threshold == FixedPoint2.Zero)
                continue;

            _thresholds.SetMobStateThreshold(ent.Owner, FixedPoint2.Max(1, threshold + adjustment), state, thresholds);
        }

        ent.Comp.AppliedHealthThresholdModifier = desired;

        Dirty(ent.Owner, ent.Comp);
    }

    private void ApplyHungerDecay(Entity<SpecialComponent> ent, bool reset)
    {
        if (!TryComp<HungerComponent>(ent.Owner, out var hunger))
            return;

        var desired = reset ? 1f : GetNeedDecayMultiplier(ent);
        var previous = ent.Comp.AppliedHungerDecayMultiplier <= 0f
            ? 1f
            : ent.Comp.AppliedHungerDecayMultiplier;

        if (MathHelper.CloseTo(desired, previous))
            return;

        var ratio = desired / previous;
        hunger.BaseDecayRate *= ratio;
        hunger.ActualDecayRate *= ratio;
        ent.Comp.AppliedHungerDecayMultiplier = desired;

        Dirty(ent.Owner, hunger);
        Dirty(ent.Owner, ent.Comp);
    }

    private void ApplyThirstDecay(Entity<SpecialComponent> ent, bool reset)
    {
        if (!TryComp<ThirstComponent>(ent.Owner, out var thirst))
            return;

        var desired = reset ? 1f : GetNeedDecayMultiplier(ent);
        var previous = ent.Comp.AppliedThirstDecayMultiplier <= 0f
            ? 1f
            : ent.Comp.AppliedThirstDecayMultiplier;

        if (MathHelper.CloseTo(desired, previous))
            return;

        var ratio = desired / previous;
        thirst.BaseDecayRate *= ratio;
        thirst.ActualDecayRate *= ratio;
        ent.Comp.AppliedThirstDecayMultiplier = desired;

        Dirty(ent.Owner, thirst);
        Dirty(ent.Owner, ent.Comp);
    }

    private void ApplyStaminaRecovery(Entity<SpecialComponent> ent, bool reset)
    {
        if (!TryComp<StaminaComponent>(ent.Owner, out var stamina))
            return;

        var desired = reset ? 1f : GetStaminaRecoveryMultiplier(ent);
        var previous = ent.Comp.AppliedStaminaRecoveryMultiplier <= 0f
            ? 1f
            : ent.Comp.AppliedStaminaRecoveryMultiplier;

        if (MathHelper.CloseTo(desired, previous))
            return;

        stamina.Decay *= desired / previous;
        ent.Comp.AppliedStaminaRecoveryMultiplier = desired;

        Dirty(ent.Owner, stamina);
        Dirty(ent.Owner, ent.Comp);
    }

    private float GetNeedDecayMultiplier(Entity<SpecialComponent> ent)
    {
        var tuning = _special.GetTuning();
        var modifier = _special.GetCurvedEffectModifier(
            ent.Owner,
            SpecialStat.Endurance,
            -tuning.EnduranceNeedDecayMultiplierPerPoint,
            ent.Comp);

        return MathF.Max(0.1f, 1f + modifier);
    }

    private float GetStaminaRecoveryMultiplier(Entity<SpecialComponent> ent)
    {
        var tuning = _special.GetTuning();
        var modifier = _special.GetCurvedEffectModifier(
            ent.Owner,
            SpecialStat.Endurance,
            tuning.EnduranceStaminaRecoveryMultiplierPerPoint,
            ent.Comp);

        return MathF.Max(0.1f, 1f + modifier);
    }

    private void ClearLegacyStaminaModifier(Entity<SpecialComponent> ent)
    {
        if (MathHelper.CloseTo(ent.Comp.AppliedStaminaCritThresholdModifier, 0f))
            return;

        if (TryComp<StaminaComponent>(ent.Owner, out var stamina))
        {
            stamina.CritThreshold -= ent.Comp.AppliedStaminaCritThresholdModifier;
            Dirty(ent.Owner, stamina);
        }

        ent.Comp.AppliedStaminaCritThresholdModifier = 0f;
        Dirty(ent.Owner, ent.Comp);
    }
}
