namespace Rimtera.World;

public enum TileId : ushort
{
    Air = 0,
    Soil = 1,
    Rock = 2,
    CopperOre = 3,
    Grass = 4,
    Sand = 5,
    Wood = 6,
    Bedrock = 7,
    Workbench = 8,
    Furnace = 9,
    Anvil = 10,
    Snow = 11,
    DesertSand = 12,
    JungleGrass = 13,
    FungalSoil = 14,
    EmberRock = 15,
    CorruptRock = 16,
    CrimsonSoil = 17,
    HallowGrass = 18,
    DungeonBrick = 19,
    CobaltOre = 20,
    ViridiumOre = 21,
    EnergyCrystal = 22,
    MiniTurret = 23,
    HydroponicsTray = 24,
    SolarPanel = 25,
    OxygenUnit = 26,
    GlacialOre = 27,
    UraniumOre = 28,
    PyrociteOre = 29,
    ScrapIronOre = 30,
    // Blight ground. Radiated blight reuses CrimsonSoil (radiated soil) and CorruptRock (caustic crystal).
    RadiatedSand = 31,
    RadiatedIce = 32,
    OvergrowthSoil = 33,
    OvergrowthRock = 34,
    OvergrowthSand = 35,
    OvergrowthIce = 36,
    // Blight hearts (2x2, any drill) and altars (2x2, only after the Fracture).
    ReactorCore = 37,
    SporeHeart = 38,
    RadAltar = 39,
    RootAltar = 40,
    // World loot and furniture.
    LifeCore = 41,
    SupplyCache = 42,
    SalvagePod = 43,
    PurifierBeacon = 44,
    // Molten Chasm ceiling and derelict structures.
    Ash = 45,
    Obsidian = 46,
    ShipDoor = 47,

