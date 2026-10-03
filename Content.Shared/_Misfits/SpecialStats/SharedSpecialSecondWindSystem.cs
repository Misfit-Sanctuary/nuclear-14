using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Standing;
using Robust.Shared.Timing;

namespace Content.Shared._Misfits.SpecialStats;

public abstract class SharedSpecialSecondWindSystem : EntitySystem
{
    [Dependency] protected readonly IGameTiming Timing = default!;
    [Dependency] protected readonly MobThresholdSystem Thresholds = default!;
    [Dependency] protected readonly MovementSpeedModifierSystem MovementSpeed = default!;
    [Dependency] private readonly SharedSpecialSystem _special = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    protected static readonly MobState[] IncapacitatedStates =
    [
        MobState.SoftCritical,
        MobState.Critical,
    ];

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SpecialComponent, DamageChangedEvent>(OnDamageChanged, before: [typeof(MobThresholdSystem)]);
        SubscribeLocalEvent<SpecialComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    public static bool IsActive(SpecialComponent component)
    {
        return component.SecondWindAppliedBonus > FixedPoint2.Zero;
    }

    protected virtual void OnStarted(Entity<SpecialComponent> ent)
    {
    }

    private void OnMobStateChanged(Entity<SpecialComponent> ent, ref MobStateChangedEvent args)
    {
        // removes the 8s sturggle thing
        if (!IsActive(ent.Comp)
            || args.NewMobState != MobState.Alive
            || args.OldMobState is not (MobState.Critical or MobState.SoftCritical)
            || !TryComp<LayingDownComponent>(ent.Owner, out var layingDown))
            return;

        layingDown.PostCritRecoveryOverride = layingDown.SoftCritStandingUpTime;
        Dirty(ent.Owner, layingDown);
    }

    private void OnDamageChanged(Entity<SpecialComponent> ent, ref DamageChangedEvent args)
    {
        if (IsActive(ent.Comp) || Timing.CurTime < ent.Comp.SecondWindCooldownUntil)
            return;

        if (!_mobState.IsAlive(ent.Owner) || !TryComp<MobThresholdsComponent>(ent.Owner, out var thresholds))
            return;

        var tuning = _special.GetTuning();
        if (!_special.HasRequirement(ent.Owner, SpecialStat.Endurance, tuning.EnduranceSecondWindMinimum, ent.Comp))
            return;

        FixedPoint2? lowest = null;
        FixedPoint2? highest = null;
        FixedPoint2? dead = null;
        foreach (var (threshold, state) in thresholds.Thresholds)
        {
            if (state == MobState.Dead)
            {
                dead = threshold;
                continue;
            }

            if (state is not (MobState.SoftCritical or MobState.Critical))
                continue;

            lowest ??= threshold;
            highest = threshold;
        }

        if (lowest == null || highest == null || args.Damageable.TotalDamage < lowest.Value)
            return;

        var bonus = FixedPoint2.New(tuning.EnduranceSecondWindBonusHealth);
        if (dead != null)
            bonus = FixedPoint2.Min(bonus, dead.Value - highest.Value - 1);

        // hit that goes past the added health still dops them
        if (bonus <= FixedPoint2.Zero || args.Damageable.TotalDamage >= lowest.Value + bonus)
            return;

        var now = Timing.CurTime;
        ent.Comp.SecondWindAppliedBonus = bonus;
        ent.Comp.SecondWindActiveUntil = now + TimeSpan.FromSeconds(tuning.EnduranceSecondWindDuration);
        ent.Comp.SecondWindCooldownUntil = now + TimeSpan.FromSeconds(tuning.EnduranceSecondWindCooldown);
        Dirty(ent);

        Thresholds.AdjustMobStateThresholds(ent.Owner, bonus, IncapacitatedStates, thresholds);
        MovementSpeed.RefreshMovementSpeedModifiers(ent.Owner);

        OnStarted(ent);
    }
}
