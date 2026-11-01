namespace Rimtera.World;

/// <summary>What a density is measured against.</summary>
public enum DensityBasis : byte
{
    /// <summary>Per million cells: underground and volume features.</summary>
    Area,
    /// <summary>Per thousand columns: surface and horizontal-band features.</summary>
    Width
}

/// <summary>
/// One pass setting: how many of a feature per unit of world, with a floor so small worlds still get one and a
/// cap only where the feature would crowd itself.
/// </summary>
public readonly record struct Density(double Per, DensityBasis Basis, int Minimum = 0, int Maximum = int.MaxValue)
{
    public double Raw(int width, int height) =>
        Basis == DensityBasis.Area ? Per * width * (double)height / 1_000_000.0 : Per * width / 1000.0;

    public int Count(int width, int height) => Math.Clamp((int)Math.Round(Raw(width, height)), Minimum, Maximum);

    /// <summary>A per-cell or per-column rate rather than a count (for scatter passes that test every cell).</summary>
    public double Rate => Basis == DensityBasis.Area ? Per / 1_000_000.0 : Per / 1000.0;
}

/// <summary>
/// Rimtera's world-generation density table: every count in every pass of <see cref="LargeWorldGenerator"/> comes
/// from here, scaled by area (underground, per million cells) or width (surface, per thousand columns). World sizes
/// for reference: Tiny 1750×900 (1.6 M cells), Small 4200×1200 (5 M), Medium 6400×1800 (11.5 M), Large 8400×2400
/// (20 M), so area features grow about 13× from Tiny to Large and width features about 4.8×. All values original.
/// </summary>
public static class WorldGenDensity
{
    private static Density A(double per, int min = 0, int max = int.MaxValue) => new(per, DensityBasis.Area, min, max);
    private static Density W(double per, int min = 0, int max = int.MaxValue) => new(per, DensityBasis.Width, min, max);

    // ---- Terrain shaping (surface) ----
    public static readonly Density Mountains = W(0.9, 1);
    public static readonly Density Valleys = W(0.8, 1);
    public static readonly Density MountainCaves = W(2.0, 1);
    /// <summary>Surface caves (0.3.0), per thousand columns: narrow chutes, wide mouths, long descents, level galleries.
    /// A little above the Terraria-style reference, for more natural openings.</summary>
    public static readonly Density CaveMouths = W(4.5, 2);
    public static readonly Density WideCaveMouths = W(1.5, 1);
    public static readonly Density DeepCaveMouths = W(0.45, 1);
    public static readonly Density ShallowGalleries = W(0.5, 1);
    public static readonly Density SurfacePockets = W(6, 2);
    public static readonly Density ScrapDunes = W(4, 1);
    /// <summary>Fuel-sea width at each world edge in columns per thousand columns (Medium ≈ 350, Large ≈ 460).</summary>
    public static readonly Density CoastWidth = W(55, 24);
    /// <summary>Coastal shelf inland of each sea where the land eases down to the shore (Medium ≈ 260 columns).</summary>
    public static readonly Density CoastShelf = W(40, 16);

    // ---- Soil, rock and caves (underground) ----
    public static readonly Density RocksInSoil = A(714, 6);
    public static readonly Density SoilInRock = A(625, 6);
    public static readonly Density ClayGravelSlate = A(212, 4);
    public static readonly Density SandAndScrap = A(222, 3);
    /// <summary>Pairs of pocks (0.3.0): one in the surface band, one larger below it.</summary>
    public static readonly Density SmallHoles = A(1000, 8);
    /// <summary>Long caves through the soil layer (0.3.0 runners: 5–15 wide, 30–200 steps).</summary>
    public static readonly Density ShallowCaves = A(14, 4);
    /// <summary>Long caverns through the rock (0.3.0 runners: 6–20 wide, 50–300 steps).</summary>
    public static readonly Density Caverns = A(80, 5);
    public static readonly Density Tunnels = A(4, 2);
    public static readonly Density SiltPockets = A(45, 2);
    public static readonly Density SlushPockets = A(40, 2);
    public static readonly Density FrozenPockets = A(2000, 3);
    public static readonly Density JunkHeaps = A(2000, 3);
    public static readonly Density CaveWallRuns = A(40, 4);

    // ---- Biomes ----
    public static readonly Density SporeGroves = A(3, 1);
    public static readonly Density FungalHollows = A(2, 1);
    public static readonly Density Canopies = W(0.35, 1);
    /// <summary>Extra bloom hives per million cells of canopy column.</summary>
    public static readonly Density HivesPerCanopyArea = A(8, 1);
    /// <summary>Liquid-uranium lakes sunk into the Irradiated Core floor.</summary>
    public static readonly Density LavaLakes = W(10, 2);
    public static readonly Density ObsidianBlobs = W(25, 2);
    public static readonly Density BlightZones = W(1.0, 2);
    public static readonly Density Altars = A(4, 3);
    public static readonly Density DeepBlightPockets = A(12, 2);
    public static readonly Density TideGrottosPerCoast = W(0.5, 1);

