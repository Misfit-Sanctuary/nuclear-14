using Content.Client.IconSmoothing;
using Content.Client.Weather;
using Content.Shared.Doors.Components;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;
using Content.Shared.Ghost;
using Content.Shared.Item;
using Content.Shared.Light.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Physics;
using Content.Shared._Misfits.Weather;
using Content.Shared.Weather;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.Utility;
using Robust.Shared.Console;
using Robust.Shared.Map.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using System.Numerics;
using Image = SixLabors.ImageSharp.Image;
using Color = Robust.Shared.Maths.Color;

namespace Content.Client._Misfits.Weather;

public sealed partial class SnowCoverSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlay = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IConsoleHost _console = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WeatherSystem _weather = default!;

    private const float FillInterval = 1f;
    private static readonly TimeSpan LocalWalkerMemory = TimeSpan.FromSeconds(3);

    private float _fillTimer;
    private readonly Dictionary<EntityUid, SnowWalkerState> _walkers = new();
    private readonly Dictionary<EntityUid, TimeSpan> _seenWalking = new();
    private readonly List<SnowFootprint> _prints = new();

    public override void Initialize()
    {
        base.Initialize();
        _overlay.AddOverlay(new SnowCoverOverlay());
        SubscribeNetworkEvent<SnowFootprintsEvent>(OnFootprints);
        SubscribeNetworkEvent<SnowTrampleSnapshotEvent>(OnSnapshot);
        _console.RegisterCommand("snowdebug", "Shows what the snow tops are finding near you", "snowdebug [paint]", SnowDebugCommand);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlay.RemoveOverlay<SnowCoverOverlay>();
        _console.UnregisterCommand("snowdebug");
        ClearAll();

        foreach (var (uid, person) in _people)
        {
            ClearPerson(uid, person);
        }

        _people.Clear();
    }

    private void OnFootprints(SnowFootprintsEvent ev)
    {
        if (!TryGetEntity(ev.Grid, out var grid))
            return;

        var trample = EnsureComp<SnowTrampleComponent>(grid.Value);
        var now = _timing.RealTime;

        foreach (var walker in ev.Walkers)
        {
            if (TryGetEntity(walker.Walker, out var uid) &&
                _seenWalking.TryGetValue(uid.Value, out var seen) &&
                now - seen < LocalWalkerMemory)
            {
                continue;
            }

            foreach (var print in walker.Prints)
            {
                SnowCoverMath.Stamp(trample, print);
            }
        }
    }

    private void OnSnapshot(SnowTrampleSnapshotEvent ev)
    {
        if (!TryGetEntity(ev.Grid, out var grid))
            return;

        var trample = EnsureComp<SnowTrampleComponent>(grid.Value);
        trample.Tiles.Clear();

        foreach (var (indices, cells) in ev.Tiles)
        {
            var tile = new SnowTrampleTile();
            Array.Copy(cells, tile.Cells, Math.Min(cells.Length, tile.Cells.Length));
            tile.Version++;
            trample.Tiles[indices] = tile;
        }

        trample.Version++;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        UpdateCaps(frameTime);
        UpdatePeople(frameTime);
        UpdateWalkers();

        _fillTimer += frameTime;
        if (_fillTimer < FillInterval)
            return;

        var elapsed = _fillTimer;
        _fillTimer = 0f;

        var query = EntityQueryEnumerator<SnowTrampleComponent, TransformComponent>();
        while (query.MoveNext(out var trample, out var xform))
        {
            if (xform.MapUid is not { } mapUid || !TryComp<SnowCoverComponent>(mapUid, out var cover))
                continue;

            SnowCoverMath.FillPaths(trample, cover.FillRate * elapsed);
        }

        var now = _timing.RealTime;
        foreach (var uid in new List<EntityUid>(_seenWalking.Keys))
        {
            if (now - _seenWalking[uid] > LocalWalkerMemory || TerminatingOrDeleted(uid))
            {
                _seenWalking.Remove(uid);
                _walkers.Remove(uid);
            }
        }
    }

    private void UpdateWalkers()
    {
        var now = _timing.RealTime;
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
                continue;
            }

            var position = Vector2.Transform(_transform.GetWorldPosition(xform), _transform.GetInvWorldMatrix(gridUid));

            if (!_walkers.TryGetValue(uid, out var state))
            {
                state = new SnowWalkerState();
                _walkers[uid] = state;
            }

            _seenWalking[uid] = now;

            _prints.Clear();
            SnowCoverMath.Walk(state, gridUid, position, _random, _prints);
            if (_prints.Count == 0)
                continue;

            var indices = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
            if (!IsOpen(gridUid, grid, indices, false) || SnowCoverMath.TileCoverage(indices, cover.Depth) <= 0f)
                continue;

            var trample = EnsureComp<SnowTrampleComponent>(gridUid);
            foreach (var print in _prints)
            {
                SnowCoverMath.Stamp(trample, print);
            }
        }
    }

    private static readonly ProtoId<ShaderPrototype> SnowCapShader = "SnowCap";
    private const float ScanInterval = 1f;
    private const float ScanRange = 14f;
    private const float MinDepth = 0.05f;

    private const int CapStages = 6;
    private const float CapLag = 0.05f;
    private const float CapJitter = 0.12f;

    private readonly ShaderInstance?[] _stageShaders = new ShaderInstance?[CapStages + 1];
    private readonly ShaderInstance?[] _buriedStageShaders = new ShaderInstance?[CapStages + 1];
    private float _capTimer;
    private float _shownAmount;

    private readonly Dictionary<EntityUid, CapEntry> _capped = new();
    private readonly HashSet<EntityUid> _seen = new();
    private readonly Dictionary<(EntityUid, Vector2i), bool> _openTiles = new();
    private readonly Dictionary<string, int> _debugCounts = new();
    private readonly Dictionary<EntityUid, string> _debugReasons = new();
    private bool _debugPaint;

    private void UpdateCaps(float frameTime)
    {
        var eye = _eye.CurrentEye;
        var mapUid = _map.GetMapOrInvalid(eye.Position.MapId);
        TryComp<SnowCoverComponent>(mapUid, out var cover);
        var depth = cover?.Depth ?? 0f;

        _shownAmount += (depth - _shownAmount) * MathF.Min(1f, frameTime * 0.5f);
        for (var stage = 1; stage <= CapStages; stage++)
        {
            if (_stageShaders[stage] is { } capShader)
                SetCapParameters(capShader, false, stage);
            if (_buriedStageShaders[stage] is { } buriedShader)
                SetCapParameters(buriedShader, true, stage);
        }

        _capTimer += frameTime;
        if (_capTimer < ScanInterval)
            return;
        _capTimer = 0f;

        _debugCounts.Clear();
        _debugReasons.Clear();

        if (depth <= MinDepth && _shownAmount <= MinDepth)
        {
            Count("snow too thin for tops");
            ClearAll();
            return;
        }

        var range = ScanRange * MathF.Max(eye.Zoom.X, eye.Zoom.Y);
        var box = Box2.CenteredAround(eye.Position.Position, new Vector2(range * 2f, range * 2f));

        _seen.Clear();
        _openTiles.Clear();

        var flags = LookupFlags.Static | LookupFlags.Sundries | LookupFlags.Approximate;
        foreach (var uid in _lookup.GetEntitiesIntersecting(eye.Position.MapId, box, flags))
        {
            Count("found");

            if (!TryComp<SpriteComponent>(uid, out var sprite) || !sprite.Visible)
            {
                Count("skipped, no visible sprite", uid);
                continue;
            }

            if (HasComp<IconSmoothComponent>(uid) || HasComp<OccluderComponent>(uid) || HasComp<DoorComponent>(uid))
            {
                Count("skipped, wall or fence", uid);
                continue;
            }

            if (HasComp<ItemComponent>(uid) || HasComp<InputMoverComponent>(uid))
            {
                Count("skipped, item or mob", uid);
                continue;
            }

            var xform = Transform(uid);
            if (!xform.Anchored && TryComp<PhysicsComponent>(uid, out var body) && body.BodyType != BodyType.Static)
            {
                Count("skipped, moves around", uid);
                continue;
            }

            if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            {
                Count("skipped, not on a grid", uid);
                continue;
            }

            var indices = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
            if (!IsOutside(gridUid, grid, indices))
            {
                Count("skipped, under a roof", uid);
                continue;
            }

            var jitter = SnowCoverMath.Hash((int) uid.Id, 4021) * CapJitter;
            var progress = SnowCoverMath.TileCoverage(indices, _shownAmount - CapLag - jitter);
            var stage = (int) MathF.Ceiling(progress * CapStages);
            if (stage <= 0)
            {
                Count("waiting, ground here isn't snowy enough yet", uid);
                continue;
            }

            _seen.Add(uid);
            var buried = sprite.DrawDepth <= (int) DrawDepth.FloorObjects;
            if (!_capped.TryGetValue(uid, out var entry))
                Cap(uid, sprite, GetStageShader(stage, buried), stage);
            else if (entry.Stage != stage)
                Restage(uid, sprite, entry, GetStageShader(stage, buried), stage);

            Count(_capped[uid].Layers.Count > 0 ? "snowed" : "skipped, every layer already has a shader", uid);
        }

        foreach (var uid in new List<EntityUid>(_capped.Keys))
        {
            if (!_seen.Contains(uid))
                Uncap(uid);
        }
    }

    private void Count(string reason, EntityUid? uid = null)
    {
        _debugCounts[reason] = _debugCounts.GetValueOrDefault(reason) + 1;
        if (uid != null)
            _debugReasons[uid.Value] = reason;
    }

    private void SnowDebugCommand(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 0 && args[0] == "paint")
        {
            _debugPaint = !_debugPaint;
            shell.WriteLine(_debugPaint ? "snow tops painted blue" : "snow tops back to normal");
            return;
        }

        var eye = _eye.CurrentEye.Position;
        var mapUid = _map.GetMapOrInvalid(eye.MapId);
        TryComp<SnowCoverComponent>(mapUid, out var cover);
        shell.WriteLine($"snow depth {cover?.Depth ?? 0f:0.00}, top thickness {_shownAmount:0.00}, things with snow tops {_capped.Count}");
        var steps = new int[CapStages + 1];
        foreach (var entry in _capped.Values)
        {
            steps[entry.Stage]++;
        }

        shell.WriteLine($"  things at each step, thinnest first {string.Join(" ", steps[1..])}");

        foreach (var (reason, count) in _debugCounts)
        {
            shell.WriteLine($"  {reason} {count}");
        }

        var near = new List<(float Distance, EntityUid Uid, string Reason)>();
        foreach (var (uid, reason) in _debugReasons)
        {
            if (TerminatingOrDeleted(uid))
                continue;

            var distance = (_transform.GetWorldPosition(uid) - eye.Position).Length();
            if (distance <= 4f)
                near.Add((distance, uid, reason));
        }

        near.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        shell.WriteLine("closest things");
        for (var i = 0; i < Math.Min(12, near.Count); i++)
        {
            var (distance, uid, reason) = near[i];
            var layers = "";
            if (TryComp<SpriteComponent>(uid, out var sprite))
            {
                var ours = 0;
                var total = 0;
                foreach (var layer in sprite.AllLayers)
                {
                    total++;
                    if (layer is SpriteComponent.Layer real && IsSceneryShader(real.Shader))
                        ours++;
                }

                layers = $" layers with snow {ours} of {total}";
            }

            shell.WriteLine($"  {MetaData(uid).EntityPrototype?.ID ?? "no prototype"} {distance:0.0} tiles away, {reason}{layers}");
        }
    }

    private bool IsOutside(EntityUid gridUid, MapGridComponent grid, Vector2i indices)
    {
        return IsOpen(gridUid, grid, indices) || IsOpen(gridUid, grid, indices + new Vector2i(0, 1));
    }

    private bool IsOpen(EntityUid gridUid, MapGridComponent grid, Vector2i indices, bool remember = true)
    {
        if (remember && _openTiles.TryGetValue((gridUid, indices), out var open))
            return open;

        TryComp<RoofComponent>(gridUid, out var roof);
        var tile = _map.GetTileRef(gridUid, grid, indices);
        open = !tile.Tile.IsEmpty && _weather.CanWeatherAffect(gridUid, grid, tile, roof);
        if (remember)
            _openTiles[(gridUid, indices)] = open;
        return open;
    }

    private void Cap(EntityUid uid, SpriteComponent sprite, ShaderInstance shader, int stage)
    {
        var layers = new List<int>();
        var index = 0;
        foreach (var layer in sprite.AllLayers)
        {
            if (layer is SpriteComponent.Layer real && real.Shader == null && real.ShaderPrototype == null)
            {
                sprite.LayerSetShader(index, shader);
                layers.Add(index);
            }

            index++;
        }

        _capped[uid] = new CapEntry(layers, stage);
    }

    private void Restage(EntityUid uid, SpriteComponent sprite, CapEntry entry, ShaderInstance shader, int stage)
    {
        var index = 0;
        foreach (var layer in sprite.AllLayers)
        {
            if (layer is SpriteComponent.Layer real && IsSceneryShader(real.Shader) && entry.Layers.Contains(index))
                sprite.LayerSetShader(index, shader);

            index++;
        }

        _capped[uid] = entry with { Stage = stage };
    }

    private ShaderInstance GetStageShader(int stage, bool buried)
    {
        var shaders = buried ? _buriedStageShaders : _stageShaders;
        if (shaders[stage] is { } existing)
            return existing;

        var shader = _proto.Index(SnowCapShader).InstanceUnique();
        SetCapParameters(shader, buried, stage);
        shaders[stage] = shader;
        return shader;
    }

    private void SetCapParameters(ShaderInstance shader, bool buried, int stage)
    {
        var step = (float) stage / CapStages;
        shader.SetParameter("amount", _shownAmount * step);
        shader.SetParameter("coverage", step);
        shader.SetParameter("debugPaint", _debugPaint ? 1f : 0f);
        shader.SetParameter("buried", buried ? Math.Clamp(_shownAmount * 2f * step, 0.01f, 1f) : 0f);
    }

    private bool IsSceneryShader(ShaderInstance? shader)
    {
        if (shader == null)
            return false;

        return Array.IndexOf(_stageShaders, shader) >= 0 || Array.IndexOf(_buriedStageShaders, shader) >= 0;
    }

    private void Uncap(EntityUid uid)
    {
        if (!_capped.Remove(uid, out var entry))
            return;

        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        var index = 0;
        foreach (var layer in sprite.AllLayers)
        {
            if (layer is SpriteComponent.Layer real && IsSceneryShader(real.Shader) && entry.Layers.Contains(index))
                sprite.LayerSetShader(index, (ShaderInstance?) null);

            index++;
        }
    }

    private readonly record struct CapEntry(List<int> Layers, int Stage);

    private const float PeopleInterval = 0.25f;
    private const float HeavySnowRate = 0.003f;
    private const float PeopleBuildSeconds = 90f;
    private const float PeopleShakeFloor = 0.3f;
    private const float PeopleShakeSeconds = 40f;
    private const float PeopleIndoorMeltSeconds = 20f;
    private const float PeopleOutdoorMeltSeconds = 150f;
    private const float PeopleMoveSpeed = 0.5f;
    private const float ServerFillSpeed = 3f;

    private float _peopleTimer;
    private readonly Dictionary<EntityUid, PersonSnow> _people = new();
    private readonly List<EntityUid> _peopleGone = new();

    private sealed class PersonSnow
    {
        public float Amount;
        public Vector2 LastPosition;
        public ShaderInstance? Shader;
    }

    private void UpdatePeople(float frameTime)
    {
        _peopleTimer += frameTime;
        if (_peopleTimer < PeopleInterval)
            return;

        var elapsed = _peopleTimer;
        _peopleTimer = 0f;

        var mapId = _eye.CurrentEye.Position.MapId;
        var mapUid = _map.GetMapOrInvalid(mapId);
        TryComp<SnowCoverComponent>(mapUid, out var cover);
        var snowfall = cover == null ? 0f : Math.Clamp(cover.FillRate / ServerFillSpeed / HeavySnowRate, 0f, 1f);

        var query = EntityQueryEnumerator<InputMoverComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var sprite, out var xform))
        {
            _people.TryGetValue(uid, out var person);

            if (person == null && snowfall <= 0f)
                continue;

            if (xform.MapID != mapId || HasComp<GhostComponent>(uid))
                continue;

            var position = _transform.GetWorldPosition(xform);
            if (person == null)
            {
                person = new PersonSnow { LastPosition = position };
                _people[uid] = person;
            }

            var outside = xform.GridUid is { } gridUid &&
                          TryComp<MapGridComponent>(gridUid, out var grid) &&
                          IsOpen(gridUid, grid, _map.TileIndicesFor(gridUid, grid, xform.Coordinates), false);

            var moving = (position - person.LastPosition).Length() / elapsed > PeopleMoveSpeed;
            person.LastPosition = position;

            if (outside && snowfall > 0f)
            {
                person.Amount += snowfall / PeopleBuildSeconds * elapsed;

                if (moving && person.Amount > PeopleShakeFloor)
                    person.Amount = MathF.Max(PeopleShakeFloor, person.Amount - elapsed / PeopleShakeSeconds);
            }
            else
            {
                person.Amount -= elapsed / (outside ? PeopleOutdoorMeltSeconds : PeopleIndoorMeltSeconds);
                if (moving)
                    person.Amount -= elapsed / PeopleShakeSeconds;
            }

            person.Amount = Math.Clamp(person.Amount, 0f, 1f);

            if (person.Amount <= 0f)
            {
                ClearPerson(uid, person);
                _people.Remove(uid);
                continue;
            }

            if (person.Shader == null)
            {
                person.Shader = _proto.Index(SnowCapShader).InstanceUnique();
                person.Shader.SetParameter("buried", 0f);
            }

            person.Shader.SetParameter("amount", person.Amount * 0.8f);
            person.Shader.SetParameter("coverage", MathF.Min(1f, 0.25f + person.Amount * 1.5f));
            person.Shader.SetParameter("debugPaint", _debugPaint ? 1f : 0f);

            var index = 0;
            foreach (var layer in sprite.AllLayers)
            {
                if (layer is SpriteComponent.Layer real && real.Shader == null && real.ShaderPrototype == null)
                    sprite.LayerSetShader(index, person.Shader);

                index++;
            }
        }

        _peopleGone.Clear();
        foreach (var (uid, person) in _people)
        {
            if (TerminatingOrDeleted(uid))
            {
                person.Shader?.Dispose();
                _peopleGone.Add(uid);
            }
        }

        foreach (var uid in _peopleGone)
        {
            _people.Remove(uid);
        }
    }

    private void ClearPerson(EntityUid uid, PersonSnow person)
    {
        if (person.Shader == null)
            return;

        if (TryComp<SpriteComponent>(uid, out var sprite))
        {
            var index = 0;
            foreach (var layer in sprite.AllLayers)
            {
                if (layer is SpriteComponent.Layer real && real.Shader == person.Shader)
                    sprite.LayerSetShader(index, (ShaderInstance?) null);

                index++;
            }
        }

        person.Shader.Dispose();
        person.Shader = null;
    }

    private void ClearAll()
    {
        foreach (var uid in new List<EntityUid>(_capped.Keys))
        {
            Uncap(uid);
        }
    }
}