    // ---- Content expansion (append-only; see World/ContentTiles.cs for definitions) ----
    // Natural blocks.
    Mud = 48,
    Clay = 49,
    Gravel = 50,
    Silt = 51,
    SaltCrust = 52,
    AcidSand = 53,
    ShellSand = 54,
    CoralReef = 55,
    Slate = 56,
    Alabaster = 57,
    BasaltColumn = 58,
    HiveComb = 59,
    Nectar = 60,
    PearlStone = 61,
    PrismaticGrass = 62,
    GlowshroomGrass = 63,
    ThinIce = 64,
    Meteorite = 65,
    MiteSilk = 66,
    MossBlue = 67,
    MossGold = 68,
    MossRed = 69,
    MossViolet = 70,
    LumeniteOre = 71,
    VoidsteelOre = 72,
    // Crafted blocks.
    FerriteBrick = 73,
    CobaltBrick = 74,
    ViridiumBrick = 75,
    LumeniteBrick = 76,
    VoidsteelBrick = 77,
    AstralBrick = 78,
    SlateBrick = 79,
    AlabasterBrick = 80,
    BasaltBrick = 81,
    PearlBrick = 82,
    HiveBrick = 83,
    CoralBrick = 84,
    Glass = 85,
    ReinforcedGlass = 86,
    HullPlatingGrey = 87,
    HullPlatingBlue = 88,
    HullPlatingGreen = 89,
    HullPlatingRed = 90,
    HullPlatingHazard = 91,
    AstroTimberPlanks = 92,
    CharredPlanks = 93,
    CrystalBlock = 94,
    CircuitPanel = 95,
    Conveyor = 96,
    Grate = 97,
    Plate = 98,
    Vent = 99,
    // Crafting stations (non-solid props).
    FieldWorkbench = 100,
    NanoFabricatorMk2 = 101,
    OreRefineryMk2 = 102,
    PowerForgeMk2 = 103,
    AlloyAnvil = 104,
    VoidForge = 105,
    AstralAssembler = 106,
    ChemLab = 107,
    BioLab = 108,
    Loom = 109,
    Sawmill = 110,
    Kiln = 111,
    CookingStation = 112,
    TinkerBench = 113,
    DroneBay = 114,
    AmmoPress = 115,
    ChargeReactor = 116,
    ResearchTerminal = 117,
    // Doors: closed doors are solid, open doors are not.
    PlankDoor = 118,
    PlankDoorOpen = 119,
    HullDoor = 120,
    HullDoorOpen = 121,
    Trapdoor = 122,
    TrapdoorOpen = 123,
    VentDoor = 124,
    VentDoorOpen = 125,
    // Furniture.
    PlankChair = 126,
    HullChair = 127,
    Stool = 128,
    Armchair = 129,
    Sofa = 130,
    ObservationChair = 131,
    PlankTable = 132,
    HullTable = 133,
    DiningTable = 134,
    CoffeeTable = 135,
    BunkBed = 136,
    BunkBedMetal = 137,
    ModularBed = 138,
    CryoPodBed = 139,
    StorageCrate = 140,
    WardrobeLocker = 141,
    Shelving = 142,
    ShelfJars = 143,
    StorageContainer = 144,
    FoodLocker = 145,
    LockedVaultChest = 146,
    WallTorch = 147,
    DeskLamp = 148,
    FloorLamp = 149,
    CeilingLight = 150,
    StandingFloodlight = 151,
    GlowLampAmber = 152,
    GlowLampCyan = 153,
    GlowLampViolet = 154,
    ColonyBanner = 155,
    SignPost = 156,
    PaintingPlanet = 157,
    PlantPot = 158,
    HerbPlanter = 159,
    DisplayPanel = 160,
    ControlConsole = 161,
    PowerHub = 162,
    BatteryCharger = 163,
    ChemicalAnalyzer = 164,
    FoodSynthesizer = 165,
    WaterPurifier = 166,
    AdvancedFabricator = 167,
    CraftingBench = 168,
    MetalFurnace = 169,
    ResearchLab = 170,
    // Structures and world features.
    BloomHive = 171,
    PrecursorVaultDoor = 172,
    // Kitbashed biome blocks.
    ShellBeach = 173,
    MyceliumStone = 174,
    SporeBlock = 175,
    MushroomCap = 176,
    CrystalSpire = 177,
    PearlSand = 178,
    CanopyGrass = 179,
    PrecursorBrick = 180,
    PrecursorGlyph = 181,
    GeodeCyan = 182,
    GeodeMagenta = 183,
    // Biome structures and vault decor (non-solid props).
    CoastalHabPod = 184,
    BeaconTower = 185,
    FungalHabRuin = 186,
    CrystalAltar = 187,
    PrecursorStatue = 188,
    GlyphTablet = 189,
    EnergyPylon = 190,
    GeodeCluster = 191,
    MiteSilkCocoon = 192,
    // ---- Batch A: world physics and tiles (definitions in World/ContentTiles.Physics.cs) ----
    PlankPlatform = 193,
    MetalPlatform = 194,
    Rope = 195,
    Ladder = 196,
    Vine = 197,
    TreeTrunk = 198,
    TreeCanopy = 199,
    PineTrunk = 200,
    PineCanopy = 201,
    PalmTrunk = 202,
    PalmCanopy = 203,
    Sapling = 204,
    Cactus = 205,
    HerbSunpetal = 206,
    HerbMoonglow = 207,
    HerbRainreed = 208,
    HerbFrostbell = 209,
    HerbEmberroot = 210,
    HerbSporecap = 211,
    Switch = 212,
    Lever = 213,
    PressurePlate = 214,
    Timer1s = 215,
    Timer3s = 216,
    Timer5s = 217,
    GateAnd = 218,
    GateOr = 219,
    GateNot = 220,
    GateXor = 221,
    DartTrap = 222,
    ExplosiveTrap = 223,
    TeleporterPad = 224,
    GiantTrunk = 225,
    GiantLeaf = 226,
    DungeonBrickPrecursor = 227,
    DungeonChest = 228,
    CabinChest = 229,
    PotSurface = 230,
    PotFrozen = 231,
    PotJunk = 232,
    PotCavern = 233,
    PotMolten = 234,
    PotDungeon = 235,
    PotCanopy = 236,
    WeaponShrine = 237,
    StandingTorch = 238,
    // ---- World generation completion (definitions in World/ContentTiles.WorldGen.cs) ----
    ScrapSandstone = 239,
    Slush = 240,
    GemAmethyst = 241,
    GemTopaz = 242,
    GemSapphire = 243,
    GemEmerald = 244,
    GemRuby = 245,
    GemDiamond = 246,
    StalactiteStone = 247,
    StalagmiteStone = 248,
    StalactiteIce = 249,
    StalagmiteIce = 250,
    StalactiteSand = 251,
    StalagmiteSand = 252,
    StalactiteAsh = 253,
    StalagmiteAsh = 254,
    StalactiteHive = 255,
    StalagmiteHive = 256,
    RubblePile = 257,
    LootPile = 258,
    ShellPile = 259,
    StatueHopper = 260,
    StatueCrab = 261,
    StatueBat = 262,
    FallenLog = 263,
    Lilypad = 264,
    Reed = 265,
    Seaweed = 266,
    CoralFan = 267,
    GraveMarker = 268,
    Boulder = 269,
    SpikeStrip = 270,
    SlagForge = 271,
    MudBrick = 272,
    SunkenChest = 273,
    SurfaceCrate = 274,
    ShrineChest = 275,
    SkyChest = 276,
    LanderChest = 277,
    SlagChest = 278,
    BroodPod = 279,
    SolarLily = 280,
    DyeBlossomIndigo = 281,
    DyeBlossomCrimson = 282,
    DyeBlossomGold = 283,
    LongMoss = 284,
    BurrowerNest = 285,
    VaultAltar = 286,
    CrystalShrub = 287,
    WildGourd = 288,
    WildMushroom = 289,
    Campfire = 290,
    // ---- Neo-Terra theme pass: the Irradiated Core (definitions in World/ContentTiles.Theme.cs) ----
    IrradiatedRock = 291,
    CoreCrust = 292,
    VitrifiedGlass = 293,
    // Liquid uranium meeting the fuel sea burns into char slag.
    CharSlag = 294,
    // ---- Hardmode progression: canopy bulbs (grow after the three hard bosses; breaking one wakes the bulb boss) ----
    CanopyBulb = 295,
    // ---- Travel (Systems/GameSimulation.Travel.cs; band 300–319): glide-walks (moving walkways) and pylons ----
    GlideWalkLeft = 300,
    GlideWalkRight = 301,
    ExpressWalkLeft = 302,
    ExpressWalkRight = 303,
    ColonyPylon = 304,
    FrostPylon = 305,
    ScrapPylon = 306,
    CanopyPylon = 307,
    CavernPylon = 308,
    CoastPylon = 309,
    // ---- Player & combat systems (band 320–339; definitions in World/ContentTiles.PlayerSystems.cs) ----
    Sifter = 320, DyeVat = 321, BossTrophy = 322, BossRelic = 323,
    // ---- World & leisure (Systems/GameSimulation.Leisure.cs; band 340–379): golf tee and cup, recorded audio decks ----
    GolfTee = 340, GolfCup = 341,
    DeckGrassland = 342, DeckUnderground = 343, DeckFrozen = 344, DeckJunk = 345, DeckMolten = 346, DeckRadiated = 347,
    DeckOvergrowth = 348, DeckFungal = 349, DeckCrystal = 350, DeckCanopy = 351, DeckCoast = 352, DeckBoss = 353,
    // ---- Content fill (band 380–449; definitions in Systems/Content/ContentFillFlora.cs): biome trees, gem trees, wild plants ----
    VinewoodTrunk = 380, VinewoodCanopy = 381, PalmwoodTrunk = 382, PalmwoodFronds = 383, ShroomStalk = 384, ShroomCap = 385,
    PrismwoodTrunk = 386, PrismwoodCanopy = 387, BlightwoodTrunk = 388, BlightwoodCanopy = 389, GemTreeTrunk = 390,
    GemCanopyAmethyst = 391, GemCanopyTopaz = 392, GemCanopySapphire = 393, GemCanopyEmerald = 394, GemCanopyRuby = 395,
    GemCanopyDiamond = 396,
    WildFiberTuft = 400, WildFrostLichen = 401, WildScrapThistle = 402, WildSeaFennel = 403, WildCanopyPod = 404,
    WildGlowcap = 405, WildPrismBloom = 406, WildBlightBulb = 407,
    // Flora parity (Systems/FloraParity.cs): cuttable grass plants that grow on grass (definitions in ContentFillFlora).
    GrassPlant = 410, TallGrassPlant = 411,
    // Player-placed markers (Batch B; definitions in Systems/PlayerTiles.cs, ids kept in a high band).
    Tombstone = 1000,
    CargoPodStation = 1002,
    KillBanner = 1003,
    // ---- Character growth (band 2000+; Systems/Content/GrowthItems.cs): the post-Fracture lifebloom plant ----
    LifebloomPod = 2000
}

