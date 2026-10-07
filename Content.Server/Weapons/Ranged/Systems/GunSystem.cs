using System.Numerics;
using Content.Server._Misfits.Movement;
using Content.Server._Misfits.Weapons.Ranged.Flamer;
using Content.Server.Cargo.Systems;
using Content.Server.Movement.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Effects;
using Content.Shared.Projectiles;
using Content.Shared._Misfits.CCVar;
using Content.Shared._Misfits.Special;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Weapons.Reflect;
using Content.Shared._Misfits.Weapons; // #Misfits Add - GunDamageBonusComponent support
using Content.Server._Misfits.Weapons.Ranged.Prediction;
using Content.Shared._Misfits.Weapons.Ranged.Flamer;
using Content.Shared._Misfits.Weapons.Ranged.Prediction;
using Content.Server.Weapons.Ranged.Events;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Player;
using Robust.Shared.Utility;
using Robust.Shared.Containers;
using Robust.Shared.Audio.Systems;
using Content.Shared.Audio;
using Robust.Shared.Random;
using Content.Server.Popups;
using Robust.Server.Audio;

namespace Content.Server.Weapons.Ranged.Systems;

public sealed partial class GunSystem : SharedGunSystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private PopupSystem _serverPopUp = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private BatterySystem _battery = default!;
    [Dependency] private DamageExamineSystem _damageExamine = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private SharedColorFlashEffectSystem _color = default!;
    [Dependency] private ServerMisfitsLagCompensationSystem _lagCompensation = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private StaminaSystem _stamina = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private GunPredictionSystem _gunPrediction = default!;
    [Dependency] private FlamerLineSystem _flamerLine = default!;
    [Dependency] private AudioSystem _audio = default!;
    private readonly HashSet<EntityUid> _lagCompCandidates = [];
    private float _lagCompAabbEnlargement;
    private float _lagCompHitscanSearchPadding;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BallisticAmmoProviderComponent, PriceCalculationEvent>(OnBallisticPrice);
        Subs.CVar(_config, PerformanceCVars.GunPredictionAabbEnlargement, v => _lagCompAabbEnlargement = v, true);
        Subs.CVar(_config, PerformanceCVars.GunPredictionHitscanSearchPadding, v => _lagCompHitscanSearchPadding = v, true);
    }

    private void OnBallisticPrice(EntityUid uid, BallisticAmmoProviderComponent component, ref PriceCalculationEvent args)
    {
        if (string.IsNullOrEmpty(component.Proto) || component.UnspawnedCount == 0)
            return;

        if (!ProtoManager.TryIndex<EntityPrototype>(component.Proto, out var proto))
        {
            Log.Error($"Unable to find fill prototype for price on {component.Proto} on {ToPrettyString(uid)}");
            return;
        }

        // Probably good enough for most.
        var price = _pricing.GetEstimatedPrice(proto);
        args.Price += price * component.UnspawnedCount;
    }

    public override List<EntityUid>? Shoot(EntityUid gunUid,
        GunComponent gun,
        List<(EntityUid? Entity, IShootable Shootable)> ammo,
        EntityCoordinates fromCoordinates,
        EntityCoordinates toCoordinates,
        out bool userImpulse,
        EntityUid? user = null,
        bool throwItems = false,
        List<int>? predictedProjectiles = null,
        ICommonSession? userSession = null)
    {
        userImpulse = true;

        if (user != null)
        {
            var selfEvent = new SelfBeforeGunShotEvent(user.Value, (gunUid, gun), ammo);
            RaiseLocalEvent(user.Value, selfEvent);
            if (selfEvent.Cancelled)
            {
                userImpulse = false;
                return null;
            }
        }

        var fromMap = fromCoordinates.ToMap(EntityManager, _xform);
        var toMap = toCoordinates.ToMapPos(EntityManager, _xform);
        var mapDirection = toMap - fromMap.Position;
        var mapAngle = mapDirection.ToAngle();
        var angle = base.GetRecoilAngle(Timing.CurTime, gun, mapDirection.ToAngle(), user);

        // If applicable, this ensures the projectile is parented to grid on spawn, instead of the map.
        var fromEnt = MapManager.TryFindGridAt(fromMap, out var gridUid, out var grid)
            ? fromCoordinates.WithEntityId(gridUid, EntityManager)
            : new EntityCoordinates(MapManager.GetMap(fromMap.MapId), fromMap.Position);

        // Update shot based on the recoil
        toMap = fromMap.Position + angle.ToVec() * mapDirection.Length();
        mapDirection = toMap - fromMap.Position;
        var gunVelocity = Physics.GetMapLinearVelocity(fromEnt);

        // I must be high because this was getting tripped even when true.
        // DebugTools.Assert(direction != Vector2.Zero);
        var shotProjectiles = new List<EntityUid>(ammo.Count);
        var predictedIndex = 0;

        void MarkPredicted(EntityUid uid)
        {
            if (!GunPrediction || predictedProjectiles == null || userSession == null)
                return;

            if (predictedIndex >= predictedProjectiles.Count)
                return;

            var comp = EnsureComp<PredictedProjectileServerComponent>(uid);
            comp.Shooter = userSession;
            comp.ClientId = predictedProjectiles[predictedIndex++];
            comp.ClientEnt = user;
            Dirty(uid, comp);
            _gunPrediction.RegisterPredictedProjectile(uid, comp);
        }

        foreach (var (ent, shootable) in ammo)
        {
            // pneumatic cannon doesn't shoot bullets it just throws them, ignore ammo handling
            if (throwItems && ent != null)
            {
                base.ShootOrThrow(ent.Value, mapDirection, gunVelocity, gun, gunUid, user);
                continue;
            }
            // TODO: change ishootable to generic
            switch (shootable)
            {
                // Cartridge shoots something else
                case CartridgeAmmoComponent cartridge:
                    if (!cartridge.Spent)
                    {
                        var uid = Spawn(cartridge.Prototype, fromEnt);
                        CreateAndFireProjectiles(uid, cartridge);

                        RaiseLocalEvent(ent!.Value, new AmmoShotEvent()
                        {
                            FiredProjectiles = shotProjectiles,
                        });

                        SetCartridgeSpent(ent.Value, cartridge, true);

                        if (cartridge.DeleteOnSpawn)
                            Del(ent.Value);
                    }
                    else
                    {
                        userImpulse = false;
                        Audio.PlayPredicted(gun.SoundEmpty, gunUid, user);
                    }

                    // Something like ballistic might want to leave it in the container still
                    if (!cartridge.DeleteOnSpawn && !Containers.IsEntityInContainer(ent!.Value))
                        EjectCartridge(ent.Value, baseCoords: Transform(gunUid).Coordinates, angle, userSession: userSession);

                    Dirty(ent!.Value, cartridge);
                    break;
                // Ammo shoots itself
                case AmmoComponent newAmmo:
                    if (ent == null)
                        break;
                    CreateAndFireProjectiles(ent.Value, newAmmo);

                    break;
                // TODO: when this this refactoredto be generics rename as like "non-piercing" hitscan or something
                case HitscanPrototype hitscan:
                    if (TryResolveGunHitscan(gunUid, out var resolvedHitscan))
                        hitscan = resolvedHitscan;

                    EntityUid lastHit = EntityUid.Invalid;

                    var beamOriginMapCoords = fromMap;
                    // [Changed by MisfitsCrew/Operator] Use grid/map effect coordinates so beam
                    // sprites are not parented to bikes or other ridden entities.
                    var beamOriginEntCoords = GetShotEffectCoordinates(fromMap);
                    var beamDir = mapDirection.Normalized();

                    //in the situation when user == null, means that the cannon fires on its own (via signals). And we need the gun to not fire by itself in this case
                    var lastUser = user ?? gunUid;
                    // [Changed by MisfitsCrew/Operator] Rider-fired hitscan must ignore both
                    // the rider and their mounted vehicle; one ignored entity is not enough.

                    // TODO: make this an event for the entity. Maybe broadcasted too
                    // so do RaiseLocalEvent(uid,IgnoreEvent ev)
                    // ev gets passed to different comps that fills a list in ev or a filter or both
                    // for riders and cars and prolly other special behaviors
                    // event itself also call another event for the gun
                    var rayExtraIgnore = GetShotExtraIgnoredEntity(user);
                    bool beamHitSomething = TryGetHitscanResult(beamOriginMapCoords, beamDir, hitscan, lastUser,
                                                    rayExtraIgnore, gun.Target, userSession,
                                                    out lastHit, out var distance);
                    // always filter out userSession, since we are still predicting the 1st shot that isnt reflected
                    FireEffects(beamOriginEntCoords, distance, beamDir.Normalized().ToAngle(), hitscan, lastHit, shooterSession: userSession);
                    // TODO: course this reflect logic/sequence should be in its own method to start no inline clutter
                    if (hitscan.Reflective == ReflectType.Energy && beamHitSomething)
                    {

                        for (int i = 0; i < 3; i++)
                        {
                            var deflectEv = new HitScanReflectAttemptEvent(user, gunUid, hitscan.Reflective, beamDir, false);
                            RaiseLocalEvent(lastHit, ref deflectEv);

                            // beam reflected(only possible to reflect if it hit something before its max range)
                            if (!deflectEv.Reflected || !beamHitSomething)
                                break;

                            // get position data and direction of deflected beam like its a new beam just "shooting" from the deflectee
                            lastUser = lastHit; // we consider the prev hit ent to be the new beam source
                            beamDir = deflectEv.Direction;
                            beamOriginEntCoords = GetShotEffectCoordinates(_transform.ToMapCoordinates(Transform(lastHit).Coordinates));
                            beamOriginMapCoords = _transform.ToMapCoordinates(beamOriginEntCoords);

                            // with new deflect data we treat it like its a new beam and see if it hit something
                            beamHitSomething = TryGetHitscanResult(beamOriginMapCoords, beamDir, hitscan, lastUser,
                               rayExtraIgnore, gun.Target, userSession,
                               out lastHit, out distance);

                            FireEffects(beamOriginEntCoords, distance, beamDir.Normalized().ToAngle(), hitscan, lastUser);
                        }
                    }
                    // in the case of these deflecting beams, whoever was the very last to be hit gets the damage
                    // deflect mechanics even for bullets, totally negates damage.
                    // refactoring needed if we want diff behavior for diff lasers in general
                    if (lastHit != EntityUid.Invalid)
                    {
                        var hitEntity = lastHit;
                        if (hitscan.StaminaDamage > 0f)
                            _stamina.TakeStaminaDamage(hitEntity, hitscan.StaminaDamage, source: user);

                        var dmg = hitscan.Damage;

                        // #Misfits Add - Apply gun-side bonus damage from GunDamageBonusComponent
                        if (dmg != null && TryComp<GunDamageBonusComponent>(gunUid, out var gunBonus) && gunBonus.BonusDamage != null)
                        {
                            dmg = new DamageSpecifier(dmg);
                            dmg += gunBonus.BonusDamage;
                        }

                        if (dmg != null && user != null)
                        {
                            var modifyDamage = new SpecialModifyHitscanDamageEvent(gunUid, dmg);
                            RaiseLocalEvent(user.Value, ref modifyDamage);
                            dmg = modifyDamage.Damage;
                        }

                        var hitName = ToPrettyString(hitEntity);
                        if (dmg != null)
                            dmg = Damageable.TryChangeDamage(hitEntity, dmg, origin: user);

                        // check null again, as TryChangeDamage returns modified damage values
                        if (dmg != null)
                        {
                            if (!Deleted(hitEntity))
                            {
                                if (dmg.AnyPositive() && !HasComp<ActorComponent>(hitEntity))
                                {
                                    var filter = Filter.Pvs(hitEntity, entityManager: EntityManager);
                                    if (userSession != null)
                                        filter.RemovePlayer(userSession);

                                    _color.RaiseEffect(Color.Red, new List<EntityUid>() { hitEntity }, filter);
                                }

                                // TODO get fallback position for playing hit sound.
                                base.PlayImpactSound(hitEntity, dmg, hitscan.Sound, hitscan.ForceSound);
                            }

                            if (user != null)
                            {
                                Logs.Add(LogType.HitScanHit,
                                    $"{ToPrettyString(user.Value):user} hit {hitName:target} using hitscan and dealt {dmg.GetTotal():damage} damage");
                            }
                            else
                            {
                                Logs.Add(LogType.HitScanHit,
                                    $"{hitName:target} hit by hitscan dealing {dmg.GetTotal():damage} damage");
                            }
                        }
                    }

                    if (lastHit != EntityUid.Invalid && user != null)
                    {
                        var hitEv = new HitscanHitEntityEvent(lastHit);
                        RaiseLocalEvent(user.Value, ref hitEv);
                    }

                    Audio.PlayPredicted(gun.SoundGunshotModified, gunUid, user);
                    break;








                case FlamerShot flamerShot:
                    if (flamerShot.ProjectileProto is { } projectileProto)
                    {
                        var projectile = Spawn(projectileProto, fromEnt);
                        base.ShootOrThrow(projectile, mapDirection, gunVelocity, gun, gunUid, user);
                        MarkPredicted(projectile);
                        shotProjectiles.Add(projectile);
                    }
                    _flamerLine.ShootLine(user ?? gunUid, gunUid, flamerShot, fromEnt, gun.ShootCoordinates);
                    Audio.PlayPredicted(gun.SoundGunshotModified, gunUid, user);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        RaiseLocalEvent(gunUid, new AmmoShotEvent()
        {
            FiredProjectiles = shotProjectiles,
        });

        void CreateAndFireProjectiles(EntityUid ammoEnt, AmmoComponent ammoComp)
        {
            if (TryComp<ProjectileSpreadComponent>(ammoEnt, out var ammoSpreadComp))
            {
                var spreadEvent = new GunGetAmmoSpreadEvent(ammoSpreadComp.Spread);
                RaiseLocalEvent(gunUid, ref spreadEvent);

                var angles = base.LinearSpread(mapAngle - spreadEvent.Spread / 2,
                    mapAngle + spreadEvent.Spread / 2, ammoSpreadComp.Count);

                base.ShootOrThrow(ammoEnt, angles[0].ToVec(), gunVelocity, gun, gunUid, user);
                shotProjectiles.Add(ammoEnt);
                MarkPredicted(ammoEnt);

                for (var i = 1; i < ammoSpreadComp.Count; i++)
                {
                    var newuid = Spawn(ammoSpreadComp.Proto, fromEnt);
                    base.ShootOrThrow(newuid, angles[i].ToVec(), gunVelocity, gun, gunUid, user);
                    shotProjectiles.Add(newuid);
                    MarkPredicted(newuid);
                }
            }
            else
            {
                base.ShootOrThrow(ammoEnt, mapDirection, gunVelocity, gun, gunUid, user);
                shotProjectiles.Add(ammoEnt);
                MarkPredicted(ammoEnt);
            }

            MuzzleFlash(gunUid, ammoComp, mapDirection.ToAngle(), user, user);
            Audio.PlayPredicted(gun.SoundGunshotModified, gunUid, user);
        }

        return shotProjectiles;
    }
    // TODO rework so we dont need to loop through results and only work off a predicate for the raycast
    private bool TryGetHitscanResult(
        MapCoordinates from,
        Vector2 direction,
        HitscanPrototype hitscan,
        EntityUid source,
        EntityUid? extraIgnoredEntity,
        EntityUid? target,
        ICommonSession? session,
        out EntityUid hit,
        out float distance)
    {
        hit = default;
        distance = hitscan.MaxLength;
        var firedFromContainer = _container.IsEntityOrParentInContainer(source);

        var ray = new CollisionRay(from.Position, direction, hitscan.CollisionMask);
        var rayCastResults = Physics.IntersectRayWithPredicate(
            from.MapId,
            ray,
            (Source: source, Extra: extraIgnoredEntity),
            static (hit, ignored) => hit == ignored.Source || hit == ignored.Extra,
            hitscan.MaxLength,
            false).ToList();
        var raycastEvent = new HitScanAfterRayCastEvent(rayCastResults);
        RaiseLocalEvent(source, ref raycastEvent);

        if (raycastEvent.RayCastResults == null)
            return false;

        if (session == null)
            return TryGetFirstValidHitscanResult(raycastEvent.RayCastResults, target, firedFromContainer, out hit, out distance);

        // #Cythisiax Fixed - Revert PR #1103 rider hitscan deferral: shots at a ridden bike hit the
        // bike fixture again (bike takes full damage) instead of being deferred to the rider/passing through.
        EntityUid? firstValidHit = null;
        var firstHitDistance = hitscan.MaxLength;
        foreach (var result in raycastEvent.RayCastResults)
        {
            if (result.HitEntity == source ||
                result.HitEntity == extraIgnoredEntity ||
                !IsValidHitscanTarget(result.HitEntity, target, firedFromContainer))
                continue;

            firstValidHit = result.HitEntity;
            firstHitDistance = result.Distance;
            break;
        }
        /*
        // TODO: rework this, change collisionMask to be physics flag LookUp
        if (TryGetLagCompensatedHitscanResult(
                   from,
                   direction,
                   hitscan.MaxLength,
                   hitscan.CollisionMask,
                   source,
                   extraIgnoredEntity,
                   target,
                   firedFromContainer,
                   session,
                   out var lagCompHit,
                   out var lagCompDistance) && lagCompDistance <= firstHitDistance)
        {
            hit = lagCompHit;
            distance = lagCompDistance;
            return true;
        }
*/
        if (firstValidHit != null)
        {
            hit = firstValidHit.Value;
            distance = firstHitDistance;
            return true;
        }

        return false;
    }

    private bool TryGetLagCompensatedHitscanResult(
        MapCoordinates from,
        Vector2 direction,
        float maxLength,
        int collisionMask,
        EntityUid source,
        EntityUid? extraIgnoredEntity,
        EntityUid? target,
        bool firedFromContainer,
        ICommonSession session,
        out EntityUid hit,
        out float distance)
    {
        hit = default;
        distance = maxLength;

        var end = from.Position + direction * maxLength;
        var searchBounds = Box2.FromTwoPoints(from.Position, end).Enlarged(_lagCompHitscanSearchPadding);
        _lagCompCandidates.Clear();
        _lookup.GetEntitiesIntersecting(from.MapId, searchBounds, _lagCompCandidates, LookupFlags.Dynamic | LookupFlags.Static);

        var found = false;
        foreach (var candidate in _lagCompCandidates)
        {
            if (candidate == source ||
                candidate == extraIgnoredEntity ||
                !TryComp(candidate, out LagCompensationComponent? _) ||
                !TryComp(candidate, out FixturesComponent? fixtures) ||
                !TryComp(candidate, out TransformComponent? xform))
            {
                continue;
            }

            if (!IsValidHitscanTarget(candidate, target, firedFromContainer))
                continue;

            if (!TryGetLagCompensatedBounds(candidate, session, collisionMask, fixtures, xform, out var bounds) ||
                !TryIntersectSegmentBox(from.Position, end, bounds, out var fraction))
            {
                continue;
            }

            var candidateDistance = fraction * maxLength;
            if (candidateDistance > distance)
                continue;

            hit = candidate;
            distance = candidateDistance;
            found = true;
        }

        return found;
    }

    private bool TryGetLagCompensatedBounds(
        EntityUid uid,
        ICommonSession session,
        int collisionMask,
        FixturesComponent fixtures,
        TransformComponent xform,
        out Box2 bounds)
    {
        bounds = default;

        var (coordinates, angle) = _lagCompensation.GetCoordinatesAngle(uid, session, xform);
        if (coordinates == EntityCoordinates.Invalid)
            return false;

        var mapCoordinates = _transform.ToMapCoordinates(coordinates);
        if (mapCoordinates.MapId == MapId.Nullspace)
            return false;

        var worldAngle = _transform.GetWorldRotation(coordinates.EntityId) + angle;
        var transform = new Transform(mapCoordinates.Position, worldAngle);
        var initialized = false;

        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if ((fixture.CollisionLayer & collisionMask) == 0)
                continue;

            for (var i = 0; i < fixture.Shape.ChildCount; i++)
            {
                var aabb = fixture.Shape.ComputeAABB(transform, i);
                bounds = initialized ? bounds.Union(aabb) : aabb;
                initialized = true;
            }
        }

        if (!initialized)
            return false;

        bounds = bounds.Enlarged(_lagCompAabbEnlargement);
        return true;
    }

    protected override void Popup(string message, EntityUid? uid, EntityUid? user) { }

    protected override void CreateEffect(EntityUid gunUid, MuzzleFlashEvent message, EntityUid? user = null, EntityUid? player = null)
    {
        var filter = Filter.Pvs(gunUid, entityManager: EntityManager);

        if (TryComp<ActorComponent>(user, out var actor))
            filter.RemovePlayer(actor.PlayerSession);

        if (GunPrediction && TryComp(player, out actor))
            filter.RemovePlayer(actor.PlayerSession);

        RaiseNetworkEvent(message, filter);
    }

    // TODO: Pseudo RNG so the client can predict these.
    #region Hitscan effects

    private void FireEffects(EntityCoordinates fromCoordinates,
        float distance,
        Angle mapDirection,
        HitscanPrototype hitscan,
        EntityUid? hitEntity = null,
        ICommonSession? shooterSession = null)
    {
        // Lord
        // Forgive me for the shitcode I am about to do
        // Effects tempt me not
        var sprites = new List<(NetCoordinates coordinates, Angle angle, SpriteSpecifier sprite, float scale)>();
        var gridUid = fromCoordinates.GetGridUid(EntityManager);
        var angle = mapDirection;

        // We'll get the effects relative to the grid / map of the firer
        // Look you could probably optimise this a bit with redundant transforms at this point.
        var xformQuery = GetEntityQuery<TransformComponent>();

        if (xformQuery.TryGetComponent(gridUid, out var gridXform))
        {
            var (_, gridRot, gridInvMatrix) = _xform.GetWorldPositionRotationInvMatrix(gridXform, xformQuery);

            fromCoordinates = new EntityCoordinates(gridUid.Value,
                Vector2.Transform(fromCoordinates.ToMapPos(EntityManager, _xform), gridInvMatrix));

            // Use the fallback angle I guess?
            angle -= gridRot;
        }

        if (distance >= 1f)
        {
            if (hitscan.MuzzleFlash != null)
            {
                var coords = fromCoordinates.Offset(angle.ToVec().Normalized() / 2);
                var netCoords = GetNetCoordinates(coords);

                sprites.Add((netCoords, angle, hitscan.MuzzleFlash, 1f));
            }

            if (hitscan.TravelFlash != null)
            {
                var coords = fromCoordinates.Offset(angle.ToVec() * (distance + 0.5f) / 2);
                var netCoords = GetNetCoordinates(coords);

                sprites.Add((netCoords, angle, hitscan.TravelFlash, distance - 1.5f));
            }
        }

        if (hitscan.ImpactFlash != null)
        {
            var coords = fromCoordinates.Offset(angle.ToVec() * distance);
            var netCoords = GetNetCoordinates(coords);

            sprites.Add((netCoords, angle.FlipPositive(), hitscan.ImpactFlash, 1f));
        }

        if (sprites.Count > 0)
        {
            var filter = Filter.Pvs(fromCoordinates, entityMan: EntityManager);
            if (shooterSession != null)
                filter.RemovePlayer(shooterSession);

            RaiseNetworkEvent(new HitscanEvent
            {
                Sprites = sprites,
                TintColor = hitscan.TintColor, // #Misfits Add: forward beam customisation
                BeamWidth = hitscan.BeamWidth,
                BeamDuration = hitscan.BeamDuration,
            }, filter);
        }
    }

    #endregion
}
