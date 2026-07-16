// #Misfits Add - console command to spawn in as a mysterious stranger only the named player can see.
using System.Linq;
using Content.Server._Misfits.Administration.MysteriousStranger;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.Server._Misfits.Administration.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed class MysteriousStrangerCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    public string Command => "mysteriousstranger";
    public string Description => Loc.GetString("mysterious-stranger-command-description");
    public string Help => "mysteriousstranger <username>";

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            var names = _playerManager.Sessions.OrderBy(c => c.Name).Select(c => c.Name).ToArray();
            return CompletionResult.FromHintOptions(names, Loc.GetString("shell-argument-username-hint"));
        }

        return CompletionResult.Empty;
    }

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError("Only players can use this command.");
            return;
        }

        if (args.Length != 1)
        {
            shell.WriteError(Help);
            return;
        }

        if (!_playerManager.TryGetSessionByUsername(args[0], out var target))
        {
            shell.WriteError(Loc.GetString("shell-target-player-does-not-exist"));
            return;
        }

        if (target == player)
        {
            shell.WriteError(Loc.GetString("mysterious-stranger-command-self"));
            return;
        }

        if (target.AttachedEntity is not { } targetEntity)
        {
            shell.WriteError(Loc.GetString("mysterious-stranger-command-no-entity"));
            return;
        }

        if (!_entManager.System<MysteriousStrangerSystem>().SpawnStranger(player, targetEntity))
            shell.WriteError(Loc.GetString("mysterious-stranger-command-failed"));
    }
}
