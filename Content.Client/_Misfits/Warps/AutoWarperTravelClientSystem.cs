using System;
using System.Numerics;
using Content.Client.Resources;
using Content.Shared._Misfits.Warps;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._Misfits.Warps;

public sealed class AutoWarperTravelClientSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlays = default!;
    [Dependency] private readonly IResourceCache _resources = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    private AutoWarperTravelOverlay? _overlay;

    public override void Initialize()
    {
        SubscribeNetworkEvent<AutoWarperTravelEvent>(OnTravel);
        SubscribeNetworkEvent<AutoWarperArrivalEvent>(OnArrival);
    }

    public override void Shutdown()
    {
        if (_overlay != null)
            _overlays.RemoveOverlay(_overlay);
    }

    private void OnTravel(AutoWarperTravelEvent args)
    {
        EnsureOverlay();

        _overlay!.StartedAt = _timing.CurTime;
        _overlay.EndsAt = _timing.CurTime + TimeSpan.FromSeconds(args.DurationSeconds);
    }

    private void OnArrival(AutoWarperArrivalEvent args)
    {
        EnsureOverlay();
        _overlay!.WelcomeText = Loc.GetString("auto-warper-welcome", ("place", args.PlaceName));
        // The travel overlay is still fading out when the server completes the move.
        _overlay.WelcomeStartedAt = _timing.CurTime + TimeSpan.FromSeconds(0.25);
        _overlay.WelcomeEndsAt = _overlay.WelcomeStartedAt + TimeSpan.FromSeconds(4);
    }

    private void EnsureOverlay()
    {
        if (_overlay != null)
            return;

        _overlay = new AutoWarperTravelOverlay(_resources, _timing);
        _overlays.AddOverlay(_overlay);
    }
}

internal sealed class AutoWarperTravelOverlay : Overlay
{
    private readonly Font _font;
    private readonly IGameTiming _timing;
    public override OverlaySpace Space => OverlaySpace.ScreenSpace;
    public TimeSpan StartedAt;
    public TimeSpan EndsAt;
    public TimeSpan WelcomeStartedAt;
    public TimeSpan WelcomeEndsAt;
    public string? WelcomeText;

    public AutoWarperTravelOverlay(IResourceCache resources, IGameTiming timing)
    {
        _font = resources.GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 22);
        _timing = timing;
        ZIndex = 1000;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var now = _timing.CurTime;
        var bounds = args.ViewportBounds;
        if (now < EndsAt)
        {
            var elapsed = (float) (now - StartedAt).TotalSeconds;
            var remaining = (float) (EndsAt - now).TotalSeconds;
            var alpha = MathF.Min(1f, MathF.Min(elapsed / 0.25f, remaining / 0.25f));
            args.ScreenHandle.DrawRect(bounds, Color.Black.WithAlpha(alpha));

            var travelText = ". . . Traveling.";
            var travelSize = args.ScreenHandle.GetDimensions(_font, travelText, 1f);
            var travelPosition = new Vector2((bounds.Width - travelSize.X) / 2f, (bounds.Height - travelSize.Y) / 2f);
            args.ScreenHandle.DrawString(_font, travelPosition, travelText, Color.White.WithAlpha(alpha));
        }

        if (WelcomeText == null || now < WelcomeStartedAt || now >= WelcomeEndsAt)
            return;

        var welcomeRemaining = (float) (WelcomeEndsAt - now).TotalSeconds;
        var welcomeAlpha = MathF.Min(1f, MathF.Min((float) (now - WelcomeStartedAt).TotalSeconds / 0.25f,
            welcomeRemaining / 0.5f));
        var welcomeSize = args.ScreenHandle.GetDimensions(_font, WelcomeText, 1f);
        var welcomePosition = new Vector2(bounds.Width - welcomeSize.X - 28f, bounds.Height - welcomeSize.Y - 64f);
        var padding = new Vector2(12f, 8f);
        args.ScreenHandle.DrawRect(new UIBox2(welcomePosition - padding, welcomePosition + welcomeSize + padding),
            new Color(0.05f, 0.05f, 0.05f, 0.8f * welcomeAlpha));
        args.ScreenHandle.DrawString(_font, welcomePosition, WelcomeText, Color.White.WithAlpha(welcomeAlpha));
    }
}