    // ---- Resources ----
    public static readonly Density CopperVeins = A(1290, 6);
    public static readonly Density CobaltVeins = A(667, 3);
    public static readonly Density ViridiumVeins = A(385, 2);
    public static readonly Density FrozenOreBlobs = A(625, 3);
    public static readonly Density JunkOreBlobs = A(625, 3);
    public static readonly Density PyrociteBlobs = W(48, 2);
    /// <summary>Glowing uranium veins in the Irradiated Core.</summary>
    public static readonly Density CoreUraniumVeins = W(30, 2);
    public static readonly Density CrystalCaves = A(9, 2);
    public static readonly Density CrystalClusters = A(166, 3);
    public static readonly Density GemVeins = A(110, 4);
    public static readonly Density ExposedGems = A(260, 4);
    public static readonly Density LifeCores = A(10, 10);
    public static readonly Density SupplyCaches = A(14, 3);
    public static readonly Density SalvagePods = A(30, 4);

    // ---- Mini-biomes and structures ----
    public static readonly Density GeodeChambers = A(6, 2);
    public static readonly Density BasaltColumns = A(3, 1);
    public static readonly Density AlabasterHalls = A(3, 1);
    public static readonly Density MiteWarrens = A(5, 1);
    public static readonly Density MossSpecks = A(11000, 50);
    public static readonly Density MossCaves = A(4, 1);
    public static readonly Density SkyIslands = W(1.5, 1);
    public static readonly Density BuriedLanders = W(0.6, 1);
    public static readonly Density CoolantSprings = W(1.2, 1);
    public static readonly Density Survivors = A(3, 2, 32);
    public static readonly Density Cabins = A(8, 2);
    public static readonly Density GiantTrees = W(1.0, 1);
    public static readonly Density WeaponShrines = A(1.5, 1);
    public static readonly Density CanopyShrines = W(1.2, 1);
    public static readonly Density SlagFoundries = A(0.6, 1);
    public static readonly Density UndergroundLakes = A(6, 1);
    public static readonly Density Campsites = A(2, 1);
    public static readonly Density RiggedChests = A(3, 1);
    public static readonly Density MiningCaches = A(3, 1);
    public static readonly Density Statues = A(10, 2);
    public static readonly Density SurfaceCrates = W(1.5, 1);
    public static readonly Density CoastChests = W(0.5, 1);

    // ---- Traps ----
    public static readonly Density BoulderTraps = A(12, 1);
    public static readonly Density DartTraps = A(14, 1);
    public static readonly Density ExplosiveTraps = A(6, 1);
    public static readonly Density SpikePits = A(8, 1);

    // ---- Water ----
    public static readonly Density SurfaceLakes = W(2.5, 1);
    public static readonly Density SurfacePonds = W(4, 1);
    public static readonly Density CavePools = A(12, 3);
    public static readonly Density WaterSeeps = A(26, 2);
    public static readonly Density LavaSeeps = A(8, 1);
    /// <summary>Liquid-uranium falls pouring inside the Irradiated Core.</summary>
    public static readonly Density UraniumFalls = W(5, 2);

    // ---- Decoration (scatter attempts) ----
    public static readonly Density Herbs = A(6250, 60);
    public static readonly Density Vines = A(3750, 30);
    public static readonly Density Pots = A(450, 20);
    /// <summary>
    /// Loose chests on cave floors (0.3.1): Terraria's underground and cavern layers hold far more chests than its
    /// cabins alone, most of them single chests sitting in the caves.
    /// </summary>
    public static readonly Density CaveChests = A(12, 6);
    /// <summary>Extra chests in the Irradiated Core (0.3.1; Terraria's underworld ruins hold several).</summary>
    public static readonly Density CoreChests = A(1.0, 3);
    /// <summary>Extra chests along the precursor dungeon's halls (0.3.1; Terraria's dungeon holds a couple of dozen).</summary>
    public static readonly Density DungeonChests = W(3.0, 4);
    public static readonly Density Speleothems = A(5200, 40);
    public static readonly Density CrystalShrubs = A(22, 1);
    public static readonly Density RubblePiles = A(300, 8);
    public static readonly Density DyeBlossoms = A(45, 2);
    public static readonly Density WildMushrooms = A(160, 4);
    public static readonly Density FallenLogs = W(12, 2);
    public static readonly Density SolarLilyPatches = W(5, 1);
    public static readonly Density WildGourds = W(6, 1);
    public static readonly Density SurfaceOrePatches = W(9, 2);
    public static readonly Density Graveyards = W(0.8, 1);
    public static readonly Density ShellPilesPerCoast = W(6, 2);

    /// <summary>Every setting by name, for the bench print and tests.</summary>
    public static IEnumerable<(string Name, Density Density)> All =>
        typeof(WorldGenDensity).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(info => info.FieldType == typeof(Density))
            .Select(info => (info.Name, (Density)info.GetValue(null)!));
}
