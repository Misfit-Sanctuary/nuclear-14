// #Misfits Add - Shadow Strike Style: sneak attack multiplier and instant neck grab
using Content.Shared._Misfits.Grab;
using Content.Shared._Misfits.MartialArts;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;

namespace Content.Server._Misfits.MartialArts.Styles;

/// <summary>
/// Shadow Strike — surgical stealth attacks and ambush control.
/// Applying damage against a target that is NOT in combat mode deals bonus damage (sneak multiplier).
/// A well-placed Hug combo can instantly force a Suffocate grab directly from stage None.
/// Learned via Training Manual: "Followers of the Apocalypse Nerve Manual".
/// Combos: SilentBlow (Harm), ShadowHug (Hug — requires target not in combat mode), ShadowDisarm (Disarm+Harm).
/// </summary>
public sealed class ShadowStrikeSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly StaminaSystem _stamina = default!;
    [Dependency] private readonly SharedCombatModeSystem _combatMode = default!;
    [Dependency] private readonly GrabIntentSystem _grab = default!;

    private const float SneakDamageMultiplier = 1.5f;

    // Combo handler is called by MisfitsComboDispatcherSystem — no direct subscription here
    // to avoid duplicate directed (Component, Event) registration.

    public void OnComboTriggered(EntityUid uid, MartialArtsKnowledgeComponent comp, MisfitsComboTriggeredEvent args)
    {
        if (comp.MartialArtsForm != MisfitsMartialArtsForms.ShadowStrike || comp.Blocked)
            return;

        var target = args.Target;
        var combo = args.Combo;

        // Check if target is unaware (not in combat mode) for sneak bonus
        var targetUnaware = !_combatMode.IsInCombatMode(target);

        switch (combo.ID)
        {
            case "ShadowSilentBlow":
                // Baseline strike — doubled if target is unaware
                var damage = targetUnaware
                    ? combo.ExtraDamage * SneakDamageMultiplier
                    : combo.ExtraDamage;

                ApplyDamage(uid, target, damage, combo.DamageType);

                _popup.PopupEntity(targetUnaware
                    ? Loc.GetString("martial-arts-shadow-silent-blow-sneak", ("target", target))
                    : Loc.GetString("martial-arts-shadow-silent-blow", ("target", target)), uid, uid, PopupType.Small);
                break;

            case "ShadowHug":
                // Instantly jump to Suffocate grab — only works if target is unaware
                if (!targetUnaware)
                {
                    _popup.PopupEntity(Loc.GetString("martial-arts-shadow-hug-failed"), uid, uid, PopupType.Small);
                    break;
                }

                // Force grab straight to Suffocate via TryGrab + force-escalate
                _grab.TryForceGrabStage(uid, target, GrabStage.Suffocate);
                _popup.PopupEntity(Loc.GetString("martial-arts-shadow-hug", ("target", target)), uid, uid, PopupType.Medium);
                break;

            case "ShadowDisarm":
                // Disarm into a quick blow — apply bonus damage if target is off-guard
                var disarmDamage = targetUnaware
                    ? combo.ExtraDamage * SneakDamageMultiplier
                    : combo.ExtraDamage;

                ApplyDamage(uid, target, disarmDamage, combo.DamageType);

                if (combo.DropItems)
                {
                    // Drop held items via stub — handled in GrabIntentSystem via StageChange
                    _stun.TryStun(target, TimeSpan.FromSeconds(combo.ParalyzeTime), true);
                }

                _popup.PopupEntity(Loc.GetString("martial-arts-shadow-disarm", ("target", target)), uid, uid, PopupType.Small);
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
