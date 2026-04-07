using System.Collections.Generic;

// #Misfits Add - Shared data types for procedural underground expedition system

namespace Content.Shared._Misfits.Expeditions;

// ─────────────────────────────────────────────────────────────────────────────
// #Misfits Add - Structured generation profile enums and data types
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Overall layout strategy for the procedural map. Influences corridor shape,
/// room density, and connectivity patterns.
/// </summary>
public enum LayoutStyle
{
    /// <summary>Rectangular grid of rooms connected by right-angle corridors (Vault).</summary>
    GridBased,
    /// <summary>Organic branching passages with irregular chambers (Sewer).</summary>
    BranchingTunnels,
    /// <summary>Predominantly linear corridors with platform rooms (Metro).</summary>
    LinearCorridors,
}

/// <summary>
/// Environmental condition applied on top of a theme. Each state modifies density,
/// hazard chance, and visual parameters via <see cref="EnvironmentalStateModifiers"/>.
/// Multiple states may stack on a single generation (e.g. Abandoned + Damaged).
/// </summary>
public enum EnvironmentalState
{
    /// <summary>Relatively intact — minimal debris, all lights functional.</summary>
    Pristine,
    /// <summary>Long abandoned — heavy decals, extra junk, reduced lighting.</summary>
    Abandoned,
    /// <summary>Water intrusion — water channels may appear in non-Sewer themes.</summary>
    Flooded,
    /// <summary>Structural collapse — rubble tiles replace floor, wall gaps, more hazards.</summary>
    Damaged,
    /// <summary>Nature reclaiming — organic decals, reduced mob count.</summary>
    Overgrown,
}

/// <summary>
/// Numeric multipliers / overrides that an <see cref="EnvironmentalState"/> applies
/// to the base generation profile. Multipliers compound when multiple states stack.
/// </summary>
// #Misfits Add - Environmental state modifiers for density/hazard/visual tuning
public sealed class EnvironmentalStateModifiers
{
    /// <summary>Multiplier on room decal count (e.g. 1.5 = 50% more decals).</summary>
    public float DecalDensityMult { get; init; } = 1f;

    /// <summary>Multiplier on junk pile count per room.</summary>
    public float JunkDensityMult { get; init; } = 1f;

    /// <summary>Multiplier on hazard spawn chance.</summary>
    public float HazardChanceMult { get; init; } = 1f;

    /// <summary>Fraction of lights to skip (0 = no reduction, 0.5 = half removed).</summary>
    public float LightReductionFraction { get; init; } = 0f;

    /// <summary>If > 0, water channels may appear even in non-Sewer themes at this chance per attempt.</summary>
    public float WaterChannelChanceOverride { get; init; } = 0f;

    /// <summary>Fraction of floor tiles replaced with rubble/broken variant.</summary>
    public float RubbleTileReplaceFraction { get; init; } = 0f;

    /// <summary>Multiplier on mob spawn count per room.</summary>
    public float MobCountMult { get; init; } = 1f;

    /// <summary>When true, nature/organic decals are injected into the decal pool.</summary>
    public bool NatureDecals { get; init; } = false;
}

/// <summary>
/// Corridor generation parameters for a theme profile.
/// </summary>
// #Misfits Add - Per-theme corridor shape configuration
public sealed class CorridorStyle
{
    /// <summary>Corridor width in tiles (2 = standard, 3 = sewer channels).</summary>
    public int Width { get; init; } = 2;

    /// <summary>Extra corridors beyond MST as fraction of room count (0.3 = ~30%).</summary>
    public float BranchingFactor { get; init; } = 0.2f;

    /// <summary>Probability [0-1] of adding loop corridors between already-connected rooms.</summary>
    public float LoopProbability { get; init; } = 0.15f;
}

/// <summary>
/// The full tile/wall/door palette for a theme. Replaces scattered const lookups.
/// </summary>
// #Misfits Add - Tile palette data bag for theme profiles
public sealed class TilePalette
{
    /// <summary>Floor tile IDs chosen randomly for room interiors.</summary>
    public string[] RoomFloorTiles { get; init; } = System.Array.Empty<string>();

