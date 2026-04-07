using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._Misfits.Expeditions;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Content.Server.Decals;

// #Misfits Add - Procedural underground expedition map generator (Vault / Sewer / Metro themes)
// Rewritten: slanted room walls, full WallRock background enclosure, door-at-threshold logic,
//            theme-specific tile variety, furniture dressing, and NPC mob spawning.

namespace Content.Server._Misfits.Expeditions;

/// <summary>
/// EntitySystem that generates procedural underground expedition maps at runtime.
/// Supports three themes: Vault (pre-war concrete), Sewer (brick tunnels + dirty water),
/// Metro (abandoned subway infrastructure).
///
/// Generation pipeline:
///   Phase A  — Room placement (faction hubs → central → standard rooms, theme-sized)
///   Phase B  — Corridor carving (minimum spanning tree, 2-tile-wide L-shaped)
///   Phase B5 — Doorway marking (corridor cells adjacent to room interior)
///   Phase C  — Sewer water-channel carving (Sewer theme only)
///   Phase D  — Tile painting (ALL cells tiled; background uses FloorAsteroidSand)
///   Phase E  — Entity spawning (WallRock fill, slanted room walls, doors, furniture, mobs)
/// </summary>
public sealed class UndergroundExpeditionMapGenerator : EntitySystem
{
    [Dependency] private readonly ITileDefinitionManager _tileDefManager = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly DecalSystem _decalSystem = default!;

    // ─────────────────────────────────────────────────────────────────────────
    // Tile IDs
    // ─────────────────────────────────────────────────────────────────────────

    // #Misfits Removed - Tile consts now live in ThemeProfile.TilePalette (UndergroundThemeProfiles.cs)
    // private const string TileBackground      = "FloorAsteroidSand";
    // private const string TileVaultFloor      = "FloorMetalTunnel";
    // private const string TileVaultRusty      = "FloorMetalTunnelRusty";
    // private const string TileVaultAlt        = "FloorMetalTunnelWasteland";
    // private const string TileVaultConcrete   = "N14FloorConcrete";
    // private const string TileVaultSteelDirty = "FloorSteelDirty";
    // private const string TileVaultConcDark   = "N14FloorConcreteDark";
    // private const string TileVaultIndustrial = "FloorMS13ConcreteIndustrial";
    // private const string TileSewerDirt       = "FloorDirtIndoors";
    // private const string TileSewerDirtNew    = "FloorDirtNew";
    // private const string TileSewerConcrete   = "FloorMS13Concrete";
    // private const string TileSewerCave       = "FloorCave";
    // private const string TileSewerBrick      = "FloorMS13BrickConcrete";
    // private const string TileMetroDark       = "FloorMetalGreyDark";
    // private const string TileMetroDarkSolid  = "FloorMetalGreyDarkSolid";
    // private const string TileMetroGrate      = "FloorMS13MetalGrate";
    // private const string TileMetroTile       = "FloorMS13MetalTile";
    // private const string TileMetroConcrete   = "N14FloorConcrete";
    // private const string TileMetroIndustrial = "FloorMS13MetalIndustrial";
    // private const string TileMetroSteelDirty = "FloorSteelDirty";
    // private const string TileMetroConcAlt    = "FloorMS13ConcreteIndustrialAlt";

    // Still-active tile consts (referenced directly in PaintTiles/PickRoomTile)
    private const string TileRubble          = "FloorRubbleIndoors";
    private const string TileSewerGrate      = "FloorMS13MetalGrate";
    private const string TileWaterDeep       = "WaterDeep";
    private const string TileGrate           = "FloorMS13MetalGrate";

    // ─────────────────────────────────────────────────────────────────────────
    // Wall entity IDs
    // ─────────────────────────────────────────────────────────────────────────

    // #Misfits Fix - Use indestructible variant so players can't break out of the dungeon
    private const string WallRockFill        = "N14WallRockSlantedIndestructible";

    // #Misfits Removed - Wall/door consts now live in ThemeProfile.TilePalette (UndergroundThemeProfiles.cs)
    // private const string WallVaultRoom       = "N14WallConcreteSlantedIndestructible";
    // private const string WallVaultHub        = "N14WallBunkerSlantedIndestructible";
    // private const string WallSewerRoom       = "N14WallBrickSlantedIndestructible";
    // private const string WallSewerHub        = "N14WallBrickGraySlantedIndestructible";
    // private const string WallMetroRoom       = "N14WallDungeonSlantedIndestructible";
    // private const string WallMetroHub        = "N14WallCombSlantedIndestructible";
    // private const string DoorVaultHub        = "N14DoorMetalReinforced";
    // private const string DoorVaultRoom       = "N14DoorBunker";
    // private const string DoorSewerHub        = "N14DoorMakeshift";
    // private const string DoorSewerRoom       = "N14DoorRoomRepaired";
    // private const string DoorMetroHub        = "N14DoorBunker";
    // private const string DoorMetroRoom       = "N14DoorWoodRoom";

    // ─────────────────────────────────────────────────────────────────────────
    // Sewer water entity
    // ─────────────────────────────────────────────────────────────────────────

    private const string WaterSewerEntity    = "N14FloorWaterSewerMedium";

    // #Misfits Removed - All FurnVault*, FurnSewer*, FurnMetro* static arrays replaced by
    // ThemeProfile.FurniturePools (UndergroundThemeProfiles.cs). Profile lookup via
    // profile.GetFurniturePool(poolKey) is the sole furniture source.
    /*
    private static readonly string[] FurnVaultStandard = { "LockerSteel", "N14LootCrateArmy", ... };
    private static readonly string[] FurnVaultHub = { "N14BarricadeMetal", ... };
    private static readonly string[] FurnSewerStandard = { "CrateWooden", ... };
    private static readonly string[] FurnSewerHub = { "N14BarricadeMetal", ... };
    private static readonly string[] FurnMetroStandard = { "N14JunkBench", ... };
    private static readonly string[] FurnMetroHub = { "N14BarricadeMetal", ... };
    */

    // ─────────────────────────────────────────────────────────────────────────
    // Mob entity pools — (proto, relativeWeight) per theme
    // ─────────────────────────────────────────────────────────────────────────

    // #Misfits Change - Faction sub-groups to prevent inter-faction infighting
    // Each room rolls one sub-group so all mobs in that room share the same faction.
    // #Misfits Removed - Mob groups now live in ThemeProfile.MobGroups (UndergroundThemeProfiles.cs)
    // private static readonly (string, int)[][] MobsVaultGroups = { ... };
    // private static readonly (string, int)[][] MobsSewerGroups = { ... };
    // private static readonly (string, int)[][] MobsMetroGroups = { ... };

    // #Misfits Removed - Room-specific Vault furniture arrays replaced by ThemeProfile.FurniturePools
    /*
    private static readonly string[] FurnVaultBarracks = { "N14BedWoodBunk", ... };
    private static readonly string[] FurnVaultLab = { "N14MachineRackServer", ... };
    private static readonly string[] FurnVaultArmory = { "N14ClosetGunCabinet", ... };
    private static readonly string[] FurnVaultVault = { "N14LootCrateVaultBigRusted", ... };
    private static readonly string[] FurnVaultOverseer = { "N14TableDeskWood", ... };
    private static readonly string[] FurnVaultReactor = { "N14GeneratorPrewar", ... };
    private static readonly string[] FurnVaultKitchen = { "N14CookingStove", ... };
    private static readonly string[] FurnVaultHydroponics = { "N14HydroponicsPlanter", ... };
    private static readonly string[] FurnVaultRecreation = { "N14TableCasinoPool", ... };
    private static readonly string[] FurnSewerCamp = { "N14BedWood", ... };
    */

    // #Misfits Removed - Floor scatter, blueprint, and junk pools now live in ThemeProfile (UndergroundThemeProfiles.cs)
    // private static readonly string[] FloorScatterVault = { ... };
    // private static readonly string[] FloorScatterSewer = { ... };
    // private static readonly string[] FloorScatterMetro = { ... };
    // private static readonly string[] BlueprintPool = { ... };
    // private static readonly string[] JunkPoolVault = { ... };
    // private static readonly string[] JunkPoolSewer = { ... };
    // private static readonly string[] JunkPoolMetro = { ... };

    // #Misfits Removed - Sewer/Metro room-specific furniture arrays replaced by ThemeProfile.FurniturePools
    /*
    private static readonly string[] FurnSewerTunnel = { "N14JunkBench", ... };
    private static readonly string[] FurnSewerPump = { "N14StorageTankFullFuel", ... };
    private static readonly string[] FurnSewerNest = { "N14BedDirty", ... };
    private static readonly string[] FurnSewerGrotto = { "N14JunkBench", ... };
    private static readonly string[] FurnSewerJunction = { "N14JunkTable", ... };
    private static readonly string[] FurnMetroTunnel = { "N14JunkBench", ... };
    private static readonly string[] FurnMetroPlatform = { "N14JunkBench", ... };
    private static readonly string[] FurnMetroMaintenance = { "Rack", ... };
    private static readonly string[] FurnMetroDepot = { "N14LootCrateMilitary", ... };
    private static readonly string[] FurnMetroCommand = { "TableWood", ... };
    */

    // #Misfits Removed - Decal and hazard pools now live in ThemeProfile (UndergroundThemeProfiles.cs)
    // private static readonly string[] DecalsVault = { ... };
    // private static readonly string[] DecalsSewer = { ... };
    // private static readonly string[] DecalsMetro = { ... };
    // private static readonly string[] HazardsVault = { ... };
    // private static readonly string[] HazardsSewer = { ... };
    // private static readonly string[] HazardsMetro = { ... };

    // =========================================================================
    // Public entry point
    // =========================================================================

    /// <summary>
    /// Generates a complete underground expedition map onto the provided grid.
    /// </summary>
    // #Misfits Change - Resolve structured ThemeProfile + EnvironmentalState modifiers
    // before entering the pipeline. Profile replaces scattered switch-per-theme logic.
    // #Misfits Fix - Returns hub positions so the expedition system can spawn players at faction hubs
    public List<(Vector2i position, int factionIndex)> GenerateMap(UndergroundGenParams p, EntityUid gridUid, MapGridComponent grid)
    {
        var rng = new Random(p.Seed);
        int W   = p.GridWidth;
        int H   = p.GridHeight;

        // ── Resolve structured generation profile + environmental state ─────────
        var profile = UndergroundThemeProfiles.GetProfile(p.Theme);

        // #Misfits Change - Use caller-supplied EnvironmentalStates when provided; otherwise auto-pick 1-2 from profile
        List<EnvironmentalState> selectedStates;
        if (p.EnvironmentalStates.Count > 0)
        {
            selectedStates = p.EnvironmentalStates;
        }
        else
        {
            selectedStates = new List<EnvironmentalState>();
            if (profile.ValidEnvironmentalStates.Count > 0)
            {
                selectedStates.Add(profile.ValidEnvironmentalStates[rng.Next(profile.ValidEnvironmentalStates.Count)]);
                // 30% chance to add a second, distinct state
                if (profile.ValidEnvironmentalStates.Count > 1 && rng.Next(100) < 30)
                {
                    EnvironmentalState second;
                    do { second = profile.ValidEnvironmentalStates[rng.Next(profile.ValidEnvironmentalStates.Count)]; }
                    while (second == selectedStates[0]);
                    selectedStates.Add(second);
                }
            }
        }

        var envMods = UndergroundThemeProfiles.MergeModifiers(selectedStates);

        // Phase A: place rooms
        var cellMap = new CellType[W, H];
        var rooms   = new List<RoomDef>();

        PlaceFactionHubs(cellMap, rooms, p, rng, W, H);
        PlaceCentralRoom(cellMap, rooms, p, rng, W, H);

        // Phase A.5: BSP zone division — restrict room types to spatially meaningful zones
        var zones = PartitionMapIntoZones(W, H, p.HubCount, rng);
        PlaceStandardRooms(cellMap, rooms, p, profile, rng, W, H, zones);

        // Phase B: 2-tile-wide MST corridors + profile-driven branch/loop passes
        CarveCorridors(cellMap, rooms, profile, rng, W, H);

        // Phase B5: identify corridor→room threshold cells — get door entities in Phase E
        var doorways = MarkDoorways(cellMap, W, H);

        // #Misfits Add - Phase B6: System 5 door validation — remove structurally invalid doorways
        ValidateDoors(doorways, cellMap, rooms, W, H);

        // Phase C: sewer water channels (Sewer theme always; other themes if Flooded state overrides)
        if (p.Theme == UndergroundTheme.Sewer || envMods.WaterChannelChanceOverride > 0)
            CarveSewerWaterChannels(cellMap, rng, W, H,
                forceCarve: p.Theme == UndergroundTheme.Sewer,
                overrideChance: envMods.WaterChannelChanceOverride);

        // Phase D: tile every cell (including background; required for atmos sealing)
        // #Misfits Add - WFC-style per-room tile map with primary/accent/edge and neighbor smoothing
        var roomTileMap = BuildRoomTileMapWFC(rooms, profile, cellMap, rng, W, H);
        PaintTiles(cellMap, gridUid, grid, profile, envMods, rng, W, H, roomTileMap);

        // Phase E: spawn all entities
        SpawnEntities(cellMap, rooms, doorways, gridUid, grid, profile, envMods, p.DifficultyTier, rng, W, H);

        // #Misfits Fix - Return hub positions so the system can place exits and spawn players correctly
        return rooms
            .Where(r => r.RoomType == RoomType.FactionHub)
            .Select(r => (new Vector2i(r.Center.cx, r.Center.cy), r.FactionIndex))
            .ToList();
    }

