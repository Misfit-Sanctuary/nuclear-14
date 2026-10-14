// #Cythisiax Added - report current player population by job faction to Prometheus.
using System.Linq;
using Content.Shared.GameTicking;
using Content.Shared.Players;
using Content.Shared.Roles;
using Prometheus;
using Robust.Server.DataMetrics;
using Robust.Shared.Enums;

namespace Content.Server.GameTicking;

public sealed partial class GameTicker
{
    [Dependency] private readonly IMetricsManager _factionMetricsManager = default!;

    private static readonly Gauge FactionPlayerCountMetric = Metrics.CreateGauge(
        "ss14_faction_player_count",
        "Connected players currently alive or critical in the round, grouped by their job faction.",
        "faction");

    private void InitializeFactionMetrics()
    {
        _factionMetricsManager.UpdateMetrics += UpdateFactionMetrics;
    }

    private void ShutdownFactionMetrics()
    {
        _factionMetricsManager.UpdateMetrics -= UpdateFactionMetrics;
    }

    private void UpdateFactionMetrics()
    {
        var counts = new Dictionary<string, int>();
        var jobFactions = new Dictionary<string, string>();

        // Keep a zero series for every faction, even when it has no players.
        // Robot and mutant departments overlap actual factions, so use them only as a fallback.
        foreach (var department in _prototypeManager.EnumeratePrototypes<DepartmentPrototype>()
                     .OrderBy(department => department.ID, StringComparer.Ordinal))
        {
            if (department.UICategory == DepartmentUICategory.NoFaction && department.ID != "Wastelander")
                continue;

            var faction = CanonicalFaction(department.ID);
            counts.TryAdd(faction, 0);

            foreach (var role in department.Roles)
            {
                if (!jobFactions.TryGetValue(role.Id, out var existing) ||
                    existing is "Robots" or "FEVMutants")
                    jobFactions[role.Id] = faction;
            }
        }

        // C-27's faction postings share one department, but belong in their posted faction.
        jobFactions["C27NCR"] = "NCR";
        jobFactions["C27BoS"] = "BrotherhoodOfSteel";

        if (RunLevel == GameRunLevel.InRound)
        {
            foreach (var session in _playerManager.Sessions)
            {
                if (session.Status != SessionStatus.InGame ||
                    !UserHasJoinedGame(session) ||
                    session.AttachedEntity is not { } attached ||
                    (!_mobState.IsAlive(attached) && !_mobState.IsCritical(attached)) ||
                    session.GetMind() is not { } mind ||
                    !_jobs.MindTryGetJob(mind, out _, out var job) ||
                    !jobFactions.TryGetValue(job.ID, out var faction))
                    continue;

                counts[faction]++;
            }
        }

        foreach (var (faction, count) in counts)
            FactionPlayerCountMetric.WithLabels(faction).Set(count);
    }

    private static string CanonicalFaction(string departmentId)
    {
        return departmentId switch
        {
            "Rangers" or "MilitaryPolice" or "OSI" => "NCR",
            "BrotherhoodOfSteelLancers" or "BoSPersonalWhitelist" => "BrotherhoodOfSteel",
            _ => departmentId,
        };
    }
}