    /// <summary>Floor tile IDs for corridors (may overlap room tiles).</summary>
    public string[] CorridorFloorTiles { get; init; } = System.Array.Empty<string>();

    /// <summary>Floor tile for faction hub interiors.</summary>
    public string[] HubFloorTiles { get; init; } = System.Array.Empty<string>();

    /// <summary>Wall entity prototype for room perimeters.</summary>
    public string RoomWallEntity { get; init; } = "";

    /// <summary>Wall entity prototype for hub perimeters (visually distinct).</summary>
    public string HubWallEntity { get; init; } = "";

    /// <summary>Background tile for empty/wall cells (atmos sealing).</summary>
    public string BackgroundTile { get; init; } = "FloorAsteroidSand";

    /// <summary>Standard room door entity.</summary>
    public string RoomDoorEntity { get; init; } = "";

    /// <summary>Hub door entity (often heavier/reinforced).</summary>
    public string HubDoorEntity { get; init; } = "";
}

/// <summary>
/// Light spawning configuration per theme.
/// </summary>
// #Misfits Add - Light placement style for theme profiles
public enum LightStyle
{
    /// <summary>Light mounted on wall tile facing inward (Vault, Sewer).</summary>
    WallMounted,
    /// <summary>Light placed on ground at interior tiles (Metro).</summary>
    GroundPost,
}

/// <summary>
/// Lighting configuration for a theme profile.
/// </summary>
// #Misfits Add - Per-theme light entity and style
public sealed class LightConfig
{
    /// <summary>Light prototype to spawn.</summary>
    public string LightEntity { get; init; } = "AlwaysPoweredWallLight";

    /// <summary>Placement style: wall-mounted (rotated) or ground post.</summary>
    public LightStyle Style { get; init; } = LightStyle.WallMounted;

    /// <summary>Default light count for room types not in the per-type map.</summary>
    public int DefaultCount { get; init; } = 1;

    /// <summary>Per-RoomType light count overrides.</summary>
    public Dictionary<RoomType, int> CountPerRoomType { get; init; } = new();
}

/// <summary>
/// Defines a single room type's generation properties within a theme profile:
/// selection weight, max count, preferred dimensions, and adjacency rules.
/// </summary>
// #Misfits Add - Room type definition with adjacency preferences for structured generation
public sealed class RoomTypeDefinition
{
    /// <summary>The room type this definition describes.</summary>
    public RoomType RoomType { get; init; }

    /// <summary>Relative weight for random selection (higher = more likely).</summary>
    public int Weight { get; init; } = 10;

    /// <summary>Maximum instances allowed per map.</summary>
    public int MaxCount { get; init; } = 3;

    /// <summary>Minimum room width (tiles).</summary>
    public int MinW { get; init; } = 6;
    /// <summary>Maximum room width (tiles).</summary>
    public int MaxW { get; init; } = 15;
    /// <summary>Minimum room height (tiles).</summary>
    public int MinH { get; init; } = 6;
    /// <summary>Maximum room height (tiles).</summary>
    public int MaxH { get; init; } = 15;

    /// <summary>Room types this type prefers to be near (score bonus).</summary>
    public List<RoomType> AdjacencyPreferences { get; init; } = new();

    /// <summary>Room types this type should NOT be adjacent to (score penalty).</summary>
    public List<RoomType> AdjacencyExclusions { get; init; } = new();

    /// <summary>Entities that MUST be spawned inside this room type.</summary>
    public List<string> RequiredFeatures { get; init; } = new();

    /// <summary>Reference key into ThemeProfile.FurniturePools.</summary>
    public string FurniturePoolKey { get; init; } = "standard";
}

/// <summary>
/// Complete generation profile for a theme. Encapsulates ALL theme-specific data:
/// layout strategy, room definitions, palettes, entity pools, environmental constraints,
/// and corridor shape. Consumed by the generator pipeline to produce a consistent,
/// rule-driven map instead of scattered random selection.
/// </summary>
// #Misfits Add - Structured theme profile replacing scattered switch-per-theme logic
public sealed class ThemeProfile
{
    /// <summary>Theme enum value this profile represents.</summary>
    public UndergroundTheme Theme { get; init; }