public sealed partial class SnowCoverOverlay : GridOverlay
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IResourceCache _cache = default!;
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly ResPath SnowTexturePath = new("/Textures/Tiles/Planet/Snow/snow.png");
    private const int SnowVariants = 13;
    private const float CacheMargin = 6f;
    private const float FadePixels = 12f;
    private const float MaxAlpha = 0.92f;
    private const float BareAlpha = 0.2f;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(2);

    private const int North = 1, East = 2, South = 4, West = 8;
    private const int NorthEast = 16, SouthEast = 32, SouthWest = 64, NorthWest = 128;

    private static readonly (Vector2i Offset, int Bit)[] Neighbours =
    {
        (new Vector2i(0, 1), North),
        (new Vector2i(1, 0), East),
        (new Vector2i(0, -1), South),
        (new Vector2i(-1, 0), West),
        (new Vector2i(1, 1), NorthEast),
        (new Vector2i(1, -1), SouthEast),
        (new Vector2i(-1, -1), SouthWest),
        (new Vector2i(-1, 1), NorthWest),
    };

    private readonly SharedMapSystem _map;
    private readonly SharedTransformSystem _transform;
    private readonly WeatherSystem _weather;
    private readonly Texture[] _variants = new Texture[SnowVariants];
    private readonly Dictionary<int, Texture> _edgeTextures = new();
    private readonly Dictionary<(EntityUid, Vector2i), FootprintTexture> _footprintTextures = new();
    private readonly HashSet<Vector2i> _usedFootprintTextures = new();
    private readonly float[] _cellWindow = new float[(SnowCoverMath.Cells + 2) * (SnowCoverMath.Cells + 2)];
    private readonly Dictionary<EntityUid, TileCache> _tileCache = new();
    private Image<Rgba32>? _sheet;
    private int _tilePixels;

    public SnowCoverOverlay()
    {
        IoCManager.InjectDependencies(this);
        _map = _entManager.System<SharedMapSystem>();
        _transform = _entManager.System<SharedTransformSystem>();
        _weather = _entManager.System<WeatherSystem>();

        ZIndex = 1;
    }

    protected override void DisposeBehavior()
    {
        base.DisposeBehavior();
        _sheet?.Dispose();
        foreach (var entry in _footprintTextures.Values)
        {
            entry.Texture?.Dispose();
        }
        _footprintTextures.Clear();
    }

    private void LoadTextures()
    {
        if (_sheet != null)
            return;

        using (var stream = _cache.ContentFileRead(SnowTexturePath))
        {
            _sheet = Image.Load<Rgba32>(stream);
        }

        var sheetTexture = _cache.GetResource<TextureResource>(SnowTexturePath).Texture;
        _tilePixels = _sheet.Height;
        for (var i = 0; i < SnowVariants; i++)
        {
            _variants[i] = new AtlasTexture(sheetTexture, UIBox2.FromDimensions(i * _tilePixels, 0, _tilePixels, _tilePixels));
        }
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.MapId == MapId.Nullspace)
            return;

        var mapUid = _map.GetMapOrInvalid(args.MapId);
        if (!_entManager.TryGetComponent<SnowCoverComponent>(mapUid, out var cover) || cover.Depth <= 0.01f)
            return;

        var grid = Grid;
        if (!_entManager.TryGetComponent<TransformComponent>(grid.Owner, out var xform) || xform.MapID != args.MapId)
            return;

        LoadTextures();

        var cached = GetCache(grid, args.WorldAABB);
        if (cached.Open.Count == 0)
            return;

        _entManager.TryGetComponent<SnowTrampleComponent>(grid.Owner, out var trample);
        var version = trample?.Version ?? 0;
        if (cached.DrawDepth != cover.Depth || cached.DrawVersion != version)
            BuildDrawList(grid.Owner, cached, grid.Comp.TileSize, cover.Depth, trample);
        cached.DrawVersion = version;

        if (cached.Draws.Count == 0)
            return;

        var worldMatrix = _transform.GetWorldMatrix(xform);
        Matrix3x2.Invert(worldMatrix, out var invMatrix);
        var localView = invMatrix.TransformBox(args.WorldBounds).Enlarged(1f);

        var handle = args.WorldHandle;
        handle.SetTransform(worldMatrix);

        foreach (var draw in cached.Draws)
        {
            if (!localView.Intersects(draw.Box))
                continue;

            handle.DrawTextureRect(draw.Texture, draw.Box, draw.Color);
        }

        handle.SetTransform(Matrix3x2.Identity);
    }

    private TileCache GetCache(Entity<MapGridComponent> grid, Box2 worldView)
    {
        var now = _timing.RealTime;
        if (_tileCache.TryGetValue(grid.Owner, out var cached) &&
            cached.Bounds.Contains(worldView) &&
            now - cached.Built < CacheLifetime)
        {
            return cached;
        }

        cached ??= new TileCache();
        cached.Open.Clear();
        cached.Covered.Clear();
        cached.Solid.Clear();
        cached.Bounds = worldView.Enlarged(CacheMargin);
        cached.Built = now;
        cached.DrawDepth = -1f;

        _entManager.TryGetComponent<RoofComponent>(grid.Owner, out var roof);
        foreach (var tile in _map.GetTilesIntersecting(grid.Owner, grid.Comp, cached.Bounds))
        {
            if (_weather.CanWeatherAffect(grid.Owner, grid.Comp, tile, roof))
                cached.Open.Add(tile.GridIndices);
            else if (HasSolid(grid, tile.GridIndices))
                cached.Solid.Add(tile.GridIndices);
            else
                cached.Covered.Add(tile.GridIndices);
        }

        _tileCache[grid.Owner] = cached;
        return cached;
    }

    private bool HasSolid(Entity<MapGridComponent> grid, Vector2i indices)
    {
        foreach (var ent in _map.GetAnchoredEntities(grid.Owner, grid.Comp, indices))
        {
            if (_entManager.TryGetComponent<PhysicsComponent>(ent, out var physics) &&
                physics.CanCollide &&
                physics.Hard &&
                physics.BodyType == BodyType.Static &&
                (physics.CollisionLayer & (int) CollisionGroup.Impassable) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private void BuildDrawList(EntityUid gridUid, TileCache cached, ushort tileSize, float depth, SnowTrampleComponent? trample)
    {
        cached.DrawDepth = depth;
        cached.Draws.Clear();
        cached.Alpha.Clear();
        _usedFootprintTextures.Clear();

        var drift = Math.Clamp((depth - 0.1f) / 0.4f, 0f, 1f);

        foreach (var indices in cached.Open)
        {
            var alpha = SnowCoverMath.TileCoverage(indices, depth);

            if (drift > 0f && TouchesSolid(cached, indices))
                alpha = MathF.Max(alpha, drift);

            cached.Alpha[indices] = alpha;
        }

        foreach (var (indices, alpha) in cached.Alpha)
        {
            if (alpha <= 0f)
                continue;

            var mask = 0;
            foreach (var (offset, bit) in Neighbours)
            {
                var next = indices + offset;
                if (cached.Covered.Contains(next) ||
                    cached.Alpha.TryGetValue(next, out var nextAlpha) && nextAlpha <= BareAlpha && alpha > nextAlpha)
                {
                    mask |= bit;
                }
            }

            if ((mask & (North | East)) != 0) mask &= ~NorthEast;
            if ((mask & (South | East)) != 0) mask &= ~SouthEast;
            if ((mask & (South | West)) != 0) mask &= ~SouthWest;
            if ((mask & (North | West)) != 0) mask &= ~NorthWest;

            var pick = SnowCoverMath.Hash(indices.X + 911, indices.Y - 37);
            var variant = pick < 0.85f ? 0 : 1 + (int) ((pick - 0.85f) / 0.15f * (SnowVariants - 1));
            variant = Math.Clamp(variant, 0, SnowVariants - 1);

            Texture texture;
            if (trample != null && trample.Tiles.TryGetValue(indices, out var tile))
                texture = GetFootprintTexture(gridUid, indices, variant, mask, trample, tile);
            else
                texture = mask == 0 ? _variants[variant] : GetEdgeTexture(variant, mask);

            var box = new Box2(indices * tileSize, (indices + Vector2i.One) * tileSize);
            cached.Draws.Add(new SnowDraw(box, texture, Color.White.WithAlpha(alpha * MaxAlpha)));
        }

        foreach (var key in new List<(EntityUid, Vector2i)>(_footprintTextures.Keys))
        {
            if (key.Item1 != gridUid || _usedFootprintTextures.Contains(key.Item2))
                continue;

            _footprintTextures[key].Texture?.Dispose();
            _footprintTextures.Remove(key);
        }
    }

    private static bool TouchesSolid(TileCache cached, Vector2i indices)
    {
        return cached.Solid.Contains(indices + new Vector2i(0, 1)) ||
               cached.Solid.Contains(indices + new Vector2i(1, 0)) ||
               cached.Solid.Contains(indices + new Vector2i(0, -1)) ||
               cached.Solid.Contains(indices + new Vector2i(-1, 0));
    }

    private static float EdgeFade(int mask, float px, float py, int size)
    {
        if (mask == 0)
            return 1f;

        var wobble = 2.5f * MathF.Sin(px / size * MathF.Tau * 2f) + 2f * MathF.Sin(py / size * MathF.Tau * 3f + 1f);

        var dist = float.MaxValue;
        if ((mask & North) != 0) dist = MathF.Min(dist, py);
        if ((mask & South) != 0) dist = MathF.Min(dist, size - py);
        if ((mask & West) != 0) dist = MathF.Min(dist, px);
        if ((mask & East) != 0) dist = MathF.Min(dist, size - px);
        if ((mask & NorthWest) != 0) dist = MathF.Min(dist, new Vector2(px, py).Length());
        if ((mask & NorthEast) != 0) dist = MathF.Min(dist, new Vector2(size - px, py).Length());
        if ((mask & SouthWest) != 0) dist = MathF.Min(dist, new Vector2(px, size - py).Length());
        if ((mask & SouthEast) != 0) dist = MathF.Min(dist, new Vector2(size - px, size - py).Length());

        var t = Math.Clamp((dist + wobble) / FadePixels, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private Texture GetEdgeTexture(int variant, int mask)
    {
        var key = variant | mask << 4;
        if (_edgeTextures.TryGetValue(key, out var texture))
            return texture;

        var size = _tilePixels;
        var sheetPixels = _sheet!.GetPixelSpan();
        var sheetWidth = _sheet!.Width;
        using var image = new Image<Rgba32>(size, size);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var source = sheetPixels[y * sheetWidth + variant * size + x];
                source.A = (byte) (source.A * EdgeFade(mask, x + 0.5f, y + 0.5f, size));
                image[x, y] = source;
            }
        }

        texture = _clyde.LoadTextureFromImage(image, $"snow-edge-{key}");
        _edgeTextures[key] = texture;
        return texture;
    }

    private Texture GetFootprintTexture(EntityUid gridUid, Vector2i indices, int variant, int mask, SnowTrampleComponent trample, SnowTrampleTile tile)
    {
        var key = (gridUid, indices);
        _usedFootprintTextures.Add(indices);

        if (_footprintTextures.TryGetValue(key, out var entry) &&
            entry.Version == tile.Version &&
            entry.Mask == mask &&
            entry.NeighbourVersion == NeighbourVersion(trample, indices) &&
            entry.Texture != null)
        {
            return entry.Texture;
        }

        const int cells = SnowCoverMath.Cells;
        const int window = cells + 2;

        for (var wy = 0; wy < window; wy++)
        {
            for (var wx = 0; wx < window; wx++)
            {
                var gx = indices.X * cells + wx - 1;
                var gy = indices.Y * cells + wy - 1;
                var ti = new Vector2i(SnowCoverMath.FloorDiv(gx, cells), SnowCoverMath.FloorDiv(gy, cells));
                var value = 0f;
                if (trample.Tiles.TryGetValue(ti, out var other))
                    value = other.Cells[(gy - ti.Y * cells) * cells + (gx - ti.X * cells)] / 255f;
                _cellWindow[wy * window + wx] = value;
            }
        }

        var size = _tilePixels;
        var sheetPixels = _sheet!.GetPixelSpan();
        var sheetWidth = _sheet!.Width;
        using var image = new Image<Rgba32>(size, size);
        var pixelStep = 2f * cells / size;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var px = x + 0.5f;
                var py = y + 0.5f;
                var source = sheetPixels[y * sheetWidth + variant * size + x];

                var u = px / size * cells - 0.5f;
                var v = (size - py) / size * cells - 0.5f;
                var packed = SampleWindow(u, v);

                var alpha = EdgeFade(mask, px, py, size);
                float r = source.R, g = source.G, b = source.B;

                if (packed > 0.01f)
                {
                    var grain = SnowCoverMath.Hash(indices.X * size + x, indices.Y * size + y) - 0.5f;
                    var cut = Math.Clamp(packed * 1.15f + grain * 0.25f * packed, 0f, 1f);

                    alpha *= 1f - 0.95f * cut;
                    var shade = 1f - 0.22f * cut;

                    var around = MathF.Max(
                        MathF.Max(SampleWindow(u + pixelStep, v), SampleWindow(u - pixelStep, v)),
                        MathF.Max(SampleWindow(u, v + pixelStep), SampleWindow(u, v - pixelStep)));
                    shade += Math.Clamp((around - packed) * 1.5f, 0f, 1f) * 0.15f * (1f - cut);

                    r = MathF.Min(255f, r * shade);
                    g = MathF.Min(255f, g * shade);
                    b = MathF.Min(255f, b * shade);
                }

                image[x, y] = new Rgba32((byte) r, (byte) g, (byte) b, (byte) (source.A * alpha));
            }
        }

        if (entry.Texture != null)
        {
            entry.Texture.SetSubImage(Vector2i.Zero, image);
        }
        else
        {
            entry.Texture = _clyde.LoadTextureFromImage(image, $"snow-prints-{indices}");
        }

        entry.Version = tile.Version;
        entry.Mask = mask;
        entry.NeighbourVersion = NeighbourVersion(trample, indices);
        _footprintTextures[key] = entry;
        return entry.Texture!;
    }

    private static int NeighbourVersion(SnowTrampleComponent trample, Vector2i indices)
    {
        var total = 0;
        foreach (var (offset, _) in Neighbours)
        {
            if (trample.Tiles.TryGetValue(indices + offset, out var other))
                total = unchecked(total * 31 + other.Version);
        }

        return total;
    }

    private float SampleWindow(float u, float v)
    {
        const int window = SnowCoverMath.Cells + 2;
        u = Math.Clamp(u + 1f, 0f, window - 1.001f);
        v = Math.Clamp(v + 1f, 0f, window - 1.001f);
        var x0 = (int) u;
        var y0 = (int) v;
        var fx = u - x0;
        var fy = v - y0;

        var a = _cellWindow[y0 * window + x0];
        var b = _cellWindow[y0 * window + x0 + 1];
        var c = _cellWindow[(y0 + 1) * window + x0];
        var d = _cellWindow[(y0 + 1) * window + x0 + 1];

        var bottom = a + (b - a) * fx;
        var top = c + (d - c) * fx;
        return bottom + (top - bottom) * fy;
    }

    private struct FootprintTexture
    {
        public OwnedTexture? Texture;
        public int Version;
        public int Mask;
        public int NeighbourVersion;
    }

    private readonly record struct SnowDraw(Box2 Box, Texture Texture, Color Color);

    private sealed class TileCache
    {
        public readonly HashSet<Vector2i> Open = new();
        public readonly HashSet<Vector2i> Covered = new();
        public readonly HashSet<Vector2i> Solid = new();
        public readonly Dictionary<Vector2i, float> Alpha = new();
        public readonly List<SnowDraw> Draws = new();
        public Box2 Bounds;
        public TimeSpan Built;
        public float DrawDepth = -1f;
        public int DrawVersion = -1;
    }
}