/// <summary>
/// Background wall ids. 0 means no wall. Any non-zero id counts as a wall for rooms and placement
/// backing, exactly as before. 1–99 are reserved for player-built walls; 100 and up are natural
/// walls laid down by world generation (and the derelict hull) so the renderer can pick a texture.
/// </summary>
public static class WallIds
{
    public const ushort None = 0;
    /// <summary>Generic player-built wall (the first player wall id).</summary>
    public const ushort PlayerHull = 1;
    public const ushort FirstNatural = 100;
    /// <summary>Regolith (dirt-layer) wall.</summary>
    public const ushort Regolith = 100;
    /// <summary>Basalt rock wall (rock and cavern layers).</summary>
    public const ushort Rock = 101;
    /// <summary>Frozen Wasteland ice wall.</summary>
    public const ushort Ice = 102;
    /// <summary>Junk Wasteland scrap wall.</summary>
    public const ushort Scrap = 103;
    /// <summary>Spore-grove (fungal) wall.</summary>
    public const ushort Spore = 104;
    /// <summary>Radiated blight wall.</summary>
    public const ushort Radiated = 105;
    /// <summary>Overgrowth blight wall.</summary>
    public const ushort Overgrowth = 106;
    /// <summary>Irradiated Core wall (the id keeps its old name; drawn as wall_irradiated_core).</summary>
    public const ushort Ash = 107;
    /// <summary>Crystal-cave wall.</summary>
    public const ushort Crystal = 108;
    /// <summary>Derelict ship hull backing (structures).</summary>
    public const ushort DerelictHull = 109;
    /// <summary>Packed sand wall (sand pockets, junk dunes).</summary>
    public const ushort Sand = 110;

    // ---- Content expansion: crafted player walls (named after their wall_* sprite) ----
    public const ushort Plank = 2;          // wall_plank
    public const ushort PlatingGrey = 3;    // wall_plating_grey
    public const ushort PlatingBlue = 4;    // wall_plating_blue
    public const ushort Glass = 5;          // wall_glass
    public const ushort BrickFerrite = 6;   // wall_brick_ferrite
    public const ushort BrickAlabaster = 7; // wall_brick_alabaster
    public const ushort Lattice = 8;        // wall_lattice
    public const ushort Circuit = 9;        // wall_circuit
    public const ushort PearlBuilt = 10;    // wall_pearl
    public const ushort HiveBuilt = 11;     // wall_hive
    public const ushort CoralBuilt = 12;    // wall_coral
    public const ushort BasaltBuilt = 13;   // wall_basalt
    public const ushort MudBuilt = 14;      // wall_mud
    public const ushort Panel = 15;         // wall_panel
    public const ushort Window = 16;        // wall_window
    public const ushort Blast = 17;         // wall_blast
    public const ushort Lead = 18;          // wall_lead

    // ---- Content expansion: natural walls laid by the large generator ----
    public const ushort NaturalMud = 111;       // wall_mud
    public const ushort NaturalHive = 112;      // wall_hive
    public const ushort NaturalCoral = 113;     // wall_coral
    public const ushort NaturalBasalt = 114;    // wall_basalt
    public const ushort NaturalPearl = 115;     // wall_pearl
    public const ushort Precursor = 116;        // wall_precursor
    public const ushort NaturalAlabaster = 117; // wall_brick_alabaster
    public const ushort Glowshroom = 118;       // wall_fungal
    // ---- Batch A natural walls ----
    public const ushort MiteSilk = 119;         // wall_silk (mite warrens)
    /// <summary>Precursor Dungeon wall: standing in front of it before the gate boss falls wakes the guardian.</summary>
    public const ushort Dungeon = 120;          // wall_dungeon
    public const ushort LivingWood = 121;       // wall_living_wood (giant canopy trees)
    public const ushort Cabin = 122;            // wall_plank (buried cabins)
    // ---- World generation completion: natural wall variety ----
    public const ushort GrassRoots = 123;       // wall_grass_roots (under grassy surfaces)
    public const ushort CaveLayered = 124;      // wall_cave_layered (rock-layer cave runs)
    public const ushort CaveRough = 125;        // wall_cave_rough (cavern-layer cave runs)
    public const ushort MossyRock = 126;        // wall_mossy_rock (moss caves)

    /// <summary>Sprite name for every content wall id (the renderer maps legacy ids itself).</summary>
    public static string? SpriteName(ushort wallId) => wallId switch
    {
        PlayerHull or PlatingGrey => "wall_plating_grey",
        Plank => "wall_plank",
        PlatingBlue => "wall_plating_blue",
        Glass => "wall_glass",
        BrickFerrite => "wall_brick_ferrite",
        BrickAlabaster or NaturalAlabaster => "wall_brick_alabaster",
        Lattice => "wall_lattice",
        Circuit => "wall_circuit",
        PearlBuilt or NaturalPearl => "wall_pearl",
        HiveBuilt or NaturalHive => "wall_hive",
        CoralBuilt or NaturalCoral => "wall_coral",
        BasaltBuilt or NaturalBasalt => "wall_basalt",
        MudBuilt or NaturalMud => "wall_mud",
        Panel => "wall_panel",
        Window => "wall_window",
        Blast => "wall_blast",
        Lead => "wall_lead",
        Precursor => "wall_precursor",
        Glowshroom => "wall_fungal",
        MiteSilk => "wall_cracked",
        Dungeon => "wall_obsidian",
        LivingWood or Cabin => "wall_plank",
        GrassRoots => "wall_grass_roots",
        CaveLayered => "wall_cave_layered",
        CaveRough => "wall_cave_rough",
        MossyRock => "wall_mossy_rock",
        _ => null
    };

