// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Input;
using Content.Shared._Misfits.Genetics.Console;
using Robust.Shared.Input;

namespace Content.Client._Misfits.Genetics.UI;

[GenerateTypedNameReferences]
public sealed partial class GenePuzzle : Control
{
    public event Action<uint, GeneticsCycle>? OnSetBase;
    public event Action? OnSequence;
    public event Action? OnResetSequence;

    private bool _busy;
    private bool _sequenced;
    private bool _writable = true;
    private string _bases = string.Empty;
    private string _originalBases = string.Empty;

    public static readonly Color Blue = Color.FromHex("#1c71b1");
    public static readonly Color Green = Color.FromHex("#1b9638");

    public GenePuzzle()
    {
        RobustXamlLoader.Load(this);

        SequenceButton.OnPressed += _ => OnSequence?.Invoke();
        ResetSequenceButton.OnPressed += _ => OnResetSequence?.Invoke();
        OnSetBase += (_, _) => UpdateSequenceButton();
    }

    public void MakeReadonly()
    {
        _writable = false;
        Tip.Visible = false; // clicking won't do anything
        SequenceButtonContainer.Visible = false;
    }

    private void UpdateSequenceButton()
    {
        SequenceButton.Disabled = _busy || _sequenced || !IsComplete();
        ResetSequenceButton.Disabled = _busy || _bases == _originalBases;
    }

    private bool IsComplete()
    {
        foreach (var c in _bases)
        {
            if (c == 'X') return false;
        }

        return true;
    }

    public void SetBusy(bool busy)
    {
        _busy = busy;
        UpdateSequenceButton();
    }

    public void SetSequenced(bool sequenced)
    {
        _sequenced = sequenced;
        UpdateSequenceButton();
    }

    public void SetBases(string bases, string originalBases)
    {
        Visible = true;
        _bases = bases;
        _originalBases = originalBases;

        if (BaseButtons.ChildCount != bases.Length)
        {
            BaseButtons.RemoveAllChildren();
            for (int i = 0; i < bases.Length; i++)
            {
                AddBase((uint) i);
            }
        }

        for (int i = 0; i < bases.Length; i++)
        {
            var button = (GeneBaseButton) BaseButtons.GetChild(i);
            button.Disabled = originalBases[i] != 'X'; // can't cycle bases that are guaranteed known
            button.Typable = _writable;
            SetButtonBase(button, bases[i]);
        }
        UpdateSequenceButton();
    }

    private void AddBase(uint i)
    {
        var button = new GeneBaseButton();
        void Cycle(GeneticsCycle cycle)
        {
            if (_busy || !_writable || button.Disabled)
                return;

            var b = GeneticsConsoleSystem.CycleBase(button.Base, cycle);
            if (b == button.Base)
                return; // typed the base it already was

            SetButtonBase(button, b);
            var chars = _bases.ToCharArray();
            chars[i] = b;
            _bases = new string(chars);
            OnSetBase?.Invoke(i, cycle);
        }

        button.OnKeyBindDown += args =>
        {
            if (args.Function == EngineKeyFunctions.UIRightClick)
                Cycle(GeneticsCycle.Last);
            else if (args.Function == ContentKeyFunctions.TryPullObject) // Ctrl click
                Cycle(GeneticsCycle.Reset);
        };
        button.OnPressed += _ =>
        {
            Cycle(GeneticsCycle.Next);
        };
        button.OnTyped += Cycle;
        BaseButtons.AddChild(button);
    }

    private void SetButtonBase(GeneBaseButton button, char b)
    {
        button.Base = b;
        button.Text = b.ToString();
        button.ModulateSelfOverride = GetColor(b);
    }

    private Color? GetColor(char b)
        => b switch
        {
            'A' => Green,
            'T' => Green,
            'G' => Blue,
            'C' => Blue,
            _ => null
        };

    private sealed class GeneBaseButton : Button
    {
        public char Base = 'X';

        public bool Typable = true;

        public event Action<GeneticsCycle>? OnTyped;

        public GeneBaseButton()
        {
            CanKeyboardFocus = true;
        }

        protected override void MouseEntered()
        {
            base.MouseEntered();

            // dont steal focus from chat etc
            if (Typable && !Disabled && UserInterfaceManager.KeyboardFocused is null or GeneBaseButton)
                GrabKeyboardFocus();
        }

        protected override void MouseExited()
        {
            base.MouseExited();
            ReleaseKeyboardFocus();
        }

        protected override void KeyboardFocusEntered()
        {
            base.KeyboardFocusEntered();
            Root?.Window?.TextInputStart();
        }

        protected override void KeyboardFocusExited()
        {
            base.KeyboardFocusExited();
            Root?.Window?.TextInputStop();
        }

        protected override void TextEntered(GUITextEnteredEventArgs args)
        {
            base.TextEntered(args);

            foreach (var c in args.Text)
            {
                GeneticsCycle? cycle = c switch
                {
                    'A' or 'a' => GeneticsCycle.A,
                    'C' or 'c' => GeneticsCycle.C,
                    'G' or 'g' => GeneticsCycle.G,
                    'T' or 't' => GeneticsCycle.T,
                    'X' or 'x' => GeneticsCycle.Reset,
                    _ => null
                };

                if (cycle is {} typed)
                    OnTyped?.Invoke(typed);
            }
        }
    }
}
