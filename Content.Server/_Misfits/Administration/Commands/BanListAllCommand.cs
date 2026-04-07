using Content.Server._Misfits.Administration.BanList;
using Content.Server.Administration;
using Content.Server.EUI;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Player;

// #Misfits Add - banlistall command: opens a GUI showing all active and historical bans server-wide
namespace Content.Server._Misfits.Administration.Commands;

[AdminCommand(AdminFlags.Ban)]
public sealed class BanListAllCommand : LocalizedCommands
{
    [Dependency] private readonly EuiManager _eui = default!;

    public override string Command => "banlistall";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        // This command only works from an in-game client, not the server console,
        // because it opens a GUI window.
        if (shell.Player is not ICommonSession player)
        {
            shell.WriteError(Loc.GetString("cmd-banlistall-console-only"));
            return;
        }

        // Open the global ban list EUI for this admin
        var eui = new MisfitsBanListAllEui();
        _eui.OpenEui(eui, player);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return CompletionResult.Empty;
    }
}