    /// <summary>Human-readable name (e.g. "Abandoned Vault").</summary>
    public string ThemeName { get; init; } = "";

    /// <summary>Brief description for logging and debugging.</summary>
    public string Description { get; init; } = "";

    /// <summary>Overall layout strategy.</summary>
    public LayoutStyle LayoutStyle { get; init; } = LayoutStyle.GridBased;

    /// <summary>All room type definitions (weights, caps, dimensions, adjacency).</summary>
    public List<RoomTypeDefinition> RoomDefinitions { get; init; } = new();

    /// <summary>Room types that MUST appear exactly once (placed in Pass 1).</summary>
    public List<RoomType> MandatoryAnchors { get; init; } = new();

    /// <summary>Environmental states valid for this theme.</summary>
    public List<EnvironmentalState> ValidEnvironmentalStates { get; init; } = new();

    /// <summary>Corridor shape parameters.</summary>
    public CorridorStyle CorridorStyle { get; init; } = new();

    /// <summary>Tile/wall/door palette.</summary>
    public TilePalette TilePalette { get; init; } = new();

    /// <summary>Light spawning configuration.</summary>
    public LightConfig LightConfig { get; init; } = new();

    /// <summary>Furniture entity pools keyed by pool name (matched via RoomTypeDefinition.FurniturePoolKey).</summary>
    public Dictionary<string, string[]> FurniturePools { get; init; } = new();

    /// <summary>Mob faction sub-groups: each room randomly picks one group, then weighted-random within it.</summary>
    public (string, int)[][] MobGroups { get; init; } = System.Array.Empty<(string, int)[]>();

    /// <summary>Decal pool for thematic visual overlays.</summary>
    public string[] DecalPool { get; init; } = System.Array.Empty<string>();

    /// <summary>Hazard entity pool.</summary>
    public string[] HazardPool { get; init; } = System.Array.Empty<string>();

    /// <summary>Junk pile scatter pool.</summary>
    public string[] JunkPool { get; init; } = System.Array.Empty<string>();

    /// <summary>Floor debris entity pool.</summary>
    public string[] FloorScatterPool { get; init; } = System.Array.Empty<string>();

    /// <summary>Blueprint reward pool.</summary>
    public string[] BlueprintPool { get; init; } = System.Array.Empty<string>();

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Looks up the RoomTypeDefinition for the given room type. Returns null
    /// if the type is not defined in this profile.
    /// </summary>
    public RoomTypeDefinition? GetRoomDef(RoomType rt)
    {
        foreach (var def in RoomDefinitions)
            if (def.RoomType == rt) return def;
        return null;
    }

    /// <summary>
    /// Returns the furniture pool for the given pool key, or an empty array.
    /// </summary>
    public string[] GetFurniturePool(string key)
    {
        return FurniturePools.TryGetValue(key, out var pool) ? pool : System.Array.Empty<string>();
    }

    /// <summary>
    /// Returns the light count for the given room type, falling back to default.
    /// </summary>
    public int GetLightCount(RoomType rt)
    {
        return LightConfig.CountPerRoomType.TryGetValue(rt, out var count) ? count : LightConfig.DefaultCount;
    }
}

/// <summary>
/// The three visual/structural themes for procedural underground expedition maps.
/// </summary>
public enum UndergroundTheme
{
    /// <summary>Pre-war concrete bunker / Vault-Tec facility.</summary>
    Vault,
    /// <summary>Crumbling brick sewer tunnels with dirty water channels.</summary>
    Sewer,
    /// <summary>Abandoned subway / metro system with metal infrastructure.</summary>
    Metro,
}

/// <summary>
/// Cell types for the procedural 2D grid.
/// <list type="bullet">
/// <item>Empty = void space — no tile, no entity</item>
/// <item>Corridor = carved passage between rooms</item>
/// <item>Room = standard interior room</item>
/// <item>FactionHub = large faction staging hub (one per faction group)</item>
/// <item>WaterChannel = sewer-theme open water trench</item>
/// <item>Platform = grate-floor elevated walkway</item>
/// </list>
/// </summary>
public enum CellType : int
{
    Empty        = 0,
    Corridor     = 1,
    Room         = 2,
    FactionHub   = 3,
    WaterChannel = 4,
    Platform     = 5,
}

