using Content.Server.Emp;
using Content.Shared._Misfits.C27;
using Content.Shared._Misfits.Emp;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;

// #Misfits Add - Server EMP handler for the C-27 humanoid robot species. Subscribes to
// EmpPulseEvent on entities carrying MisfitsC27Component and applies Shock damage scaled by
// the pulse's energy budget plus the configured stun. Server-only because EMP damage and
// status effects are authoritative on the server.
namespace Content.Server._Misfits.C27;

// #Misfits Add - C-27 humanoid robot EMP handler. Spec: EMP pulses drain power cells AND inflict
// posibrain damage; optional PA-style stun. We model the posibrain damage as Shock damage to the
// chassis (the brain is an organ inside the body — damaging the mob propagates through the
// damageable). Battery drain is left to the existing Battery / power-cell EmpPulseEvent
// subscribers — if the C-27 ever gets a power cell slot, it will already be handled.
public sealed class MisfitsC27EmpSystem : EntitySystem
{
    private static readonly ProtoId<DamageTypePrototype> ShockDamage = "Shock";

    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MisfitsC27Component, EmpPulseEvent>(OnEmpPulse);
    }

    private void OnEmpPulse(Entity<MisfitsC27Component> ent, ref EmpPulseEvent args)
    {
        // A hand pulse grenade is full strength. Chemical and other weaker EMPs deal the same
        // fraction of this prototype's configured damage ceiling as their battery-drain energy.
        var totalShock = ent.Comp.MaxEmpShockDamage * MisfitsEmpScaling.GetStrength(args.EnergyConsumption);

        // Apply the electrical trauma once to the chassis. Originless damage is otherwise treated
        // like an explosion by the body system and copied to every limb.
        if (_proto.TryIndex(ShockDamage, out var shockProto))
        {
            var damage = new DamageSpecifier(shockProto, totalShock);
            _damageable.TryChangeDamage(ent, damage, ignoreResistances: true, origin: null, doPartDamage: false);
        }

        // Mark Affected so the EMP visual effect spawns over the chassis.
        args.Affected = true;

        // Optional PA-style stun: sets the EmpDisabled component so the mob is locked out of
        // interactions for the pulse duration. EmpSystem.DoEmpEffects handles the actual
        // EnsureComp<EmpDisabledComponent> when args.Disabled is true.
        if (ent.Comp.ApplyEmpStun)
            args.Disabled = true;

        _popup.PopupEntity(Loc.GetString("c27-emp-hit"), ent, ent);
    }
}
