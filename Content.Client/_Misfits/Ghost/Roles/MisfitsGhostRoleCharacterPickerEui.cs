// #Misfits Add - Client side of the ghost role character picker dropdown.
using System.Collections.Generic;
using System.Numerics;
using Content.Client.Eui;
using Content.Shared._Misfits.Ghost.Roles;
using Content.Shared.Eui;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._Misfits.Ghost.Roles;

[UsedImplicitly]
public sealed class MisfitsGhostRoleCharacterPickerEui : BaseEui
{
    private readonly DefaultWindow _window;
    private readonly OptionButton _options;
    private readonly Button _take;
    private readonly Label _empty;

    // Preference slot for each dropdown entry, aligned with its item id.
    private readonly List<int> _characterIndices = new();

    // Set once we are closing, so the window does not send a second close message.
    private bool _closing;

    // How many characters the dropdown is currently offering.
    public int OptionCount => _characterIndices.Count;

    public MisfitsGhostRoleCharacterPickerEui()
    {
        _window = new DefaultWindow
        {
            Title = Loc.GetString("misfits-ghost-role-picker-title"),
            MinSize = new Vector2(420, 160),
        };

        _options = new OptionButton { MinWidth = 300 };

        // OptionButton only reports clicks, so without this a click selects nothing.
        _options.OnItemSelected += args => _options.SelectId(args.Id);
        _take = new Button { Text = Loc.GetString("misfits-ghost-role-picker-take") };
        var cancel = new Button { Text = Loc.GetString("misfits-ghost-role-picker-cancel") };
        _empty = new Label
        {
            Text = Loc.GetString("misfits-ghost-role-picker-none"),
            Visible = false,
        };

        _window.Contents.AddChild(new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            Margin = new Thickness(8),
            Children =
            {
                new Label { Text = Loc.GetString("misfits-ghost-role-picker-description") },
                _options,
                _empty,
                new BoxContainer
                {
                    Orientation = LayoutOrientation.Horizontal,
                    Children =
                    {
                        _take,
                        new Control { MinSize = new Vector2(8, 0) },
                        cancel,
                    },
                },
            },
        });

        _take.OnPressed += _ =>
        {
            if (_options.SelectedId < 0 || _options.SelectedId >= _characterIndices.Count)
                return;

            _closing = true;
            SendMessage(new MisfitsGhostRoleCharacterChosenMessage(_characterIndices[_options.SelectedId]));
            _window.Close();
        };

        cancel.OnPressed += _ => _window.Close();

        _window.OnClose += () =>
        {
            if (_closing)
                return;

            _closing = true;
            SendMessage(new CloseEuiMessage());
        };
    }

    public override void Opened()
    {
        base.Opened();
        _window.OpenCentered();
    }

    public override void Closed()
    {
        base.Closed();
        _closing = true;
        _window.Close();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is not MisfitsGhostRoleCharacterEuiState picker)
            return;

        // Role names are locale keys in the prototype, and plain text when set at runtime.
        _window.Title = Loc.GetString(picker.RoleName);

        _options.Clear();
        _characterIndices.Clear();

        for (var i = 0; i < picker.Options.Length; i++)
        {
            var option = picker.Options[i];
            _options.AddItem(option.Name, i);
            _characterIndices.Add(option.Index);
        }

        // AddItem already highlights the first entry, so nothing else to select here.
        _options.Disabled = picker.Options.Length == 0;
        _take.Disabled = picker.Options.Length == 0;
        _empty.Visible = picker.Options.Length == 0;
    }
}
