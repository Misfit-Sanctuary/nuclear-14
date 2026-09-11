using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._Misfits.Expeditions;

namespace Content.Server._Misfits.Expeditions.Generation;

/// <summary>
/// Builds the high-level grammar for a ruin. This is intentionally pure data:
/// the geometry pass may retry placement without changing what the ruin is.
/// </summary>
public static class UndergroundExpeditionPlanBuilder
{
    public static ExpeditionGenerationPlan Build(
        UndergroundGenParams parameters,
        ThemeProfile profile,
        IReadOnlyCollection<EnvironmentalState> states,
        Random random)
    {
        var hubCount = parameters.FactionSpawnGroups.Count > 0
            ? Math.Clamp(Math.Min(parameters.HubCount, parameters.FactionSpawnGroups.Count), 1, 4)
            : 1;

        var plan = new ExpeditionGenerationPlan
        {
            Seed = parameters.Seed,
            Theme = parameters.Theme,
            Identity = BuildIdentity(parameters.Theme, states, random),
            EntryRoomId = "entry-0",
            ObjectiveRoomId = parameters.Theme switch
            {
                UndergroundTheme.Vault => "vault-core",
                UndergroundTheme.Sewer => "nest",
                UndergroundTheme.Metro => "command",
                _ => throw new ArgumentOutOfRangeException(),
            },
        };

        for (var i = 0; i < hubCount; i++)
        {
            plan.Rooms.Add(new PlannedExpeditionRoom
            {
                Id = $"entry-{i}",
                RoomType = RoomType.FactionHub,
                ZoneRole = ZoneRole.Entry,
                SecurityLevel = 0,
                FactionIndex = parameters.FactionSpawnGroups.Count > i ? i : -1,
            });
        }

        switch (parameters.Theme)
        {
            case UndergroundTheme.Vault:
                BuildVault(plan);
                break;
            case UndergroundTheme.Sewer:
                BuildSewer(plan);
                break;
            case UndergroundTheme.Metro:
                BuildMetro(plan);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        var firstInterior = plan.Connections.First(connection => connection.From == plan.EntryRoomId).To;
        for (var i = 1; i < hubCount; i++)
            Connect(plan, $"entry-{i}", firstInterior);

        AddOptionalRooms(plan, parameters, profile, random);
        AddOptionalLoops(plan, profile, random);
        return plan;
    }

    public static ExpeditionPlanValidationResult Validate(ExpeditionGenerationPlan plan)
    {
        var result = new ExpeditionPlanValidationResult();
        var byId = new Dictionary<string, PlannedExpeditionRoom>();
        foreach (var room in plan.Rooms)
        {
            if (!byId.TryAdd(room.Id, room))
                result.Errors.Add($"duplicate room id '{room.Id}'");
        }

        if (!byId.ContainsKey(plan.EntryRoomId))
            result.Errors.Add($"missing entry room '{plan.EntryRoomId}'");
        if (!byId.ContainsKey(plan.ObjectiveRoomId))
            result.Errors.Add($"missing objective room '{plan.ObjectiveRoomId}'");

        foreach (var connection in plan.Connections)
        {
            if (!byId.ContainsKey(connection.From) || !byId.ContainsKey(connection.To))
                result.Errors.Add($"connection '{connection.From}' -> '{connection.To}' references a missing room");
            if (connection.From == connection.To)
                result.Errors.Add($"room '{connection.From}' connects to itself");
        }

        if (byId.ContainsKey(plan.EntryRoomId))
        {
            var reached = new HashSet<string> { plan.EntryRoomId };
            var queue = new Queue<string>();
            queue.Enqueue(plan.EntryRoomId);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var connection in plan.Connections)
                {
                    string? next = null;
                    if (connection.From == current) next = connection.To;
                    else if (connection.To == current) next = connection.From;
                    if (next != null && reached.Add(next)) queue.Enqueue(next);
                }
            }

            foreach (var room in plan.Rooms.Where(room => room.Required && !reached.Contains(room.Id)))
                result.Errors.Add($"required room '{room.Id}' is disconnected");
        }