    // =========================================================================
    // Phase A — Room Placement
    // =========================================================================

    private static void PlaceFactionHubs(
        CellType[,] cellMap, List<RoomDef> rooms,
        UndergroundGenParams p, Random rng, int W, int H)
    {
        int hubCount = Math.Min(p.HubCount, 4);

        for (int i = 0; i < hubCount; i++)
        {
            // #Misfits Tweak - Reduced hub size: smaller footprint while still fitting 10+ player characters
            // (7–10 outer → 5–8 interior = 25–64 floor tiles, comfortable for 10 players)
            int hubW = rng.Next(7, 11);
            int hubH = rng.Next(7, 11);
            var (zx0, zy0, zx1, zy1) = GetHubZone(i, W, H);
            int maxX = Math.Max(zx0, zx1 - hubW);
            int maxY = Math.Max(zy0, zy1 - hubH);

            for (int attempt = 0; attempt < 20; attempt++)
            {
                int x = rng.Next(zx0, maxX + 1);
                int y = rng.Next(zy0, maxY + 1);
                var cand = new RoomDef
                {
                    X = x, Y = y, W = hubW, H = hubH,
                    RoomType = RoomType.FactionHub, FactionIndex = i,
                };
                if (rooms.Any(r => r.Overlaps(cand, 2))) continue;
                rooms.Add(cand);
                PaintRoom(cellMap, cand, CellType.FactionHub, W, H);
                break;
            }
        }
    }

    private static (int x0, int y0, int x1, int y1) GetHubZone(int idx, int W, int H)
    {
        const int margin = 3;
        int thirdW = W / 3;
        int thirdH = H / 3;
        return idx switch
        {
            0 => (margin,     margin,      thirdW,     thirdH),
            1 => (W - thirdW, margin,      W - margin, thirdH),
            2 => (W - thirdW, H - thirdH,  W - margin, H - margin),
            3 => (margin,     H - thirdH,  thirdW,     H - margin),
            _ => (margin,     margin,      W / 2,      H / 2),
        };
    }

    private static void PlaceCentralRoom(
        CellType[,] cellMap, List<RoomDef> rooms,
        UndergroundGenParams p, Random rng, int W, int H)
    {
        // Large central room near the grid centre — primary objective area
        int roomW = rng.Next(12, 20);
        int roomH = rng.Next(12, 20);
        int baseCx = W / 2 - roomW / 2;
        int baseCy = H / 2 - roomH / 2;

        for (int attempt = 0; attempt < 30; attempt++)
        {
            int x = Math.Clamp(baseCx + rng.Next(-5, 6), 3, W - roomW - 3);
            int y = Math.Clamp(baseCy + rng.Next(-5, 6), 3, H - roomH - 3);
            var cand = new RoomDef { X = x, Y = y, W = roomW, H = roomH, RoomType = RoomType.Central };
            if (rooms.Any(r => r.Overlaps(cand, 2))) continue;
            rooms.Add(cand);
            PaintRoom(cellMap, cand, CellType.Room, W, H);
            return;
        }

        // Fallback: force-place at exact centre
        var fb = new RoomDef
        {
            X = Math.Clamp(baseCx, 3, W - roomW - 3),
            Y = Math.Clamp(baseCy, 3, H - roomH - 3),
            W = roomW, H = roomH, RoomType = RoomType.Central,
        };
        rooms.Add(fb);
        PaintRoom(cellMap, fb, CellType.Room, W, H);
    }

    // #Misfits Change - Two-pass room placement driven by ThemeProfile: mandatory anchors first,
    // then capped random fill with adjacency scoring from RoomTypeDefinition rules.
    // #Misfits Add - Accepts BSP zones for spatially-aware placement in Pass 2
    private static void PlaceStandardRooms(
        CellType[,] cellMap, List<RoomDef> rooms,
        UndergroundGenParams p, ThemeProfile profile, Random rng, int W, int H,
        List<MapZone> zones)
    {
        int remaining = p.MaxRooms;

        // ── Pass 1: Mandatory anchor rooms from profile ─────────────────────────
        var anchors = new List<RoomType>(profile.MandatoryAnchors);

        // Shuffle anchors so map layout order varies each seed
        for (int i = anchors.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (anchors[i], anchors[j]) = (anchors[j], anchors[i]);
        }

        var placedCounts = new Dictionary<RoomType, int>();

        foreach (var anchorType in anchors)
        {
            if (remaining <= 0) break;
            bool success = false;
            int budget1 = 40;
            for (int attempt = 0; attempt < budget1 && !success; attempt++)
            {
                var (rw, rh) = GetRoomDimensionsFromProfile(profile, anchorType, rng);
                if (rw > W - 6 || rh > H - 6) continue;
                int x = rng.Next(3, W - rw - 3);
                int y = rng.Next(3, H - rh - 3);
                var cand = new RoomDef { X = x, Y = y, W = rw, H = rh, RoomType = anchorType };
                if (rooms.Any(r => r.Overlaps(cand, 2))) continue;
                rooms.Add(cand);
                PaintRoom(cellMap, cand, CellType.Room, W, H);
                placedCounts[anchorType] = placedCounts.GetValueOrDefault(anchorType) + 1;
                remaining--;
                success = true;
            }
        }

        // ── Pass 2: 4-candidate tournament with adjacency scoring ────────────────
        // Each iteration generates N candidate (x, y, roomType) tuples, scores them
        // via ScorePlacement(), and picks the highest-scoring valid candidate.
        // #Misfits Add - Zone-constrained positioning: candidates prefer zones matching their role
        const int candidatesPerRound = 4;
        int budget2 = remaining * 6;

        for (int attempt = 0; attempt < budget2 && remaining > 0; attempt++)
        {
            // Generate N candidates, each with a random position and room type
            RoomDef? bestCand = null;
            int bestScore = int.MinValue;

            for (int c = 0; c < candidatesPerRound; c++)
            {
                var roomType = PickRoomTypeFromProfile(profile, rng, placedCounts);
                var (rw, rh) = GetRoomDimensionsFromProfile(profile, roomType, rng);
                if (rw > W - 6 || rh > H - 6) continue;

                // Zone-constrained position: try matching zone first, fall back after 10 failures
                int x, y;
                var targetRole = GetZoneRoleForRoomType(roomType, profile.Theme);
                var matchingZones = zones.Where(z => z.Role == targetRole).ToList();
                bool zoneFound = false;

                if (matchingZones.Count > 0)
                {
                    for (int zAttempt = 0; zAttempt < 10; zAttempt++)
                    {
                        var zone = matchingZones[rng.Next(matchingZones.Count)];
                        int maxX = zone.X + zone.W - rw;
                        int maxY = zone.Y + zone.H - rh;
                        if (maxX < zone.X || maxY < zone.Y) continue;
                        x = rng.Next(zone.X, maxX + 1);
                        y = rng.Next(zone.Y, maxY + 1);
                        var cand = new RoomDef { X = x, Y = y, W = rw, H = rh, RoomType = roomType };
                        if (rooms.Any(r => r.Overlaps(cand, 2))) continue;

                        int score = ScorePlacement(x + rw / 2, y + rh / 2, roomType, rooms, profile, W, H);
                        if (score > bestScore || (score == bestScore && rng.Next(2) == 0))
                        {
                            bestScore = score;
                            bestCand = cand;
                        }
                        zoneFound = true;
                        break;
                    }
                }

                // Fallback: unconstrained random position (original behavior)
                if (!zoneFound)
                {
                    x = rng.Next(3, W - rw - 3);
                    y = rng.Next(3, H - rh - 3);
                    var cand = new RoomDef { X = x, Y = y, W = rw, H = rh, RoomType = roomType };
                    if (rooms.Any(r => r.Overlaps(cand, 2))) continue;

                    int score = ScorePlacement(x + rw / 2, y + rh / 2, roomType, rooms, profile, W, H);
                    if (score > bestScore || (score == bestScore && rng.Next(2) == 0))
                    {
                        bestScore = score;
                        bestCand = cand;
                    }
                }
            }

            if (bestCand == null) continue; // all candidates overlapped

            rooms.Add(bestCand);
            PaintRoom(cellMap, bestCand, CellType.Room, W, H);
            placedCounts[bestCand.RoomType] = placedCounts.GetValueOrDefault(bestCand.RoomType) + 1;
            remaining--;
        }
    }

    /// <summary>
    /// Picks a weighted-random room type from the profile's RoomDefinitions,
    /// filtering out types that have reached their MaxCount cap.
    /// Falls back to the first definition if all are capped.
    /// </summary>
    // #Misfits Add - Profile-driven room type selection replacing GetThematicRoomTypeCapped
    private static RoomType PickRoomTypeFromProfile(
        ThemeProfile profile, Random rng,
        Dictionary<RoomType, int> placedCounts)
    {
        var filtered = new List<(RoomType type, int weight)>();
        foreach (var def in profile.RoomDefinitions)
        {
            int placed = placedCounts.GetValueOrDefault(def.RoomType);
            if (placed < def.MaxCount)
                filtered.Add((def.RoomType, def.Weight));
        }

        if (filtered.Count == 0)
            return profile.RoomDefinitions[0].RoomType; // safety fallback

        int total = 0;
        foreach (var (_, w) in filtered) total += w;
        int roll  = rng.Next(total);
        int cumul = 0;
        foreach (var (type, weight) in filtered)
        {
            cumul += weight;
            if (roll < cumul) return type;
        }
        return filtered[^1].type;
    }

    /// <summary>
    /// Returns room dimensions from the profile's RoomTypeDefinition for the given type.
    /// Falls back to theme-generic GetRoomDimensions if no definition exists.
    /// </summary>
    // #Misfits Add - Profile-driven room dimensions
    // #Misfits Add - depthFactor scales min/max ranges: near hub = smaller, near center = full size
    private static (int w, int h) GetRoomDimensionsFromProfile(
        ThemeProfile profile, RoomType roomType, Random rng, float depthFactor = 0.5f)
    {
        var def = profile.GetRoomDef(roomType);
        if (def != null)
        {
            // Scale dimension ranges using depth factor (near hub = tighter/smaller range)
            // #Misfits Fix - Inline lerp: MathF.Lerp not available pre-.NET 9
            int scaledMinW = (int)MathF.Round(def.MinW + (def.MinW * 0.7f - def.MinW) * (1f - depthFactor));
            int scaledMaxW = (int)MathF.Round(def.MaxW * 0.7f + (def.MaxW - def.MaxW * 0.7f) * depthFactor);
            int scaledMinH = (int)MathF.Round(def.MinH + (def.MinH * 0.7f - def.MinH) * (1f - depthFactor));
            int scaledMaxH = (int)MathF.Round(def.MaxH * 0.7f + (def.MaxH - def.MaxH * 0.7f) * depthFactor);
            // Ensure min <= max after scaling
            scaledMinW = Math.Max(4, Math.Min(scaledMinW, scaledMaxW));
            scaledMinH = Math.Max(4, Math.Min(scaledMinH, scaledMaxH));
            return (rng.Next(scaledMinW, scaledMaxW + 1), rng.Next(scaledMinH, scaledMaxH + 1));
        }
        // Fallback for FactionHub, Central, and undefined types
        return GetRoomDimensions(profile.Theme, rng);
    }

    /// <summary>
    /// Scores a candidate placement by checking adjacency preferences/exclusions
    /// against already-placed rooms within Chebyshev distance 12.
    /// Returns positive for good fits, negative for conflicts, 0 for neutral.
    /// </summary>
    // #Misfits Change - Replaced ScoreAdjacency: Chebyshev distance 12, +2 pref / -3 exclusion weights
    private static int ScorePlacement(
        int cx, int cy, RoomType roomType,
        List<RoomDef> placed, ThemeProfile profile, int W, int H)
    {
        var def = profile.GetRoomDef(roomType);
        if (def == null) return 0;

        int score = 0;

        foreach (var room in placed)
        {
            // Skip hub/central — they don't participate in adjacency logic
            if (room.RoomType == RoomType.FactionHub || room.RoomType == RoomType.Central) continue;

            var (rx, ry) = room.Center;
            // Chebyshev distance: max of axis deltas
            int dist = Math.Max(Math.Abs(cx - rx), Math.Abs(cy - ry));
            if (dist > 12) continue;

            if (def.AdjacencyPreferences.Contains(room.RoomType))
                score += 2;

            if (def.AdjacencyExclusions.Contains(room.RoomType))
                score -= 3;
        }

        return score;
    }

