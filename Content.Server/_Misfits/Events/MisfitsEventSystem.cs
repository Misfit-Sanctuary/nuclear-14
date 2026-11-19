using System.Linq;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking.Rules;
using Content.Server._Misfits.Ghost.Roles;
using Content.Server.Radio.EntitySystems;
using Content.Shared._Misfits.Events;
using Content.Shared.Dataset;
using Content.Shared.GameTicking.Components;
using Content.Shared.Ghost;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Misfits.Events;

// Fork event pool: eligibility, weighting and spawning. Only MisfitsEvent prototypes are ever picked.
public sealed class MisfitsEventSystem : GameRuleSystem<MisfitsEventComponent>
{
    private const int SpawnAttempts = 25;
    private const float MinSpawnDistance = 2f;


    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;

    // Events an admin switched off; they stay in the pool, but no roll picks them.
    private readonly HashSet<string> _disabled = new();

    // Waves still to arrive, per running event rule.
    private readonly Dictionary<EntityUid, List<PendingWave>> _waves = new();

    private sealed class PendingWave
    {
        public TimeSpan Due;
        public MapCoordinates Around;
        public float Radius;
        public List<MisfitsEventSpawnEntry> Spawns = new();
        public int CountMin;
        public int CountMax;
        public string? Announcement;
    }

    #region Pool

    public bool IsEventDisabled(string eventId)
    {
        return _disabled.Contains(eventId);
    }

    public void SetEventDisabled(string eventId, bool disabled)
    {
        if (disabled)
            _disabled.Add(eventId);
        else
            _disabled.Remove(eventId);
    }

    // Every fork event, eligible or not.
    public Dictionary<EntityPrototype, MisfitsEventComponent> AllEvents()
    {
        var events = new Dictionary<EntityPrototype, MisfitsEventComponent>();
        foreach (var proto in _prototype.EnumeratePrototypes<EntityPrototype>())
        {
            if (proto.Abstract)
                continue;

            if (!proto.TryGetComponent<MisfitsEventComponent>(out var component))
                continue;

            events.Add(proto, component);
        }

        return events;
    }

    // Events a roll may pick right now, after the player, time and occurrence limits.
    public Dictionary<EntityPrototype, MisfitsEventComponent> AvailableEvents(
        int? playerCountOverride = null,
        TimeSpan? roundTimeOverride = null)
    {
        var playerCount = playerCountOverride ?? _playerManager.PlayerCount;
        var roundTime = roundTimeOverride ?? GameTicker.RoundDuration();

        var available = new Dictionary<EntityPrototype, MisfitsEventComponent>();
        foreach (var (proto, component) in AllEvents())
        {
            if (CanRun(proto, component, playerCount, roundTime))
                available.Add(proto, component);
        }

        return available;
    }

    private bool CanRun(EntityPrototype proto, MisfitsEventComponent component, int playerCount, TimeSpan roundTime)
    {
        if (IsEventDisabled(proto.ID))
            return false;

        if (GameTicker.IsGameRuleAdded(proto.ID))
            return false;

        if (component.MaxOccurrences is { } max && GetOccurrences(proto.ID) >= max)
            return false;

        if (playerCount < component.MinimumPlayers)
            return false;

        if (roundTime.TotalMinutes < component.EarliestStart)
            return false;

        var lastRun = LastRunTime(proto.ID);
        if (lastRun != TimeSpan.Zero && roundTime.TotalMinutes < component.ReoccurrenceDelay + lastRun.TotalMinutes)
            return false;

        return true;
    }

    // How often this event has been started this round.
    public int GetOccurrences(string eventId)
    {
        return GameTicker.AllPreviousGameRules.Count(rule => rule.Item2 == eventId);
    }

    // Round time this event last started, or zero if it never has.
    private TimeSpan LastRunTime(string eventId)
    {
        foreach (var (time, rule) in GameTicker.AllPreviousGameRules.Reverse())
        {
            if (rule == eventId)
                return time;
        }

        return TimeSpan.Zero;
    }

    // Weighted pick from a pool; null when nothing can be picked.
    public string? PickEvent(Dictionary<EntityPrototype, MisfitsEventComponent> pool)
    {
        var entries = pool.Select(pair => (pair.Key.ID, pair.Value.Weight)).ToList();
        return TryPickWeighted(entries, out var picked) ? picked : null;
    }

