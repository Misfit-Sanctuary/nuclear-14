using Content.Server._Misfits.WastelandMap;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Misfits.Administration.Commands;

/// <summary>Opens the round-scoped global map pin manager.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class PinMapCommand : IConsoleCommand
{
    [Dependency] private readonly IEntitySystemManager _systems = default!;

    public string Command => "pinmap";
    public string Description => "Opens the global tactical-map pin manager.";
    public string Help => "pinmap";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError(Help);
            return;
        }

        if (shell.Player?.AttachedEntity is not { Valid: true } actor)
        {
            shell.WriteError("You must have an attached in-game entity to manage map pins.");
            return;
        }

        var mapPins = _systems.GetEntitySystem<MapPinSystem>();
        if (!mapPins.TryOpenManager(actor))
            shell.WriteError("Could not open the map pin manager.");
    }
}
