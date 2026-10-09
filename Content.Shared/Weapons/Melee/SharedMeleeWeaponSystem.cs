using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Shared.ActionBlocker;
using Content.Shared.Administration.Logs;
using Content.Shared._NC.Mountable.Components;
using Content.Shared.Buckle.Components;
using Content.Shared.CombatMode;
using Content.Shared.Contests;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.FixedPoint;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Mech.Components;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Vehicles;
using Content.Shared.Weapons.Melee.Components;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared._Misfits.Grab; // #Misfits Add - grab intent integration
using Content.Shared._Misfits.MartialArts; // #Misfits Add - combo attack event integration
using Content.Shared._Misfits.Deathclaw; // #Cythisiax Add - allow explicit non-Damageable structure targets
using Content.Shared.Movement.Pulling.Components; // #Misfits Add - check if puller is attacking pulled target
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using ItemToggleMeleeWeaponComponent = Content.Shared.Item.ItemToggle.Components.ItemToggleMeleeWeaponComponent;

namespace Content.Shared.Weapons.Melee;

public abstract class SharedMeleeWeaponSystem : EntitySystem
{
    [Dependency] protected readonly ISharedAdminLogManager   AdminLogger     = default!;
    [Dependency] protected readonly ActionBlockerSystem      Blocker         = default!;
    [Dependency] protected readonly SharedCombatModeSystem   CombatMode      = default!;
    [Dependency] protected readonly DamageableSystem         Damageable      = default!;
    [Dependency] protected readonly SharedInteractionSystem  Interaction     = default!;
    [Dependency] protected readonly SharedMapSystem              MapManager      = default!;
    [Dependency] protected readonly SharedPopupSystem        PopupSystem     = default!;
    [Dependency] protected readonly IGameTiming              Timing          = default!;
    [Dependency] protected readonly SharedTransformSystem    TransformSystem = default!;
    [Dependency] private   readonly InventorySystem         _inventory       = default!;
    [Dependency] private   readonly EntityLookupSystem      _lookup          = default!;
    [Dependency] private   readonly MeleeSoundSystem        _meleeSound      = default!;
    [Dependency] private   readonly SharedPhysicsSystem     _physics         = default!;
    [Dependency] private   readonly IPrototypeManager       _protoManager    = default!;
    [Dependency] private   readonly StaminaSystem           _stamina         = default!;
    [Dependency] private   readonly ContestsSystem          _contests        = default!;
    // #Misfits Add - grab intent system reference for grab-type light attack interception
    [Dependency] private   readonly GrabIntentSystem          _grabIntent      = default!;

    private const int AttackMask = (int) (CollisionGroup.MobMask | CollisionGroup.Opaque);
    protected const float WideArcTolerance = 0.05f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MeleeWeaponComponent, HandSelectedEvent>(OnMeleeSelected);
        SubscribeLocalEvent<BonusMeleeDamageComponent, GetMeleeDamageEvent>(OnGetBonusMeleeDamage);
        SubscribeLocalEvent<BonusMeleeDamageComponent, GetHeavyDamageModifierEvent>(OnGetBonusHeavyDamageModifier);
        SubscribeLocalEvent<BonusMeleeAttackRateComponent, GetMeleeAttackRateEvent>(OnGetBonusMeleeAttackRate);

        SubscribeLocalEvent<ItemToggleMeleeWeaponComponent, ItemToggledEvent>(OnItemToggle);

