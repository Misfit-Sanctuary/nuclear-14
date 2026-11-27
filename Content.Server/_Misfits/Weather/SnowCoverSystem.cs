using System.Numerics;
using Content.Server.Atmos.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.CombatMode;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Ghost;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Light.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Popups;
using Content.Shared.Temperature.Components;
using Content.Shared.Throwing;
using Content.Shared.Verbs;
using Content.Shared.Weather;
using Content.Shared._Misfits.Weather;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Input.Binding;
using Robust.Shared.Input;
using Robust.Shared.Map.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Physics;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Misfits.Weather;

public sealed partial class SnowCoverSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedWeatherSystem _weather = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedSnowSlowedSystem _slowed = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private const float CheckInterval = 2f;
    private const float WalkInterval = 0.2f;
    private const float SendInterval = 1f;
    private const float SendStep = 0.02f;

    private const float FillSpeed = 3f;

    private const float DeepSnowSpeed = 0.65f;

    private const float TrackPickup = 0.3f;
    private const float TrackTiles = 2f;
    private const float TrackStep = 0.32f;
    private const float TrackRadius = 0.075f;
    private const float TrackStrength = 0.7f;
    private const float TrackLife = 25f;

    private float _timer;
    private float _walkTimer;
    private float _sendTimer;

    private readonly Dictionary<EntityUid, SnowWalkerState> _walkers = new();
    private readonly Dictionary<EntityUid, Dictionary<EntityUid, List<SnowFootprint>>> _pending = new();
    private readonly List<SnowFootprint> _prints = new();
    private readonly Dictionary<EntityUid, List<SnowHeat>> _heats = new();
    private readonly Dictionary<EntityUid, List<SnowTrailDot>> _trailDots = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _timer += frameTime;
        if (_timer >= CheckInterval)
        {
            UpdateDepth(_timer);
            _timer = 0f;
        }

        _walkTimer += frameTime;
        if (_walkTimer >= WalkInterval)
        {
            _walkTimer = 0f;
            UpdateWalkers();
        }

        _sendTimer += frameTime;
        if (_sendTimer >= SendInterval)
        {
            _sendTimer = 0f;
            SendPending();
        }
    }

    private void UpdateDepth(float elapsed)
    {
        var query = EntityQueryEnumerator<WeatherComponent>();
        while (query.MoveNext(out var mapUid, out var weather))
        {
            var rate = 0f;
            foreach (var (protoId, data) in weather.Weather)
            {
                if (!_proto.TryIndex<WeatherPrototype>(protoId, out var proto) || proto.SnowAccumulation == 0f)
                    continue;

                rate += proto.SnowAccumulation * _weather.GetPercent(data, mapUid);
            }

            TryComp<SnowCoverComponent>(mapUid, out var cover);

            if (cover == null && rate <= 0f)
                continue;

            cover ??= EnsureComp<SnowCoverComponent>(mapUid);

            var fillRate = MathF.Max(rate, 0f) * FillSpeed;
            var dirty = MathF.Abs(fillRate - cover.FillRate) > 0.0001f;
            cover.FillRate = fillRate;

            if (rate == 0f)
                rate = -cover.MeltRate;

            var depth = Math.Clamp(cover.Depth + rate * elapsed, 0f, 1f);
            if (depth != cover.Depth)
            {
                cover.Depth = depth;
                if (MathF.Abs(depth - cover.SentDepth) >= SendStep || depth == 0f || depth == 1f)
                {
                    cover.SentDepth = depth;
                    dirty = true;
                }
            }

            if (dirty)
                Dirty(mapUid, cover);

            UpdatePaths(mapUid, cover, elapsed);

            if (cover.Depth > 0.05f)
                UpdateHeat(mapUid, elapsed);
        }
    }

    private void UpdatePaths(EntityUid mapUid, SnowCoverComponent cover, float elapsed)
    {
        var grids = EntityQueryEnumerator<SnowTrampleComponent, TransformComponent>();
        while (grids.MoveNext(out var gridUid, out var trample, out var xform))
        {
            if (xform.MapUid != mapUid || trample.Tiles.Count == 0)
                continue;

            if (cover.Depth <= 0f)
            {
                trample.Tiles.Clear();
                trample.Version++;
                _pending.Remove(gridUid);
                RaiseNetworkEvent(new SnowTrampleSnapshotEvent { Grid = GetNetEntity(gridUid) }, Filter.Broadcast());
                continue;
            }

            SnowCoverMath.FillPaths(trample, cover.FillRate * elapsed);
        }
    }

    private void UpdateHeat(EntityUid mapUid, float elapsed)
    {
        foreach (var list in _heats.Values)
        {
            list.Clear();
        }

        var fires = EntityQueryEnumerator<FlammableComponent, TransformComponent>();
        while (fires.MoveNext(out var uid, out var flammable, out var xform))
        {
            if (flammable.OnFire)
                AddHeat(uid, xform, mapUid);
        }

        var hot = EntityQueryEnumerator<AlwaysHotComponent, TransformComponent>();
        while (hot.MoveNext(out var uid, out _, out var xform))
        {
            AddHeat(uid, xform, mapUid);
        }

        var toggles = EntityQueryEnumerator<ItemToggleHotComponent, ItemToggleComponent, TransformComponent>();
        while (toggles.MoveNext(out var uid, out _, out var toggle, out var xform))
        {
            if (toggle.Activated)
                AddHeat(uid, xform, mapUid);
        }

        foreach (var (gridUid, heats) in _heats)
        {
            if (heats.Count == 0 || TerminatingOrDeleted(gridUid))
                continue;

            var trample = EnsureComp<SnowTrampleComponent>(gridUid);
            foreach (var heat in heats)
            {
                SnowCoverMath.Melt(trample, heat, elapsed);
            }

            var ev = new SnowMeltEvent
            {
                Grid = GetNetEntity(gridUid),
                Elapsed = elapsed,
                Heats = new List<SnowHeat>(heats),
            };
            RaiseNetworkEvent(ev, Filter.Broadcast());
        }
    }

    private void AddHeat(EntityUid uid, TransformComponent xform, EntityUid mapUid)
    {
        if (xform.MapUid != mapUid ||
            xform.GridUid is not { } gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid))
        {
            return;
        }

        var indices = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        TryComp<RoofComponent>(gridUid, out var roof);
        if (!_weather.CanWeatherAffect(gridUid, grid, _map.GetTileRef(gridUid, grid, indices), roof))
            return;

        var position = Vector2.Transform(_transform.GetWorldPosition(xform), _transform.GetInvWorldMatrix(gridUid));
        var radius = xform.Anchored ? SnowCoverMath.BigHeatRadius : SnowCoverMath.SmallHeatRadius;

        if (!_heats.TryGetValue(gridUid, out var list))
        {
            list = new List<SnowHeat>();
            _heats[gridUid] = list;
        }

        list.Add(new SnowHeat(position, radius));
    }

    public float SnowAt(EntityUid uid, TransformComponent xform, out EntityUid gridUid, out Vector2 position)
    {
        return SnowAt(xform.Coordinates, out gridUid, out position);
    }

    public float SnowAt(EntityCoordinates coords, out EntityUid gridUid, out Vector2 position)
    {
        gridUid = default;
        position = default;

        if (_transform.GetMap(coords) is not { } mapUid ||
            !TryComp<SnowCoverComponent>(mapUid, out var cover) ||
            cover.Depth <= 0.05f ||
            _transform.GetGrid(coords) is not { } grid ||
            !TryComp<MapGridComponent>(grid, out var gridComp))
        {
            return 0f;
        }

        gridUid = grid;
        position = Vector2.Transform(_transform.ToMapCoordinates(coords).Position, _transform.GetInvWorldMatrix(grid));
        var indices = _map.TileIndicesFor(grid, gridComp, coords);
        TryComp<RoofComponent>(grid, out var roof);
        if (!_weather.CanWeatherAffect(grid, gridComp, _map.GetTileRef(grid, gridComp, indices), roof))
            return 0f;

        var coverage = SnowCoverMath.TileCoverage(indices, cover.Depth);
        if (coverage <= 0f)
            return 0f;

        var packed = TryComp<SnowTrampleComponent>(grid, out var trample)
            ? SnowCoverMath.Sample(trample, position, 0.15f)
            : 0f;

        return coverage * (1f - MathF.Min(1f, packed * 1.5f));
    }

    public bool HasSnowCover(EntityUid? mapUid)
    {
        return mapUid != null && TryComp<SnowCoverComponent>(mapUid, out var cover) && cover.Depth > 0.05f;
    }

    public bool NearHeat(EntityUid gridUid, Vector2 position, float scale)
    {
        return _heats.TryGetValue(gridUid, out var heats) && SnowCoverMath.NearHeat(heats, position, scale);
    }

    public void AddPrints(EntityUid gridUid, EntityUid walker, List<SnowFootprint> prints)
    {
        if (prints.Count == 0)
            return;

        var trample = EnsureComp<SnowTrampleComponent>(gridUid);
        foreach (var print in prints)
        {
            SnowCoverMath.Stamp(trample, print);
        }

        if (!_pending.TryGetValue(gridUid, out var byWalker))
        {
            byWalker = new Dictionary<EntityUid, List<SnowFootprint>>();
            _pending[gridUid] = byWalker;
        }

        if (!byWalker.TryGetValue(walker, out var list))
        {
            list = new List<SnowFootprint>();
            byWalker[walker] = list;
        }

        list.AddRange(prints);
    }

    private void UpdateWalkers()
    {
        var query = EntityQueryEnumerator<InputMoverComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid is not { } mapUid ||
                !TryComp<SnowCoverComponent>(mapUid, out var cover) ||
                cover.Depth <= 0.05f ||
                HasComp<GhostComponent>(uid) ||
                TryComp<PhysicsComponent>(uid, out var physics) && physics.BodyStatus == BodyStatus.InAir ||
                xform.GridUid is not { } gridUid ||
                !TryComp<MapGridComponent>(gridUid, out var grid))
            {
                _walkers.Remove(uid);
                if (HasComp<SnowSlowedComponent>(uid))
                    RemComp<SnowSlowedComponent>(uid);
                continue;
            }

            var position = Vector2.Transform(_transform.GetWorldPosition(xform), _transform.GetInvWorldMatrix(gridUid));
            var indices = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
            var tile = _map.GetTileRef(gridUid, grid, indices);
            TryComp<RoofComponent>(gridUid, out var roof);
            var open = _weather.CanWeatherAffect(gridUid, grid, tile, roof);
            var coverage = open ? SnowCoverMath.TileCoverage(indices, cover.Depth) : 0f;

            var slowed = EnsureComp<SnowSlowedComponent>(uid);
            var trample = EnsureComp<SnowTrampleComponent>(gridUid);

            if (!_walkers.TryGetValue(uid, out var state))
            {
                state = new SnowWalkerState();
                _walkers[uid] = state;
            }

            _prints.Clear();
            SnowCoverMath.Walk(state, gridUid, position, _random, _prints);

            if (open && coverage > TrackPickup)
            {
                state.Snowy = TrackTiles;
            }
            else if (state.Snowy > 0f)
            {
                foreach (var print in _prints)
                {
                    if (state.Snowy <= 0f)
                        break;

                    if (!open)
                        AddTrail(gridUid, new SnowTrailDot(print.Position, TrackRadius, TrackStrength * (0.3f + 0.7f * state.Snowy / TrackTiles), TrackLife));

                    state.Snowy -= TrackStep;
                }
            }

            if (coverage > 0f && _prints.Count > 0)
            {
                foreach (var print in _prints)
                {
                    SnowCoverMath.Stamp(trample, print);
                }

                if (!_pending.TryGetValue(gridUid, out var byWalker))
                {
                    byWalker = new Dictionary<EntityUid, List<SnowFootprint>>();
                    _pending[gridUid] = byWalker;
                }

                if (!byWalker.TryGetValue(uid, out var list))
                {
                    list = new List<SnowFootprint>();
                    byWalker[uid] = list;
                }

                list.AddRange(_prints);
            }

            var packed = coverage > 0f ? SnowCoverMath.Sample(trample, position, 0.15f) : 1f;
            var snow = coverage * (1f - MathF.Min(1f, packed * 1.5f));
            _slowed.SetModifier((uid, slowed), 1f - (1f - DeepSnowSpeed) * snow);
        }
    }

    private void SendPending()
    {
        foreach (var (gridUid, byWalker) in _pending)
        {
            if (byWalker.Count == 0 || TerminatingOrDeleted(gridUid))
                continue;

            var ev = new SnowFootprintsEvent { Grid = GetNetEntity(gridUid) };
            foreach (var (walker, prints) in byWalker)
            {
                ev.Walkers.Add(new SnowWalkerPrints
                {
                    Walker = GetNetEntity(walker),
                    Prints = new List<SnowFootprint>(prints),
                });
            }

            RaiseNetworkEvent(ev, Filter.Broadcast());
        }

        _pending.Clear();

        foreach (var (gridUid, dots) in _trailDots)
        {
            if (dots.Count == 0 || TerminatingOrDeleted(gridUid))
                continue;

            RaiseNetworkEvent(new SnowTrailEvent
            {
                Grid = GetNetEntity(gridUid),
                Dots = new List<SnowTrailDot>(dots),
            }, Filter.Broadcast());
        }

        _trailDots.Clear();
    }

    public void AddTrail(EntityUid gridUid, SnowTrailDot dot)
    {
        if (!_trailDots.TryGetValue(gridUid, out var dots))
        {
            dots = new List<SnowTrailDot>();
            _trailDots[gridUid] = dots;
        }

        dots.Add(dot);
    }

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        var query = EntityQueryEnumerator<SnowTrampleComponent>();
        while (query.MoveNext(out var gridUid, out var trample))
        {
            var ev = new SnowTrampleSnapshotEvent { Grid = GetNetEntity(gridUid) };
            foreach (var (indices, tile) in trample.Tiles)
            {
                ev.Tiles[indices] = (byte[]) tile.Cells.Clone();
            }

            RaiseNetworkEvent(ev, args.Player);
        }
    }
}

