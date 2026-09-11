using Content.Server.Body.Systems;
using Content.Server.Temperature.Components;
using Content.Shared._Misfits.Medical.Cryogenics;
using Content.Shared.Damage;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Content.Shared.Medical.Cryogenics;
using Content.Shared._Shitmed.Targeting; // Shitmed Change
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.EntityEffects.Effects;

[UsedImplicitly]
public sealed partial class TemperatureScaledHealthChange : EntityEffect
{
    [DataField(required: true)]
    public DamageSpecifier Damage = default!;

    [DataField]
    public bool ScaleByQuantity;

    [DataField]
    public bool IgnoreResistances = true;

    /// <summary>Tempo (K) at or above which the effect is fully inverted into harm.</summary>
    [DataField]
    public float WarmPoint = 310f;

    /// <summary>Temp (K) at or below which the effect is fully healing.</summary>
    [DataField]
    public float ColdPoint = 250f;

    /// <summary>Magnitude of the multiplier at both extremes - full heal at ColdPoint, full harm at WarmPoint.</summary>
    [DataField]
    public float MaxMultiplier = 3f;

    [DataField]
    public float OutsidePodFraction = 0.01f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        var scale = FixedPoint2.New(1);

        if (args is EntityEffectReagentArgs reagentArgs)
        {
            scale = ScaleByQuantity ? reagentArgs.Quantity * reagentArgs.Scale : reagentArgs.Scale;
        }

        var multiplier = -MaxMultiplier * OutsidePodFraction;
        if (IsInsideCryoPod(args.EntityManager, args.TargetEntity) &&
            args.EntityManager.TryGetComponent<TemperatureComponent>(args.TargetEntity, out var temperature))
        {
            var span = WarmPoint - ColdPoint;
            multiplier = 0f;
            if (span > 0f)
            {
                var t = Math.Clamp((temperature.CurrentTemperature - ColdPoint) / span, 0f, 1f);
                // t=0 at/below ColdPoint -> +MaxMultiplier (full heal); t=1 at/above WarmPoint -> -MaxMultiplier (full harm)
                multiplier = MaxMultiplier * (1f - 2f * t);
            }
        }

        args.EntityManager.System<DamageableSystem>().TryChangeDamage(
            args.TargetEntity,
            Damage * scale * multiplier,
            IgnoreResistances,
            interruptsDoAfters: false,
            // Shitmed Change Start
            targetPart: TargetBodyPart.All,
            partMultiplier: 0.5f,
            canSever: false);
            // Shitmed Change End
    }

    private static bool IsInsideCryoPod(IEntityManager entityManager, EntityUid target)
    {
        if (!entityManager.HasComponent<InsideCryoPodComponent>(target))
            return false;

        var parent = entityManager.GetComponent<TransformComponent>(target).ParentUid;
        return entityManager.HasComponent<MisfitsCryoPodComponent>(parent);
    }
}
