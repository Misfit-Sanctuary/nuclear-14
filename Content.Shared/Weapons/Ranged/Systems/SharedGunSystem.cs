using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Administration.Logs;
using Content.Shared.Audio;
using Content.Shared.Contests;
using Content.Shared.CombatMode;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Examine;
using Content.Shared.Gravity;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Item;
using Content.Shared.Mech.Components; // Goobstation
using Content.Shared._Misfits.CCVar;
using Content.Shared._Misfits.Random;
using Content.Shared._Misfits.Weapons;
using Content.Shared._Misfits.Weapons.Ranged.Prediction;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Tag;
using Content.Shared.Throwing;
using Content.Shared.Timing;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Whitelist;
using Robust.Shared.Configuration;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared.Weapons.Ranged.Systems;

public abstract partial class SharedGunSystem : EntitySystem
{
    [Dependency] private   readonly ActionBlockerSystem _actionBlockerSystem = default!;
    [Dependency] protected readonly IGameTiming Timing = default!;
    [Dependency] protected readonly IMapManager MapManager = default!;
    [Dependency] private   readonly INetManager _netManager = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] protected readonly IPrototypeManager ProtoManager = default!;
    [Dependency] protected readonly IRobustRandom Random = default!;
    [Dependency] protected readonly ISharedAdminLogManager Logs = default!;
    [Dependency] protected readonly ContestsSystem Contests = default!;
    [Dependency] protected readonly DamageableSystem Damageable = default!;
    [Dependency] protected readonly ExamineSystemShared Examine = default!;
    [Dependency] private   readonly ItemSlotsSystem _slots = default!;
    [Dependency] private   readonly RechargeBasicEntityAmmoSystem _recharge = default!;
    [Dependency] protected readonly SharedActionsSystem Actions = default!;
    [Dependency] protected readonly SharedAppearanceSystem Appearance = default!;
    [Dependency] protected readonly SharedAudioSystem Audio = default!;
    [Dependency] private   readonly SharedCombatModeSystem _combatMode = default!;
    [Dependency] protected readonly SharedContainerSystem Containers = default!;
    [Dependency] private   readonly SharedGravitySystem _gravity = default!;
    [Dependency] protected readonly SharedPointLightSystem Lights = default!;
    [Dependency] protected readonly SharedPopupSystem PopupSystem = default!;
    [Dependency] protected readonly SharedPhysicsSystem Physics = default!;
    [Dependency] protected readonly SharedProjectileSystem Projectiles = default!;
    [Dependency] protected readonly SharedTransformSystem TransformSystem = default!;
    [Dependency] protected readonly TagSystem TagSystem = default!;
    [Dependency] protected readonly ThrowingSystem ThrowingSystem = default!;
    [Dependency] private   readonly UseDelaySystem _useDelay = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private readonly IEntityManager _entManager = default!;

    private const float InteractNextFire = 0.3f;
    private const double SafetyNextFire = 0.5;
    private const float EjectOffset = 0.4f;
    protected const string AmmoExamineColor = "yellow";
    protected const string FireRateExamineColor = "yellow";
    public const string ModeExamineColor = "cyan";
    private const float DamagePitchVariation = 0.05f;

    public bool GunPrediction { get; private set; }

    public override void Initialize()
    {
        SubscribeAllEvent<RequestStopShootEvent>(OnStopShootRequest);
        SubscribeLocalEvent<GunComponent, MeleeHitEvent>(OnGunMelee);

        // Ammo providers
        InitializeBallistic();
        InitializeBattery();
        InitializeCartridge();
        InitializeChamberMagazine();
        InitializeMagazine();
        InitializeRevolver();
        InitializeBasicEntity();
        InitializeClothing();
        InitializeContainer();
        InitializeSolution();

        // Interactions
        SubscribeLocalEvent<GunComponent, GetVerbsEvent<AlternativeVerb>>(OnAltVerb);
        SubscribeLocalEvent<GunComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<GunComponent, CycleModeEvent>(OnCycleMode);
        SubscribeLocalEvent<GunComponent, HandSelectedEvent>(OnGunSelected);
        SubscribeLocalEvent<GunComponent, MapInitEvent>(OnMapInit);

        Subs.CVar(_config, PerformanceCVars.GunPrediction, v => GunPrediction = v, true);
    }

    private void OnMapInit(Entity<GunComponent> gun, ref MapInitEvent args)
    {
#if DEBUG
        if (gun.Comp.NextFire > Timing.CurTime)
            Log.Warning($"Initializing a map that contains an entity that is on cooldown. Entity: {ToPrettyString(gun)}");

        DebugTools.Assert((gun.Comp.AvailableModes & gun.Comp.SelectedMode) != 0x0);
#endif

        RefreshModifiers((gun, gun));
    }

    private void OnGunMelee(EntityUid uid, GunComponent component, MeleeHitEvent args)
    {
        if (!TryComp<MeleeWeaponComponent>(uid, out var melee))
            return;

        if (melee.NextAttack > component.NextFire)
        {
            component.NextFire = melee.NextAttack;
            Dirty(uid, component);
        }
    }

    private void OnStopShootRequest(RequestStopShootEvent ev, EntitySessionEventArgs args)
    {
        var gunUid = GetEntity(ev.Gun);

        var user = args.SenderSession.AttachedEntity;

        if (user == null)
            return;

        if (TryComp<MechPilotComponent>(user.Value, out var mechPilot) &&
            TryComp<MechComponent>(mechPilot.Mech, out var mech) &&
            mech.CurrentSelectedEquipment.HasValue)
            user = mechPilot.Mech;

        if (!TryGetGun(user.Value, out var ent, out var gun))
            return;

        if (ent != gunUid)
            return;

        StopShooting(gunUid, gun);
    }

    public bool CanShoot(GunComponent component)
    {
        if (component.NextFire > Timing.CurTime)
            return false;

        return true;
    }

    public bool TryGetGun(EntityUid entity, out EntityUid gunEntity, [NotNullWhen(true)] out GunComponent? gunComp)
    {
        gunEntity = default;
        gunComp = null;

        if (TryComp<MechComponent>(entity, out var mech) &&
            mech.CurrentSelectedEquipment.HasValue &&
            TryComp<GunComponent>(mech.CurrentSelectedEquipment.Value, out var mechGun))
        {
            gunEntity = mech.CurrentSelectedEquipment.Value;
            gunComp = mechGun;
            return true;
        }

        if (EntityManager.TryGetComponent(entity, out HandsComponent? hands) &&
            hands.ActiveHandEntity is { } held &&
            TryComp(held, out GunComponent? gun))
        {
            gunEntity = held;
            gunComp = gun;
            return true;
        }

        // Last resort is check if the entity itself is a gun.
        if (TryComp(entity, out gun))
        {
            gunEntity = entity;
            gunComp = gun;
            return true;
        }

        return false;
    }

    private void StopShooting(EntityUid uid, GunComponent gun)
    {
        if (gun.ShotCounter == 0)
            return;

        gun.ShotCounter = 0;
        gun.ShootCoordinates = null;
        gun.Target = null;
        Dirty(uid, gun);
    }

    /// <summary>
    /// Attempts to shoot at the target coordinates. Resets the shot counter after every shot.
    /// </summary>
    public void AttemptShoot(EntityUid user, EntityUid gunUid, GunComponent gun, EntityCoordinates toCoordinates)
    {
        gun.ShootCoordinates = toCoordinates;
        AttemptShoot(user, gunUid, gun);
        gun.ShotCounter = 0;
    }

    /// <summary>
    /// Shoots by assuming the gun is the user at default coordinates.
    /// </summary>
    public void AttemptShoot(EntityUid gunUid, GunComponent gun)
    {
        var coordinates = new EntityCoordinates(gunUid, gun.DefaultDirection);
        gun.ShootCoordinates = coordinates;
        AttemptShoot(gunUid, gunUid, gun);
        gun.ShotCounter = 0;
    }

    public List<EntityUid>? ShootRequested(NetEntity netGun,
        NetCoordinates coordinates,
        NetEntity? target,
        List<int>? predictedProjectiles,
        ICommonSession session)
    {
        var user = session.AttachedEntity;

        if (user == null ||
            !_combatMode.IsInCombatMode(user))
        {
            return null;
        }

        if (TryComp<MechPilotComponent>(user.Value, out var mechPilot))
            user = mechPilot.Mech;

        if (!TryGetGun(user.Value, out var ent, out var gun) ||
            HasComp<ItemComponent>(user) ||
            ent != GetEntity(netGun))
        {
            return null;
        }

        gun.ShootCoordinates = GetCoordinates(coordinates);
        gun.Target = GetEntity(target);
        return AttemptShoot(user.Value, ent, gun, predictedProjectiles, session);
    }

    private List<EntityUid>? AttemptShoot(EntityUid user,
        EntityUid gunUid,
        GunComponent gun,
        List<int>? predictedProjectiles = null,
        ICommonSession? userSession = null)
    {
        if (gun.FireRateModified <= 0f ||
            !_actionBlockerSystem.CanAttack(user))
            return null;

        var toCoordinates = gun.ShootCoordinates;

        if (toCoordinates == null)
            return null;

        var curTime = Timing.CurTime;

        // check if anything wants to prevent shooting
        var prevention = new ShotAttemptedEvent
        {
            User = user,
            Used = (gunUid, gun)
        };
        RaiseLocalEvent(gunUid, ref prevention);
        if (prevention.Cancelled)
            return null;

        RaiseLocalEvent(user, ref prevention);
        if (prevention.Cancelled)
            return null;

        // Need to do this to play the clicking sound for empty automatic weapons
        // but not play anything for burst fire.
        if (gun.NextFire > curTime)
            return null;

        var fireRate = TimeSpan.FromSeconds(1f / gun.FireRateModified);

        if (gun.SelectedMode == SelectiveFire.Burst || gun.BurstActivated)
            fireRate = TimeSpan.FromSeconds(1f / gun.BurstFireRate);

        // First shot
        // Previously we checked shotcounter but in some cases all the bullets got dumped at once
        // curTime - fireRate is insufficient because if you time it just right you can get a 3rd shot out slightly quicker.
        if (gun.NextFire < curTime - fireRate || gun.ShotCounter == 0 && gun.NextFire < curTime)
            gun.NextFire = curTime;

        var shots = 0;
        var lastFire = gun.NextFire;

        while (gun.NextFire <= curTime)
        {
            gun.NextFire += fireRate;
            shots++;
        }

        // NextFire has been touched regardless so need to dirty the gun.
        Dirty(gunUid, gun);

        // Get how many shots we're actually allowed to make, due to clip size or otherwise.
        // Don't do this in the loop so we still reset NextFire.
        if (!gun.BurstActivated)
        {
            switch (gun.SelectedMode)
            {
                case SelectiveFire.SemiAuto:
                    shots = Math.Min(shots, 1 - gun.ShotCounter);
                    break;
                case SelectiveFire.Burst:
                    shots = Math.Min(shots, gun.ShotsPerBurstModified - gun.ShotCounter);
                    break;
                case SelectiveFire.FullAuto:
                    break;
                default:
                    throw new ArgumentOutOfRangeException($"No implemented shooting behavior for {gun.SelectedMode}!");
            }
        } else
            shots = Math.Min(shots, gun.ShotsPerBurstModified - gun.ShotCounter);

        var attemptEv = new AttemptShootEvent(user, null);
        RaiseLocalEvent(gunUid, ref attemptEv);

        if (attemptEv.Cancelled)
        {
            if (attemptEv.Message != null)
                PopupSystem.PopupClient(attemptEv.Message, gunUid, user);

            gun.BurstActivated = false;
            gun.BurstShotsCount = 0;
            gun.NextFire = TimeSpan.FromSeconds(Math.Max(lastFire.TotalSeconds + SafetyNextFire, gun.NextFire.TotalSeconds));
            return null;
        }

        gun.ShotCounter += shots;

        if (!Timing.IsFirstTimePredicted)
            return null;

        var fromCoordinates = Transform(user).Coordinates;
        var ignoredEntity = GetShotIgnoreEntity(user);

        // #Misfits Add: apply per-gun origin offset (e.g. Assaultron head beam)
        if (gun.ShootOffset != Vector2.Zero)
            fromCoordinates = fromCoordinates.Offset(gun.ShootOffset);

        // Remove ammo
        var ev = new TakeAmmoEvent(shots, new List<(EntityUid? Entity, IShootable Shootable)>(), fromCoordinates, user);

        // Listen it just makes the other code around it easier if shots == 0 to do this.
        if (shots > 0)
            RaiseLocalEvent(gunUid, ev);

        DebugTools.Assert(ev.Ammo.Count <= shots);
        DebugTools.Assert(shots >= 0);
        UpdateAmmoCount(gunUid);

        if (ev.Ammo.Count <= 0)
        {
            // triggers effects on the gun if it's empty
            var emptyGunShotEvent = new OnEmptyGunShotEvent();
            RaiseLocalEvent(gunUid, ref emptyGunShotEvent);

            gun.BurstActivated = false;
            gun.BurstShotsCount = 0;
            gun.NextFire += TimeSpan.FromSeconds(gun.BurstCooldown);

            // Play empty gun sounds if relevant
            // If they're firing an existing clip then don't play anything.
            if (shots > 0)
            {
                if (ev.Reason != null && Timing.IsFirstTimePredicted)
                {
                    PopupSystem.PopupCursor(ev.Reason);
                }

                // Don't spam safety sounds at gun fire rate, play it at a reduced rate.
                // May cause prediction issues? Needs more tweaking
                gun.NextFire = TimeSpan.FromSeconds(Math.Max(lastFire.TotalSeconds + SafetyNextFire, gun.NextFire.TotalSeconds));
                Audio.PlayPredicted(gun.SoundEmpty, gunUid, user);
                return null;
            }

            return null;
        }

        // Handle burstfire
        if (gun.SelectedMode == SelectiveFire.Burst)
        {
            gun.BurstActivated = true;
        }
        if (gun.BurstActivated)
        {
            gun.BurstShotsCount += shots;
            if (gun.BurstShotsCount >= gun.ShotsPerBurstModified)
            {
                gun.NextFire += TimeSpan.FromSeconds(gun.BurstCooldown);
                gun.BurstActivated = false;
                gun.BurstShotsCount = 0;
            }
        }

        // Shoot confirmed - sounds also played here in case it's invalid (e.g. cartridge already spent).
        var projectiles = Shoot(
            gunUid,
            gun,
            ev.Ammo,
            fromCoordinates,
            toCoordinates.Value,
            out var userImpulse,
            user,
            throwItems: attemptEv.ThrowItems,
            predictedProjectiles: predictedProjectiles,
            userSession: userSession);
        var shotEv = new GunShotEvent(user, ev.Ammo);
        RaiseLocalEvent(gunUid, ref shotEv);

        if (userImpulse && TryComp<PhysicsComponent>(user, out var userPhysics))
        {
            if (_gravity.IsWeightless(user, userPhysics))
                CauseImpulse(fromCoordinates, toCoordinates.Value, user, userPhysics);
        }

        Dirty(gunUid, gun);
        return projectiles;
    }

    public void Shoot(
        EntityUid gunUid,
        GunComponent gun,
        EntityUid ammo,
        EntityCoordinates fromCoordinates,
        EntityCoordinates toCoordinates,
        out bool userImpulse,
        EntityUid? user = null,
        bool throwItems = false)
    {
        var shootable = EnsureShootable(ammo);
        Shoot(gunUid,
            gun,
            new List<(EntityUid? Entity, IShootable Shootable)>(1) { (ammo, shootable) },
            fromCoordinates,
            toCoordinates,
            out userImpulse,
            user,
            throwItems);
    }

    public abstract List<EntityUid>? Shoot(
        EntityUid gunUid,
        GunComponent gun,
        List<(EntityUid? Entity, IShootable Shootable)> ammo,
        EntityCoordinates fromCoordinates,
        EntityCoordinates toCoordinates,
        out bool userImpulse,
        EntityUid? user = null,
        bool throwItems = false,
        List<int>? predictedProjectiles = null,
        ICommonSession? userSession = null);

    public virtual void ShootProjectile(EntityUid uid,
        Vector2 direction,
        Vector2 gunVelocity,
        EntityUid gunUid,
        EntityUid? user = null,
        float speed = 20f)
    {
        var physics = EnsureComp<PhysicsComponent>(uid);
        Physics.SetBodyStatus(uid, physics, BodyStatus.InAir);

        // #Misfits Fix - Prevent projectiles from inheriting parent physics (e.g. mount velocity).
        // Revolvers and battery weapons spawn projectiles at the shooter's (mount-local) coordinates,
        // making them transform children of dynamic entities like Brahdo mounts or motorcycles.
        // This caused the projectile to inherit the mount's angular/linear velocity through the
        // transform hierarchy, letting riders "steer" bullets mid-flight by rotating the mount.
        // Ballistic weapons were not affected because they re-spawn at grid-level coordinates.
        //
        // Fix: reparent the projectile to the map and set map-level velocity directly,
        // so parent physics cannot influence the projectile's trajectory.
        var mapCoords = TransformSystem.GetMapCoordinates(uid);
        TransformSystem.SetCoordinates(uid, TransformSystem.ToCoordinates(mapCoords));
        Physics.SetLinearVelocity(uid, gunVelocity + direction.Normalized() * speed, body: physics);

        var projectile = EnsureComp<ProjectileComponent>(uid);
        Projectiles.SetShooter(uid, projectile, user ?? gunUid);
        projectile.Weapon = gunUid;
        projectile.ExtraIgnoredEntity = GetShotIgnoreEntity(user);

        TransformSystem.SetWorldRotationNoLerp(uid, direction.ToWorldAngle() + projectile.Angle);
    }

    protected void ShootOrThrow(EntityUid uid,
        Vector2 mapDirection,
        Vector2 gunVelocity,
        GunComponent gun,
        EntityUid gunUid,
        EntityUid? user)
    {
        if (gun.Target is { } target && !TerminatingOrDeleted(target))
        {
            var targeted = EnsureComp<TargetedProjectileComponent>(uid);
            targeted.Target = target;
            Dirty(uid, targeted);
        }

        if (!HasComp<ProjectileComponent>(uid))
        {
            RemoveShootable(uid);
            ThrowingSystem.TryThrow(uid, mapDirection, gun.ProjectileSpeedModified, user);
            return;
        }

        ShootProjectile(uid, mapDirection, gunVelocity, gunUid, user, gun.ProjectileSpeedModified);
    }

    protected Angle[] LinearSpread(Angle start, Angle end, int intervals)
    {
        var angles = new Angle[intervals];
        DebugTools.Assert(intervals > 1);

        for (var i = 0; i <= intervals - 1; i++)
        {
            angles[i] = new Angle(start + (end - start) * i / (intervals - 1));
        }

        return angles;
    }

    protected Angle GetRecoilAngle(TimeSpan curTime, GunComponent component, Angle direction, EntityUid? user = null)
    {
        var timeSinceLastFire = (curTime - component.LastFire).TotalSeconds;
        var newTheta = MathHelper.Clamp(component.CurrentAngle.Theta + component.AngleIncreaseModified.Theta - component.AngleDecayModified.Theta * timeSinceLastFire,
            component.MinAngleModified.Theta,
            component.MaxAngleModified.Theta);
        component.CurrentAngle = new Angle(newTheta);
        component.LastFire = component.NextFire;

        float random;
        if (GunPrediction)
        {
            ulong tick = ((ulong) Timing.CurTick.Value << 32) | (uint) GetNetEntity(component.Owner).Id;
            random = new Xoroshiro64S(tick).NextFloat(-0.5f, 0.5f);
        }
        else
        {
            random = Random.NextFloat(-0.5f, 0.5f);
        }

        random /= Contests.MassContest(user);
        var spread = component.CurrentAngle.Theta * random;
        var angle = new Angle(direction.Theta + spread);
        DebugTools.Assert(spread <= component.MaxAngleModified.Theta);
        return angle;
    }

    public void PlayImpactSound(EntityUid otherEntity,
        DamageSpecifier? modifiedDamage,
        SoundSpecifier? weaponSound,
        bool forceWeaponSound,
        Filter? filter = null,
        EntityUid? projectile = null)
    {
        DebugTools.Assert(!Deleted(otherEntity), "Impact sound entity was deleted");

        if (_netManager.IsClient && HasComp<PredictedProjectileServerComponent>(projectile))
            return;

        filter ??= Filter.Pvs(otherEntity, entityManager: EntityManager);
        var playedSound = false;

        if (!forceWeaponSound &&
            modifiedDamage != null &&
            modifiedDamage.GetTotal() > 0 &&
            TryComp<RangedDamageSoundComponent>(otherEntity, out var rangedSound))
        {
            var type = SharedMeleeWeaponSystem.GetHighestDamageSound(modifiedDamage, ProtoManager);

            if (type != null &&
                rangedSound.SoundTypes?.TryGetValue(type, out var damageSoundType) == true &&
                filter.Count > 0)
            {
                Audio.PlayEntity(damageSoundType, filter, otherEntity, true, AudioParams.Default.WithVariation(DamagePitchVariation));
                playedSound = true;
            }
            else if (type != null &&
                     rangedSound.SoundGroups?.TryGetValue(type, out var damageSoundGroup) == true &&
                     filter.Count > 0)
            {
                Audio.PlayEntity(damageSoundGroup, filter, otherEntity, true, AudioParams.Default.WithVariation(DamagePitchVariation));
                playedSound = true;
            }
        }

        if (!playedSound && weaponSound != null && filter.Count > 0)
            Audio.PlayEntity(weaponSound, filter, otherEntity, true);
    }

    protected EntityUid? GetShotIgnoreEntity(EntityUid? user)
    {
        if (user is not { } shooter)
            return null;

        if (!TryComp<MechPilotComponent>(shooter, out var mechPilot))
            return shooter;

        return mechPilot.Mech;
    }

    protected bool TryResolveGunHitscan(EntityUid gunUid, out HitscanPrototype hitscan)
    {
        hitscan = default!;

        var providerUid = gunUid;
        if (!TryComp<HitscanBatteryAmmoProviderComponent>(providerUid, out var provider))
        {
            var magEnt = GetMagazineEntity(gunUid);
            if (magEnt == null || !TryComp<HitscanBatteryAmmoProviderComponent>(magEnt.Value, out provider))
                return false;

            providerUid = magEnt.Value;
        }

        if (!ProtoManager.TryIndex(provider.Prototype, out HitscanPrototype? baseHitscan))
            return false;

        hitscan = baseHitscan;

        if (providerUid != gunUid &&
            TryComp<GunDamageBonusComponent>(providerUid, out var providerOverride) &&
            providerOverride.HitscanProtoOverride != null &&
            ProtoManager.TryIndex(providerOverride.HitscanProtoOverride, out HitscanPrototype? providerOverrideHitscan))
        {
            hitscan = providerOverrideHitscan;
        }

        if (TryComp<GunDamageBonusComponent>(gunUid, out var gunOverride) &&
            gunOverride.HitscanProtoOverride != null &&
            ProtoManager.TryIndex(gunOverride.HitscanProtoOverride, out HitscanPrototype? gunOverrideHitscan))
        {
            hitscan = gunOverrideHitscan;
        }

        return true;
    }

    protected bool IsValidHitscanTarget(EntityUid hitEntity, EntityUid? target, bool firedFromContainer)
    {
        return firedFromContainer ||
               hitEntity == target ||
               CompOrNull<RequireProjectileTargetComponent>(hitEntity)?.Active != true;
    }

    protected bool TryGetFirstValidHitscanResult(
        IEnumerable<RayCastResults> results,
        EntityUid? target,
        bool firedFromContainer,
        out EntityUid hit,
        out float distance)
    {
        foreach (var result in results)
        {
            if (!IsValidHitscanTarget(result.HitEntity, target, firedFromContainer))
                continue;

            hit = result.HitEntity;
            distance = result.Distance;
            return true;
        }

        hit = default;
        distance = 0f;
        return false;
    }

    protected static bool TryIntersectSegmentBox(Vector2 start, Vector2 end, Box2 box, out float fraction)
    {
        if (box.Contains(start))
        {
            fraction = 0f;
            return true;
        }

        var direction = end - start;
        var min = 0f;
        var max = 1f;

        if (!ClipAxis(start.X, direction.X, box.Left, box.Right, ref min, ref max) ||
            !ClipAxis(start.Y, direction.Y, box.Bottom, box.Top, ref min, ref max))
        {
            fraction = 0f;
            return false;
        }

        fraction = min;
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

    protected abstract void Popup(string message, EntityUid? uid, EntityUid? user);

    /// <summary>
    /// Call this whenever the ammo count for a gun changes.
    /// </summary>
    protected virtual void UpdateAmmoCount(EntityUid uid, bool prediction = true) {}

    protected void SetCartridgeSpent(EntityUid uid, CartridgeAmmoComponent cartridge, bool spent)
    {
        if (cartridge.Spent != spent)
            Dirty(uid, cartridge);

        cartridge.Spent = spent;
        Appearance.SetData(uid, AmmoVisuals.Spent, spent);
    }

    /// <summary>
    /// Drops a single cartridge / shell
    /// </summary>
    protected void EjectCartridge(
        EntityUid entity,
        Angle? angle = null,
        bool playSound = true)
    {
        // TODO: Sound limit version.
        var offsetPos = Random.NextVector2(EjectOffset);
        var xform = Transform(entity);

        var coordinates = xform.Coordinates;
        coordinates = coordinates.Offset(offsetPos);

        TransformSystem.SetLocalRotation(xform, Random.NextAngle());
        TransformSystem.SetCoordinates(entity, xform, coordinates);

        // decides direction the casing ejects and only when not cycling
        if (angle != null)
        {
            Angle ejectAngle = angle.Value;
            ejectAngle += 3.7f; // 212 degrees; casings should eject slightly to the right and behind of a gun
            ThrowingSystem.TryThrow(entity, ejectAngle.ToVec().Normalized() / 100, 5f);
        }
        if (playSound && TryComp<CartridgeAmmoComponent>(entity, out var cartridge))
        {
            Audio.PlayPvs(cartridge.EjectSound, entity, AudioParams.Default.WithVariation(SharedContentAudioSystem.DefaultVariation).WithVolume(-1f));
        }

        // Make spent cartridges unpickable and automatically despawn when ejected.
        if (TryComp<CartridgeAmmoComponent>(entity, out var cartridge2) && cartridge2.Spent)
        {
            var despawn = EnsureComp<TimedDespawnComponent>(entity);
            despawn.Lifetime = 30f; // #Misfits Tweak - Reduce casing despawn from 5min to 30s to prevent entity buildup during war

            _entManager.RemoveComponent<ItemComponent>(entity);

            // #Misfits Fix - Casings ejected without a throw angle (revolver/manual cycling)
            // never get ThrownItemComponent, so LandEvent never fires and
            // CasingPhysicsOptSystem can't strip their physics. Remove it here.
            if (angle == null)
                RemCompDeferred<PhysicsComponent>(entity);
        }
    }

    protected IShootable EnsureShootable(EntityUid uid)
    {
        if (TryComp<CartridgeAmmoComponent>(uid, out var cartridge))
            return cartridge;

        return EnsureComp<AmmoComponent>(uid);
    }

    protected void RemoveShootable(EntityUid uid)
    {
        RemCompDeferred<CartridgeAmmoComponent>(uid);
        RemCompDeferred<AmmoComponent>(uid);
    }

    protected void MuzzleFlash(EntityUid gun, AmmoComponent component, Angle worldAngle, EntityUid? user = null, EntityUid? player = null)
    {
        var attemptEv = new GunMuzzleFlashAttemptEvent();
        RaiseLocalEvent(gun, ref attemptEv);
        if (attemptEv.Cancelled)
            return;

        var sprite = component.MuzzleFlash;

        if (sprite == null)
            return;

        var ev = new MuzzleFlashEvent(GetNetEntity(gun), sprite, worldAngle);
        CreateEffect(gun, ev, user, player);
    }

    public void CauseImpulse(EntityCoordinates fromCoordinates, EntityCoordinates toCoordinates, EntityUid user, PhysicsComponent userPhysics)
    {
        var fromMap = fromCoordinates.ToMapPos(EntityManager, TransformSystem);
        var toMap = toCoordinates.ToMapPos(EntityManager, TransformSystem);
        var shotDirection = (toMap - fromMap).Normalized();

        const float impulseStrength = 25.0f;
        var impulseVector =  shotDirection * impulseStrength;
        Physics.ApplyLinearImpulse(user, -impulseVector, body: userPhysics);
    }

    public void RefreshModifiers(Entity<GunComponent?> gun)
    {
        if (!Resolve(gun, ref gun.Comp))
            return;

        var comp = gun.Comp;
        var ev = new GunRefreshModifiersEvent(
            (gun, comp),
            comp.SoundGunshot,
            comp.CameraRecoilScalar,
            comp.AngleIncrease,
            comp.AngleDecay,
            comp.MaxAngle,
            comp.MinAngle,
            comp.ShotsPerBurst,
            comp.FireRate,
            comp.ProjectileSpeed
        );

        RaiseLocalEvent(gun, ref ev);

        comp.SoundGunshotModified = ev.SoundGunshot;
        comp.CameraRecoilScalarModified = ev.CameraRecoilScalar;
        comp.AngleIncreaseModified = ev.AngleIncrease;
        comp.AngleDecayModified = ev.AngleDecay;
        comp.MaxAngleModified = ev.MaxAngle;
        comp.MinAngleModified = ev.MinAngle;
        comp.ShotsPerBurstModified = ev.ShotsPerBurst;
        comp.FireRateModified = ev.FireRate;
        comp.ProjectileSpeedModified = ev.ProjectileSpeed;

        Dirty(gun);
    }

    protected abstract void CreateEffect(EntityUid gunUid, MuzzleFlashEvent message, EntityUid? user = null, EntityUid? player = null);

    // Corvax-Change-Start
    public void ChangeTarget(EntityUid target, GunComponent gun)
    {
        gun.Target = target;
    }
    // Corvax-Change-End

    /// <summary>
    /// Used for animated effects on the client.
    /// </summary>
    [Serializable, NetSerializable]
    public sealed class HitscanEvent : EntityEventArgs
    {
        public List<(NetCoordinates coordinates, Angle angle, SpriteSpecifier Sprite, float Distance)> Sprites = new();

        // #Misfits Add: optional beam customisation forwarded from HitscanPrototype
        public Color? TintColor;
        public float BeamWidth = 1f;
        public float BeamDuration = 0.48f;
    }
}

/// <summary>
///     Raised directed on the gun before firing to see if the shot should go through.
/// </summary>
/// <remarks>
///     Handling this in server exclusively will lead to mispredicts.
/// </remarks>
/// <param name="User">The user that attempted to fire this gun.</param>
/// <param name="Cancelled">Set this to true if the shot should be cancelled.</param>
/// <param name="ThrowItems">Set this to true if the ammo shouldn't actually be fired, just thrown.</param>
[ByRefEvent]
public record struct AttemptShootEvent(EntityUid User, string? Message, bool Cancelled = false, bool ThrowItems = false);

/// <summary>
///     Raised directed on the gun after firing.
/// </summary>
/// <param name="User">The user that fired this gun.</param>
[ByRefEvent]
public record struct GunShotEvent(EntityUid User, List<(EntityUid? Uid, IShootable Shootable)> Ammo);

public enum EffectLayers : byte
{
    Unshaded,
}

[Serializable, NetSerializable]
public enum AmmoVisuals : byte
{
    Spent,
    AmmoCount,
    AmmoMax,
    HasAmmo, // used for generic visualizers. c# stuff can just check ammocount != 0
    MagLoaded,
    BoltClosed,
}