public sealed class SnowfallRenderer
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly ProtoId<ShaderPrototype> SnowfallShader = "Snowfall";
    private const float Wrap = 8192f;

    private readonly ShaderInstance _shader;
    private readonly Dictionary<string, SnowfallState> _states = new();

    private sealed class SnowfallState
    {
        public float Fall;
        public float Drift;
        public TimeSpan Last;
        public float Seed;
    }

    public SnowfallRenderer()
    {
        IoCManager.InjectDependencies(this);
        _shader = _proto.Index(SnowfallShader).InstanceUnique();
    }

    public void Draw(in OverlayDrawArgs args, WeatherPrototype proto, SnowfallLook look, float alpha, Texture mask, Matrix3x2 invMatrix)
    {
        if (args.Viewport.Eye is not { } eye)
            return;

        var now = _timing.RealTime;
        if (!_states.TryGetValue(proto.ID, out var state))
        {
            state = new SnowfallState { Last = now, Seed = _states.Count * 13.7f };
            _states[proto.ID] = state;
        }

        var dt = (float) Math.Clamp((now - state.Last).TotalSeconds, 0, 0.1);
        state.Last = now;

        var t = (float) now.TotalSeconds + state.Seed;
        var wave = 0.6f * MathF.Sin(t * 0.13f) + 0.4f * MathF.Sin(t * 0.31f + 1.7f);
        var direction = look.Wind >= 0f ? 1f : -1f;
        var gust = MathF.Max(wave, 0f);
        var wind = look.Wind + direction * look.Gust * gust * gust - direction * 0.15f * look.Gust * MathF.Max(-wave, 0f);

        state.Drift = (state.Drift + wind * dt) % Wrap;
        state.Fall = (state.Fall + look.FallSpeed * (1f + 0.15f * gust) * dt) % Wrap;

        var eyePos = eye.Position.Position;
        var size = args.Viewport.Size;
        var eyePixel = Vector2.Transform(eyePos, invMatrix);
        var pixelsPerTile = EyeManager.PixelsPerMeter * args.Viewport.RenderScale.X / eye.Zoom.X;

        _shader.SetParameter("MASK_TEXTURE", mask);
        _shader.SetParameter("eyePos", eyePos);
        _shader.SetParameter("eyePixel", new Vector2(eyePixel.X, size.Y - eyePixel.Y));
        _shader.SetParameter("pixelsPerTile", pixelsPerTile);
        _shader.SetParameter("intensity", alpha);
        _shader.SetParameter("fall", state.Fall);
        _shader.SetParameter("drift", state.Drift);
        _shader.SetParameter("density", look.Density);
        _shader.SetParameter("flakeSize", look.FlakeSize);
        _shader.SetParameter("sway", look.Sway);
        _shader.SetParameter("haze", look.Haze);
        _shader.SetParameter("landing", look.Landing);
        _shader.SetParameter("clearRadius", proto.VisibilityClearRadius);
        _shader.SetParameter("clearBuffer", proto.VisibilityClearBuffer);

        var handle = args.WorldHandle;
        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
