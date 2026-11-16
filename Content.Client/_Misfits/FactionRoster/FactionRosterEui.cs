// #Cythisiax Added - Faction roster: client side of the faction lead's roster window.

using Content.Client.Eui;
using Content.Shared.Eui;
using Content.Shared._Misfits.FactionRoster;
using JetBrains.Annotations;

namespace Content.Client._Misfits.FactionRoster;

[UsedImplicitly]
public sealed class FactionRosterEui : BaseEui
{
    private readonly FactionRosterWindow _window;

    public FactionRosterEui()
    {
        _window = new FactionRosterWindow();
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.OnRefresh += () => SendMessage(new FactionRosterRefreshMessage());
        _window.OnPromote += (id, track) => SendMessage(new FactionRosterPromoteMessage(id, track));
        _window.OnDemote += (id, track) => SendMessage(new FactionRosterDemoteMessage(id, track));
        _window.OnDischarge += id => SendMessage(new FactionRosterDischargeMessage(id));
        _window.OnToggleSpecialty += (id, role) => SendMessage(new FactionRosterToggleSpecialtyMessage(id, role));
        _window.OnRecruit += target => SendMessage(new FactionRosterRecruitMessage(target));
        _window.OnSetOath += oath => SendMessage(new FactionRosterSetOathMessage(oath));
    }

    public override void Opened()
    {
        base.Opened();
        _window.OpenCentered();
    }

    public override void Closed()
    {
        base.Closed();
        _window.Close();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is FactionRosterEuiState rosterState)
            _window.UpdateState(rosterState);
    }
}
