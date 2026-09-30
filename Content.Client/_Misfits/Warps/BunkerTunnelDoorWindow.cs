// #Misfits Add - Menu the tunnel door opens for Enclave members.
using System.Numerics;
using Content.Shared._Misfits.Warps;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._Misfits.Warps;

/// <summary>
/// Lists the surface hatches with their lock state. Selecting one shows a camera view of it, so you
/// can see what is waiting up there, and "Go" takes you up through it.
/// </summary>
public sealed class BunkerTunnelDoorWindow : DefaultWindow
{
    public event Action<NetEntity?>? OnPreview;
    public event Action<NetEntity>? OnGo;

    private readonly BoxContainer _list;
    private readonly BunkerHatchFeedControl _feed;
    private readonly Label _empty;
    private readonly Button _goButton;
    private readonly Dictionary<NetEntity, Button> _rows = new();
    private NetEntity? _selected;

    public BunkerTunnelDoorWindow()
    {
        Title = Loc.GetString("bunker-door-ui-title");
        MinSize = new Vector2(720, 420);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
        Contents.AddChild(root);

        var left = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, MinWidth = 200, SeparationOverride = 4 };
        root.AddChild(left);

        left.AddChild(new Label { Text = Loc.GetString("bunker-door-ui-pick") });

        _list = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4, VerticalExpand = true };
        left.AddChild(_list);

        _empty = new Label { Text = Loc.GetString("bunker-door-ui-no-hatches"), Visible = false };
        left.AddChild(_empty);

        _goButton = new Button { Text = Loc.GetString("bunker-door-ui-go"), Disabled = true };
        _goButton.OnPressed += _ =>
        {
            if (_selected is { } hatch)
                OnGo?.Invoke(hatch);
        };
        left.AddChild(_goButton);

        _feed = new BunkerHatchFeedControl();
        root.AddChild(_feed);
    }

    public void UpdateState(BunkerTunnelDoorUiState state)
    {
        _list.RemoveAllChildren();
        _rows.Clear();
        _empty.Visible = state.Hatches.Length == 0;

        foreach (var hatch in state.Hatches)
        {
            var entry = hatch;
            var status = Loc.GetString(entry.Locked ? "bunker-hatch-ui-status-locked" : "bunker-hatch-ui-status-open");
            var button = new Button
            {
                Text = $"{entry.Label} - {status}",
                ToggleMode = true,
                Pressed = entry.Hatch == _selected,
            };
            button.OnPressed += _ => Select(entry.Hatch);
            _list.AddChild(button);
            _rows[entry.Hatch] = button;
        }

        // The hatch we were looking at is gone (deleted or re-rolled).
        if (_selected is { } selected && !_rows.ContainsKey(selected))
            Select(null);
    }

    private void Select(NetEntity? hatch)
    {
        _selected = hatch;
        _goButton.Disabled = hatch == null;
        _feed.SetHatch(hatch);

        foreach (var (rowHatch, button) in _rows)
        {
            button.Pressed = rowHatch == hatch;
        }

        OnPreview?.Invoke(hatch);
    }
}
