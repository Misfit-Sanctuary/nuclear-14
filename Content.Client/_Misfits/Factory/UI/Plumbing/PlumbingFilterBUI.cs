// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from Goob Station / Trauma Station

using Content.Shared._Misfits.Factory.Plumbing;

namespace Content.Client._Misfits.Factory.UI.Plumbing;

public sealed class PlumbingFilterBUI : BoundUserInterface
{
    private PlumbingFilterWindow? _window;

    public PlumbingFilterBUI(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<PlumbingFilterWindow>();
        _window.SetEntity(Owner);
        _window.OnChange += id => SendPredictedMessage(new PlumbingFilterChangeMessage(id));
    }
}
