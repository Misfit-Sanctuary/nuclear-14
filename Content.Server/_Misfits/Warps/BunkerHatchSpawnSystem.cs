// #Misfits Add - Spawns the surface bunker hatches at random spots each round.
using System.Linq;
using Content.Server.Administration;
using Content.Server.GameTicking;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Random;

namespace Content.Server._Misfits.Warps;

/// <summary>
/// At round start, picks <see cref="HatchesPerRound"/> of the mapper-placed
/// <see cref="BunkerHatchSpawnPointComponent"/> markers at random (per tunnel channel) and puts a
/// bunker hatch on each, labelled "Hatch A", "Hatch B", and so on. Runs on
/// <see cref="RoundStartedEvent"/>, after every map (including the MultiZ levels) has loaded.
/// </summary>
public sealed class BunkerHatchSpawnSystem : EntitySystem
{
    public const int HatchesPerRound = 2;
    private const string HatchPrototype = "N14BunkerHatchTunnel";

    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IConsoleHost _console = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private ISawmill _sawmill = default!;

    /// <summary>
    /// Hatches this system spawned, so a reroll can clear them without touching hand-placed ones.
    /// </summary>
    private readonly List<EntityUid> _spawned = new();

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("bunker.hatch");

        SubscribeLocalEvent<RoundStartedEvent>(_ => SpawnRoundHatches());

        _console.RegisterCommand("bunkerhatchreroll",
            Loc.GetString("bunker-hatch-reroll-command-description"),
            "bunkerhatchreroll",
            RerollCommand);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _console.UnregisterCommand("bunkerhatchreroll");
    }

    [AdminCommand(AdminFlags.Mapping)]
    private void RerollCommand(IConsoleShell shell, string argStr, string[] args)
    {
        var count = SpawnRoundHatches();
        shell.WriteLine(Loc.GetString("bunker-hatch-reroll-command-done", ("count", count)));
    }

    /// <summary>
    /// Clears the hatches from last time and spawns a fresh set. Returns how many were spawned.
    /// </summary>
    public int SpawnRoundHatches()
    {
        foreach (var old in _spawned)
        {
            if (!TerminatingOrDeleted(old))
                QueueDel(old);
        }

        _spawned.Clear();

        var byChannel = new Dictionary<string, List<EntityUid>>();
        var query = EntityQueryEnumerator<BunkerHatchSpawnPointComponent>();
        while (query.MoveNext(out var uid, out var point))
        {
            if (!byChannel.TryGetValue(point.Channel, out var list))
                byChannel[point.Channel] = list = new List<EntityUid>();

            list.Add(uid);
        }

        foreach (var (channel, points) in byChannel)
        {
            if (points.Count < HatchesPerRound)
            {
                _sawmill.Warning($"Only {points.Count} bunker hatch spawn point(s) on channel '{channel}', wanted {HatchesPerRound}.");
            }

            _random.Shuffle(points);
            var picked = points.Take(HatchesPerRound).ToList();

            for (var i = 0; i < picked.Count; i++)
            {
                var hatch = Spawn(HatchPrototype, _transform.GetMapCoordinates(picked[i]));
                var teleporter = Comp<BunkerTeleporterComponent>(hatch);
                teleporter.Channel = channel;
                teleporter.Label = Loc.GetString("bunker-hatch-label", ("letter", (char) ('A' + i)));
                _spawned.Add(hatch);
            }
        }

        return _spawned.Count;
    }
}
