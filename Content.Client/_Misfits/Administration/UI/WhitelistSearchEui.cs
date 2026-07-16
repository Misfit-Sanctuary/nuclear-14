// #Misfits Change - Client-side EUI for the Whitelist Search admin panel
using Content.Client.Eui;
using Content.Shared._Misfits.Administration;
using Content.Shared.Eui;

namespace Content.Client._Misfits.Administration.UI;

public sealed class WhitelistSearchEui : BaseEui
{
    private WhitelistSearchWindow _window;

    public WhitelistSearchEui()
    {
        _window = new WhitelistSearchWindow();
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.OnSearch += query => SendMessage(new SearchPlayersMessage(query));
        _window.OnSelectPlayer += playerId => SendMessage(new SelectPlayerMessage(playerId));
        _window.OnSetJobs += (jobs, whitelisting) => SendMessage(new SetWhitelistSearchJobMessage(jobs, whitelisting));
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is not WhitelistSearchEuiState cast)
            return;

        _window.HandleState(cast);
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
        _window.Dispose();
    }
}
