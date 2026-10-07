using System.Numerics;
using Content.Client._Misfits.Movement; // #Misfits Add
using Content.Shared._Misfits.Weapons.Ranged.Prediction;
using Content.Client.Animations;
using Content.Client.Gameplay;
using Content.Client.Items;
using Content.Client.Weapons.Ranged.Components;
using Content.Shared.Camera;
using Content.Shared.CombatMode;
using Content.Shared._Misfits.CCVar;
using Content.Shared.Damage;
using Content.Shared.Effects;
using Content.Shared.Mech.Components; // Goobstation
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Weapons.Reflect;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Physics;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Shared.Animations;
using Robust.Shared.Configuration;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using SharedGunSystem = Content.Shared.Weapons.Ranged.Systems.SharedGunSystem;
using TimedDespawnComponent = Robust.Shared.Spawners.TimedDespawnComponent;

namespace Content.Client.Weapons.Ranged.Systems;

public sealed partial class GunSystem : SharedGunSystem
{
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private IInputManager _inputManager = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IStateManager _state = default!;
    [Dependency] private AnimationPlayerSystem _animPlayer = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private InputSystem _inputSystem = default!;
    [Dependency] private SharedColorFlashEffectSystem _color = default!;
    [Dependency] private SharedCameraRecoilSystem _recoil = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private PhysicsSystem _physics = default!;
    [Dependency] private MisfitsLagCompensationSystem _lagComp = default!; // #Misfits Add — lag compensation tick stamp
    [Dependency] private ILogManager _logMan = default!;
    private readonly HashSet<EntityUid> _lagCompCandidates = [];
    private float _lagCompAabbEnlargement;
    private float _lagCompHitscanSearchPadding;
    private EntityQuery<SpriteComponent> _spriteQuery;


    [ValidatePrototypeId<EntityPrototype>]
    public const string HitscanProto = "HitscanEffect";

    public bool SpreadOverlay
    {
        get => _spreadOverlay;
        set
        {
            if (_spreadOverlay == value)
                return;

            _spreadOverlay = value;
            var overlayManager = IoCManager.Resolve<IOverlayManager>();

            if (_spreadOverlay)
            {
                overlayManager.AddOverlay(new GunSpreadOverlay(
                    EntityManager,
                    _eyeManager,
                    Timing,
                    _inputManager,
                    _player,
                    this,
                    _xform));
            }
            else
            {
                overlayManager.RemoveOverlay<GunSpreadOverlay>();
            }
        }
    }

    private bool _spreadOverlay;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesOutsidePrediction = true;
        SubscribeLocalEvent<AmmoCounterComponent, ItemStatusCollectMessage>(OnAmmoCounterCollect);
        SubscribeLocalEvent<AmmoCounterComponent, UpdateClientAmmoEvent>(OnUpdateClientAmmo);
        SubscribeAllEvent<MuzzleFlashEvent>(OnMuzzleFlash);

        // Plays animated effects on the client.
        SubscribeNetworkEvent<HitscanEvent>(OnHitscan);
        Subs.CVar(_config, PerformanceCVars.GunPredictionAabbEnlargement, v => _lagCompAabbEnlargement = v, true);
        Subs.CVar(_config, PerformanceCVars.GunPredictionHitscanSearchPadding, v => _lagCompHitscanSearchPadding = v, true);

        InitializeMagazineVisuals();
        InitializeSpentAmmo();