public sealed partial class SnowballSystem : EntitySystem
{
    [Dependency] private SnowCoverSystem _snow = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private StaminaSystem _stamina = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedCombatModeSystem _combat = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;

    private static readonly EntProtoId SnowballProto = "MisfitsSnowball";
    private static readonly EntProtoId GiantProto = "MisfitsSnowballGiant";
    private static readonly EntProtoId SnowmanProto = "MisfitsSnowman";
    private const float BuildSeconds = 4f;
    private static readonly SoundSpecifier SnowSound = new SoundCollectionSpecifier("FootstepSnow");

    private const float MinSnow = 0.2f;
    private const float MakeSeconds = 1.5f;
    private const float ClickClearance = 0.35f;
    private const float PackSeconds = 1.2f;
    private const float HitStamina = 4f;
    private const float HitSnow = 0.15f;
    private const float GiantHitGrowth = 0.03f;

    private const float RollInterval = 0.1f;
    private const float PushSpeed = 2.4f;
    private const float PushKick = 1.35f;
    private const float SnowDrag = 0.8f;
    private const float GrowPerTile = 0.07f;
    private const float PrintStep = 0.2f;
    private const float MeltSeconds = 900f;
    private const float HeatMeltSeconds = 60f;
    private const float MeltAway = 0.6f;
    private const string FixtureId = "fix1";

