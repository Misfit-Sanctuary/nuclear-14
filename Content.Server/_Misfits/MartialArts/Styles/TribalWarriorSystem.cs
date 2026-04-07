// #Misfits Add - Tribal Warrior Fighting Style: primal combat, consecutive-strike scaling, gun suppression
using Content.Shared._Misfits.MartialArts;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Server._Misfits.MartialArts.Styles;

/// <summary>
/// Tribal Warrior Style — primal, raw, and relentless.
/// Consecutive unarmed strikes build power (ConsecutiveGnashes), escalating damage.
/// Warriors who meditate on this form reject firearms; wielding one in combat mode is suppressed.
/// Learned via Training Manual: "Khans Tribal Combat Codex".
/// Combos: SavageGnash (Harm), WarCry (Harm+Harm+Harm), TribeStomp (Disarm+Disarm).
/// </summary>
public sealed class TribalWarriorSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly StaminaSystem _stamina = default!;
    [Dependency] private readonly SharedCombatModeSystem _combatMode = default!;

    private const float GnashDamageMultiplierPerStack = 0.25f; // +25% damage per consecutive gnash
    private const int MaxGnashStacks = 5;

    // Combo + ShotAttempted handlers are called by MisfitsComboDispatcherSystem —
    // no direct subscription here to avoid duplicate directed (Component, Event) registration.

    public void OnShotAttempted(EntityUid uid, MartialArtsKnowledgeComponent comp, ref ShotAttemptedEvent args)
    {
        if (comp.MartialArtsForm != MisfitsMartialArtsForms.TribalWarriorStyle || comp.Blocked)
            return;

        // Only suppress in active combat mode — allows holstering etc. out of combat
        if (!_combatMode.IsInCombatMode(uid))
            return;

        _popup.PopupEntity(Loc.GetString("martial-arts-tribal-no-guns"), uid, uid, PopupType.Small);
        args.Cancel();
    }

    public void OnComboTriggered(EntityUid uid, MartialArtsKnowledgeComponent comp, MisfitsComboTriggeredEvent args)
    {
        if (comp.MartialArtsForm != MisfitsMartialArtsForms.TribalWarriorStyle || comp.Blocked)
            return;

        var target = args.Target;
        var combo = args.Combo;

        if (!TryComp<CanPerformComboComponent>(uid, out var comboComp))
            return;

        switch (combo.ID)
        {
            case "TribalSavageGnash":
                // Consecutive gnash: damage scales per stack, then resets
                var gnashStacks = Math.Min(comboComp.ConsecutiveGnashes, MaxGnashStacks);
                var scaledDamage = combo.ExtraDamage * (1f + gnashStacks * GnashDamageMultiplierPerStack);

                ApplyDamage(uid, target, scaledDamage, combo.DamageType);
                comboComp.ConsecutiveGnashes++;

                if (gnashStacks >= MaxGnashStacks)
                {
                    // Max stacks reached — release a powerful surge and reset
                    _popup.PopupEntity(Loc.GetString("martial-arts-tribal-gnash-surge"), uid, uid, PopupType.Large);
                    comboComp.ConsecutiveGnashes = 0;
                }
                else
                {
                    _popup.PopupEntity(Loc.GetString("martial-arts-tribal-gnash", ("target", target), ("stacks", gnashStacks + 1)), uid, uid, PopupType.Small);
                }
                break;

            case "TribalWarCry":
                // A sequence of three strikes that staggers and knocks down
                comboComp.ConsecutiveGnashes = 0; // Expend gnash stacks on war cry
                ApplyDamage(uid, target, combo.ExtraDamage, combo.DamageType);
                _stun.TryKnockdown(target, TimeSpan.FromSeconds(combo.ParalyzeTime), true);
                if (combo.StaminaDamage > 0)
                    _stamina.TakeStaminaDamage(target, combo.StaminaDamage, source: uid);
                _popup.PopupEntity(Loc.GetString("martial-arts-tribal-war-cry", ("target", target)), uid, uid, PopupType.Medium);
                break;

            case "TribalTribeStomp":
                // Double disarm opening into a stomp — knocks down and deals damage
                ApplyDamage(uid, target, combo.ExtraDamage, combo.DamageType);
                _stun.TryStun(target, TimeSpan.FromSeconds(combo.ParalyzeTime), true);
                _popup.PopupEntity(Loc.GetString("martial-arts-tribal-tribe-stomp", ("target", target)), uid, uid, PopupType.Medium);
                break;
        }
    }

    private void ApplyDamage(EntityUid uid, EntityUid target, float amount, string type)
    {
        if (amount <= 0)
            return;

        var dmg = new DamageSpecifier();
        dmg.DamageDict.Add(type, amount);
        _damageable.TryChangeDamage(target, dmg, origin: uid);
    }
}
