using System.Numerics;
using Content.Server._Misfits.SpecialStats.Components;
using Content.Server.Actions;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared._Misfits.SpecialStats;
using Content.Shared.Damage;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Reflect;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.SpecialStats;

/// <summary>
/// Grants active combat abilities (charge, parry, cripple) to characters with
/// high physical SPECIAL stats. See the med HUD system for the grant pattern.
/// </summary>
public sealed class SpecialCombatAbilitySystem : EntitySystem
{
    [Dependency] private readonly ActionsSystem _actions = default!;
    [Dependency] private readonly SharedSpecialSystem _special = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly SharedMeleeWeaponSystem _melee = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SpecialChangedEvent>(OnSpecialChanged);
        SubscribeLocalEvent<SpecialStatsReadyEvent>(OnStatsReady);
        SubscribeLocalEvent<SpecialShutdownEvent>(OnSpecialShutdown);
        SubscribeLocalEvent<SpecialCombatAbilitiesComponent, ComponentShutdown>(OnAbilitiesShutdown);
        SubscribeLocalEvent<SpecialCombatAbilitiesComponent, SpecialChargeActionEvent>(OnCharge);
        SubscribeLocalEvent<SpecialCombatAbilitiesComponent, SpecialParryActionEvent>(OnParry);
        SubscribeLocalEvent<SpecialParryActiveComponent, AttackedEvent>(OnParryAttacked);
        SubscribeLocalEvent<SpecialParryActiveComponent, DamageModifyEvent>(OnParryDamageModify);
        SubscribeLocalEvent<SpecialCombatAbilitiesComponent, SpecialCrippleActionEvent>(OnCripple);
    }

    private void OnSpecialChanged(ref SpecialChangedEvent args)
    {
        if (TryComp<SpecialComponent>(args.ChangedEntity, out var special))
            ApplyAbilities((args.ChangedEntity, special));
    }

    private void OnStatsReady(ref SpecialStatsReadyEvent args)
    {
        if (TryComp<SpecialComponent>(args.Entity, out var special))
            ApplyAbilities((args.Entity, special));
    }

    private void OnSpecialShutdown(ref SpecialShutdownEvent args)
    {
        RemComp<SpecialCombatAbilitiesComponent>(args.Entity);
    }

    private void ApplyAbilities(Entity<SpecialComponent> ent)
    {
        var comp = EnsureComp<SpecialCombatAbilitiesComponent>(ent.Owner);

        UpdateAbility(ent, comp, SpecialStat.Agility, ref comp.ChargeActionEntity, comp.ChargeAction);
        UpdateAbility(ent, comp, SpecialStat.Endurance, ref comp.ParryActionEntity, comp.ParryAction);
        UpdateAbility(ent, comp, SpecialStat.Strength, ref comp.CrippleActionEntity, comp.CrippleAction);
    }

    private void UpdateAbility(
        Entity<SpecialComponent> ent,
        SpecialCombatAbilitiesComponent comp,
        SpecialStat stat,
        ref EntityUid? actionEntity,
        string actionId)
    {
        if (_special.GetEffective(ent.Owner, stat, ent.Comp) >= comp.Threshold)
        {
            _actions.AddAction(ent.Owner, ref actionEntity, actionId);
        }
        else
        {
            _actions.RemoveAction(ent.Owner, actionEntity);
            actionEntity = null;
        }
    }

    private void OnCharge(Entity<SpecialCombatAbilitiesComponent> ent, ref SpecialChargeActionEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;
        var userPos = _transform.GetMapCoordinates(user);
        var targetPos = _transform.ToMapCoordinates(args.Target);

        if (targetPos.MapId != userPos.MapId)
            return;

        var direction = targetPos.Position - userPos.Position;
        if (direction == Vector2.Zero)
            return;

        if (direction.Length() > ent.Comp.ChargeRange)
            direction = Vector2.Normalize(direction) * ent.Comp.ChargeRange;

        // A throw, not a teleport: walls and obstacles stop the lunge naturally.
        _throwing.TryThrow(user, direction, ent.Comp.ChargeSpeed, user,
            pushbackRatio: 0f, compensateFriction: true, recoil: false, doSpin: false);
        args.Handled = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SpecialParryActiveComponent>();
        while (query.MoveNext(out var uid, out var active))
        {
            if (_timing.CurTime >= active.EndTime)
                EndParry(uid, active);
        }
    }

    private void OnParry(Entity<SpecialCombatAbilitiesComponent> ent, ref SpecialParryActionEvent args)
    {
        if (args.Handled || HasComp<SpecialParryActiveComponent>(ent.Owner))
            return;

        var uid = ent.Owner;
        var active = AddComp<SpecialParryActiveComponent>(uid);
        active.EndTime = _timing.CurTime + ent.Comp.ParryWindow;
        active.StunTime = ent.Comp.ParryStunTime;

        if (TryComp<ReflectComponent>(uid, out var reflect))
        {
            active.HadReflect = true;
            active.PrevReflects = reflect.Reflects;
            active.PrevProb = reflect.ReflectProb;
            active.PrevProbByType = reflect.ReflectProbByType;
        }
        else
        {
            reflect = AddComp<ReflectComponent>(uid);
        }

        reflect.Reflects = ReflectType.Energy | ReflectType.NonEnergy | ReflectType.SmallCaliber | ReflectType.MediumCaliber;
        reflect.ReflectProb = 1f;
        reflect.ReflectProbByType = new();
        Dirty(uid, reflect);

        _popup.PopupEntity(Loc.GetString("special-parry-window", ("user", uid)), uid, PopupType.MediumCaution);
        args.Handled = true;
    }

    private void OnParryAttacked(Entity<SpecialParryActiveComponent> ent, ref AttackedEvent args)
    {
        if (args.User == ent.Owner)
            return;

        ent.Comp.PendingNegateAttacker = args.User;
        _stun.TryStun(args.User, ent.Comp.StunTime, refresh: true);
    }

    private void OnParryDamageModify(Entity<SpecialParryActiveComponent> ent, ref DamageModifyEvent args)
    {
        if (args.Origin == null || args.Origin != ent.Comp.PendingNegateAttacker)
            return;

        args.Damage = new DamageSpecifier();
        ent.Comp.PendingNegateAttacker = null;
    }

    private void EndParry(EntityUid uid, SpecialParryActiveComponent active)
    {
        if (TryComp<ReflectComponent>(uid, out var reflect))
        {
            if (active.HadReflect)
            {
                reflect.Reflects = active.PrevReflects;
                reflect.ReflectProb = active.PrevProb;
                reflect.ReflectProbByType = active.PrevProbByType ?? new();
                Dirty(uid, reflect);
            }
            else
            {
                RemComp<ReflectComponent>(uid);
            }
        }

        RemComp<SpecialParryActiveComponent>(uid);
    }

    private void OnCripple(Entity<SpecialCombatAbilitiesComponent> ent, ref SpecialCrippleActionEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;
        var target = args.Target;

        if (target == user)
            return;

        // The action's range/whitelist already validated the target; the melee
        // attempt re-checks range, weapon cooldown, and combat mode. If it
        // fails, don't set Handled so the action cooldown isn't consumed.
        if (!_melee.TryGetWeapon(user, out var weaponUid, out var weapon))
            return;

        if (!_melee.AttemptLightAttack(user, weaponUid, weapon, target))
            return;

        _stun.TrySlowdown(target, ent.Comp.CrippleDuration, refresh: true,
            ent.Comp.CrippleSpeedMultiplier, ent.Comp.CrippleSpeedMultiplier);
        args.Handled = true;
    }

    private void OnAbilitiesShutdown(Entity<SpecialCombatAbilitiesComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ChargeActionEntity);
        _actions.RemoveAction(ent.Owner, ent.Comp.ParryActionEntity);
        _actions.RemoveAction(ent.Owner, ent.Comp.CrippleActionEntity);
    }
}