        var requiredTypes = plan.Theme switch
        {
            UndergroundTheme.Vault => new[] { RoomType.Central, RoomType.VaultSecurity, RoomType.VaultMaintenance, RoomType.VaultReactor, RoomType.VaultArmory, RoomType.VaultOverseer, RoomType.VaultVault },
            UndergroundTheme.Sewer => new[] { RoomType.SewerJunction, RoomType.SewerPump, RoomType.SewerNest },
            UndergroundTheme.Metro => new[] { RoomType.MetroPlatform, RoomType.MetroDepot, RoomType.MetroCommand },
            _ => Array.Empty<RoomType>(),
        };
        foreach (var roomType in requiredTypes)
        {
            if (plan.Rooms.All(room => room.RoomType != roomType))
                result.Errors.Add($"{plan.Theme} grammar is missing {roomType}");
        }

        return result;
    }

    private static void BuildVault(ExpeditionGenerationPlan plan)
    {
        Add(plan, "atrium", RoomType.Central, ZoneRole.Transit);
        Add(plan, "barracks", RoomType.VaultBarracks, ZoneRole.Entry);
        Add(plan, "kitchen", RoomType.VaultKitchen, ZoneRole.Transit);
        Add(plan, "laboratory", RoomType.VaultLab, ZoneRole.Utility, 1);
        Add(plan, "maintenance", RoomType.VaultMaintenance, ZoneRole.Utility, 1);
        Add(plan, "reactor", RoomType.VaultReactor, ZoneRole.Hazard, 2);
        Add(plan, "security", RoomType.VaultSecurity, ZoneRole.Entry, 1);
        Add(plan, "armory", RoomType.VaultArmory, ZoneRole.Secure, 2);
        Add(plan, "overseer", RoomType.VaultOverseer, ZoneRole.Secure, 2);
        Add(plan, "vault-core", RoomType.VaultVault, ZoneRole.Hazard, 3, true);

        // Vault circulation: the entrance is screened by security before the
        // atrium, residential/support rooms branch from the atrium, and command
        // plus high-value storage occupy the deepest controlled branch.
        Connect(plan, "entry-0", "security");
        Connect(plan, "security", "atrium");
        Connect(plan, "atrium", "barracks");
        Connect(plan, "atrium", "kitchen");
        Connect(plan, "atrium", "laboratory");
        Connect(plan, "atrium", "maintenance");
        Connect(plan, "maintenance", "reactor");
        Connect(plan, "atrium", "armory");
        Connect(plan, "atrium", "overseer");
        Connect(plan, "overseer", "vault-core");
    }

    private static void BuildSewer(ExpeditionGenerationPlan plan)
    {
        Add(plan, "junction", RoomType.SewerJunction, ZoneRole.Entry);
        Add(plan, "main-tunnel", RoomType.SewerTunnel, ZoneRole.Transit);
        Add(plan, "pump", RoomType.SewerPump, ZoneRole.Utility, 1);
        Add(plan, "camp", RoomType.SewerCamp, ZoneRole.Transit);
        Add(plan, "grotto", RoomType.SewerGrotto, ZoneRole.Secure, 1);
        Add(plan, "nest", RoomType.SewerNest, ZoneRole.Hazard, 3, true);

        // A sewer is a chain of one-tile passages, not a multi-exit atrium.
        // This keeps every interior room at two connections or fewer.
        Connect(plan, "entry-0", "junction");
        Connect(plan, "junction", "camp");
        Connect(plan, "camp", "main-tunnel");
        Connect(plan, "main-tunnel", "pump");
        Connect(plan, "pump", "grotto");
        Connect(plan, "grotto", "nest");
    }

    private static void BuildMetro(ExpeditionGenerationPlan plan)
    {
        Add(plan, "platform", RoomType.MetroPlatform, ZoneRole.Entry);
        Add(plan, "track-tunnel", RoomType.MetroTunnel, ZoneRole.Transit);
        Add(plan, "maintenance", RoomType.MetroMaintenance, ZoneRole.Utility, 1);
        Add(plan, "depot", RoomType.MetroDepot, ZoneRole.Secure, 2);
        Add(plan, "command", RoomType.MetroCommand, ZoneRole.Hazard, 3, true);

        Connect(plan, "entry-0", "platform");
        Connect(plan, "platform", "track-tunnel");
        Connect(plan, "track-tunnel", "maintenance");
        Connect(plan, "platform", "depot");
        Connect(plan, "depot", "command");
    }

    private static void AddOptionalRooms(
        ExpeditionGenerationPlan plan,
        UndergroundGenParams parameters,
        ThemeProfile profile,
        Random random)
    {
        var interiorCount = plan.Rooms.Count(room => room.RoomType != RoomType.FactionHub);
        var minimum = Math.Max(parameters.MinRooms, interiorCount);
        var maximum = Math.Max(minimum, parameters.MaxRooms);
        var target = random.Next(minimum, maximum + 1);
        var counts = plan.Rooms.GroupBy(room => room.RoomType).ToDictionary(group => group.Key, group => group.Count());

        while (interiorCount < target)
        {
            var candidates = profile.RoomDefinitions
                .Where(def => counts.GetValueOrDefault(def.RoomType) < def.MaxCount)
                .ToList();
            if (candidates.Count == 0)
                break;

            var totalWeight = candidates.Sum(def => Math.Max(0, def.Weight));
            if (totalWeight <= 0)
                break;
            var roll = random.Next(totalWeight);
            var chosen = candidates[^1];
            foreach (var candidate in candidates)
            {
                roll -= Math.Max(0, candidate.Weight);
                if (roll < 0)
                {
                    chosen = candidate;
                    break;
                }
            }

            var id = $"optional-{interiorCount}-{chosen.RoomType}";
            var role = RoleFor(chosen.RoomType, plan.Theme);
            Add(plan, id, chosen.RoomType, role, Math.Max(0, (int) role - 1), required: false);

            // One-tile sewer doors need a simple, traversable tunnel rather than
            // a growing collection of dead-end branches.  Insert extra chambers
            // immediately before the terminal nest: every room stays reachable,
            // no sewer room gains a third exit, and larger configured ruins can
            // still meet their requested room budget.
            if (plan.Theme == UndergroundTheme.Sewer)
            {
                var objectiveConnection = plan.Connections.Single(edge =>
                    edge.From == plan.ObjectiveRoomId || edge.To == plan.ObjectiveRoomId);
                var predecessorId = objectiveConnection.From == plan.ObjectiveRoomId
                    ? objectiveConnection.To
                    : objectiveConnection.From;

                plan.Connections.Remove(objectiveConnection);
                Connect(plan, predecessorId, id, objectiveConnection.Required);
                Connect(plan, id, plan.ObjectiveRoomId, objectiveConnection.Required);

                counts[chosen.RoomType] = counts.GetValueOrDefault(chosen.RoomType) + 1;
                interiorCount++;
                continue;
            }

            var possibleParents = plan.Rooms
                .Where(room => room.Id != id && room.Id != plan.ObjectiveRoomId && room.ZoneRole <= role)
                .Where(room => plan.Theme != UndergroundTheme.Sewer ||
                               plan.Connections.Count(edge => edge.From == room.Id || edge.To == room.Id) < 2)
                .ToList();
            if (possibleParents.Count == 0)
                break;
            var preferred = possibleParents
                .Where(room => chosen.AdjacencyPreferences.Contains(room.RoomType))
                .ToList();
            var parentPool = preferred.Count > 0 ? preferred : possibleParents;
            var parent = parentPool[random.Next(parentPool.Count)];
            Connect(plan, parent.Id, id, required: false);

            counts[chosen.RoomType] = counts.GetValueOrDefault(chosen.RoomType) + 1;
            interiorCount++;
        }
    }

    private static void AddOptionalLoops(ExpeditionGenerationPlan plan, ThemeProfile profile, Random random)
    {
        // Sewer rooms are deliberately capped at two exits for one-tile doors.
        if (plan.Theme == UndergroundTheme.Sewer)
            return;

        var candidates = plan.Rooms.Where(room => room.RoomType != RoomType.FactionHub).ToList();
        var loopBudget = (int)(candidates.Count * profile.CorridorStyle.BranchingFactor);
        for (var i = 0; i < loopBudget && candidates.Count > 2; i++)
        {
            if (random.NextDouble() >= profile.CorridorStyle.LoopProbability)
                continue;
            var from = candidates[random.Next(candidates.Count)];
            var to = candidates[random.Next(candidates.Count)];
            if (from == to || plan.Connections.Any(edge =>
                    edge.From == from.Id && edge.To == to.Id || edge.From == to.Id && edge.To == from.Id))
                continue;
            Connect(plan, from.Id, to.Id, required: false);
        }
    }

    private static ExpeditionRuinIdentity BuildIdentity(
        UndergroundTheme theme,
        IReadOnlyCollection<EnvironmentalState> states,
        Random random)
    {
        var siteTypes = theme switch
        {
            UndergroundTheme.Vault => new[] { "civil-defense vault", "research shelter", "continuity bunker" },
            UndergroundTheme.Sewer => new[] { "municipal interceptor", "flood-control works", "industrial drainage network" },
            UndergroundTheme.Metro => new[] { "commuter station", "freight interchange", "civil-defense transit station" },
            _ => new[] { "underground ruin" },
        };
        var failure = states.Contains(EnvironmentalState.Flooded) ? "catastrophic flooding"
            : states.Contains(EnvironmentalState.Damaged) ? "structural collapse"
            : states.Contains(EnvironmentalState.Overgrown) ? "ecological reclamation"
            : states.Contains(EnvironmentalState.Abandoned) ? "abandonment and systems failure"
            : "an orderly evacuation";
        var current = theme switch
        {
            UndergroundTheme.Vault => "sealed security zones and scavenger intrusion",
            UndergroundTheme.Sewer => "survivor traces and territorial creatures",
            UndergroundTheme.Metro => "dead infrastructure and hostile squatters",
            _ => "unknown occupation",
        };
        return new ExpeditionRuinIdentity
        {
            SiteType = siteTypes[random.Next(siteTypes.Length)],
            FailureCause = failure,
            CurrentState = current,
        };
    }

    private static void Add(
        ExpeditionGenerationPlan plan,
        string id,
        RoomType roomType,
        ZoneRole role,
        int security = 0,
        bool objective = false,
        bool required = true)
    {
        plan.Rooms.Add(new PlannedExpeditionRoom
        {
            Id = id,
            RoomType = roomType,
            ZoneRole = role,
            SecurityLevel = security,
            IsObjective = objective,
            Required = required,
        });
    }

    private static void Connect(ExpeditionGenerationPlan plan, string from, string to, bool required = true)
    {
        plan.Connections.Add(new PlannedExpeditionConnection { From = from, To = to, Required = required });
    }

    private static ZoneRole RoleFor(RoomType roomType, UndergroundTheme theme)
    {
        return roomType switch
        {
            RoomType.FactionHub => ZoneRole.Entry,
            RoomType.Central or RoomType.VaultSecurity or RoomType.VaultKitchen or RoomType.VaultBarracks or RoomType.SewerJunction or RoomType.MetroPlatform => ZoneRole.Entry,
            RoomType.VaultHydroponics or RoomType.VaultRecreation or RoomType.SewerTunnel or RoomType.SewerCamp or RoomType.MetroTunnel => ZoneRole.Transit,
            RoomType.VaultLab or RoomType.VaultMaintenance or RoomType.SewerPump or RoomType.MetroMaintenance => ZoneRole.Utility,
            RoomType.VaultArmory or RoomType.VaultOverseer or RoomType.SewerGrotto or RoomType.MetroDepot => ZoneRole.Secure,
            RoomType.VaultVault or RoomType.VaultReactor or RoomType.SewerNest or RoomType.MetroCommand => ZoneRole.Hazard,
            _ => theme == UndergroundTheme.Vault ? ZoneRole.Utility : ZoneRole.Transit,
        };
    }
}
