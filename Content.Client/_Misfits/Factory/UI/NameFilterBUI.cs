// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from Goob Station / Trauma Station

using Content.Shared._Misfits.Factory.Filters;

namespace Content.Client._Misfits.Factory.UI;

public sealed class NameFilterBUI : BoundUserInterface
{
    private NameFilterWindow? _window;

    public NameFilterBUI(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<NameFilterWindow>();
        _window.SetEntity(Owner);
        _window.OnSetName += name => SendPredictedMessage(new NameFilterSetNameMessage(name));
        _window.OnSetMode += mode => SendPredictedMessage(new NameFilterSetModeMessage(mode));
    }
}
