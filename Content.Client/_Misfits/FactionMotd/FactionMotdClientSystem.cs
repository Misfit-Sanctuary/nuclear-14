// #Cythisiax Added - Faction MOTD: registers /factionmotd (alias /fmotd) and owns the MOTD
// window. The server decides faction membership and leadership; the client only shows
// whatever state it is sent, including the automatic popup when a member spawns in.

using Content.Shared._Misfits.FactionMotd;
using Robust.Client.Console;
using Robust.Shared.Console;

namespace Content.Client._Misfits.FactionMotd;

public sealed class FactionMotdClientSystem : EntitySystem
{
    [Dependency] private readonly IClientConsoleHost _conHost = default!;

    private FactionMotdWindow? _window;

    /// <summary>The player explicitly asked for the window, so open it on the next state.</summary>
    private bool _openRequested;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<FactionMotdStateMsg>(OnState);

        _conHost.RegisterCommand("factionmotd",
            Loc.GetString("faction-motd-command-description"),
            "factionmotd",
            OpenCommand);
        _conHost.RegisterCommand("fmotd",
            Loc.GetString("faction-motd-command-description"),
            "fmotd",
            OpenCommand);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _conHost.UnregisterCommand("factionmotd");
        _conHost.UnregisterCommand("fmotd");
        _window?.Close();
        _window = null;
    }

    private void OpenCommand(IConsoleShell shell, string argStr, string[] args)
    {
        _openRequested = true;
        RaiseNetworkEvent(new FactionMotdOpenRequestMsg());
    }

    private void OnState(FactionMotdStateMsg msg)
    {
        var fresh = _window == null;

        // Status replies (saves, refreshes) only update a window that is already open.
        if (fresh && !_openRequested && !msg.OnSpawn)
            return;

        _openRequested = false;

        if (_window == null)
        {
            _window = new FactionMotdWindow();
            _window.OnClose += () => _window = null;
            _window.OnRefresh += () => RaiseNetworkEvent(new FactionMotdOpenRequestMsg());
            _window.OnSave += text => RaiseNetworkEvent(new FactionMotdSetMsg { Text = text });
        }

        _window.UpdateState(msg);

        if (fresh)
        {
            _window.ShowEditorIfEmpty(msg);
            _window.OpenCentered();
        }
        else
        {
            _window.MoveToFront();
        }
    }
}