        // Misfit add: refactoring obsolete sprite methods
        _spriteQuery = GetEntityQuery<SpriteComponent>();

    }

    private void OnUpdateClientAmmo(EntityUid uid, AmmoCounterComponent ammoComp, ref UpdateClientAmmoEvent args)
    {
        UpdateAmmoCount(uid, ammoComp);
    }

    private void OnMuzzleFlash(MuzzleFlashEvent args)
    {
        var gunUid = GetEntity(args.Uid);

        CreateEffect(gunUid, args, gunUid, _player.LocalEntity);
    }

    private void OnHitscan(HitscanEvent ev)
    {
        foreach (var a in ev.Sprites)
        {
            var coords = GetCoordinates(a.coordinates);
            SpawnHitscanEffect(coords, a.angle, a.Sprite, a.Distance, ev.TintColor, ev.BeamWidth, ev.BeamDuration);
        }
    }

    public override void Update(float frameTime)
    {
        if (!Timing.IsFirstTimePredicted)
            return;

        var entityNull = _player.LocalEntity;

        if (entityNull == null || !TryComp<CombatModeComponent>(entityNull, out var combat) || !combat.IsInCombatMode)
        {
            return;
        }

        var entity = entityNull.Value;

        if (!TryGetGun(entity, out var gunUid, out var gun))
        {
            return;
        }

        var useKey = gun.UseKey ? EngineKeyFunctions.Use : EngineKeyFunctions.UseSecondary;

        if (_inputSystem.CmdStates.GetState(useKey) != BoundKeyState.Down && !gun.BurstActivated)
        {
            if (gun.ShotCounter != 0)
                EntityManager.RaisePredictiveEvent(new RequestStopShootEvent { Gun = GetNetEntity(gunUid) });
            return;
        }

        if (gun.NextFire > Timing.CurTime)
            return;

        var mousePos = _eyeManager.PixelToMap(_inputManager.MouseScreenPosition);

        if (mousePos.MapId == MapId.Nullspace)
        {
            if (gun.ShotCounter != 0)
                EntityManager.RaisePredictiveEvent(new RequestStopShootEvent { Gun = GetNetEntity(gunUid) });

            return;
        }
        //if(Inputting really fast ignore)
        // Define target coordinates relative to gun entity, so that network latency on moving grids doesn't fuck up the target location.
        var coordinates = _xform.ToCoordinates(entity, mousePos);

        NetEntity? target = null;
        if (_state.CurrentState is GameplayStateBase screen)
            target = GetNetEntity(screen.GetClickedEntity(mousePos));

        Log.Debug($"Sending shoot request tick {Timing.CurTick} / {Timing.CurTime}");

        if (_player.LocalSession is not { } session)
            return;

        var projectiles = ShootRequested(GetNetEntity(gunUid), GetNetCoordinates(coordinates), target, null, session);

        EntityManager.RaisePredictiveEvent(new RequestShootEvent
        {
            Target = target,
            Coordinates = GetNetCoordinates(coordinates),
            Gun = GetNetEntity(gunUid),
            Shot = projectiles?.Select(p => p.Id).ToList(),
            LastRealTick = _lagComp.GetLastRealTick(), // #Misfits Add
        });
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
        //
        if (!GunPrediction)
        {
            // Rather than splitting client / server for every ammo provider it's easier
            // to just delete the spawned entities. This is for programmer sanity despite the wasted perf.

            //Misfit: bad^^^^^^^^^^^ really bad^^^^
            var direction = _xform.ToMapCoordinates(fromCoordinates).Position - _xform.ToMapCoordinates(toCoordinates).Position;
            var worldAngle = direction.ToAngle().Opposite();

            foreach (var (ent, shootable) in ammo)
            {
                if (throwItems)
                {
                    Recoil(user, direction, gun.CameraRecoilScalarModified);
                    if (IsClientSide(ent!.Value))
                        Del(ent.Value);
                    else
                        RemoveShootable(ent.Value);
                    continue;
                }
                // TODO: use ishootable like an actual interface
                switch (shootable)
                {
                    case CartridgeAmmoComponent cartridge:
                        if (!cartridge.Spent)
                        {
                            SetCartridgeSpent(ent!.Value, cartridge, true);
                            MuzzleFlash(gunUid, cartridge, worldAngle, user, _player.LocalEntity);
                            Audio.PlayPredicted(gun.SoundGunshotModified, gunUid, user);
                            Recoil(user, direction, gun.CameraRecoilScalarModified);
                        }
                        else
                        {
                            userImpulse = false;
                            Audio.PlayPredicted(gun.SoundEmpty, gunUid, user);
                        }

                        if (IsClientSide(ent!.Value))
                            Del(ent.Value);

                        break;
                    case AmmoComponent newAmmo:
                        MuzzleFlash(gunUid, newAmmo, worldAngle, user, _player.LocalEntity);
                        Audio.PlayPredicted(gun.SoundGunshotModified, gunUid, user);
                        Recoil(user, direction, gun.CameraRecoilScalarModified);
                        if (IsClientSide(ent!.Value))
                            Del(ent.Value);
                        else
                            RemoveShootable(ent.Value);
                        break;
                    case HitscanPrototype:
                        PredictHitscan(gunUid, fromCoordinates, worldAngle.ToVec(), worldAngle, gun.Target, user);
                        Audio.PlayPredicted(gun.SoundGunshotModified, gunUid, user);
                        Recoil(user, direction, gun.CameraRecoilScalarModified);
                        break;
                }
            }

            return null;
        }

        var fromMap = fromCoordinates.ToMap(EntityManager, _xform);
        var toMap = toCoordinates.ToMapPos(EntityManager, _xform);
        var mapDirection = toMap - fromMap.Position;
        var mapAngle = mapDirection.ToAngle();
        var angle = GetRecoilAngle(Timing.CurTime, gun, mapDirection.ToAngle(), user);
        var fromEnt = MapManager.TryFindGridAt(fromMap, out var gridUid, out _)
            ? fromCoordinates.WithEntityId(gridUid, EntityManager)
            : new EntityCoordinates(MapManager.GetMap(fromMap.MapId), fromMap.Position);

        toMap = fromMap.Position + angle.ToVec() * mapDirection.Length();
        mapDirection = toMap - fromMap.Position;
        var gunVelocity = Physics.GetMapLinearVelocity(fromEnt);
        var shotProjectiles = new List<EntityUid>(ammo.Count);

        void TrackProjectile(EntityUid uid)
        {
            EnsureComp<PredictedProjectileClientComponent>(uid);
            _physics.UpdateIsPredicted(uid);
            shotProjectiles.Add(uid);
        }

        void CreateAndFireProjectiles(EntityUid ammoEnt, AmmoComponent ammoComp)
        {
            if (TryComp<ProjectileSpreadComponent>(ammoEnt, out var ammoSpreadComp))
            {
                var spreadEvent = new GunGetAmmoSpreadEvent(ammoSpreadComp.Spread);
                RaiseLocalEvent(gunUid, ref spreadEvent);

                var angles = LinearSpread(mapAngle - spreadEvent.Spread / 2,
                    mapAngle + spreadEvent.Spread / 2, ammoSpreadComp.Count);

                TrackProjectile(ammoEnt);
                ShootOrThrow(ammoEnt, angles[0].ToVec(), gunVelocity, gun, gunUid, user);

                for (var i = 1; i < ammoSpreadComp.Count; i++)
                {
                    var newUid = Spawn(ammoSpreadComp.Proto, fromEnt);
                    TrackProjectile(newUid);
                    ShootOrThrow(newUid, angles[i].ToVec(), gunVelocity, gun, gunUid, user);
                }
            }
            else
            {
                TrackProjectile(ammoEnt);
                ShootOrThrow(ammoEnt, mapDirection, gunVelocity, gun, gunUid, user);
            }

            MuzzleFlash(gunUid, ammoComp, mapDirection.ToAngle(), user, _player.LocalEntity);
            Audio.PlayPredicted(gun.SoundGunshotModified, gunUid, user);
        }

        foreach (var (ent, shootable) in ammo)
        {
            if (throwItems)
            {
                Recoil(user, mapDirection, gun.CameraRecoilScalarModified);
                if (IsClientSide(ent!.Value))
                    Del(ent.Value);
                else
                    RemoveShootable(ent.Value);
                continue;
            }
            // TODO: use ishootable like an actual interface
            switch (shootable)
            {
                case CartridgeAmmoComponent cartridge:
                    if (!cartridge.Spent)
                    {
                        var uid = Spawn(cartridge.Prototype, fromEnt);
                        CreateAndFireProjectiles(uid, cartridge);
                        SetCartridgeSpent(ent!.Value, cartridge, true);
                    }
                    else
                    {
                        userImpulse = false;
                        Audio.PlayPredicted(gun.SoundEmpty, gunUid, user);
                    }

                    Recoil(user, mapDirection, gun.CameraRecoilScalarModified);

                    if (!cartridge.DeleteOnSpawn && !Containers.IsEntityInContainer(ent!.Value))
                        EjectCartridge(ent.Value, baseCoords: Transform(gunUid).Coordinates, angle);
                    // misfit: removed ejected carts deleted by EjectCartridge
                    //         plus redundant check this is only called by client
                    //if (IsClientSide(ent!.Value))
                    //    Del(ent.Value);

                    break;
                case AmmoComponent newAmmo:
                    CreateAndFireProjectiles(ent!.Value, newAmmo);
                    Recoil(user, mapDirection, gun.CameraRecoilScalarModified);
                    if (!IsClientSide(ent!.Value))
                        RemoveShootable(ent.Value);
                    break;
                case HitscanPrototype:
                    PredictHitscan(gunUid, fromCoordinates, mapDirection, mapDirection.ToAngle(), gun.Target, user);
                    Audio.PlayPredicted(gun.SoundGunshotModified, gunUid, user);
                    Recoil(user, mapDirection, gun.CameraRecoilScalarModified);
                    break;
            }
        }

        return shotProjectiles;
    }

    private void Recoil(EntityUid? user, Vector2 recoil, float recoilScalar)
    {
        if (!Timing.IsFirstTimePredicted || user == null || recoil == Vector2.Zero || recoilScalar == 0)
            return;

        _recoil.KickCamera(user.Value, recoil.Normalized() * 0.5f * recoilScalar);
    }

    protected override void Popup(string message, EntityUid? uid, EntityUid? user)
    {
        if (uid == null || user == null || !Timing.IsFirstTimePredicted)
            return;

        _popup.PopupEntity(message, uid.Value, user.Value);
    }

    protected override void CreateEffect(EntityUid gunUid, MuzzleFlashEvent message, EntityUid? tracked = null, EntityUid? player = null)
    {
        if (!Timing.IsFirstTimePredicted)
            return;

        // EntityUid check added to stop throwing exceptions due to https://github.com/space-wizards/space-station-14/issues/28252
        // TODO: Check to see why invalid entities are firing effects.
        if (gunUid == EntityUid.Invalid)
        {
            Log.Debug($"Invalid Entity sent MuzzleFlashEvent (proto: {message.Prototype}, gun: {ToPrettyString(gunUid)})");
            return;
        }

        var gunXform = Transform(gunUid);
        var gridUid = gunXform.GridUid;
        EntityCoordinates coordinates;

        if (TryComp(gridUid, out MapGridComponent? mapGrid))
        {
            coordinates = new EntityCoordinates(gridUid.Value, _maps.LocalToGrid(gridUid.Value, mapGrid, gunXform.Coordinates));
        }
        else if (gunXform.MapUid != null)
        {
            coordinates = new EntityCoordinates(gunXform.MapUid.Value, _xform.GetWorldPosition(gunXform));
        }
        else
        {
            return;
        }

        var ent = Spawn(message.Prototype, coordinates);
        _xform.SetWorldRotationNoLerp(ent, message.Angle);

        if (tracked != null)
        {
            var track = EnsureComp<TrackUserComponent>(ent);
            track.User = tracked;
            track.Offset = Vector2.UnitX / 2f;
        }

        var lifetime = 0.4f;

        if (TryComp<TimedDespawnComponent>(gunUid, out var despawn))
        {
            lifetime = despawn.Lifetime;
        }

        var anim = new Animation()
        {
            Length = TimeSpan.FromSeconds(lifetime),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Color),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(Color.White.WithAlpha(1f), 0),
                        new AnimationTrackProperty.KeyFrame(Color.White.WithAlpha(0f), lifetime)
                    }
                }
            }
        };

        _animPlayer.Play(ent, anim, "muzzle-flash");
        if (!TryComp(gunUid, out PointLightComponent? light))
        {
            light = (PointLightComponent) _factory.GetComponent(typeof(PointLightComponent));
            light.NetSyncEnabled = false;
            AddComp(gunUid, light);
        }

        Lights.SetEnabled(gunUid, true, light);
        Lights.SetRadius(gunUid, 2f, light);
        Lights.SetColor(gunUid, Color.FromHex("#cc8e2b"), light);
        Lights.SetEnergy(gunUid, 5f, light);

        var animTwo = new Animation()
        {
            Length = TimeSpan.FromSeconds(lifetime),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(PointLightComponent),
                    Property = nameof(PointLightComponent.Energy),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(5f, 0),
                        new AnimationTrackProperty.KeyFrame(0f, lifetime)
                    }
                },
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(PointLightComponent),
                    Property = nameof(PointLightComponent.AnimatedEnable),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(true, 0),
                        new AnimationTrackProperty.KeyFrame(false, lifetime)
                    }
                }
            }
        };

        var uidPlayer = EnsureComp<AnimationPlayerComponent>(gunUid);

        _animPlayer.Stop(gunUid, uidPlayer, "muzzle-flash-light");
        _animPlayer.Play((gunUid, uidPlayer), animTwo, "muzzle-flash-light");
    }

    public override void ShootProjectile(EntityUid uid,
        Vector2 direction,
        Vector2 gunVelocity,
        EntityUid gunUid,
        EntityUid? user = null,
        float speed = 20f)
    {
        EnsureComp<PredictedProjectileClientComponent>(uid);
        _physics.UpdateIsPredicted(uid);
        base.ShootProjectile(uid, direction, gunVelocity, gunUid, user, speed);
    }
    // TODO misfits: for now we dont try predicting visuals of deflections until refactor can be done
    private void PredictHitscan(EntityUid gunUid,
        EntityCoordinates fromCoordinates,
        Vector2 direction,
        Angle worldAngle,
        EntityUid? target,
        EntityUid? user)
    {
        if (!Timing.IsFirstTimePredicted)
            return;

        if (!IsLocalShooter(user) || !TryResolveGunHitscan(gunUid, out var hitscan))
            return;

        var fromMap = fromCoordinates.ToMap(EntityManager, _xform);
        if (fromMap.MapId == MapId.Nullspace || direction.LengthSquared() <= 0.0001f)
            return;

        var normalizedDirection = direction.Normalized();
        var from = fromMap;
        // [Changed by MisfitsCrew/Operator] Use grid/map effect coordinates so predicted beam
        // sprites are not parented to bikes or other ridden entities.
        var fromEffect = GetShotEffectCoordinates(fromMap);
        // [Changed by MisfitsCrew/Operator] Keep the rider as the shot source and ignore the
        // mounted vehicle separately so predicted lasers do not collide with either one.
        var ignoredEntity = GetShotExtraIgnoredEntity(user);
        var source = user ?? gunUid;
        var historicalTick = GetPredictedHitscanTick();
        //var reflectAttempts = hitscan.Reflective != ReflectType.None ? 3 : 1;

        // TODO misfits: for now we dont try predicting visuals of deflections until refactor can be done
        TryGetPredictedHitscanResult(
                    from,
                    normalizedDirection,
                    hitscan,
                    source,
                    ignoredEntity,
                    target,
                    historicalTick,
                    out var hitEntity,
                    out var distance);

        SpawnPredictedHitscanEffects(fromEffect, worldAngle, normalizedDirection, hitscan, distance);
        if (hitEntity == EntityUid.Invalid && HasComp<DamageableComponent>(hitEntity) && !HasComp<ActorComponent>(hitEntity))
            _color.RaiseEffect(Color.Red, new List<EntityUid> { hitEntity }, Filter.Local());

        // original code for client side predicted deflect visuals. Didnt sync with actual server hitscans on deflect due to rng desync
        /*
                for (var reflectAttempt = 0; reflectAttempt < reflectAttempts; reflectAttempt++)
                {

                    if (!TryGetPredictedHitscanResult(
                            from,
                            normalizedDirection,
                            hitscan,
                            source,
                            ignoredEntity,
                            target,
                            historicalTick,
                            out var hit,
                            out var distance))
                    {

                        if (reflectAttempt == 0)
                            SpawnPredictedHitscanEffects(fromEffect, worldAngle, normalizedDirection, hitscan, hitscan.MaxLength);
                        break;
                    }

                    hitEntity = hit;
                    SpawnPredictedHitscanEffects(fromEffect, worldAngle, normalizedDirection, hitscan, distance);

                    if (hitscan.Reflective == ReflectType.None)
                        break;

                    var reflectEv = new HitScanReflectAttemptEvent(user, gunUid, hitscan.Reflective, normalizedDirection, false);
                    RaiseLocalEvent(hit, ref reflectEv);

                    if (!reflectEv.Reflected || reflectEv.Direction.LengthSquared() <= 0.0001f)
                        break;

                    fromEffect = GetShotEffectCoordinates(Transform(hit).Coordinates.ToMap(EntityManager, _xform));
                    from = fromEffect.ToMap(EntityManager, _xform);
                    normalizedDirection = reflectEv.Direction.Normalized();
                    worldAngle = normalizedDirection.ToAngle();
                }



                if (hitEntity is not null && HasComp<DamageableComponent>(hitEntity) && !HasComp<ActorComponent>(hitEntity))
                    _color.RaiseEffect(Color.Red, new List<EntityUid> { hitEntity.Value }, Filter.Local());
        */

    }

    private GameTick GetPredictedHitscanTick()
    {
        var tick = _lagComp.GetLastRealTick();
        return tick > GameTick.Zero ? tick - 1 : tick;
    }

    private bool TryGetPredictedHitscanResult(
        MapCoordinates from,
        Vector2 direction,
        HitscanPrototype hitscan,
        EntityUid source,
        EntityUid? ignoredEntity,
        EntityUid? target,
        GameTick historicalTick,
        out EntityUid hit,
        out float distance)
    {
        hit = EntityUid.Invalid;
        distance = hitscan.MaxLength;

        var ray = new CollisionRay(from.Position, direction, hitscan.CollisionMask);
        var rayCastResults = Physics.IntersectRayWithPredicate(
            from.MapId,
            ray,
            (Source: source, Extra: ignoredEntity),
            static (hit, ignored) => hit == ignored.Source || hit == ignored.Extra,
            hitscan.MaxLength,
            false).ToList();
        var firedFromContainer = Containers.IsEntityOrParentInContainer(source);

        // #Cythisiax Fixed - Revert PR #1103 rider hitscan deferral: shots at a ridden bike hit the
        // bike fixture again (bike takes full damage) instead of being deferred to the rider/passing through.

        //EntityUid? currentDynamicHit = null;
        var staticDistance = hitscan.MaxLength;
        var currentDynamicDistance = hitscan.MaxLength;

        foreach (var result in rayCastResults)
        {
            if (result.HitEntity == source ||
                result.HitEntity == ignoredEntity ||
                !IsValidHitscanTarget(result.HitEntity, target, firedFromContainer))
                continue;
            /*
            if (TryComp<PhysicsComponent>(result.HitEntity, out var resultPhysics) &&
                resultPhysics.BodyType != BodyType.Static)
            {
                currentDynamicHit ??= result.HitEntity;
                currentDynamicDistance = MathF.Min(currentDynamicDistance, result.Distance);
                continue;
            }
*/
            hit = result.HitEntity;
            distance = result.Distance;
            return true;
        }
        /*
                if (TryGetHistoricalHitscanResult(
                        from,
                        direction,
                        hitscan.MaxLength,
                        hitscan.CollisionMask,
                        source,
                        ignoredEntity,
                        target,
                        firedFromContainer,
                        historicalTick,
                        out var historicalHit,
                        out var historicalDistance) &&
                    historicalDistance <= staticDistance)
                {
                    hit = historicalHit;
                    distance = historicalDistance;
                    return true;
                }
        */

        /*
                if (currentDynamicHit != null)
                {
                    hit = currentDynamicHit.Value;
                    distance = currentDynamicDistance;
                    return true;
                }
        */
        return false;
    }

    private bool TryGetHistoricalHitscanResult(
        MapCoordinates from,
        Vector2 direction,
        float maxLength,
        int collisionMask,
        EntityUid source,
        EntityUid? ignoredEntity,
        EntityUid? target,
        bool firedFromContainer,
        GameTick historicalTick,
        out EntityUid hit,
        out float distance)
    {
        hit = default;
        distance = maxLength;

        var end = from.Position + direction * maxLength;
        var searchBounds = Box2.FromTwoPoints(from.Position, end).Enlarged(_lagCompHitscanSearchPadding);
        _lagCompCandidates.Clear();
        _lookup.GetEntitiesIntersecting(from.MapId, searchBounds, _lagCompCandidates, LookupFlags.Dynamic);

        var found = false;
        foreach (var candidate in _lagCompCandidates)
        {
            if (candidate == source ||
                candidate == ignoredEntity ||
                !TryComp(candidate, out FixturesComponent? fixtures) ||
                !TryComp(candidate, out TransformComponent? xform))
            {
                continue;
            }

            // #Cythisiax Fixed - Revert PR #1103: don't skip strap/bike entities in lag-comp hitscan;
            // bikes are valid hitscan targets and take full damage again.
            if (!IsValidHitscanTarget(candidate, target, firedFromContainer) ||
                !TryGetHistoricalHitscanBounds(candidate, historicalTick, collisionMask, fixtures, xform, out var bounds) ||
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

    private bool TryGetHistoricalHitscanBounds(
        EntityUid uid,
        GameTick historicalTick,
        int collisionMask,
        FixturesComponent fixtures,
        TransformComponent xform,
        out Box2 bounds)
    {
        bounds = default;

        var (coordinates, angle) = _lagComp.GetCoordinatesAngle(uid, historicalTick, xform);
        if (coordinates == EntityCoordinates.Invalid)
            return false;

        var mapCoordinates = _xform.ToMapCoordinates(coordinates);
        if (mapCoordinates.MapId == MapId.Nullspace)
            return false;

        var worldAngle = _xform.GetWorldRotation(coordinates.EntityId) + angle;
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

    private void SpawnPredictedHitscanEffects(
        EntityCoordinates fromCoordinates,
        Angle worldAngle,
        Vector2 normalizedDirection,
        HitscanPrototype hitscan,
        float distance)
    {
        if (distance >= 1f)
        {
            if (hitscan.MuzzleFlash != null)
            {
                var coords = fromCoordinates.Offset(normalizedDirection / 2f);
                SpawnHitscanEffect(coords, worldAngle, hitscan.MuzzleFlash, 1f, hitscan.TintColor, hitscan.BeamWidth, hitscan.BeamDuration);
            }

            if (hitscan.TravelFlash != null)
            {
                var coords = fromCoordinates.Offset(normalizedDirection * (distance + 0.5f) / 2f);
                SpawnHitscanEffect(coords, worldAngle, hitscan.TravelFlash, distance - 1.5f, hitscan.TintColor, hitscan.BeamWidth, hitscan.BeamDuration);
            }
        }

        if (hitscan.ImpactFlash != null)
        {
            var coords = fromCoordinates.Offset(normalizedDirection * distance);
            SpawnHitscanEffect(coords, worldAngle.FlipPositive(), hitscan.ImpactFlash, 1f, hitscan.TintColor, hitscan.BeamWidth, hitscan.BeamDuration);
        }
    }

    private bool IsLocalShooter(EntityUid? user)
    {
        if (_player.LocalEntity is not { } local || user == null)
            return false;

        if (user == local)
            return true;

        return TryComp<MechPilotComponent>(local, out var mechPilot) && mechPilot.Mech == user;
    }

    private void SpawnHitscanEffect(EntityCoordinates coords,
        Angle angle,
        SpriteSpecifier spriteSpecifier,
        float distance,
        Color? tintColor,
        float beamWidth,
        float beamDuration)
    {
        if (spriteSpecifier is not SpriteSpecifier.Rsi rsi || Deleted(coords.EntityId))
            return;

        var ent = Spawn(HitscanProto, coords);
        var sprite = Comp<SpriteComponent>(ent);
        var xform = Transform(ent);
        xform.LocalRotation = angle;
        sprite[EffectLayers.Unshaded].AutoAnimated = false;
        sprite.LayerSetSprite(EffectLayers.Unshaded, rsi);
        sprite.LayerSetState(EffectLayers.Unshaded, rsi.RsiState);
        sprite.Scale = new Vector2(distance, beamWidth);
        sprite[EffectLayers.Unshaded].Visible = true;

        if (tintColor != null)
            sprite.LayerSetColor(EffectLayers.Unshaded, tintColor.Value);

        if (beamDuration > 2f && TryComp<TimedDespawnComponent>(ent, out var despawn))
            despawn.Lifetime = beamDuration + 0.5f;

        var anim = new Animation()
        {
            Length = TimeSpan.FromSeconds(beamDuration),
            AnimationTracks =
            {
                new AnimationTrackSpriteFlick()
                {
                    LayerKey = EffectLayers.Unshaded,
                    KeyFrames =
                    {
                        new AnimationTrackSpriteFlick.KeyFrame(rsi.RsiState, 0f),
                    }
                }
            }
        };

        _animPlayer.Play(ent, anim, "hitscan-effect");
    }
}
