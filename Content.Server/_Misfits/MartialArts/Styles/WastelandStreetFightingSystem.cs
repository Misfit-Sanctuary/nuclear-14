// #Misfits Add - Wasteland Street Fighting: dirty brawling with throws, eye pokes, and wheel throws
using Content.Server._Misfits.Grab;
using Content.Shared._Misfits.Grab;
using Content.Shared._Misfits.MartialArts;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Popups;
using Content.Shared.StatusEffect;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Shared.Random;

namespace Content.Server._Misfits.MartialArts.Styles;

/// <summary>
/// Wasteland Street Fighting — no rules, no honor, just survival.
/// Specializes in throws, dirty tricks, and debilitating finishers.
/// Learned via Training Manual: "Mojave Underground Fight Club Rulebook" (no rules).
/// Combos: DirtyKick (Harm+Disarm), WheelThrow (Grab+Grab), EyePoke (Disarm+Disarm).
/// </summary>
public sealed class WastelandStreetFightingSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly StaminaSystem _stamina = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;
    [Dependency] private readonly StatusEffectsSystem _status = default!;
    [Dependency] private readonly GrabIntentServerSystem _grabServer = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    // Combo handler is called by MisfitsComboDispatcherSystem — no direct subscription here
    // to avoid duplicate directed (Component, Event) registration.

    public void OnComboTriggered(EntityUid uid, MartialArtsKnowledgeComponent comp, MisfitsComboTriggeredEvent args)
    {
        if (comp.MartialArtsForm != MisfitsMartialArtsForms.WastelandStreetFighting || comp.Blocked)
            return;

        var target = args.Target;
        var combo = args.Combo;

        switch (combo.ID)
        {
            case "WastelandDirtyKick":
                // Applies brute damage and a short stun — low-down dirty kick to the shin
                ApplyDamage(uid, target, combo.ExtraDamage, combo.DamageType);
                _stun.TryStun(target, TimeSpan.FromSeconds(combo.ParalyzeTime), true);
                _popup.PopupEntity(Loc.GetString("martial-arts-wasteland-dirty-kick", ("target", target)), uid, uid, PopupType.Small);
                break;

            case "WastelandWheelThrow":
                // Requires an active grab; throws the target in a spinning arc
                if (!TryComp<GrabIntentComponent>(uid, out var grabber) ||
                    grabber.GrabStage < GrabStage.Hard)
                {
                    _popup.PopupEntity(Loc.GetString("martial-arts-wasteland-no-grab"), uid, uid, PopupType.Small);
                    break;
                }

                var throwDir = (Transform(target).Coordinates.Position - Transform(uid).Coordinates.Position);
                if (throwDir == System.Numerics.Vector2.Zero)
                    throwDir = _random.NextVector2() with { Y = 1f };

                _grabServer.ThrowGrabbedEntity(uid, grabber, target, throwDir.Normalized() * combo.ThrownSpeed);
                ApplyDamage(uid, target, combo.ExtraDamage, combo.DamageType);
                _stun.TryKnockdown(target, TimeSpan.FromSeconds(combo.ParalyzeTime), true);
                _popup.PopupEntity(Loc.GetString("martial-arts-wasteland-wheel-throw", ("target", target)), uid, uid, PopupType.Medium);
                break;

            case "WastelandEyePoke":
                // Attempt a temporary blindness status effect
                _status.TryAddStatusEffect(target, "TemporaryBlindness",
                    TimeSpan.FromSeconds(combo.ParalyzeTime), true, "TemporaryBlindness");
                if (combo.StaminaDamage > 0)
                    _stamina.TakeStaminaDamage(target, combo.StaminaDamage, source: uid);
                _popup.PopupEntity(Loc.GetString("martial-arts-wasteland-eye-poke", ("target", target)), uid, uid, PopupType.Medium);
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