    // #Misfits Removed - GetRoomTypeCaps and GetThematicRoomTypeCapped replaced by
    // PickRoomTypeFromProfile which uses ThemeProfile.RoomDefinitions for caps + weights.
    /*
    private static Dictionary<RoomType, int> GetRoomTypeCaps(UndergroundTheme theme) => theme switch
    {
        UndergroundTheme.Vault => new Dictionary<RoomType, int>
        {
            { RoomType.VaultBarracks,    3 },
            { RoomType.VaultKitchen,     2 },
            { RoomType.VaultHydroponics, 2 },
            { RoomType.VaultRecreation,  2 },
            { RoomType.VaultLab,         3 },
            { RoomType.VaultArmory,      2 },
            { RoomType.VaultVault,       2 },
            { RoomType.VaultOverseer,    1 },
            { RoomType.VaultReactor,     1 },
        },
        UndergroundTheme.Sewer => new Dictionary<RoomType, int>
        {
            { RoomType.SewerTunnel,   4 },
            { RoomType.SewerJunction, 3 },
            { RoomType.SewerGrotto,   3 },
            { RoomType.SewerPump,     2 },
            { RoomType.SewerNest,     2 },
            { RoomType.SewerCamp,     2 },
        },
        UndergroundTheme.Metro => new Dictionary<RoomType, int>
        {
            { RoomType.MetroPlatform,    4 },
            { RoomType.MetroTunnel,      3 },
            { RoomType.MetroMaintenance, 3 },
            { RoomType.MetroDepot,       2 },
            { RoomType.MetroCommand,     1 },
        },
        _ => new Dictionary<RoomType, int>(),
    };

    private static RoomType GetThematicRoomTypeCapped(
        UndergroundTheme theme, Random rng,
        Dictionary<RoomType, int> placedCounts,
        Dictionary<RoomType, int> maxCounts)
    {
        var candidates = theme switch
        {
            UndergroundTheme.Vault => new List<(RoomType type, int weight)>
            {
                (RoomType.VaultBarracks,    18),
                (RoomType.VaultKitchen,     10),
                (RoomType.VaultHydroponics,  7),
                (RoomType.VaultRecreation,   5),
                (RoomType.VaultLab,         15),
                (RoomType.VaultArmory,      13),
                (RoomType.VaultVault,       10),
                (RoomType.VaultOverseer,    10),
                (RoomType.VaultReactor,     12),
            },
            UndergroundTheme.Sewer => new List<(RoomType type, int weight)>
            {
                (RoomType.SewerTunnel,   35),
                (RoomType.SewerJunction, 15),
                (RoomType.SewerGrotto,   12),
                (RoomType.SewerPump,     10),
                (RoomType.SewerNest,     10),
                (RoomType.SewerCamp,     18),
            },
            UndergroundTheme.Metro => new List<(RoomType type, int weight)>
            {
                (RoomType.MetroPlatform,    30),
                (RoomType.MetroTunnel,      20),
                (RoomType.MetroMaintenance, 20),
                (RoomType.MetroDepot,       15),
                (RoomType.MetroCommand,     15),
            },
            _ => new List<(RoomType, int)>(),
        };

        var filtered = candidates
            .Where(c => !maxCounts.ContainsKey(c.type) ||
                        placedCounts.GetValueOrDefault(c.type) < maxCounts[c.type])
            .ToList();

        if (filtered.Count == 0)
            return GetThematicRoomType(theme, rng);

        int total = filtered.Sum(c => c.weight);
        int roll  = rng.Next(total);
        int cumul = 0;
        foreach (var (type, weight) in filtered)
        {
            cumul += weight;
            if (roll < cumul) return type;
        }
        return filtered[^1].type;
    }
    */

    /// <summary>
    /// Returns theme-specific room dimensions with shape variation.
    /// Sewer = elongated tunnels; Metro = wide platforms; Vault = varied command rooms.
    /// </summary>
    private static (int w, int h) GetRoomDimensions(UndergroundTheme theme, Random rng)
    {
        switch (theme)
        {
            case UndergroundTheme.Sewer:
            {
                // Mix elongated and square shapes for interesting tunnel feel
                return rng.Next(10) switch
                {
                    < 4 => (rng.Next(5, 9),   rng.Next(12, 22)), // tall narrow tunnel
                    < 8 => (rng.Next(12, 22), rng.Next(5, 9)),   // wide flat tunnel
                    _   => (rng.Next(7,  13), rng.Next(7, 13)),  // square antechamber
                };
            }
            case UndergroundTheme.Metro:
            {
                // Metro platforms — predominantly long
                return rng.Next(10) switch
                {
                    < 5 => (rng.Next(16, 26), rng.Next(6, 10)),  // long E/W platform
                    < 8 => (rng.Next(6,  10), rng.Next(16, 26)), // long N/S platform
                    _   => (rng.Next(10, 16), rng.Next(10, 16)), // standard room
                };
            }
            default: // Vault
            {
                return rng.Next(10) switch
                {
                    < 3 => (rng.Next(6, 9),   rng.Next(6, 9)),  // small storage
                    < 7 => (rng.Next(9, 15),  rng.Next(9, 15)), // medium office/lab
                    _   => (rng.Next(12, 18), rng.Next(10, 16)),// large command room
                };
            }
        }
    }

    private static void PaintRoom(CellType[,] cellMap, RoomDef room, CellType type, int W, int H)
    {
        for (int x = room.X; x < room.X + room.W && x < W; x++)
        for (int y = room.Y; y < room.Y + room.H && y < H; y++)
            cellMap[x, y] = type;
    }

    // =========================================================================
    // Phase A.5 — BSP Zone Division
    // =========================================================================

    /// <summary>
    /// Divides the map interior into a 3×3 grid of zones. Each zone is assigned a
    /// <see cref="ZoneRole"/> based on its Manhattan distance (in zone-grid steps) from
    /// the nearest hub corner. Hub corners are the (0,0),(2,0),(2,2),(0,2) zone cells
    /// for up to 4 hubs.
    /// </summary>
    // #Misfits Add - BSP zone partition for spatially-aware room placement
    private static List<MapZone> PartitionMapIntoZones(int W, int H, int hubCount, Random rng)
    {
        const int margin = 8;
        int innerW = W - margin * 2;
        int innerH = H - margin * 2;
        int cellW = innerW / 3;
        int cellH = innerH / 3;

        // Hub corner positions in zone-grid coordinates (matching GetHubZone corner order)
        var hubCorners = new List<(int zx, int zy)>();
        if (hubCount >= 1) hubCorners.Add((0, 0)); // bottom-left
        if (hubCount >= 2) hubCorners.Add((2, 0)); // bottom-right
        if (hubCount >= 3) hubCorners.Add((2, 2)); // top-right
        if (hubCount >= 4) hubCorners.Add((0, 2)); // top-left

        var zones = new List<MapZone>();
        for (int gx = 0; gx < 3; gx++)
        for (int gy = 0; gy < 3; gy++)
        {
            // Manhattan distance to nearest hub corner in zone-grid space
            int minDist = int.MaxValue;
            foreach (var (hx, hy) in hubCorners)
            {
                int d = Math.Abs(gx - hx) + Math.Abs(gy - hy);
                if (d < minDist) minDist = d;
            }

            // Fallback if no hubs (shouldn't happen, but safe)
            if (hubCorners.Count == 0) minDist = 2;

            var role = minDist switch
            {
                0     => ZoneRole.Entry,
                1     => ZoneRole.Transit,
                2     => ZoneRole.Utility,
                3     => ZoneRole.Secure,
                _     => ZoneRole.Hazard,
            };

            zones.Add(new MapZone
            {
                X = margin + gx * cellW,
                Y = margin + gy * cellH,
                W = (gx == 2) ? innerW - cellW * 2 : cellW, // last column absorbs remainder
                H = (gy == 2) ? innerH - cellH * 2 : cellH, // last row absorbs remainder
                Role = role,
                DepthFromHub = minDist,
            });
        }

        return zones;
    }

    /// <summary>
    /// Returns the preferred <see cref="ZoneRole"/> for a given room type and theme.
    /// Used to restrict where rooms are placed on the map.
    /// </summary>
    // #Misfits Add - Zone role assignment per room type per theme
    private static ZoneRole GetZoneRoleForRoomType(RoomType roomType, UndergroundTheme theme)
    {
        return (theme, roomType) switch
        {
            // Vault zone assignments — familiar near entry, dangerous in center
            (UndergroundTheme.Vault, RoomType.VaultBarracks)    => ZoneRole.Entry,
            (UndergroundTheme.Vault, RoomType.VaultKitchen)     => ZoneRole.Transit,
            (UndergroundTheme.Vault, RoomType.VaultHydroponics) => ZoneRole.Transit,
            (UndergroundTheme.Vault, RoomType.VaultRecreation)  => ZoneRole.Transit,
            (UndergroundTheme.Vault, RoomType.VaultLab)         => ZoneRole.Utility,
            (UndergroundTheme.Vault, RoomType.VaultArmory)      => ZoneRole.Utility,
            (UndergroundTheme.Vault, RoomType.VaultOverseer)    => ZoneRole.Secure,
            (UndergroundTheme.Vault, RoomType.VaultVault)       => ZoneRole.Secure,
            (UndergroundTheme.Vault, RoomType.VaultReactor)     => ZoneRole.Hazard,

            // Sewer zone assignments — junctions near entry, nests deep
            (UndergroundTheme.Sewer, RoomType.SewerJunction)    => ZoneRole.Entry,
            (UndergroundTheme.Sewer, RoomType.SewerTunnel)      => ZoneRole.Transit,
            (UndergroundTheme.Sewer, RoomType.SewerGrotto)      => ZoneRole.Transit,
            (UndergroundTheme.Sewer, RoomType.SewerPump)        => ZoneRole.Utility,
            (UndergroundTheme.Sewer, RoomType.SewerCamp)        => ZoneRole.Utility,
            (UndergroundTheme.Sewer, RoomType.SewerNest)        => ZoneRole.Hazard,

            // Metro zone assignments — platforms near entry, command deepest
            (UndergroundTheme.Metro, RoomType.MetroPlatform)    => ZoneRole.Entry,
            (UndergroundTheme.Metro, RoomType.MetroTunnel)      => ZoneRole.Transit,
            (UndergroundTheme.Metro, RoomType.MetroMaintenance) => ZoneRole.Utility,
            (UndergroundTheme.Metro, RoomType.MetroDepot)       => ZoneRole.Secure,
            (UndergroundTheme.Metro, RoomType.MetroCommand)     => ZoneRole.Hazard,

            // Non-themed / structural rooms — always Entry or Transit
            (_, RoomType.FactionHub) => ZoneRole.Entry,
            (_, RoomType.Central)    => ZoneRole.Hazard,
            _                        => ZoneRole.Transit,
        };
    }

    /// <summary>
    /// Returns a 0.0–1.0 depth factor based on Euclidean distance from the room's center
    /// to the nearest faction hub. 0.0 = adjacent to hub (safe), 1.0 = map center (dangerous).
    /// Used to scale room sizes, furniture density, and mob spawn rates.
    /// </summary>
    // #Misfits Add - Depth gradient factor for Hades-style difficulty scaling
    private static float GetDepthFactor(RoomDef room, List<RoomDef> hubs, int W, int H)
    {
        if (hubs.Count == 0) return 0.5f;
        float maxDist = Math.Max(W, H);
        var (cx, cy) = room.Center;
        float minDist = hubs
            .Select(h =>
            {
                var (hx, hy) = h.Center;
                return MathF.Sqrt((cx - hx) * (cx - hx) + (cy - hy) * (cy - hy));
            })
            .Min();
        return Math.Clamp(minDist / (maxDist * 0.6f), 0f, 1f);
    }

    // =========================================================================
    // System 4 — Entity Placement Constraint Helpers
    // =========================================================================

    // #Misfits Add - cardinal direction offsets used by placement helpers
    private static readonly (int dx, int dy)[] OrthoDirs =
    {
        ( 0,  1),
        ( 0, -1),
        ( 1,  0),
        (-1,  0),
    };

    // #Misfits Add - entity placement rule classifier for System 4
    /// <summary>
    /// Returns the <see cref="PlacementRule"/> for <paramref name="entityProto"/>.
    /// <paramref name="roomTypeName"/> is checked so Table entities inside kitchen rooms use WallRow.
    /// </summary>
    private static PlacementRule GetPlacementRule(string entityProto, string roomTypeName = "")
    {
        // Door/Airlock protos must never be placed as furniture — caller should skip
        if (entityProto.Contains("Door", StringComparison.OrdinalIgnoreCase) ||
            entityProto.Contains("Airlock", StringComparison.OrdinalIgnoreCase))
            return PlacementRule.FreeStanding; // sentinel — caller filters these out

        // Kitchen tables → WallRow
        if (entityProto.Contains("Table", StringComparison.OrdinalIgnoreCase)
            && roomTypeName.Contains("Kitchen", StringComparison.OrdinalIgnoreCase))
            return PlacementRule.WallRow;

        // OnSurface: items that must sit on a parent entity
        if (entityProto.Contains("N14ComputerTerminal", StringComparison.OrdinalIgnoreCase) ||
            entityProto.Contains("Microwave", StringComparison.OrdinalIgnoreCase) ||
            entityProto.Contains("Stove", StringComparison.OrdinalIgnoreCase) ||
            entityProto.Contains("Grille", StringComparison.OrdinalIgnoreCase))
            return PlacementRule.OnSurface;

        // OnSurface: weapon variants on armory shelves
        if (entityProto.StartsWith("N14Weapon", StringComparison.OrdinalIgnoreCase))
            return PlacementRule.OnSurface;

        // WallAttached: signs, posters — go ON a wall cell
        if (entityProto.Contains("Sign", StringComparison.OrdinalIgnoreCase) ||
            entityProto.Contains("Poster", StringComparison.OrdinalIgnoreCase))
            return PlacementRule.WallAttached;

        // WallAdjacent: closets, shelves, cabinets — on floor next to wall
        if (entityProto.Contains("Closet", StringComparison.OrdinalIgnoreCase) ||
            entityProto.Contains("Shelf", StringComparison.OrdinalIgnoreCase) ||
            entityProto.Contains("Cabinet", StringComparison.OrdinalIgnoreCase) ||
            entityProto.Contains("N14LootFilingCabinet", StringComparison.OrdinalIgnoreCase))
            return PlacementRule.WallAdjacent;

        return PlacementRule.FreeStanding;
    }

