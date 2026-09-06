// #Misfits Change - Custom map viewer control with zoom, pan, and player position marker
using System.Collections.Generic;
using System.Numerics;
using Content.Shared._Misfits.WastelandMap;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._Misfits.WastelandMap;

/// <summary>
/// A control that displays a texture with mouse-wheel zoom and click-drag pan,
/// and overlays a dot at the local player's current position on the map.
/// </summary>
public sealed class MapViewerControl : Control
{
    public enum AnnotationMode : byte
    {
        None,
        Marker,
        Box,
        Draw,
        Erase,
    }

    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly IResourceCache _resourceCache = default!;

    private readonly Font _blipLabelFont;

    public event Action<WastelandMapAnnotation>? OnAddAnnotation;
    public event Action<int>? OnRemoveAnnotation;
    public event Action? OnClearAnnotations;

    public MapViewerControl()
    {
        IoCManager.InjectDependencies(this);
        // #Misfits Tweak - Reduced font size from 12 to 8 so map labels are legible and don't crowd the map
        _blipLabelFont = new VectorFont(_resourceCache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Bold.ttf"), 8);
        // Must be Stop to receive mouse wheel, click, and move events.
        MouseFilter = MouseFilterMode.Stop;
        RectClipContent = true;
    }

    private Texture? _texture;
    private Box2 _worldBounds;
    private WastelandMapTrackedBlip[] _trackedBlips = [];
    private WastelandMapAnnotation[] _annotations = [];
    private AnnotationMode _annotationMode;
    private string _pendingAnnotationText = string.Empty;
    private Vector2? _annotationDragStartUv;
    private Vector2? _annotationDragCurrentUv;

    // Free-draw state
    private Color _currentColor = new Color(0.95f, 0.50f, 0.15f, 1f); // default orange
    private float _currentStrokeWidth = 3f;
    private readonly List<Vector2> _freeDrawUvPoints = new();
    private bool _freeDrawActive;
    private const int MaxStrokeUvPoints = 256;

    // Erase-mode hover
    private int _hoveredAnnotationIndex = -1;

    // Zoom: 1.0 = fit to window. Values > 1 are zoomed in.
    private float _zoom = 1f;

    // Pan offset in UI pixels from the centered position.
    private Vector2 _pan = Vector2.Zero;

    private bool _dragging;
    private Vector2 _dragStart;
    private Vector2 _panAtDragStart;

    private const float ZoomStep = 1.15f;
    private const float ZoomMin = 0.5f;
    private const float ZoomMax = 16f;

    public void SetTexture(Texture? texture, Box2 worldBounds)
    {
        _texture = texture;
        _worldBounds = worldBounds;
        _zoom = 1f;
        _pan = Vector2.Zero;
    }

    public void SetTrackedBlips(WastelandMapTrackedBlip[] trackedBlips)
    {
        _trackedBlips = trackedBlips;
    }

    public void SetAnnotations(WastelandMapAnnotation[] annotations)
    {
        _annotations = annotations;
    }

    public void SetAnnotationMode(AnnotationMode mode)
    {
        _annotationMode = mode;
        _annotationDragStartUv = null;
        _annotationDragCurrentUv = null;
        _freeDrawActive = false;
        _freeDrawUvPoints.Clear();
        _hoveredAnnotationIndex = -1;
        _dragging = false;
    }

    public void SetPendingAnnotationText(string text)
    {
        _pendingAnnotationText = text;
    }

    public void SetAnnotationColor(Color color)
    {
        _currentColor = color;
    }

    public void SetAnnotationStrokeWidth(float width)
    {
        _currentStrokeWidth = Math.Clamp(width, 1f, 12f);
    }

    /// <summary>Pack RGBA floats (0-1) into a uint: (R&lt;&lt;24)|(G&lt;&lt;16)|(B&lt;&lt;8)|A.</summary>
    public static uint ToPackedColor(Color c) =>
        ((uint)(Math.Clamp(c.R, 0f, 1f) * 255) << 24) |
        ((uint)(Math.Clamp(c.G, 0f, 1f) * 255) << 16) |
        ((uint)(Math.Clamp(c.B, 0f, 1f) * 255) << 8) |
         (uint)(Math.Clamp(c.A, 0f, 1f) * 255);

    /// <summary>Unpack a uint RGBA8 color into a Color.</summary>
    public static Color FromPackedColor(uint packed) =>
        new Color(
            ((packed >> 24) & 0xFF) / 255f,
            ((packed >> 16) & 0xFF) / 255f,
            ((packed >>  8) & 0xFF) / 255f,
             (packed        & 0xFF) / 255f);

    public void ClearAnnotations()
    {
        _annotationDragStartUv = null;
        _annotationDragCurrentUv = null;
        OnClearAnnotations?.Invoke();
    }

    // Compute fit-to-window scale (1.0 in _zoom space = fills the control).
    private float FitScale
    {
        get
        {
            if (_texture == null || Size.X <= 0 || Size.Y <= 0)
                return 1f;
            return Math.Min(Size.X / _texture.Width, Size.Y / _texture.Height);
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        if (_texture == null)
            return;

        // The map math below is intentionally in virtual UI coordinates so it
        // matches mouse input positions. DrawingHandleScreen is pixel-space, so
        // scale the draw transform once here instead of mixing spaces per call.
        var oldTransform = handle.GetTransform();
        handle.SetTransform(Matrix3Helpers.CreateScale(new Vector2(UIScale)) * oldTransform);

        var scale = FitScale * _zoom;
        var drawW = _texture.Width * scale;
        var drawH = _texture.Height * scale;

        // Center in control, then apply pan offset.
        var x = (Size.X - drawW) / 2f + _pan.X;
        var y = (Size.Y - drawH) / 2f + _pan.Y;

        handle.DrawTextureRect(_texture, UIBox2.FromDimensions(x, y, drawW, drawH));

        if (TryGetPlayerUv(out var playerUv))
        {
            var markerX = x + playerUv.X * drawW;
            var markerY = y + playerUv.Y * drawH;
            DrawMarker(handle, new Vector2(markerX, markerY));
        }

        foreach (var blip in _trackedBlips)
        {
            if (!TryGetUv(new Vector2(blip.X, blip.Y), out var uv))
                continue;

            var blipX = x + uv.X * drawW;
            var blipY = y + uv.Y * drawH;
            var blipPos = new Vector2(blipX, blipY);
            DrawTrackedHolotagMarker(handle, blipPos, blip.Kind);
            DrawTrackedHolotagLabel(handle, blipPos, blip.Label, blip.Kind);
        }

        foreach (var annotation in _annotations)
        {
            DrawAnnotation(handle, annotation, x, y, drawW, drawH);
        }

        if (_annotationMode == AnnotationMode.Box &&
            _annotationDragStartUv.HasValue &&
            _annotationDragCurrentUv.HasValue)
        {
            var preview = new WastelandMapAnnotation(
                WastelandMapAnnotationType.Box,
                _annotationDragStartUv.Value.X,
                _annotationDragStartUv.Value.Y,
                _annotationDragCurrentUv.Value.X,
                _annotationDragCurrentUv.Value.Y,
                GetAnnotationLabel("Box"),
                ToPackedColor(_currentColor),
                _currentStrokeWidth,
                null);
            DrawAnnotation(handle, preview, x, y, drawW, drawH, true);
        }

        // Erase mode: highlight annotation under cursor in red
        if (_annotationMode == AnnotationMode.Erase &&
            _hoveredAnnotationIndex >= 0 &&
            _hoveredAnnotationIndex < _annotations.Length)
        {
            DrawAnnotationHighlight(handle, _annotations[_hoveredAnnotationIndex], x, y, drawW, drawH);
        }

        // Draw mode: show in-progress freehand stroke
        if (_annotationMode == AnnotationMode.Draw && _freeDrawActive && _freeDrawUvPoints.Count >= 2)
        {
            var previewColor = new Color(_currentColor.R, _currentColor.G, _currentColor.B, 0.70f);
            for (var i = 0; i < _freeDrawUvPoints.Count - 1; i++)
            {
                var p0 = new Vector2(x + _freeDrawUvPoints[i].X * drawW,     y + _freeDrawUvPoints[i].Y * drawH);
                var p1 = new Vector2(x + _freeDrawUvPoints[i + 1].X * drawW, y + _freeDrawUvPoints[i + 1].Y * drawH);
                DrawThickLine(handle, p0, p1, previewColor, _currentStrokeWidth);
            }
        }

        handle.SetTransform(oldTransform);
    }

    private bool TryGetPlayerUv(out Vector2 uv)
    {
        uv = default;

        if (_worldBounds.Width <= 0 || _worldBounds.Height <= 0)
            return false;

        var localEntity = _playerManager.LocalEntity;
        if (!localEntity.HasValue ||
            !_entityManager.TryGetComponent<TransformComponent>(localEntity.Value, out var xform))
        {
            return false;
        }

        var xformSystem = _entityManager.System<SharedTransformSystem>();

        // The renderer/image bounds are derived from grid-local tile coordinates,
        // but depending on the player's parent chain the live entity position may
        // be exposed in either world-space or map-space. Try both and use the one
        // that actually lands inside the rendered bounds.
        var worldPos = xformSystem.GetWorldPosition(xform);
        if (TryGetUv(worldPos, out uv))
            return true;

        var mapPos = xformSystem.GetMapCoordinates(xform).Position;
        return TryGetUv(mapPos, out uv);
    }

    private bool TryGetUv(Vector2 position, out Vector2 uv)
    {
        var u = (position.X - _worldBounds.Left) / _worldBounds.Width;
        var v = 1f - (position.Y - _worldBounds.Bottom) / _worldBounds.Height;
        uv = new Vector2(u, v);
        return u >= 0f && u <= 1f && v >= 0f && v <= 1f;
    }

    private void DrawMarker(DrawingHandleScreen handle, Vector2 markerPos)
    {
        var pulse = (float) (0.5 + 0.5 * Math.Sin(_gameTiming.RealTime.TotalSeconds * 4.0));
        var outerRadius = 24f + 10f * pulse;

        handle.DrawCircle(markerPos, outerRadius, new Color(1f, 0.2f, 0.2f, 0.35f * pulse));
        handle.DrawCircle(markerPos, 18f, new Color(0f, 0f, 0f, 0.55f));
        handle.DrawCircle(markerPos, 14f, new Color(1f, 0.15f, 0.15f, 1f));
        handle.DrawCircle(markerPos, 6f, Color.White);
    }

    private void DrawTrackedHolotagMarker(DrawingHandleScreen handle, Vector2 markerPos, WastelandMapTrackedBlipKind kind)
    {
        var color = GetTrackedBlipColor(kind);
        handle.DrawCircle(markerPos, 16f, new Color(0f, 0f, 0f, 0.55f));

        switch (kind)
        {
            case WastelandMapTrackedBlipKind.Elder:
                handle.DrawCircle(markerPos, 11f, color);
                handle.DrawRect(UIBox2.FromDimensions(markerPos + new Vector2(-2f, -9f), new Vector2(4f, 18f)), Color.White);
                handle.DrawRect(UIBox2.FromDimensions(markerPos + new Vector2(-9f, -2f), new Vector2(18f, 4f)), Color.White);
                break;

            case WastelandMapTrackedBlipKind.Paladin:
                DrawDiamond(handle, markerPos, 12f, color);
                handle.DrawCircle(markerPos, 3.5f, Color.White);
                break;

            case WastelandMapTrackedBlipKind.Knight:
                handle.DrawRect(UIBox2.FromDimensions(markerPos - new Vector2(9f, 9f), new Vector2(18f, 18f)), color);
                handle.DrawCircle(markerPos, 3.5f, Color.White);
                break;

            case WastelandMapTrackedBlipKind.Scribe:
                DrawTriangle(handle, markerPos, 13f, color);
                handle.DrawCircle(markerPos, 3.5f, Color.White);
                break;

            case WastelandMapTrackedBlipKind.Squire:
                handle.DrawCircle(markerPos, 11f, color);
                handle.DrawCircle(markerPos, 3.5f, Color.White);
                break;

            // #Misfits Add - Willower pendant/navigation marker.
            case WastelandMapTrackedBlipKind.Willower:
                handle.DrawCircle(markerPos, 11f, color);
                handle.DrawCircle(markerPos, 4f, Color.White);
                break;

            // #Misfits Add - Legion rank blip shapes
            case WastelandMapTrackedBlipKind.LegionCenturion:
                // Gold star: cross + circle, like Elder but gold-tinted
                handle.DrawCircle(markerPos, 11f, color);
                handle.DrawRect(UIBox2.FromDimensions(markerPos + new Vector2(-2f, -9f), new Vector2(4f, 18f)), Color.White);
                handle.DrawRect(UIBox2.FromDimensions(markerPos + new Vector2(-9f, -2f), new Vector2(18f, 4f)), Color.White);
                break;

            case WastelandMapTrackedBlipKind.LegionDecanus:
                // Red diamond for squad leaders
                DrawDiamond(handle, markerPos, 12f, color);
                handle.DrawCircle(markerPos, 3.5f, Color.White);
                break;

            case WastelandMapTrackedBlipKind.LegionWarrior:
                // Dark red square for warriors / specialists
                handle.DrawRect(UIBox2.FromDimensions(markerPos - new Vector2(9f, 9f), new Vector2(18f, 18f)), color);
                handle.DrawCircle(markerPos, 3.5f, Color.White);
                break;

            case WastelandMapTrackedBlipKind.LegionRecruit:
                // Brown circle for recruits, auxilia, slaves
                handle.DrawCircle(markerPos, 11f, color);
                handle.DrawCircle(markerPos, 5f, Color.Black);
                break;

            case WastelandMapTrackedBlipKind.GroupRallyPoint:
                DrawDiamond(handle, markerPos, 12f, color);
                handle.DrawCircle(markerPos, 3.5f, Color.Black);
                handle.DrawCircle(markerPos, 2f, Color.White);
                break;

            case WastelandMapTrackedBlipKind.TribalHuntTarget:
                // Hunt target: bright crimson ring + white center for fast recognition.
                handle.DrawCircle(markerPos, 11f, color);
                handle.DrawCircle(markerPos, 7f, Color.Black);
                handle.DrawCircle(markerPos, 3f, Color.White);
                break;
            // #Misfits Add - Followers dead body blip: pale white X mark
            case WastelandMapTrackedBlipKind.DeadBody:
                DrawX(handle, markerPos, 9f, color);
                break;
            case WastelandMapTrackedBlipKind.CriticalSoul:
                handle.DrawCircle(markerPos, 11f, color);
                handle.DrawCircle(markerPos, 4f, Color.White);
                break;
            case WastelandMapTrackedBlipKind.DeadSoul:
                DrawX(handle, markerPos, 10f, color);
                break;
            case WastelandMapTrackedBlipKind.TribeCritical:
                handle.DrawCircle(markerPos, 12f, color);
                handle.DrawCircle(markerPos, 7f, Color.Black);
                handle.DrawCircle(markerPos, 3f, Color.White);
                break;
            case WastelandMapTrackedBlipKind.MaterialExtractor:
                DrawDiamond(handle, markerPos, 13f, color);
                handle.DrawCircle(markerPos, 6f, Color.Black);
                handle.DrawRect(UIBox2.FromDimensions(markerPos + new Vector2(-2f, -8f), new Vector2(4f, 16f)), Color.White);
                handle.DrawRect(UIBox2.FromDimensions(markerPos + new Vector2(-8f, -2f), new Vector2(16f, 4f)), Color.White);
                break;
            case WastelandMapTrackedBlipKind.ExpeditionEntrance:
                handle.DrawCircle(markerPos, 12f, color);
                handle.DrawCircle(markerPos, 7f, Color.Black);
                DrawDiamond(handle, markerPos, 4f, Color.White);
                break;
            // End Misfits Add
        }
    }

    private void DrawTrackedHolotagLabel(DrawingHandleScreen handle, Vector2 markerPos, string label, WastelandMapTrackedBlipKind kind)
    {
        if (string.IsNullOrWhiteSpace(label))
            return;

        var labelPos = markerPos + new Vector2(14f, -14f);
        var textDimensions = handle.GetDimensions(_blipLabelFont, label, 1f);
        var padding = new Vector2(4f, 2f); // #Misfits Tweak - tighter padding for smaller font
        var rectTopLeft = labelPos - padding;
        var rectBottomRight = labelPos + textDimensions + padding;

        handle.DrawRect(new UIBox2(rectTopLeft, rectBottomRight), new Color(0f, 0f, 0f, 0.8f));
        handle.DrawString(_blipLabelFont, labelPos, label, GetTrackedBlipColor(kind));
    }

    private void DrawAnnotation(DrawingHandleScreen handle, WastelandMapAnnotation annotation, float x, float y, float drawW, float drawH, bool preview = false)
    {
        var baseColor = FromPackedColor(annotation.PackedColor);
        var alpha = preview ? 0.55f : 0.90f;
        var color = new Color(baseColor.R, baseColor.G, baseColor.B, alpha);

        switch (annotation.Type)
        {
            case WastelandMapAnnotationType.Marker:
            {
                var markerPos = new Vector2(x + annotation.StartX * drawW, y + annotation.StartY * drawH);
                handle.DrawCircle(markerPos, 13f, new Color(0f, 0f, 0f, 0.6f));
                handle.DrawCircle(markerPos, 9f, color);
                handle.DrawCircle(markerPos, 3f, Color.White);
                DrawAnnotationLabel(handle, markerPos + new Vector2(16f, 10f), annotation.Label, color);
                break;
            }

            case WastelandMapAnnotationType.Box:
            {
                var start = new Vector2(x + annotation.StartX * drawW, y + annotation.StartY * drawH);
                var end   = new Vector2(x + annotation.EndX   * drawW, y + annotation.EndY   * drawH);
                var topLeft     = Vector2.Min(start, end);
                var bottomRight = Vector2.Max(start, end);
                DrawBorder(handle, new UIBox2(topLeft, bottomRight), color, 3f);
                DrawAnnotationLabel(handle, topLeft + new Vector2(6f, 6f), annotation.Label, color);
                break;
            }

            case WastelandMapAnnotationType.Draw:
            {
                var pts = annotation.StrokePoints;
                if (pts == null || pts.Length < 4)
                    break;
                for (var i = 0; i < pts.Length - 2; i += 2)
                {
                    var p0 = new Vector2(x + pts[i]     * drawW, y + pts[i + 1] * drawH);
                    var p1 = new Vector2(x + pts[i + 2] * drawW, y + pts[i + 3] * drawH);
                    DrawThickLine(handle, p0, p1, color, annotation.StrokeWidth);
                }
                break;
            }
        }
    }

    private void DrawAnnotationHighlight(DrawingHandleScreen handle, WastelandMapAnnotation annotation, float x, float y, float drawW, float drawH)
    {
        var eraseColor = new Color(1f, 0.15f, 0.15f, 0.90f);
        switch (annotation.Type)
        {
            case WastelandMapAnnotationType.Marker:
            {
                var pos = new Vector2(x + annotation.StartX * drawW, y + annotation.StartY * drawH);
                handle.DrawCircle(pos, 15f, new Color(1f, 0f, 0f, 0.35f));
                handle.DrawCircle(pos, 13f, new Color(0f, 0f, 0f, 0.6f));
                handle.DrawCircle(pos, 9f, eraseColor);
                handle.DrawCircle(pos, 3f, Color.White);
                break;
            }
            case WastelandMapAnnotationType.Box:
            {
                var start       = new Vector2(x + annotation.StartX * drawW, y + annotation.StartY * drawH);
                var end         = new Vector2(x + annotation.EndX   * drawW, y + annotation.EndY   * drawH);
                var topLeft     = Vector2.Min(start, end);
                var bottomRight = Vector2.Max(start, end);
                DrawBorder(handle, new UIBox2(topLeft, bottomRight), eraseColor, 5f);
                break;
            }
            case WastelandMapAnnotationType.Draw:
            {
                var pts = annotation.StrokePoints;
                if (pts == null || pts.Length < 4) break;
                for (var i = 0; i < pts.Length - 2; i += 2)
                {
                    var p0 = new Vector2(x + pts[i]     * drawW, y + pts[i + 1] * drawH);
                    var p1 = new Vector2(x + pts[i + 2] * drawW, y + pts[i + 3] * drawH);
                    DrawThickLine(handle, p0, p1, eraseColor, Math.Max(annotation.StrokeWidth, 4f));
                }
                break;
            }
        }
    }

    private void DrawAnnotationLabel(DrawingHandleScreen handle, Vector2 position, string label, Color color)
    {
        if (string.IsNullOrWhiteSpace(label))
            return;

        var textDimensions = handle.GetDimensions(_blipLabelFont, label, 1f);
        var padding = new Vector2(4f, 2f); // #Misfits Tweak - tighter padding for smaller font
        handle.DrawRect(new UIBox2(position - padding, position + textDimensions + padding), new Color(0f, 0f, 0f, 0.82f));
        handle.DrawString(_blipLabelFont, position, label, color);
    }

    /// <summary>Draw a thick anti-aliased line segment with rounded end-caps.</summary>
    private static void DrawThickLine(DrawingHandleScreen handle, Vector2 p0, Vector2 p1, Color color, float width)
    {
        if (width <= 1.5f)
        {
            handle.DrawLine(p0, p1, color);
            return;
        }
        var dir = p1 - p0;
        var len = dir.Length();
        if (len < 0.001f)
        {
            handle.DrawCircle(p0, width / 2f, color);
            return;
        }
        dir /= len;
        var perp = new Vector2(-dir.Y, dir.X) * (width / 2f);
        var verts = new[]
        {
            p0 - perp, p0 + perp, p1 + perp,
            p0 - perp, p1 + perp, p1 - perp,
        };
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, verts, color);
        handle.DrawCircle(p0, width / 2f, color);
        handle.DrawCircle(p1, width / 2f, color);
    }

    private static void DrawBorder(DrawingHandleScreen handle, UIBox2 rect, Color color, float width)
    {
        handle.DrawRect(new UIBox2(rect.Left, rect.Top, rect.Right, rect.Top + width), color);
        handle.DrawRect(new UIBox2(rect.Left, rect.Bottom - width, rect.Right, rect.Bottom), color);
        handle.DrawRect(new UIBox2(rect.Left, rect.Top, rect.Left + width, rect.Bottom), color);
        handle.DrawRect(new UIBox2(rect.Right - width, rect.Top, rect.Right, rect.Bottom), color);
    }

    private static void DrawDiamond(DrawingHandleScreen handle, Vector2 center, float radius, Color color)
    {
        var vertices = new[]
        {
            center + new Vector2(0f, -radius),
            center + new Vector2(radius, 0f),
            center + new Vector2(-radius, 0f),
            center + new Vector2(-radius, 0f),
            center + new Vector2(radius, 0f),
            center + new Vector2(0f, radius),
        };
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, vertices, color);
    }