    #endregion

    #region Running

    // Starts an event by prototype id, ignoring eligibility.
    public EntityUid? RunEvent(string eventId)
    {
        if (!_prototype.TryIndex<EntityPrototype>(eventId, out var proto) ||
            !proto.TryGetComponent<MisfitsEventComponent>(out _))
        {
            Log.Error($"'{eventId}' is not a Misfits event prototype.");
            return null;
        }

        if (!GameTicker.StartGameRule(eventId, out var rule))
        {
            Log.Warning($"Could not start Misfits event {eventId}, it may already be running.");
            return null;
        }

        var message = $"Started Misfits event {eventId}";
        Log.Info(message);
        ChatManager.SendAdminAlert(message);
        return rule;
    }

    // Rolls a weighted event from the eligible pool and starts it.
    public EntityUid? RunRandomEvent()
    {
        var pool = AvailableEvents();
        var picked = PickEvent(pool);
        if (picked == null)
        {
            Log.Warning("No Misfits events were eligible to roll.");
            return null;
        }

        return RunEvent(picked);
    }

    #endregion

    #region Spawning

    protected override void Started(EntityUid uid, MisfitsEventComponent component, GameRuleComponent gameRule,
        GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        component.LastRunAt = Timing.CurTime;
        component.LastSpawnedCount = 0;
        component.SpawnedAt = null;
        component.LastSpawnLocation = null;
        component.LastFailure = null;

        if (component.Announcement is { } announcement)
            Announce(announcement);

        if (!TryGetAnchor(component, out var anchor, out var location, out var zoneRadius))
        {
            component.LastFailure = "could not find a spawn point";
            Log.Warning($"Could not find a spawn anchor for Misfits event {args.RuleId}, nothing spawned.");
            return;
        }

        var spawns = ValidSpawns(component.Spawns);
        var specials = ValidSpawns(component.SpecialSpawns);
        if (spawns.Count == 0 && specials.Count == 0)
        {
            component.LastFailure = "no valid spawn entries";
            Log.Error($"Misfits event {args.RuleId} has no valid spawn entries, nothing spawned.");
            return;
        }

        // An anchor with its own radius overrides how far spawns may scatter from it.
        var searchRadius = zoneRadius > 0f ? zoneRadius : component.SpawnRadius;

        // Clustered events pick one spot and pack everything into it, so the group arrives together.
        var spawnAround = anchor;
        var scatter = searchRadius;
        if (component.ClusterRadius > 0f && TryFindSpawnCoords(anchor, searchRadius, out var clusterCenter))
        {
            spawnAround = _transform.ToMapCoordinates(clusterCenter);
            scatter = component.ClusterRadius;
        }

        // Structures go down first, so the roles that follow arrive on their floor.
        var structures = PlaceStructures(component.Structures, spawnAround);

        // Rolled once per team here, so every spawner of that team shares one frequency.
        component.TeamFrequencies = RollTeamFrequencies(component);

        // One line for the whole run, so every role in it is told the same thing.
        component.BriefingText = RollBriefing(component);

        var spawned = SpawnEntries(spawns, component.CountMin, component.CountMax, spawnAround, scatter,
            component);
        var specialSpawned = SpawnEntries(specials, component.SpecialCountMin, component.SpecialCountMax,
            spawnAround, scatter, component);

        component.LastSpawnedCount = spawned + specialSpawned;
        component.LastSpawnLocation = location;

        Log.Info($"Misfits event {args.RuleId} spawned {spawned} entities and {specialSpawned} special spawns " +
                 $"and placed {structures} structures near '{location}' at {spawnAround}.");

        if (component.LastSpawnedCount == 0)
        {
            component.LastFailure = "no walkable tile near the spawn point";
            Log.Warning($"Misfits event {args.RuleId} spawned nothing at {spawnAround}: " +
                        "no walkable tile was found and the anchor tile fallback failed too.");
        }

        // Remember where it happened so ghosts can warp to it from chat.
        component.SpawnedAt = spawnAround;

        ScheduleWaves(uid, component, spawnAround, scatter);
        NotifyGhosts(uid);
    }

