using System.Numerics;
using Content.Shared.Light.Components;
using Content.Shared.Weather;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Client.Overlays;

public sealed partial class StencilOverlay
{
    private List<Entity<MapGridComponent>> _grids = new();

    private void DrawWeather(in OverlayDrawArgs args, WeatherPrototype weatherProto, float alpha, Matrix3x2 invMatrix)
    {
        if (weatherProto.Sprite == null)
            return;
        var worldHandle = args.WorldHandle;
        var mapId = args.MapId;
        var worldAABB = args.WorldAABB;
        var worldBounds = args.WorldBounds;
        var position = args.Viewport.Eye?.Position.Position ?? Vector2.Zero;
        var viewport = args.Viewport;
        var renderScale = viewport.RenderScale.X;
        var viewportSize = viewport.Size;
        var hasEye = viewport.Eye != null;
        var eyePosition = viewport.Eye?.Position.Position ?? Vector2.Zero;
        var eyeZoom = viewport.Eye?.Zoom ?? Vector2.One;

        // #Misfits Fix - Throttle stencil mask rebuild to 4 Hz. The mask is baked in
        // screen space, so it is only reusable while the view is completely static;
        // invMatrix covers eye position, zoom and rotation. Otherwise the timer picks
        // up tile/roof changes. Per-frame rebuilds were the #1 weather rendering cost.
        const float StencilInterval = 0.25f;
        _stencilAccum += (float) _timing.FrameTime.TotalSeconds;
        var rebuildStencil = _stencilAccum >= StencilInterval || invMatrix != _lastStencilMatrix;

        // Cut out the irrelevant bits via stencil
        // This is why we don't just use parallax; we might want specific tiles to get drawn over
        // particularly for planet maps or stations.
        if (rebuildStencil)
        {
            _stencilAccum = 0f;
            _lastStencilMatrix = invMatrix;

        worldHandle.RenderInRenderTarget(_blep!, () =>
        {
            var xformQuery = _entManager.GetEntityQuery<TransformComponent>();
            _grids.Clear();

            // idk if this is safe to cache in a field and clear sloth help
            _entManager.System<SharedMapSystem>().FindGridsIntersecting(mapId, worldAABB, ref _grids);

            foreach (var grid in _grids)
            {
                var matrix = _transform.GetWorldMatrix(grid, xformQuery);
                var matty =  Matrix3x2.Multiply(matrix, invMatrix);
                worldHandle.SetTransform(matty);
                _entManager.TryGetComponent(grid.Owner, out RoofComponent? roofComp);

                foreach (var tile in _entManager.System<SharedMapSystem>().GetTilesIntersecting(grid.Owner, grid.Comp, worldAABB))
                {
                    // Ignored tiles for stencil
                    if (_weather.CanWeatherAffect(grid.Owner, grid, tile, roofComp))
                    {
                        continue;
                    }

                    var gridTile = new Box2(tile.GridIndices * grid.Comp.TileSize,
                        (tile.GridIndices + Vector2i.One) * grid.Comp.TileSize);

                    worldHandle.DrawRect(gridTile, Color.White);
                }
            }
        }, Color.Transparent);

        } // #Misfits Fix - end stencil rebuild throttle block

        worldHandle.SetTransform(Matrix3x2.Identity);

        if (weatherProto.Snowfall is { } snowfall)
        {
            _snowfall.Draw(args, weatherProto, snowfall, alpha, _blep!.Texture, invMatrix);
            return;
        }

        // #Cythisiax Added - rain weathers draw with the rain shader instead of the scrolling sprite
        if (weatherProto.Rainfall is { } rainfall)
        {
            _rainfall.Draw(args, weatherProto, rainfall, alpha, _blep!.Texture, invMatrix);
            return;
        }

        var curTime = _timing.RealTime;
        var sprite = _sprite.GetFrame(weatherProto.Sprite, curTime);

        _weatherDrawShader.SetParameter("MASK_TEXTURE", _blep!.Texture);

        if (weatherProto.VisibilityClearRadius > 0f && hasEye)
        {
            var length = eyeZoom.X;
            var pixelCenter = Vector2.Transform(eyePosition, invMatrix);
            var pixelMaxRange = weatherProto.VisibilityClearRadius * renderScale / length * EyeManager.PixelsPerMeter;
            var pixelBufferRange = MathF.Max(1f, weatherProto.VisibilityClearBuffer * renderScale / length * EyeManager.PixelsPerMeter);
            var pixelMinRange = MathF.Max(0f, pixelMaxRange - pixelBufferRange);

            _weatherDrawShader.SetParameter("position", new Vector2(pixelCenter.X, viewportSize.Y - pixelCenter.Y));
            _weatherDrawShader.SetParameter("maxRange", pixelMaxRange);
            _weatherDrawShader.SetParameter("minRange", pixelMinRange);
            _weatherDrawShader.SetParameter("bufferRange", pixelBufferRange);
        }
        else
        {
            _weatherDrawShader.SetParameter("maxRange", 0f);
            _weatherDrawShader.SetParameter("minRange", 0f);
            _weatherDrawShader.SetParameter("bufferRange", 1f);
        }

        _weatherDrawShader.SetParameter("gradient", 0.80f);
        worldHandle.UseShader(_weatherDrawShader);

        _parallax.DrawParallax(worldHandle, worldAABB, sprite, curTime, position, Vector2.Zero, modulate: (weatherProto.Color ?? Color.White).WithAlpha(alpha));

        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(null);
    }
}
