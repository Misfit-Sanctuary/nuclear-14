// #Cythisiax Added - Faction roster: consent dialog shown to a player offered recruitment.

using Content.Server.EUI;
using Content.Shared.Eui;
using Content.Shared._Misfits.FactionRoster;

namespace Content.Server._Misfits.FactionRoster;

public sealed class FactionRecruitOfferEui : BaseEui
{
    private readonly string _factionName;
    private readonly string _recruiterName;
    private readonly string _offerText;
    private readonly Action _onAccept;
    private readonly Action _onDecline;
    private bool _resolved;

    public FactionRecruitOfferEui(
        string factionName,
        string recruiterName,
        string offerText,
        Action onAccept,
        Action onDecline)
    {
        _factionName = factionName;
        _recruiterName = recruiterName;
        _offerText = offerText;
        _onAccept = onAccept;
        _onDecline = onDecline;
    }

    public override void Opened()
    {
        base.Opened();
        StateDirty();
    }

    public override EuiStateBase GetNewState()
    {
        return new FactionRecruitOfferEuiState(_factionName, _recruiterName, _offerText);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (_resolved || msg is not FactionRecruitOfferDecisionMessage decision)
            return;

        _resolved = true;
        if (decision.Accepted)
            _onAccept();
        else
            _onDecline();

        Close();
    }

    public override void Closed()
    {
        base.Closed();

        if (_resolved)
            return;

        // Disconnecting or closing the window counts as declining.
        _resolved = true;
        _onDecline();
    }
}
