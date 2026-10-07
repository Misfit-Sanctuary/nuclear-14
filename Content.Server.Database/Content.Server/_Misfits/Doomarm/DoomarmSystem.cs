// #Misfits Add - Doomarm abilities and attach/detach handling:
//   Kinetic Discharge - charge for a moment, lunge, throw the first person you hit.
//   Groundbreaker     - leap toward the click, slam the 3x3 where you land, slowing everyone in it.
//   Orbital Descent   - go up to the z-level above, steer, crash down on a 5x5.
// Using them back to back builds heat; going over the limit burns the user.

using System.Numerics;
using Content.Server._MultiZ.Core;
using Content.Server.Chat.Systems;
using Content.Shared._Misfits.Doomarm;
using Content.Shared._Misfits.MeleeCharge;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared.Actions;
using Content.Shared.Camera;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Speech;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Misfits.Doomarm;

public sealed partial class DoomarmSystem : SharedDoomarmSystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MapSystem _map = default!;
    [Dependency] private MeleeChargeSystem _meleeCharge = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MZSystem _multiZ = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedCameraRecoilSystem _recoil = default!;
    [Dependency] private SharedRoofSystem _roof = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;

    private readonly HashSet<Entity<MobStateComponent>> _mobs = new();

    /// <summary>
    /// How close (in tiles, centre to centre) someone has to be for the lunge to connect.
    /// Two mobs touching are about 0.7 apart.
    /// </summary>
    private const float LungeReach = 1.0f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DoomarmComponent, BodyPartComponentsModifyEvent>(OnArmModify);
        SubscribeLocalEvent<DoomarmFistComponent, GotUnequippedEvent>(OnFistUnequipped);

        SubscribeLocalEvent<DoomarmComponent, DoomarmLungeActionEvent>(OnLungeAction);
        SubscribeLocalEvent<DoomarmComponent, DoomarmLungeDoAfterEvent>(OnLungeDoAfter);
        SubscribeLocalEvent<DoomarmComponent, DoomarmSlamActionEvent>(OnSlamAction);
        SubscribeLocalEvent<DoomarmComponent, DoomarmDescentActionEvent>(OnDescentAction);

        SubscribeLocalEvent<DoomarmLungingComponent, LandEvent>(OnLungeLand);
        SubscribeLocalEvent<DoomarmAirborneComponent, ComponentShutdown>(OnAirborneShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = Timing.CurTime;

        var hops = EntityQueryEnumerator<DoomarmHoppingComponent>();
        while (hops.MoveNext(out var uid, out var hop))
        {
            if (now < hop.LandAt)
                continue;

            var arm = hop.Arm;
            RemCompDeferred<DoomarmHoppingComponent>(uid);

            if (TryComp<DoomarmComponent>(arm, out var comp))
                DoSlam(uid, (arm, comp));
        }

        var lunges = EntityQueryEnumerator<DoomarmLungingComponent>();
        while (lunges.MoveNext(out var uid, out var lunge))
        {
            if (now >= lunge.EndAt)
            {
                RemCompDeferred<DoomarmLungingComponent>(uid);
                continue;
            }

            if (!lunge.HasHit)
                CheckLungeHit((uid, lunge));
        }

        var airborne = EntityQueryEnumerator<DoomarmAirborneComponent>();
        while (airborne.MoveNext(out var uid, out var air))
        {
            UpdateAirborne(uid, air, now);
        }
    }

    // ── Attach / detach ──────────────────────────────────────────────────

    /// <summary>
    /// Raised on the arm when it's attached to a body (or re-enabled after being crippled) and when
    /// it's removed (or crippled). Hands out or takes back the abilities and the fist.
    /// </summary>
    private void OnArmModify(Entity<DoomarmComponent> ent, ref BodyPartComponentsModifyEvent args)
    {
        var body = args.Body;

        if (!args.Add)
        {
            _actions.RemoveProvidedActions(body, ent);
            DeleteFist(ent);
            return;
        }

        _actions.AddAction(body, ref ent.Comp.LungeActionEntity, ent.Comp.LungeAction, ent);
        _actions.AddAction(body, ref ent.Comp.SlamActionEntity, ent.Comp.SlamAction, ent);
        _actions.AddAction(body, ref ent.Comp.DescentActionEntity, ent.Comp.DescentAction, ent);
        Dirty(ent);

        EquipFist(ent, body);
    }

    /// <summary>
    /// Puts the punching fist in the gloves slot, pushing any gloves they wore onto the floor.
    /// </summary>
    private void EquipFist(Entity<DoomarmComponent> ent, EntityUid body)
    {
        DeleteFist(ent);

        if (_inventory.TryGetSlotEntity(body, GlovesSlot, out var worn))
        {
            if (HasComp<DoomarmFistComponent>(worn))
                return;

            _inventory.TryUnequip(body, body, GlovesSlot, silent: true, force: true);
        }

        var fist = Spawn(ent.Comp.Fist, Transform(body).Coordinates);
        if (!_inventory.TryEquip(body, body, fist, GlovesSlot, silent: true, force: true))
        {
            Del(fist);
            return;
        }

        ent.Comp.FistEntity = fist;
    }

    private void DeleteFist(Entity<DoomarmComponent> ent)
    {
        if (ent.Comp.FistEntity is { } fist && !TerminatingOrDeleted(fist))
            QueueDel(fist);

        ent.Comp.FistEntity = null;
    }

    private void OnFistUnequipped(Entity<DoomarmFistComponent> ent, ref GotUnequippedEvent args)
    {
        // However it left the slot (Doomarm removed, body gibbed), it never exists loose.
        QueueDel(ent);
    }

    // ── Shared helpers ───────────────────────────────────────────────────

    /// <summary>
    /// The Doomarm must be attached to this user and working, and they can't already be mid-ability.
    /// </summary>
    private bool CanUse(Entity<DoomarmComponent> ent, EntityUid user)
    {
        if (!TryGetDoomarm(user, out var arm) || arm.Value.Owner != ent.Owner)
        {
            _popup.PopupEntity(Loc.GetString("doomarm-not-working"), user, user);
            return false;
        }

        if (HasComp<DoomarmAirborneComponent>(user) ||
            HasComp<DoomarmHoppingComponent>(user) ||
            HasComp<DoomarmLungingComponent>(user))
        {
            _popup.PopupEntity(Loc.GetString("doomarm-busy"), user, user);
            return false;
        }

        return true;
    }

    private void AddHeat(Entity<DoomarmComponent> ent, EntityUid user, float amount)
    {
        var heat = GetHeat(ent.Comp) + amount;

        if (heat > ent.Comp.OverheatThreshold)
        {
            _damageable.TryChangeDamage(user, ent.Comp.OverheatDamage, ignoreResistances: true, origin: ent.Owner);
            _audio.PlayPvs(ent.Comp.OverheatSound, user);
            _popup.PopupEntity(Loc.GetString("doomarm-overheat"), user, user, PopupType.MediumCaution);
            heat = ent.Comp.OverheatThreshold;
        }

        ent.Comp.Heat = heat;
        ent.Comp.HeatUpdatedAt = Timing.CurTime;
        Dirty(ent);
    }

    /// <summary>
    /// Shout one of the ability's lines in bold, like a warcry.
    /// </summary>
    private void Shout(Entity<DoomarmComponent> ent, EntityUid user, string ability)
    {
        var line = Loc.GetString($"doomarm-shout-{ability}-{_random.Next(1, ent.Comp.ShoutVariants + 1)}");
        _proto.TryIndex<SpeechVerbPrototype>(ent.Comp.ShoutVerb, out var verb);
        _chat.TrySendInGameICMessage(user, line, InGameICChatType.Speak, ChatTransmitRange.Normal,
            ignoreActionBlocker: true, speechVerbOverride: verb);
    }

    /// <summary>
    /// Square ("Chebyshev") distance in tiles: 0 = same tile, 1 = the 3x3 around it, 2 = the 5x5.
    /// </summary>
    private int TileDistance(MapCoordinates center, EntityUid other)
    {
        var otherPos = _transform.GetMapCoordinates(other);

        Vector2i a, b;
        if (_map.TryFindGridAt(center, out var gridUid, out var grid))
        {
            a = _map.CoordinatesToTile(gridUid, grid, center);
            b = _map.CoordinatesToTile(gridUid, grid, otherPos);
        }
        else
        {
            a = new Vector2i((int) MathF.Floor(center.Position.X), (int) MathF.Floor(center.Position.Y));
            b = new Vector2i((int) MathF.Floor(otherPos.Position.X), (int) MathF.Floor(otherPos.Position.Y));
        }

        return Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    /// <summary>
    /// Every living mob within <paramref name="radius"/> tiles (square) of the centre, except the user.
    /// </summary>
    private IEnumerable<(EntityUid Uid, int Distance)> MobsInSquare(MapCoordinates center, int radius, EntityUid user)
    {
        _mobs.Clear();
        // A circle that fully covers the square; the tile check below trims the corners exactly.
        _lookup.GetEntitiesInRange(center, radius * 1.5f + 1f, _mobs);

        foreach (var mob in _mobs)
        {
            if (mob.Owner == user || _mobState.IsDead(mob.Owner, mob.Comp))
                continue;

            var distance = TileDistance(center, mob.Owner);
            if (distance <= radius)
                yield return (mob.Owner, distance);
        }
    }

    private void Impact(Entity<DoomarmComponent> ent, EntityUid user, MapCoordinates center, float shakeRange)
    {
        Spawn(ent.Comp.ImpactEffect, center);
        _audio.PlayPvs(ent.Comp.ImpactSound, user);

        // Screen shake that fades out with distance, like the vertibird combat drop.
        foreach (var nearby in _lookup.GetEntitiesInRange<CameraRecoilComponent>(center, shakeRange))
        {
            var delta = _transform.GetMapCoordinates(nearby).Position - center.Position;
            var distance = delta.Length();
            var strength = ent.Comp.ShakeStrength * (1f - distance / shakeRange);
            if (strength <= 0f)
                continue;

            var direction = distance > 0.01f ? delta / distance : new Vector2(0f, 1f);
            _recoil.KickCamera(nearby, direction * strength);
        }
    }

    // ── Kinetic Discharge ────────────────────────────────────────────────

    private void OnLungeAction(Entity<DoomarmComponent> ent, ref DoomarmLungeActionEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;
        if (!CanUse(ent, user))
            return;

        // Not marked handled here: the cooldown only starts if the charge-up finishes.
        var doAfter = new DoAfterArgs(EntityManager, user, ent.Comp.LungeChargeTime,
            new DoomarmLungeDoAfterEvent(GetNetCoordinates(args.Target)), ent.Owner, used: ent.Owner)
        {
            BreakOnMove = true,
            BlockDuplicate = true,
            NeedHand = false,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        _audio.PlayPvs(ent.Comp.LungeChargeSound, user);
        _popup.PopupEntity(Loc.GetString("doomarm-lunge-charging", ("user", user)), user, PopupType.Medium);
    }

    private void OnLungeDoAfter(Entity<DoomarmComponent> ent, ref DoomarmLungeDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        var user = args.User;
        if (!CanUse(ent, user))
            return;

        _actions.StartUseDelay(ent.Comp.LungeActionEntity);
        AddHeat(ent, user, ent.Comp.LungeHeat);
        Shout(ent, user, "lunge");

        var target = GetCoordinates(args.TargetCoordinates);
        var from = _transform.GetMapCoordinates(user).Position;
        var to = _transform.ToMapCoordinates(target).Position;
        var direction = to - from;
        direction = direction.LengthSquared() > 0.001f ? direction.Normalized() : new Vector2(0f, -1f);

        var lunging = EnsureComp<DoomarmLungingComponent>(user);
        lunging.Arm = ent.Owner;
        lunging.Direction = direction;
        lunging.HasHit = false;
        lunging.EndAt = Timing.CurTime + TimeSpan.FromSeconds(2);

        // Same throw the deathclaw charge uses: can't steer mid-lunge, stops on landing.
        _meleeCharge.PerformDash(user, target, ent.Comp.LungeSpeed, ent.Comp.LungeRange);
    }

    /// <summary>
    /// Hits the first living mob within reach in front of the lunge. Checked every tick instead of using
    /// ThrowDoHitEvent, because that event never fires for thrown mobs: they carry more than one
    /// fixture (fire adds one), and the throw system only adds its hit fixture to single-fixture entities.
    /// </summary>
    private void CheckLungeHit(Entity<DoomarmLungingComponent> ent)
    {
        if (!TryComp<DoomarmComponent>(ent.Comp.Arm, out var arm))
            return;

        EntityUid? target = null;
        var closest = float.MaxValue;
        var userPos = _transform.GetMapCoordinates(ent.Owner);

        _mobs.Clear();
        _lookup.GetEntitiesInRange(userPos, LungeReach, _mobs);
        foreach (var mob in _mobs)
        {
            if (mob.Owner == ent.Owner || _mobState.IsDead(mob.Owner, mob.Comp))
                continue;

            var delta = _transform.GetMapCoordinates(mob.Owner).Position - userPos.Position;
            var distance = delta.LengthSquared();
            if (distance >= closest)
                continue;

            // Only people in front of the lunge (within about 60 degrees of its direction), not beside or behind.
            if (distance > 0.0001f && Vector2.Dot(delta / MathF.Sqrt(distance), ent.Comp.Direction) < 0.5f)
                continue;

            closest = distance;
            target = mob.Owner;
        }

        if (target is not { } hit)
            return;

        ent.Comp.HasHit = true;

        _damageable.TryChangeDamage(hit, arm.LungeDamage, origin: ent.Owner);
        _audio.PlayPvs(arm.LungeHitSound, hit);

        // Send them flying along the lunge, like a power fist hit. No thrower set, so the
        // throw-impact chat lines don't fire for this.
        _throwing.TryThrow(hit, ent.Comp.Direction * arm.LungeKnockbackDistance,
            arm.LungeKnockbackSpeed, null, 0f, playSound: false, doSpin: false);

        // The lunger stops dead on the hit.
        _physics.SetLinearVelocity(ent.Owner, Vector2.Zero);
    }

    private void OnLungeLand(Entity<DoomarmLungingComponent> ent, ref LandEvent args)
    {
        RemCompDeferred<DoomarmLungingComponent>(ent);
    }

    // ── Groundbreaker ────────────────────────────────────────────────────

    private void OnSlamAction(Entity<DoomarmComponent> ent, ref DoomarmSlamActionEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;
        if (!CanUse(ent, user))
            return;

        args.Handled = true;
        AddHeat(ent, user, ent.Comp.SlamHeat);
        Shout(ent, user, "slam");

        var hop = EnsureComp<DoomarmHoppingComponent>(user);
        hop.Arm = ent.Owner;
        hop.LandAt = Timing.CurTime + TimeSpan.FromSeconds(ent.Comp.HopTime);

        // Leap toward the click, at most HopRange tiles, timed so it lands as the hop ends.
        // A wall in the way stops the leap early; the slam still happens where they end up.
        var from = _transform.GetMapCoordinates(user).Position;
        var to = _transform.ToMapCoordinates(args.Target).Position;
        var distance = MathF.Min((to - from).Length(), ent.Comp.HopRange);
        if (distance > 0.1f)
            _meleeCharge.PerformDash(user, args.Target, distance / ent.Comp.HopTime, ent.Comp.HopRange);

        RaiseNetworkEvent(new DoomarmHopEvent(GetNetEntity(user), ent.Comp.HopTime), Filter.Pvs(user));
    }

    private void DoSlam(EntityUid user, Entity<DoomarmComponent> ent)
    {
        if (TerminatingOrDeleted(user))
            return;

        var center = _transform.GetMapCoordinates(user);
        var slow = TimeSpan.FromSeconds(ent.Comp.SlamSlowTime);

        foreach (var (mob, _) in MobsInSquare(center, ent.Comp.SlamRadius, user))
        {
            _damageable.TryChangeDamage(mob, ent.Comp.SlamDamage, origin: user);
            _stun.TrySlowdown(mob, slow, true, ent.Comp.SlamSlowMultiplier, ent.Comp.SlamSlowMultiplier);
        }

        Impact(ent, user, center, ent.Comp.SlamRadius + 4f);
    }

    // ── Orbital Descent ──────────────────────────────────────────────────

    private void OnDescentAction(Entity<DoomarmComponent> ent, ref DoomarmDescentActionEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;
        if (!CanUse(ent, user))
            return;

        var xform = Transform(user);
        if (xform.MapUid is not { } groundMap ||
            !_multiZ.TryMapUp(groundMap, out _) ||
            xform.ParentUid != xform.MapUid && xform.ParentUid != xform.GridUid)
        {
            // Not on a map with a level above (or inside a locker / vehicle). Nothing used up.
            _popup.PopupEntity(Loc.GetString("doomarm-descent-no-sky"), user, user);
            return;
        }

        args.Handled = true;
        AddHeat(ent, user, ent.Comp.DescentHeat);

        // A roof above (a floor on the level above, or an indoor roof on this map): smash into it.
        if (_multiZ.HasTileAbove(user) || IsUnderRoof(user))
        {
            _damageable.TryChangeDamage(user, ent.Comp.CeilingDamage, origin: ent.Owner);
            _audio.PlayPvs(ent.Comp.ImpactSound, user);
            _popup.PopupEntity(Loc.GetString("doomarm-descent-ceiling-self"), user, user, PopupType.MediumCaution);
            _popup.PopupEntity(Loc.GetString("doomarm-descent-ceiling-others", ("user", user)), user,
                Filter.PvsExcept(user), true, PopupType.Medium);
            return;
        }

        var start = _transform.GetWorldPosition(xform);
        Shout(ent, user, "descent");
        _audio.PlayPvs(ent.Comp.LaunchSound, user);

        if (!_multiZ.TryMove(user, 1))
            return;

        _physics.SetLinearVelocity(user, Vector2.Zero);

        // The ring on the ground shows everyone below where the crash will land.
        var groundMapId = Comp<MapComponent>(groundMap).MapId;
        var marker = Spawn(ent.Comp.LandingMarker, new MapCoordinates(start, groundMapId));
        _popup.PopupEntity(Loc.GetString("doomarm-descent-warning"), marker, PopupType.LargeCaution);

        var air = EnsureComp<DoomarmAirborneComponent>(user);
        air.Arm = ent.Owner;
        air.LandAt = Timing.CurTime + TimeSpan.FromSeconds(ent.Comp.AirTime);
        air.Marker = marker;
        air.GroundMap = groundMap;
        air.LastSafePosition = start;
        Dirty(user, air);
    }

    /// <summary>
    /// Is this spot inside a building? Maps mark indoor tiles with a roof flag (it's what makes them dark).
    /// </summary>
    private bool IsUnderRoof(EntityUid uid)
    {
        var coords = _transform.GetMapCoordinates(uid);
        if (!_map.TryFindGridAt(coords, out var gridUid, out var grid) ||
            !TryComp<RoofComponent>(gridUid, out var roof))
            return false;

        return _roof.IsRooved((gridUid, grid, roof), _map.CoordinatesToTile(gridUid, grid, coords));
    }

    /// <summary>
    /// Can someone crash down at this spot on the ground level: a floor, no wall on it, and no roof over it?
    /// </summary>
    private bool IsLandable(MapId groundMapId, Vector2 position)
    {
        var coords = new MapCoordinates(position, groundMapId);
        if (!_map.TryFindGridAt(coords, out var gridUid, out var grid))
            return false;

        var tile = _map.CoordinatesToTile(gridUid, grid, coords);
        if (!_map.TryGetTileRef(gridUid, grid, tile, out var tileRef) || tileRef.Tile.IsEmpty)
            return false;

        if (TryComp<RoofComponent>(gridUid, out var roof) && _roof.IsRooved((gridUid, grid, roof), tile))
            return false;

        return !_turf.IsTileBlocked(gridUid, tile, CollisionGroup.Impassable, grid);
    }

    private void UpdateAirborne(EntityUid user, DoomarmAirborneComponent air, TimeSpan now)
    {
        if (!TryComp<MapComponent>(air.GroundMap, out var groundMapComp))
        {
            RemCompDeferred<DoomarmAirborneComponent>(user);
            return;
        }

        // Knocked out, killed or had the Doomarm cut off up there: fall straight down, no crash.
        var crash = !_mobState.IsIncapacitated(user) &&
                    TryGetDoomarm(user, out var arm) && arm.Value.Owner == air.Arm;

        var position = _transform.GetWorldPosition(user);
        if (IsLandable(groundMapComp.MapId, position))
            air.LastSafePosition = position;

        if (air.Marker is { } marker && !TerminatingOrDeleted(marker))
            _transform.SetMapCoordinates(marker, new MapCoordinates(air.LastSafePosition, groundMapComp.MapId));

        if (crash && now < air.LandAt)
            return;

        Land(user, air, groundMapComp.MapId, crash);
    }

    private void Land(EntityUid user, DoomarmAirborneComponent air, MapId groundMapId, bool crash)
    {
        var landing = new MapCoordinates(air.LastSafePosition, groundMapId);
        var armUid = air.Arm;

        RemComp<DoomarmAirborneComponent>(user);

        // A direct move down: no MultiZ fall damage to the user.
        if (!_multiZ.TryMove(user, -1, worldPosition: air.LastSafePosition))
            _transform.SetMapCoordinates(user, landing);

        _physics.SetLinearVelocity(user, Vector2.Zero);

        if (!crash || !TryComp<DoomarmComponent>(armUid, out var comp))
            return;

        Entity<DoomarmComponent> ent = (armUid, comp);
        var radius = comp.DescentRingDamage.Count - 1;

        foreach (var (mob, distance) in MobsInSquare(landing, radius, user))
        {
            var damage = new DamageSpecifier();
            damage.DamageDict["Blunt"] = comp.DescentRingDamage[distance];
            _damageable.TryChangeDamage(mob, damage, origin: user);
        }

        Impact(ent, user, landing, radius + 6f);
        _popup.PopupEntity(Loc.GetString("doomarm-descent-landed", ("user", user)), user, PopupType.LargeCaution);
    }

    private void OnAirborneShutdown(Entity<DoomarmAirborneComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Marker is { } marker && !TerminatingOrDeleted(marker))
            QueueDel(marker);

        ent.Comp.Marker = null;
    }
}
