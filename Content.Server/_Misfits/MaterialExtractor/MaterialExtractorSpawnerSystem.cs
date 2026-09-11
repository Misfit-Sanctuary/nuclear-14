using Content.Server.GameTicking;
using Content.Server.Maps;
using Content.Server._Misfits.Expeditions;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._Misfits.MaterialExtractor;

/// <summary>
/// Creates the single round-scoped Seismic Material Extractor on Wendover.
/// Placement is deliberately data-independent: it examines the loaded grid rather
/// than relying on a serialized map marker.
/// </summary>
public sealed partial class MaterialExtractorSpawnerSystem : EntitySystem
{
    private const string WendoverGameMap = "Wendover";
    private const string ExtractorPrototype = "N14SeismicMaterialExtractor";
    private static readonly Vector2i[] AdjacentOffsets =
    [
        new(-1, -1), new(0, -1), new(1, -1), new(-1, 0),
        new(1, 0), new(-1, 1), new(0, 1), new(1, 1),
    ];
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private N14ExpeditionEntranceSpawnerSystem _expeditionEntrances = default!;

    private readonly HashSet<MapId> _wendoverMaps = [];
    private ISawmill _log = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PostGameMapLoad>(OnPostGameMapLoad);
        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        _log = Logger.GetSawmill("material_extractor");
    }

    private void OnPostGameMapLoad(PostGameMapLoad ev)
    {
        if (ev.GameMap.ID == WendoverGameMap)
            _wendoverMaps.Add(ev.Map);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _wendoverMaps.Clear();
    }

    private void OnRoundStarted(RoundStartedEvent ev)
    {
        foreach (var mapId in _wendoverMaps)
            TrySpawnForMap(mapId);
    }

    private void TrySpawnForMap(MapId mapId)
    {
        if (!TryGetWendoverGrid(mapId, out var gridUid, out var grid))
        {
            _log.Warning($"Unable to find a grid on Wendover map {mapId}; skipping material extractor.");
            return;
        }

        // No terrain gate: pick a real boulder and put the landmark beside it.
        var rockCandidates = new List<Vector2i>();
        var transforms = EntityQueryEnumerator<TransformComponent>();
        while (transforms.MoveNext(out var uid, out var xform))
        {
            if (xform.GridUid != gridUid || MetaData(uid).EntityPrototype?.ID is not ("FloraRockSolid01" or "FloraRockSolid02" or "FloraRockSolid03"))
                continue;

            rockCandidates.Add(_map.CoordinatesToTile(gridUid, grid, xform.Coordinates));
        }

        if (rockCandidates.Count == 0)
        {
            _log.Warning($"No solid boulders were found on Wendover map {mapId}; skipping material extractor.");
            return;
        }

        var rockTile = rockCandidates[_random.Next(rockCandidates.Count)];
        var tile = rockTile + AdjacentOffsets[_random.Next(AdjacentOffsets.Length)];
        var extractor = Spawn(ExtractorPrototype, _map.GridTileToLocal(gridUid, grid, tile));
        // Round-start landmarks are spawned directly rather than placed by a player,
        // so anchor them here to satisfy the portable-generator start path.
        _transform.AnchorEntity(extractor, Transform(extractor));
        _log.Info($"Spawned the round's material extractor at {tile} beside boulder {rockTile} on Wendover map {mapId}.");

        // The extractor is visible proof that this established round-start path
        // ran for Wendover. Enforce expedition entrances from the same path so
        // every Wendover/faction tacmap has usable expedition GPS targets too.
        _expeditionEntrances.EnsureEntrancesForMap(mapId);
    }

    private bool TryGetWendoverGrid(MapId mapId, out EntityUid gridUid, out MapGridComponent grid)
    {
        var query = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var candidate, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            gridUid = uid;
            grid = candidate;
            return true;
        }

        gridUid = default;
        grid = default!;
        return false;
    }

}
