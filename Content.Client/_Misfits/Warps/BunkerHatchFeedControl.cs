// #Misfits Add - Live camera view through a surface bunker hatch.
using System.Numerics;
using Content.Client.Eye;
using Content.Client.Viewport;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Graphics;
using Robust.Shared.Timing;

namespace Content.Client._Misfits.Warps;

/// <summary>
/// Shows what the Eye on a bunker hatch sees. The server has to be streaming the area around the
/// hatch to us first (it does that when the UI sends a view request), so until the hatch shows up
/// on our side this shows a "connecting" panel instead.
/// <para>
/// Same approach as the Overwatch feed on the TacMap: copy the hatch's eye into a FixedEye every
/// frame and let a ScalingViewport render it.
/// </para>
/// </summary>
public sealed class BunkerHatchFeedControl : Control
{
    [Dependency] private readonly IEntityManager _entMan = default!;

    private readonly EyeLerpingSystem _eyeLerping;
    private readonly ScalingViewport _viewport;
    private readonly PanelContainer _fallback;
    private readonly Label _fallbackLabel;
    private readonly FixedEye _feedEye = new();
    private readonly FixedEye _blankEye = new();

    private NetEntity? _hatch;
    private EntityUid? _lerpingEye;

    public BunkerHatchFeedControl()
    {
        IoCManager.InjectDependencies(this);
        _eyeLerping = _entMan.System<EyeLerpingSystem>();

        MinSize = new Vector2(480, 320);
        HorizontalExpand = true;
        VerticalExpand = true;

        var layout = new LayoutContainer { HorizontalExpand = true, VerticalExpand = true };
        AddChild(layout);

        _viewport = new ScalingViewport
        {
            ViewportSize = new Vector2i(480, 320),
            Eye = _blankEye,
            MouseFilter = MouseFilterMode.Ignore,
            Visible = false,
        };
        LayoutContainer.SetAnchorPreset(_viewport, LayoutContainer.LayoutPreset.Wide);
        layout.AddChild(_viewport);

        _fallbackLabel = new Label
        {
            Text = Loc.GetString("bunker-hatch-feed-none"),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            FontColorOverride = Color.FromHex("#33FF33"),
        };
        _fallback = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat(Color.FromHex("#050805")),
        };
        _fallback.AddChild(_fallbackLabel);
        LayoutContainer.SetAnchorPreset(_fallback, LayoutContainer.LayoutPreset.Wide);
        layout.AddChild(_fallback);
    }

    /// <summary>
    /// Which hatch to show. Null blanks the feed.
    /// </summary>
    public void SetHatch(NetEntity? hatch)
    {
        if (_hatch == hatch)
            return;

        _hatch = hatch;
        ReleaseEye();
        _fallbackLabel.Text = Loc.GetString(hatch == null ? "bunker-hatch-feed-none" : "bunker-hatch-feed-connecting");
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        var hasFeed = false;
        if (_hatch is { } netHatch
            && _entMan.TryGetEntity(netHatch, out var hatch)
            && _entMan.TryGetComponent<EyeComponent>(hatch, out var eye))
        {
            if (_lerpingEye != hatch)
            {
                ReleaseEye();
                _eyeLerping.AddEye(hatch.Value, eye);
                _lerpingEye = hatch;
            }

            _feedEye.Position = eye.Eye.Position;
            _feedEye.Offset = eye.Eye.Offset;
            _feedEye.Rotation = eye.Eye.Rotation;
            _feedEye.Zoom = eye.Eye.Zoom;
            _feedEye.DrawFov = eye.Eye.DrawFov;
            _feedEye.DrawLight = eye.Eye.DrawLight;
            hasFeed = true;
        }

        _viewport.Eye = hasFeed ? _feedEye : _blankEye;
        _viewport.Visible = hasFeed;
        _fallback.Visible = !hasFeed;
    }

    private void ReleaseEye()
    {
        if (_lerpingEye is { } old && _entMan.EntityExists(old))
            _eyeLerping.RemoveEye(old);

        _lerpingEye = null;
    }

    protected override void Dispose(bool disposing)
    {
        ReleaseEye();
        base.Dispose(disposing);
    }
}
