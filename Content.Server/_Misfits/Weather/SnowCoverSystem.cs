using System.Numerics;
using Content.Shared._Misfits.Weather;
using Content.Shared.Ghost;
using Content.Shared.Light.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Weather;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
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

    private float _timer;
    private float _walkTimer;
    private float _sendTimer;

    private readonly Dictionary<EntityUid, SnowWalkerState> _walkers = new();
    private readonly Dictionary<EntityUid, Dictionary<EntityUid, List<SnowFootprint>>> _pending = new();
    private readonly List<SnowFootprint> _prints = new();

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
