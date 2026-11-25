using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Systems;
using Content.Shared._N14.Radiation.Components;
using Content.Shared.Radiation.Events;
using Content.Shared.Humanoid; // #Cythisiax Added - identify FEV species for radiation immunity.
using Robust.Shared.Maths;

namespace Content.Server._N14.Radiation;

public sealed partial class RadiationHealingSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;

    private static readonly string[] HealableTypes = { "Blunt", "Slash", "Piercing", "Heat", "Cold", "Caustic",};

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<RadiationHealingComponent, DamageableComponent>();
        while (query.MoveNext(out var uid, out var healing, out var damage))
        {
            var oldExposure = healing.CurrentExposure;

            // Try to heal if we have both radiation and healable damage below the cap.
            var healingNow = false;
            if (healing.CurrentExposure > 0f && damage.TotalDamage < healing.HealCap)
            {
                var healable = GetHealableDamage(damage);
                if (healable > 0f)
                {
                    var radUsed = Math.Min(healing.CurrentExposure, healing.CurrentExposure * frameTime);
                    if (radUsed > 0f)
                    {
                        var healAmount = radUsed * healing.HealFactor;

                        DamageSpecifier spec = new();
                        spec.DamageDict["Blunt"] = FixedPoint2.New(-healAmount);
                        spec.DamageDict["Slash"] = FixedPoint2.New(-healAmount);
                        spec.DamageDict["Piercing"] = FixedPoint2.New(-healAmount);
                        spec.DamageDict["Heat"] = FixedPoint2.New(-healAmount);

                        _damageable.TryChangeDamage(uid, spec, interruptsDoAfters: false);
                        healing.CurrentExposure -= radUsed;
                        healingNow = true;
                    }
                }
            }

            // Natural decay of unused radiation.
            if (healing.CurrentExposure > 0f)
                healing.CurrentExposure = Math.Max(0f, healing.CurrentExposure - healing.DecayRate * frameTime);

            if (!MathHelper.CloseTo(oldExposure, healing.CurrentExposure) || healingNow)
            {
                Dirty(uid, healing);
                _movement.RefreshMovementSpeedModifiers(uid);
            }
        }
    }

    private static float GetHealableDamage(DamageableComponent damage)
    {
        float amount = 0f;
        foreach (var type in HealableTypes)
        {
            if (damage.Damage.DamageDict.TryGetValue(type, out var val) && val > FixedPoint2.Zero)
                amount += val.Float();
        }

        return amount;
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RadiationHealingComponent, OnIrradiatedEvent>(OnIrradiated);
        SubscribeLocalEvent<RadiationHealingComponent, BeforeDamageChangedEvent>(OnBeforeRadiationDamage);
    }

    private void OnIrradiated(EntityUid uid, RadiationHealingComponent component, OnIrradiatedEvent args)
    {
        component.CurrentExposure += args.TotalRads;
        Dirty(uid, component);
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    /// <summary>
    /// Intercepts any Radiation damage (including from reagents with ignoreResistances=true)
    /// and converts it to healing instead. Radiation is stripped from the damage specifier so
    /// it never reaches the health pool or body parts.
    ///
    /// #Cythisiax Fixed - FEV species take no radiation damage; other radiation-healing species retain their existing behavior.
    /// </summary>
    private void OnBeforeRadiationDamage(
        EntityUid uid, RadiationHealingComponent component, ref BeforeDamageChangedEvent args
        )
    {
        if (!args.Damage.DamageDict.TryGetValue("Radiation", out var radDamage) 
            || radDamage <= FixedPoint2.Zero)
            return;

        // #Cythisiax Fixed - FEV species absorb radiation from the environment, food, and reagents without taking radiation damage.
        if (TryComp<HumanoidAppearanceComponent>(uid, out var appearance) &&
            (appearance.Species == "SuperMutant" || appearance.Species == "Nightkin"))
            args.Damage.DamageDict.Remove("Radiation");

        // Convert the intercepted radiation into healing spread across physical damage types
        var healAmount = radDamage.Float() * component.HealPerRad;
        // DamageSpecifier spec = new();
        foreach (var type in HealableTypes)
            if (args.Damage.DamageDict.TryGetValue(type, out var damage))
            {
                var total = damage - healAmount;
                args.Damage.DamageDict.Remove(type);
                args.Damage.DamageDict.Add(type, total);
            }
            else
            {
                args.Damage.DamageDict.Add(type, -healAmount);
            }
            // spec.DamageDict[type] = FixedPoint2.New(-healAmount);

        // _damageable.TryChangeDamage(uid, spec, interruptsDoAfters: false);
    }
}
