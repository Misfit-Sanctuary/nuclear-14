using System.Numerics;
using Content.Shared._Misfits.Fire;
using Content.Shared._Misfits.Weapons.Ranged.Flamer;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.Weapons.Ranged.Flamer;

public sealed class FlamerLineSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public void ShootLine(EntityUid user, EntityUid gunUid, FlamerShot shot, EntityCoordinates fromCoordinates, EntityCoordinates? targetCoordinates)
    {
        if (targetCoordinates == null)
            return;

        if (_transform.GetGrid(fromCoordinates) is not { } gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid))
        {
            return;
        }

        var fromMap = fromCoordinates.ToMap(EntityManager, _transform);
        var targetMap = targetCoordinates.Value.ToMap(EntityManager, _transform);

        if (fromMap.MapId != targetMap.MapId)
            return;

        var start = _mapSystem.TileIndicesFor(gridUid, grid, fromMap);
        var target = _mapSystem.TileIndicesFor(gridUid, grid, targetMap);

        var tiles = GetLine(start, target, shot.Range);
        if (tiles.Count == 0)
            return;

        var skippedFirst = false;
        foreach (var tile in tiles)
        {
            if (!skippedFirst)
            {
                skippedFirst = true;
                continue;
            }

            if (!_mapSystem.TryGetTileRef(gridUid, grid, tile, out var tileRef) || tileRef.Tile.IsEmpty)
                break;

            var coords = _mapSystem.GridTileToLocal(gridUid, grid, tile);
            if (tileRef.IsBlockedTurf(false, _lookup))
                break;

            SpawnOrRefreshTileFire(shot.FireTilePrototype, coords);
        }
    }

    public void SpawnDiamond(EntProtoId fireTilePrototype, EntityCoordinates center, int range)
    {
        if (_transform.GetGrid(center) is not { } gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid))
        {
            return;
        }

        var origin = _mapSystem.TileIndicesFor(gridUid, grid, center);
        for (var x = -range; x <= range; x++)
        {
            for (var y = -range; y <= range; y++)
            {
                if (Math.Abs(x) + Math.Abs(y) > range)
                    continue;

                var tile = origin + new Vector2i(x, y);
                if (!_mapSystem.TryGetTileRef(gridUid, grid, tile, out var tileRef) || tileRef.Tile.IsEmpty)
                    continue;

                SpawnOrRefreshTileFire(fireTilePrototype, _mapSystem.GridTileToLocal(gridUid, grid, tile));
            }
        }
    }

    private void SpawnOrRefreshTileFire(EntProtoId fireTilePrototype, EntityCoordinates coords)
    {
        foreach (var uid in coords.GetEntitiesInTile(LookupFlags.Static | LookupFlags.Dynamic, _lookup))
        {
            if (!HasComp<TileFireComponent>(uid))
                continue;

            QueueDel(uid);
        }

        Spawn(fireTilePrototype, coords);
    }

    private static List<Vector2i> GetLine(Vector2i start, Vector2i end, int maxSteps)
    {
        var points = new List<Vector2i>();

        var x0 = start.X;
        var y0 = start.Y;
        var x1 = end.X;
        var y1 = end.Y;

        var dx = Math.Abs(x1 - x0);
        var sx = x0 < x1 ? 1 : -1;
        var dy = -Math.Abs(y1 - y0);
        var sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;
        var steps = 0;

        while (true)
        {
            points.Add(new Vector2i(x0, y0));
            if ((x0 == x1 && y0 == y1) || steps >= maxSteps)
                break;

            var e2 = 2 * error;
            if (e2 >= dy)
            {
                error += dy;
                x0 += sx;
            }

            if (e2 <= dx)
            {
                error += dx;
                y0 += sy;
            }

            steps++;
        }

        return points;
    }
}