    // #Misfits Add - X marker for dead body blips on the Followers tac-map.
    private static void DrawX(DrawingHandleScreen handle, Vector2 center, float radius, Color color)
    {
        DrawThickLine(handle, center + new Vector2(-radius, -radius), center + new Vector2(radius, radius), color, 3f);
        DrawThickLine(handle, center + new Vector2(radius, -radius), center + new Vector2(-radius, radius), color, 3f);
    }

    private static void DrawTriangle(DrawingHandleScreen handle, Vector2 center, float radius, Color color)
    {
        var vertices = new[]
        {
            center + new Vector2(0f, -radius),
            center + new Vector2(-radius, radius * 0.8f),
            center + new Vector2(radius, radius * 0.8f),
        };
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, vertices, color);
    }

    private static Color GetTrackedBlipColor(WastelandMapTrackedBlipKind kind)
    {
        return kind switch
        {
            WastelandMapTrackedBlipKind.Elder => new Color(0.95f, 0.2f, 0.2f, 1f),
            WastelandMapTrackedBlipKind.Paladin => new Color(0.2f, 0.55f, 1f, 1f),
            WastelandMapTrackedBlipKind.Knight => new Color(0.15f, 0.85f, 0.35f, 1f),
            WastelandMapTrackedBlipKind.Scribe => new Color(0.35f, 0.95f, 0.95f, 1f),
            WastelandMapTrackedBlipKind.Squire => new Color(1f, 0.6f, 0.15f, 1f),
            WastelandMapTrackedBlipKind.Willower => new Color(0.84f, 0.61f, 0.24f, 1f), // #Misfits Add - Tribe department gold
            // #Misfits Add - Legion rank colours (red/gold Caesar's Legion palette)
            WastelandMapTrackedBlipKind.LegionCenturion => new Color(0.95f, 0.72f, 0.08f, 1f), // gold
            WastelandMapTrackedBlipKind.LegionDecanus => new Color(0.92f, 0.18f, 0.12f, 1f),   // bright red
            WastelandMapTrackedBlipKind.LegionWarrior => new Color(0.70f, 0.16f, 0.12f, 1f),   // dark red
            WastelandMapTrackedBlipKind.LegionRecruit => new Color(0.62f, 0.32f, 0.12f, 1f),   // brown
            WastelandMapTrackedBlipKind.GroupRallyPoint => new Color(0.97f, 0.78f, 0.18f, 1f),
            WastelandMapTrackedBlipKind.TribalHuntTarget => new Color(1f, 0.20f, 0.18f, 1f),
            // #Misfits Add - Followers dead body blip: pale white
            WastelandMapTrackedBlipKind.DeadBody => new Color(0.9f, 0.9f, 0.9f, 1f),
            WastelandMapTrackedBlipKind.CriticalSoul => new Color(0.15f, 1f, 0.50f, 1f),
            WastelandMapTrackedBlipKind.DeadSoul => new Color(0.68f, 0.80f, 0.72f, 1f),
            WastelandMapTrackedBlipKind.TribeCritical => new Color(0.25f, 0.95f, 1f, 1f),
            WastelandMapTrackedBlipKind.MaterialExtractor => new Color(1f, 0.55f, 0.08f, 1f),
            WastelandMapTrackedBlipKind.ExpeditionEntrance => new Color(0.68f, 0.70f, 0.72f, 1f),
            // End Misfits Add
            _ => new Color(0.98f, 0.84f, 0.15f, 0.95f),
        };
    }

    private string GetAnnotationLabel(string fallback)
    {
        return string.IsNullOrWhiteSpace(_pendingAnnotationText) ? fallback : _pendingAnnotationText.Trim();
    }

    private bool TryControlToUv(Vector2 controlPosition, out Vector2 uv)
    {
        uv = default;

        if (_texture == null)
            return false;

        var scale = FitScale * _zoom;
        var drawW = _texture.Width * scale;
        var drawH = _texture.Height * scale;
        var x = (Size.X - drawW) / 2f + _pan.X;
        var y = (Size.Y - drawH) / 2f + _pan.Y;

        var u = (controlPosition.X - x) / drawW;
        var v = (controlPosition.Y - y) / drawH;
        uv = new Vector2(u, v);
        return u >= 0f && u <= 1f && v >= 0f && v <= 1f;
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);
        if (_texture == null)
            return;

        var oldZoom = _zoom;
        if (args.Delta.Y > 0)
            _zoom = Math.Min(_zoom * ZoomStep, ZoomMax);
        else if (args.Delta.Y < 0)
            _zoom = Math.Max(_zoom / ZoomStep, ZoomMin);

        // Zoom toward mouse cursor position.
        var mouseInControl = args.RelativePosition;
        var centerOffset = mouseInControl - Size / 2f;
        _pan = ((_pan - centerOffset) * (_zoom / oldZoom)) + centerOffset;

        ClampPan();
        args.Handle();
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function == EngineKeyFunctions.UIRightClick)
        {
            if (TryGetAnnotationIndexAt(args.RelativePosition, out var index))
            {
                OnRemoveAnnotation?.Invoke(index);
                args.Handle();
            }

            return;
        }

        if (args.Function == EngineKeyFunctions.UIClick)
        {
            // Erase mode: left-click removes hovered annotation
            if (_annotationMode == AnnotationMode.Erase)
            {
                if (_hoveredAnnotationIndex >= 0)
                {
                    OnRemoveAnnotation?.Invoke(_hoveredAnnotationIndex);
                    _hoveredAnnotationIndex = -1;
                }
                args.Handle();
                return;
            }

            // Draw mode: begin freehand stroke
            if (_annotationMode == AnnotationMode.Draw)
            {
                if (TryControlToUv(args.RelativePosition, out var drawUv))
                {
                    _freeDrawUvPoints.Clear();
                    _freeDrawUvPoints.Add(drawUv);
                    _freeDrawActive = true;
                    args.Handle();
                }
                return;
            }

            if (_annotationMode == AnnotationMode.Marker)
            {
                if (TryControlToUv(args.RelativePosition, out var uv))
                {
                    OnAddAnnotation?.Invoke(new WastelandMapAnnotation(
                        WastelandMapAnnotationType.Marker,
                        uv.X,
                        uv.Y,
                        uv.X,
                        uv.Y,
                        GetAnnotationLabel("Marker"),
                        ToPackedColor(_currentColor),
                        _currentStrokeWidth,
                        null));
                    args.Handle();
                }
                return;
            }

            if (_annotationMode == AnnotationMode.Box)
            {
                if (TryControlToUv(args.RelativePosition, out var uv))
                {
                    _annotationDragStartUv = uv;
                    _annotationDragCurrentUv = uv;
                    args.Handle();
                }
                return;
            }

            _dragging = true;
            _dragStart = args.RelativePosition;
            _panAtDragStart = _pan;
            args.Handle();
        }
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (args.Function == EngineKeyFunctions.UIClick)
        {
            // Draw mode: finalize freehand stroke on mouse release
            if (_annotationMode == AnnotationMode.Draw && _freeDrawActive)
            {
                _freeDrawActive = false;
                if (_freeDrawUvPoints.Count >= 2)
                {
                    var pts = new float[_freeDrawUvPoints.Count * 2];
                    for (var i = 0; i < _freeDrawUvPoints.Count; i++)
                    {
                        pts[i * 2]     = _freeDrawUvPoints[i].X;
                        pts[i * 2 + 1] = _freeDrawUvPoints[i].Y;
                    }
                    OnAddAnnotation?.Invoke(new WastelandMapAnnotation(
                        WastelandMapAnnotationType.Draw, 0f, 0f, 0f, 0f,
                        GetAnnotationLabel("Drawing"),
                        ToPackedColor(_currentColor),
                        _currentStrokeWidth,
                        pts));
                }
                _freeDrawUvPoints.Clear();
                args.Handle();
                return;
            }

            if (_annotationMode == AnnotationMode.Box &&
                _annotationDragStartUv.HasValue &&
                _annotationDragCurrentUv.HasValue)
            {
                OnAddAnnotation?.Invoke(new WastelandMapAnnotation(
                    WastelandMapAnnotationType.Box,
                    _annotationDragStartUv.Value.X,
                    _annotationDragStartUv.Value.Y,
                    _annotationDragCurrentUv.Value.X,
                    _annotationDragCurrentUv.Value.Y,
                    GetAnnotationLabel("Box"),
                    ToPackedColor(_currentColor),
                    _currentStrokeWidth,
                    null));
                _annotationDragStartUv = null;
                _annotationDragCurrentUv = null;
                args.Handle();
                return;
            }

            _dragging = false;
        }
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        // Erase mode: track which annotation is under the cursor
        if (_annotationMode == AnnotationMode.Erase)
        {
            TryGetAnnotationIndexAt(args.RelativePosition, out _hoveredAnnotationIndex);
            return;
        }

        // Draw mode with button held: accumulate freehand points
        if (_annotationMode == AnnotationMode.Draw && _freeDrawActive)
        {
            if (TryControlToUv(args.RelativePosition, out var uv) &&
                _freeDrawUvPoints.Count < MaxStrokeUvPoints)
            {
                // Only add if moved enough to matter (avoids huge duplicate-point arrays)
                if (_freeDrawUvPoints.Count == 0 ||
                    Vector2.DistanceSquared(uv, _freeDrawUvPoints[_freeDrawUvPoints.Count - 1]) > 0.00005f)
                {
                    _freeDrawUvPoints.Add(uv);
                }
            }
            return;
        }

        if (_annotationMode == AnnotationMode.Box && _annotationDragStartUv.HasValue)
        {
            if (TryControlToUv(args.RelativePosition, out var uv))
                _annotationDragCurrentUv = uv;
            return;
        }

        if (!_dragging)
            return;

        _pan = _panAtDragStart + (args.RelativePosition - _dragStart);
        ClampPan();
    }

    private void ClampPan()
    {
        if (_texture == null)
            return;

        var scale = FitScale * _zoom;
        var drawW = _texture.Width * scale;
        var drawH = _texture.Height * scale;

        // Allow panning up to half the image beyond the control edge.
        var maxPanX = drawW / 2f;
        var maxPanY = drawH / 2f;
        _pan.X = Math.Clamp(_pan.X, -maxPanX, maxPanX);
        _pan.Y = Math.Clamp(_pan.Y, -maxPanY, maxPanY);
    }

    private bool TryGetAnnotationIndexAt(Vector2 controlPosition, out int index)
    {
        index = -1;

        if (_texture == null || _annotations.Length == 0)
            return false;

        var scale = FitScale * _zoom;
        var drawW = _texture.Width * scale;
        var drawH = _texture.Height * scale;
        var x = (Size.X - drawW) / 2f + _pan.X;
        var y = (Size.Y - drawH) / 2f + _pan.Y;

        var bestDistance = float.PositiveInfinity;
        const float maxDistance = 24f;

        for (var i = 0; i < _annotations.Length; i++)
        {
            var distance = GetAnnotationDistance(_annotations[i], controlPosition, x, y, drawW, drawH);
            if (distance > maxDistance || distance >= bestDistance)
                continue;

            bestDistance = distance;
            index = i;
        }

        return index >= 0;
    }

    private static float GetAnnotationDistance(WastelandMapAnnotation annotation, Vector2 position, float x, float y, float drawW, float drawH)
    {
        return annotation.Type switch
        {
            WastelandMapAnnotationType.Marker => Vector2.Distance(position,
                new Vector2(x + annotation.StartX * drawW, y + annotation.StartY * drawH)),
            WastelandMapAnnotationType.Box => DistanceToBox(annotation, position, x, y, drawW, drawH),
            WastelandMapAnnotationType.Draw => DistanceToStroke(annotation, position, x, y, drawW, drawH),
            _ => float.PositiveInfinity,
        };
    }

    private static float DistanceToStroke(WastelandMapAnnotation annotation, Vector2 position, float x, float y, float drawW, float drawH)
    {
        var pts = annotation.StrokePoints;
        if (pts == null || pts.Length < 4)
            return float.PositiveInfinity;
        var minDist = float.PositiveInfinity;
        for (var i = 0; i < pts.Length - 2; i += 2)
        {
            var p0 = new Vector2(x + pts[i]     * drawW, y + pts[i + 1] * drawH);
            var p1 = new Vector2(x + pts[i + 2] * drawW, y + pts[i + 3] * drawH);
            var d = DistanceToLineSegment(position, p0, p1);
            if (d < minDist)
                minDist = d;
        }
        return minDist;
    }

    private static float DistanceToLineSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lenSq = Vector2.Dot(ab, ab);
        if (lenSq < 0.0001f)
            return Vector2.Distance(p, a);
        var t = Math.Clamp(Vector2.Dot(p - a, ab) / lenSq, 0f, 1f);
        return Vector2.Distance(p, a + t * ab);
    }

    private static float DistanceToBox(WastelandMapAnnotation annotation, Vector2 position, float x, float y, float drawW, float drawH)
    {
        var start = new Vector2(x + annotation.StartX * drawW, y + annotation.StartY * drawH);
        var end = new Vector2(x + annotation.EndX * drawW, y + annotation.EndY * drawH);
        var min = Vector2.Min(start, end);
        var max = Vector2.Max(start, end);

        if (position.X >= min.X && position.X <= max.X && position.Y >= min.Y && position.Y <= max.Y)
            return 0f;

        var dx = Math.Max(min.X - position.X, Math.Max(0f, position.X - max.X));
        var dy = Math.Max(min.Y - position.Y, Math.Max(0f, position.Y - max.Y));
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
