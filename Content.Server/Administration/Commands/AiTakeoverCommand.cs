using Content.Shared.Administration;
using Content.Shared.Silicons.StationAi;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.Server.Administration.Commands;

[AdminCommand(AdminFlags.Debug)]
public sealed class AiTakeoverCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    public string Command => "aitakeover";
    public string Description => "Spawns a Station AI brain into an empty AI core and gives it to a player.";
    public string Help => "aitakeover <target core> <player username>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError("Wrong number of arguments. Usage: " + Help);
            return;
        }

        if (!NetEntity.TryParse(args[0], out var coreNetEntity) ||
            !_entityManager.TryGetEntity(coreNetEntity, out var coreUid))
        {
            shell.WriteError("Invalid target core entity.");
            return;
        }

        if (!_entityManager.TryGetComponent(coreUid.Value, out StationAiCoreComponent? core))
        {
            shell.WriteError("Target entity is not a Station AI core.");
            return;
        }

        if (!_playerManager.TryGetSessionByUsername(args[1], out var player))
        {
            shell.WriteError(Loc.GetString("parse-session-fail", ("username", args[1])));
            return;
        }

        var stationAi = _entityManager.System<SharedStationAiSystem>();
        if (!stationAi.TryTakeoverEmptyCore((coreUid.Value, core), player.UserId, out var brain, out var error))
        {
            shell.WriteError(error ?? "Failed to take over Station AI core.");
            return;
        }

        shell.WriteLine($"Transferred {player.Name} into Station AI brain {_entityManager.GetNetEntity(brain.Value)}.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHint("<target core>"),
            2 => CompletionResult.FromHintOptions(
                CompletionHelper.SessionNames(players: _playerManager),
                "<player username>"),
            _ => CompletionResult.Empty
        };
    }
}
