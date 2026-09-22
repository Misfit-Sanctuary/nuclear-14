using System.Diagnostics.CodeAnalysis;
using Content.Shared.Administration.Logs;
using Content.Shared.Audio;
using Content.Shared.Database;
using Content.Shared.Hands;
using Content.Shared.IdentityManagement; // #Misfits Change Add: for identity-aware names in ricochet popup
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Upload;

namespace Content.Shared.Weapons.Reflect;

/// <summary>
/// This handles reflecting projectiles and hitscan shots.
/// </summary>
public sealed partial class ReflectSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private ItemToggleSystem _toggle = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private InventorySystem _inventorySystem = default!;
    // misfits: fix stuff deflecting from inside inventory.
    private static SlotFlags _deflectSlots = SlotFlags.OUTERCLOTHING | SlotFlags.INNERCLOTHING | SlotFlags.SUITSTORAGE | SlotFlags.BACK;
    public override void Initialize()
    {
        base.Initialize();

        // TODO MISFITS: temp until can refactor gun code. Need psuedo rng to have client and server visuals align
        // client only visual predicts non-reflected shots/lasers now. Otherwise we will have invisible projectiles from server that dont match client visual
        if (_netManager.IsServer)
        {
            SubscribeLocalEvent<ReflectComponent, ProjectileDeflectAttemptEvent>(OnReflectCollide);
            SubscribeLocalEvent<ReflectUserComponent, ProjectileDeflectAttemptEvent>(OnReflectUserCollide);
        }

        SubscribeLocalEvent<ReflectComponent, HitScanReflectAttemptEvent>(OnReflectHitscan);
        SubscribeLocalEvent<ReflectComponent, GotEquippedEvent>(OnReflectEquipped);
        SubscribeLocalEvent<ReflectComponent, GotUnequippedEvent>(OnReflectUnequipped);
        SubscribeLocalEvent<ReflectComponent, GotEquippedHandEvent>(OnReflectHandEquipped);
        SubscribeLocalEvent<ReflectComponent, GotUnequippedHandEvent>(OnReflectHandUnequipped);
        SubscribeLocalEvent<ReflectComponent, ItemToggledEvent>(OnToggleReflect);


        SubscribeLocalEvent<ReflectUserComponent, HitScanReflectAttemptEvent>(OnReflectUserHitscan);
    }

    private void OnReflectUserHitscan(EntityUid uid, ReflectUserComponent component, ref HitScanReflectAttemptEvent args)
    {
        if (args.Reflected)
            return;

        foreach (var ent in _inventorySystem.GetHandOrInventoryEntities(uid, _deflectSlots))
        {

            if (!TryComp<ReflectComponent>(ent, out var reflectComp) ||
                (reflectComp.Reflects & args.Reflective) == 0x0 ||
                !TryReflectHitscan(uid, ent, args.Shooter, args.SourceItem, args.Direction, out var dir))
                continue;

            args.Direction = dir.Value;
            args.Reflected = true;
            break;
        }
    }

    private void OnReflectUserCollide(EntityUid uid, ReflectUserComponent component, ref ProjectileDeflectAttemptEvent args)
    {
        foreach (var ent in _inventorySystem.GetHandOrInventoryEntities(uid, _deflectSlots))
        {
            if (!TryReflectProjectile(uid, ent, args.ProjUid))
                continue;

            args.Deflected = true;
            break;
        }
    }
    //TODO MISFITS:refactor. Client not subscribed to anymore Need psuedo rng to have client and server visuals align
    private void OnReflectCollide(EntityUid uid, ReflectComponent component, ref ProjectileDeflectAttemptEvent args)
    {
        if (args.Deflected)
            return;

        if (TryReflectProjectile(uid, uid, args.ProjUid, reflect: component))
            args.Deflected = true;
    }
    //TODO MISFITS:refactor. Client not subscribed to anymore Need psuedo rng to have client and server visuals align
    private bool TryReflectProjectile(EntityUid user, EntityUid reflector, EntityUid projectile, ProjectileComponent? projectileComp = null, ReflectComponent? reflect = null)
    {
        if (!Resolve(reflector, ref reflect, false) ||
            // !_toggle.IsActivated(reflector) ||
            !TryComp<ReflectiveComponent>(projectile, out var reflective) ||
            (reflect.Reflects & reflective.Reflective) == 0x0 ||
            !TryComp<PhysicsComponent>(projectile, out var physics))
        {
            return false;
        }

        // #Misfits Change Add: Use per-type probability override if defined, otherwise fall back to ReflectProb.
        var matchedType = reflective.Reflective & reflect.Reflects;
        var prob = reflect.ReflectProb;
        foreach (ReflectType type in Enum.GetValues<ReflectType>())
        {
            if (type == ReflectType.None) continue;
            if ((matchedType & type) != 0 && reflect.ReflectProbByType.TryGetValue(type, out var typeProb))
            {
                prob = typeProb;
                break;
            }
        }
        if (!_random.Prob(prob))
            return false;


        var rotation = _random.NextAngle(-reflect.Spread / 2, reflect.Spread / 2).Opposite();
        var existingVelocity = _physics.GetMapLinearVelocity(projectile, component: physics);
        var relativeVelocity = existingVelocity - _physics.GetMapLinearVelocity(user);
        var newVelocity = rotation.RotateVec(relativeVelocity);

        // Have the velocity in world terms above so need to convert it back to local.
        var difference = newVelocity - existingVelocity;

        _physics.SetLinearVelocity(projectile, physics.LinearVelocity + difference, body: physics);

        var locRot = Transform(projectile).LocalRotation;
        var newRot = rotation.RotateVec(locRot.ToVec());
        _transform.SetLocalRotation(projectile, newRot.ToAngle());

        if (_netManager.IsServer)
        {

            RaiseNetworkEvent(new ProjectileDeflectMsg(GetNetEntity(projectile)));
            // #Misfits Change Add: Show descriptive popup for small-caliber rounds bouncing off power armor / shields.
            if ((reflective.Reflective & ReflectType.SmallCaliber) != 0)
            {
                TryComp<ProjectileComponent>(projectile, out var pComp);
                var targetName = Identity.Name(user, EntityManager);
                var bulletName = Name(projectile);
                var shooterName = pComp?.Shooter is { } shooterId
                    ? Identity.Name(shooterId, EntityManager)
                    : Loc.GetString("reflect-unknown-shooter");
                _popup.PopupEntity(
                    Loc.GetString("reflect-shot-small-caliber",
                        ("shooter", shooterName), ("bullet", bulletName), ("target", targetName)),
                    user,
                    PopupType.Medium);
            }
            // #Misfits Change Add: Descriptive message for medium-caliber rounds glancing off power armor.
            else if ((reflective.Reflective & ReflectType.MediumCaliber) != 0)
            {
                TryComp<ProjectileComponent>(projectile, out var pComp);
                var targetName = Identity.Name(user, EntityManager);
                var bulletName = Name(projectile);
                var shooterName = pComp?.Shooter is { } shooterId
                    ? Identity.Name(shooterId, EntityManager)
                    : Loc.GetString("reflect-unknown-shooter");
                _popup.PopupEntity(
                    Loc.GetString("reflect-shot-medium-caliber",
                        ("shooter", shooterName), ("bullet", bulletName), ("target", targetName)),
                    user,
                    PopupType.Medium);
            }
            else
            {
                _popup.PopupEntity(Loc.GetString("reflect-shot"), user);
            }
            _audio.PlayPvs(reflect.SoundOnReflect, user, AudioHelpers.WithVariation(0.05f, _random));
        }

        if (Resolve(projectile, ref projectileComp, false))
        {
            _adminLogger.Add(LogType.BulletHit, LogImpact.Medium, $"{ToPrettyString(user)} reflected {ToPrettyString(projectile)} from {ToPrettyString(projectileComp.Weapon)} shot by {projectileComp.Shooter}");

            projectileComp.Shooter = user;
            projectileComp.Weapon = user;
            Dirty(projectile, projectileComp);
        }
        else
        {
            _adminLogger.Add(LogType.BulletHit, LogImpact.Medium, $"{ToPrettyString(user)} reflected {ToPrettyString(projectile)}");
        }

        return true;
    }

    private void OnReflectHitscan(EntityUid uid, ReflectComponent component, ref HitScanReflectAttemptEvent args)
    {
        if (args.Reflected ||
            (component.Reflects & args.Reflective) == 0x0)
        {
            return;
        }

        if (TryReflectHitscan(uid, uid, args.Shooter, args.SourceItem, args.Direction, out var dir))
        {
            args.Direction = dir.Value;
            args.Reflected = true;
        }
    }
    // TODO: remove this and have hitscan/proj all use same reflect method
    private bool TryReflectHitscan(
        EntityUid user,
        EntityUid reflector,
        EntityUid? shooter,
        EntityUid shotSource,
        Vector2 direction,
        [NotNullWhen(true)] out Vector2? newDirection)
    {
        if (!TryComp<ReflectComponent>(reflector, out var reflect) ||
            // !_toggle.IsActivated(reflector) ||
            !_random.Prob(reflect.ReflectProbByType[ReflectType.Energy]))
        {
            newDirection = null;
            return false;
        }


        _popup.PopupPredicted(Loc.GetString("reflect-shot"), user, user);
        _audio.PlayPredicted(reflect.SoundOnReflect, user, user, audioParams: AudioHelpers.WithVariation(0.05f, _random));


        var spread = _random.NextAngle(-reflect.Spread / 2, reflect.Spread / 2);

        newDirection = spread.RotateVec(-direction.Normalized());

        var strShooter = ToPrettyString(shooter) is EntityStringRepresentation shooterR ? $" shot by {shooterR}" : string.Empty;

        _adminLogger.Add(LogType.HitScanHit, LogImpact.Medium, $"{ToPrettyString(user)} reflected hitscan from {ToPrettyString(shotSource)}{strShooter}");


        return true;
    }

    private void OnReflectEquipped(EntityUid uid, ReflectComponent component, GotEquippedEvent args)
    {
        if (_gameTiming.ApplyingState)
            return;

        EnsureComp<ReflectUserComponent>(args.Equipee);
    }

    private void OnReflectUnequipped(EntityUid uid, ReflectComponent comp, GotUnequippedEvent args)
    {
        RefreshReflectUser(args.Equipee);
    }

    private void OnReflectHandEquipped(EntityUid uid, ReflectComponent component, GotEquippedHandEvent args)
    {
        if (_gameTiming.ApplyingState)
            return;

        EnsureComp<ReflectUserComponent>(args.User);
    }

    private void OnReflectHandUnequipped(EntityUid uid, ReflectComponent component, GotUnequippedHandEvent args)
    {
        RefreshReflectUser(args.User);
    }

    private void OnToggleReflect(EntityUid uid, ReflectComponent comp, ref ItemToggledEvent args)
    {
        if (args.User is { } user)
            RefreshReflectUser(user);
    }

    /// <summary>
    /// Refreshes whether someone has reflection potential so we can raise directed events on them.
    /// </summary>
    private void RefreshReflectUser(EntityUid user)
    {
        foreach (var ent in _inventorySystem.GetHandOrInventoryEntities(user, SlotFlags.All & ~SlotFlags.POCKET))
        {
            if (!HasComp<ReflectComponent>(ent) || !_toggle.IsActivated(ent))
                continue;

            EnsureComp<ReflectUserComponent>(user);
            return;
        }

        RemCompDeferred<ReflectUserComponent>(user);
    }
}
