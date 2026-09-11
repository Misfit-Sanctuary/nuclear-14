using Content.Server.GameTicking;
using Content.Server.Maps;
using Content.Shared.GameTicking;
using Content.Shared._Misfits.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._Misfits.Expeditions;

/// <summary>
/// Places round-scoped, mystery expedition entrances on every loaded game map.
/// Each entrance is placed beside a distinct authored tree stump, using the
/// same landmark-adjacent approach as the seismic material extractor.
/// </summary>
public sealed partial class N14ExpeditionEntranceSpawnerSystem : EntitySystem
{
    private const int EntrancesPerMap = 3;
    private static readonly HashSet<string> StumpPrototypes =
    [
        "FloraTreeStump",
        "FloraTreeStumpConifer",
    ];

    private static readonly string[] EntrancePrototypes =
    [
        "N14ExpeditionEntranceSquareLadder",
        "N14ExpeditionEntranceManhole",
        "N14ExpeditionEntranceBunker",
        "N14ExpeditionEntranceBoardedWell",
    ];

    private static readonly Vector2i[] AdjacentOffsets =
    [
        new(-1, -1), new(0, -1), new(1, -1), new(-1, 0),
        new(1, 0), new(-1, 1), new(0, 1), new(1, 1),
    ];

    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly HashSet<MapId> _loadedGameMaps = [];
    private readonly HashSet<MapId> _spawnedMaps = [];
    private bool _enabled;
    private ISawmill _log = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PostGameMapLoad>(OnPostGameMapLoad);
        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        Subs.CVar(_config, ExpeditionCVars.Enabled, enabled => _enabled = enabled, true);
        _log = Logger.GetSawmill("expedition_entrances");
    }

    private void OnPostGameMapLoad(PostGameMapLoad ev)
    {
        _loadedGameMaps.Add(ev.Map);

        // Some game maps finish loading after RoundStartedEvent. Handle both
        // event orders so every round map receives entrances exactly once.
        if (_gameTicker.RunLevel == GameRunLevel.InRound)
            EnsureEntrancesForMap(ev.Map);
    }

    private void OnRoundStarted(RoundStartedEvent ev)
    {
        foreach (var mapId in _loadedGameMaps)
            EnsureEntrancesForMap(mapId);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _loadedGameMaps.Clear();
        _spawnedMaps.Clear();
    }

    /// <summary>
    /// Ensures a loaded game map has its round-scoped expedition entrances.
    /// Public so other proven round landmark spawners can enforce the same
    /// invariant without duplicating placement or creating extra entrances.
    /// </summary>
    public void EnsureEntrancesForMap(MapId mapId)
    {
        if (!_enabled)
            return;

        if (_spawnedMaps.Contains(mapId))
            return;

        if (TrySpawnForMap(mapId))
            _spawnedMaps.Add(mapId);
    }

    private bool TrySpawnForMap(MapId mapId)
    {
        if (!TryGetMapGrid(mapId, out var gridUid, out var grid))
        {
            _log.Warning($"No grid found on game map {mapId}; skipping expedition entrances.");
            return false;
        }

        var stumps = GetStumps(gridUid);
        _random.Shuffle(stumps);
        // A landmark may anchor only one entrance. The cap keeps small maps from
        // reusing a stump solely to force the normal three-entrance target.
        var desiredEntrances = Math.Min(EntrancesPerMap, stumps.Count);

        var usedStumps = new HashSet<EntityUid>();
        var placed = 0;
        foreach (var stump in stumps)
        {
            if (placed >= desiredEntrances)
                break;

            if (!usedStumps.Add(stump))
                continue;

            // Deliberately identical placement model to the seismic extractor:
            // pick a real landmark and put the entrance on a random adjacent tile.
            // Stump identity is the only placement requirement.
            var stumpTile = _map.CoordinatesToTile(gridUid, grid, Transform(stump).Coordinates);
            var tile = stumpTile + AdjacentOffsets[_random.Next(AdjacentOffsets.Length)];
            var entrance = Spawn(_random.Pick(EntrancePrototypes), _map.GridTileToLocal(gridUid, grid, tile));
            _transform.AnchorEntity(entrance, Transform(entrance));
            placed++;
            _log.Info($"Spawned expedition entrance {MetaData(entrance).EntityPrototype?.ID} at {tile} beside {ToPrettyString(stump)} on game map {mapId}.");
        }

        _log.Info($"Expedition entrance placement on game map {mapId}: found {stumps.Count} stump(s), placed {placed}/{desiredEntrances} entrance(s).");
        return placed > 0;
    }

    private List<EntityUid> GetStumps(EntityUid gridUid)
    {
        var stumps = new List<EntityUid>();
        var query = EntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var uid, out var xform))
        {
            if (xform.GridUid != gridUid || MetaData(uid).EntityPrototype?.ID is not { } prototype)
                continue;

            if (StumpPrototypes.Contains(prototype))
                stumps.Add(uid);
        }
        return stumps;
    }

    private bool TryGetMapGrid(MapId mapId, out EntityUid gridUid, out MapGridComponent grid)
    {
        // Match the material extractor exactly: the first grid belonging to
        // the requested game map is the surface grid to use.
        var query = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var candidate, out var candidateGrid, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            gridUid = candidate;
            grid = candidateGrid;
            return true;
        }

        gridUid = default;
        grid = default!;
        return false;
    }
}