    public static bool IsNatural(ushort wallId) => wallId >= FirstNatural;

    public static string Name(ushort wallId) => wallId switch
    {
        Plank => "Plank wall",
        PlatingGrey => "Grey plating wall",
        PlatingBlue => "Blue plating wall",
        Glass => "Glass wall",
        BrickFerrite => "Ferrite brick wall",
        BrickAlabaster => "Alabaster brick wall",
        Lattice => "Lattice wall",
        Circuit => "Circuit wall",
        PearlBuilt => "Pearl wall",
        HiveBuilt => "Hive wall",
        CoralBuilt => "Coral wall",
        BasaltBuilt => "Basalt wall",
        MudBuilt => "Mud wall",
        Panel => "Panel wall",
        Window => "Window wall",
        Blast => "Blast wall",
        Lead => "Lead-lined wall",
        NaturalMud => "Mud wall",
        NaturalHive => "Hive wall",
        NaturalCoral => "Coral wall",
        NaturalBasalt => "Basalt wall",
        NaturalPearl => "Pearl wall",
        Precursor => "Precursor wall",
        NaturalAlabaster => "Alabaster wall",
        Glowshroom => "Glowshroom wall",
        MiteSilk => "Silk-webbed wall",
        Dungeon => "Precursor dungeon wall",
        LivingWood => "Living wood wall",
        Cabin => "Cabin wall",
        GrassRoots => "Root-laced wall",
        CaveLayered => "Layered rock wall",
        CaveRough => "Rough cavern wall",
        MossyRock => "Mossy rock wall",
        None => "None",
        Regolith => "Regolith wall",
        Rock => "Rock wall",
        Ice => "Ice wall",
        Scrap => "Scrap wall",
        Spore => "Spore wall",
        Radiated => "Radiated wall",
        Overgrowth => "Overgrowth wall",
        Ash => "Irradiated core wall",
        Crystal => "Crystal wall",
        DerelictHull => "Derelict hull wall",
        Sand => "Sand wall",
        _ => wallId < FirstNatural ? "Built wall" : "Natural wall"
    };
}

public enum LiquidKind : byte
{
    None = 0,
    Water = 1,
    Lava = 2,
    /// <summary>Acid sea of the Acid Coast: harms anything standing in it.</summary>
    Acid = 3,
    /// <summary>Nectar pools of the Bloom Hives.</summary>
    Nectar = 4,
    /// <summary>Phase pool liquid: flows like water, transmutes dropped items and phases whoever bathes in it.</summary>
    Phase = 5,
    /// <summary>The fuel seas at both world edges: an amber hydrocarbon that flows, settles and is swum like water.</summary>
    Fuel = 6
}

public enum CraftingStationId : byte
{
    None,
    Workbench,
    Furnace,
    Anvil,
    // Content expansion (append-only).
    FieldWorkbench,
    NanoFabricatorMk2,
    OreRefineryMk2,
    PowerForgeMk2,
    AlloyAnvil,
    VoidForge,
    AstralAssembler,
    ChemLab,
    BioLab,
    Loom,
    Sawmill,
    Kiln,
    CookingStation,
    TinkerBench,
    DroneBay,
    AmmoPress,
    ChargeReactor,
    ResearchTerminal,
    // Player & combat systems pass (explicit, clear of sequential appends).
    DyeVat = 200
}

/// <summary>Player-facing names for crafting stations (enum identifiers stay save-stable).</summary>
public static class CraftingStationNames
{
    public static string DisplayName(this CraftingStationId station) => station switch
    {
        CraftingStationId.Workbench => "Nano-Fabricator",
        CraftingStationId.Furnace => "Ore Refinery",
        CraftingStationId.Anvil => "Power Forge",
        CraftingStationId.FieldWorkbench => "Field Workbench",
        CraftingStationId.NanoFabricatorMk2 => "Nano-Fabricator Mk2",
        CraftingStationId.OreRefineryMk2 => "Ore Refinery Mk2",
        CraftingStationId.PowerForgeMk2 => "Power Forge Mk2",
        CraftingStationId.AlloyAnvil => "Alloy Anvil",
        CraftingStationId.VoidForge => "Void Forge",
        CraftingStationId.AstralAssembler => "Astral Assembler",
        CraftingStationId.ChemLab => "Chem Lab",
        CraftingStationId.BioLab => "Bio Lab",
        CraftingStationId.Loom => "Loom",
        CraftingStationId.Sawmill => "Sawmill",
        CraftingStationId.Kiln => "Kiln",
        CraftingStationId.CookingStation => "Cooking Station",
        CraftingStationId.TinkerBench => "Tinker Bench",
        CraftingStationId.DroneBay => "Drone Bay",
        CraftingStationId.AmmoPress => "Ammo Press",
        CraftingStationId.ChargeReactor => "Charge Reactor",
        CraftingStationId.ResearchTerminal => "Research Terminal",
        CraftingStationId.DyeVat => "Dye Vat",
        _ => "crafting station"
    };

