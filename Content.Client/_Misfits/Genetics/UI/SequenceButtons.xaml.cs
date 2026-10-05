// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Misfits.Genetics.Mutations;

namespace Content.Client._Misfits.Genetics.UI;

[GenerateTypedNameReferences]
public sealed partial class SequenceButtons : ScrollContainer
{
    public event Action<uint>? OnSelected;

    private int? _selected;
    private List<SequenceState> _sequences = new();
    private List<BaseButton> _buttons = new();

    public uint? Index
    {
        get
        {
            for (int i = 0; i < _sequences.Count; i++)
            {
                if (_sequences[i].Number == _selected)
                    return (uint) i;
            }

            return null;
        }
    }

    public SequenceState? Sequence
        => Index is {} i ? _sequences[(int) i] : null;

    public SequenceButtons()
    {
        RobustXamlLoader.Load(this);

        OnSelected += sel =>
        {
            var number = sel < _sequences.Count ? _sequences[(int) sel].Number : (int?) null;
            _selected = _selected == number
                ? null
                : number;

            for (int i = 0; i < _buttons.Count; i++)
            {
                _buttons[i].Pressed = _selected != null && _sequences[i].Number == _selected;
            }
        };
    }

    public void SetStates(List<SequenceState> states)
    {
        _sequences = states;
    }

    public void UpdateSequences()
    {
        _buttons.Clear();
        Buttons.RemoveAllChildren();
        for (int i = 0; i < _sequences.Count; i++)
        {
            var sequence = _sequences[i];
            var index = (uint) i;
            var rarity = sequence.Rarity.RarityChar();
            var text = Loc.GetString("genetics-console-sequence-text", ("rarity", rarity), ("number", sequence.Number));
            var button = new Button()
            {
                // TODO: use a wrapping shader or something to do helix animated button
                Text = text,
                ToggleMode = true,
                HorizontalExpand = true
            };
            button.Pressed = sequence.Number == _selected;
            button.OnPressed += _ => OnSelected?.Invoke(index);
            /*button.AddChild(new Label()
            {
                Text = text,
                HorizontalAlignment = HAlignment.Center
            });*/
            _buttons.Add(button);
            Buttons.AddChild(button);
        }
    }
}
