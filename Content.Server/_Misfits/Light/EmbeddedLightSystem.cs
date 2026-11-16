// #Cythisiax Added - RT 295 disables shadow-casting lights that sit inside an occluder (wall torches, signs,
// wall lights on a wall or door edge). On map init, nudge such lights just outside the occluder, or stop them
// casting shadows when there is no free side.
using System.Numerics;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Misfits.Light;

public sealed class EmbeddedLightSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedPointLightSystem _lights = default!;

    private const float Tolerance = 0.001f;
    private const float EdgeOffset = 0.51f;
    private const int SearchRadius = 2;

    private static readonly Vector2[] Directions =
    [
        new(0, -1),
        new(0, 1),
        new(-1, 0),
        new(1, 0),
    ];

    private EntityQuery<OccluderComponent> _occluderQuery;
    private EntityQuery<TransformComponent> _xformQuery;

    public override void Initialize()
    {
        base.Initialize();

        _occluderQuery = GetEntityQuery<OccluderComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();

        SubscribeLocalEvent<PointLightComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<PointLightComponent> ent, ref MapInitEvent args)
    {
        var light = ent.Comp;
        if (!light.CastShadows)
            return;

        var xform = Transform(ent);
        if (xform.GridUid is not { } gridUid || xform.ParentUid != gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var rotation = xform.LocalRotation;
        var origin = xform.LocalPosition;

        if (!IsEmbedded(ent, gridUid, grid, origin + rotation.RotateVec(light.Offset)))
            return;

        if (light.Offset != Vector2.Zero)
        {
            var pulled = light.Offset * 0.98f;
            if (!IsEmbedded(ent, gridUid, grid, origin + rotation.RotateVec(pulled)))
            {
                _lights.SetOffset(ent, pulled, light);
                return;
            }
        }

        foreach (var dir in Directions)
        {
            var offset = dir * EdgeOffset;
            if (IsEmbedded(ent, gridUid, grid, origin + rotation.RotateVec(offset)))
                continue;

            _lights.SetOffset(ent, offset, light);
            return;
        }

        _lights.SetCastShadows(ent, false, light);
    }

    private bool IsEmbedded(EntityUid self, EntityUid gridUid, MapGridComponent grid, Vector2 point)
    {
        var tile = _map.LocalToTile(gridUid, grid, new EntityCoordinates(gridUid, point));

        for (var x = -SearchRadius; x <= SearchRadius; x++)
        {
            for (var y = -SearchRadius; y <= SearchRadius; y++)
            {
                var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, tile + new Vector2i(x, y));
                while (anchored.MoveNext(out var uid))
                {
                    if (uid == self || !_occluderQuery.TryComp(uid, out var occluder) || !occluder.Enabled)
                        continue;

                    if (!_xformQuery.TryComp(uid, out var occXform) || occXform.ParentUid != gridUid)
                        continue;

                    var local = (-occXform.LocalRotation).RotateVec(point - occXform.LocalPosition);
                    if (PointInPolygon(occluder.Polygon, local))
                        return true;
                }
            }
        }

        return false;
    }

    private static bool PointInPolygon(ReadOnlySpan<Vector2> polygon, Vector2 point)
    {
        if (polygon.Length < 3)
            return false;

        var sign = 0;
        for (var i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Length];
            var edge = b - a;
            var length = edge.Length();
            if (length <= 0f)
                continue;

            var cross = (edge.X * (point.Y - a.Y) - edge.Y * (point.X - a.X)) / length;
            if (MathF.Abs(cross) <= Tolerance)
                continue;

            var s = cross > 0 ? 1 : -1;
            if (sign == 0)
                sign = s;
            else if (s != sign)
                return false;
        }

        return true;
    }
}