/// <summary>
/// Parameters bag carried from <see cref="N14ExpeditionMapEntry"/> into the
/// generator. Constructed in code; not deserialized from YAML.
/// </summary>
public sealed class UndergroundGenParams
{
    /// <summary>RNG seed for deterministic generation.</summary>
    public int Seed { get; set; }

    /// <summary>Visual / structural theme.</summary>
    public UndergroundTheme Theme { get; set; }

    /// <summary>Grid width in tiles (square).</summary>
    public int GridWidth { get; set; } = 80;

    /// <summary>Grid height in tiles (square).</summary>
    public int GridHeight { get; set; } = 80;

    /// <summary>0 = Easy, 1 = Medium, 2 = Hard.</summary>
    public int DifficultyTier { get; set; }

    /// <summary>Minimum number of standard rooms to guarantee.</summary>
    public int MinRooms { get; set; } = 6;

    /// <summary>Maximum number of standard rooms to attempt.</summary>
    public int MaxRooms { get; set; } = 12;

    /// <summary>Number of faction staging hubs (1–4, clamped to 4 corners).</summary>
    public int HubCount { get; set; } = 2;

    /// <summary>
    /// Faction spawn groups from the difficulty prototype.
    /// One hub is allocated per group up to HubCount.
    /// </summary>
    public List<N14FactionSpawnGroup> FactionSpawnGroups { get; set; } = new();

    // #Misfits Add - External environmental state override; if non-empty, GenerateMap uses these instead of auto-picking
    /// <summary>Environmental states to apply. If empty, GenerateMap auto-picks from the profile.</summary>
    public List<EnvironmentalState> EnvironmentalStates { get; set; } = new();
}

/// <summary>
/// Defines a rectangular room on the procedural grid.
/// </summary>
public sealed class RoomDef
{
    /// <summary>Left edge (grid X index, inclusive).</summary>
    public int X { get; set; }

    /// <summary>Bottom edge (grid Y index, inclusive).</summary>
    public int Y { get; set; }

    /// <summary>Width in tiles.</summary>
    public int W { get; set; }

    /// <summary>Height in tiles.</summary>
    public int H { get; set; }

    /// <summary>What kind of room this is. #Misfits Change - Standard removed, default to Central</summary>
    public RoomType RoomType { get; set; } = RoomType.Central;

    /// <summary>
    /// Index of the faction spawn group that "owns" this hub (-1 = no faction).
    /// </summary>
    public int FactionIndex { get; set; } = -1;

    /// <summary>Grid-space center of the room.</summary>
    public (int cx, int cy) Center => (X + W / 2, Y + H / 2);

    /// <summary>
    /// Returns true if this room overlaps <paramref name="other"/> when both
    /// are expanded outward by <paramref name="margin"/> tiles on each side.
    /// margin=2 enforces a minimum 2-tile wall gap between room interiors.
    /// </summary>
    public bool Overlaps(RoomDef other, int margin = 2)
    {
        return !(X >= other.X + other.W + margin ||
                 X + W + margin <= other.X         ||
                 Y >= other.Y + other.H + margin ||
                 Y + H + margin <= other.Y);
    }
}

/// <summary>
/// Classifies what a room is used for during generation and dressing.
/// #Misfits Change - Expanded to 16 thematic variants per Underground Expedition design spec
/// </summary>
public enum RoomType
{
    /// <summary>Large faction staging hub placed at a map corner.</summary>
    FactionHub,

    /// <summary>Central congregation / objective room near the map centre.</summary>
    Central,

    // Vault variants
    /// <summary>Vault dweller housing and barracks.</summary>
    VaultBarracks,

    /// <summary>Kitchen and cafeteria area.</summary>
    // #Misfits Add - VaultKitchen room type for lived-in food/cooking areas
    VaultKitchen,

    /// <summary>Hydroponic farming bays with planters and water storage.</summary>
    // #Misfits Add - VaultHydroponics from Vault.yml analysis
    VaultHydroponics,

