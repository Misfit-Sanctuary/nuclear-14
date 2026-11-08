// #Cythisiax Added - Faction roster: client side of the recruitment consent dialog.

using Content.Client.Eui;
using Content.Shared.Eui;
using Content.Shared._Misfits.FactionRoster;
using JetBrains.Annotations;
using Robust.Client.Graphics;

namespace Content.Client._Misfits.FactionRoster;

[UsedImplicitly]
public sealed class FactionRecruitOfferEui : BaseEui
{
    private readonly FactionRecruitOfferWindow _window;
    private bool _responded;

    public FactionRecruitOfferEui()
    {
        _window = new FactionRecruitOfferWindow();
        _window.OnAccepted += () => Respond(true);
        _window.OnDeclined += () => Respond(false);
        _window.OnClose += () => Respond(false);
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is FactionRecruitOfferEuiState offer)
            _window.SetOffer(offer.FactionName, offer.RecruiterName, offer.OfferText);
    }

    public override void Opened()
    {
        base.Opened();
        IoCManager.Resolve<IClyde>().RequestWindowAttention();
        _window.OpenCentered();
    }

    public override void Closed()
    {
        base.Closed();
        _responded = true;
        _window.Close();
    }

    private void Respond(bool accepted)
    {
        if (_responded)
            return;

        _responded = true;
        SendMessage(new FactionRecruitOfferDecisionMessage(accepted));
        _window.Close();
    }
}
