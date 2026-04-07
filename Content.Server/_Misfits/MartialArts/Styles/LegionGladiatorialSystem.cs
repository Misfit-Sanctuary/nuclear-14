// #Misfits Add - Legion Gladiatorial combat: brutal slam, sweep, crushing blow, and choke combos
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
/// Legion Gladiatorial Combat — the brutal melee doctrine of Caesar's Legion.
/// Centurions and Legates begin with this style. Focuses on throws, ground-slams, and choke kills.
/// Combos: BodySlam (Harm+Disarm+Grab), FootSweep (Disarm+Disarm), CrushingBlow (Harm+Harm+Harm), NeckGrip (Grab at Suffocate).
/// </summary>
public sealed class LegionGladiatorialSystem : EntitySystem
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
        if (comp.MartialArtsForm != MisfitsMartialArtsForms.LegionGladiatorial || comp.Blocked)
            return;

        var target = args.Target;
        var combo = args.Combo;

        switch (combo.ID)
        {
            case "LegionBodySlam":
                ExecuteBodySlam(uid, target, combo);
                break;
            case "LegionFootSweep":
                ExecuteFootSweep(uid, target, combo);
                break;
            case "LegionCrushingBlow":
                ExecuteCrushingBlow(uid, target, combo);
                break;
            case "LegionNeckGrip":
                ExecuteNeckGrip(uid, target, combo);
                break;
        }
    }

    /// <summary>Body Slam: Harm+Disarm+Grab — throw target forward, deal blunt + stun.</summary>
    private void ExecuteBodySlam(EntityUid uid, EntityUid target, MisfitsComboPrototype combo)
    {
        var dir = (Transform(target).WorldPosition - Transform(uid).WorldPosition).Normalized();

        if (!TryComp<GrabIntentComponent>(uid, out var grabComp))
            return;

        ApplyComboEffects(uid, target, combo);
        _throwing.TryThrow(target, dir * 3f, combo.ThrownSpeed, uid);
        _stun.TryKnockdown(target, TimeSpan.FromSeconds(combo.ParalyzeTime), true);
        _popup.PopupEntity(Loc.GetString("martial-arts-legion-body-slam", ("target", target)), uid, uid, PopupType.Medium);
    }

    /// <summary>Foot Sweep: Disarm+Disarm — sweep the legs, prone the target.</summary>
    private void ExecuteFootSweep(EntityUid uid, EntityUid target, MisfitsComboPrototype combo)
    {
        ApplyComboEffects(uid, target, combo);
        _stun.TryKnockdown(target, TimeSpan.FromSeconds(combo.ParalyzeTime), true);
        _popup.PopupEntity(Loc.GetString("martial-arts-legion-foot-sweep", ("target", target)), uid, uid, PopupType.Small);
    }

    /// <summary>Crushing Blow: Harm+Harm+Harm — heavy consecutive strike, high blunt + stamina crash.</summary>
    private void ExecuteCrushingBlow(EntityUid uid, EntityUid target, MisfitsComboPrototype combo)
    {
        ApplyComboEffects(uid, target, combo);
        _stun.TryStun(uid: target, time: TimeSpan.FromSeconds(combo.ParalyzeTime), refresh: true);
        _popup.PopupEntity(Loc.GetString("martial-arts-legion-crushing-blow", ("target", target)), uid, uid, PopupType.Medium);
    }

    /// <summary>Neck Grip: Grab while already at Suffocate — deals heavy damage and drops all items.</summary>
    private void ExecuteNeckGrip(EntityUid uid, EntityUid target, MisfitsComboPrototype combo)
    {
        if (!TryComp<GrabIntentComponent>(uid, out var grabComp) || grabComp.GrabStage < GrabStage.Suffocate)
            return;

        if (combo.DropItems && TryComp<HandsComponent>(target, out var targetHands))
        {
            foreach (var hand in _hands.EnumerateHands(target, targetHands))
                _hands.TryDrop(target, hand, checkActionBlocker: false, doDropInteraction: false, handsComp: targetHands);
        }

        ApplyComboEffects(uid, target, combo);
        _popup.PopupEntity(Loc.GetString("martial-arts-legion-neck-grip", ("target", target)), uid, uid, PopupType.LargeCaution);
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