    /// <summary>Recreation room: pool tables, fitness equipment, instruments.</summary>
    // #Misfits Add - VaultRecreation from Vault.yml analysis
    VaultRecreation,

    /// <summary>Research and medical laboratory.</summary>
    VaultLab,

    /// <summary>Weapons cache and security armory.</summary>
    VaultArmory,

    /// <summary>Main vault chamber (treasure room, climax).</summary>
    VaultVault,

    /// <summary>Overseer command center.</summary>
    VaultOverseer,

    /// <summary>Reactor room (hazardous, high radiation).</summary>
    VaultReactor,

    // Sewer variants
    /// <summary>Standard tunnel passage.</summary>
    SewerTunnel,

    /// <summary>Pump station and water treatment utility.</summary>
    SewerPump,

    /// <summary>Creature lair or nest (highly hazardous).</summary>
    SewerNest,

    /// <summary>Improvised underground survivor camp with beds, fire, and basic supplies.</summary>
    // #Misfits Add - SewerCamp from MercerIslandSewers.yml analysis
    SewerCamp,

    /// <summary>Natural cavern chamber.</summary>
    SewerGrotto,

    /// <summary>Pipe junction and intersection chamber.</summary>
    SewerJunction,

    // Metro variants
    /// <summary>Passenger station platform.</summary>
    MetroPlatform,

    /// <summary>Maintenance and utility room.</summary>
    MetroMaintenance,

    /// <summary>Control and dispatch center.</summary>
    MetroCommand,

    /// <summary>Cargo and vehicle depot.</summary>
    MetroDepot,

    /// <summary>Transit tunnel passage.</summary>
    MetroTunnel,
}

// ─────────────────────────────────────────────────────────────────────────────
// #Misfits Add - BSP zone division types for spatially-aware room placement
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Functional role assigned to a map zone based on its distance from faction hub corners.
/// Used by PlaceStandardRooms to restrict which room types can appear in which zones.
/// </summary>
public enum ZoneRole
{
    /// <summary>Near hub entry point — safe, familiar rooms (barracks, junctions).</summary>
    Entry,
    /// <summary>Medium distance — transitional rooms (kitchens, tunnels, grotto).</summary>
    Transit,
    /// <summary>Functional rooms away from hubs (labs, pumps, maintenance).</summary>
    Utility,
    /// <summary>High-value rooms in protected interior (overseer, vault, depot).</summary>
    Secure,
    /// <summary>Map center — most dangerous rooms (reactor, nest, command).</summary>
    Hazard,
}

/// <summary>
/// A rectangular zone within the map grid assigned a <see cref="ZoneRole"/>.
/// The map is divided into a 3×3 grid of zones with roles assigned by
/// Manhattan distance from hub corners.
/// </summary>
public struct MapZone
{
    /// <summary>Left edge X (inclusive).</summary>
    public int X;
    /// <summary>Bottom edge Y (inclusive).</summary>
    public int Y;
    /// <summary>Width in tiles.</summary>
    public int W;
    /// <summary>Height in tiles.</summary>
    public int H;
    /// <summary>Functional role — controls which room types belong here.</summary>
    public ZoneRole Role;
    /// <summary>Manhattan distance (in zone-grid steps) from the nearest hub corner zone.</summary>
    public int DepthFromHub;
}

// ─────────────────────────────────────────────────────────────────────────────
// #Misfits Add - System 4 placement rule enum for entity placement constraints
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Determines how an entity is spatially placed within a room during dressing.
/// </summary>
public enum PlacementRule
{
    /// <summary>Spawns ON a wall tile (Empty cell adjacent to Room). Signs, posters.</summary>
    WallAttached,
    /// <summary>Spawns on a floor tile adjacent to a wall. Closets, shelves, filing cabinets.</summary>
    WallAdjacent,
    /// <summary>Spawns on same tile as a previously placed parent entity. Terminal on desk, weapon on shelf.</summary>
    OnSurface,
    /// <summary>Default — any open floor tile, edge-biased.</summary>
    FreeStanding,
    /// <summary>Consecutive floor tiles along the longest wall. Kitchen tables.</summary>
    WallRow,
}
