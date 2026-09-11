using System.Diagnostics.CodeAnalysis;
using Content.Shared.Tag;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.Areas;

public sealed class AreaSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TagSystem _tag = default!;

    private EntityQuery<MapGridComponent> _mapGridQuery;
    private EntityQuery<AreaGridComponent> _areaGridQuery;

    public override void Initialize()
    {
        base.Initialize();

        _mapGridQuery = GetEntityQuery<MapGridComponent>();
        _areaGridQuery = GetEntityQuery<AreaGridComponent>();

        SubscribeLocalEvent<AreaGridComponent, MapInitEvent>(OnAreaGridMapInit);
    }

    private void OnAreaGridMapInit(Entity<AreaGridComponent> ent, ref MapInitEvent args)
    {
        foreach (var areaProto in ent.Comp.Areas.Values.Distinct())
        {
            EnsureAreaEntityExists(ent.Comp, areaProto);
        }
    }

    private void EnsureAreaEntityExists(AreaGridComponent areaGrid, EntProtoId<AreaComponent> area)
    {
        if (areaGrid.AreaEntities.ContainsKey(area))
            return;

        areaGrid.AreaEntities[area] = Spawn(area, MapCoordinates.Nullspace);
    }

    public bool TryGetArea(
        EntityCoordinates coordinates,
        [NotNullWhen(true)] out Entity<AreaComponent>? area,
        [NotNullWhen(true)] out EntProtoId<AreaComponent>? areaId)
    {
        area = null;
        areaId = null;

        if (_transform.GetGrid(coordinates) is not { } gridId ||
            !_mapGridQuery.TryComp(gridId, out var grid) ||
            !_areaGridQuery.TryComp(gridId, out var areaGrid))
        {
            return false;
        }

        var indices = _map.TileIndicesFor(gridId, grid, coordinates);
        if (!areaGrid.Areas.TryGetValue(indices, out var proto))
            return false;

        if (!areaGrid.AreaEntities.TryGetValue(proto, out var areaEnt) ||
            !TryComp(areaEnt, out AreaComponent? areaComp))
        {
            return false;
        }

        area = (areaEnt, areaComp);
        areaId = proto;
        return true;
    }

    public bool IsInArea(EntityUid uid, EntProtoId<AreaComponent> area)
    {
        return TryGetArea(Transform(uid).Coordinates, out _, out var found) && found == area;
    }

    public bool HasAreaTag(EntityUid uid, ProtoId<TagPrototype> tag)
    {
        return TryGetArea(Transform(uid).Coordinates, out var area, out _) && _tag.HasTag(area.Value.Owner, tag);
    }
}