    // #Misfits Add - parent-proto keyword lookup for OnSurface entities
    /// <summary>
    /// Returns a keyword to match against already-placed parents for OnSurface linkage.
    /// </summary>
    private static string GetOnSurfaceParentKeyword(string entityProto)
    {
        if (entityProto.Contains("N14ComputerTerminal", StringComparison.OrdinalIgnoreCase))
            return "Desk";
        if (entityProto.Contains("Microwave", StringComparison.OrdinalIgnoreCase) ||
            entityProto.Contains("Stove", StringComparison.OrdinalIgnoreCase) ||
            entityProto.Contains("Grille", StringComparison.OrdinalIgnoreCase))
            return "Table";
        if (entityProto.StartsWith("N14Weapon", StringComparison.OrdinalIgnoreCase))
            return "N14ShelfMetal";
        return string.Empty;
    }

    // #Misfits Add - finds Empty cells on the wall perimeter of a room (surrogate for CellType.Wall)
    /// <summary>
    /// Returns every <see cref="CellType.Empty"/> cell that sits on or one tile outside
    /// the room's bounding box AND is orthogonally adjacent to at least one
    /// <see cref="CellType.Room"/> or <see cref="CellType.FactionHub"/> cell inside
    /// the room. These are the "wall" tiles onto which WallAttached entities spawn.
    /// </summary>
    private static List<(int x, int y)> FindWallTilesInRoom(
        RoomDef room, CellType[,] cellMap, int W, int H)
    {
        var results = new List<(int x, int y)>();

        int x0 = Math.Max(0, room.X - 1);
        int x1 = Math.Min(W - 1, room.X + room.W);
        int y0 = Math.Max(0, room.Y - 1);
        int y1 = Math.Min(H - 1, room.Y + room.H);

        for (int x = x0; x <= x1; x++)
        for (int y = y0; y <= y1; y++)
        {
            if (cellMap[x, y] != CellType.Empty)
                continue;

            // Must be adjacent to at least one Room/Hub cell inside the room
            foreach (var (dx, dy) in OrthoDirs)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                if (nx < room.X || nx >= room.X + room.W ||
                    ny < room.Y || ny >= room.Y + room.H)
                    continue;
                var ct = cellMap[nx, ny];
                if (ct is CellType.Room or CellType.FactionHub)
                {
                    results.Add((x, y));
                    break;
                }
            }
        }

