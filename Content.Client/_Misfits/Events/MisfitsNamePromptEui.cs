// #Misfits Add - client side of the "name the mob you just took over" prompt.
using System.Numerics;
using Content.Client.Eui;
using Content.Shared._Misfits.Events;
using Content.Shared.Eui;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._Misfits.Events;

[UsedImplicitly]
public sealed class MisfitsNamePromptEui : BaseEui
{
    private readonly DefaultWindow _window;
    private readonly LineEdit _name;

    // Set once we are closing, so the window does not send a second close message.
    private bool _closing;

    public MisfitsNamePromptEui()
    {
        _window = new DefaultWindow
        {
            Title = Loc.GetString("misfits-name-prompt-title"),
            MinSize = new Vector2(380, 150),
        };

        _name = new LineEdit { MinWidth = 320 };
        var take = new Button { Text = Loc.GetString("misfits-name-prompt-take") };
        var keep = new Button { Text = Loc.GetString("misfits-name-prompt-keep") };

        _window.Contents.AddChild(new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            Margin = new Thickness(8),
            Children =
            {
                new Label { Text = Loc.GetString("misfits-name-prompt-description") },
                _name,
                new BoxContainer
                {
                    Orientation = LayoutOrientation.Horizontal,
                    Children =
                    {
                        take,
                        new Control { MinSize = new Vector2(8, 0) },
                        keep,
                    },
                },
            },
        });

        take.OnPressed += _ => Submit();
        _name.OnTextEntered += _ => Submit();

        // Keeping the default name just closes the prompt.
        keep.OnPressed += _ => _window.Close();

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
        _name.GrabKeyboardFocus();
    }

    public override void Closed()
    {
        base.Closed();
        _closing = true;
        _window.Close();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is not MisfitsNamePromptEuiState prompt)
            return;

        _window.Title = prompt.Title;

        // Only prefill before the player has typed anything.
        if (string.IsNullOrEmpty(_name.Text))
            _name.Text = prompt.CurrentName;
    }

    private void Submit()
    {
        var name = _name.Text.Trim();

        // The server rejects empty names, so leave the window up until something is typed.
        if (name.Length == 0)
            return;

        _closing = true;
        SendMessage(new MisfitsNamePromptSubmittedMessage(name));
        _window.Close();
    }
}
