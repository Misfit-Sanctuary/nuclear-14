using Content.Server.Emp;
using Content.Shared._Misfits.C27;
using Content.Shared._Misfits.Emp;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Popups;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.Silicon;

/// <summary>
/// Makes EMP pulses directly damage robot chassis in addition to draining their batteries.
/// C-27s are excluded because their species-specific system supplies its own EMP damage.
/// </summary>
public sealed class RobotEmpSystem : EntitySystem
{
    private const float EmpShockDamage = 75f;

    private static readonly ProtoId<DamageTypePrototype> ShockDamage = "Shock";

    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BorgChassisComponent, EmpPulseEvent>(OnEmpPulse);
    }

    private void OnEmpPulse(Entity<BorgChassisComponent> ent, ref EmpPulseEvent args)
    {
        // Avoid stacking this generic robot damage with the C-27's configurable EMP response.
        if (HasComp<MisfitsC27Component>(ent))
            return;

        if (!_prototypes.TryIndex(ShockDamage, out var shockPrototype))
            return;

        // Chassis-wide EMP trauma is one damage packet, not one packet per body part.
        var damage = new DamageSpecifier(
            shockPrototype,
            EmpShockDamage * MisfitsEmpScaling.GetStrength(args.EnergyConsumption));
        _damageable.TryChangeDamage(ent, damage, ignoreResistances: true, origin: null, doPartDamage: false);

        args.Affected = true;
        _popup.PopupEntity(Loc.GetString("robot-emp-hit"), ent, ent, PopupType.LargeCaution);
    }
}