    /// <summary>
    /// Stations that also satisfy simpler ones: an upgraded fabricator still fabricates, every forge
    /// past the first still forges, and the astral assembler covers everything metal.
    /// </summary>
    public static IEnumerable<CraftingStationId> Implied(this CraftingStationId station) => station switch
    {
        CraftingStationId.NanoFabricatorMk2 => [CraftingStationId.Workbench, CraftingStationId.FieldWorkbench],
        CraftingStationId.Workbench => [CraftingStationId.FieldWorkbench],
        CraftingStationId.OreRefineryMk2 => [CraftingStationId.Furnace],
        // Terraria's Glass Kiln counts as a Furnace and its Alchemy Table as a Bottle (Recipe.SetupTileInheritance).
        CraftingStationId.Kiln => [CraftingStationId.Furnace],
        CraftingStationId.BioLab => [CraftingStationId.ChemLab],
        CraftingStationId.PowerForgeMk2 => [CraftingStationId.Anvil],
        CraftingStationId.AlloyAnvil => [CraftingStationId.Anvil, CraftingStationId.PowerForgeMk2],
        CraftingStationId.VoidForge => [CraftingStationId.Furnace, CraftingStationId.OreRefineryMk2],
        CraftingStationId.AstralAssembler => [CraftingStationId.Anvil, CraftingStationId.PowerForgeMk2, CraftingStationId.AlloyAnvil,
            CraftingStationId.VoidForge, CraftingStationId.NanoFabricatorMk2],
        _ => []
    };
}

public readonly record struct GridPoint(int X, int Y);

/// <summary>Per-cell block shape set with the shaping hammer (stored in the cell's spare flag bits).</summary>
public enum TileShape : byte
{
    Full = 0,
    /// <summary>Bottom half only.</summary>
    Half = 1,
    /// <summary>Floor slope rising to the right (solid bottom-right triangle).</summary>
    SlopeUpRight = 2,
    /// <summary>Floor slope rising to the left (solid bottom-left triangle).</summary>
    SlopeUpLeft = 3,
    /// <summary>Ceiling slope: solid top-right triangle.</summary>
    CeilingRight = 4,
    /// <summary>Ceiling slope: solid top-left triangle.</summary>
    CeilingLeft = 5
}

public readonly record struct TileDefinition(
    TileId Id,
    string Name,
    bool IsSolid,
    bool BlocksLiquid,
    byte HitPoints,
    float MiningResistance = 1f,
    ushort FrameGroup = 0,
    CraftingStationId Station = CraftingStationId.None,
    int MinimumToolPower = 0);

public sealed class TileCatalog
{
    private readonly Dictionary<TileId, TileDefinition> _definitions = new()
    {
        [TileId.Air] = new(TileId.Air, "Air", false, false, 0),
        [TileId.Soil] = new(TileId.Soil, "Regolith", true, true, 18, 1f, 1),
        [TileId.Rock] = new(TileId.Rock, "Basalt", true, true, 42, 1.5f, 2),
        [TileId.CopperOre] = new(TileId.CopperOre, "Ferrite ore", true, true, 50, 1.8f, 2),
        [TileId.Grass] = new(TileId.Grass, "Regolith moss", true, true, 20, 1f, 1),
        [TileId.Sand] = new(TileId.Sand, "Silica sand", true, true, 16, 0.8f, 3),
        [TileId.Wood] = new(TileId.Wood, "Hull panel", true, true, 28, 0.7f, 4),
        [TileId.Bedrock] = new(TileId.Bedrock, "Bedrock", true, true, byte.MaxValue, 100f, 5),
        [TileId.Workbench] = new(TileId.Workbench, "Nano-Fabricator", true, true, 32, 1f, 6, CraftingStationId.Workbench),
        [TileId.Furnace] = new(TileId.Furnace, "Ore Refinery", true, true, 48, 1.5f, 7, CraftingStationId.Furnace),
        [TileId.Anvil] = new(TileId.Anvil, "Power Forge", true, true, 40, 1.4f, 8, CraftingStationId.Anvil)
        ,[TileId.Snow] = new(TileId.Snow, "Ice", true, true, 16, 0.8f, 9)
        ,[TileId.DesertSand] = new(TileId.DesertSand, "Dune sand", true, true, 16, 0.8f, 10)
        ,[TileId.JungleGrass] = new(TileId.JungleGrass, "Vine mat", true, true, 22, 1.1f, 11)
        ,[TileId.FungalSoil] = new(TileId.FungalSoil, "Spore mat", true, true, 22, 1.1f, 12)
        ,[TileId.EmberRock] = new(TileId.EmberRock, "Magma rock", true, true, 54, 1.8f, 13)
        ,[TileId.CorruptRock] = new(TileId.CorruptRock, "Void rock", true, true, 48, 1.6f, 14)
        ,[TileId.CrimsonSoil] = new(TileId.CrimsonSoil, "Rift soil", true, true, 28, 1.3f, 15)
        ,[TileId.HallowGrass] = new(TileId.HallowGrass, "Prism moss", true, true, 24, 1.2f, 16)
        ,[TileId.DungeonBrick] = new(TileId.DungeonBrick, "Derelict plating", true, true, 58, 2f, 17)
        // Deep ores need a Ferrite drill; crystals grow on cave walls and shatter easily.
        ,[TileId.CobaltOre] = new(TileId.CobaltOre, "Cobalt ore", true, true, 56, 2f, 2, MinimumToolPower: 35)
        ,[TileId.ViridiumOre] = new(TileId.ViridiumOre, "Viridium ore", true, true, 64, 2.3f, 2, MinimumToolPower: 35)
        ,[TileId.EnergyCrystal] = new(TileId.EnergyCrystal, "Energy crystal", true, true, 30, 1.2f, 2)
        // Colony furniture is non-solid: actors and shots pass through it.
        ,[TileId.MiniTurret] = new(TileId.MiniTurret, "Mini-turret", false, false, 36, 1.2f, 18)
        ,[TileId.HydroponicsTray] = new(TileId.HydroponicsTray, "Hydroponics tray", false, false, 24, 0.9f, 19)
        ,[TileId.SolarPanel] = new(TileId.SolarPanel, "Solar panel", false, false, 28, 1f, 20)
        ,[TileId.OxygenUnit] = new(TileId.OxygenUnit, "Oxygen unit", false, false, 32, 1.1f, 21)
        // Biome ores: scrap iron is a starter ore on the junk surface; the hazard-biome ores need a Ferrite drill.
        ,[TileId.GlacialOre] = new(TileId.GlacialOre, "Glacial ore", true, true, 52, 1.9f, 2, MinimumToolPower: 35)
        ,[TileId.UraniumOre] = new(TileId.UraniumOre, "Uranium ore", true, true, 60, 2.1f, 2, MinimumToolPower: 35)
        ,[TileId.PyrociteOre] = new(TileId.PyrociteOre, "Pyrocite ore", true, true, 68, 2.4f, 2, MinimumToolPower: 35)
        ,[TileId.ScrapIronOre] = new(TileId.ScrapIronOre, "Scrap-iron ore", true, true, 40, 1.4f, 2)
        // Blight ground (spreads; purifiers turn it back).
        ,[TileId.RadiatedSand] = new(TileId.RadiatedSand, "Irradiated sand", true, true, 16, 0.8f, 3)
        ,[TileId.RadiatedIce] = new(TileId.RadiatedIce, "Glowing ice", true, true, 16, 0.8f, 9)
        ,[TileId.OvergrowthSoil] = new(TileId.OvergrowthSoil, "Overgrown soil", true, true, 24, 1.1f, 1)
        ,[TileId.OvergrowthRock] = new(TileId.OvergrowthRock, "Mossy rock", true, true, 46, 1.5f, 2)
        ,[TileId.OvergrowthSand] = new(TileId.OvergrowthSand, "Root-choked sand", true, true, 18, 0.9f, 3)
        ,[TileId.OvergrowthIce] = new(TileId.OvergrowthIce, "Vine-veined ice", true, true, 18, 0.9f, 9)
        // Hearts break with any drill; altars refuse every drill until the Fracture.
        ,[TileId.ReactorCore] = new(TileId.ReactorCore, "Reactor core", true, true, 60, 1f, 22)
        ,[TileId.SporeHeart] = new(TileId.SporeHeart, "Spore heart", true, true, 60, 1f, 23)
        ,[TileId.RadAltar] = new(TileId.RadAltar, "Rad altar", true, true, 90, 1.4f, 24)
        ,[TileId.RootAltar] = new(TileId.RootAltar, "Root altar", true, true, 90, 1.4f, 25)
        // Loot and furniture are non-solid except the buried cache.
        ,[TileId.LifeCore] = new(TileId.LifeCore, "Life core", false, false, 24, 1f, 26)
        ,[TileId.SupplyCache] = new(TileId.SupplyCache, "Supply cache", true, true, 30, 1f, 27)
        ,[TileId.SalvagePod] = new(TileId.SalvagePod, "Salvage pod", false, false, 10, 0.6f, 28)
        ,[TileId.PurifierBeacon] = new(TileId.PurifierBeacon, "Purifier beacon", false, false, 30, 1f, 29)
        ,[TileId.Ash] = new(TileId.Ash, "Ash", true, true, 20, 0.9f, 30)
        ,[TileId.Obsidian] = new(TileId.Obsidian, "Obsidian", true, true, 70, 2.4f, 31, MinimumToolPower: 35)
        ,[TileId.ShipDoor] = new(TileId.ShipDoor, "Hatch door", false, false, 30, 1f, 32)
    };
    // Dense mirror of the dictionary so hot paths (liquids, rooms, collisions) index by id.
    private TileDefinition?[] _dense = Array.Empty<TileDefinition?>();

