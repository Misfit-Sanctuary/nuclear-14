// #Misfits Add - Ranger Combat Technique: precise disarms, joint locks, and takedowns
using Content.Shared._Misfits.Grab;
using Content.Shared._Misfits.MartialArts;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Misfits.MartialArts.Styles;

/// <summary>
/// Ranger Combat Technique — the precision fighting doctrine of the Desert Rangers.
/// Veteran Rangers and Ranger Chiefs begin with this style.
/// Focuses on quick disarms, joint locks (armbar at Suffocate), and takedown throws.
/// Combos: QuickDisarm (Disarm+Harm), PressurePoint (Harm+Disarm+Disarm), TakedownThrow (Grab+Harm), ArmBar (Grab+Grab at Hard+).
/// </summary>
public sealed class RangerCombatSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly GrabIntentSystem _grab = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly StaminaSystem _stamina = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;

    // Combo handler is called by MisfitsComboDispatcherSystem — no direct subscription here
    // to avoid duplicate directed (Component, Event) registration.

    public void OnComboTriggered(EntityUid uid, MartialArtsKnowledgeComponent comp, MisfitsComboTriggeredEvent args)
    {
        if (comp.MartialArtsForm != MisfitsMartialArtsForms.RangerCombatTechnique || comp.Blocked)
            return;

        var target = args.Target;
        var combo = args.Combo;

        switch (combo.ID)
        {
            case "RangerQuickDisarm":
                ExecuteQuickDisarm(uid, target, combo);
                break;
            case "RangerPressurePoint":
                ExecutePressurePoint(uid, target, combo);
                break;
            case "RangerTakedownThrow":
                ExecuteTakedownThrow(uid, target, combo);
                break;
            case "RangerArmBar":
                ExecuteArmBar(uid, target, combo);
                break;
        }
    }

    /// <summary>Quick Disarm: Disarm+Harm — strip the target's weapon and stagger them.</summary>
    private void ExecuteQuickDisarm(EntityUid uid, EntityUid target, MisfitsComboPrototype combo)
    {
        if (TryComp<HandsComponent>(target, out var targetHands))
        {
            foreach (var hand in _hands.EnumerateHands(target, targetHands))
                _hands.TryDrop(target, hand, checkActionBlocker: false, doDropInteraction: false, handsComp: targetHands);
        }
        ApplyComboEffects(uid, target, combo);
        _popup.PopupEntity(Loc.GetString("martial-arts-ranger-quick-disarm", ("target", target)), uid, uid, PopupType.Small);
    }

    /// <summary>Pressure Point: Harm+Disarm+Disarm — stun and drain stamina from a precision nerve strike.</summary>
    private void ExecutePressurePoint(EntityUid uid, EntityUid target, MisfitsComboPrototype combo)
    {
        ApplyComboEffects(uid, target, combo);
        _stun.TryStun(target, TimeSpan.FromSeconds(combo.ParalyzeTime), true);
        _popup.PopupEntity(Loc.GetString("martial-arts-ranger-pressure-point", ("target", target)), uid, uid, PopupType.Medium);
    }

    /// <summary>Takedown Throw: Grab+Harm — leveraged throw dealing blunt damage on landing.</summary>
    private void ExecuteTakedownThrow(EntityUid uid, EntityUid target, MisfitsComboPrototype combo)
    {
        var dir = (Transform(target).WorldPosition - Transform(uid).WorldPosition).Normalized();

        ApplyComboEffects(uid, target, combo);
        _throwing.TryThrow(target, dir * 2f, combo.ThrownSpeed, uid);
        _stun.TryKnockdown(target, TimeSpan.FromSeconds(combo.ParalyzeTime), true);
        _popup.PopupEntity(Loc.GetString("martial-arts-ranger-takedown-throw", ("target", target)), uid, uid, PopupType.Medium);
    }

    /// <summary>Arm Bar: Grab+Grab at Hard+ — locks the target in an arm bar; high stamina drain and paralysis.</summary>
    private void ExecuteArmBar(EntityUid uid, EntityUid target, MisfitsComboPrototype combo)
    {
        if (!TryComp<GrabIntentComponent>(uid, out var grabComp) || grabComp.GrabStage < GrabStage.Hard)
            return;

        // Force to Suffocate stage to represent the lock
        if (TryComp<GrabbableComponent>(target, out var grabbable))
            _grab.TrySetGrabStages(uid, grabComp, target, grabbable, GrabStage.Suffocate);

        ApplyComboEffects(uid, target, combo);
        _stun.TryKnockdown(target, TimeSpan.FromSeconds(combo.ParalyzeTime), true);
        _popup.PopupEntity(Loc.GetString("martial-arts-ranger-arm-bar", ("target", target)), uid, uid, PopupType.LargeCaution);
    }

    private void ApplyComboEffects(EntityUid uid, EntityUid target, MisfitsComboPrototype combo)
    {
        if (combo.ExtraDamage > 0)
        {
            var dmg = new DamageSpecifier();
            dmg.DamageDict.Add(combo.DamageType, combo.ExtraDamage);
            _damageable.TryChangeDamage(target, dmg, origin: uid);
        }

        if (combo.StaminaDamage > 0)
            _stamina.TakeStaminaDamage(target, combo.StaminaDamage, source: uid);
    }
}
