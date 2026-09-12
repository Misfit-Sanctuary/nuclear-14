using System.Numerics;
using Content.Server._Misfits.SpecialStats.Components;
using Content.Server.Actions;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared._Misfits.SpecialStats;
using Content.Shared._Misfits.Warcry;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Interaction.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
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
    [Dependency] private readonly SharedContentEyeSystem _eye = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private readonly NpcFactionSystem _faction = default!;

    private readonly HashSet<EntityUid> _rallyTargets = new();

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
        SubscribeLocalEvent<SpecialCombatAbilitiesComponent, SpecialKeenEyeActionEvent>(OnKeenEye);
        SubscribeLocalEvent<SpecialCombatAbilitiesComponent, SpecialKeenEyeStopDoAfterEvent>(OnKeenEyeStop);
        SubscribeLocalEvent<SpecialCombatAbilitiesComponent, SpecialRallyActionEvent>(OnRally);
        SubscribeLocalEvent<SpecialCombatAbilitiesComponent, SpecialLuckyBreakActionEvent>(OnLuckyBreak);
        SubscribeLocalEvent<SpecialChargingComponent, ThrowDoHitEvent>(OnChargingDoHit);
        SubscribeLocalEvent<SpecialChargingComponent, StopThrowEvent>(OnChargingStop);
        SubscribeLocalEvent<SpecialChargingComponent, DamageModifyEvent>(OnChargingDamageModify);
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
        UpdateAbility(ent, comp, SpecialStat.Perception, ref comp.KeenEyeActionEntity, comp.KeenEyeAction);
        UpdateAbility(ent, comp, SpecialStat.Charisma, ref comp.RallyActionEntity, comp.RallyAction);
        UpdateAbility(ent, comp, SpecialStat.Luck, ref comp.LuckyBreakActionEntity, comp.LuckyBreakAction);

        // Losing the ability while scoped must not leave the user immobile.
        if (comp.KeenEyeActionEntity == null)
            EndKeenEye(ent.Owner, comp);
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

        if (!RequireMeleeWeapon(user))
            return;

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

        var charging = EnsureComp<SpecialChargingComponent>(user);
        charging.EndTime = _timing.CurTime + TimeSpan.FromSeconds(2);
        charging.StaggerTime = ent.Comp.ChargeStaggerTime;
        charging.DamageMultiplier = ent.Comp.ChargeDamageMultiplier;

        args.Handled = true;
    }

    private void OnChargingDoHit(Entity<SpecialChargingComponent> ent, ref ThrowDoHitEvent args)
    {
        // Silently does nothing against non-mobs (walls, furniture).
        _stun.TryStun(args.Target, ent.Comp.StaggerTime, refresh: true);
    }

    private void OnChargingDamageModify(Entity<SpecialChargingComponent> ent, ref DamageModifyEvent args)
    {
        args.Damage *= ent.Comp.DamageMultiplier;
    }

    private void OnChargingStop(Entity<SpecialChargingComponent> ent, ref StopThrowEvent args)
    {
        RemComp<SpecialChargingComponent>(ent.Owner);
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

        // Safety net: StopThrowEvent normally ends the charging state.
        var chargeQuery = EntityQueryEnumerator<SpecialChargingComponent>();
        while (chargeQuery.MoveNext(out var uid, out var charging))
        {
            if (_timing.CurTime >= charging.EndTime)
                RemComp<SpecialChargingComponent>(uid);
        }
    }

    private void OnParry(Entity<SpecialCombatAbilitiesComponent> ent, ref SpecialParryActionEvent args)
    {
        if (args.Handled || HasComp<SpecialParryActiveComponent>(ent.Owner))
            return;

        var uid = ent.Owner;

        if (!RequireMeleeWeapon(uid))
            return;
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

    /// <summary>
    /// Charge and parry require an actual melee weapon in the active hand.
    /// Unarmed attacks resolve the user itself as the weapon, and guns pass
    /// TryGetWeapon via their bash stats, so both are rejected explicitly.
    /// </summary>
    private bool RequireMeleeWeapon(EntityUid user)
    {
        if (_melee.TryGetWeapon(user, out var weaponUid, out _)
            && weaponUid != user
            && !HasComp<GunComponent>(weaponUid))
        {
            return true;
        }

        _popup.PopupEntity(Loc.GetString("special-ability-needs-melee"), user, user, PopupType.SmallCaution);
        return false;
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

    private void OnKeenEye(Entity<SpecialCombatAbilitiesComponent> ent, ref SpecialKeenEyeActionEvent args)
    {
        if (args.Handled)
            return;

        var uid = ent.Owner;

        if (TryComp<SpecialKeenEyeScopedComponent>(uid, out var scoped))
        {
            if (scoped.Stopping)
                return;

            var doAfterArgs = new DoAfterArgs(EntityManager, uid, ent.Comp.KeenEyeExitDelay,
                new SpecialKeenEyeStopDoAfterEvent(), uid)
            {
                BreakOnMove = false,
                BreakOnDamage = false,
                RequireCanInteract = false,
            };

            if (_doAfter.TryStartDoAfter(doAfterArgs))
                scoped.Stopping = true;

            args.Handled = true;
            return;
        }

        AddComp<SpecialKeenEyeScopedComponent>(uid);
        // BlockMovementComponent is shared with other systems; if another system
        // ever adds it to the same mob, removal on unscope could free them early.
        var blocker = EnsureComp<BlockMovementComponent>(uid);
        blocker.BlockInteraction = false;
        EnsureComp<ContentEyeComponent>(uid);
        _eye.SetZoom(uid, new Vector2(ent.Comp.KeenEyeZoom, ent.Comp.KeenEyeZoom), ignoreLimits: true);
        _actions.SetToggled(ent.Comp.KeenEyeActionEntity, true);
        args.Handled = true;
    }

    private void OnKeenEyeStop(Entity<SpecialCombatAbilitiesComponent> ent, ref SpecialKeenEyeStopDoAfterEvent args)
    {
        if (args.Cancelled)
        {
            if (TryComp<SpecialKeenEyeScopedComponent>(ent.Owner, out var scoped))
                scoped.Stopping = false;
            return;
        }

        if (args.Handled)
            return;

        EndKeenEye(ent.Owner, ent.Comp);
        args.Handled = true;
    }

    private void EndKeenEye(EntityUid uid, SpecialCombatAbilitiesComponent comp)
    {
        if (!HasComp<SpecialKeenEyeScopedComponent>(uid))
            return;

        RemComp<SpecialKeenEyeScopedComponent>(uid);
        RemComp<BlockMovementComponent>(uid);
        _eye.ResetZoom(uid);
        _actions.SetToggled(comp.KeenEyeActionEntity, false);
    }

    private void OnRally(Entity<SpecialCombatAbilitiesComponent> ent, ref SpecialRallyActionEvent args)
    {
        if (args.Handled)
            return;

        var uid = ent.Owner;
        var expiry = _timing.CurTime + ent.Comp.RallyDuration;

        _rallyTargets.Clear();
        _rallyTargets.Add(uid);
        _lookup.GetEntitiesInRange(Transform(uid).Coordinates, ent.Comp.RallyRange, _rallyTargets);

        foreach (var target in _rallyTargets)
        {
            if (target != uid && (_mobState.IsDead(target) || !_faction.IsEntityFriendly(uid, target)))
                continue;

            if (!HasComp<MovementSpeedModifierComponent>(target))
                continue;

            var buff = EnsureComp<WarcryBuffComponent>(target);
            buff.SpeedBonus = MathF.Max(buff.SpeedBonus, ent.Comp.RallySpeedBonus);
            if (expiry > buff.ExpiresAt)
                buff.ExpiresAt = expiry;

            Dirty(target, buff);
            _movementSpeed.RefreshMovementSpeedModifiers(target);
        }

        _popup.PopupCoordinates(Loc.GetString("special-rally-nearby", ("user", uid)),
            Transform(uid).Coordinates, PopupType.Medium);
        args.Handled = true;
    }

    private void OnLuckyBreak(Entity<SpecialCombatAbilitiesComponent> ent, ref SpecialLuckyBreakActionEvent args)
    {
        if (args.Handled)
            return;

        if (!_special.TryModifyTemporary(ent.Owner, SpecialStat.Luck, ent.Comp.LuckyBreakBoost,
                ent.Comp.LuckyBreakDuration, "lucky-break"))
            return;

        _popup.PopupEntity(Loc.GetString("special-lucky-break"), ent.Owner, ent.Owner);
        args.Handled = true;
    }

    private void OnAbilitiesShutdown(Entity<SpecialCombatAbilitiesComponent> ent, ref ComponentShutdown args)
    {
        EndKeenEye(ent.Owner, ent.Comp);
        _actions.RemoveAction(ent.Owner, ent.Comp.ChargeActionEntity);
        _actions.RemoveAction(ent.Owner, ent.Comp.ParryActionEntity);
        _actions.RemoveAction(ent.Owner, ent.Comp.CrippleActionEntity);
        _actions.RemoveAction(ent.Owner, ent.Comp.KeenEyeActionEntity);
        _actions.RemoveAction(ent.Owner, ent.Comp.RallyActionEntity);
        _actions.RemoveAction(ent.Owner, ent.Comp.LuckyBreakActionEntity);
    }
}
