using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Server.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Toolshed;

namespace Content.Server._Misfits.Areas;

[ToolshedCommand, AdminCommand(AdminFlags.Host)]
public sealed class AreasCommand : ToolshedCommand
{
    private MapSystem? _map;

    [CommandImplementation("save")]
    public void Save()
    {
        _map ??= GetSys<MapSystem>();

        var gridQuery = GetEntityQuery<MapGridComponent>();
        var query = EntityManager.AllEntityQueryEnumerator<AreaComponent, MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var metaData, out var xform))
        {
            if (xform.GridUid is not { } gridId ||
                !gridQuery.TryComp(gridId, out var grid) ||
                metaData.EntityPrototype is not { } prototype)
            {
                continue;
            }

            var areaGrid = EnsureComp<AreaGridComponent>(gridId);
            var indices = _map.TileIndicesFor(gridId, grid, xform.Coordinates);
            areaGrid.Areas[indices] = prototype.ID;
            QDel(uid);
        }
    }

    [CommandImplementation("load")]
    public void Load()
    {
        _map ??= GetSys<MapSystem>();

        var query = EntityManager.AllEntityQueryEnumerator<AreaGridComponent, MapGridComponent>();
        while (query.MoveNext(out var uid, out var areaGrid, out var mapGrid))
        {
            foreach (var (position, protoId) in areaGrid.Areas)
            {
                var coordinates = _map.GridTileToLocal(uid, mapGrid, position);
                Spawn(protoId, coordinates);
            }
        }
    }
}
