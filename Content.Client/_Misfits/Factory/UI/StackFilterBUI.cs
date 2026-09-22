// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from Goob Station / Trauma Station

using Content.Shared._Misfits.Factory.Filters;

namespace Content.Client._Misfits.Factory.UI;

public sealed class StackFilterBUI : BoundUserInterface
{
    private StackFilterWindow? _window;

    public StackFilterBUI(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<StackFilterWindow>();
        _window.SetEntity(Owner);
        _window.OnSetMin += min => SendPredictedMessage(new StackFilterSetMinMessage(min));
        _window.OnSetSize += size => SendPredictedMessage(new StackFilterSetSizeMessage(size));
    }
}
