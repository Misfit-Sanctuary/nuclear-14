using System.Collections.Generic;

// #Misfits Add - Static registry of structured theme profiles for procedural dungeon generation.
// Each profile encapsulates ALL theme-specific data: room definitions, palettes, pools, adjacency
// rules, and environmental constraints. Replaces scattered switch expressions in the generator.

namespace Content.Shared._Misfits.Expeditions;

/// <summary>
/// Static factory that builds <see cref="ThemeProfile"/> instances for each
/// <see cref="UndergroundTheme"/>. Call <see cref="GetProfile"/> at the start
/// of generation to obtain the complete rule set for the selected theme.
/// </summary>
public static class UndergroundThemeProfiles
{
    /// <summary>
    /// Returns the structured generation profile for the given theme.
    /// </summary>
    public static ThemeProfile GetProfile(UndergroundTheme theme) => theme switch
    {
        UndergroundTheme.Vault => BuildVaultProfile(),
        UndergroundTheme.Sewer => BuildSewerProfile(),
        UndergroundTheme.Metro => BuildMetroProfile(),
        _ => BuildVaultProfile(),
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Vault Profile
    // ─────────────────────────────────────────────────────────────────────────

    private static ThemeProfile BuildVaultProfile() => new()
    {
        Theme       = UndergroundTheme.Vault,
        ThemeName   = "Abandoned Vault",
        Description = "Pre-war Vault-Tec underground bunker. Grid-based layout with concrete corridors, " +
                      "reactor rooms, armories, and sealed command centers.",
        LayoutStyle = LayoutStyle.GridBased,

        MandatoryAnchors = new List<RoomType>
        {
            RoomType.VaultReactor,
            RoomType.VaultOverseer,
            RoomType.VaultArmory,
            RoomType.VaultKitchen,
        },

        RoomDefinitions = new List<RoomTypeDefinition>
        {
            // Barracks — common, medium-sized sleeping quarters
            new()
            {
                RoomType = RoomType.VaultBarracks, Weight = 18, MaxCount = 3,
                MinW = 9, MaxW = 15, MinH = 9, MaxH = 15,
                FurniturePoolKey = "barracks",
                RequiredFeatures = { "N14BedBunk" },
                AdjacencyPreferences = { RoomType.VaultKitchen, RoomType.VaultRecreation },
            },
            // Kitchen — mid-weight, near barracks
            new()
            {
                RoomType = RoomType.VaultKitchen, Weight = 10, MaxCount = 2,
                MinW = 9, MaxW = 15, MinH = 9, MaxH = 15,
                FurniturePoolKey = "kitchen",
                RequiredFeatures = { "N14CookingStoveWide" },
                AdjacencyPreferences = { RoomType.VaultBarracks, RoomType.VaultHydroponics },
            },
            // Hydroponics — rare, near kitchen
            new()
            {
                RoomType = RoomType.VaultHydroponics, Weight = 7, MaxCount = 2,
                MinW = 9, MaxW = 15, MinH = 9, MaxH = 15,
                FurniturePoolKey = "hydroponics",
                RequiredFeatures = { "N14HydroponicsPlanter" },
                AdjacencyPreferences = { RoomType.VaultKitchen },
                AdjacencyExclusions = { RoomType.VaultArmory },
            },
            // Recreation — rare, leisure area near barracks
            new()
            {
                RoomType = RoomType.VaultRecreation, Weight = 5, MaxCount = 2,
                MinW = 12, MaxW = 18, MinH = 10, MaxH = 16,
                FurniturePoolKey = "recreation",
                RequiredFeatures = { "N14TableCasinoPool", "N14JunkJukebox" },
                AdjacencyPreferences = { RoomType.VaultBarracks },
            },
            // Lab — common, research/medical
            new()
            {
                RoomType = RoomType.VaultLab, Weight = 15, MaxCount = 3,
                MinW = 9, MaxW = 15, MinH = 9, MaxH = 15,
                FurniturePoolKey = "lab",
                RequiredFeatures = { "N14WorkbenchChemistryset" },
                AdjacencyPreferences = { RoomType.VaultReactor },
                AdjacencyExclusions = { RoomType.VaultKitchen },
            },
            // Armory — high-value, contested
            new()
            {
                RoomType = RoomType.VaultArmory, Weight = 13, MaxCount = 2,
                MinW = 9, MaxW = 15, MinH = 9, MaxH = 15,
                FurniturePoolKey = "armory",
                RequiredFeatures = { "N14ClosetGunCabinet" },
                AdjacencyPreferences = { RoomType.VaultOverseer },
            },
            // Vault treasure room — guarded
            new()
            {
                RoomType = RoomType.VaultVault, Weight = 10, MaxCount = 2,
                MinW = 9, MaxW = 15, MinH = 9, MaxH = 15,
                FurniturePoolKey = "vault",
                RequiredFeatures = { "N14LootClosetSafe" },
                AdjacencyPreferences = { RoomType.VaultOverseer, RoomType.VaultArmory },
                AdjacencyExclusions = { RoomType.VaultBarracks },
            },
            // Overseer command — singular authority room
            new()
            {
                RoomType = RoomType.VaultOverseer, Weight = 10, MaxCount = 1,
                MinW = 12, MaxW = 18, MinH = 10, MaxH = 16,
                FurniturePoolKey = "overseer",
                RequiredFeatures = { "N14TableDeskWood" },
                AdjacencyPreferences = { RoomType.VaultArmory, RoomType.VaultVault },
            },
            // Reactor — power core, exactly once
            new()
            {
                RoomType = RoomType.VaultReactor, Weight = 12, MaxCount = 1,
                MinW = 12, MaxW = 18, MinH = 10, MaxH = 16,
                FurniturePoolKey = "reactor",
                RequiredFeatures = { "N14GeneratorVaultTecReactor" },
                AdjacencyPreferences = { RoomType.VaultLab },
                AdjacencyExclusions = { RoomType.VaultBarracks, RoomType.VaultKitchen },
            },
        },

        ValidEnvironmentalStates = new List<EnvironmentalState>
        {
            EnvironmentalState.Pristine,
            EnvironmentalState.Abandoned,
            EnvironmentalState.Damaged,
        },

        CorridorStyle = new CorridorStyle
        {
            Width = 2,
            BranchingFactor = 0.2f,
            LoopProbability = 0.15f,
        },

        TilePalette = new TilePalette
        {
            RoomFloorTiles     = new[] { "FloorMetalTunnel", "FloorMetalTunnelRusty", "FloorMetalTunnelWasteland", "N14FloorConcrete", "FloorSteelDirty", "N14FloorConcreteDark", "FloorMS13ConcreteIndustrial" },
            CorridorFloorTiles = new[] { "FloorMetalTunnel", "FloorMetalTunnelRusty", "FloorMetalTunnelWasteland", "N14FloorConcrete", "FloorSteelDirty", "N14FloorConcreteDark", "FloorMS13ConcreteIndustrial" },
            HubFloorTiles      = new[] { "FloorMetalTunnel", "N14FloorConcrete", "N14FloorConcreteDark" },
            RoomWallEntity     = "N14WallConcreteSlantedIndestructible",
            HubWallEntity      = "N14WallBunkerSlantedIndestructible",
            BackgroundTile     = "FloorAsteroidSand",
            RoomDoorEntity     = "N14DoorBunker",
            HubDoorEntity      = "N14DoorMetalReinforced",
        },

        LightConfig = new LightConfig
        {
            LightEntity  = "AlwaysPoweredWallLight",
            Style        = LightStyle.WallMounted,
            DefaultCount = 1,
            CountPerRoomType = new Dictionary<RoomType, int>
            {
                { RoomType.FactionHub,       3 },
                { RoomType.VaultOverseer,    3 },
                { RoomType.VaultVault,       3 },
                { RoomType.VaultRecreation,  3 },
                { RoomType.Central,          2 },
                { RoomType.VaultBarracks,    2 },
                { RoomType.VaultKitchen,     2 },
                { RoomType.VaultHydroponics, 2 },
                { RoomType.VaultLab,         2 },
                { RoomType.VaultArmory,      2 },
            },
        },

        // ── Furniture pools ───────────────────────────────────────────────────
        // Tier 1 anchors are in RequiredFeatures above (guaranteed single spawn).
        // Each pool here = Tier 2 (supporting) + Tier 3 (ambient) items.
        // Pool is sampled uniformly; Tier 3 items are ~25–33% of each array to stay
        // under the "max 30% ambient" design guideline.
        FurniturePools = new Dictionary<string, string[]>
        {
            ["standard"] = new[]
            {
                // Generic fallback for unclassed rooms
                "N14ClosetGrey1", "N14ClosetGrey2", "N14ClosetRusty",
                "N14TableDeskMetal", "N14ShelfMetal", "N14ComputerTerminalRusted",
                "N14JunkPile1", "N14JunkPile3", "N14JunkPile5",
                "N14DecorFloorPaper", "N14DecorFloorGlass1",
            },
            ["hub"] = new[]
            {
                "N14BarricadeMetal", "N14BarricadeMetalGreen", "N14BarricadeSandbagSingle",
                "N14BarricadeTanktrapRusty", "N14LootCrateMilitary", "N14ShelfMetal",
                "N14LootCrateVaultStandard", "N14WorkbenchWeaponbench", "N14WorkbenchAmmobench",
                "N14VendingMachineNukaCola", "N14JunkPile2", "N14JunkPile7",
            },

            // ── Vault Barracks ─────────────────────────────────────────────────
            // Tier 2: footlockers, lockers, dresser, nightstand, lamp, poster, personal items
            // Tier 3: junk piles, broken glass, cardboard
            ["barracks"] = new[]
            {
                // Tier 2 — supporting (functional + personal detritus)
                "N14LootCrateFootlocker",    // at foot of every bunk; mandatory personal storage
                "N14ClosetGrey1",            // standing wall locker
                "N14ClosetGrey2",            // second locker variant
                "N14TableDeskMetalSmall",    // repurposed as nightstand
                "N14Dresser",                // clothing storage
                "N14LightSmall",             // wall-mounted lamp
                "N14PosterVaultTec01",       // Vault-Tec propaganda, given not chosen
                "N14PosterVaultTec03",       // alternate vault poster
                "N14DrinkNukaColaBottleFull", // bottle on the nightstand, never collected
                "N14Magazine",               // left open face-down on a bunk
                "N14JunkVaultCanteen",       // personal kit item on the nightstand
                // Tier 3 — ambient fill
                "N14JunkPile3",
                "N14DecorFloorGlass1",
                "N14DecorFloorCardboard",
            },

            // ── Vault Kitchen ──────────────────────────────────────────────────
            // Tier 2: counter, sink, fridge, shelving, cleaning tools, canned goods
            // Tier 3: food debris, broken glass, trashbin
            ["kitchen"] = new[]
            {
                // Tier 2
                "N14TableCounterMetal",      // prep counter beside the stove
                "N14Sink",                   // dish station
                "N14LootClosetFridge",       // refrigerator with contents
                "N14ShelfMetal",             // shelving for tins and dishes
                "N14ShelfMetalMeds",         // repurposed as condiment/spice shelf
                "N14Mop",                    // leaning against a wall
                "N14MopBucket",              // full of brown water
                "N14FoodCram",               // canned goods on counter
                "N14FoodPorkBeans",          // second tin variety
                "N14FoodInstamash",          // third tin variety
                "N14JunkCoffeepot",          // cook's personal coffeepot, ring-stained
                "N14FoodCram",               // extra tin stacked near the sink
                // Tier 3
                "N14DecorFloorFood2",
                "N14Trashbin",
                "N14DecorFloorGlass2",
            },

            // ── Vault Hydroponics ──────────────────────────────────────────────
            // Tier 2: empty planter, soil, fertilizer, tools, grow lights, pipes
            // Tier 3: broken pot, organic debris
            ["hydroponics"] = new[]
            {
                // Tier 2
                "N14HydroponicsPlanterEmpty", // a second planter, soil turned but unreplanted
                "N14Soil",                    // loose soil spilled on the floor
                "N14SackFertilizerFull",      // fertilizer bags stacked by the planters
                "N14HydroponicsToolShovel",   // trowel leaning against a planter
                "N14HydroponicsToolHoe",      // hoe that turned the soil, still dirty
                "N14HydroponicsToolClippers", // for trimming; every garden needs them
                "N14LightSmall",              // grow lighting overhead
                "N14GasPipeStraight",         // irrigation lines between beds
                "N14JunkVaultCanteen",        // worker's canteen left behind
                // Tier 3
                "N14PlantpotBroken1",
                "N14JunkPile4",
                "N14DecorFloorFood3",
            },

            // ── Vault Recreation ───────────────────────────────────────────────
            // Tier 2: TV, vending, magazine rack, coffee table, arcade, reading material
            // Tier 3: broken glass, junk pile, cardboard
            ["recreation"] = new[]
            {
                // Tier 2
                "N14TelevisionTube",          // vault-issue TV, screen dead
                "N14VendingMachineNukaCola",  // refreshment for off-duty hours
                "N14VendingMachineCigarette", // stress relief option
                "N14MagazineRack",            // reading material stand
                "N14TableWoodLow",            // coffee table
                "N14JunkArcade",              // arcade cabinet against the wall
                "N14Magazine",               // scattered on the table or floor
                "N14DrinkNukaColaBottleFull", // set down and never reclaimed
                // Tier 3
                "N14DecorFloorGlass4",
                "N14JunkPile6",
                "N14DecorFloorCardboard",
            },

            // ── Vault Lab ──────────────────────────────────────────────────────
            // Tier 2: lab table, rusted terminal, filing cabinets, notice board,
            //         chalkboard, mug, scattered papers
            // Tier 3: broken glass, junk pile, more paper
            ["lab"] = new[]
            {
                // Tier 2
                "N14TableDeskMetal",         // lab table
                "N14ComputerTerminalRusted", // research terminal, logged off mid-session
                "N14filingCabinet",          // drawer still open
                "N14LootFilingCabinet",      // second cabinet, contents worth finding
                "N14NoticeBoard",            // safety protocols and experiment schedules
                "N14ChalkboardWall",         // mid-calculation when the vault fell
                "N14JunkCoffeepot",          // coffeepot ring-stained into the desk surface
                "N14DecorFloorPaper1",       // research notes scattered in haste
                // Tier 3
                "N14DecorFloorGlass3",
                "N14JunkPile5",
                "N14DecorFloorPaper2",
            },

            // ── Vault Armory ───────────────────────────────────────────────────
            // Tier 2: weapon bench, ammo cans, army crate, locker, terminal, chair
            // Tier 3: junk pile, broken glass, fallen board
            ["armory"] = new[]
            {
                // Tier 2
                "N14WorkbenchWeaponbench",   // cleaning and maintenance bench
                "N14AmmoBox10mm",            // ammo cans in a corner
                "N14AmmoBox9mm",             // second caliber
                "N14CrateArmy",              // military-grade sealed crate
                "N14ClosetGrey1",            // reinforced locker for sidearms
                "N14ShelfMetal",             // shelving for equipment
                "N14ComputerTerminal",       // inventory tracking terminal
                "N14ChairMetalFolding",      // one chair at the terminal; comfort not the goal
                // Tier 3
                "N14JunkPile8",
                "N14DecorFloorGlass2",
                "N14DecorFloorBoard3",
            },

            // ── Vault Vault ────────────────────────────────────────────────────
            // Tier 2: vault crates, shelving, security terminal, reinforced locker
            // Tier 3: sealed barrel, debris, inventory manifest
            ["vault"] = new[]
            {
                // Tier 2
                "N14LootCrateVaultBigRusted",       // large vault crates on reinforced shelving
                "N14LootCrateVaultLongRusted",      // elongated vault container
                "N14LootCrateVaultStandard",        // standard access-required crate
                "N14ShelfMetal",                    // heavy steel shelving along walls
                "N14ComputerTerminalWall",          // wall-mounted security terminal
                "N14ClosetGenericRefillingMilitary", // reinforced locker, last guard's post
                // Tier 3
                "N14YellowBarrel",
                "N14JunkPile1",
                "N14DecorFloorBoard4",
            },

            // ── Vault Overseer ─────────────────────────────────────────────────
            // Tier 2: working terminal (nicer model), padded chair, safe, vault-tec seal,
            //         bookshelf, personal mug
            // Tier 3: dead plant, scattered papers, calendar
            ["overseer"] = new[]
            {
                // Tier 2
                "N14ComputerTerminal",       // better model than elsewhere in the vault
                "N14ChairOfficeBlue",        // padded; everyone else had folding metal
                "N14LootClosetSafe",         // the combination only one person knew
                "N14SignVaultTec",           // authority made visible on the wall
                "N14PosterVaultTec01",       // framed, corners not bent
                "N14Bookshelf",              // actual shelved books, maintained
                "N14JunkCoffeepot",          // nicer than the barracks pots, not dented
                // Tier 3
                "N14Plantpot1",
                "N14DecorFloorPaper2",
                "N14WallDecorCalendar",
            },

            // ── Vault Reactor ──────────────────────────────────────────────────
            // Tier 2: APC panel, modular console, substation, coolant pipes,
            //         radiation barrels, warning posters, maintenance terminal, wrench
            // Tier 3: debris, broken glass
            ["reactor"] = new[]
            {
                // Tier 2
                "N14APCBreaker",                    // control panel the operators faced each shift
                "N14MachineModularMachineConsoleTall", // secondary control panel bank
                "N14SubstationBasicRusty",          // power conditioning between reactor and grid
                "N14GasPipeStraight",               // coolant and exhaust piping
                "N14GasPipeBend",                   // pipe routing at corners
                "N14YellowBarrel",                  // radiation waste drums
                "N14PosterDangerSign1",             // mandatory rad warning by regulation
                "N14PosterCautionSign1",            // secondary caution on the core housing
                "N14ComputerTerminalWallControls",  // maintenance terminal on the wall
                "N14Wrench",                        // left mid-job near a pipe fitting
                // Tier 3
                "N14DecorFloorBoard1",
                "N14JunkPile7",
                "N14DecorFloorGlass1",
            },
        },

        MobGroups = new[]
        {
            new[] { ("N14MobGhoulFeral", 30), ("N14MobGhoulFeralReaver", 20), ("N14MobGhoulFeralRotter", 15) },
            new[] { ("N14MobRobotProtectronHostile", 15), ("N14MobRobotAssaultronHostile", 10) },
        },

        DecalPool        = new[] { "DirtHeavy", "DirtMedium", "Damaged", "Rust", "burnt1", "burnt2", "Remains" },
        HazardPool       = new[] { "RadiationPulse", "SignRadiation" },
        JunkPool         = new[] { "N14JunkPile1", "N14JunkPile2", "N14JunkPile3", "N14JunkPile4", "N14JunkPile5", "N14JunkPile6", "N14JunkPile1Refilling2", "N14JunkPile1Refilling5" },
        FloorScatterPool = new[] { "N14DecorFloorPaper", "N14DecorFloorPaper1", "N14DecorFloorCardboard", "N14DecorFloorTrashbags1", "N14DecorFloorTrashbags2", "N14DecorFloorTrashbags3", "N14DecorFloorFood1", "N14DecorFloorFood3", "N14DecorFloorFood6", "N14DecorFloorGlass1", "N14DecorFloorSkeleton", "N14DecorFloorSkeletonOver", "N14DecorFloorBookPile1", "N14DecorFloorBookPile4", "N14DecorFloorBookstack1" },
        BlueprintPool    = new[] { "N14BlueprintVaultWeaponsT1", "N14BlueprintVaultWeaponsT2", "N14BlueprintVaultWeaponsT3", "N14BlueprintVaultWeaponsT4", "N14BlueprintVaultArmorT1", "N14BlueprintVaultArmorT2", "N14BlueprintVaultAmmoT1", "N14BlueprintVaultAmmoT2", "N14BlueprintNCRWeaponsT1", "N14BlueprintLegionWeaponsT1", "N14BlueprintNCRArmorT1", "N14BlueprintLegionArmorT1" },
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Sewer Profile
    // ─────────────────────────────────────────────────────────────────────────

    private static ThemeProfile BuildSewerProfile() => new()
    {
        Theme       = UndergroundTheme.Sewer,
        ThemeName   = "Sewers",
        Description = "Crumbling brick sewer tunnels and drainage chambers. Branching organic layout " +
                      "with narrow passages, flooded channels, and improvised survivor camps.",
        LayoutStyle = LayoutStyle.BranchingTunnels,

        MandatoryAnchors = new List<RoomType>
        {
            RoomType.SewerPump,
            RoomType.SewerNest,
        },

        RoomDefinitions = new List<RoomTypeDefinition>
        {
            // Tunnel — the most common passage type
            new()
            {
                RoomType = RoomType.SewerTunnel, Weight = 35, MaxCount = 4,
                MinW = 5, MaxW = 9, MinH = 12, MaxH = 22,
                FurniturePoolKey = "tunnel",
                RequiredFeatures = { "N14GasPipeStraight" },
            },
            // Junction — intersection chamber
            new()
            {
                RoomType = RoomType.SewerJunction, Weight = 15, MaxCount = 3,
                MinW = 7, MaxW = 13, MinH = 7, MaxH = 13,
                FurniturePoolKey = "junction",
                RequiredFeatures = { "N14APCBreaker" },
                AdjacencyPreferences = { RoomType.SewerTunnel },
            },
            // Grotto — natural cavern
            new()
            {
                RoomType = RoomType.SewerGrotto, Weight = 12, MaxCount = 3,
                MinW = 7, MaxW = 13, MinH = 7, MaxH = 13,
                FurniturePoolKey = "grotto",
                RequiredFeatures = { "N14DecorStalagmite1" },
            },
            // Pump station — utility, rare
            new()
            {
                RoomType = RoomType.SewerPump, Weight = 10, MaxCount = 2,
                MinW = 9, MaxW = 15, MinH = 9, MaxH = 15,
                FurniturePoolKey = "pump",
                RequiredFeatures = { "N14MachineWaterTreatmentBroken" },
                AdjacencyPreferences = { RoomType.SewerJunction },
            },
            // Nest — creature lair, dangerous
            new()
            {
                RoomType = RoomType.SewerNest, Weight = 10, MaxCount = 2,
                MinW = 7, MaxW = 13, MinH = 7, MaxH = 13,
                FurniturePoolKey = "nest",
                RequiredFeatures = { "N14DecorFloorSkeleton" },
                AdjacencyExclusions = { RoomType.SewerCamp },
            },
            // Camp — survivor camp, moderate
            new()
            {
                RoomType = RoomType.SewerCamp, Weight = 18, MaxCount = 2,
                MinW = 7, MaxW = 13, MinH = 7, MaxH = 13,
                FurniturePoolKey = "camp",
                RequiredFeatures = { "N14Bedroll" },
                AdjacencyPreferences = { RoomType.SewerJunction, RoomType.SewerPump },
                AdjacencyExclusions = { RoomType.SewerNest },
            },
        },

        ValidEnvironmentalStates = new List<EnvironmentalState>
        {
            EnvironmentalState.Abandoned,
            EnvironmentalState.Flooded,
            EnvironmentalState.Damaged,
            EnvironmentalState.Overgrown,
        },

        CorridorStyle = new CorridorStyle
        {
            Width = 2,
            BranchingFactor = 0.35f,
            LoopProbability = 0.30f,
        },

        TilePalette = new TilePalette
        {
            RoomFloorTiles     = new[] { "FloorDirtIndoors", "FloorDirtNew", "FloorMS13MetalGrate", "FloorMS13Concrete", "FloorCave", "FloorMS13BrickConcrete" },
            CorridorFloorTiles = new[] { "FloorDirtIndoors", "FloorDirtNew", "FloorMS13MetalGrate", "FloorCave", "FloorMS13BrickConcrete" },
            HubFloorTiles      = new[] { "FloorDirtIndoors", "FloorMS13BrickConcrete", "FloorMS13Concrete" },
            RoomWallEntity     = "N14WallBrickSlantedIndestructible",
            HubWallEntity      = "N14WallBrickGraySlantedIndestructible",
            BackgroundTile     = "FloorAsteroidSand",
            RoomDoorEntity     = "N14DoorRoomRepaired",
            HubDoorEntity      = "N14DoorMakeshift",
        },

        LightConfig = new LightConfig
        {
            LightEntity  = "N14TorchWall",
            Style        = LightStyle.WallMounted,
            DefaultCount = 1,
            CountPerRoomType = new Dictionary<RoomType, int>
            {
                { RoomType.FactionHub,  3 },
                { RoomType.SewerGrotto, 2 },
                { RoomType.SewerPump,   2 },
            },
        },

        FurniturePools = new Dictionary<string, string[]>
        {
            ["standard"] = new[]
            {
                "N14JunkBench", "N14JunkTable", "N14JunkBed1", "N14JunkToilet", "N14JunkSink",
                "N14JunkDresser", "N14JunkCabinet", "N14JunkPile4", "N14JunkPile6", "N14JunkPile8",
            },
            ["hub"] = new[]
            {
                "N14BarricadeMetal", "N14JunkBench", "CrateWooden", "N14BarricadeSandbagSingle",
                "N14JunkTable", "N14BurningBarrel", "N14Bonfire", "N14WorkbenchMetal",
            },

            // ── Sewer Tunnel ───────────────────────────────────────────────────
            // Tier 2: additional pipe shapes, vent, junction box, graffiti, tin can
            // Tier 3: debris, broken glass, rotted board
            ["tunnel"] = new[]
            {
                // Tier 2
                "N14GasPipeBend",            // pipe turn where the tunnel curves
                "N14GasPipeFourway",         // branch point on the main run
                "N14WallmountVent",          // drainage/exhaust vent fixture on wall
                "N14APCBreaker",             // junction box with sprung panel
                "N14WallmountVentOpen",      // #Misfits Fix - replaced decal N14GraffitiArrow (not spawnable)
                "N14JunkTincan",             // discarded, evidence of passage
                // Tier 3
                "N14JunkPile9",
                "N14DecorFloorGlass5",
                "N14DecorFloorBoard1",
            },

            // ── Sewer Junction ─────────────────────────────────────────────────
            // Tier 2: pipe convergence, directional graffiti, skeleton, old exit sign
            // Tier 3: debris, glass, trash
            ["junction"] = new[]
            {
                // Tier 2
                "N14GasPipeFourway",         // physical convergence of pipe runs
                "N14SignDanger",             // #Misfits Fix - replaced decal N14GraffitiArrow (not spawnable)
                "N14WallDecorExitsign",      // #Misfits Fix - replaced decal N14GraffitiDanger (not spawnable)
                "N14SignNotice",             // #Misfits Fix - replaced decal N14GraffitiCampEast (not spawnable)
                "N14SignStreetExit",         // repurposed exit sign, rusted
                "N14DecorFloorSkeleton",     // a body at the crossroads
                "N14WallmountVentDamaged",   // blown vent grate, access unclear
                "N14WallmountVentDamaged",   // #Misfits Fix - replaced decal N14GraffitiDead (not spawnable)
                "N14JunkTincan",             // resting-point litter
                // Tier 3
                "N14JunkPile10",
                "N14DecorFloorGlass6",
                "N14DecorFloorTrashbags1",
            },

            // ── Sewer Pump ─────────────────────────────────────────────────────
            // Tier 2: intake/output pipes, maintenance panel, monitoring terminal,
            //         wrench, lunchbox, dropped work order
            // Tier 3: oily debris, chemical barrel, fallen panel
            ["pump"] = new[]
            {
                // Tier 2
                "N14GasPipeStraight",        // intake and output lines from the pump
                "N14GasPipeBend",            // routing the flow away from the unit
                "N14APCBreaker",             // maintenance panel on the wall
                "N14ComputerTerminalWall",   // pressure and flow monitoring
                "N14Wrench",                 // left beside a fitting mid-job
                "N14JunkLunchbox",           // the maintenance worker's lunch
                "N14DecorFloorPaper1",       // soggy work order on the floor
                // Tier 3
                "N14JunkPile11",
                "N14RedBarrel",
                "N14DecorFloorBoard2",
            },

            // ── Sewer Nest ─────────────────────────────────────────────────────
            // Tier 2: torn bags, cardboard, dragged-in junk, tire, territorial graffiti
            // Tier 3: broken glass, second junk pile
            ["nest"] = new[]
            {
                // Tier 2
                "N14DecorFloorTrashbags2",   // torn bags dragged in as nesting material
                "N14DecorFloorTrashbags3",   // more torn fabric layered into bedding
                "N14DecorFloorCardboard",    // flattened cardboard at the nest center
                "N14JunkTire1",              // dragged in; creatures collect without purpose
                "N14JunkPile1",              // assorted debris, brought not placed
                "N14SignDanger",             // #Misfits Fix - replaced decal N14GraffitiDead (not spawnable)
                // Tier 3
                "N14DecorFloorGlass4",
                "N14JunkPile12",
            },

            // ── Sewer Grotto ───────────────────────────────────────────────────
            // Tier 2: more stalagmite formations, cave fungus (multiple varieties)
            // Tier 3: cave debris, plank across a pool
            ["grotto"] = new[]
            {
                // Tier 2
                "N14DecorStalagmite2",              // second formation; caves cluster
                "N14DecorStalagmite3",              // shorter; the variety reads geological
                "N14WastelandFloraWildCaveFungus",  // fungal growth; the moisture supports it
                "N14FloraProduceWildCaveFungus",    // smaller clusters at the formation base
                "N14WastelandFloraWildCaveFungusRad", // rad-active variant; the water table is contaminated
                // Tier 3
                "N14DecorStalagmite4",
                "N14DecorFloorBoard1",
                "N14JunkPile3",
            },

            // ── Sewer Camp ─────────────────────────────────────────────────────
            // Tier 2: campfire, cook pot, shopping cart, cardboard, tin cans,
            //         graffiti (territorial + despair), stolen supply crate
            // Tier 3: loose scrap, trash bags, broken bottle
            ["camp"] = new[]
            {
                // Tier 2
                "N14Bonfire",                // an extinguished campfire; scorch marks predate you
                "N14JunkCookpot",            // cook pot on the fire grate; ate here regularly
                "N14CrateTrashcart",         // shopping cart full of gathered scrap
                "N14DecorFloorCardboard",    // cardboard ground cover; bedroll sits on top
                "N14JunkTincan",             // empty tins by the fire; all that's left
                "N14SignDanger",              // #Misfits Fix - replaced decal N14GraffitiDontOpen (not spawnable)
                "N14SignNotice",              // #Misfits Fix - replaced decal N14GraffitiDeadinside (not spawnable)
                "N14CrateArmy",             // stolen supply crate, now furniture
                // Tier 3
                "N14JunkPile5",
                "N14DecorFloorTrashbags4",
                "N14DecorFloorGlass5",
            },
        },

        MobGroups = new[]
        {
            new[] { ("N14MobRadroach", 35), ("N14MobRadscorpion", 15), ("N14MobBloatfly", 10) },
            new[] { ("N14MobGhoulFeral", 25), ("N14MobGhoulFeralRotter", 15) },
        },

        DecalPool        = new[] { "DirtHeavy", "DirtLight", "DirtMedium", "Dirt", "Damaged", "Rust", "DirtHeavyMonotile" },
        HazardPool       = new[] { "Acidifier", "SignBiohazard" },
        JunkPool         = new[] { "N14JunkPile4", "N14JunkPile5", "N14JunkPile6", "N14JunkPile7", "N14JunkPile8", "N14JunkPile1Refilling3", "N14JunkPile1Refilling4", "N14JunkPile1Refilling9" },
        FloorScatterPool = new[] { "N14DecorFloorCardboard", "N14DecorFloorBrickrubble", "N14DecorFloorBrickStack", "N14DecorFloorTrashbags1", "N14DecorFloorTrashbags4", "N14DecorFloorTrashbags6", "N14DecorFloorFood1", "N14DecorFloorFood2", "N14DecorFloorScrapwood", "N14DecorFloorSkeleton", "N14DecorFloorPallet" },
        BlueprintPool    = new[] { "N14BlueprintVaultWeaponsT1", "N14BlueprintVaultWeaponsT2", "N14BlueprintVaultArmorT1", "N14BlueprintVaultAmmoT1" },
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Metro Profile
    // ─────────────────────────────────────────────────────────────────────────

    private static ThemeProfile BuildMetroProfile() => new()
    {
        Theme       = UndergroundTheme.Metro,
        ThemeName   = "Metro Tunnels",
        Description = "Abandoned subway system with industrial grate platforms, linear corridors, " +
                      "dispatch centers, and maintenance tunnels.",
        LayoutStyle = LayoutStyle.LinearCorridors,

        MandatoryAnchors = new List<RoomType>
        {
            RoomType.MetroCommand,
            RoomType.MetroDepot,
        },

        RoomDefinitions = new List<RoomTypeDefinition>
        {
            // Platform — long station areas, most common
            new()
            {
                RoomType = RoomType.MetroPlatform, Weight = 30, MaxCount = 4,
                MinW = 16, MaxW = 26, MinH = 6, MaxH = 10,
                FurniturePoolKey = "platform",
                RequiredFeatures = { "N14JunkBench" },
                AdjacencyPreferences = { RoomType.MetroTunnel },
            },
            // Tunnel — transit passages
            new()
            {
                RoomType = RoomType.MetroTunnel, Weight = 20, MaxCount = 3,
                MinW = 6, MaxW = 10, MinH = 16, MaxH = 26,
                FurniturePoolKey = "tunnel",
                RequiredFeatures = { "N14Rails" },
            },
            // Maintenance — utility back-rooms
            new()
            {
                RoomType = RoomType.MetroMaintenance, Weight = 20, MaxCount = 3,
                MinW = 10, MaxW = 16, MinH = 10, MaxH = 16,
                FurniturePoolKey = "maintenance",
                RequiredFeatures = { "N14WorkbenchMetal", "N14ShelfMetal" },
                AdjacencyPreferences = { RoomType.MetroPlatform },
            },
            // Depot — cargo area, moderate
            new()
            {
                RoomType = RoomType.MetroDepot, Weight = 15, MaxCount = 2,
                MinW = 10, MaxW = 16, MinH = 10, MaxH = 16,
                FurniturePoolKey = "depot",
                RequiredFeatures = { "N14BlackBarrelFull" },
                AdjacencyPreferences = { RoomType.MetroCommand },
            },
            // Command — dispatch center, singular
            new()
            {
                RoomType = RoomType.MetroCommand, Weight = 15, MaxCount = 1,
                MinW = 10, MaxW = 16, MinH = 10, MaxH = 16,
                FurniturePoolKey = "command",
                RequiredFeatures = { "N14MachineModularMachineDialsTall" },
                AdjacencyPreferences = { RoomType.MetroDepot, RoomType.MetroPlatform },
            },
        },

        ValidEnvironmentalStates = new List<EnvironmentalState>
        {
            EnvironmentalState.Pristine,
            EnvironmentalState.Abandoned,
            EnvironmentalState.Damaged,
        },

        CorridorStyle = new CorridorStyle
        {
            Width = 2,
            BranchingFactor = 0.15f,
            LoopProbability = 0.10f,
        },

        TilePalette = new TilePalette
        {
            RoomFloorTiles     = new[] { "FloorMetalGreyDark", "FloorMetalGreyDarkSolid", "FloorMS13MetalGrate", "FloorMS13MetalTile", "N14FloorConcrete", "FloorMS13MetalIndustrial", "FloorSteelDirty", "FloorMS13ConcreteIndustrialAlt" },
            CorridorFloorTiles = new[] { "FloorMetalGreyDark", "FloorMS13MetalGrate", "FloorMS13MetalTile", "FloorMS13MetalIndustrial", "FloorSteelDirty" },
            HubFloorTiles      = new[] { "FloorMetalGreyDark", "FloorMS13MetalTile", "FloorMS13MetalIndustrial" },
            RoomWallEntity     = "N14WallDungeonSlantedIndestructible",
            HubWallEntity      = "N14WallCombSlantedIndestructible",
            BackgroundTile     = "FloorAsteroidSand",
            RoomDoorEntity     = "N14DoorWoodRoom",
            HubDoorEntity      = "N14DoorBunker",
        },

        LightConfig = new LightConfig
        {
            LightEntity  = "LightPostSmall",
            Style        = LightStyle.GroundPost,
            DefaultCount = 1,
            CountPerRoomType = new Dictionary<RoomType, int>
            {
                { RoomType.FactionHub,       3 },
                { RoomType.MetroCommand,     3 },
                { RoomType.MetroPlatform,    3 },
                { RoomType.Central,          2 },
                { RoomType.MetroMaintenance, 2 },
                { RoomType.MetroDepot,       2 },
            },
        },

        FurniturePools = new Dictionary<string, string[]>
        {
            ["standard"] = new[]
            {
                "N14JunkBench", "N14JunkTable", "N14JunkArcade", "N14JunkTV", "N14JunkJukebox",
                "N14LootCrateArmy", "N14JunkMachine", "N14JunkDresser", "N14JunkPile9",
                "N14JunkPile10", "N14JunkPile11",
            },
            ["hub"] = new[]
            {
                "N14BarricadeMetal", "N14JunkBench", "N14LootCrateMilitary", "N14BarricadeSandbagSingle",
                "N14JunkMachine", "N14JunkPile12", "N14JunkPile1",
            },

            // ── Metro Platform ─────────────────────────────────────────────────
            // Tier 2: magazine rack, vending machines, trash bin, ad posters,
            //         abandoned briefcase, scattered newspaper, second trash can
            // Tier 3: newspaper floor scatter, debris, broken glass
            ["platform"] = new[]
            {
                // Tier 2
                "N14MagazineRack",            // news rack by the turnstile, papers still inside
                "N14VendingMachineNukaCola",  // the machine a commuter fed quarters into
                "N14VendingMachineCigarette", // for the stressed commuter
                "N14Trashbin",                // overflowing, never emptied since the last train
                "N14PosterAdvertNukaCola1",   // advertisement in a dead lightbox
                "N14PosterAdvertGrognak1",    // second platform advertisement
                "N14BriefcaseContract",       // set down beside a bench and never reclaimed
                "N14Magazine",                // dropped newspaper on the platform floor
                "N14DecorStreetTrashbin",     // second trash can at the far end
                // Tier 3
                "N14DecorFloorPaper2",
                "N14JunkPile6",
                "N14DecorFloorGlass3",
            },

            // ── Metro Tunnel ───────────────────────────────────────────────────
            // Tier 2: rail curve, cable conduit, junction box, blown vent, dead light, fallen panel
            // Tier 3: track rubble, glass, more debris
            ["tunnel"] = new[]
            {
                // Tier 2
                "N14RailsTurnNE",            // the line bends here
                "N14GasPipeStraight",        // cable conduit along the wall
                "N14APCBreaker",             // junction box at a maintenance interval
                "N14WallmountVentDamaged",   // blown vent; the tunnel had forced-air ventilation
                "N14LightSmallEmpty",        // dead light overhead; this section went dark
                "N14DecorFloorBoard5",       // fallen ceiling panel across the track bed
                // Tier 3
                "N14JunkPile8",
                "N14DecorFloorGlass4",
                "N14DecorFloorBoard6",
            },

            // ── Metro Maintenance ──────────────────────────────────────────────
            // Tier 2: electrical panel, second locker, third locker (open), mop bucket,
            //         lunchbox, dropped work order, wrench, wall clock
            // Tier 3: supply crate, floor debris, broken fluorescent
            ["maintenance"] = new[]
            {
                // Tier 2
                "N14APCBreaker",             // the panel this room exists to service
                "N14ClosetGrey1",            // locker with a uniform still inside
                "N14ClosetGrey2",            // second locker, hanging open
                "N14MopBucket",              // in the corner; the crew cleaned too
                "N14JunkLunchbox",           // on the end of the workbench; every-shift lunch
                "N14DecorFloorPaper1",       // work order or inspection report on the bench
                "N14Wrench",                 // most-used tool, left out
                "N14WallDecorClock",         // shift timing on the wall
                // Tier 3
                "N14CrateArmy",
                "N14JunkPile2",
                "N14DecorFloorGlass5",
            },

            // ── Metro Depot ────────────────────────────────────────────────────
            // Tier 2: second drum type, heavy shelving, shop shelving, industrial welder,
            //         portable welder, maintenance cart, maintenance log, supervisor desk
            // Tier 3: industrial debris, fallen gantry, shattered gauge
            ["depot"] = new[]
            {
                // Tier 2
                "N14RedBarrel",              // second drum type alongside the black barrels
                "N14ShelfMetal",             // heavy shelving with spare parts
                "N14ShelfMetalShop",         // second shelf unit, different part organization
                "N14WelderIndustrial",       // industrial welding equipment
                "N14Welder",                 // portable welder left near the last job
                "N14DecorMinecart",          // maintenance cart for heavy parts
                "N14DecorFloorPaper3",       // maintenance log on a dropped clipboard
                "N14TableDeskMetal",         // shift supervisor's desk for paperwork
                // Tier 3
                "N14JunkPile10",
                "N14DecorFloorBoard6",
                "N14DecorFloorGlass6",
            },

            // ── Metro Command ──────────────────────────────────────────────────
            // Tier 2: dead screen monitors, radio table, notice board, chalkboard
            //         schedule, operator desk, controller's mug, coat rack
            // Tier 3: last dispatch papers, light debris, broken mug
            ["command"] = new[]
            {
                // Tier 2
                "N14ComputerTerminalWallDisplays", // wall-mounted screen array, dead track diagrams
                "N14ComputerTerminalWallControls", // adjacent control panel
                "N14RadioTable",                   // controller communicated with maintenance here
                "N14NoticeBoard",                  // route maps, schedules, emergency protocols
                "N14ChalkboardWall",               // shift timetable still partially legible
                "N14TableDeskMetal",               // shift supervisor's desk
                "N14JunkCoffeepot",                // rings burned into the desk surface over years
                "N14ShelfWoodClothesRack",         // coat rack; someone's jacket still on it
                // Tier 3
                "N14DecorFloorPaper1",
                "N14JunkPile4",
                "N14DecorFloorGlass1",
            },
        },

        MobGroups = new[]
        {
            new[] { ("N14MobGhoulFeral", 30), ("N14MobGhoulFeralReaver", 25), ("N14MobGhoulFeralRotter", 10) },
            new[] { ("N14MobRaiderPsycho", 20), ("N14MobRaiderFernMelee", 15) },
        },

        DecalPool        = new[] { "DirtLight", "DirtMedium", "Damaged", "Rust", "burnt3", "burnt4", "Remains" },
        HazardPool       = new[] { "RadiationPulse", "SignCorrosives" },
        JunkPool         = new[] { "N14JunkPile7", "N14JunkPile8", "N14JunkPile9", "N14JunkPile10", "N14JunkPile11", "N14JunkPile12", "N14JunkPile1Refilling7", "N14JunkPile1Refilling10" },
        FloorScatterPool = new[] { "N14DecorFloorPaper", "N14DecorFloorGlass1", "N14DecorFloorCardboard", "N14DecorFloorScrapwood", "N14DecorFloorTrashbags2", "N14DecorFloorTrashbags5", "N14DecorFloorFood4", "N14DecorFloorFood5", "N14DecorFloorBrickrubble", "N14DecorFloorBookPile2" },
        BlueprintPool    = new[] { "N14BlueprintVaultWeaponsT1", "N14BlueprintVaultArmorT1", "N14BlueprintNCRWeaponsT1", "N14BlueprintLegionWeaponsT1" },
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Environmental State Modifier Definitions
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the generation modifiers for the given environmental state.
    /// </summary>
    public static EnvironmentalStateModifiers GetStateModifiers(EnvironmentalState state) => state switch
    {
        EnvironmentalState.Pristine => new EnvironmentalStateModifiers
        {
            // Minimal wear — lights fully functional, no extra debris
        },
        EnvironmentalState.Abandoned => new EnvironmentalStateModifiers
        {
            DecalDensityMult       = 1.5f,
            JunkDensityMult        = 1.3f,
            LightReductionFraction = 0.3f,
        },
        EnvironmentalState.Flooded => new EnvironmentalStateModifiers
        {
            WaterChannelChanceOverride = 0.10f,
            DecalDensityMult           = 1.2f,
        },
        EnvironmentalState.Damaged => new EnvironmentalStateModifiers
        {
            RubbleTileReplaceFraction = 0.15f,
            HazardChanceMult          = 1.25f,
            DecalDensityMult          = 1.3f,
            LightReductionFraction    = 0.2f,
        },
        EnvironmentalState.Overgrown => new EnvironmentalStateModifiers
        {
            MobCountMult   = 0.7f,
            NatureDecals   = true,
            DecalDensityMult = 1.4f,
        },
        _ => new EnvironmentalStateModifiers(),
    };

    /// <summary>
    /// Merges multiple environmental state modifiers into a single compound modifier.
    /// Multipliers compound multiplicatively; fractions take the max; booleans OR.
    /// </summary>
    public static EnvironmentalStateModifiers MergeModifiers(IEnumerable<EnvironmentalState> states)
    {
        float decal     = 1f, junk   = 1f, hazard = 1f, mob = 1f;
        float lightSkip = 0f, rubble = 0f, water  = 0f;
        bool  nature    = false;

        foreach (var state in states)
        {
            var m = GetStateModifiers(state);
            decal     *= m.DecalDensityMult;
            junk      *= m.JunkDensityMult;
            hazard    *= m.HazardChanceMult;
            mob       *= m.MobCountMult;
            lightSkip  = System.Math.Max(lightSkip, m.LightReductionFraction);
            rubble     = System.Math.Max(rubble, m.RubbleTileReplaceFraction);
            water      = System.Math.Max(water, m.WaterChannelChanceOverride);
            nature    |= m.NatureDecals;
        }

        return new EnvironmentalStateModifiers
        {
            DecalDensityMult           = decal,
            JunkDensityMult            = junk,
            HazardChanceMult           = hazard,
            MobCountMult               = mob,
            LightReductionFraction     = lightSkip,
            RubbleTileReplaceFraction  = rubble,
            WaterChannelChanceOverride = water,
            NatureDecals               = nature,
        };
    }
}
