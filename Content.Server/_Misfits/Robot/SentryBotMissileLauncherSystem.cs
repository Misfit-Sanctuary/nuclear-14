// Handles the missile launch WorldTargetAction for player-controlled Sentry Bot chassis.
// When activated, the system enters a targeting phase: an area-wide emote announces
// "TARGETING", then after a configurable delay the missile fires and a "MISSILE LAUNCHED"
// emote broadcasts. Nearby entities see a "MISSILE LOCK DETECTED" warning popup.

using Content.Server.Chat.Systems;
using Content.Shared._Misfits.Robot;
using Content.Shared.Actions;
using Content.Shared.Chat;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Robot;

public sealed class SentryBotMissileLauncherSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly ChatSystem _chat = default!;

    private const string MissilePrototype = "N14ProjectileMissile";
    private const float WarningRange = 6f;
    private const float MissileSpeed = 12f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SentryBotChassisComponent, ComponentInit>(OnChassisInit);
        SubscribeLocalEvent<SentryBotChassisComponent, SentryBotMissileLaunchEvent>(OnMissileLaunch);
    }

    private void OnChassisInit(EntityUid uid, SentryBotChassisComponent comp, ComponentInit args)
    {
        _actions.AddAction(uid, ref comp.MissileLaunchActionEntity, "ActionSentryBotMissileLaunch");
    }

    private void OnMissileLaunch(EntityUid uid, SentryBotChassisComponent comp, SentryBotMissileLaunchEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        // Already targeting — ignore duplicate clicks.
        if (comp.IsTargeting)
            return;

        // Enter targeting phase — store the target and start the delay.
        comp.IsTargeting = true;
        comp.TargetCoords = args.Target;
        comp.MissileLaunchTime = _timing.CurTime + TimeSpan.FromSeconds(comp.TargetingDelay);

        // Area-wide emote: announce targeting lock-on.
        _chat.TrySendInGameICMessage(
            uid,
            Loc.GetString("sentrybot-targeting-emote"),
            InGameICChatType.Emote,
            ChatTransmitRange.Normal,
            ignoreActionBlocker: true);

        // Warn entities near the target location.
        var nearbyEntities = _lookup.GetEntitiesInRange(args.Target, WarningRange);
        foreach (var nearby in nearbyEntities)
        {
            if (nearby == uid)
                continue;

            _popup.PopupEntity(
                Loc.GetString("sentrybot-missile-lock-warning"),
                nearby,
                nearby,
                PopupType.LargeCaution);
        }
    }

    // #Misfits Tweak - Gate missile launch polling to 0.5 Hz; the targeting delay is
    // seconds-scale and uses IGameTiming.CurTime so no drift occurs.
    private float _missileAccumulator;
    private const float MissileUpdateInterval = 0.5f;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _missileAccumulator += frameTime;
        if (_missileAccumulator < MissileUpdateInterval)
            return;
        _missileAccumulator -= MissileUpdateInterval;

        var query = EntityQueryEnumerator<SentryBotChassisComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.IsTargeting)
                continue;

            if (_timing.CurTime < comp.MissileLaunchTime)
                continue;

            // Targeting delay has elapsed — fire the missile.
            comp.IsTargeting = false;

            var xform = Transform(uid);
            var fromCoords = xform.Coordinates;

            // Area-wide emote: announce missile launch.
            _chat.TrySendInGameICMessage(
                uid,
                Loc.GetString("sentrybot-missile-launched-emote"),
                InGameICChatType.Emote,
                ChatTransmitRange.Normal,
                ignoreActionBlocker: true);

            // Spawn the missile projectile and fire toward the stored target.
            var fromMap = fromCoords.ToMap(EntityManager, _transform);
            var spawnCoords = _mapManager.TryFindGridAt(fromMap, out var gridUid, out _)
                ? fromCoords.WithEntityId(gridUid, EntityManager)
                : new EntityCoordinates(_mapManager.GetMapEntityId(fromMap.MapId), fromMap.Position);

            var missile = Spawn(MissilePrototype, spawnCoords);
            var userVelocity = _physics.GetMapLinearVelocity(uid);
            var direction = comp.TargetCoords.ToMapPos(EntityManager, _transform)
                          - spawnCoords.ToMapPos(EntityManager, _transform);

            _gun.ShootProjectile(missile, direction, userVelocity, uid, uid, MissileSpeed);
        }
    }
}