    // Result of each event's most recent run, keyed by event id.
    public Dictionary<string, MisfitsLastRun> GetLastRuns()
    {
        var runs = new Dictionary<string, MisfitsLastRun>();
        var newest = new Dictionary<string, TimeSpan>();

        var query = EntityQueryEnumerator<MisfitsEventComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (component.LastRunAt is not { } when)
                continue;

            if (MetaData(uid).EntityPrototype?.ID is not { } id)
                continue;

            if (newest.TryGetValue(id, out var seen) && seen >= when)
                continue;

            newest[id] = when;
            runs[id] = new MisfitsLastRun(component.SpawnedAt, component.LastSpawnedCount,
                component.LastFailure, component.LastSpawnLocation);
        }

        return runs;
    }

    // Sends every ghost a chat line with a clickable [Warp] link to the event.
    private void NotifyGhosts(EntityUid rule)
    {
        var text = Loc.GetString("misfits-event-ghost-notice", ("event", MetaData(rule).EntityName));
        var netRule = GetNetEntity(rule);

        foreach (var session in _playerManager.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } mob)
                continue;

            if (!HasComp<GhostComponent>(mob))
                continue;

            RaiseNetworkEvent(new MisfitsEventGhostAnnouncement { Text = text, Rule = netRule }, session.Channel);
        }
    }

    // Spawns countMin..countMax weighted picks from entries around a point.
    private int SpawnEntries(List<MisfitsEventSpawnEntry> entries, int countMin, int countMax,
        MapCoordinates around, float radius, MisfitsEventComponent? frequencies = null)
    {
        if (entries.Count == 0)
            return 0;

        var max = Math.Max(countMin, countMax);
        var count = RobustRandom.Next(Math.Max(0, countMin), max + 1);

        // Unique entries leave the pool once picked, so a nest and its guardian both arrive.
        var remaining = new List<MisfitsEventSpawnEntry>(entries);

        var spawned = 0;
        for (var i = 0; i < count; i++)
        {
            if (!TryPickSpawn(remaining, out var spawn))
                continue;

            // An entry can place a whole group at once, e.g. a trader with both guards.
            for (var placed = 0; placed < Math.Max(1, spawn.Count); placed++)
            {
                if (!TryFindSpawnCoords(around, radius, out var coords))
                    break;

                var spawnedEntity = Spawn(spawn.Id, coords);
                ApplyRoleData(spawnedEntity, frequencies);
                spawned++;
            }

            remaining.RemoveAll(entry => entry.Unique && entry.Id == spawn.Id);
        }

        return spawned;
    }

    protected override void ActiveTick(EntityUid uid, MisfitsEventComponent component, GameRuleComponent gameRule,
        float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        TickWaves(uid, component);

        if (component.Duration <= TimeSpan.Zero)
            return;

        if (Timing.CurTime < gameRule.ActivatedAt + component.Duration)
            return;

        ForceEndSelf(uid, gameRule);
    }

    protected override void Ended(EntityUid uid, MisfitsEventComponent component, GameRuleComponent gameRule,
        GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);
        _waves.Remove(uid);
        component.TeamFrequencies.Clear();
        component.BriefingText = null;
    }

    // Queues the event's later groups, all around the spot it already spawned at.
    private void ScheduleWaves(EntityUid uid, MisfitsEventComponent component, MapCoordinates around, float radius)
    {
        var pending = new List<PendingWave>();

        foreach (var wave in component.Waves)
        {
            var spawns = ValidSpawns(wave.Spawns);
            if (spawns.Count == 0 || wave.Delay < 0f)
                continue;

            pending.Add(new PendingWave
            {
                Due = Timing.CurTime + TimeSpan.FromSeconds(wave.Delay),
                Around = around,
                Radius = radius,
                Spawns = spawns,
                CountMin = wave.CountMin,
                CountMax = wave.CountMax,
                Announcement = wave.Announcement,
            });
        }

        if (pending.Count > 0)
            _waves[uid] = pending;
    }

    private void TickWaves(EntityUid uid, MisfitsEventComponent component)
    {
        if (!_waves.TryGetValue(uid, out var waves))
            return;

        for (var i = waves.Count - 1; i >= 0; i--)
        {
            var wave = waves[i];
            if (Timing.CurTime < wave.Due)
                continue;

            var spawned = SpawnEntries(wave.Spawns, wave.CountMin, wave.CountMax, wave.Around, wave.Radius,
                component);
            component.LastSpawnedCount += spawned;

            if (wave.Announcement is { } line)
                Announce(line);

            Log.Info($"Misfits event wave spawned {spawned} entities near '{component.LastSpawnLocation}' at " +
                     $"{wave.Around}.");
            waves.RemoveAt(i);
        }

        if (waves.Count == 0)
            _waves.Remove(uid);
    }

    // Merges pre-mapped structures onto the map at a point; each file's own origin lands there. Internal for tests only.
    internal int PlaceStructures(List<MisfitsEventStructure> structures, MapCoordinates around)
    {
        var placed = 0;

        foreach (var structure in structures)
        {
            if (structure.Chance < 1f && RobustRandom.NextFloat() >= structure.Chance)
                continue;

            try
            {
                if (_mapLoader.TryMergeMap(around.MapId, structure.Path, out _, offset: around.Position))
                {
                    placed++;
                    continue;
                }

                Log.Error($"Could not place event structure {structure.Path} on map {around.MapId}.");
            }
            catch (Exception e)
            {
                // A broken map file should cost the event its building, not the whole event.
                Log.Error($"Event structure {structure.Path} failed to load: {e}");
            }
        }

        return placed;
    }

    // One frequency per radio team in the event, waves included, rolled once when it starts.
    private Dictionary<string, int> RollTeamFrequencies(MisfitsEventComponent component)
    {
        var frequencies = new Dictionary<string, int>();

        foreach (var id in AllSpawnIds(component))
        {
            if (!_prototype.TryIndex<EntityPrototype>(id, out var proto))
                continue;

            if (!proto.TryGetComponent<MisfitsGhostRoleCharacterPickerComponent>(out var picker))
                continue;

            if (picker.RadioTeam is not { } team || frequencies.ContainsKey(team))
                continue;

            // Inside the band a handheld radio can actually be tuned to.
            frequencies[team] = RobustRandom.Next(RadioDeviceSystem.MinHandheldFrequency,
                RadioDeviceSystem.MaxHandheldFrequency + 1);
        }

        return frequencies;
    }

    // Everything an event can spawn, later waves included.
    private static IEnumerable<EntProtoId> AllSpawnIds(MisfitsEventComponent component)
    {
        foreach (var entry in component.Spawns)
            yield return entry.Id;

        foreach (var entry in component.SpecialSpawns)
            yield return entry.Id;

        foreach (var wave in component.Waves)
        {
            foreach (var entry in wave.Spawns)
                yield return entry.Id;
        }
    }

    // Stamps the rolled frequency and the run's briefing on a spawner, so its picker knows what to hand out.
    private void ApplyRoleData(EntityUid uid, MisfitsEventComponent? component)
    {
        if (component == null || !TryComp<MisfitsGhostRoleCharacterPickerComponent>(uid, out var picker))
            return;

        if (picker.RadioTeam is { } team && component.TeamFrequencies.TryGetValue(team, out var frequency))
            picker.Frequency = frequency;

        picker.Briefing = component.BriefingText;
    }

    // One line from the event's briefing dataset, or null when it has none.
    private string? RollBriefing(MisfitsEventComponent component)
    {
        if (component.Briefing is not { } id ||
            !_prototype.TryIndex<LocalizedDatasetPrototype>(id, out var dataset) ||
            dataset.Values.Count == 0)
        {
            return null;
        }

        return Loc.GetString(dataset.Values[RobustRandom.Next(dataset.Values.Count)]);
    }

    private void Announce(string announcement)
    {
        _chat.DispatchGlobalAnnouncement(
            Loc.GetString(announcement),
            sender: "Wasteland",
            playSound: false,
            colorOverride: Color.Gold);
    }

    // Where the event spawns: a random anchor marker on this map, named for the logs.
    private bool TryGetAnchor(MisfitsEventComponent component, out MapCoordinates anchor, out string location,
        out float zoneRadius)
    {
        zoneRadius = 0f;

        // Anchors on the round's own map win, so events never land on a separate level like the vault.
        var roundAnchors = new List<(MapCoordinates At, string Name, float Radius)>();
        var otherAnchors = new List<(MapCoordinates At, string Name, float Radius)>();

        var query = EntityQueryEnumerator<MisfitsEventAnchorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var marker, out var xform))
        {
            if (!KindMatches(component, marker.Kind))
                continue;

            var known = (_transform.GetMapCoordinates(xform), MetaData(uid).EntityName, marker.Radius);
            if (xform.MapID == GameTicker.DefaultMap)
                roundAnchors.Add(known);
            else
                otherAnchors.Add(known);
        }

        var candidates = roundAnchors.Count > 0 ? roundAnchors : otherAnchors;
        if (candidates.Count > 0)
        {
            var pick = RobustRandom.Pick(candidates);
            anchor = pick.At;
            location = pick.Name;
            zoneRadius = pick.Radius;
            return true;
        }

        // No anchors mapped yet, so use somewhere real rather than spawning nowhere.
        anchor = _transform.ToMapCoordinates(GameTicker.GetObserverSpawnPoint());
        location = "observer spawn";
        Log.Warning($"No MisfitsEventAnchor on this map, using the observer spawn point at {anchor}.");
        return true;
    }

    // An event with no listed kinds takes any anchor; generic anchors are always usable.
    private static bool KindMatches(MisfitsEventComponent component, MisfitsEventAnchorKind kind)
    {
        return kind == MisfitsEventAnchorKind.Any ||
               component.AnchorKinds.Count == 0 ||
               component.AnchorKinds.Contains(kind);
    }

    private bool IsWalkable(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        var tileRef = _map.GetTileRef(gridUid, grid, tile);
        return !tileRef.Tile.IsSpace() &&
               !_turf.IsTileBlocked(tileRef, CollisionGroup.MobMask) &&
               !_atmosphere.IsTileAirBlocked(gridUid, tile, mapGridComp: grid);
    }

    // Finds a walkable tile within radius of the anchor.
    private bool TryFindSpawnCoords(MapCoordinates anchor, float radius, out EntityCoordinates coords)
    {
        coords = default;

        var min = Math.Min(MinSpawnDistance, radius);
        for (var i = 0; i < SpawnAttempts; i++)
        {
            var offset = RobustRandom.NextVector2(min, radius);
            var candidate = new MapCoordinates(anchor.Position + offset, anchor.MapId);

            if (!_map.TryFindGridAt(candidate, out var gridUid, out var grid))
                continue;

            var tile = _map.CoordinatesToTile(gridUid, grid, candidate);

            if (!IsWalkable(gridUid, grid, tile))
                continue;

            coords = _map.GridTileToLocal(gridUid, grid, tile);
            return true;
        }

        // Everything around the anchor was blocked, so use its own tile.
        if (!_map.TryFindGridAt(anchor, out var fallbackGrid, out var fallback))
            return false;

        var fallbackTile = _map.CoordinatesToTile(fallbackGrid, fallback, anchor);
        coords = _map.GridTileToLocal(fallbackGrid, fallback, fallbackTile);
        return true;
    }

    private List<MisfitsEventSpawnEntry> ValidSpawns(List<MisfitsEventSpawnEntry> entries)
    {
        var valid = new List<MisfitsEventSpawnEntry>(entries.Count);
        foreach (var entry in entries)
        {
            if (!_prototype.HasIndex<EntityPrototype>(entry.Id))
            {
                Log.Error($"Misfits event references unknown prototype '{entry.Id}', skipping it.");
                continue;
            }

            valid.Add(entry);
        }

        return valid;
    }

    private bool TryPickSpawn(List<MisfitsEventSpawnEntry> entries, out MisfitsEventSpawnEntry spawn)
    {
        return TryPickWeighted(entries.Select(entry => (entry, entry.Weight)).ToList(), out spawn);
    }

    // Weighted pick from (item, weight) pairs; false when nothing has weight.
    private bool TryPickWeighted<T>(IReadOnlyList<(T Item, float Weight)> entries, out T picked)
    {
        picked = default!;

        if (entries.Count == 0)
            return false;

        var totalWeight = 0f;
        foreach (var (_, weight) in entries)
        {
            totalWeight += Math.Max(0f, weight);
        }

        if (totalWeight <= 0f)
            return false;

        var roll = RobustRandom.NextFloat() * totalWeight;
        foreach (var (item, weight) in entries)
        {
            roll -= Math.Max(0f, weight);
            if (roll <= 0f)
            {
                picked = item;
                return true;
            }
        }

        // Float rounding fallback.
        picked = entries[^1].Item;
        return true;
    }

    #endregion
}

// Outcome of one run: where it spawned (null when it failed), how many, and why not.
public readonly record struct MisfitsLastRun(MapCoordinates? At, int Count, string? Failure, string? Location);