    public TileCatalog()
    {
        foreach (var definition in ContentTiles.Definitions)
            _definitions[definition.Id] = new TileDefinition(definition.Id, definition.Name, definition.Solid, definition.Solid,
                definition.HitPoints, definition.Resistance, definition.FrameGroup, definition.Station, definition.MinimumToolPower);
        foreach (var definition in Rimtera.Systems.PlayerTiles.Definitions)
            _definitions[definition.Id] = definition;
        foreach (var definition in Rimtera.Systems.LeisureContent.TileDefinitions) _definitions[definition.Id] = definition;
        foreach (var definition in Rimtera.Systems.TravelContent.TileDefinitions) _definitions[definition.Id] = definition;
        foreach (var definition in _definitions.Values)
            StoreDense(definition);
    }

    private void StoreDense(TileDefinition definition)
    {
        var index = (int)definition.Id;
        if (index >= _dense.Length)
            Array.Resize(ref _dense, Math.Max(index + 1, _dense.Length * 2));
        _dense[index] = definition;
    }

    public bool Contains(TileId id) => _definitions.ContainsKey(id);

    public TileDefinition this[TileId id]
    {
        get
        {
            var index = (int)id;
            if ((uint)index < (uint)_dense.Length && _dense[index] is { } definition)
                return definition;
            throw new KeyNotFoundException($"No tile definition is registered for {id}.");
        }
    }

    public void Register(TileDefinition definition)
    {
        _definitions[definition.Id] = definition;
        StoreDense(definition);
    }
}

/// <summary>
/// One world cell packed into 8 bytes: tile (u16), wall (u16), liquid amount (u8, 0–<see cref="LiquidUnitsMax"/>),
/// liquid kind (low 3 bits) plus 5 spare flag bits (u8), damage (u8) and the 8-neighbour frame mask (u8).
/// Frame atlas coordinates are derived from the mask, never stored. Liquid depth is exposed as a float
/// in [0, 1] mapped onto the byte, so callers keep the float API; amounts round to 1/240.
/// </summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
public struct WorldCell
{
    /// <summary>Liquid units in a full cell; 240 divides evenly by 2, 3, 4, 5, 6, 8, 10, 12, 15 and 16.</summary>
    public const int LiquidUnitsMax = 240;
    private const float UnitsToDepth = 1f / LiquidUnitsMax;
    private const byte KindMask = 0x07;

    private ushort _tile;
    private ushort _wall;
    private byte _liquid;
    private byte _kindFlags;
    private byte _damage;
    private byte _mask;

    public TileId TileId { readonly get => (TileId)_tile; set => _tile = (ushort)value; }
    public ushort WallId { readonly get => _wall; set => _wall = value; }

    /// <summary>Liquid depth in [0, 1], stored as <see cref="LiquidUnits"/>/240 (writes round to the nearest unit).</summary>
    public float LiquidDepth
    {
        readonly get => _liquid * UnitsToDepth;
        set => _liquid = DepthToUnits(value);
    }

