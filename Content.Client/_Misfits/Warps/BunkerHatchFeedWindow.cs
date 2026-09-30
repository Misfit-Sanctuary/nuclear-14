// #Misfits Add - Window the Enclave TacMap opens when you click a bunker hatch.
using System.Numerics;
using Content.Shared._Misfits.Warps;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._Misfits.Warps;

/// <summary>
/// Camera view of one hatch plus a button to lock or unlock it. Anyone at the TacMap can look; the
/// server only lets Enclave NCOs and up change the lock and pops up a message for everyone else.
/// </summary>
public sealed class BunkerHatchFeedWindow : DefaultWindow
{
    /// <summary>
    /// Hatch, and true to lock / false to unlock.
    /// </summary>
    public event Action<NetEntity, bool>? OnLockPressed;

    private readonly BunkerHatchFeedControl _feed;
    private readonly Label _status;
    private readonly Button _lockButton;
    private BunkerHatchEntry? _hatch;

    public NetEntity? Hatch => _hatch?.Hatch;

    public BunkerHatchFeedWindow(bool showLockButton = true)
    {
        MinSize = new Vector2(520, 440);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        Contents.AddChild(root);

        _feed = new BunkerHatchFeedControl();
        root.AddChild(_feed);

        var bar = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
        root.AddChild(bar);

        _status = new Label { HorizontalExpand = true, VerticalAlignment = VAlignment.Center };
        bar.AddChild(_status);

        _lockButton = new Button { MinWidth = 120, ToolTip = Loc.GetString("bunker-hatch-ui-lock-tooltip"), Visible = showLockButton };
        _lockButton.OnPressed += _ =>
        {
            if (_hatch is { } hatch)
                OnLockPressed?.Invoke(hatch.Hatch, !hatch.Locked);
        };
        bar.AddChild(_lockButton);
    }

    public void SetHatch(BunkerHatchEntry hatch)
    {
        _hatch = hatch;
        Title = Loc.GetString("bunker-hatch-ui-feed-title", ("label", hatch.Label));
        _feed.SetHatch(hatch.Hatch);
        _status.Text = Loc.GetString(hatch.Locked ? "bunker-hatch-ui-status-locked" : "bunker-hatch-ui-status-open");
        _status.FontColorOverride = hatch.Locked ? Color.FromHex("#F2382E") : Color.FromHex("#40F259");
        _lockButton.Text = Loc.GetString(hatch.Locked ? "bunker-hatch-ui-unlock" : "bunker-hatch-ui-lock");
    }
}
