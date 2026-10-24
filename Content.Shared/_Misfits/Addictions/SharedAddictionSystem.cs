// #Misfits Change - Ported from Delta-V addiction system
using Content.Shared.Damage;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.StatusEffect;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._Misfits.Addictions;

/// <summary>
///     Shared addiction system. Handles applying and suppressing addictions
///     via the StatusEffectsSystem. Server overrides provide update/popup logic.
/// </summary>
public abstract class SharedAddictionSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly StatusEffectsSystem _statusEffects = default!;

    private static readonly TimeSpan ExposureGap = TimeSpan.FromSeconds(15);

    /// <summary>
    ///     Status effect key used for the addiction status.
    /// </summary>
    public ProtoId<StatusEffectPrototype> StatusEffectKey = "Addicted";

    /// <summary>
    ///     Server-side time bookkeeping for suppression windows.
    /// </summary>
    protected abstract void UpdateTime(EntityUid uid);

    /// <summary>
    ///     Attempts to apply an addiction to the entity.
    ///     If the entity already has the effect, extends its duration.
    ///     If the entity is not yet addicted, repeated exposures to the specific reagent are tracked
    ///     until the configured threshold is reached.
    ///     Calls <see cref="OnAddictionApplied"/> so the server can send drug-specific chat messages.
    /// </summary>
    /// <param name="drugId">Prototype ID of the addictive reagent. Null falls back to immediate addiction.</param>
    /// <param name="drugName">Localized name of the drug (e.g. "hydra"). Empty skips chat messages.</param>
    /// <param name="addictionThreshold">Number of exposures required before addiction starts.</param>
    /// <returns>
    ///     True if the entity is addicted after this call and withdrawal data should be updated.
    ///     False if the exposure was only recorded toward the threshold.
    /// </returns>
    public virtual bool TryApplyAddiction(
        EntityUid uid,
        float addictionTime,
        ProtoId<ReagentPrototype>? drugId = null,
        string drugName = "",
        int addictionThreshold = 2,
        FixedPoint2? currentQuantity = null,
        StatusEffectsComponent? status = null)
    {
        if (!Resolve(uid, ref status, false))
            return false;

        UpdateTime(uid);

        // #Misfits Change /Tweak:/ Track whether this is a new addiction or an existing one deepening
        var isNew = !_statusEffects.HasStatusEffect(uid, StatusEffectKey, status);

        if (isNew)
        {
            var threshold = Math.Max(1, addictionThreshold);

            if (threshold > 1 && drugId != null)
            {
                var exposure = EnsureComp<AddictionExposureComponent>(uid);
                var reagentId = drugId.Value;
                var now = _timing.CurTime;
                var isNewExposure = true;

                if (exposure.LastSeenTimes.TryGetValue(reagentId, out var lastSeen))
                    isNewExposure = now - lastSeen > ExposureGap;

                if (!isNewExposure
                    && currentQuantity != null
                    && exposure.LastSeenQuantities.TryGetValue(reagentId, out var lastQuantity)
                    && currentQuantity.Value > lastQuantity)
                {
                    isNewExposure = true;
                }

                exposure.LastSeenTimes[reagentId] = now;

                if (currentQuantity != null)
                    exposure.LastSeenQuantities[reagentId] = currentQuantity.Value;

                exposure.ExposureCounts.TryGetValue(reagentId, out var count);

                if (isNewExposure)
                    count++;

                if (count < threshold)
                {
                    exposure.ExposureCounts[reagentId] = count;
                    return false;
                }

                exposure.ExposureCounts.Remove(reagentId);
                exposure.LastSeenTimes.Remove(reagentId);
                exposure.LastSeenQuantities.Remove(reagentId);
            }
        }

        if (isNew)
        {
            _statusEffects.TryAddStatusEffect<AddictedComponent>(
                uid,
                StatusEffectKey,
                TimeSpan.FromSeconds(addictionTime),
                false,
                status);
        }
        else
        {
            _statusEffects.TryAddTime(uid, StatusEffectKey, TimeSpan.FromSeconds(addictionTime), status);
        }

        // Store drug name and increment dose count on the component
        if (TryComp<AddictedComponent>(uid, out var addicted))
        {
            if (!string.IsNullOrEmpty(drugName))
                addicted.DrugName = drugName;

            addicted.DoseCount++;
        }

        OnAddictionApplied(uid, isNew);
        return true;
    }

    // #Misfits Change /Add:/ Store per-drug withdrawal effect parameters on the component.
    /// <summary>
    ///     Stores withdrawal gameplay effect parameters on the entity's <see cref="AddictedComponent"/>.
    ///     Called by the <c>Addicting</c> reagent effect after <see cref="TryApplyAddiction"/>.
    ///     Only updates a field if the new value represents a stronger effect than what is already set,
    ///     preventing a milder drug from overriding a harsher one's withdrawal on a multi-drug user.
    /// </summary>
    public void SetWithdrawalEffects(
        EntityUid uid,
        string moodEffect = "",
        DamageSpecifier? damage = null,
        float speedPenalty = 1.0f,
        float staminaDrain = 0.0f)
    {
        if (!TryComp<AddictedComponent>(uid, out var addicted))
            return;

        // Only update mood if not already set (first drug setting mood wins across different drugs)
        if (!string.IsNullOrEmpty(moodEffect) && string.IsNullOrEmpty(addicted.WithdrawalMoodEffect))
            addicted.WithdrawalMoodEffect = moodEffect;

        // Take the highest damage value
        if (damage != null)
            addicted.WithdrawalDamage = damage;

        // Take the lowest (most punishing) speed penalty
        if (speedPenalty < addicted.WithdrawalSpeedPenalty)
            addicted.WithdrawalSpeedPenalty = speedPenalty;

        // Take the highest stamina drain
        if (staminaDrain > addicted.WithdrawalStaminaDrain)
            addicted.WithdrawalStaminaDrain = staminaDrain;
    }

    /// <summary>
    ///     Called after addiction is applied or extended.
    ///     Server override uses this to send drug-specific chat messages based on dose count / severity.
    /// </summary>
    protected virtual void OnAddictionApplied(EntityUid uid, bool isNew) { }

    /// <summary>
    ///     Suppresses active addiction symptoms for a duration.
    /// </summary>
    public virtual void TrySuppressAddiction(EntityUid uid, float duration)
    {
        if (!TryComp<AddictedComponent>(uid, out var addicted))
            return;

        UpdateAddictionSuppression(uid, addicted, duration);
    }

    /// <summary>
    ///     Marks the addiction as suppressed and updates the suppression end time.
    /// </summary>
    protected void UpdateAddictionSuppression(EntityUid uid, AddictedComponent component, float duration)
    {
        component.Suppressed = true;
        Dirty(uid, component);
    }
}