    /// <summary>Raw liquid amount, 0–<see cref="LiquidUnitsMax"/>.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public byte LiquidUnits { readonly get => _liquid; set => _liquid = Math.Min(value, (byte)LiquidUnitsMax); }

    public LiquidKind LiquidKind
    {
        readonly get => (LiquidKind)(_kindFlags & KindMask);
        set => _kindFlags = (byte)((_kindFlags & ~KindMask) | ((byte)value & KindMask));
    }

    /// <summary>Five spare bits for paint or flags (unused by the engine, saved with the cell).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public byte Flags { readonly get => (byte)(_kindFlags >> 3); set => _kindFlags = (byte)((_kindFlags & KindMask) | ((value & 0x1F) << 3)); }

    /// <summary>
    /// Block shape (low three flag bits) for solid blocks and platforms: full, half, or one of four slopes.
    /// Stateful props (herbs, lamps, timers, gates, levers, traps) reuse the same bits as <see cref="State"/>.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public TileShape Shape { readonly get => (TileShape)(Flags & 0x07); set => Flags = (byte)((Flags & ~0x07) | ((byte)value & 0x07)); }

    /// <summary>Three state bits for stateful props (shares storage with <see cref="Shape"/>).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public byte State { readonly get => (byte)(Flags & 0x07); set => Flags = (byte)((Flags & ~0x07) | (value & 0x07)); }

    /// <summary>An actuator is fitted to this cell (wire pulses toggle <see cref="IsInactive"/>).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasActuator { readonly get => (Flags & 0x08) != 0; set => Flags = (byte)(value ? Flags | 0x08 : Flags & ~0x08); }

    /// <summary>Actuated off: the block stays in place but actors and falling blocks pass through it.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsInactive { readonly get => (Flags & 0x10) != 0; set => Flags = (byte)(value ? Flags | 0x10 : Flags & ~0x10); }

    public byte Damage { readonly get => _damage; set => _damage = value; }
    public byte FrameMask { readonly get => _mask; set => _mask = value; }

    /// <summary>Atlas x of the frame, derived from the mask's low nibble (writes set that nibble).</summary>
    public ushort FrameX
    {
        readonly get => (ushort)((_mask & 0x0F) * TileFramingSystem.FrameWidth);
        set => _mask = (byte)((_mask & 0xF0) | ((value / TileFramingSystem.FrameWidth) & 0x0F));
    }

    /// <summary>Atlas y of the frame, derived from the mask's high nibble (writes set that nibble).</summary>
    public ushort FrameY
    {
        readonly get => (ushort)(((_mask >> 4) & 0x0F) * TileFramingSystem.FrameHeight);
        set => _mask = (byte)((_mask & 0x0F) | (((value / TileFramingSystem.FrameHeight) & 0x0F) << 4));
    }

    /// <summary>The raw 8 bytes as one value (run-length comparisons, codecs).</summary>
    internal readonly ulong Packed => System.Runtime.CompilerServices.Unsafe.As<WorldCell, ulong>(ref System.Runtime.CompilerServices.Unsafe.AsRef(in this));

    internal static WorldCell FromPacked(ulong packed) => System.Runtime.CompilerServices.Unsafe.As<ulong, WorldCell>(ref packed);

    /// <summary>Maps a float depth to liquid units (NaN and negatives become 0, values above 1 clamp).</summary>
    public static byte DepthToUnits(float depth) =>
        !(depth > 0f) ? (byte)0 : depth >= 1f ? (byte)LiquidUnitsMax : (byte)MathF.Round(depth * LiquidUnitsMax);
}

/// <summary>Raised after a cell is written (tile, wall, liquid or framing); listeners filter what they need.</summary>
public delegate void CellChangedHandler(int x, int y, in WorldCell before, in WorldCell after);

/// <summary>A compact, row-major tile map. Per-cell state stays in one contiguous array of 8-byte cells.</summary>
public sealed class TileGrid
{
    public const float MaximumLiquidDepth = 1f;
    private const float LiquidEpsilon = 0.0001f;

    private readonly WorldCell[] _cells;
    private long _tileRevision;

    public TileGrid(int width, int height)
    {
        if (width < 1 || height < 1)
            throw new ArgumentOutOfRangeException(nameof(width), "Grid dimensions must be positive.");

        Width = width;
        Height = height;
        _cells = new WorldCell[checked(width * height)];
    }

    public int Width { get; }
    public int Height { get; }
    public int Count => _cells.Length;

    /// <summary>Bytes held by the cell array (8 per cell).</summary>
    public long CellBytes => (long)_cells.Length * System.Runtime.CompilerServices.Unsafe.SizeOf<WorldCell>();

    /// <summary>
    /// Bumped whenever a cell's tile or wall changes (not on liquid-only or framing updates),
    /// so derived maps such as rooms can rebuild lazily.
    /// </summary>
    public long TileRevision => Interlocked.Read(ref _tileRevision);

    /// <summary>
    /// Change notifications for engine systems (active liquids, dirty rooms, beacons). Bulk loads
    /// through <see cref="LoadCells"/> and bulk reframes do not raise it.
    /// </summary>
    public event CellChangedHandler? CellChanged;

    /// <summary>True while anything listens to <see cref="CellChanged"/>.</summary>
    internal bool HasCellListeners => CellChanged is not null;

    /// <summary>Row-major read-only view of every cell (saves, compression, bulk scans).</summary>
    public ReadOnlySpan<WorldCell> Cells => _cells;

    /// <summary>Writable view for bulk engine passes (generation, reframing); raises no events.</summary>
    internal Span<WorldCell> MutableCells => _cells;

    /// <summary>The backing array for parallel bulk passes (generation, reframing); raises no events.</summary>
    internal WorldCell[] RawCells => _cells;

    public bool Contains(GridPoint point) =>
        (uint)point.X < (uint)Width && (uint)point.Y < (uint)Height;

