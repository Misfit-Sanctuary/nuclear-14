using Content.Server.Administration;
using Content.Shared._Misfits.Expeditions;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Server._Misfits.Administration.Commands;

/// <summary>
/// Lists the currently spawned surface entrances so expedition placement can be
/// verified from the server console without relying on the tactical map.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class ExpeditionEntrancesCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    public string Command => "expeditionentrances";
    public string Description => "Lists active Auspicious Entry expedition entrances and their map coordinates.";
    public string Help => "expeditionentrances";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError(Help);
            return;
        }

        var transformSystem = _entManager.System<SharedTransformSystem>();
        var entrances = new List<(EntityUid Uid, string Prototype, MapCoordinates Coordinates)>();
        var query = _entManager.AllEntityQueryEnumerator<N14ExpeditionBoardComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var board, out var transform))
        {
            if (!board.DirectLaunch)
                continue;

            var prototype = _entManager.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID ?? "<runtime>";
            entrances.Add((uid, prototype, transformSystem.GetMapCoordinates(uid, transform)));
        }

        if (entrances.Count == 0)
        {
            shell.WriteError("No Auspicious Entry expedition entrances are active. Expected entrances beside distinct FloraTreeStump landmarks.");
            return;
        }

        shell.WriteLine($"Found {entrances.Count} active Auspicious Entry expedition entrance(s):");
        foreach (var entrance in entrances)
        {
            shell.WriteLine($"  {entrance.Prototype} ({entrance.Uid}): {entrance.Coordinates}");
        }
    }
}