    private float _rollTimer;
    private readonly HashSet<EntityUid> _touching = new();
    private readonly List<SnowFootprint> _prints = new();
    private readonly List<EntityUid> _melted = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HandsComponent, SnowballMakeDoAfterEvent>(OnMake);
        SubscribeLocalEvent<SnowballComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<SnowballComponent, SnowballPackDoAfterEvent>(OnPack);
        SubscribeLocalEvent<SnowballComponent, ThrowDoHitEvent>(OnThrowHit);
        SubscribeLocalEvent<SnowballComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<SnowballComponent, SnowmanBuildDoAfterEvent>(OnBuildSnowman);

        CommandBinds.Builder
            .BindBefore(EngineKeyFunctions.Use, new PointerInputCmdHandler(OnClick), typeof(SharedInteractionSystem))
            .Register<SnowballSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<SnowballSystem>();
    }

    private bool OnClick(ICommonSession? session, EntityCoordinates coords, EntityUid clicked)
    {
        if (session?.AttachedEntity is not { } user ||
            clicked.IsValid() ||
            !coords.IsValid(EntityManager) ||
            !HasComp<HandsComponent>(user) ||
            _combat.IsInCombatMode(user) ||
            !_blocker.CanInteract(user, null) ||
            !_interaction.InRangeUnobstructed(user, coords))
        {
            return false;
        }

        EntityUid? ball = null;
        if (_hands.TryGetActiveItem(user, out var held))
        {
            if (!TryComp<SnowballComponent>(held, out var heldBall) || heldBall.Giant)
                return false;

            ball = held;
        }

        if (_snow.SnowAt(coords, out _, out _) < MinSnow)
            return false;

        foreach (var near in _lookup.GetEntitiesInRange(coords, ClickClearance, LookupFlags.Dynamic | LookupFlags.Sundries))
        {
            if (near == user || near == ball)
                continue;

            if (HasComp<ItemComponent>(near) || HasComp<SnowballComponent>(near))
                return false;
        }

        var net = GetNetCoordinates(coords);
        if (ball == null)
        {
            _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, MakeSeconds, new SnowballMakeDoAfterEvent(net), user)
            {
                BreakOnMove = true,
                NeedHand = true,
            });
        }
        else
        {
            StartPack(user, ball.Value, net);
        }

        return true;
    }

    private void OnMake(Entity<HandsComponent> ent, ref SnowballMakeDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        var user = ent.Owner;

        if (!TakeSnow(user, GetCoordinates(args.Coordinates)))
            return;

        var ball = SpawnAtPosition(SnowballProto, Transform(user).Coordinates);
        _hands.TryPickupAnyHand(user, ball);
    }

    private void OnUseInHand(Entity<SnowballComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled || ent.Comp.Giant)
            return;

        args.Handled = true;
        var feet = Transform(args.User).Coordinates.Offset(new Vector2(0f, -0.3f));
        StartPack(args.User, ent, GetNetCoordinates(feet));
    }

    private void StartPack(EntityUid user, EntityUid ball, NetCoordinates coords)
    {
        if (_snow.SnowAt(GetCoordinates(coords), out _, out _) < MinSnow)
        {
            _popup.PopupEntity(Loc.GetString("snowball-no-snow"), user, user);
            return;
        }

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, PackSeconds, new SnowballPackDoAfterEvent(coords), ball, used: ball)
        {
            BreakOnMove = true,
            NeedHand = true,
            BreakOnDropItem = true,
        });
    }

    private void OnPack(Entity<SnowballComponent> ent, ref SnowballPackDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        var user = args.User;

        if (!TakeSnow(user, GetCoordinates(args.Coordinates)))
            return;

        if (ent.Comp.Size < SnowballMath.MaxPacks)
        {
            ent.Comp.Size += 1f;
            Dirty(ent);
            _popup.PopupEntity(Loc.GetString("snowball-packed"), user, user);
            return;
        }

        var coords = Transform(user).Coordinates;
        QueueDel(ent);
        var giant = SpawnAtPosition(GiantProto, coords);
        if (TryComp<SnowballComponent>(giant, out var giantBall))
            SetGiantSize((giant, giantBall), SnowballMath.GiantStart);

        _popup.PopupEntity(Loc.GetString("snowball-too-big"), user, user, PopupType.Medium);
    }

    private bool TakeSnow(EntityUid user, EntityCoordinates coords)
    {
        if (_snow.SnowAt(coords, out var gridUid, out var position) < MinSnow)
        {
            _popup.PopupEntity(Loc.GetString("snowball-no-snow"), user, user);
            return false;
        }

        _prints.Clear();
        _prints.Add(new SnowFootprint(position, 0.22f, 160));
        _snow.AddPrints(gridUid, user, _prints);
        _audio.PlayPvs(SnowSound, coords);
        return true;
    }

    private void OnThrowHit(Entity<SnowballComponent> ent, ref ThrowDoHitEvent args)
    {
        if (ent.Comp.Giant || TerminatingOrDeleted(ent))
            return;

        var target = args.Target;

        if (TryComp<SnowballComponent>(target, out var targetBall) && targetBall.Giant)
        {
            SetGiantSize((target, targetBall), targetBall.Size + GiantHitGrowth * ent.Comp.Size);
        }
        else
        {
            if (HasComp<StaminaComponent>(target))
                _stamina.TakeStaminaDamage(target, HitStamina * ent.Comp.Size, source: ent);

            if (HasComp<InputMoverComponent>(target))
            {
                RaiseNetworkEvent(new SnowballHitEvent
                {
                    Target = GetNetEntity(target),
                    Amount = HitSnow * ent.Comp.Size,
                }, Filter.Pvs(target));
            }
        }

        Splat(ent);
    }

    private void OnGetVerbs(Entity<SnowballComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!ent.Comp.Giant || ent.Comp.Snowman || !args.CanInteract || !args.CanAccess)
            return;

        var user = args.User;
        var big = ent.Comp.Size >= SnowballMath.SnowmanMin;
        var target = ent.Owner;

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("snowman-verb-build"),
            Disabled = !big,
            Message = big ? null : Loc.GetString("snowman-too-small"),
            Act = () =>
            {
                _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, BuildSeconds, new SnowmanBuildDoAfterEvent(), target, target)
                {
                    BreakOnMove = true,
                    NeedHand = true,
                });
            },
        });
    }

    private void OnBuildSnowman(Entity<SnowballComponent> ent, ref SnowmanBuildDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || ent.Comp.Snowman || ent.Comp.Size < SnowballMath.SnowmanMin)
            return;

        args.Handled = true;
        var xform = Transform(ent);
        var snowman = SpawnAtPosition(SnowmanProto, xform.Coordinates);
        if (TryComp<SnowballComponent>(snowman, out var made))
            SetGiantSize((snowman, made), ent.Comp.Size);

        _audio.PlayPvs(SnowSound, xform.Coordinates);
        _popup.PopupEntity(Loc.GetString("snowman-built"), snowman, args.User);
        QueueDel(ent);
    }

    private void MeltSnowman(Entity<SnowballComponent> ent, TransformComponent xform, float elapsed)
    {
        if (!_snow.HasSnowCover(xform.MapUid))
        {
            SetGiantSize(ent, ent.Comp.Size - elapsed / MeltSeconds * SnowballMath.GiantMax);
            return;
        }

        _snow.SnowAt(xform.Coordinates, out var gridUid, out var position);
        if (gridUid != default && _snow.NearHeat(gridUid, position, 1.3f))
            SetGiantSize(ent, ent.Comp.Size - elapsed / HeatMeltSeconds * SnowballMath.GiantMax);
    }


    private void Splat(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid))
            return;

        _audio.PlayPvs(SnowSound, Transform(uid).Coordinates);
        QueueDel(uid);
    }

    private void SetGiantSize(Entity<SnowballComponent> ent, float size)
    {
        size = Math.Clamp(size, 0f, SnowballMath.GiantMax);
        if (MathF.Abs(size - ent.Comp.Size) < 0.001f)
            return;

        ent.Comp.Size = size;
        Dirty(ent);

        if (TryComp<FixturesComponent>(ent, out var fixtures) &&
            fixtures.Fixtures.TryGetValue(FixtureId, out var fixture) &&
            fixture.Shape is PhysShapeCircle circle)
        {
            _physics.SetRadius(ent, FixtureId, fixture, circle, SnowballMath.GiantRadius * MathF.Max(size, 0.5f), fixtures);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _rollTimer += frameTime;
        if (_rollTimer < RollInterval)
            return;

        var elapsed = _rollTimer;
        _rollTimer = 0f;
        _melted.Clear();

        var query = EntityQueryEnumerator<SnowballComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var ball, out var body, out var xform))
        {
            if (!ball.Giant)
                continue;

            if (ball.Snowman)
            {
                MeltSnowman((uid, ball), xform, elapsed);
            }
            else
            {
                Push(uid, ball, body);
                Roll((uid, ball), xform, elapsed);
            }

            if (ball.Size < MeltAway)
                _melted.Add(uid);
        }

        foreach (var uid in _melted)
        {
            QueueDel(uid);
        }

    }

    private void Push(EntityUid uid, SnowballComponent ball, PhysicsComponent body)
    {
        _touching.Clear();
        _physics.GetContactingEntities((uid, body), _touching, true);

        var ballPos = _transform.GetWorldPosition(uid);
        var best = Vector2.Zero;

        foreach (var other in _touching)
        {
            if (!HasComp<InputMoverComponent>(other) || !TryComp<PhysicsComponent>(other, out var otherBody))
                continue;

            var away = ballPos - _transform.GetWorldPosition(other);
            if (away.LengthSquared() < 0.0001f)
                continue;

            away = Vector2.Normalize(away);
            var toward = Vector2.Dot(otherBody.LinearVelocity, away);
            if (toward <= 0.2f)
                continue;

            var push = away * MathF.Min(toward * PushKick, PushSpeed / MathF.Max(1f, ball.Size * 0.8f));
            if (push.LengthSquared() > best.LengthSquared())
                best = push;
        }

        if (best != Vector2.Zero && body.LinearVelocity.LengthSquared() < best.LengthSquared())
            _physics.SetLinearVelocity(uid, best, body: body);
    }

    private void Roll(Entity<SnowballComponent> ent, TransformComponent xform, float elapsed)
    {
        var ball = ent.Comp;

        if (!_snow.HasSnowCover(xform.MapUid))
        {
            SetGiantSize(ent, ball.Size - elapsed / MeltSeconds * SnowballMath.GiantMax);
            ball.Grid = default;
            return;
        }

        _snow.SnowAt(xform.Coordinates, out var gridUid, out var position);
        if (gridUid == default || xform.ParentUid != gridUid)
        {
            ball.Grid = default;
            return;
        }

        if (_snow.NearHeat(gridUid, position, 1.3f))
            SetGiantSize(ent, ball.Size - elapsed / HeatMeltSeconds * SnowballMath.GiantMax);

        if (ball.Grid != gridUid)
        {
            ball.Grid = gridUid;
            ball.LastPosition = position;
            ball.Carry = 0f;
            return;
        }

        var delta = position - ball.LastPosition;
        var distance = delta.Length();
        if (distance < 0.01f)
            return;

        if (distance > 3f)
        {
            ball.LastPosition = position;
            ball.Carry = 0f;
            return;
        }

        var dir = delta / distance;
        var radius = SnowballMath.GiantRadius * ball.Size;
        var snow = _snow.SnowAt(xform.Coordinates.Offset(dir * radius * 1.1f), out _, out _);

        if (snow > 0f)
        {
            SetGiantSize(ent, ball.Size + distance * snow * GrowPerTile / MathF.Max(1f, ball.Size * 0.7f));

            if (TryComp<PhysicsComponent>(ent, out var body))
                _physics.SetLinearVelocity(ent, body.LinearVelocity * MathF.Max(0f, 1f - snow * SnowDrag * elapsed), body: body);
        }

        var travel = PrintStep - ball.Carry;
        _prints.Clear();

        while (travel <= distance)
        {
            _prints.Add(new SnowFootprint(ball.LastPosition + dir * travel, radius * 0.85f, 200));
            travel += PrintStep;
        }

        ball.Carry = distance - (travel - PrintStep);
        ball.LastPosition = position;
        _snow.AddPrints(gridUid, ent, _prints);
    }
}
