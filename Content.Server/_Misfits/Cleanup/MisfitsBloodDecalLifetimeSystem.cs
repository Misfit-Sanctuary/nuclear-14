using Content.Server.Decals;
using Content.Server.Spawners.Components;
using Content.Shared.Light.Components;
using Content.Shared.Weather;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Cleanup;

/// <summary>
/// Removes generated gore decals after a bounded lifetime. Grid decals are map data rather than
/// entities, so they cannot use <c>TimedDespawnComponent</c>. The tracker updates only opted-in
/// blood spawners and fades in ten-second steps to avoid per-frame decal/network churn.
/// </summary>
public sealed class MisfitsBloodDecalLifetimeSystem : EntitySystem
{
    private static readonly ProtoId<WeatherPrototype> RainProtoId = "Rain";
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(10);

    [Dependency] private readonly DecalSystem _decals = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedWeatherSystem _weather = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly Dictionary<(EntityUid GridUid, uint DecalId), TrackedDecal> _tracked = new();
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MisfitsBloodDecalLifetimeComponent, RandomDecalSpawnedEvent>(OnDecalSpawned);
    }

    private void OnDecalSpawned(Entity<MisfitsBloodDecalLifetimeComponent> ent, ref RandomDecalSpawnedEvent args)
    {
        var lifetime = TimeSpan.FromSeconds(Math.Max(0f, ent.Comp.Lifetime));
        if (lifetime <= TimeSpan.Zero)
            return;

        var fadeDuration = TimeSpan.FromSeconds(Math.Clamp(ent.Comp.FadeDuration, 0f, ent.Comp.Lifetime));
        var now = _timing.CurTime;
        _tracked[(args.GridUid, args.DecalId)] = new TrackedDecal(
            args.Coordinates,
            args.Color,
            now + lifetime - fadeDuration,
            now + lifetime,
            TimeSpan.FromSeconds(Math.Max(0f, ent.Comp.RainFadeDuration)));
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        if (now < _nextUpdate)
            return;

        _nextUpdate = now + UpdateInterval;
        var remove = new List<(EntityUid GridUid, uint DecalId)>();

        foreach (var (key, decal) in _tracked)
        {
            if (IsExposedToRain(key.GridUid, decal.Coordinates) && decal.RainFadeDuration > TimeSpan.Zero)
            {
                var rainExpiry = now + decal.RainFadeDuration;
                if (rainExpiry < decal.ExpiresAt)
                {
                    decal.FadeStart = now;
                    decal.ExpiresAt = rainExpiry;
                }
            }

            if (now >= decal.ExpiresAt)
            {
                _decals.RemoveDecal(key.GridUid, key.DecalId);
                remove.Add(key);
                continue;
            }

            if (now < decal.FadeStart)
                continue;

            var duration = decal.ExpiresAt - decal.FadeStart;
            var alpha = duration <= TimeSpan.Zero
                ? 0f
                : Math.Clamp((float) ((decal.ExpiresAt - now) / duration), 0f, 1f);

            // A false result means a janitor, cleaner, map deletion, or another system already removed it.
            if (!_decals.SetDecalColor(key.GridUid, key.DecalId, decal.Color.WithAlpha(decal.Color.A * alpha)))
                remove.Add(key);
        }

        foreach (var key in remove)
            _tracked.Remove(key);
    }

    private bool IsExposedToRain(EntityUid gridUid, EntityCoordinates coordinates)
    {
        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        var mapUid = Transform(gridUid).MapUid;
        if (mapUid == null || !TryComp<WeatherComponent>(mapUid, out var weather) ||
            !weather.Weather.TryGetValue(RainProtoId, out var rain) ||
            (rain.State != WeatherState.Starting && rain.State != WeatherState.Running))
        {
            return false;
        }

        TryComp<RoofComponent>(gridUid, out var roof);
        var tile = _map.GetTileRef(gridUid, grid, coordinates);
        return _weather.CanWeatherAffect(gridUid, grid, tile, roof);
    }

    private sealed class TrackedDecal(
        EntityCoordinates coordinates,
        Color color,
        TimeSpan fadeStart,
        TimeSpan expiresAt,
        TimeSpan rainFadeDuration)
    {
        public EntityCoordinates Coordinates { get; } = coordinates;
        public Color Color { get; } = color;
        public TimeSpan FadeStart { get; set; } = fadeStart;
        public TimeSpan ExpiresAt { get; set; } = expiresAt;
        public TimeSpan RainFadeDuration { get; } = rainFadeDuration;
    }
}
