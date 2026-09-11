using Content.Server.Explosion.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared._Misfits.PowerArmor;
using Content.Shared.Vehicles;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.Hawkins;

/// <summary>
/// Applies the Hawkins device's focused anti-armor damage when it receives any trigger,
/// including a linked remote signal while the charge is lying loose or still airborne.
/// </summary>
public sealed class HawkinsShapedChargeSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    private readonly HashSet<EntityUid> _nearby = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HawkinsShapedChargeComponent, TriggerEvent>(OnTriggered);
    }

    private void OnTriggered(EntityUid uid, HawkinsShapedChargeComponent component, TriggerEvent args)
    {
        _nearby.Clear();
        _lookup.GetEntitiesInRange(Transform(uid).Coordinates, component.Range, _nearby);

        var structural = _prototypes.Index<DamageTypePrototype>("Structural");

        foreach (var target in _nearby)
        {
            // PowerArmorWorn tracks the actual armor item, so this damages integrity instead
            // of bypassing the suit and directly injuring the wearer.
            if (TryComp<PowerArmorWornComponent>(target, out var worn) && Exists(worn.Armor))
            {
                _damageable.TryChangeDamage(
                    worn.Armor,
                    new DamageSpecifier(structural, FixedPoint2.New(component.ArmorDamage)),
                    ignoreResistances: true,
                    origin: uid);
            }

            if (HasComp<MotorbikeComponent>(target))
            {
                _damageable.TryChangeDamage(
                    target,
                    new DamageSpecifier(structural, FixedPoint2.New(component.VehicleDamage)),
                    ignoreResistances: true,
                    origin: uid);
            }
        }

        _nearby.Clear();
    }
}