        return results;
    }

    // #Misfits Add - finds floor tiles inside a room that are adjacent to a wall (Empty) cell
    /// <summary>
    /// Returns every <see cref="CellType.Room"/> or <see cref="CellType.FactionHub"/> cell
    /// inside the room that has at least one orthogonal <see cref="CellType.Empty"/> neighbour.
    /// These are the "wall-adjacent floor" tiles onto which WallAdjacent entities spawn.
    /// </summary>
    private static List<(int x, int y)> FindWallAdjacentFloorTiles(
        RoomDef room, CellType[,] cellMap, int W, int H)
    {
        var results = new List<(int x, int y)>();

        for (int x = room.X; x < room.X + room.W && x < W; x++)
        for (int y = room.Y; y < room.Y + room.H && y < H; y++)
        {
            var ct = cellMap[x, y];
            if (ct is not (CellType.Room or CellType.FactionHub))
                continue;

            foreach (var (dx, dy) in OrthoDirs)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= W || ny >= H)
                {
                    results.Add((x, y));
                    break;
                }
                if (cellMap[nx, ny] == CellType.Empty)
                {
                    results.Add((x, y));
                    break;
                }
            }
        }

        return results;
    }

    // #Misfits Add - finds the longest consecutive run of wall-adjacent tiles along one wall
    /// <summary>
    /// From the wall-adjacent floor tiles, finds the longest consecutive horizontal or
    /// vertical run sharing the same wall direction. Used by WallRow (kitchen tables).
    /// </summary>
    private static List<(int x, int y)> FindLongestWallRun(
        RoomDef room, CellType[,] cellMap, int W, int H)
    {
        var wallAdjacent = FindWallAdjacentFloorTiles(room, cellMap, W, H);
        if (wallAdjacent.Count == 0)
            return wallAdjacent;

        var bestRun = new List<(int x, int y)>();

        foreach (var (wallDx, wallDy) in OrthoDirs)
        {
            // Filter tiles whose Empty neighbour is in this exact direction
            var candidates = new List<(int x, int y)>();
            foreach (var t in wallAdjacent)
            {
                int nx = t.x + wallDx, ny = t.y + wallDy;
                if (nx >= 0 && ny >= 0 && nx < W && ny < H && cellMap[nx, ny] == CellType.Empty)
                    candidates.Add(t);
            }
            if (candidates.Count == 0) continue;

            // Run axis is perpendicular to wall normal
            bool horizontal = wallDy != 0;

            // Group by the perpendicular coord (same row or column)
            var groups = new Dictionary<int, List<(int x, int y)>>();
            foreach (var t in candidates)
            {
                int key = horizontal ? t.y : t.x;
                if (!groups.ContainsKey(key))
                    groups[key] = new List<(int x, int y)>();
                groups[key].Add(t);
            }

            foreach (var group in groups.Values)
            {
                group.Sort((a, b) => horizontal ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

                int runStart = 0;
                for (int i = 1; i <= group.Count; i++)
                {
                    bool broken = i == group.Count;
                    if (!broken)
                    {
                        int prev = horizontal ? group[i - 1].x : group[i - 1].y;
                        int curr = horizontal ? group[i].x : group[i].y;
                        broken = curr != prev + 1;
                    }
                    if (broken)
                    {
                        int len = i - runStart;
                        if (len > bestRun.Count)
                            bestRun = group.GetRange(runStart, len);
                        runStart = i;
                    }
                }
            }
        }

        return bestRun;
    }

    // =========================================================================
    // Phase B — Corridor Carving (minimum spanning tree, 2-tile-wide)
    // =========================================================================

    // #Misfits Change - CarveCorridors now accepts ThemeProfile for branch/loop post-passes
    private static void CarveCorridors(CellType[,] cellMap, List<RoomDef> rooms,
                                        ThemeProfile profile, Random rng, int W, int H)
    {
        if (rooms.Count < 2) return;

        // ── Pass A: MST — connect every room via minimum spanning tree ───────
        var connected   = new List<RoomDef> { rooms[0] };
        var unconnected = new List<RoomDef>(rooms.Skip(1));

        while (unconnected.Count > 0)
        {
            RoomDef? bestFrom = null;
            RoomDef? bestTo   = null;
            float    bestDist = float.MaxValue;

            foreach (var from in connected)
            {
                var (fx, fy) = from.Center;
                foreach (var to in unconnected)
                {
                    var (tx, ty) = to.Center;
                    float dist = (tx - fx) * (tx - fx) + (ty - fy) * (ty - fy);
                    if (dist < bestDist) { bestDist = dist; bestFrom = from; bestTo = to; }
                }
            }

            if (bestFrom == null || bestTo == null) break;

            var (ax, ay) = bestFrom.Center;
            var (bx, by) = bestTo.Center;
            CarveLCorridor(cellMap, ax, ay, bx, by, rng, W, H);

            connected.Add(bestTo!);
            unconnected.Remove(bestTo!);
        }

        // ── Pass B: Branch corridors — extra random connections based on BranchingFactor ──
        int extraCount = (int)(rooms.Count * profile.CorridorStyle.BranchingFactor);
        for (int i = 0; i < extraCount; i++)
        {
            int idxA = rng.Next(rooms.Count);
            int idxB;
            do { idxB = rng.Next(rooms.Count); } while (idxB == idxA);

            var (ax2, ay2) = rooms[idxA].Center;
            var (bx2, by2) = rooms[idxB].Center;
            CarveLCorridor(cellMap, ax2, ay2, bx2, by2, rng, W, H);
        }

        // ── Pass C: Loop corridors — probabilistic extra links between room pairs ────
        int loopCap   = rooms.Count;
        int loopCount = 0;
        for (int i = 0; i < rooms.Count && loopCount < loopCap; i++)
        {
            for (int j = i + 1; j < rooms.Count && loopCount < loopCap; j++)
            {
                if (rng.NextDouble() >= profile.CorridorStyle.LoopProbability)
                    continue;

                var (lx1, ly1) = rooms[i].Center;
                var (lx2, ly2) = rooms[j].Center;
                CarveLCorridor(cellMap, lx1, ly1, lx2, ly2, rng, W, H);
                loopCount++;
            }
        }
    }

    private static void CarveLCorridor(CellType[,] cellMap, int ax, int ay, int bx, int by,
                                        Random rng, int W, int H)
    {
        if (rng.Next(2) == 0) { CarveHLine(cellMap, ax, bx, ay, W, H); CarveVLine(cellMap, bx, ay, by, W, H); }
        else                  { CarveVLine(cellMap, ax, ay, by, W, H); CarveHLine(cellMap, ax, bx, by, W, H); }
    }

    private static void CarveHLine(CellType[,] cellMap, int x0, int x1, int y, int W, int H)
    {
        int minX = Math.Min(x0, x1);
        int maxX = Math.Max(x0, x1);
        for (int x = minX; x <= maxX; x++)
        {
            TrySetCorridor(cellMap, x, y,     W, H);
            TrySetCorridor(cellMap, x, y + 1, W, H); // 2-tile wide
        }
    }

    private static void CarveVLine(CellType[,] cellMap, int x, int y0, int y1, int W, int H)
    {
        int minY = Math.Min(y0, y1);
        int maxY = Math.Max(y0, y1);
        for (int y = minY; y <= maxY; y++)
        {
            TrySetCorridor(cellMap, x,     y, W, H);
            TrySetCorridor(cellMap, x + 1, y, W, H); // 2-tile wide
        }
    }

    private static void TrySetCorridor(CellType[,] cellMap, int x, int y, int W, int H)
    {
        if (x < 0 || x >= W || y < 0 || y >= H) return;
        if (cellMap[x, y] == CellType.Empty)
            cellMap[x, y] = CellType.Corridor;
    }

    // =========================================================================
    // Phase B5 — Doorway Marking
    // =========================================================================

    /// <summary>
    /// Returns the set of corridor cells that sit directly on the room/corridor boundary.
    /// These cells receive door entities instead of wall entities in Phase E.
    /// With 2-tile corridors this naturally creates double doors at each entry.
    /// </summary>
    // #Misfits Fix - Cluster adjacent doorway candidates, keep max 2 per cluster
    // to prevent long walls of doors (7+ N14DoorBunker in a row)
    private static HashSet<(int, int)> MarkDoorways(CellType[,] cellMap, int W, int H)
    {
        // Step 1: Find all corridor cells adjacent to a room/hub
        var candidates = new HashSet<(int, int)>();
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        {
            if (cellMap[x, y] != CellType.Corridor)
                continue;

            if (IsRoomAt(cellMap, x + 1, y, W, H) || IsRoomAt(cellMap, x - 1, y, W, H) ||
                IsRoomAt(cellMap, x, y + 1, W, H) || IsRoomAt(cellMap, x, y - 1, W, H))
            {
                candidates.Add((x, y));
            }
        }

        // Step 2: Flood-fill cluster adjacent candidates
        var visited = new HashSet<(int, int)>();
        var clusters = new List<List<(int, int)>>();

        foreach (var cell in candidates)
        {
            if (visited.Contains(cell)) continue;

            var cluster = new List<(int, int)>();
            var queue = new Queue<(int, int)>();
            queue.Enqueue(cell);
            visited.Add(cell);

            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                cluster.Add((cx, cy));
                // Check 4 cardinal neighbours
                (int, int)[] neighbours = { (cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1) };
                foreach (var nb in neighbours)
                {
                    if (candidates.Contains(nb) && !visited.Contains(nb))
                    {
                        visited.Add(nb);
                        queue.Enqueue(nb);
                    }
                }
            }
            clusters.Add(cluster);
        }

        // Step 3: From each cluster, keep at most 2 cells (centred)
        var doorways = new HashSet<(int, int)>();
        foreach (var cluster in clusters)
        {
            if (cluster.Count <= 2)
            {
                foreach (var c in cluster)
                    doorways.Add(c);
            }
            else
            {
                // Sort by position, take the 2 most central cells
                cluster.Sort((a, b) => a.Item1 != b.Item1
                    ? a.Item1.CompareTo(b.Item1)
                    : a.Item2.CompareTo(b.Item2));
                int mid = cluster.Count / 2;
                doorways.Add(cluster[mid - 1]);
                doorways.Add(cluster[mid]);
            }
        }

        return doorways;
    }

    private static bool IsRoomAt(CellType[,] cellMap, int x, int y, int W, int H)
    {
        if (x < 0 || x >= W || y < 0 || y >= H) return false;
        return cellMap[x, y] is CellType.Room or CellType.FactionHub;
    }

    // =========================================================================
    // Phase B6 — System 5: Door Validation
    // =========================================================================

    // #Misfits Add - System 5: validates doorway cells against structural constraints
    /// <summary>
    /// Post-processes the doorway set from MarkDoorways. Removes doorway cells that
    /// violate structural constraints:
    /// <list type="bullet">
    ///   <item>CONSTRAINT 1: must have orthogonal wall (Empty) neighbor</item>
    ///   <item>CONSTRAINT 2: must have orthogonal corridor neighbor</item>
    ///   <item>CONSTRAINT 3: must be on room perimeter (within 1 tile of room edge)</item>
    /// </list>
    /// Invalid doorway cells are removed from the set so no door entity spawns there.
    /// </summary>
    private static void ValidateDoors(
        HashSet<(int, int)> doorways,
        CellType[,] cellMap, List<RoomDef> rooms, int W, int H)
    {
        var toRemove = new List<(int, int)>();

        foreach (var (dx, dy) in doorways)
        {
            // CONSTRAINT 1 — must have at least one orthogonal Empty (wall) neighbor
            bool hasWallNeighbor =
                (InBounds(dx + 1, dy, W, H) && cellMap[dx + 1, dy] == CellType.Empty) ||
                (InBounds(dx - 1, dy, W, H) && cellMap[dx - 1, dy] == CellType.Empty) ||
                (InBounds(dx, dy + 1, W, H) && cellMap[dx, dy + 1] == CellType.Empty) ||
                (InBounds(dx, dy - 1, W, H) && cellMap[dx, dy - 1] == CellType.Empty);

            if (!hasWallNeighbor)
            {
                // No wall contact — this is just a corridor tile, not a door
                toRemove.Add((dx, dy));
                continue;
            }

            // CONSTRAINT 2 — must have at least one orthogonal Corridor neighbor
            bool hasCorridorNeighbor =
                (InBounds(dx + 1, dy, W, H) && cellMap[dx + 1, dy] == CellType.Corridor) ||
                (InBounds(dx - 1, dy, W, H) && cellMap[dx - 1, dy] == CellType.Corridor) ||
                (InBounds(dx, dy + 1, W, H) && cellMap[dx, dy + 1] == CellType.Corridor) ||
                (InBounds(dx, dy - 1, W, H) && cellMap[dx, dy - 1] == CellType.Corridor);

            if (!hasCorridorNeighbor)
            {
                // No corridor — this door leads nowhere, seal it
                toRemove.Add((dx, dy));
                continue;
            }

            // CONSTRAINT 3 — must be on room perimeter (within 1 tile of any room's bounding box edge)
            bool onPerimeter = false;
            foreach (var room in rooms)
            {
                if (dx < room.X - 1 || dx > room.X + room.W ||
                    dy < room.Y - 1 || dy > room.Y + room.H)
                    continue; // not near this room

                // Check if within 1 tile of the room's bounding box edge
                bool nearEdge = dx <= room.X || dx >= room.X + room.W - 1 ||
                                dy <= room.Y || dy >= room.Y + room.H - 1;
                if (nearEdge)
                {
                    onPerimeter = true;
                    break;
                }
            }

            if (!onPerimeter)
            {
                // Interior door — remove it
                toRemove.Add((dx, dy));
            }
        }

        foreach (var cell in toRemove)
            doorways.Remove(cell);
    }

    // =========================================================================
    // Phase C — Sewer Water Channels
    // =========================================================================

    // #Misfits Change - 3-wide sewer channels: catwalk | water | catwalk (was 2-wide double water)
    // #Misfits Fix - Added forceCarve/overrideChance: non-Sewer maps now get sparse probabilistic channels
    private static void CarveSewerWaterChannels(CellType[,] cellMap, Random rng, int W, int H,
                                                 bool forceCarve, float overrideChance)
    {
        int channelCount = rng.Next(2, 5);

        for (int i = 0; i < channelCount; i++)
        {
            // Non-Sewer maps skip most channels — sparse water intrusion only
            if (!forceCarve && rng.NextDouble() >= overrideChance)
                continue;
            bool horizontal = rng.Next(2) == 0;

            if (horizontal)
            {
                // 3-wide horizontal: (x, y)=Platform | (x, y+1)=WaterChannel | (x, y+2)=Platform
                int y      = rng.Next(H / 4, H * 3 / 4 - 2);  // ensure y+2 stays in bounds
                int xStart = rng.Next(2, W / 4);
                int xEnd   = rng.Next(W * 3 / 4, W - 2);
                for (int x = xStart; x <= xEnd; x++)
                {
                    if (InBounds(x, y,     W, H) && cellMap[x, y]     == CellType.Empty) cellMap[x, y]     = CellType.Platform;
                    if (InBounds(x, y + 1, W, H) && cellMap[x, y + 1] == CellType.Empty) cellMap[x, y + 1] = CellType.WaterChannel;
                    if (InBounds(x, y + 2, W, H) && cellMap[x, y + 2] == CellType.Empty) cellMap[x, y + 2] = CellType.Platform;
                }
            }
            else
            {
                // 3-wide vertical: (x, y)=Platform | (x+1, y)=WaterChannel | (x+2, y)=Platform
                int x      = rng.Next(W / 4, W * 3 / 4 - 2);  // ensure x+2 stays in bounds
                int yStart = rng.Next(2, H / 4);
                int yEnd   = rng.Next(H * 3 / 4, H - 2);
                for (int y = yStart; y <= yEnd; y++)
                {
                    if (InBounds(x,     y, W, H) && cellMap[x,     y] == CellType.Empty) cellMap[x,     y] = CellType.Platform;
                    if (InBounds(x + 1, y, W, H) && cellMap[x + 1, y] == CellType.Empty) cellMap[x + 1, y] = CellType.WaterChannel;
                    if (InBounds(x + 2, y, W, H) && cellMap[x + 2, y] == CellType.Empty) cellMap[x + 2, y] = CellType.Platform;
                }
            }
        }
    }

    // =========================================================================
    // Phase D — Tile Painting
    // =========================================================================

    // #Misfits Change - PaintTiles driven by ThemeProfile.TilePalette + WFC room tile map + env state rubble
    private void PaintTiles(
        CellType[,] cellMap, EntityUid gridUid, MapGridComponent grid,
        ThemeProfile profile, EnvironmentalStateModifiers envMods, Random rng, int W, int H,
        Dictionary<(int, int), string>? roomTileMap = null)
    {
        var palette = profile.TilePalette;

        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        {
            string tileId = cellMap[x, y] switch
            {
                CellType.Corridor     => palette.CorridorFloorTiles[rng.Next(palette.CorridorFloorTiles.Length)],
                // #Misfits Change - WFC map lookup with rubble override, fallback to random palette
                CellType.Room         => roomTileMap != null && roomTileMap.TryGetValue((x, y), out var t)
                                         ? ApplyRubbleOverride(t, envMods, rng)
                                         : palette.RoomFloorTiles[rng.Next(palette.RoomFloorTiles.Length)],
                CellType.FactionHub   => palette.HubFloorTiles[rng.Next(palette.HubFloorTiles.Length)],
                CellType.WaterChannel => TileWaterDeep,
                CellType.Platform     => TileGrate,
                _                     => palette.BackgroundTile,
            };

            SetTile(gridUid, grid, x, y, tileId);
        }
    }

    /// <summary>
    /// Applies environmental rubble override to a pre-assigned tile.
    /// Used by the WFC tile map to inject damage without breaking tile coherence.
    /// </summary>
    // #Misfits Add - Rubble override extracted from PickRoomTile for WFC integration
    private static string ApplyRubbleOverride(
        string tile, EnvironmentalStateModifiers envMods, Random rng)
        => envMods.RubbleTileReplaceFraction > 0 &&
           rng.NextDouble() < envMods.RubbleTileReplaceFraction
           ? TileRubble : tile;

    // #Misfits Removed - PickRoomTile and BuildRoomTileMap replaced by BuildRoomTileMapWFC
    // with WFC-style primary/accent/edge tile assignment and neighbor smoothing.

    /// <summary>
    /// Builds a WFC-inspired tile assignment map for all rooms. Each room gets:
    ///   - A primary tile (dominant ~70% of interior cells)
    ///   - An accent tile (variation ~20%)
    ///   - An edge tile (perimeter + doorway proximity ~10%)
    /// A single smoothing pass removes isolated accent tiles (salt-and-pepper noise)
    /// by reassigning cells with 0 same-tile neighbors to their most common neighbor tile.
    /// </summary>
    // #Misfits Add - WFC-style tile coherence for natural room material distribution
    private static Dictionary<(int, int), string> BuildRoomTileMapWFC(
        List<RoomDef> rooms, ThemeProfile profile,
        CellType[,] cellMap, Random rng, int W, int H)
    {
        var tiles = profile.TilePalette.RoomFloorTiles;
        if (tiles.Length == 0) return new Dictionary<(int, int), string>();

        var map = new Dictionary<(int, int), string>();

        foreach (var room in rooms)
        {
            // Pick primary, accent, edge tiles for this room
            string primary = tiles[rng.Next(tiles.Length)];
            string accent = tiles[rng.Next(tiles.Length)];
            // Re-roll accent once if it matches primary (allow if only 1 tile available)
            if (accent == primary && tiles.Length > 1)
                accent = tiles[rng.Next(tiles.Length)];
            string edge = tiles[rng.Next(tiles.Length)];

            for (int rx = room.X; rx < room.X + room.W; rx++)
            for (int ry = room.Y; ry < room.Y + room.H; ry++)
            {
                // Check if this cell is on the room's inner perimeter (1 tile inset from walls)
                bool isInnerPerimeter = (rx == room.X + 1 || rx == room.X + room.W - 2 ||
                                         ry == room.Y + 1 || ry == room.Y + room.H - 2);

                // Check if adjacent to a corridor cell (doorway proximity)
                bool nearDoorway = false;
                for (int dx = -1; dx <= 1 && !nearDoorway; dx++)
                for (int dy = -1; dy <= 1 && !nearDoorway; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = rx + dx, ny = ry + dy;
                    if (nx >= 0 && nx < W && ny >= 0 && ny < H && cellMap[nx, ny] == CellType.Corridor)
                        nearDoorway = true;
                }

                // Assign tile based on position context
                string assigned;
                if (nearDoorway)
                    assigned = rng.NextDouble() < 0.6 ? edge : primary;
                else if (isInnerPerimeter)
                    assigned = rng.NextDouble() < 0.8 ? edge : primary;
                else
                    assigned = rng.NextDouble() < 0.7 ? primary : accent;

                map[(rx, ry)] = assigned;
            }
        }

        // ── Smoothing pass: remove isolated accent tiles ─────────────────────
        // If a cell has 0 neighbors with the same tile, reassign to most common neighbor tile.
        var offsets = new (int, int)[] { (-1, 0), (1, 0), (0, -1), (0, 1) };
        var toFix = new List<((int, int) pos, string newTile)>();

        foreach (var (pos, tile) in map)
        {
            int sameCount = 0;
            var neighborCounts = new Dictionary<string, int>();

            foreach (var (dx, dy) in offsets)
            {
                var nk = (pos.Item1 + dx, pos.Item2 + dy);
                if (!map.TryGetValue(nk, out var nt)) continue;
                if (nt == tile) sameCount++;
                neighborCounts[nt] = neighborCounts.GetValueOrDefault(nt) + 1;
            }

            // Isolated cell — reassign to most common neighbor
            if (sameCount == 0 && neighborCounts.Count > 0)
            {
                string best = tile;
                int bestN = 0;
                foreach (var (nt, cnt) in neighborCounts)
                {
                    if (cnt > bestN) { best = nt; bestN = cnt; }
                }
                toFix.Add((pos, best));
            }
        }

        foreach (var (pos, newTile) in toFix)
            map[pos] = newTile;

        return map;
    }

    // #Misfits Removed - Tile selection helpers replaced by ThemeProfile.TilePalette lookups in PaintTiles
    /*
    private static string GetCorridorTile(UndergroundTheme theme, Random rng) => theme switch { ... };
    private static string GetRoomTile(UndergroundTheme theme, Random rng) => theme switch { ... };
    private static string GetHubTile(UndergroundTheme theme, Random rng) => theme switch { ... };
    */

    // =========================================================================
    // Phase E — Entity Spawning
    // =========================================================================

    // #Misfits Change - SpawnEntities now driven by ThemeProfile + EnvironmentalStateModifiers
    private void SpawnEntities(
        CellType[,] cellMap, List<RoomDef> rooms, HashSet<(int, int)> doorways,
        EntityUid gridUid, MapGridComponent grid,
        ThemeProfile profile, EnvironmentalStateModifiers envMods, int difficultyTier, Random rng, int W, int H)
    {
        var palette = profile.TilePalette;
        var theme   = profile.Theme;

        // ── 1. Wall and water pass ─────────────────────────────────────────────
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        {
            var cell = cellMap[x, y];

            if (cell == CellType.Platform)
            {
                // #Misfits Add - Catwalk entity on grate tiles beside sewer water channels
                SpawnAt("Catwalk", gridUid, grid, x, y);
                continue;
            }

            if (cell == CellType.WaterChannel)
            {
                SpawnAt(WaterSewerEntity, gridUid, grid, x, y);
                continue;
            }

            if (cell != CellType.Empty)
                continue;

            // #Misfits Change - Wall selection now from profile TilePalette
            bool adjRoom = IsAdjacentToTraversable(cellMap, x, y, W, H);
            string wallProto;
            if (adjRoom)
            {
                bool adjHub = (InBounds(x + 1, y, W, H) && cellMap[x + 1, y] == CellType.FactionHub)
                           || (InBounds(x - 1, y, W, H) && cellMap[x - 1, y] == CellType.FactionHub)
                           || (InBounds(x, y + 1, W, H) && cellMap[x, y + 1] == CellType.FactionHub)
                           || (InBounds(x, y - 1, W, H) && cellMap[x, y - 1] == CellType.FactionHub);
                wallProto = adjHub ? palette.HubWallEntity : palette.RoomWallEntity;
            }
            else
            {
                wallProto = WallRockFill;
            }

            SpawnAt(wallProto, gridUid, grid, x, y);
        }

        // ── 2. Door pass: threshold cells between corridor and room ────────────
        foreach (var (dx, dy) in doorways)
        {
            bool hasCorridor =
                (InBounds(dx + 1, dy, W, H) && cellMap[dx + 1, dy] == CellType.Corridor) ||
                (InBounds(dx - 1, dy, W, H) && cellMap[dx - 1, dy] == CellType.Corridor) ||
                (InBounds(dx, dy + 1, W, H) && cellMap[dx, dy + 1] == CellType.Corridor) ||
                (InBounds(dx, dy - 1, W, H) && cellMap[dx, dy - 1] == CellType.Corridor);
            if (!hasCorridor) continue;

            bool isHub = (InBounds(dx + 1, dy, W, H) && cellMap[dx + 1, dy] == CellType.FactionHub)
                       || (InBounds(dx - 1, dy, W, H) && cellMap[dx - 1, dy] == CellType.FactionHub)
                       || (InBounds(dx, dy + 1, W, H) && cellMap[dx, dy + 1] == CellType.FactionHub)
                       || (InBounds(dx, dy - 1, W, H) && cellMap[dx, dy - 1] == CellType.FactionHub);

            // #Misfits Change - Door entity from profile palette
            SpawnAt(isHub ? palette.HubDoorEntity : palette.RoomDoorEntity, gridUid, grid, dx, dy);
            if (theme == UndergroundTheme.Sewer)
                SetTile(gridUid, grid, dx, dy, TileSewerGrate);
        }

        // ── 3. Room dressing: furniture + mobs + lights + decals ──────────────
        var lightCfg = profile.LightConfig;

        // #Misfits Add - Track light floor cells and reactor center for LV wire routing (Vault only)
        var lightFloorPositions  = new List<(int x, int y)>();
        (int x, int y)? reactorCenter = null;

        // #Misfits Add - Pre-compute hub list for depth factor calculations
        var hubs = rooms.Where(r => r.RoomType == RoomType.FactionHub).ToList();

        foreach (var room in rooms)
        {
            if (theme == UndergroundTheme.Vault && room.RoomType == RoomType.VaultReactor && !reactorCenter.HasValue)
                reactorCenter = room.Center;

            // #Misfits Add - Compute depth factor once per room for density/spawn scaling
            float depthFactor = GetDepthFactor(room, hubs, W, H);
            // #Misfits Change - pass cellMap/W/H for System 4 placement rules
            DressRoom(room, gridUid, grid, profile, envMods, rng, cellMap, W, H, depthFactor);
            SpawnRoomMobs(room, gridUid, grid, profile, envMods, difficultyTier, rng, depthFactor);

            // Sub-pass: Lights (profile-driven count and style)
            int lightCount = profile.GetLightCount(room.RoomType);
            // #Misfits Add - Environmental light reduction: skip a fraction of lights in degraded environments
            if (envMods.LightReductionFraction > 0)
                lightCount = Math.Max(1, (int)(lightCount * (1f - envMods.LightReductionFraction)));

            int innerW = room.W - 2;
            int innerH = room.H - 2;
            
            if (innerW > 0 && innerH > 0)
            {
                var taken = new HashSet<(int, int)>();

                if (lightCfg.Style == LightStyle.GroundPost)
                {
                    // Ground post light: random interior placement
                    for (int i = 0; i < lightCount && taken.Count < lightCount; i++)
                    {
                        for (int attempt = 0; attempt < 5; attempt++)
                        {
                            int lx = room.X + 1 + rng.Next(innerW);
                            int ly = room.Y + 1 + rng.Next(innerH);
                            if (taken.Contains((lx, ly))) continue;
                            SpawnLight(gridUid, grid, lx, ly, lightCfg, Direction.South);
                            taken.Add((lx, ly));
                            break;
                        }
                    }
                }
                else
                {
                    // Wall-mounted light: placed AT the wall cell facing inward
                    var wallCandidates = new List<(int wx, int wy, Direction facing, int fx, int fy)>();
                    for (int lx = room.X + 1; lx < room.X + room.W - 1; lx++)
                    for (int ly = room.Y + 1; ly < room.Y + room.H - 1; ly++)
                    {
                        if (InBounds(lx, ly + 1, W, H) && cellMap[lx, ly + 1] == CellType.Empty)
                            wallCandidates.Add((lx, ly + 1, Direction.South, lx, ly));
                        else if (InBounds(lx, ly - 1, W, H) && cellMap[lx, ly - 1] == CellType.Empty)
                            wallCandidates.Add((lx, ly - 1, Direction.North, lx, ly));
                        else if (InBounds(lx + 1, ly, W, H) && cellMap[lx + 1, ly] == CellType.Empty)
                            wallCandidates.Add((lx + 1, ly, Direction.West,  lx, ly));
                        else if (InBounds(lx - 1, ly, W, H) && cellMap[lx - 1, ly] == CellType.Empty)
                            wallCandidates.Add((lx - 1, ly, Direction.East,  lx, ly));
                    }

                    for (int i = wallCandidates.Count - 1; i > 0; i--)
                    {
                        int j = rng.Next(i + 1);
                        (wallCandidates[i], wallCandidates[j]) = (wallCandidates[j], wallCandidates[i]);
                    }

                    foreach (var (wx, wy, facing, fx, fy) in wallCandidates)
                    {
                        if (taken.Count >= lightCount) break;

                        bool tooClose = false;
                        foreach (var (tx, ty) in taken)
                        {
                            if (Math.Abs(wx - tx) + Math.Abs(wy - ty) < 3)
                            {
                                tooClose = true;
                                break;
                            }
                        }
                        if (tooClose) continue;

                        SpawnLight(gridUid, grid, wx, wy, lightCfg, facing);
                        taken.Add((wx, wy));
                        if (theme == UndergroundTheme.Vault)
                            lightFloorPositions.Add((fx, fy));
                    }
                }

                // Sub-pass: Decals via DecalSystem (profile-driven pool + env density multiplier)
                int decalCount = (int)(rng.Next(2, 7) * envMods.DecalDensityMult);
                var decalTaken = new HashSet<(int, int)>();
                // #Misfits Add - Nature decals injected when Overgrown env state is active
                var effectiveDecalPool = envMods.NatureDecals
                    ? profile.DecalPool.Concat(new[] { "Flowersbr", "Flowerspv", "Grassd1", "Grassd2", "Grassd3" }).ToArray()
                    : profile.DecalPool;
                for (int i = 0; i < decalCount && decalTaken.Count < decalCount; i++)
                {
                    for (int attempt = 0; attempt < 8; attempt++)
                    {
                        int ddx = room.X + 1 + rng.Next(innerW);
                        int ddy = room.Y + 1 + rng.Next(innerH);
                        if (decalTaken.Contains((ddx, ddy))) continue;
                        if (effectiveDecalPool.Length > 0)
                        {
                            var decalProto = effectiveDecalPool[rng.Next(effectiveDecalPool.Length)];
                            var decalCoords = _mapSystem.GridTileToLocal(gridUid, grid, new Vector2i(ddx, ddy));
                            _decalSystem.TryAddDecal(decalProto, decalCoords, out _);
                        }
                        decalTaken.Add((ddx, ddy));
                        break;
                    }
                }
            }
        }

        // #Misfits Removed - Exit points are now placed by N14ExpeditionSystem.SpawnExitPoints()
        // which correctly sets N14ExpeditionExitComponent.ExpeditionMap. The generator-spawned exits
        // were inert because SpawnAt() never set that component field.
        // foreach (var hub in rooms.Where(r => r.RoomType == RoomType.FactionHub))
        // {
        //     var (hubCx, hubCy) = hub.Center;
        //     SpawnAt("N14ExpeditionExitPoint", gridUid, grid, hubCx, hubCy);
        // }

        // ── 5. Navigation marker pass ──────────────────────────────────────────
        {
            var hubCenters = rooms
                .Where(r => r.RoomType == RoomType.FactionHub)
                .Select(r => r.Center)
                .ToList();

            var cardinalOffsets = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            foreach (var (hcx, hcy) in hubCenters)
            {
                foreach (var (ox, oy) in cardinalOffsets)
                {
                    int mx = hcx + ox, my = hcy + oy;
                    if (!InBounds(mx, my, W, H)) continue;
                    if (cellMap[mx, my] is not (CellType.FactionHub or CellType.Room or CellType.Corridor)) continue;
                    string arrowDecal = rng.Next(2) == 0 ? "N14GraffitiArrowshelterleft" : "N14GraffitiArrowshelterright";
                    var arrowCoords = _mapSystem.GridTileToLocal(gridUid, grid, new Vector2i(mx, my));
                    _decalSystem.TryAddDecal(arrowDecal, arrowCoords, out _);
                }
            }

            for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
            {
                if (cellMap[x, y] is not (CellType.Room or CellType.Corridor)) continue;
                if (rng.Next(100) >= 8) continue;

                bool nearHub = false;
                foreach (var (hcx, hcy) in hubCenters)
                {
                    if (Math.Max(Math.Abs(x - hcx), Math.Abs(y - hcy)) <= 8) { nearHub = true; break; }
                }
                if (!nearHub) continue;

                string arrowDecal = rng.Next(2) == 0 ? "N14GraffitiArrowshelterleft" : "N14GraffitiArrowshelterright";
                var markerCoords = _mapSystem.GridTileToLocal(gridUid, grid, new Vector2i(x, y));
                _decalSystem.TryAddDecal(arrowDecal, markerCoords, out _);
            }
        }

        // ── 6. Large-room sentry guardian pass ──────────────────────────────────
        foreach (var room in rooms)
        {
            if (room.W * room.H <= 100) continue;
            if (rng.Next(100) >= 15) continue;
            var (scx, scy) = room.Center;
            string sentryProto = rng.Next(2) == 0 ? "N14MobRobotSentryBot" : "N14MobRobotSentryBotBallistic";
            SpawnAt(sentryProto, gridUid, grid, scx, scy);
            if (InBounds(scx + 1, scy, W, H))
                SpawnAt("N14LootCrateVaultBigRusted", gridUid, grid, scx + 1, scy);
        }

        // ── 7. LV wire routing: Vault only ─────────────────────────────────────
        if (theme == UndergroundTheme.Vault && reactorCenter.HasValue)
        {
            foreach (var floorPos in lightFloorPositions)
                RouteWire(floorPos, reactorCenter.Value, cellMap, gridUid, grid, W, H);
        }
    }

    // #Misfits Removed - Wall/door helpers replaced by ThemeProfile.TilePalette entity lookups in SpawnEntities
    /*
    private static string GetRoomWallProto(UndergroundTheme theme, CellType[,] cellMap, int x, int y, int W, int H) => ...;
    private static string GetHubDoor(UndergroundTheme theme) => ...;
    private static string GetRoomDoor(UndergroundTheme theme) => ...;
    */

    // ─────────────────────────────────────────────────────────────────────────
    // Room Dressing — furniture placement (System 4)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Places themed furniture inside the room using placement rule constraints.
    /// WallAttached entities go on wall cells, WallAdjacent on floor tiles next to walls,
    /// OnSurface entities stack on parent entities, WallRow creates kitchen table lines,
    /// FreeStanding uses edge-biased random floor placement.
    /// #Misfits Change - System 4: placement-rule-aware dressing replaces unconstrained spawning
    /// </summary>
    // #Misfits Add - depthFactor scales furniture density: near hub = sparse staging, center = dense
    private void DressRoom(RoomDef room, EntityUid gridUid, MapGridComponent grid,
                            ThemeProfile profile, EnvironmentalStateModifiers envMods, Random rng,
                            CellType[,] cellMap, int W, int H,
                            float depthFactor = 0.5f)
    {
        int innerW = room.W - 2;
        int innerH = room.H - 2;
        if (innerW < 1 || innerH < 1) return;

        // #Misfits Change - Raised all item counts for "lived-in" room feel; new room types added
        int itemCount = room.RoomType switch
        {
            RoomType.FactionHub       => rng.Next(12, 19),
            RoomType.Central          => rng.Next(10, 17),
            RoomType.VaultOverseer    => rng.Next(8, 15),
            RoomType.VaultVault       => rng.Next(8, 15),
            RoomType.VaultArmory      => rng.Next(8, 15),
            RoomType.VaultBarracks    => rng.Next(6, 13),
            RoomType.VaultLab         => rng.Next(6, 13),
            RoomType.VaultKitchen     => rng.Next(6, 13),
            RoomType.VaultHydroponics => rng.Next(6, 12),
            RoomType.VaultRecreation  => rng.Next(8, 14),
            RoomType.VaultReactor     => rng.Next(4, 9),
            RoomType.SewerGrotto      => rng.Next(4, 9),
            RoomType.SewerPump        => rng.Next(4, 9),
            RoomType.SewerNest        => rng.Next(4, 9),
            RoomType.SewerCamp        => rng.Next(5, 10),
            RoomType.SewerJunction    => rng.Next(2, 6),
            RoomType.SewerTunnel      => rng.Next(1, 3),
            RoomType.MetroPlatform    => rng.Next(6, 12),
            RoomType.MetroMaintenance => rng.Next(4, 9),
            RoomType.MetroDepot       => rng.Next(4, 9),
            RoomType.MetroCommand     => rng.Next(6, 12),
            RoomType.MetroTunnel      => rng.Next(1, 3),
            _                         => rng.Next(2, 6),
        };

        // #Misfits Add - Depth-scaled item count: 50% near hub → 130% at center
        float depthScalar = 0.5f + (1.3f - 0.5f) * depthFactor;
        itemCount = Math.Max(1, (int)(itemCount * depthScalar));

        // #Misfits Change - Furniture pool via profile lookup (RoomTypeDefinition.FurniturePoolKey)
        var def = profile.GetRoomDef(room.RoomType);
        string poolKey = def?.FurniturePoolKey ?? (room.RoomType == RoomType.FactionHub ? "hub" : "standard");
        var pool = profile.GetFurniturePool(poolKey);
        if (pool.Length == 0)
            pool = profile.GetFurniturePool("standard");

        // ── tracking sets ───────────────────────────────────────────────────────
        var occupiedTiles  = new HashSet<(int, int)>();
        // Maps tile → proto name for OnSurface parent lookups
        var placedEntities = new Dictionary<(int, int), string>();
        string roomTypeName = room.RoomType.ToString();

        // ── precompute tile lists ───────────────────────────────────────────────
        var wallTiles         = FindWallTilesInRoom(room, cellMap, W, H);
        var wallAdjacentTiles = FindWallAdjacentFloorTiles(room, cellMap, W, H);

        // ══════════════════════════════════════════════════════════════════════
        // RULE E — Office composition: mandatory desk + chair + terminal
        // ══════════════════════════════════════════════════════════════════════
        // #Misfits Add - System 4 Rule E: office rooms get mandatory desk/chair/terminal group
        bool isOfficeRoom = room.RoomType is RoomType.VaultOverseer or RoomType.MetroCommand;
        if (isOfficeRoom && wallAdjacentTiles.Count > 0)
        {
            // Place desk against a wall
            var shuffledWA = new List<(int x, int y)>(wallAdjacentTiles);
            for (int si = shuffledWA.Count - 1; si > 0; si--)
            {
                int sj = rng.Next(si + 1);
                (shuffledWA[si], shuffledWA[sj]) = (shuffledWA[sj], shuffledWA[si]);
            }

            (int x, int y) deskTile = (-1, -1);
            foreach (var t in shuffledWA)
            {
                if (occupiedTiles.Contains(t)) continue;
                deskTile = t;
                break;
            }

            if (deskTile.x >= 0)
            {
                // Spawn the desk
                string deskProto = "N14TableDeskMetalSmall";
                SpawnAt(deskProto, gridUid, grid, deskTile.x, deskTile.y);
                occupiedTiles.Add(deskTile);
                placedEntities[deskTile] = deskProto;
                itemCount--;

                // Spawn terminal on desk (OnSurface)
                SpawnAt("N14ComputerTerminal", gridUid, grid, deskTile.x, deskTile.y);
                itemCount--;

                // Place chair one tile toward room center from desk, facing center
                var (cx, cy) = room.Center;
                int bestDx = 0, bestDy = 0;
                float bestDot = float.MinValue;
                foreach (var (ddx, ddy) in OrthoDirs)
                {
                    int chairX = deskTile.x + ddx, chairY = deskTile.y + ddy;
                    if (chairX < room.X || chairX >= room.X + room.W ||
                        chairY < room.Y || chairY >= room.Y + room.H)
                        continue;
                    if (occupiedTiles.Contains((chairX, chairY))) continue;
                    var ct = cellMap[chairX, chairY];
                    if (ct is not (CellType.Room or CellType.FactionHub)) continue;
                    // Dot product with direction to center — pick direction that points most toward center
                    float dot = ddx * (cx - deskTile.x) + ddy * (cy - deskTile.y);
                    if (dot > bestDot) { bestDot = dot; bestDx = ddx; bestDy = ddy; }
                }

                int chairFinalX = deskTile.x + bestDx;
                int chairFinalY = deskTile.y + bestDy;
                if (bestDot > float.MinValue && !occupiedTiles.Contains((chairFinalX, chairFinalY)))
                {
                    SpawnAt("Chair", gridUid, grid, chairFinalX, chairFinalY);
                    occupiedTiles.Add((chairFinalX, chairFinalY));
                    placedEntities[(chairFinalX, chairFinalY)] = "Chair";
                    itemCount--;
                }

                // Optional filing cabinet on same or adjacent wall
                if (itemCount > 0)
                {
                    foreach (var t in shuffledWA)
                    {
                        if (occupiedTiles.Contains(t)) continue;
                        SpawnAt("N14LootFilingCabinet", gridUid, grid, t.x, t.y);
                        occupiedTiles.Add(t);
                        placedEntities[t] = "N14LootFilingCabinet";
                        itemCount--;
                        break;
                    }
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // RULE D — WallRow: kitchen tables along longest wall
        // ══════════════════════════════════════════════════════════════════════
        // #Misfits Add - System 4 Rule D: kitchen rooms get table row along longest wall
        bool isKitchenRoom = room.RoomType == RoomType.VaultKitchen;
        if (isKitchenRoom)
        {
            var wallRun = FindLongestWallRun(room, cellMap, W, H);
            int tablesToPlace = Math.Min(wallRun.Count, Math.Max(2, itemCount / 2));
            string[] kitchenAppliances = { "N14CookingStove", "N14CookingGrille", "N14Microwave" };
            int applianceIdx = 0;

            for (int ti = 0; ti < tablesToPlace && ti < wallRun.Count; ti++)
            {
                var tile = wallRun[ti];
                if (occupiedTiles.Contains(tile)) continue;

                SpawnAt("TableWood", gridUid, grid, tile.x, tile.y);
                occupiedTiles.Add(tile);
                placedEntities[tile] = "TableWood";
                itemCount--;

                // Place appliance on surface (OnSurface Rule C)
                if (applianceIdx < kitchenAppliances.Length)
                {
                    SpawnAt(kitchenAppliances[applianceIdx], gridUid, grid, tile.x, tile.y);
                    applianceIdx++;
                }
                else
                {
                    // Cycle appliances
                    SpawnAt(kitchenAppliances[rng.Next(kitchenAppliances.Length)], gridUid, grid, tile.x, tile.y);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // RequiredFeatures pre-pass (placement-rule-aware)
        // ══════════════════════════════════════════════════════════════════════
        // #Misfits Add - RequiredFeatures use placement rules instead of random floor
        if (def?.RequiredFeatures != null)
        {
            foreach (var reqProto in def.RequiredFeatures)
            {
                if (itemCount <= 0) break;
                var rule = GetPlacementRule(reqProto, roomTypeName);
                if (!TryPlaceByRule(reqProto, rule, room, gridUid, grid,
                                     wallTiles, wallAdjacentTiles, occupiedTiles, placedEntities, rng,
                                     cellMap, W, H))
                    continue;
                itemCount--;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // Main furniture loop: placement-rule-aware
        // ══════════════════════════════════════════════════════════════════════
        // #Misfits Change - System 4: dispatch each entity to the correct placement rule
        int mainBudget = itemCount * 5;
        int placed = 0;

        for (int i = 0; i < mainBudget && placed < itemCount; i++)
        {
            string proto = pool[rng.Next(pool.Length)];

            // Skip door/airlock entities from furniture pool — doors come from corridor system
            if (proto.Contains("Door", StringComparison.OrdinalIgnoreCase) ||
                proto.Contains("Airlock", StringComparison.OrdinalIgnoreCase))
                continue;

            var rule = GetPlacementRule(proto, roomTypeName);

            // OnSurface items need a parent — defer until after main pass
            if (rule == PlacementRule.OnSurface)
            {
                if (TryPlaceOnSurface(proto, occupiedTiles, placedEntities, gridUid, grid))
                    placed++;
                continue;
            }

            if (TryPlaceByRule(proto, rule, room, gridUid, grid,
                                wallTiles, wallAdjacentTiles, occupiedTiles, placedEntities, rng,
                                cellMap, W, H))
                placed++;
        }

        // ── Junk scatter: env-state-scaled junk piles ───────────────────────────
        int junkCount = (int)(rng.Next(1, 4) * envMods.JunkDensityMult);
        for (int j = 0; j < junkCount; j++)
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                int jx = room.X + 1 + rng.Next(innerW);
                int jy = room.Y + 1 + rng.Next(innerH);
                if (occupiedTiles.Contains((jx, jy))) continue;
                SpawnAt(profile.JunkPool[rng.Next(profile.JunkPool.Length)], gridUid, grid, jx, jy);
                occupiedTiles.Add((jx, jy));
                break;
            }
        }

        // ── Floor entity scatter ────────────────────────────────────────────────
        int scatterCount = rng.Next(3, 9);
        for (int s = 0; s < scatterCount; s++)
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                int sx = room.X + 1 + rng.Next(innerW);
                int sy = room.Y + 1 + rng.Next(innerH);
                if (occupiedTiles.Contains((sx, sy))) continue;
                SpawnAt(profile.FloorScatterPool[rng.Next(profile.FloorScatterPool.Length)], gridUid, grid, sx, sy);
                occupiedTiles.Add((sx, sy));
                break;
            }
        }

        // ── Blueprint scatter: 5% chance in high-value rooms ────────────────────
        bool isBlueprintRoom = room.RoomType is RoomType.VaultArmory or RoomType.VaultVault or RoomType.FactionHub;
        if (isBlueprintRoom && rng.Next(100) < 5 && profile.BlueprintPool.Length > 0)
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                int bx = room.X + 1 + rng.Next(innerW);
                int by = room.Y + 1 + rng.Next(innerH);
                if (occupiedTiles.Contains((bx, by))) continue;
                SpawnAt(profile.BlueprintPool[rng.Next(profile.BlueprintPool.Length)], gridUid, grid, bx, by);
                break;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // System 4 — Placement Dispatch Helpers
    // ─────────────────────────────────────────────────────────────────────────

    // #Misfits Add - dispatches entity to correct placement tile based on rule
    /// <summary>
    /// Places <paramref name="proto"/> according to <paramref name="rule"/>.
    /// Returns true if successfully placed, false if no valid tile found.
    /// </summary>
    private bool TryPlaceByRule(
        string proto, PlacementRule rule,
        RoomDef room, EntityUid gridUid, MapGridComponent grid,
        List<(int x, int y)> wallTiles,
        List<(int x, int y)> wallAdjacentTiles,
        HashSet<(int, int)> occupiedTiles,
        Dictionary<(int, int), string> placedEntities,
        Random rng,
        CellType[,] cellMap, int W, int H)
    {
        switch (rule)
        {
            case PlacementRule.WallAttached:
            {
                // Spawn ON a wall cell (CellType.Empty adjacent to Room)
                var candidates = wallTiles.Where(t => !occupiedTiles.Contains(t)).ToList();
                if (candidates.Count == 0) return false;
                var tile = candidates[rng.Next(candidates.Count)];
                SpawnAt(proto, gridUid, grid, tile.x, tile.y);
                occupiedTiles.Add(tile);
                placedEntities[tile] = proto;
                return true;
            }

            case PlacementRule.WallAdjacent:
            {
                // Spawn on floor tile adjacent to wall
                var candidates = wallAdjacentTiles.Where(t => !occupiedTiles.Contains(t)).ToList();
                if (candidates.Count == 0) return false;
                var tile = candidates[rng.Next(candidates.Count)];
                SpawnAt(proto, gridUid, grid, tile.x, tile.y);
                occupiedTiles.Add(tile);
                placedEntities[tile] = proto;

                // Special: armory shelves get an immediate weapon spawn on same tile
                if (proto.Contains("ShelfMetal", StringComparison.OrdinalIgnoreCase) &&
                    room.RoomType is RoomType.VaultArmory)
                {
                    string[] weapons = { "N14WeaponPistol10mm", "N14WeaponRifleVarmint", "N14WeaponShotgunCaravan" };
                    SpawnAt(weapons[rng.Next(weapons.Length)], gridUid, grid, tile.x, tile.y);
                }

                return true;
            }

            case PlacementRule.OnSurface:
                return TryPlaceOnSurface(proto, occupiedTiles, placedEntities, gridUid, grid);

            case PlacementRule.WallRow:
            {
                // Handled by kitchen pre-pass; if we get here as standalone, fall through to FreeStanding
                return TryPlaceFreeStanding(proto, room, gridUid, grid, occupiedTiles, placedEntities, rng);
            }

            case PlacementRule.FreeStanding:
            default:
                return TryPlaceFreeStanding(proto, room, gridUid, grid, occupiedTiles, placedEntities, rng);
        }
    }

    // #Misfits Add - FreeStanding placement: edge-biased random floor tile
    private bool TryPlaceFreeStanding(
        string proto, RoomDef room,
        EntityUid gridUid, MapGridComponent grid,
        HashSet<(int, int)> occupiedTiles,
        Dictionary<(int, int), string> placedEntities,
        Random rng)
    {
        int innerW = room.W - 2;
        int innerH = room.H - 2;
        if (innerW < 1 || innerH < 1) return false;

        for (int attempt = 0; attempt < 8; attempt++)
        {
            int fx, fy;
            bool useEdge = rng.Next(10) < 7 && innerW >= 2 && innerH >= 2;
            if (useEdge)
            {
                int side = rng.Next(4);
                switch (side)
                {
                    case 0: fx = room.X + 1;         fy = room.Y + 1 + rng.Next(innerH); break;
                    case 1: fx = room.X + innerW;    fy = room.Y + 1 + rng.Next(innerH); break;
                    case 2: fx = room.X + 1 + rng.Next(innerW); fy = room.Y + 1;         break;
                    default:fx = room.X + 1 + rng.Next(innerW); fy = room.Y + innerH;    break;
                }
            }
            else
            {
                fx = room.X + 1 + rng.Next(innerW);
                fy = room.Y + 1 + rng.Next(innerH);
            }
            if (occupiedTiles.Contains((fx, fy))) continue;

            SpawnAt(proto, gridUid, grid, fx, fy);
            occupiedTiles.Add((fx, fy));
            placedEntities[(fx, fy)] = proto;
            return true;
        }
        return false;
    }

    // #Misfits Add - OnSurface placement: spawn on same tile as parent entity
    /// <summary>
    /// Finds a tile containing the required parent entity and spawns <paramref name="proto"/>
    /// on top of it. Returns false if no parent found.
    /// </summary>
    private bool TryPlaceOnSurface(
        string proto,
        HashSet<(int, int)> occupiedTiles,
        Dictionary<(int, int), string> placedEntities,
        EntityUid gridUid, MapGridComponent grid)
    {
        string parentKw = GetOnSurfaceParentKeyword(proto);
        if (string.IsNullOrEmpty(parentKw)) return false;

        // Find tiles where parent entity is placed
        var parentTiles = new List<(int x, int y)>();
        foreach (var (pos, entProto) in placedEntities)
        {
            if (entProto.Contains(parentKw, StringComparison.OrdinalIgnoreCase))
                parentTiles.Add(pos);
        }

        if (parentTiles.Count == 0) return false;

        // Pick a random parent tile (allow stacking — OnSurface entities share the tile)
        var target = parentTiles[new Random().Next(parentTiles.Count)];
        SpawnAt(proto, gridUid, grid, target.x, target.y);
        return true;
    }

    // #Misfits Removed - PickFurniturePool replaced by profile.GetFurniturePool(poolKey) in DressRoom
    // private static string[] PickFurniturePool(UndergroundTheme theme, RoomType roomType) =>
    //     GetFurniturePool(roomType, theme);


    // ─────────────────────────────────────────────────────────────────────────
    // Mob Spawning
    // ─────────────────────────────────────────────────────────────────────────
    // #Misfits Change - Mob spawning now uses room-type-specific spawn counts
    // ─────────────────────────────────────────────────────────────────────────

    // #Misfits Change - Mob spawning now uses profile MobGroups + env state MobCountMult
    // #Misfits Add - depthFactor scales spawn chance: 40% near hub → 100% at center
    private void SpawnRoomMobs(RoomDef room, EntityUid gridUid, MapGridComponent grid,
                                ThemeProfile profile, EnvironmentalStateModifiers envMods,
                                int difficultyTier, Random rng, float depthFactor = 0.5f)
    {
        // #Misfits Fix - FactionHub rooms are player spawn points; never populate with hostile NPCs
        if (room.RoomType == RoomType.FactionHub) return;

        int spawnChance = room.RoomType switch
        {
            RoomType.Central         => 100,
            RoomType.VaultOverseer   => 80,
            RoomType.VaultArmory     => 85,
            RoomType.VaultVault      => 70,
            RoomType.SewerNest       => 95,
            RoomType.SewerPump       => 60,
            RoomType.MetroCommand    => 80,
            RoomType.MetroPlatform   => 70,
            RoomType.MetroDepot      => 75,
            _                        => 50,
        };

        // #Misfits Add - Depth-scaled spawn chance: 40% of base near hub, full at center
        // #Misfits Fix - Inline lerp: MathF.Lerp not available pre-.NET 9
        spawnChance = (int)(spawnChance * 0.4f + (spawnChance - spawnChance * 0.4f) * depthFactor);

        if (rng.Next(100) >= spawnChance) return;

        int mobCount = GetMobSpawnCount(room.RoomType, difficultyTier);
        // #Misfits Add - Environmental mob count modifier (Overgrown = fewer mobs, etc.)
        mobCount = Math.Max(1, (int)(mobCount * envMods.MobCountMult));

        // #Misfits Change - Mob groups from profile instead of static arrays
        var groups = profile.MobGroups;
        if (groups.Length == 0) return;
        var pool   = groups[rng.Next(groups.Length)];
        int total  = pool.Sum(m => m.Item2);

        int innerW = room.W - 2;
        int innerH = room.H - 2;
        if (innerW < 1 || innerH < 1) return;

        var taken = new HashSet<(int, int)>();

        for (int i = 0; i < mobCount; i++)
        {
            int roll  = rng.Next(total);
            int cumul = 0;
            string mob = pool[0].Item1;
            foreach (var (proto, weight) in pool)
            {
                cumul += weight;
                if (roll < cumul) { mob = proto; break; }
            }

            for (int attempt = 0; attempt < 8; attempt++)
            {
                int mx = room.X + 1 + rng.Next(innerW);
                int my = room.Y + 1 + rng.Next(innerH);
                if (taken.Contains((mx, my))) continue;
                SpawnAt(mob, gridUid, grid, mx, my);
                taken.Add((mx, my));
                break;
            }
        }
    }

    // #Misfits Removed - GetMobGroups replaced by ThemeProfile.MobGroups
    // private static (string, int)[][] GetMobGroups(UndergroundTheme theme) => ...;

    // =========================================================================
    // Tile helper
    // =========================================================================

    private void SetTile(EntityUid gridUid, MapGridComponent grid, int x, int y, string tileId)
    {
        var tileDef = _tileDefManager[tileId];
        _mapSystem.SetTile(gridUid, grid, new Vector2i(x, y), new Tile(tileDef.TileId));
    }

    // =========================================================================
    // Spawn helper
    // =========================================================================

    private void SpawnAt(string proto, EntityUid gridUid, MapGridComponent grid, int x, int y)
    {
        var coords = _mapSystem.GridTileToLocal(gridUid, grid, new Vector2i(x, y));
        Spawn(proto, coords);
    }

    // =========================================================================
    // Grid helpers
    // =========================================================================

    // #Misfits Fix - Renamed and expanded: theme walls now placed adjacent to corridors too,
    // preventing jarring rock walls at corridor edges
    private static bool IsAdjacentToTraversable(CellType[,] cellMap, int x, int y, int W, int H)
    {
        return IsTraversableAt(cellMap, x + 1, y, W, H) || IsTraversableAt(cellMap, x - 1, y, W, H) ||
               IsTraversableAt(cellMap, x, y + 1, W, H) || IsTraversableAt(cellMap, x, y - 1, W, H);
    }

    private static bool IsTraversableAt(CellType[,] cellMap, int x, int y, int W, int H)
    {
        if (x < 0 || x >= W || y < 0 || y >= H) return false;
        return cellMap[x, y] is CellType.Room or CellType.FactionHub or CellType.Corridor;
    }

    private static bool InBounds(int x, int y, int W, int H) =>
        x >= 0 && x < W && y >= 0 && y < H;

    // =========================================================================
    // #Misfits Add - LV Wire BFS Routing (Vault theme only)
    // =========================================================================

    /// <summary>
    /// Traces a BFS path through traversable cells from <paramref name="from"/> to
    /// <paramref name="to"/> and spawns a <c>CableApcExtension</c> entity at every cell
    /// along the route. This gives a visual LV wire trail from each wall light back to
    /// the reactor generator room.
    /// </summary>
    private void RouteWire(
        (int x, int y) from, (int x, int y) to,
        CellType[,] cellMap, EntityUid gridUid, MapGridComponent grid, int W, int H)
    {
        if (from == to)
        {
            SpawnAt("CableApcExtension", gridUid, grid, from.x, from.y);
            return;
        }

        var prev  = new Dictionary<(int, int), (int, int)>();
        var queue = new Queue<(int, int)>();
        queue.Enqueue(from);
        prev[from] = from;

        bool reached = false;
        while (queue.Count > 0 && !reached)
        {
            var (cx, cy) = queue.Dequeue();
            foreach (var (nx, ny) in new[] { (cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1) })
            {
                if (!InBounds(nx, ny, W, H) || prev.ContainsKey((nx, ny))) continue;
                var ct = cellMap[nx, ny];
                if (ct is not (CellType.Room or CellType.Corridor or CellType.FactionHub)) continue;
                prev[(nx, ny)] = (cx, cy);
                if (nx == to.x && ny == to.y) { reached = true; break; }
                queue.Enqueue((nx, ny));
            }
        }

        if (!reached) return;

        // Reconstruct BFS path and spawn wire at each cell (including start and end)
        var current = to;
        while (current != from)
        {
            SpawnAt("CableApcExtension", gridUid, grid, current.x, current.y);
            current = prev[current];
        }
        SpawnAt("CableApcExtension", gridUid, grid, from.x, from.y);
    }

    // =========================================================================
    // #Misfits Add - Room Type Variety Helpers
    // =========================================================================

    // #Misfits Removed - GetThematicRoomType replaced by PickRoomTypeFromProfile
    /*
    private static RoomType GetThematicRoomType(UndergroundTheme theme, Random rng)
    {
        int roll = rng.Next(100);
        return theme switch
        {
            UndergroundTheme.Vault => roll switch
            {
                < 18 => RoomType.VaultBarracks,
                < 28 => RoomType.VaultKitchen,
                < 35 => RoomType.VaultHydroponics,
                < 40 => RoomType.VaultRecreation,
                < 55 => RoomType.VaultLab,
                < 68 => RoomType.VaultArmory,
                < 78 => RoomType.VaultVault,
                < 88 => RoomType.VaultOverseer,
                _    => RoomType.VaultReactor,
            },
            UndergroundTheme.Sewer => roll switch
            {
                < 35 => RoomType.SewerTunnel,
                < 50 => RoomType.SewerJunction,
                < 62 => RoomType.SewerGrotto,
                < 72 => RoomType.SewerPump,
                < 82 => RoomType.SewerNest,
                _    => RoomType.SewerCamp,
            },
            UndergroundTheme.Metro => roll switch
            {
                < 30 => RoomType.MetroPlatform,
                < 50 => RoomType.MetroTunnel,
                < 70 => RoomType.MetroMaintenance,
                < 85 => RoomType.MetroDepot,
                _    => RoomType.MetroCommand,
            },
            _ => RoomType.VaultBarracks,
        };
    }
    */

    // #Misfits Removed - GetFurniturePool replaced by ThemeProfile.FurniturePools
    /*
    private static string[] GetFurniturePool(RoomType roomType, UndergroundTheme theme) =>
        (theme, roomType) switch
        {
            (UndergroundTheme.Vault, RoomType.VaultBarracks) => FurnVaultBarracks,
            // ... all variants ...
            _ => FurnVaultStandard,
        };
    */

    // #Misfits Removed - GetDecalPool, GetHazardPool, GetLightCount replaced by ThemeProfile fields
    // private static string[] GetDecalPool(UndergroundTheme theme) => ...;
    // private static string[] GetHazardPool(UndergroundTheme theme) => ...;
    // private static int GetLightCount(RoomType roomType) => ...; // now ThemeProfile.GetLightCount()

    /// <summary>
    /// Returns the number of mobs to spawn in this room, scaled by difficulty tier (1-3).
    /// </summary>
    private static int GetMobSpawnCount(RoomType roomType, int difficultyTier) =>
        roomType switch
        {
            // #Misfits Removed - FactionHub case removed; hubs never receive mob spawns (player entry rooms)
            //RoomType.FactionHub      => 2 + difficultyTier,
            RoomType.Central          => 2 + difficultyTier,
            RoomType.VaultOverseer    => 2,
            RoomType.VaultVault       => 1 + (difficultyTier / 2),
            RoomType.VaultKitchen     => 1,
            RoomType.VaultHydroponics => 1,                        // #Misfits Add - occasional wandering ghoul
            RoomType.VaultRecreation  => 1,                        // #Misfits Add
            RoomType.VaultArmory      => 3 + (difficultyTier / 2),
            RoomType.VaultReactor     => 1,
            RoomType.SewerNest        => 4 + difficultyTier,
            RoomType.SewerCamp        => 2 + difficultyTier,       // #Misfits Add - defended survivor camp
            RoomType.SewerPump        => 2,
            RoomType.MetroCommand     => 2 + (difficultyTier / 2),
            RoomType.MetroPlatform    => 2 + (difficultyTier / 2),
            _                         => 1,
        };

    /// <summary>
    /// Spawns a light entity at the given grid coordinate.
    /// Light brightness and color match the theme for visual coherence.
    /// </summary>
    // #Misfits Removed - Light consts now live in ThemeProfile.LightConfig (UndergroundThemeProfiles.cs)
    // private const string LightVault  = "AlwaysPoweredWallLight";
    // private const string LightSewer  = "N14TorchWall";
    // private const string LightMetro  = "LightPostSmall";

    // #Misfits Change - SpawnLight now driven by LightConfig from profile
    private void SpawnLight(EntityUid gridUid, MapGridComponent grid, int x, int y,
        LightConfig lightCfg, Direction facing)
    {
        var coords = _mapSystem.GridTileToLocal(gridUid, grid, new Vector2i(x, y));
        var ent = Spawn(lightCfg.LightEntity, coords);
        // Wall-mounted lights need rotation to face away from the wall
        if (lightCfg.Style == LightStyle.WallMounted)
        {
            var xform = Transform(ent);
            xform.LocalRotation = facing.ToAngle();
        }
    }
}