    public bool Contains(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    public WorldCell GetCell(GridPoint point) => _cells[Index(point)];

    public WorldCell GetCell(int x, int y) => _cells[Index(new GridPoint(x, y))];

    /// <summary>Tile id at a cell, or <paramref name="outside"/> beyond the grid.</summary>
    public TileId TileAt(int x, int y, TileId outside = TileId.Bedrock) =>
        (uint)x < (uint)Width && (uint)y < (uint)Height ? _cells[y * Width + x].TileId : outside;

    public void SetCell(GridPoint point, WorldCell cell)
    {
        var index = Index(point);
        var previous = _cells[index];
        if (previous.TileId != cell.TileId || previous.WallId != cell.WallId)
            BumpRevision();
        _cells[index] = cell;
        CellChanged?.Invoke(point.X, point.Y, previous, cell);
    }

    public void SetTile(GridPoint point, TileId tileId)
    {
        var index = Index(point);
        var cell = _cells[index];
        var previous = cell;
        if (cell.TileId != tileId)
        {
            BumpRevision();
            cell.Flags = TilePhysics.CarryFlags(cell.TileId, tileId, cell.Flags);
        }
        cell.TileId = tileId;
        cell.Damage = 0;
        cell.FrameMask = 0;
        _cells[index] = cell;
        CellChanged?.Invoke(point.X, point.Y, previous, cell);
    }

    /// <summary>Sets the background wall id (0 removes it).</summary>
    public void SetWall(GridPoint point, ushort wallId)
    {
        var index = Index(point);
        var cell = _cells[index];
        if (cell.WallId == wallId)
            return;
        var previous = cell;
        cell.WallId = wallId;
        BumpRevision();
        _cells[index] = cell;
        CellChanged?.Invoke(point.X, point.Y, previous, cell);
    }

    /// <summary>Replaces every cell at once (save loading). Raises no per-cell events.</summary>
    public void LoadCells(ReadOnlySpan<WorldCell> cells)
    {
        if (cells.Length != _cells.Length)
            throw new ArgumentException("Cell count does not match the grid.", nameof(cells));
        cells.CopyTo(_cells);
        BumpRevision();
    }

    /// <summary>Marks a bulk edit made through <see cref="MutableCells"/> (forces derived maps to rebuild).</summary>
    internal void MarkBulkEdit() => BumpRevision();

    private void BumpRevision() => Interlocked.Increment(ref _tileRevision);

    public bool TryDamageTile(GridPoint point, byte damage, TileCatalog catalog, out TileId brokenTile)
    {
        var index = Index(point);
        var cell = _cells[index];
        var tile = catalog[cell.TileId];
        brokenTile = TileId.Air;

        if (damage == 0 || tile.HitPoints == 0)
            return false;

        var remainingHitPoints = Math.Max(0, tile.HitPoints - cell.Damage);
        if (damage >= remainingHitPoints)
        {
            var previous = cell;
            brokenTile = cell.TileId;
            BumpRevision();
            cell.Flags = 0;
            cell.TileId = TileId.Air;
            cell.Damage = 0;
            cell.FrameMask = 0;
            _cells[index] = cell;
            CellChanged?.Invoke(point.X, point.Y, previous, cell);
            return true;
        }

        cell.Damage = (byte)(cell.Damage + damage);
        _cells[index] = cell;
        return false;
    }

    public bool TryAddLiquid(GridPoint point, LiquidKind kind, float depth, TileCatalog catalog)
    {
        if (!Contains(point) || kind == LiquidKind.None || !float.IsFinite(depth) || depth <= LiquidEpsilon)
            return false;

        var cell = GetCell(point);
        if (catalog[cell.TileId].BlocksLiquid ||
            (cell.LiquidUnits > 0 && cell.LiquidKind != kind))
            return false;

        var nextUnits = Math.Min(WorldCell.LiquidUnitsMax, cell.LiquidUnits + WorldCell.DepthToUnits(depth));
        if (nextUnits <= cell.LiquidUnits)
            return false;

        cell.LiquidUnits = (byte)nextUnits;
        cell.LiquidKind = kind;
        SetCell(point, cell);
        return true;
    }

    /// <summary>
    /// Moves up to <paramref name="requested"/> depth from one cell to another (rounded to whole liquid
    /// units, so volume is conserved exactly). Returns the depth moved.
    /// </summary>
    public float TransferLiquid(GridPoint source, GridPoint destination, float requested, TileCatalog catalog)
    {
        if (!float.IsFinite(requested) || requested <= LiquidEpsilon || !Contains(source) || !Contains(destination))
            return 0;

        var from = GetCell(source);
        var to = GetCell(destination);
        if (from.LiquidUnits == 0 || catalog[to.TileId].BlocksLiquid ||
            (to.LiquidUnits > 0 && to.LiquidKind != from.LiquidKind))
            return 0;

        var moved = Math.Min(WorldCell.DepthToUnits(requested), Math.Min(from.LiquidUnits, WorldCell.LiquidUnitsMax - to.LiquidUnits));
        if (moved <= 0)
            return 0;

        var kind = from.LiquidKind;
        from.LiquidUnits = (byte)(from.LiquidUnits - moved);
        if (from.LiquidUnits == 0)
            from.LiquidKind = LiquidKind.None;
        to.LiquidUnits = (byte)(to.LiquidUnits + moved);
        to.LiquidKind = kind;
        SetCell(source, from);
        SetCell(destination, to);
        return moved / (float)WorldCell.LiquidUnitsMax;
    }

    public double TotalLiquidDepth()
    {
        long total = 0;
        foreach (var cell in _cells)
            total += cell.LiquidUnits;
        return total / (double)WorldCell.LiquidUnitsMax;
    }

    public int CountTiles(TileId tileId)
    {
        var count = 0;
        foreach (var cell in _cells)
            if (cell.TileId == tileId)
                count++;
        return count;
    }

    public int CountLiquidCells()
    {
        var count = 0;
        foreach (var cell in _cells)
            if (cell.LiquidUnits > 0)
                count++;
        return count;
    }

    private int Index(GridPoint point)
    {
        if (!Contains(point))
            throw new ArgumentOutOfRangeException(nameof(point), $"Point {point} is outside the grid.");
        return point.Y * Width + point.X;
    }
}
