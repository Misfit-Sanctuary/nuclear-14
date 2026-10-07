using Content.Server.Emp;
using Content.Shared.EntityEffects;
using Content.Shared.Chemistry.Reagent;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server.EntityEffects.Effects;


[DataDefinition]
public sealed partial class EmpReactionEffect : EntityEffect
{
    /// <summary>
    ///     Impulse range per unit of quantity
    /// </summary>
    [DataField("rangePerUnit")]
    public float EmpRangePerUnit = 0.5f;

    /// <summary>
    ///     Maximum impulse range
    /// </summary>
    [DataField("maxRange")]
    public float EmpMaxRange = 10;

    /// <summary>
    ///     How much energy will be drain from sources
    /// </summary>
    [DataField]
    public float EnergyConsumption = 12500;

    /// <summary>
    ///     Optional battery-drain energy per reaction unit. When configured, this replaces the
    ///     fixed <see cref="EnergyConsumption"/> value and lets larger reactions create stronger EMPs.
    /// </summary>
    [DataField]
    public float? EnergyConsumptionPerUnit;

    /// <summary>Maximum energy produced by a quantity-scaled reaction.</summary>
    [DataField]
    public float? MaxEnergyConsumption;

    /// <summary>
    ///     Amount of time entities will be disabled
    /// </summary>
    [DataField("duration")]
    public float DisableDuration = 15;

    /// <summary>Optional disable duration contributed by each reaction unit.</summary>
    [DataField]
    public float? DisableDurationPerUnit;

    /// <summary>Maximum disable duration produced by a quantity-scaled reaction.</summary>
    [DataField]
    public float? MaxDisableDuration;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
            => Loc.GetString("reagent-effect-guidebook-emp-reaction-effect", ("chance", Probability));

    public override void Effect(EntityEffectBaseArgs args)
    {
        var tSys = args.EntityManager.System<TransformSystem>();
        var transform = args.EntityManager.GetComponent<TransformComponent>(args.TargetEntity);

        var quantity = 1f;

        if (args is EntityEffectReagentArgs reagentArgs)
            quantity = (float) reagentArgs.Quantity;

        var range = MathF.Min(quantity * EmpRangePerUnit, EmpMaxRange);
        var energyConsumption = EnergyConsumptionPerUnit is { } energyPerUnit
            ? MathF.Min(quantity * energyPerUnit, MaxEnergyConsumption ?? float.PositiveInfinity)
            : EnergyConsumption;
        var disableDuration = DisableDurationPerUnit is { } durationPerUnit
            ? MathF.Min(quantity * durationPerUnit, MaxDisableDuration ?? float.PositiveInfinity)
            : DisableDuration;

        args.EntityManager.System<EmpSystem>()
            .EmpPulse(tSys.GetMapCoordinates(args.TargetEntity, xform: transform),
            range,
            energyConsumption,
            disableDuration);
    }
}