        SubscribeAllEvent<HeavyAttackEvent>(OnHeavyAttack);
        SubscribeAllEvent<LightAttackEvent>(OnLightAttack);
        SubscribeAllEvent<DisarmAttackEvent>(OnDisarmAttack);
        SubscribeAllEvent<StopAttackEvent>(OnStopAttack);

#if DEBUG
        SubscribeLocalEvent<MeleeWeaponComponent,
                            MapInitEvent>                   (OnMapInit);
    }

    private void OnMapInit(EntityUid uid, MeleeWeaponComponent component, MapInitEvent args)
    {
        if (component.NextAttack > Timing.CurTime)
            Log.Warning($"Initializing a map that contains an entity that is on cooldown. Entity: {ToPrettyString(uid)}");
#endif
    }

    private void OnMeleeSelected(EntityUid uid, MeleeWeaponComponent component, HandSelectedEvent args)
    {
        var attackRate = GetAttackRate(uid, args.User, component);
        if (attackRate.Equals(0f))
            return;

        if (!component.ResetOnHandSelected)
            return;

        if (Paused(uid))
            return;

        // If someone swaps to this weapon then reset its cd.
        var curTime = Timing.CurTime;
        var minimum = curTime + TimeSpan.FromSeconds(attackRate);

        if (minimum < component.NextAttack)
            return;

        component.NextAttack = minimum;
        DirtyField(uid, component, nameof(MeleeWeaponComponent.NextAttack));
    }

    private void OnGetBonusMeleeDamage(EntityUid uid, BonusMeleeDamageComponent component, ref GetMeleeDamageEvent args)
    {
        if (component.BonusDamage != null)
            args.Damage += component.BonusDamage;
        if (component.DamageModifierSet != null)
            args.Modifiers.Add(component.DamageModifierSet);
    }

    private void OnGetBonusHeavyDamageModifier(EntityUid uid, BonusMeleeDamageComponent component, ref GetHeavyDamageModifierEvent args)
    {
        args.DamageModifier += component.HeavyDamageFlatModifier;
        args.Multipliers *= component.HeavyDamageMultiplier;
    }

    private void OnGetBonusMeleeAttackRate(EntityUid uid, BonusMeleeAttackRateComponent component, ref GetMeleeAttackRateEvent args)
    {
        args.Rate += component.FlatModifier;
        args.Multipliers *= component.Multiplier;
    }

    private void OnStopAttack(StopAttackEvent msg, EntitySessionEventArgs args)
    {
        var user = args.SenderSession.AttachedEntity;

        if (user == null)
            return;

        if (!TryGetWeapon(user.Value, out var weaponUid, out var weapon) ||
            weaponUid != GetEntity(msg.Weapon))
        {
            return;
        }

        if (!weapon.Attacking)
            return;

        weapon.Attacking = false;
        DirtyField(weaponUid, weapon, nameof(MeleeWeaponComponent.Attacking));
    }

    private void OnLightAttack(LightAttackEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not {} user)
            return;

        if (!TryGetWeapon(user, out var weaponUid, out var weapon) ||
            weaponUid != GetEntity(msg.Weapon))
        {
            return;
        }

        AttemptAttack(user, weaponUid, weapon, msg, args.SenderSession);
    }

    private void OnHeavyAttack(HeavyAttackEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not {} user)
            return;

        if (!TryGetWeapon(user, out var weaponUid, out var weapon) ||
            weaponUid != GetEntity(msg.Weapon) ||
            !weapon.CanWideSwing) // Goobstation Change
        {
            return;
        }

        AttemptAttack(user, weaponUid, weapon, msg, args.SenderSession);
    }

    private void OnDisarmAttack(DisarmAttackEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not {} user)
            return;

        if (TryGetWeapon(user, out var weaponUid, out var weapon))
            AttemptAttack(user, weaponUid, weapon, msg, args.SenderSession);
    }

    /// <summary>
    /// Gets the total damage a weapon does, including modifiers like wielding and enablind/disabling
    /// </summary>
    public DamageSpecifier GetDamage(EntityUid uid, EntityUid user, MeleeWeaponComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return new DamageSpecifier();

        var ev = new GetMeleeDamageEvent(uid, new(component.Damage), new(), user, component.ResistanceBypass);
        RaiseLocalEvent(uid, ref ev);
        if (user != uid)
            RaiseLocalEvent(user, ref ev);

        if (component.ContestArgs is not null)

            ev.Damage *= _contests.ContestConstructor(user, component.ContestArgs);

        return DamageSpecifier.ApplyModifierSets(ev.Damage, ev.Modifiers);
    }

    public float GetAttackRate(EntityUid uid, EntityUid user, MeleeWeaponComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return 0;

        var ev = new GetMeleeAttackRateEvent(uid, component.AttackRate, 1, user);
        RaiseLocalEvent(uid, ref ev);

        return ev.Rate * ev.Multipliers;
    }

    public FixedPoint2 GetHeavyDamageModifier(EntityUid uid, EntityUid user, MeleeWeaponComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return FixedPoint2.Zero;

        var ev = new GetHeavyDamageModifierEvent(uid, component.ClickDamageModifier, 1, user);
        RaiseLocalEvent(uid, ref ev);
        if (user != uid)
            RaiseLocalEvent(user, ref ev);

        return ev.DamageModifier * ev.Multipliers * component.HeavyDamageBaseModifier;
    }

    public bool GetResistanceBypass(EntityUid uid, EntityUid user, MeleeWeaponComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return false;

        var ev = new GetMeleeDamageEvent(uid, new(component.Damage), new(), user, component.ResistanceBypass);
        RaiseLocalEvent(uid, ref ev);
        if (user != uid)
            RaiseLocalEvent(user, ref ev);

        return ev.ResistanceBypass;
    }

    public bool TryGetWeapon(EntityUid entity, out EntityUid weaponUid, [NotNullWhen(true)] out MeleeWeaponComponent? melee)
    {
        weaponUid = default;
        melee = null;

        var ev = new GetMeleeWeaponEvent();
        RaiseLocalEvent(entity, ev);
        if (ev.Handled)
        {
            if (TryComp(ev.Weapon, out melee))
            {
                weaponUid = ev.Weapon.Value;
                return true;
            }

            return false;
        }

        // Use inhands entity if we got one.
        if (EntityManager.TryGetComponent(entity, out HandsComponent? hands) &&
            hands.ActiveHandEntity is { } held)
        {
            // Make sure the entity is a weapon AND it doesn't need
            // to be equipped to be used (E.g boxing gloves).
            if (EntityManager.TryGetComponent(held, out melee) &&
                !melee.MustBeEquippedToUse)
            {
                weaponUid = held;
                return true;
            }

            if (!HasComp<VirtualItemComponent>(held))
                return false;
        }

        // Use hands clothing if applicable.
        if (_inventory.TryGetSlotEntity(entity, "gloves", out var gloves) &&
            TryComp<MeleeWeaponComponent>(gloves, out var glovesMelee))
        {
            weaponUid = gloves.Value;
            melee = glovesMelee;
            return true;
        }

        // Use our own melee
        if (TryComp(entity, out melee))
        {
            weaponUid = entity;
            return true;
        }

        return false;
    }

    public void AttemptLightAttackMiss(EntityUid user, EntityUid weaponUid, MeleeWeaponComponent weapon, EntityCoordinates coordinates)
    {
        AttemptAttack(user, weaponUid, weapon, new LightAttackEvent(null, GetNetEntity(weaponUid), GetNetCoordinates(coordinates)), null);
    }

    public bool AttemptLightAttack(EntityUid user, EntityUid weaponUid, MeleeWeaponComponent weapon, EntityUid target)
    {
        if (!TryComp(target, out TransformComponent? targetXform))
            return false;

        return AttemptAttack(user, weaponUid, weapon, new LightAttackEvent(GetNetEntity(target), GetNetEntity(weaponUid), GetNetCoordinates(targetXform.Coordinates)), null);
    }

    public bool AttemptDisarmAttack(EntityUid user, EntityUid weaponUid, MeleeWeaponComponent weapon, EntityUid target)
    {
        if (!TryComp(target, out TransformComponent? targetXform))
            return false;

        return AttemptAttack(user, weaponUid, weapon, new DisarmAttackEvent(GetNetEntity(target), GetNetCoordinates(targetXform.Coordinates)), null);
    }

    /// <summary>
    /// Called when a windup is finished and an attack is tried.
    /// </summary>
    /// <returns>True if attack successful</returns>
    private bool AttemptAttack(EntityUid user, EntityUid weaponUid, MeleeWeaponComponent weapon, AttackEvent attack, ICommonSession? session)
    {
        var curTime = Timing.CurTime;

        if (weapon.NextAttack > curTime)
            return false;

        if (!CombatMode.IsInCombatMode(user))
            return false;

        var fireRateSwingModifier = 1f;

        EntityUid? target = null;
        switch (attack)
        {
            case LightAttackEvent light:
                if (light.Target != null && !TryGetEntity(light.Target, out target))
                {
                    // Target was lightly attacked & deleted.
                    return false;
                }

                if (!Blocker.CanAttack(user, target, (weaponUid, weapon)))
                    return false;

                // Can't self-attack if you're the weapon
                if (weaponUid == target)
                    return false;

                break;
            case HeavyAttackEvent:
                fireRateSwingModifier = weapon.HeavyRateModifier;
                break;
            case DisarmAttackEvent disarm:
                if (disarm.Target != null && !TryGetEntity(disarm.Target, out target))
                {
                    // Target was lightly attacked & deleted.
                    return false;
                }

                if (!Blocker.CanAttack(user, target, (weaponUid, weapon), true))
                    return false;
                break;
            default:
                if (!Blocker.CanAttack(user, weapon: (weaponUid, weapon)))
                    return false;
                break;
        }

        // Windup time checked elsewhere.
        var fireRate = TimeSpan.FromSeconds(GetAttackRate(weaponUid, user, weapon) * fireRateSwingModifier);
        var swings = 0;

        // TODO: If we get autoattacks then probably need a shotcounter like guns so we can do timing properly.
        if (weapon.NextAttack < curTime)
            weapon.NextAttack = curTime;

        while (weapon.NextAttack <= curTime)
        {
            weapon.NextAttack += fireRate;
            swings++;
        }

        DirtyField(weaponUid, weapon, nameof(MeleeWeaponComponent.NextAttack));

        // Do this AFTER attack so it doesn't spam every tick
        var ev = new AttemptMeleeEvent
        {
            PlayerUid = user
        };
        RaiseLocalEvent(weaponUid, ref ev);

        if (ev.Cancelled)
        {
            if (ev.Message != null)
            {
                PopupSystem.PopupClient(ev.Message, weaponUid, user);
            }

            return false;
        }

        // Attack confirmed
        for (var i = 0; i < swings; i++)
        {
            string animation;

            switch (attack)
            {
                case LightAttackEvent light:
                    // #Misfits Add - Intercept light attack on pulled target to escalate grab stage
                    if (target != null
                        && TryComp<PullerComponent>(user, out var pullerComp)
                        && pullerComp.Pulling == target.Value)
                    {
                        var grabAttempt = new GrabAttemptEvent(user, target.Value);
                        RaiseLocalEvent(target.Value, ref grabAttempt);
                        if (grabAttempt.Cancelled)
                        {
                            // Grab was handled — skip normal light attack
                            animation = weapon.Animation;
                            break;
                        }
                    }
                    DoLightAttack(user, light, weaponUid, weapon, session);
                    // #Misfits Add - Raise combo event for the martial arts engine after a light attack
                    if (target != null && HasComp<CanPerformComboComponent>(user))
                        RaiseLocalEvent(user, new MisfitsComboAttackPerformedEvent(user, target.Value, weaponUid, MisfitsComboAttackType.Harm));
                    animation = weapon.Animation;
                    break;
                case DisarmAttackEvent disarm:
                    if (!DoDisarm(user, disarm, weaponUid, weapon, session))
                        return false;
                    // #Misfits Add - Raise combo event for the martial arts engine after a disarm
                    if (target != null && HasComp<CanPerformComboComponent>(user))
                        RaiseLocalEvent(user, new MisfitsComboAttackPerformedEvent(user, target.Value, weaponUid, MisfitsComboAttackType.Disarm));
                    animation = weapon.Animation;
                    break;
                case HeavyAttackEvent heavy:
                    if (!DoHeavyAttack(user, heavy, weaponUid, weapon, session))
                        return false;

                    animation = weapon.WideAnimation;
                    break;
                default:
                    throw new NotImplementedException();
            }

            DoLungeAnimation(user, weaponUid, weapon.Angle, TransformSystem.ToMapCoordinates(GetCoordinates(attack.Coordinates)), weapon.Range, animation);
        }

        var attackEv = new MeleeAttackEvent(weaponUid);
        RaiseLocalEvent(user, ref attackEv);

        weapon.Attacking = true;
        return true;
    }

    protected abstract bool InRange(EntityUid user, EntityUid target, float range, ICommonSession? session, GameTick? lastRealTick = null);

    protected bool CanDoLightAttack(EntityUid user, [NotNullWhen(true)] EntityUid? target, MeleeWeaponComponent component, [NotNullWhen(true)] out TransformComponent? targetXform, ICommonSession? session = null, GameTick? lastRealTick = null)
    {
        targetXform = null;
        if (Deleted(target) ||
            !TryComp<TransformComponent>(target, out targetXform) ||
            // Not in LOS.
            !InRange(user, target.Value, component.Range, session, lastRealTick))
        {
            return false;
        }

        if (HasComp<DamageableComponent>(target))
            return true;

        // #Cythisiax Add - let narrowly scoped innate weapons opt into targets that
        // intentionally lack Damageable (Bwonsamdi's indestructible structures).
        var attempt = new MeleeNonDamageableTargetAttemptEvent(target.Value);
        RaiseLocalEvent(user, ref attempt);
        return attempt.Allowed;
    }

    protected virtual void DoLightAttack(EntityUid user, LightAttackEvent ev, EntityUid meleeUid, MeleeWeaponComponent component, ICommonSession? session)
    {
        // If I do not come back later to fix Light Attacks being Heavy Attacks you can throw me in the spider pit -Errant
        var damage = GetDamage(meleeUid, user, component) * GetHeavyDamageModifier(meleeUid, user, component);
        var target = GetEntity(ev.Target);
        var resistanceBypass = GetResistanceBypass(meleeUid, user, component);

        // For consistency with wide attacks stuff needs damageable.
        if (!CanDoLightAttack(user, target, component, out var targetXform, session, ev.LastRealTick))
        {
            // Leave IsHit set to true, because the only time it's set to false
            // is when a melee weapon is examined. Misses are inferred from an
            // empty HitEntities.
            // TODO: This needs fixing
            if (meleeUid == user)
            {
                AdminLogger.Add(LogType.MeleeHit,
                    LogImpact.Low,
                    $"{ToPrettyString(user):actor} melee attacked (light) using their hands and missed");
            }
            else
            {
                AdminLogger.Add(LogType.MeleeHit,
                    LogImpact.Low,
                    $"{ToPrettyString(user):actor} melee attacked (light) using {ToPrettyString(meleeUid):tool} and missed");
            }
            var missEvent = new MeleeHitEvent(new List<EntityUid>(), user, meleeUid, damage, null);
            RaiseLocalEvent(meleeUid, missEvent);
            _meleeSound.PlaySwingSound(user, meleeUid, component);
            return;
        }

        // Sawmill.Debug($"Melee damage is {damage.Total} out of {component.Damage.Total}");

        // Raise event before doing damage so we can cancel damage if the event is handled
        var hitEvent = new MeleeHitEvent(new List<EntityUid> { target.Value }, user, meleeUid, damage, null);
        RaiseLocalEvent(meleeUid, hitEvent);

        if (hitEvent.Handled)
            return;

        var targets = new List<EntityUid>(1)
        {
            target.Value
        };

        var weapon = GetEntity(ev.Weapon);

        // We skip weapon -> target interaction, as forensics system applies DNA on hit
        Interaction.DoContactInteraction(user, weapon);

        // If the user is using a long-range weapon, this probably shouldn't be happening? But I'll interpret melee as a
        // somewhat messy scuffle. See also, heavy attacks.
        Interaction.DoContactInteraction(user, target);

        // For stuff that cares about it being attacked.
        var attackedEvent = new AttackedEvent(meleeUid, user, targetXform.Coordinates);
        RaiseLocalEvent(target.Value, attackedEvent);

        var modifiedDamage = DamageSpecifier.ApplyModifierSets(damage + hitEvent.BonusDamage + attackedEvent.BonusDamage, hitEvent.ModifiersList);
        var damageResult = Damageable.TryChangeDamage(target, modifiedDamage, origin:user, ignoreResistances: resistanceBypass, partMultiplier: component.ClickPartDamageMultiplier);

        if (damageResult is {Empty: false})
        {
            // If the target has stamina and is taking blunt damage, they should also take stamina damage based on their blunt to stamina factor
            if (damageResult.DamageDict.TryGetValue("Blunt", out var bluntDamage))
            {
                _stamina.TakeStaminaDamage(target.Value, (bluntDamage * component.BluntStaminaDamageFactor).Float(), visual: false, source: user, with: meleeUid == user ? null : meleeUid);
            }

            if (meleeUid == user)
            {
                AdminLogger.Add(LogType.MeleeHit,
                    LogImpact.Medium,
                    $"{ToPrettyString(user):actor} melee attacked (light) {ToPrettyString(target.Value):subject} using their hands and dealt {damageResult.GetTotal():damage} damage");
            }
            else
            {
                AdminLogger.Add(LogType.MeleeHit,
                    LogImpact.Medium,
                    $"{ToPrettyString(user):actor} melee attacked (light) {ToPrettyString(target.Value):subject} using {ToPrettyString(meleeUid):tool} and dealt {damageResult.GetTotal():damage} damage");
            }

        }

        _meleeSound.PlayHitSound(target.Value, user, GetHighestDamageSound(modifiedDamage, _protoManager), hitEvent.HitSoundOverride, component.SoundHit, component.SoundNoDamage);

        if (damageResult?.GetTotal() > FixedPoint2.Zero)
        {
            DoDamageEffect(targets, user, targetXform);
        }
    }

    protected abstract void DoDamageEffect(List<EntityUid> targets, EntityUid? user,  TransformComponent targetXform);

    private bool DoHeavyAttack(EntityUid user, HeavyAttackEvent ev, EntityUid meleeUid, MeleeWeaponComponent component, ICommonSession? session)
    {
        // TODO: This is copy-paste as fuck with DoPreciseAttack
        if (!TryComp(user, out TransformComponent? userXform))
            return false;

        // Client-provided coordinates can refer to an entity that was deleted while
        // the message was in flight. Reject them before converting to map coordinates.
        if (!TryGetEntity(ev.Coordinates.NetEntity, out var coordinateEntity) ||
            !HasComp<TransformComponent>(coordinateEntity.Value))
            return false;

        var targetMap = TransformSystem.ToMapCoordinates(GetCoordinates(ev.Coordinates));

        if (targetMap.MapId != userXform.MapID)
            return false;

        if (TryComp<StaminaComponent>(user, out var stamina))
        {
            if (stamina.CritThreshold - stamina.StaminaDamage <= component.HeavyStaminaCost)
            {
                PopupSystem.PopupClient(Loc.GetString("melee-heavy-no-stamina"), meleeUid, user);
                return false;
            }

            _stamina.TakeStaminaDamage(user, component.HeavyStaminaCost, stamina, visual: false);
        }

        var userPos = TransformSystem.GetWorldPosition(userXform);
        var direction = targetMap.Position - userPos;
        // [Changed by MisfitsCrew/Operator] Wide swings use the cursor for direction, not reach;
        // otherwise the swing only hits when the cursor is placed over the target.
        var distance = component.Range * component.HeavyRangeModifier;

        var damage = GetDamage(meleeUid, user, component);
        var entities = GetEntityList(ev.Entities);

        // Naughty input
        if (entities.Count > component.MaxTargets)
        {
            entities.RemoveRange(component.MaxTargets, entities.Count - component.MaxTargets);
        }

        // Validate client
        for (var i = entities.Count - 1; i >= 0; i--)
        {
            if (ArcRaySuccessful(entities[i],
                    userPos,
                    direction.ToWorldAngle(),
                    component.Angle,
                    distance,
                    userXform.MapID,
                    user,
                    session,
                    ev.LastRealTick))
            {
                continue;
            }

            // Bad input
            entities.RemoveAt(i);
        }

        // [Changed by MisfitsCrew/Operator] Recover server-side candidates only after bogus
        // client entries have been removed, so stale or malicious max-target lists cannot block prediction correction.
        AddHeavyAttackCandidates(entities,
            user,
            meleeUid,
            component,
            userPos,
            direction.ToWorldAngle(),
            distance,
            userXform.MapID,
            session,
            ev.LastRealTick);

        if (entities.Count == 0)
        {
            if (meleeUid == user)
            {
                AdminLogger.Add(LogType.MeleeHit,
                    LogImpact.Low,
                    $"{ToPrettyString(user):actor} melee attacked (heavy) using their hands and missed");
            }
            else
            {
                AdminLogger.Add(LogType.MeleeHit,
                    LogImpact.Low,
                    $"{ToPrettyString(user):actor} melee attacked (heavy) using {ToPrettyString(meleeUid):tool} and missed");
            }
            var missEvent = new MeleeHitEvent(new List<EntityUid>(), user, meleeUid, damage, direction);
            RaiseLocalEvent(meleeUid, missEvent);

            // immediate audio feedback
            _meleeSound.PlaySwingSound(user, meleeUid, component);

            return true;
        }

        var targets = new List<EntityUid>();
        var damageQuery = GetEntityQuery<DamageableComponent>();

        foreach (var entity in entities)
        {
            if (IsUserRelatedAttackEntity(user, entity) ||
                !damageQuery.HasComponent(entity))
                continue;

            targets.Add(entity);
        }

        // Sawmill.Debug($"Melee damage is {damage.Total} out of {component.Damage.Total}");

        // Raise event before doing damage so we can cancel damage if the event is handled
        var hitEvent = new MeleeHitEvent(targets, user, meleeUid, damage, direction);
        RaiseLocalEvent(meleeUid, hitEvent);

        if (hitEvent.Handled)
            return true;

        var weapon = GetEntity(ev.Weapon);

        Interaction.DoContactInteraction(user, weapon);

        // For stuff that cares about it being attacked.
        foreach (var target in targets)
        {
            // We skip weapon -> target interaction, as forensics system applies DNA on hit

            // If the user is using a long-range weapon, this probably shouldn't be happening? But I'll interpret melee as a
            // somewhat messy scuffle. See also, light attacks.
            Interaction.DoContactInteraction(user, target);
        }

        var appliedDamage = new DamageSpecifier();

        for (var i = targets.Count - 1; i >= 0; i--)
        {
            var entity = targets[i];
            // We raise an attack attempt here as well,
            // primarily because this was an untargeted wideswing: if a subscriber to that event cared about
            // the potential target (such as for pacifism), they need to be made aware of the target here.
            // In that case, just continue.
            if (!Blocker.CanAttack(user, entity, (weapon, component)))
            {
                targets.RemoveAt(i);
                continue;
            }

            var attackedEvent = new AttackedEvent(meleeUid, user, GetCoordinates(ev.Coordinates));
            RaiseLocalEvent(entity, attackedEvent);
            var modifiedDamage = DamageSpecifier.ApplyModifierSets(damage + hitEvent.BonusDamage + attackedEvent.BonusDamage, hitEvent.ModifiersList);

            var damageResult = Damageable.TryChangeDamage(entity, modifiedDamage, origin: user, partMultiplier: component.HeavyPartDamageMultiplier);

            if (damageResult != null && damageResult.GetTotal() > FixedPoint2.Zero)
            {
                // If the target has stamina and is taking blunt damage, they should also take stamina damage based on their blunt to stamina factor
                if (damageResult.DamageDict.TryGetValue("Blunt", out var bluntDamage))
                {
                    _stamina.TakeStaminaDamage(entity, (bluntDamage * component.BluntStaminaDamageFactor).Float(), visual: false, source: user, with: meleeUid == user ? null : meleeUid);
                }

                appliedDamage += damageResult;

                if (meleeUid == user)
                {
                    AdminLogger.Add(LogType.MeleeHit,
                        LogImpact.Medium,
                        $"{ToPrettyString(user):actor} melee attacked (heavy) {ToPrettyString(entity):subject} using their hands and dealt {damageResult.GetTotal():damage} damage");
                }
                else
                {
                    AdminLogger.Add(LogType.MeleeHit,
                        LogImpact.Medium,
                        $"{ToPrettyString(user):actor} melee attacked (heavy) {ToPrettyString(entity):subject} using {ToPrettyString(meleeUid):tool} and dealt {damageResult.GetTotal():damage} damage");
                }
            }
        }

        // [Changed by MisfitsCrew/Operator] Use filtered damageable targets for feedback so a
        // rider's own bike cannot produce hit sounds after being excluded from damage.
        if (targets.Count != 0)
        {
            var target = targets.First();
            _meleeSound.PlayHitSound(target, user, GetHighestDamageSound(appliedDamage, _protoManager), hitEvent.HitSoundOverride, component.SoundHit, component.SoundNoDamage);
        }

        if (appliedDamage.GetTotal() > FixedPoint2.Zero)
        {
            DoDamageEffect(targets, user, Transform(targets[0]));
        }

        return true;
    }

    protected HashSet<EntityUid> ArcRayCast(Vector2 position, Angle angle, Angle arcWidth, float range, MapId mapId, EntityUid ignore)
    {
        // TODO: This is pretty sucky.
        var widthRad = arcWidth;
        var increments = 1 + 35 * (int) Math.Ceiling(widthRad / (2 * Math.PI));
        var increment = widthRad / increments;
        var baseAngle = angle - widthRad / 2;
        var extraIgnored = GetUserExtraIgnoredEntity(ignore);

        var resSet = new HashSet<EntityUid>();

        for (var i = 0; i < increments; i++)
        {
            var castAngle = new Angle(baseAngle + increment * i);
            // [Changed by MisfitsCrew/Operator] Ignore both the attacker and their mounted vehicle
            // so rider wide swings do not select the bike as the first arc hit.
            var res = _physics.IntersectRayWithPredicate(mapId,
                new CollisionRay(position,
                    castAngle.ToWorldVec(),
                    AttackMask),
                (User: ignore, Extra: extraIgnored),
                static (hit, ignored) => hit == ignored.User || hit == ignored.Extra,
                range,
                false)
                .ToList();

            if (res.Count != 0)
            {
                resSet.Add(res[0].HitEntity);
            }
        }

        AddArcBoundsCandidates(resSet, position, angle, arcWidth, range, mapId, ignore, extraIgnored);

        return resSet;
    }

    private void AddArcBoundsCandidates(
        HashSet<EntityUid> result,
        Vector2 position,
        Angle angle,
        Angle arcWidth,
        float range,
        MapId mapId,
        EntityUid ignore,
        EntityUid? extraIgnored)
    {
        // [Changed by MisfitsCrew/Operator] Add physical edge-overlap candidates during
        // client prediction too, so edge wide swings do not damage without local feedback.
        var bounds = Box2.CenteredAround(position, new Vector2(range * 2f, range * 2f));
        var candidates = _lookup.GetEntitiesIntersecting(mapId, bounds, LookupFlags.Dynamic);

        foreach (var candidate in candidates)
        {
            if (result.Contains(candidate) ||
                candidate == ignore ||
                candidate == extraIgnored ||
                !HasComp<DamageableComponent>(candidate) ||
                !TryComp(candidate, out TransformComponent? xform) ||
                !TryComp(candidate, out FixturesComponent? fixtures))
            {
                continue;
            }

            if (TargetBoundsOverlapArc(xform.Coordinates, xform.LocalRotation, fixtures, position, angle, arcWidth, range))
                result.Add(candidate);
        }
    }

    private bool TargetBoundsOverlapArc(
        EntityCoordinates targetCoords,
        Angle targetLocalAngle,
        FixturesComponent fixtures,
        Vector2 origin,
        Angle angle,
        Angle arcWidth,
        float range)
    {
        var targetMap = TransformSystem.ToMapCoordinates(targetCoords);
        if (targetMap.MapId == MapId.Nullspace)
            return false;

        var worldAngle = TransformSystem.GetWorldRotation(targetCoords.EntityId) + targetLocalAngle;
        var transform = new Transform(targetMap.Position, worldAngle);
        var initialized = false;
        var bounds = new Box2(targetMap.Position, targetMap.Position);

        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (!fixture.Hard ||
                (fixture.CollisionLayer & AttackMask) == 0)
            {
                continue;
            }

            for (var i = 0; i < fixture.Shape.ChildCount; i++)
            {
                var aabb = fixture.Shape.ComputeAABB(transform, i);
                bounds = initialized ? bounds.Union(aabb) : aabb;
                initialized = true;
            }
        }

        if (!initialized)
            return false;

        var halfArc = arcWidth / 2.0 + WideArcTolerance;
        foreach (var point in GetBoxTestPoints(bounds, origin))
        {
            if (PointInArc(point, origin, angle, halfArc, range))
                return true;
        }

        var centerEnd = origin + angle.ToWorldVec() * range;
        var leftEnd = origin + new Angle(angle - arcWidth / 2.0).ToWorldVec() * range;
        var rightEnd = origin + new Angle(angle + arcWidth / 2.0).ToWorldVec() * range;
        return SegmentIntersectsBox(origin, centerEnd, bounds) ||
               SegmentIntersectsBox(origin, leftEnd, bounds) ||
               SegmentIntersectsBox(origin, rightEnd, bounds);
    }

    protected static IEnumerable<Vector2> GetBoxTestPoints(Box2 bounds, Vector2 origin)
    {
        yield return bounds.BottomLeft;
        yield return bounds.BottomRight;
        yield return bounds.TopLeft;
        yield return bounds.TopRight;
        yield return new Vector2(
            Math.Clamp(origin.X, bounds.Left, bounds.Right),
            Math.Clamp(origin.Y, bounds.Bottom, bounds.Top));
    }

    protected static bool PointInArc(Vector2 point, Vector2 origin, Angle angle, Angle halfArc, float range)
    {
        var delta = point - origin;
        if (delta.LengthSquared() > range * range || delta.LengthSquared() <= 0.001f)
            return false;

        return Math.Abs((double) Angle.ShortestDistance(angle, delta.ToWorldAngle())) <= (double) halfArc;
    }

    protected static bool SegmentIntersectsBox(Vector2 start, Vector2 end, Box2 box)
    {
        if (box.Contains(start))
            return true;

        var direction = end - start;
        var min = 0f;
        var max = 1f;

        if (!ClipAxis(start.X, direction.X, box.Left, box.Right, ref min, ref max) ||
            !ClipAxis(start.Y, direction.Y, box.Bottom, box.Top, ref min, ref max))
        {
            return false;
        }

        return max >= min;
    }

    private static bool ClipAxis(float start, float direction, float minBound, float maxBound, ref float min, ref float max)
    {
        if (Math.Abs(direction) < 0.0001f)
            return start >= minBound && start <= maxBound;

        var inv = 1f / direction;
        var enter = (minBound - start) * inv;
        var exit = (maxBound - start) * inv;

        if (enter > exit)
            (enter, exit) = (exit, enter);

        min = Math.Max(min, enter);
        max = Math.Min(max, exit);
        return max >= min;
    }

    protected virtual void AddHeavyAttackCandidates(
        List<EntityUid> entities,
        EntityUid user,
        EntityUid meleeUid,
        MeleeWeaponComponent component,
        Vector2 position,
        Angle angle,
        float range,
        MapId mapId,
        ICommonSession? session,
        GameTick? lastRealTick)
    {
    }

    protected bool IsUserRelatedAttackEntity(EntityUid user, EntityUid entity)
    {
        if (entity == user)
            return true;

        return GetUserExtraIgnoredEntity(user) == entity;
    }

    private EntityUid? GetUserExtraIgnoredEntity(EntityUid user)
    {
        if (TryComp<BuckleComponent>(user, out var buckle) &&
            buckle.BuckledTo is { } buckledTo &&
            (HasComp<VehicleComponent>(buckledTo) || HasComp<MountableComponent>(buckledTo)))
        {
            return buckledTo;
        }

        if (TryComp<MechPilotComponent>(user, out var mechPilot))
            return mechPilot.Mech;

        return null;
    }

    protected virtual bool ArcRaySuccessful(EntityUid targetUid,
        Vector2 position,
        Angle angle,
        Angle arcWidth,
        float range,
        MapId mapId,
        EntityUid ignore,
        ICommonSession? session,
        GameTick? lastRealTick = null)
    {
        // Only matters for server.
        return true;
    }


    public static string? GetHighestDamageSound(DamageSpecifier modifiedDamage, IPrototypeManager protoManager)
    {
        var groups = modifiedDamage.GetDamagePerGroup(protoManager);

        // Use group if it's exclusive, otherwise fall back to type.
        if (groups.Count == 1)
        {
            return groups.Keys.First();
        }

        var highestDamage = FixedPoint2.Zero;
        string? highestDamageType = null;

        foreach (var (type, damage) in modifiedDamage.DamageDict)
        {
            if (damage <= highestDamage)
                continue;

            highestDamageType = type;
        }

        return highestDamageType;
    }

    protected virtual bool DoDisarm(EntityUid user, DisarmAttackEvent ev, EntityUid meleeUid, MeleeWeaponComponent component, ICommonSession? session)
    {
        var target = GetEntity(ev.Target);

        if (Deleted(target) ||
            user == target)
        {
            return false;
        }

        // Play a sound to give instant feedback; same with playing the animations
        _meleeSound.PlaySwingSound(user, meleeUid, component);
        return true;
    }

    private void DoLungeAnimation(EntityUid user, EntityUid weapon, Angle angle, MapCoordinates coordinates, float length, string? animation)
    {
        // TODO: Assert that offset eyes are still okay.
        if (!TryComp(user, out TransformComponent? userXform))
            return;

        var invMatrix = TransformSystem.GetInvWorldMatrix(userXform);
        var localPos = Vector2.Transform(coordinates.Position, invMatrix);

        if (localPos.LengthSquared() <= 0f)
            return;

        localPos = userXform.LocalRotation.RotateVec(localPos);

        // We'll play the effect just short visually so it doesn't look like we should be hitting but actually aren't.
        const float bufferLength = 0.2f;
        var visualLength = length - bufferLength;

        if (localPos.Length() > visualLength)
            localPos = localPos.Normalized() * visualLength;

        DoLunge(user, weapon, angle, localPos, animation);
    }

    public abstract void DoLunge(EntityUid user, EntityUid weapon, Angle angle, Vector2 localPos, string? animation, bool predicted = true);

    /// <summary>
    /// Used to update the MeleeWeapon component on item toggle.
    /// </summary>
    private void OnItemToggle(EntityUid uid, ItemToggleMeleeWeaponComponent itemToggleMelee, ItemToggledEvent args)
    {
        if (!TryComp(uid, out MeleeWeaponComponent? meleeWeapon))
            return;

        if (args.Activated)
        {
            if (itemToggleMelee.ActivatedDamage != null)
            {
                //Setting deactivated damage to the weapon's regular value before changing it.
                itemToggleMelee.DeactivatedDamage ??= meleeWeapon.Damage;
                meleeWeapon.Damage = itemToggleMelee.ActivatedDamage;
                DirtyField(uid, meleeWeapon, nameof(MeleeWeaponComponent.Damage));
            }

            meleeWeapon.SoundHit = itemToggleMelee.ActivatedSoundOnHit;

            if (itemToggleMelee.ActivatedSoundOnHitNoDamage != null)
            {
                //Setting the deactivated sound on no damage hit to the weapon's regular value before changing it.
                itemToggleMelee.DeactivatedSoundOnHitNoDamage ??= meleeWeapon.SoundNoDamage;
                meleeWeapon.SoundNoDamage = itemToggleMelee.ActivatedSoundOnHitNoDamage;
                DirtyField(uid, meleeWeapon, nameof(MeleeWeaponComponent.SoundNoDamage));
            }

            if (itemToggleMelee.ActivatedSoundOnSwing != null)
            {
                //Setting the deactivated sound on no damage hit to the weapon's regular value before changing it.
                itemToggleMelee.DeactivatedSoundOnSwing ??= meleeWeapon.SoundSwing;
                meleeWeapon.SoundSwing = itemToggleMelee.ActivatedSoundOnSwing;
                DirtyField(uid, meleeWeapon, nameof(MeleeWeaponComponent.SoundSwing));
            }

            if (itemToggleMelee.DeactivatedSecret)
            {
                meleeWeapon.Hidden = false;
            }
        }
        else
        {
            if (itemToggleMelee.DeactivatedDamage != null)
            {
                meleeWeapon.Damage = itemToggleMelee.DeactivatedDamage;
                DirtyField(uid, meleeWeapon, nameof(MeleeWeaponComponent.Damage));
            }

            meleeWeapon.SoundHit = itemToggleMelee.DeactivatedSoundOnHit;
            DirtyField(uid, meleeWeapon, nameof(MeleeWeaponComponent.SoundHit));

            if (itemToggleMelee.DeactivatedSoundOnHitNoDamage != null)
            {
                meleeWeapon.SoundNoDamage = itemToggleMelee.DeactivatedSoundOnHitNoDamage;
                DirtyField(uid, meleeWeapon, nameof(MeleeWeaponComponent.SoundNoDamage));
            }

            if (itemToggleMelee.DeactivatedSoundOnSwing != null)
            {
                meleeWeapon.SoundSwing = itemToggleMelee.DeactivatedSoundOnSwing;
                DirtyField(uid, meleeWeapon, nameof(MeleeWeaponComponent.SoundSwing));
            }

            if (itemToggleMelee.DeactivatedSecret)
            {
                meleeWeapon.Hidden = true;
            }
        }
    }
}
