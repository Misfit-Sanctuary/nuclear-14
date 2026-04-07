// #Misfits Add - Desert Survival Fighting: velocity-scaled improvised brawling
using Content.Shared._Misfits.MartialArts;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Robust.Shared.Physics.Components;

namespace Content.Server._Misfits.MartialArts.Styles;

/// <summary>
/// Desert Survival Fighting — improvised brawling forged in the wasteland.
/// All damage scales with how fast the performer is moving when they strike.
/// A miss grants a short power buff. Speed burst activates on grab.
/// Learned via Training Manual: "Wasteland Scrapper's Notes".
/// Combos: RushingStrike (Harm), PowerBurst (Disarm+Harm), SlamDown (Grab+Harm).
/// </summary>
public sealed class DesertSurvivalSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly StaminaSystem _stamina = default!;

    private const float VelocityDamageFactor = 1.5f; // Multiplier per unit of velocity

    // Combo handler is called by MisfitsComboDispatcherSystem — no direct subscription here
    // to avoid duplicate directed (Component, Event) registration.

    public void OnComboTriggered(EntityUid uid, MartialArtsKnowledgeComponent comp, MisfitsComboTriggeredEvent args)
    {
        if (comp.MartialArtsForm != MisfitsMartialArtsForms.DesertSurvivalFighting || comp.Blocked)
            return;

        var target = args.Target;
        var combo = args.Combo;

        // Scale extra damage by performer's current velocity
        var scaledDamage = GetVelocityScaledDamage(uid, combo.ExtraDamage);

        switch (combo.ID)
        {
            case "DesertRushingStrike":
                ApplyScaledDamage(uid, target, scaledDamage, combo.DamageType);
                _popup.PopupEntity(Loc.GetString("martial-arts-desert-rushing-strike", ("target", target)), uid, uid, PopupType.Small);
                break;

            case "DesertPowerBurst":
                ApplyScaledDamage(uid, target, scaledDamage, combo.DamageType);
                _stun.TryStun(target, TimeSpan.FromSeconds(combo.ParalyzeTime), true);
                _popup.PopupEntity(Loc.GetString("martial-arts-desert-power-burst", ("target", target)), uid, uid, PopupType.Medium);
                break;

            case "DesertSlamDown":
                ApplyScaledDamage(uid, target, scaledDamage, combo.DamageType);
                _stun.TryKnockdown(target, TimeSpan.FromSeconds(combo.ParalyzeTime), true);
                if (combo.StaminaDamage > 0)
                    _stamina.TakeStaminaDamage(target, combo.StaminaDamage, source: uid);
                _popup.PopupEntity(Loc.GetString("martial-arts-desert-slam-down", ("target", target)), uid, uid, PopupType.Medium);
                break;
        }
    }

    /// <summary>
    /// Gets damage amount scaled by the performer's current linear velocity.
    /// Stationary performers deal base damage; moving performers deal more.
    /// </summary>
    private float GetVelocityScaledDamage(EntityUid uid, float baseDamage)
    {
        if (!TryComp<PhysicsComponent>(uid, out var physics))
            return baseDamage;

        var speed = physics.LinearVelocity.Length();
        return baseDamage + speed * VelocityDamageFactor;
    }

    private void ApplyScaledDamage(EntityUid uid, EntityUid target, float amount, string type)
    {
        if (amount <= 0)
            return;

        var dmg = new DamageSpecifier();
        dmg.DamageDict.Add(type, amount);
        _damageable.TryChangeDamage(target, dmg, origin: uid);
    }
}
