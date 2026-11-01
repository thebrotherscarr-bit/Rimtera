using Rimtera.Systems;

namespace Rimtera.World;

/// <summary>A generation stage built from a delegate (used by the detailed generator).</summary>
public sealed class DelegateGenerationPass : IWorldGenerationPass
{
    private readonly Action<TileGrid, TileCatalog, Random, CancellationToken> _apply;

    public DelegateGenerationPass(string name, double weight, Action<TileGrid, TileCatalog, Random, CancellationToken> apply)
    {
        Name = name;
        Weight = weight;
        _apply = apply;
    }

    public string Name { get; }
    public double Weight { get; }
    public void Apply(TileGrid grid, TileCatalog catalog, Random random, CancellationToken cancellationToken) =>
        _apply(grid, catalog, random, cancellationToken);
}

/// <summary>One surface blight zone: the column span it converts and how deep it reaches.</summary>
public readonly record struct BlightZone(int Left, int Right, int Depth);

/// <summary>A small floating island and the hut built on it.</summary>
public readonly record struct SkyIsland(int Left, int Right, int Top);

/// <summary>What the detailed generator placed, for the simulation, renderer and tests.</summary>
public sealed class WorldFeatures
{
    public WorldBlight Blight { get; internal set; }
    public List<BlightZone> BlightZones { get; } = new();
    /// <summary>Top-left cells of the 2×2 blight hearts.</summary>
    public List<GridPoint> Hearts { get; } = new();
    /// <summary>Top-left cells of the 2×2 blight altars.</summary>
    public List<GridPoint> Altars { get; } = new();
    public List<GridPoint> LifeCores { get; } = new();
    public List<GridPoint> SupplyCaches { get; } = new();
    public List<GridPoint> SalvagePods { get; } = new();
    public List<SkyIsland> Islands { get; } = new();
    /// <summary>Bounding box of the derelict crash ship (left, top, right, bottom), or null.</summary>
    public (int Left, int Top, int Right, int Bottom)? Derelict { get; internal set; }
    public int SpawnColumn { get; internal set; }
    /// <summary>Content expansion: biome regions and structures.</summary>
    public List<(int Left, int Right)> AcidCoasts { get; } = new();
    public List<(int Left, int Top, int Right, int Bottom)> FungalHollows { get; } = new();
    public List<(int Left, int Right)> Canopies { get; } = new();
    public List<(int Left, int Top, int Right, int Bottom)> BloomHives { get; } = new();
    public List<(int Left, int Top, int Right, int Bottom)> GeodeChambers { get; } = new();
    public List<(int Left, int Top, int Right, int Bottom)> BasaltColumns { get; } = new();
    public List<(int Left, int Top, int Right, int Bottom)> AlabasterHalls { get; } = new();
    public List<(int Left, int Top, int Right, int Bottom)> MiteWarrens { get; } = new();
    /// <summary>Bounding box of the precursor vault, or null.</summary>
    public (int Left, int Top, int Right, int Bottom)? Vault { get; internal set; }
    /// <summary>Cells where bound survivors wait inside structures.</summary>
    public List<GridPoint> SurvivorSpots { get; } = new();
    // ---- Batch A structures (not persisted; tests and first-run setup read them) ----
    public List<(int Left, int Top, int Right, int Bottom)> Cabins { get; } = new();
    public (int Left, int Top, int Right, int Bottom)? Dungeon { get; internal set; }
    public List<(int Left, int Top, int Right, int Bottom)> GiantTrees { get; } = new();
    public List<GridPoint> Shrines { get; } = new();
    public int Pots { get; internal set; }
    public int Trees { get; internal set; }
    public int Herbs { get; internal set; }
    /// <summary>Trap wiring laid at generation (cell, layer); the simulation installs it on a fresh world.</summary>
    public List<(GridPoint Cell, int Layer)> TrapWires { get; } = new();
    public int CavernRow { get; internal set; }
    /// <summary>Pass name → milliseconds, filled when generation is timed.</summary>
    public Dictionary<string, double> PassMilliseconds { get; } = new();

    // ---- World generation completion (saved through WorldMetaCodec's Sites, Counts and Chests) ----
    /// <summary>Named feature sites (bounding boxes; single cells are one-cell boxes). Keys are <see cref="FeatureNames"/>.</summary>
    public Dictionary<string, List<(int Left, int Top, int Right, int Bottom)>> Sites { get; } = new();
    /// <summary>Named tallies of scattered features (gems, speleothems, piles, plants, sloped edges...).</summary>
    public Dictionary<string, int> Counts { get; } = new();
    /// <summary>Every chest placed by generation with its depth tier (0 surface or sky, 1 underground, 2 cavern, 3 molten).</summary>
    public List<(GridPoint Cell, int Tier)> Chests { get; } = new();

    public IReadOnlyList<(int Left, int Top, int Right, int Bottom)> SitesOf(string name) =>
        Sites.TryGetValue(name, out var list) ? list : Array.Empty<(int, int, int, int)>();

    public int CountOf(string name) => Counts.GetValueOrDefault(name);

    internal void AddSite(string name, int left, int top, int right, int bottom)
    {
        if (!Sites.TryGetValue(name, out var list))
            Sites[name] = list = new();
        list.Add((left, top, right, bottom));
    }

    internal void AddSite(string name, GridPoint cell) => AddSite(name, cell.X, cell.Y, cell.X, cell.Y);

    internal void AddCount(string name, int amount = 1) => Counts[name] = Counts.GetValueOrDefault(name) + amount;

    private Dictionary<GridPoint, int>? _chestTiers;

    /// <summary>The depth tier recorded for a generated chest, or -1.</summary>
    public int ChestTier(GridPoint cell)
    {
        if (_chestTiers is null || _chestTiers.Count != Chests.Count)
        {
            _chestTiers = new Dictionary<GridPoint, int>(Chests.Count);
            foreach (var (point, tier) in Chests)
                _chestTiers[point] = tier;
        }
        return _chestTiers.GetValueOrDefault(cell, -1);
    }
}

/// <summary>Keys for <see cref="WorldFeatures.Sites"/> and <see cref="WorldFeatures.Counts"/>.</summary>
public static class FeatureNames
{
    public const string ScrapDunes = "scrap-dunes", BuriedLanders = "buried-landers", CoolantSprings = "coolant-springs";
    public const string ScrapHive = "scrap-hive", BurrowerNests = "burrower-nests", TideGrottos = "tide-grottos";
    public const string PhasePool = "phase-pool", MountainCaves = "mountain-caves", SurfacePockets = "surface-pockets", FloodedPockets = "flooded-pockets";
    public const string UndergroundLakes = "underground-lakes", SiltPockets = "silt-pockets", SlushPockets = "slush-pockets";
    public const string GemVeins = "gem-veins", ExposedGems = "exposed-gems", MossCaves = "moss-caves", LongMoss = "long-moss";
    public const string Speleothems = "speleothems", CrystalShrubs = "crystal-shrubs", RubblePiles = "rubble-piles";
    public const string ShellPiles = "shell-piles", FallenLogs = "fallen-logs", WaterPlants = "water-plants", CoralFans = "coral-fans";
    public const string Graveyards = "graveyards", BoulderTraps = "boulder-traps", DartTraps = "dart-traps";
    public const string ExplosiveTraps = "explosive-traps", SpikePits = "spike-pits", SlagFoundries = "slag-foundries";
    public const string IslandHouses = "island-houses", SurfaceOre = "surface-ore", BroodPods = "brood-pods";
    public const string SolarLilies = "solar-lilies", DyeBlossoms = "dye-blossoms", Campsites = "campsites";
    public const string RiggedChests = "rigged-chests", MiningCaches = "mining-caches", Statues = "statues", WiredStatues = "wired-statues";
    public const string CanopyShrines = "canopy-shrines", TempleRooms = "temple-rooms", TempleAltar = "temple-altar";
    public const string UndergroundCanopy = "underground-canopy", UndergroundCanopyTrees = "underground-canopy-trees";
    public const string CanopyPools = "canopy-pools", SurfacePonds = "surface-ponds", DrainedDuneWater = "drained-dune-water";
    public const string WaterSeeps = "water-seeps", LavaSeeps = "lava-seeps", SlopedEdges = "sloped-edges", HalfBlocks = "half-blocks";
    public const string SettledSand = "settled-sand", ClearedSkyWalls = "cleared-sky-walls", CaveWallRuns = "cave-wall-runs";
    public const string EnclosedWalls = "enclosed-walls", GrassWalls = "grass-walls", CooledSurfaceLava = "cooled-surface-lava";
    public const string SurfaceCrates = "surface-crates", SunkenChests = "sunken-chests", ImprovedChests = "improved-chests";
    public const string WildGourds = "wild-gourds", WildMushrooms = "wild-mushrooms", ExtraHives = "extra-hives";
    public const string UraniumFalls = "uranium-falls", UraniumLakes = "uranium-lakes", Oceans = "oceans";
}

/// <summary>
/// Rimtera's detailed world generator (~30 ordered stages) for the larger world sizes and for any
/// world created with a blight. All shapes, counts and tunings are original; the only borrowed
/// idea is the ordered, weighted pass structure and a blob-walker primitive for features.
/// Layers from the top: sky (islands), surface with hills, mountains and valleys, a regolith
/// layer, the rock layer, the cavern layer, and the Irradiated Core (liquid uranium under a cracked crust ceiling).
/// </summary>
public sealed partial class LargeWorldGenerator
{
    private readonly int _width;
    private readonly int _height;
    private readonly WorldBlight _blight;
    private readonly WorldHazard? _hazard;
    private double[] _profile = Array.Empty<double>();
    private int[] _surface = Array.Empty<int>();
    private int[] _rockLine = Array.Empty<int>();
    private int[] _moltenTop = Array.Empty<int>();
    private int[] _frozenEdge = Array.Empty<int>();
    private int[] _junkStart = Array.Empty<int>();
    private int _cavernRow;
    private readonly List<RadiatedPocket> _pockets = new();
    private readonly List<(int Left, int Right, int Top, int Bottom)> _fungal = new();

    public LargeWorldGenerator(int width, int height, WorldBlight blight, WorldHazard? hazard)
    {
        _width = width;
        _height = height;
        _blight = blight == WorldBlight.Random ? WorldBlight.Radiated : blight;
        _hazard = hazard;
        Features.Blight = _blight;
    }

    public WorldFeatures Features { get; } = new();
    public BiomeLayout? Layout { get; private set; }

    /// <summary>A pass count from the density table (area or width scaled).</summary>
    private int N(Density density) => density.Count(_width, _height);
    private readonly List<(int Center, double HalfWidth, double Height)> _mountains = new();

    /// <summary>Adds every stage, in order, to <paramref name="pipeline"/>.</summary>
    public void AddPasses(WorldGenerationPipeline pipeline)
    {
        void Add(string name, double weight, Action<TileGrid, TileCatalog, Random, CancellationToken> apply) =>
            pipeline.AddPass(new DelegateGenerationPass(name, weight, (grid, catalog, random, token) =>
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                apply(grid, catalog, random, token);
                Features.PassMilliseconds[name] = watch.Elapsed.TotalMilliseconds;
            }));

        Add("Sketching the horizon", 0.4, SketchHorizon);
        Add("Raising mountains", 0.4, RaiseMountains);
        Add("Laying down strata", 1.2, LayStrata);
        Add("Scattering rocks in the soil", 0.6, RocksInSoil);
        Add("Mixing soil into the rock", 0.6, SoilInRock);
        Add("Mixing clay, gravel and slate", 0.4, ClayGravelSlate);
        Add("Burying sand and scrap", 0.5, SandAndScrap);
        Add("Pocking the ground", 0.4, SmallHoles);
        Add("Hollowing shallow caves", 0.8, ShallowCaves);
        Add("Opening the caverns", 1.4, Caverns);
        Add("Carving tunnels", 0.9, Tunnels);
        Add("Digging cave mouths", 0.5, CaveMouths);
        Add("Hollowing mountain caves", 0.3, MountainCaves);
        Add("Freezing the west", 0.8, FreezeWest);
        Add("Pocketing slush", 0.2, SlushPockets);
        Add("Rusting the east", 0.8, RustEast);
        Add("Raising scrap dunes", 0.3, ScrapDunes);
        Add("Hollowing the scrap hive", 0.4, ScrapHive);
        Add("Filling the oceans", 0.6, AcidCoasts);
        Add("Carving tide grottos", 0.3, TideGrottos);
        Add("Spreading spore groves", 0.6, SporeGroves);
        Add("Growing the fungal hollows", 0.5, FungalHollows);
        Add("Irradiating the core", 1.2, MeltUnderworld);
        Add("Seeding the blight", 1.2, SeedBlight);
        Add("Rooting deep blight pockets", 0.7, DeepBlightPockets);
        Add("Threading ore veins", 1.0, OreVeins);
        Add("Growing biome ores", 0.7, BiomeOres);
        Add("Pocketing silt", 0.2, SiltPockets);
        Add("Growing crystal caves", 0.5, CrystalCaves);
        Add("Setting gems in the stone", 0.3, GemVeins);
        Add("Shaping mini-biomes", 0.6, MiniBiomes);
        Add("Hiding life cores", 0.3, LifeCores);
        Add("Burying supply caches", 0.4, SupplyCaches);
        Add("Lifting sky islands", 0.4, SkyIslands);
        Add("Crashing the derelict", 0.4, Derelict);
        Add("Burying landers", 0.3, BuriedLanders);
        Add("Greening the grasslands", 0.5, Greening);
        Add("Raising the verdant canopy", 0.6, VerdantCanopy);
        Add("Sinking the canopy roots", 0.6, UndergroundCanopy);
        Add("Sealing the precursor vault", 0.3, PrecursorVault);
        Add("Winding the vault temple", 0.4, VaultTemple);
        Add("Building canopy shrines", 0.2, CanopyShrines);
        Add("Binding the survivors", 0.1, PlaceSurvivors);
        Add("Raising background walls", 1.2, BackgroundWalls);
        Add("Weaving cave walls", 0.6, CaveWallVariety);
        Add("Raising the precursor dungeon", 0.5, PrecursorDungeon);
        Add("Burying cabins", 0.4, BuriedCabins);
        Add("Growing giant canopy trees", 0.3, GiantCanopyTrees);
        Add("Enshrining weapons", 0.2, WeaponShrines);
        Add("Webbing the mite warrens", 0.1, WebMiteWarrens);
        Add("Pooling underground lakes", 0.4, UndergroundLakes);
        Add("Pooling the phase lake", 0.2, PhasePool);
        Add("Ruining the core reactors", 0.3, SlagFoundries);
        Add("Setting up campsites and caches", 0.3, MicroBiomes);
        Add("Raising old-world statues", 0.2, Statues);
        Add("Rigging traps", 0.4, Traps);
        Add("Filling surface lakes", 0.5, SurfaceLakes);
        Add("Digging surface ponds", 0.2, SurfacePonds);
        Add("Pouring canopy pools", 0.2, CanopyPools);
        Add("Draining the dunes", 0.2, DrainDunes);
        Add("Tapping coolant springs", 0.2, CoolantSprings);
        Add("Sealing the sea floor", 0.1, SealSeaFloor);
        Add("Settling loose sand", 0.5, SettleLooseSand);
        Add("Clearing sky-exposed walls", 0.3, ClearSkyWalls);
        Add("Flooding cave pockets", 0.4, FloodCavePockets);
        Add("Settling liquids", 1.5, SettleLiquids);
        Add("Draining surface uranium", 0.2, CoolSurfaceLava);
        Add("Cutting seeps", 0.3, Seeps);
        Add("Icing frozen ponds", 0.2, IceFrozenPonds);
        Add("Hiding surface and sunken chests", 0.2, SurfaceAndSunkenChests);
        Add("Smoothing the terrain", 0.8, SmoothTerrain);
        Add("Growing moss caves", 0.3, MossCaves);
        Add("Hanging speleothems", 0.5, Speleothems);
        Add("Exposing gems", 0.3, ExposedGems);
        Add("Exposing surface ore and stone", 0.2, SurfaceOre);
        Add("Growing trees and herbs", 0.5, Flora);
        Add("Felling old logs", 0.2, FallenLogs);
        Add("Planting water life", 0.4, WaterLife);
        Add("Planting lilies, dyes and gourds", 0.3, SurfacePlants);
        Add("Growing biome trees, gem trees and wild plants", 0.3, Rimtera.Systems.ContentFillFlora.Generate); // content fill
        Add("Scattering pots", 0.3, Pots);
        Add("Heaping rubble and shell piles", 0.3, Piles);
        Add("Laying graveyards", 0.2, Graveyards);
        Add("Seeding brood pods", 0.1, BroodPods);
        Add("Improving chest contents", 0.4, ImproveChests);
        Add("Tidying up", 0.6, Cleanup);
        Add("Laying derelict transit walkways", 0.1, TransitWalkways);
        AddSeedVariantPasses(Add); // secret seed variants (LargeWorldGenerator.Seeds.cs)
        Add("Sprouting grass and flowers", 0.2, Rimtera.Systems.ContentFillFlora.GenerateGrassPlants); // flora parity: cuttable grass plants
        Add("Choosing the landing site", 0.2, ChooseLandingSite);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Smooth value noise in [-1, 1]: random control points eased with smoothstep.</summary>
    private static double[] Noise(Random random, int length, double spacing)
    {
        spacing = Math.Max(1.0, spacing);
        var controls = new double[(int)(length / spacing) + 3];
        for (var i = 0; i < controls.Length; i++)
            controls[i] = random.NextDouble() * 2 - 1;
        var result = new double[length];
        for (var i = 0; i < length; i++)
        {
            var position = i / spacing;
            var segment = (int)position;
            var t = position - segment;
            var eased = t * t * (3 - 2 * t);
            result[i] = controls[segment] + (controls[segment + 1] - controls[segment]) * eased;
        }
        return result;
    }

    private static bool IsProtected(TileId tile) => tile is TileId.Bedrock or TileId.ReactorCore or TileId.SporeHeart or
        TileId.VaultAltar or TileId.SlagForge or TileId.MudBrick or TileId.BroodPod or (>= TileId.SunkenChest and <= TileId.SlagChest) or
        TileId.RadAltar or TileId.RootAltar or TileId.SupplyCache or TileId.SalvagePod or TileId.LifeCore or
        TileId.DungeonBrick or TileId.Wood or TileId.ShipDoor or TileId.PrecursorBrick or TileId.PrecursorGlyph or
        TileId.PrecursorVaultDoor or TileId.LockedVaultChest or TileId.BloomHive or TileId.CoastalHabPod or TileId.BeaconTower or
        TileId.FungalHabRuin or TileId.PrecursorStatue or TileId.GlyphTablet or TileId.EnergyPylon or
        TileId.DungeonBrickPrecursor or TileId.DungeonChest or TileId.CabinChest or TileId.WeaponShrine or TileId.GiantTrunk or
        TileId.DartTrap or TileId.PressurePlate;

    /// <summary>
    /// Blob walker: stamps a ragged disc, drifts with a wandering velocity and shrinks, repeating
    /// for <paramref name="steps"/> steps. <paramref name="paint"/> maps the current tile to its
    /// replacement (or null to leave it). The workhorse for pockets, caves and veins.
    /// </summary>
    private static void Blob(TileGrid grid, Random random, double x, double y, double radius, int steps,
        double vx, double vy, Func<TileId, TileId?> paint, double wander = 0.5, int minY = 0, int maxY = int.MaxValue)
    {
        maxY = Math.Min(maxY, grid.Height - 2);
        for (var step = 0; step < steps; step++)
        {
            var r = radius * (1.0 - 0.5 * step / Math.Max(1, steps)) * (0.85 + random.NextDouble() * 0.3);
            if (r < 0.5)
                break;
            var reach = (int)Math.Ceiling(r);
            var cx = (int)Math.Round(x);
            var cy = (int)Math.Round(y);
            for (var oy = -reach; oy <= reach; oy++)
            {
                var py = cy + oy;
                if (py < minY || py > maxY || py < 0)
                    continue;
                for (var ox = -reach; ox <= reach; ox++)
                {
                    var px = cx + ox;
                    if ((uint)px >= (uint)grid.Width)
                        continue;
                    var edge = r * r * (0.75 + random.NextDouble() * 0.35);
                    if (ox * ox + oy * oy > edge)
                        continue;
                    var tile = grid.TileAt(px, py);
                    if (IsProtected(tile))
                        continue;
                    if (paint(tile) is { } next && next != tile)
                        grid.SetTile(new GridPoint(px, py), next);
                }
            }
            vx = Math.Clamp(vx + (random.NextDouble() - 0.5) * wander, -1.4, 1.4);
            vy = Math.Clamp(vy + (random.NextDouble() - 0.5) * wander, -1.4, 1.4);
            x += vx;
            y += vy;
        }
    }

    private static TileId? CarveAir(TileId tile) => tile == TileId.Air ? null : TileId.Air;

    /// <summary>
    /// The long cave carver (0.3.0): walks <paramref name="steps"/> steps on a drifting heading, clearing a diamond whose
    /// size shrinks from <paramref name="width"/> to nothing, so one call cuts a long, tapering, winding cave that meets
    /// others. A given heading (<paramref name="vx"/>, <paramref name="vy"/>) sends it that way, e.g. down from the surface;
    /// <paramref name="level"/> keeps it from drifting up or down. Protected tiles are never cut.
    /// </summary>
    private void Runner(TileGrid grid, Random random, double x, double y, double width, int steps,
        double? vx = null, double? vy = null, bool level = false, int minY = 1)
    {
        // Worlds shorter than Tiny (900 rows; only test worlds) get proportionally smaller caves, so a 72-row world isn't
        // carved hollow. Every real world size uses the full numbers.
        var scale = Math.Clamp(_height / 900.0, 0.2, 1.0);
        width *= scale;
        steps = Math.Max(1, (int)(steps * scale));
        var dx = vx ?? random.Next(-10, 11) * 0.1;
        var dy = vy ?? random.Next(-10, 11) * 0.1;
        for (var left = steps; left > 0; left--)
        {
            var size = width * left / (double)steps;
            var half = size * 0.5;
            int x0 = (int)(x - half), x1 = (int)(x + half), y0 = (int)(y - half), y1 = (int)(y + half);
            for (var cx = Math.Max(1, x0); cx < Math.Min(grid.Width - 1, x1); cx++)
            for (var cy = Math.Max(minY, y0); cy < Math.Min(grid.Height - 2, y1); cy++)
            {
                if (Math.Abs(cx - x) + Math.Abs(cy - y) >= width * 0.5 * (1 + random.Next(-10, 11) * 0.015))
                    continue;
                var tile = grid.TileAt(cx, cy);
                if (tile != TileId.Air && !IsProtected(tile))
                    grid.SetTile(new GridPoint(cx, cy), TileId.Air);
            }
            x += dx;
            y += dy;
            dx = Math.Clamp(dx + random.Next(-10, 11) * 0.05, -1, 1);
            if (!level)
                dy = Math.Clamp(dy + random.Next(-10, 11) * 0.05, -1, 1);
        }
    }

    private static void Fill(TileGrid grid, int left, int top, int right, int bottom, TileId tile, bool force = false)
    {
        for (var y = Math.Max(0, top); y <= Math.Min(grid.Height - 2, bottom); y++)
        for (var x = Math.Max(0, left); x <= Math.Min(grid.Width - 1, right); x++)
            if (force || !IsProtected(grid.TileAt(x, y)))
                grid.SetTile(new GridPoint(x, y), tile);
    }

    private int RandomRow(Random random, int top, int bottom) =>
        random.Next(Math.Clamp(top, 1, _height - 2), Math.Clamp(Math.Max(top + 1, bottom), 2, _height - 1));

    private bool NearSpawn(int x, double fraction) => Math.Abs(x - _width / 2) < _width * fraction;

    private bool InBlightZone(int x, int margin = 0) =>
        Features.BlightZones.Any(zone => x >= zone.Left - margin && x <= zone.Right + margin);

    private bool IsFrozen(int x, int y) => x < _frozenEdge[Math.Clamp(y, 0, _height - 1)];
    private bool IsJunk(int x, int y) => x >= _junkStart[Math.Clamp(y, 0, _height - 1)];

    private static int TopSolid(TileGrid grid, TileCatalog catalog, int x)
    {
        for (var y = 0; y < grid.Height; y++)
            if (catalog[grid.TileAt(x, y)].IsSolid)
                return y;
        return grid.Height - 1;
    }

    /// <summary>A cave floor: open, non-liquid cell with solid ground below and room above.</summary>
    private static bool IsCaveFloor(TileGrid grid, TileCatalog catalog, int x, int y)
    {
        if (!grid.Contains(x, y + 1) || !grid.Contains(x, y - 1))
            return false;
        var cell = grid.GetCell(x, y);
        return cell.TileId == TileId.Air && cell.LiquidDepth <= 0f &&
               catalog[grid.TileAt(x, y + 1)].IsSolid && !IsProtected(grid.TileAt(x, y + 1)) &&
               grid.GetCell(x, y + 1).Shape == TileShape.Full &&
               grid.TileAt(x, y - 1) == TileId.Air;
    }

    // ---------------------------------------------------------------- terrain

    /// <summary>
    /// The surface line (0.3.0, matched to the Terraria-style reference in docs/WORLDGEN_DIFF.md §4): a walk across the
    /// world in runs of 5–40 columns. Each run is a plateau, a rise, a dip, a steep climb or a steep drop, and nudges the
    /// line a tile at a time, so the ground rolls in readable hills and valleys instead of jittering.
    /// </summary>
    private void SketchHorizon(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        _profile = new double[_width];
        var y = _height * 0.23 * (0.9 + random.NextDouble() * 0.2);
        var top = _height * 0.15;
        var bottom = _height * 0.30;
        int left = 0, kind = 0;
        for (var x = 0; x < _width; x++)
        {
            if (left <= 0)
            {
                kind = random.Next(5);
                left = random.Next(5, 40);
                if (kind == 0)
                    left = (int)(left * random.Next(5, 30) * 0.2); // plateaus run longer
            }
            left--;
            switch (kind)
            {
                case 0: // plateau: an occasional step either way
                    while (random.Next(7) == 0) y += random.Next(-1, 2);
                    break;
                case 1: // rise
                    while (random.Next(4) == 0) y--;
                    while (random.Next(10) == 0) y++;
                    break;
                case 2: // dip
                    while (random.Next(4) == 0) y++;
                    while (random.Next(10) == 0) y--;
                    break;
                case 3: // climb
                    while (random.Next(2) == 0) y--;
                    while (random.Next(6) == 0) y++;
                    break;
                default: // drop
                    while (random.Next(2) == 0) y++;
                    while (random.Next(5) == 0) y--;
                    break;
            }
            y = Math.Clamp(y, top, bottom);
            _profile[x] = y;
        }
    }

    /// <summary>Columns of fuel sea at each edge, and the coastal shelf inland of it (both scale with width).</summary>
    private int OceanWidth => Math.Min(N(WorldGenDensity.CoastWidth), _width / 5);
    private int ShelfWidth => Math.Min(N(WorldGenDensity.CoastShelf), _width / 8);
    /// <summary>Distance from either world edge inside which terrain belongs to the sea and its shelf.</summary>
    private int CoastReach => OceanWidth + ShelfWidth;

    private void RaiseMountains(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        // Mountains and valleys keep inland: the coastal shelf and the sea stay low and open.
        var inlandLeft = Math.Min(_width / 3, CoastReach + 40);
        var inlandRight = Math.Max(inlandLeft + 1, _width - inlandLeft);
        var mountains = N(WorldGenDensity.Mountains);
        for (var i = 0; i < mountains; i++)
        {
            var center = random.Next(inlandLeft, inlandRight);
            if (NearSpawn(center, 0.08))
                continue;
            var halfWidth = 30 + random.NextDouble() * 50 + _width * 0.002;
            var height = _height * (0.015 + random.NextDouble() * 0.02);
            _mountains.Add((center, halfWidth, height));
            for (var x = (int)(center - halfWidth * 2.2); x <= center + halfWidth * 2.2; x++)
            {
                if ((uint)x >= (uint)_width)
                    continue;
                var d = (x - center) / halfWidth;
                // A rounded ridge rising out of the rolling land (0.3.0: lower, without the sharp crest).
                var lift = Math.Exp(-d * d) * height;
                _profile[x] -= lift;
            }
        }

        var valleys = N(WorldGenDensity.Valleys);
        for (var i = 0; i < valleys; i++)
        {
            var center = random.Next(inlandLeft, inlandRight);
            if (NearSpawn(center, 0.08))
                continue;
            var halfWidth = 25 + random.NextDouble() * 40;
            var depth = _height * (0.012 + random.NextDouble() * 0.018);
            for (var x = (int)(center - halfWidth * 2); x <= center + halfWidth * 2; x++)
            {
                if ((uint)x >= (uint)_width)
                    continue;
                var d = (x - center) / halfWidth;
                _profile[x] += Math.Exp(-d * d * 1.6) * depth;
            }
        }

        // The landing site is a small level pad (0.3.0: no longer a 400-column plateau; the land rolls right up to it).
        var mid = _width / 2;
        var flat = 8;
        var level = _profile[mid];
        for (var x = mid - flat * 2; x <= mid + flat * 2; x++)
        {
            if ((uint)x >= (uint)_width)
                continue;
            var weight = Math.Clamp(1.0 - (Math.Abs(x - mid) - flat) / (double)flat, 0, 1);
            _profile[x] = _profile[x] * (1 - weight) + level * weight;
        }

        // The coasts: over the shelf the land eases down to just above sea level, then stays there under the sea
        // (the sea pass carves the beach and the basin), so every edge biome slopes into its ocean.
        var shoreRow = _height * 0.255;
        for (var x = 0; x < _width; x++)
        {
            var distance = Math.Min(x, _width - 1 - x);
            if (distance >= CoastReach)
                continue;
            var t = Math.Clamp((distance - OceanWidth) / (double)Math.Max(1, ShelfWidth), 0, 1);
            var eased = t * t * (3 - 2 * t);
            _profile[x] = Math.Min(shoreRow, shoreRow * (1 - eased) + _profile[x] * eased);
        }

        _surface = new int[_width];
        for (var x = 0; x < _width; x++)
            _surface[x] = Math.Clamp((int)Math.Round(_profile[x]), Math.Max(4, (int)(_height * 0.09)), (int)(_height * 0.36));
    }

    private void LayStrata(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var dirtNoise = Noise(random, _width, 14);
        var moltenNoise = Noise(random, _width, 18);
        _rockLine = new int[_width];
        _moltenTop = new int[_width];
        var moltenFraction = _hazard switch
        {
            WorldHazard.MoltenChasm => 0.78,
            WorldHazard.RadiatedDeeps => 0.88,
            _ => 0.84
        };
        var dirtDepth = Math.Max(4, _height * 0.08);
        var maxSurface = _surface.Max();
        _cavernRow = Math.Clamp((int)(maxSurface + _height * 0.16), maxSurface + 4, _height - 8);
        Features.CavernRow = _cavernRow;
        for (var x = 0; x < _width; x++)
        {
            _rockLine[x] = _surface[x] + (int)Math.Round(dirtDepth + dirtNoise[x] * dirtDepth * 0.3);
            _moltenTop[x] = Math.Clamp((int)Math.Round(_height * moltenFraction + moltenNoise[x] * _height * 0.025),
                _cavernRow + 4, _height - 3);
        }
        // Row-major bulk fill of the fresh grid (nothing listens to cell events yet).
        var strata = grid.RawCells;
        for (var y = 0; y < _height; y++)
        {
            if ((y & 63) == 0)
                token.ThrowIfCancellationRequested();
            var row = y * _width;
            for (var x = 0; x < _width; x++)
            {
                if (y < _surface[x])
                    continue;
                strata[row + x].TileId = y >= _height - 1 ? TileId.Bedrock : y < _rockLine[x] ? TileId.Soil : TileId.Rock;
            }
        }
        grid.MarkBulkEdit();

        // Surface biome borders per row (frozen west, junk east), jittered and tapering with depth.
        var frozenBase = _width * (0.17 + random.NextDouble() * 0.05);
        var junkBase = _width * (0.79 + random.NextDouble() * 0.05);
        var frozenJitter = Noise(random, _height, 6);
        var junkJitter = Noise(random, _height, 6);
        var amplitude = Math.Clamp(_width / 80, 1, 8);
        _frozenEdge = new int[_height];
        _junkStart = new int[_height];
        for (var y = 0; y < _height; y++)
        {
            var depth = Math.Clamp((y - _cavernRow) / (double)Math.Max(1, _height - _cavernRow), 0, 1);
            var taper = 1.0 - depth * 0.35;
            _frozenEdge[y] = Math.Clamp((int)Math.Round(frozenBase * taper + frozenJitter[y] * amplitude), 1, _width / 2 - 1);
            _junkStart[y] = Math.Clamp((int)Math.Round(_width - (_width - junkBase) * taper + junkJitter[y] * amplitude),
                _width / 2 + 1, _width - 1);
        }
    }

    private void RocksInSoil(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var count = N(WorldGenDensity.RocksInSoil);
        for (var i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            var x = random.Next(_width);
            var y = RandomRow(random, _surface[x] + 2, _rockLine[x] + 2);
            Blob(grid, random, x, y, 1.5 + random.NextDouble() * 2.5, random.Next(4, 12),
                random.NextDouble() - 0.5, random.NextDouble() - 0.5, tile => tile == TileId.Soil ? TileId.Rock : null);
        }
    }

    private void SoilInRock(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var count = N(WorldGenDensity.SoilInRock);
        for (var i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            var x = random.Next(_width);
            var y = RandomRow(random, _rockLine[x], _moltenTop[x] - 6);
            Blob(grid, random, x, y, 2 + random.NextDouble() * 3, random.Next(5, 14),
                random.NextDouble() - 0.5, random.NextDouble() - 0.5, tile => tile == TileId.Rock ? TileId.Soil : null);
        }
    }

    private void SandAndScrap(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var count = N(WorldGenDensity.SandAndScrap);
        for (var i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            var x = random.Next(_width);
            var y = RandomRow(random, _surface[x] + 3, _cavernRow + _height / 10);
            var sand = random.Next(3) != 0;
            Blob(grid, random, x, y, 2 + random.NextDouble() * 2.5, random.Next(4, 10),
                random.NextDouble() - 0.5, random.NextDouble() * 0.4 - 0.2,
                tile => tile is TileId.Soil or TileId.Rock ? (sand ? TileId.Sand : TileId.ScrapIronOre) : null);
        }
    }

    /// <summary>Pocks (0.3.0): a small cut in the surface band and a larger one below it, many times over.</summary>
    private void SmallHoles(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var count = N(WorldGenDensity.SmallHoles);
        var highest = _surface.Min();
        var lowest = _surface.Max();
        for (var i = 0; i < count; i++)
        {
            if ((i & 255) == 0)
                token.ThrowIfCancellationRequested();
            var x = random.Next(_width);
            Runner(grid, random, x, random.Next(highest, lowest + 1), random.Next(2, 5), random.Next(2, 20),
                minY: Math.Max(1, _surface[x] - 2));
            x = random.Next(_width);
            Runner(grid, random, x, random.Next(lowest, Math.Max(lowest + 1, _moltenTop[x] - 2)), random.Next(8, 15), random.Next(7, 30),
                minY: _surface[x] + 6);
        }
    }

    private void ShallowCaves(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var count = N(WorldGenDensity.ShallowCaves);
        for (var i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            var x = random.Next(_width);
            var y = RandomRow(random, _surface.Max(), _rockLine.Max());
            Runner(grid, random, x, y, random.Next(5, 15), random.Next(30, 200), minY: _surface[x] + 6);
        }
    }

    private void Caverns(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var count = N(WorldGenDensity.Caverns);
        for (var i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            var x = random.Next(_width);
            var y = RandomRow(random, _rockLine.Max(), _moltenTop[x] - 4);
            Runner(grid, random, x, y, random.Next(6, 20), random.Next(50, 300), minY: _surface[x] + 8);
        }
    }

    /// <summary>Long wavy tunnels: a heading that swings on a slow sine plus noise.</summary>
    private void Tunnels(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var count = N(WorldGenDensity.Tunnels);
        for (var i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            double x = random.Next(_width);
            double y = RandomRow(random, _rockLine[(int)x] + 2, _moltenTop[(int)x] - 6);
            var heading = random.Next(2) == 0 ? 0.0 : Math.PI;
            var shortest = Math.Min(_width / 8, 160);
            var length = random.Next(shortest, Math.Max(shortest + 1, Math.Min(_width / 3, 640)));
            var phase = random.NextDouble() * Math.PI * 2;
            var period = 18 + random.NextDouble() * 30;
            var radius = 1.4 + random.NextDouble() * 1.4;
            for (var step = 0; step < length; step++)
            {
                var swing = Math.Sin(phase + step / period * Math.PI * 2) * 0.55 + (random.NextDouble() - 0.5) * 0.25;
                var angle = heading + swing;
                x += Math.Cos(angle);
                y += Math.Sin(angle) * 0.8;
                if (x < 2 || x >= _width - 2)
                    break;
                y = Math.Clamp(y, _rockLine[(int)x], _moltenTop[(int)x] - 2);
                var r = radius + Math.Sin(step * 0.21) * 0.5;
                Stamp(grid, (int)x, (int)y, r, CarveAir);
            }
        }
    }

    private static void Stamp(TileGrid grid, int cx, int cy, double radius, Func<TileId, TileId?> paint)
    {
        var reach = (int)Math.Ceiling(radius);
        for (var oy = -reach; oy <= reach; oy++)
        for (var ox = -reach; ox <= reach; ox++)
        {
            if (ox * ox + oy * oy > radius * radius + 0.3)
                continue;
            var x = cx + ox;
            var y = cy + oy;
            if (!grid.Contains(x, y) || y >= grid.Height - 1)
                continue;
            var tile = grid.TileAt(x, y);
            if (IsProtected(tile))
                continue;
            if (paint(tile) is { } next && next != tile)
                grid.SetTile(new GridPoint(x, y), next);
        }
    }

    /// <summary>
    /// Surface caves (0.3.0): runners started on the ground and sent downward, so the land has natural openings into the
    /// caves below — narrow chutes, wide sloping mouths, a few long descents and some level galleries just under the ground.
    /// Only the landing pad itself is spared.
    /// </summary>
    private void CaveMouths(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        int Column() => random.Next(_width / 20, _width - _width / 20);
        bool Pad(int x) => Math.Abs(x - _width / 2) < 20;
        void Mouth(double width, int steps, double vx, double vy, bool level = false)
        {
            var x = Column();
            if (Pad(x))
                return;
            Runner(grid, random, x, _surface[x], width, steps, vx, vy, level);
            Features.AddCount(FeatureNames.SurfacePockets);
        }
        for (var i = N(WorldGenDensity.CaveMouths); i > 0; i--)
            Mouth(random.Next(3, 6), random.Next(5, 50), random.Next(-10, 11) * 0.1, 1.0);
        for (var i = N(WorldGenDensity.WideCaveMouths); i > 0; i--)
            Mouth(random.Next(10, 15), random.Next(50, 130), 0, 2.0);
        for (var i = N(WorldGenDensity.DeepCaveMouths); i > 0; i--)
            Mouth(random.Next(12, 25), random.Next(150, 500), random.Next(-10, 11) * 0.1, 4.0);
        for (var i = N(WorldGenDensity.ShallowGalleries); i > 0; i--)
            Mouth(random.Next(7, 12), random.Next(150, 250), 0, 1.0, level: true);
    }

    // ---------------------------------------------------------------- biomes

    private void FreezeWest(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        for (var y = 0; y < _height - 1; y++)
        for (var x = 0; x < _frozenEdge[y]; x++)
        {
            if (y >= _moltenTop[x])
                continue;
            var tile = grid.TileAt(x, y);
            if (tile is TileId.Soil or TileId.Grass or TileId.Sand || (tile == TileId.Rock && y < _rockLine[x]))
                grid.SetTile(new GridPoint(x, y), TileId.Snow);
        }
        // Ice pockets through the frozen rock layer (the underground extension).
        var pockets = (int)Math.Max(WorldGenDensity.FrozenPockets.Minimum, WorldGenDensity.FrozenPockets.Rate * _frozenEdge[_cavernRow] * _height);
        for (var i = 0; i < pockets; i++)
        {
            var y = RandomRow(random, _rockLine[0], _moltenTop[0] - 6);
            var x = random.Next(0, Math.Max(1, _frozenEdge[y]));
            Blob(grid, random, x, y, 2 + random.NextDouble() * 3, random.Next(5, 14), random.NextDouble() - 0.5,
                random.NextDouble() - 0.5, tile => tile == TileId.Rock ? TileId.Snow : null);
        }
    }

    private void RustEast(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        for (var y = 0; y < _height - 1; y++)
        for (var x = _junkStart[y]; x < _width; x++)
        {
            if (y >= _moltenTop[x])
                continue;
            var tile = grid.TileAt(x, y);
            // A skin of loose dune sand; hardened dune sand below, so the wasteland does not collapse into its caves.
            if (tile is TileId.Soil or TileId.Grass || (tile == TileId.Sand && y < _rockLine[x]))
                grid.SetTile(new GridPoint(x, y), y < Math.Min(_rockLine[x], _surface[x] + 7) ? TileId.DesertSand : TileId.ScrapSandstone);
        }
        var heaps = (int)Math.Max(WorldGenDensity.JunkHeaps.Minimum, WorldGenDensity.JunkHeaps.Rate * (_width - _junkStart[_cavernRow]) * _height);
        for (var i = 0; i < heaps; i++)
        {
            var y = RandomRow(random, _rockLine[_width - 1], _moltenTop[_width - 1] - 6);
            var x = random.Next(Math.Min(_width - 1, _junkStart[y]), _width);
            Blob(grid, random, x, y, 2 + random.NextDouble() * 3, random.Next(5, 14), random.NextDouble() - 0.5,
                random.NextDouble() - 0.5, tile => tile is TileId.Rock or TileId.Soil ? TileId.ScrapSandstone : null);
        }
    }

    private void SporeGroves(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var groves = N(WorldGenDensity.SporeGroves);
        for (var i = 0; i < groves * 6 && _fungal.Count < groves; i++)
        {
            var x = random.Next(_width);
            var y = RandomRow(random, _cavernRow, _moltenTop[Math.Min(x, _width - 1)] - 10);
            if (IsFrozen(x, y) || IsJunk(x, y))
                continue;
            var rx = random.Next(6, 13);
            var ry = random.Next(3, 7);
            for (var py = y - ry - 2; py <= y + ry + 2; py++)
            for (var px = x - rx - 2; px <= x + rx + 2; px++)
            {
                if (!grid.Contains(px, py) || py >= _height - 1)
                    continue;
                var dx = (px - x) / (double)rx;
                var dy = (py - y) / (double)ry;
                var d = dx * dx + dy * dy;
                var tile = grid.TileAt(px, py);
                if (IsProtected(tile))
                    continue;
                if (d <= 1.0 + random.NextDouble() * 0.2)
                    grid.SetTile(new GridPoint(px, py), TileId.Air);
                else if (d <= 1.9 && tile is TileId.Rock or TileId.Soil)
                    grid.SetTile(new GridPoint(px, py), TileId.FungalSoil);
            }
            _fungal.Add((x - rx - 2, x + rx + 2, y - ry - 2, y + ry + 2));
        }
    }

    /// <summary>Top row of the Irradiated Core's open cavern floor per column (set by <see cref="MeltUnderworld"/>).</summary>
    private int[] _coreFloor = Array.Empty<int>();
    /// <summary>Lowest row of the core cavern's ceiling per column.</summary>
    private int[] _coreCeiling = Array.Empty<int>();

    /// <summary>
    /// The Irradiated Core: a cracked crust over irradiated rock, hollowed into one wide, open cavern (a ragged ceiling,
    /// rock pillars and a rolling floor) with liquid-uranium lakes sunk into the floor, vitrified glass seams, and falls
    /// pouring from the ceiling. Liquid uranium is stored as <see cref="LiquidKind.Lava"/>.
    /// </summary>
    private void MeltUnderworld(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var crustNoise = Noise(random, _width, 7);
        for (var x = 0; x < _width; x++)
        {
            token.ThrowIfCancellationRequested();
            var ceiling = _moltenTop[x] - (2 + (int)Math.Round((crustNoise[x] + 1) * 1.5));
            for (var y = Math.Max(_surface[x], ceiling); y < _height - 1; y++)
            {
                var tile = grid.TileAt(x, y);
                if (tile == TileId.Air || IsProtected(tile))
                    continue;
                grid.SetTile(new GridPoint(x, y), y < _moltenTop[x] ? TileId.CoreCrust : TileId.IrradiatedRock);
            }
        }

        // The open cavern: a ragged ceiling, a rolling floor, and rock pillars that join them now and then.
        var ceilingBroad = Noise(random, _width, Math.Max(12, _width / 60.0));
        var ceilingFine = Noise(random, _width, 5);
        var floorBroad = Noise(random, _width, Math.Max(16, _width / 45.0));
        var floorFine = Noise(random, _width, 6);
        _coreCeiling = new int[_width];
        _coreFloor = new int[_width];
        // Rock pillars: narrow in the middle, flaring into the ceiling and the floor.
        var pillars = new bool[_width];
        var pillarSites = new List<(int Center, int Half)>();
        for (var x = random.Next(40, 160); x < _width - 4; x += random.Next(140, 320))
        {
            var half = random.Next(2, 6);
            pillarSites.Add((x, half));
            for (var px = x - half; px <= x + half; px++)
                if ((uint)px < (uint)_width)
                    pillars[px] = true;
        }
        for (var x = 0; x < _width; x++)
        {
            var band = Math.Max(24, _height - 1 - _moltenTop[x]);
            _coreCeiling[x] = _moltenTop[x] + (int)Math.Round(band * (0.3 + ceilingBroad[x] * 0.09) + ceilingFine[x] * 2.5);
            _coreFloor[x] = _height - 2 - (int)Math.Round(band * (0.27 + floorBroad[x] * 0.08) + floorFine[x] * 1.5);
            _coreFloor[x] = Math.Max(_coreFloor[x], _coreCeiling[x] + 8);
        }
        bool InPillar(int x, int y)
        {
            foreach (var (center, half) in pillarSites)
            {
                var reach = Math.Abs(x - center);
                if (reach > half + 12)
                    continue;
                var span = Math.Max(1, _coreFloor[center] - _coreCeiling[center]);
                var t = Math.Clamp((y - _coreCeiling[center]) / (double)span, 0, 1);
                var flare = 12 * Math.Pow(1 - Math.Sin(Math.PI * t), 2.5);
                if (reach <= half + flare)
                    return true;
            }
            return false;
        }
        for (var x = 0; x < _width; x++)
        {
            for (var y = _coreCeiling[x]; y < _coreFloor[x]; y++)
            {
                var tile = grid.TileAt(x, y);
                if (tile != TileId.Air && !IsProtected(tile) && !InPillar(x, y))
                    grid.SetTile(new GridPoint(x, y), TileId.Air);
            }
            if (pillars[x])
                continue;
            // A skin of cracked crust under the ceiling and on the floor.
            for (var y = _coreCeiling[x] - 2; y < _coreCeiling[x]; y++)
                if (grid.TileAt(x, y) == TileId.IrradiatedRock)
                    grid.SetTile(new GridPoint(x, y), TileId.CoreCrust);
            if (grid.TileAt(x, _coreFloor[x]) == TileId.IrradiatedRock && random.Next(3) != 0)
                grid.SetTile(new GridPoint(x, _coreFloor[x]), TileId.CoreCrust);
        }

        // Liquid uranium lakes: basins sunk into the cavern floor and filled to the old floor line.
        var lakes = N(WorldGenDensity.LavaLakes);
        for (var i = 0; i < lakes; i++)
        {
            token.ThrowIfCancellationRequested();
            var cx = random.Next(8, _width - 8);
            if (pillars[cx])
                continue;
            var rx = random.Next(6, Math.Max(7, Math.Min(34, _width / 18)));
            var depth = random.Next(3, Math.Max(4, Math.Min(12, (_height - _coreFloor[cx]) / 2)));
            var level = _coreFloor[cx];
            for (var x = cx - rx; x <= cx + rx; x++)
            {
                if (x < 1 || x >= _width - 1 || pillars[x])
                    continue;
                var dx = (x - cx) / (double)rx;
                var sink = (int)Math.Round(depth * Math.Sqrt(Math.Max(0, 1 - dx * dx)));
                var bottom = Math.Min(_height - 3, Math.Max(level, _coreFloor[x]) + sink);
                for (var y = Math.Min(level, _coreFloor[x]); y < bottom; y++)
                {
                    if (IsProtected(grid.TileAt(x, y)))
                        continue;
                    grid.SetTile(new GridPoint(x, y), TileId.Air);
                    if (y >= level)
                        grid.TryAddLiquid(new GridPoint(x, y), LiquidKind.Lava, 1f, catalog);
                }
                // Glassy shores where the uranium licks the rock.
                if (grid.TileAt(x, bottom) is TileId.IrradiatedRock or TileId.CoreCrust && random.Next(2) == 0)
                    grid.SetTile(new GridPoint(x, bottom), TileId.VitrifiedGlass);
            }
            Features.AddSite(FeatureNames.UraniumLakes, cx - rx, level, cx + rx, level + depth);
        }

        // Vitrified glass seams where old uranium flows quenched.
        var glass = N(WorldGenDensity.ObsidianBlobs);
        for (var i = 0; i < glass; i++)
        {
            var x = random.Next(_width);
            var y = RandomRow(random, _moltenTop[x], _height - 2);
            Blob(grid, random, x, y, 1.5 + random.NextDouble() * 1.5, random.Next(3, 8), random.NextDouble() - 0.5,
                random.NextDouble() - 0.5, tile => tile == TileId.IrradiatedRock ? TileId.VitrifiedGlass : null);
        }

        // Uranium falls: pockets in the cavern ceiling that pour liquid uranium down onto the floor.
        for (var i = N(WorldGenDensity.UraniumFalls); i > 0; i--)
        {
            var x = random.Next(4, _width - 4);
            if (pillars[x])
                continue;
            var top = _coreCeiling[x];
            for (var y = top - 4; y < top; y++)
            for (var dx = 0; dx <= 1; dx++)
            {
                if (IsProtected(grid.TileAt(x + dx, y)))
                    continue;
                grid.SetTile(new GridPoint(x + dx, y), TileId.Air);
                grid.TryAddLiquid(new GridPoint(x + dx, y), LiquidKind.Lava, 1f, catalog);
            }
            Features.AddCount(FeatureNames.UraniumFalls);
        }
    }

    // ---------------------------------------------------------------- blight

    private void SeedBlight(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var zones = N(WorldGenDensity.BlightZones);
        var zoneWidth = Math.Clamp((int)(_width * 0.05), _width >= 400 ? 34 : 10, 110);
        for (var attempt = 0; attempt < zones * 40 && Features.BlightZones.Count < zones; attempt++)
        {
            var center = random.Next(zoneWidth, _width - zoneWidth);
            if (NearSpawn(center, 0.12) || InCoast(center - zoneWidth) || InCoast(center + zoneWidth))
                continue;
            // Prefer the temperate band so each zone reads as blight, not as the frozen or junk wastes it borders.
            if (attempt < zones * 25 && (IsFrozen(center, _surface[center]) || IsJunk(center, _surface[center])))
                continue;
            if (Features.BlightZones.Any(zone => Math.Abs((zone.Left + zone.Right) / 2 - center) < zoneWidth * 2.2))
                continue;
            // Alternate sides of the landing site so both halves of the map get blight.
            var wantLeft = Features.BlightZones.Count % 2 == 0;
            if (attempt < zones * 20 && (center < _width / 2) != wantLeft)
                continue;
            var half = zoneWidth / 2 + random.Next(-zoneWidth / 6, zoneWidth / 6 + 1);
            var depth = Math.Max(_rockLine[center] - _surface[center] + 4, (_cavernRow - _surface[center]) * 2 / 3);
            Features.BlightZones.Add(new BlightZone(Math.Max(0, center - half), Math.Min(_width - 1, center + half), depth));
        }

        var edgeNoise = Noise(random, _height, 5);
        foreach (var zone in Features.BlightZones)
        {
            token.ThrowIfCancellationRequested();
            for (var x = zone.Left - 3; x <= zone.Right + 3; x++)
            {
                if ((uint)x >= (uint)_width)
                    continue;
                for (var y = _surface[x]; y < Math.Min(_height - 1, _surface[x] + zone.Depth); y++)
                {
                    var softEdge = (int)Math.Round(edgeNoise[y] * 3);
                    if (x < zone.Left + softEdge || x > zone.Right + softEdge)
                        continue;
                    var tile = grid.TileAt(x, y);
                    if (BlightRules.Convert(tile, _blight) is { } blighted)
                        grid.SetTile(new GridPoint(x, y), blighted);
                }
            }

            var chasms = Math.Max(1, (zone.Right - zone.Left) / 14);
            for (var c = 0; c < chasms; c++)
            {
                var x = zone.Left + (c + 1) * (zone.Right - zone.Left) / (chasms + 1) + random.Next(-2, 3);
                x = Math.Clamp(x, 3, _width - 4);
                var bottom = _blight == WorldBlight.Radiated
                    ? CarveShaft(grid, random, x, zone)
                    : CarveTangle(grid, random, x, zone);
                PlaceHeartChamber(grid, catalog, random, bottom.X, bottom.Y);
            }
        }

        // Altars: a couple per zone near the surface chambers, plus a few deep in the rock.
        var altarTile = BlightRules.AltarTile(_blight);
        var wantedAltars = N(WorldGenDensity.Altars);
        for (var attempt = 0; attempt < wantedAltars * 60 && Features.Altars.Count < wantedAltars; attempt++)
        {
            var x = random.Next(4, _width - 5);
            if (NearSpawn(x, 0.1))
                continue;
            var nearZone = Features.Altars.Count < Features.BlightZones.Count * 2 && Features.BlightZones.Count > 0;
            if (nearZone)
            {
                var zone = Features.BlightZones[Features.Altars.Count % Features.BlightZones.Count];
                x = random.Next(zone.Left, zone.Right + 1);
            }
            var y = nearZone ? RandomRow(random, _surface[x] + 4, _surface[x] + zone0Depth(x)) : RandomRow(random, _rockLine[x], _moltenTop[x] - 8);
            if (TryPlaceSquare(grid, x, y, altarTile, carveRoom: true))
                Features.Altars.Add(new GridPoint(x, y));
        }

        int zone0Depth(int x) => Features.BlightZones.FirstOrDefault(z => x >= z.Left && x <= z.Right).Depth is var d and > 0 ? d : 12;
    }

    /// <summary>Radiated chasm: a straight shaft lined with caustic crystal. Returns the bottom cell.</summary>
    private GridPoint CarveShaft(TileGrid grid, Random random, int x, BlightZone zone)
    {
        var halfWidth = random.Next(1, 3);
        var bottom = Math.Min(_moltenTop[x] - 8, _surface[x] + zone.Depth + random.Next(0, Math.Max(1, zone.Depth / 3)));
        bottom = Math.Max(bottom, _surface[x] + 6);
        var wobble = 0.0;
        for (var y = _surface[x] - 2; y <= bottom; y++)
        {
            wobble += (random.NextDouble() - 0.5) * 0.3;
            wobble = Math.Clamp(wobble, -1.5, 1.5);
            var cx = x + (int)Math.Round(wobble);
            for (var ox = -halfWidth - 2; ox <= halfWidth + 2; ox++)
            {
                var px = cx + ox;
                if (!grid.Contains(px, y) || y >= _height - 1 || IsProtected(grid.TileAt(px, y)))
                    continue;
                if (Math.Abs(ox) <= halfWidth)
                    grid.SetTile(new GridPoint(px, y), TileId.Air);
                else if (grid.TileAt(px, y) != TileId.Air)
                    grid.SetTile(new GridPoint(px, y), TileId.CorruptRock);
            }
        }
        return new GridPoint(x + (int)Math.Round(wobble), bottom);
    }

    /// <summary>Overgrowth chasm: two or three winding vine tunnels lined with mossy rock.</summary>
    private GridPoint CarveTangle(TileGrid grid, Random random, int x, BlightZone zone)
    {
        var target = Math.Max(_surface[x] + 8, Math.Min(_moltenTop[x] - 8, _surface[x] + zone.Depth));
        var end = new GridPoint(x, target);
        for (var strand = 0; strand < 2 + random.Next(2); strand++)
        {
            double cx = x + random.Next(-3, 4);
            double cy = _surface[Math.Clamp((int)cx, 0, _width - 1)] - 1;
            var phase = random.NextDouble() * 6.28;
            for (var step = 0; step < (target - cy) * 2.5 && cy < target; step++)
            {
                cx += Math.Sin(phase + step * 0.18) * 0.9 + (random.NextDouble() - 0.5) * 0.4;
                cx = Math.Clamp(cx, zone.Left - 4, zone.Right + 4);
                cy += 0.45 + random.NextDouble() * 0.3;
                Stamp(grid, (int)cx, (int)cy, 3.2, tile => tile is TileId.Air or TileId.Bedrock ? null :
                    BlightRules.Convert(tile, _blight) ?? (tile is TileId.CopperOre or TileId.ScrapIronOre ? TileId.OvergrowthRock : null));
                Stamp(grid, (int)cx, (int)cy, 1.7, CarveAir);
                end = new GridPoint((int)cx, (int)cy);
            }
        }
        return end;
    }

    /// <summary>Carves a small chamber at <paramref name="x"/>,<paramref name="y"/> and seats a 2×2 heart on its floor.</summary>
    private void PlaceHeartChamber(TileGrid grid, TileCatalog catalog, Random random, int x, int y)
    {
        x = Math.Clamp(x, 3, _width - 5);
        y = Math.Clamp(y, 4, _height - 6);
        var lining = _blight == WorldBlight.Radiated ? TileId.CorruptRock : TileId.OvergrowthRock;
        for (var py = y - 3; py <= y + 3; py++)
        for (var px = x - 4; px <= x + 5; px++)
        {
            if (!grid.Contains(px, py) || py >= _height - 1 || IsProtected(grid.TileAt(px, py)))
                continue;
            var inside = px > x - 4 && px < x + 5 && py > y - 3 && py < y + 2;
            grid.SetTile(new GridPoint(px, py), inside ? TileId.Air : lining);
        }
        var heart = BlightRules.HeartTile(_blight);
        var top = new GridPoint(x, y);
        for (var dy = 0; dy < 2; dy++)
        for (var dx = 0; dx < 2; dx++)
            grid.SetTile(new GridPoint(x + dx, y + dy), heart);
        // Floor under the heart.
        for (var dx = -1; dx < 3; dx++)
            if (grid.Contains(x + dx, y + 2) && grid.TileAt(x + dx, y + 2) == TileId.Air)
                grid.SetTile(new GridPoint(x + dx, y + 2), lining);
        Features.Hearts.Add(top);
    }

    /// <summary>Seats a 2×2 tile (altar) with a little headroom, on solid ground.</summary>
    private bool TryPlaceSquare(TileGrid grid, int x, int y, TileId tile, bool carveRoom)
    {
        if (x < 2 || x >= _width - 4 || y < 3 || y >= _height - 4)
            return false;
        for (var py = y - 2; py <= y + 2; py++)
        for (var px = x - 1; px <= x + 2; px++)
            if (IsProtected(grid.TileAt(px, py)))
                return false;
        if (carveRoom)
        {
            for (var py = y - 2; py <= y + 1; py++)
            for (var px = x - 1; px <= x + 2; px++)
                grid.SetTile(new GridPoint(px, py), TileId.Air);
            for (var px = x - 1; px <= x + 2; px++)
                if (grid.TileAt(px, y + 2) == TileId.Air)
                    grid.SetTile(new GridPoint(px, y + 2), TileId.Rock);
        }
        for (var dy = 0; dy < 2; dy++)
        for (var dx = 0; dx < 2; dx++)
            grid.SetTile(new GridPoint(x + dx, y + dy), tile);
        return true;
    }

    /// <summary>Deep pockets follow the world's blight: caustic pockets or overgrown caverns.</summary>
    private void DeepBlightPockets(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var wanted = N(WorldGenDensity.DeepBlightPockets) * (_hazard == WorldHazard.RadiatedDeeps ? 2 : 1);
        for (var attempt = 0; attempt < wanted * 16 && _pockets.Count < wanted; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var cx = random.Next(4, _width - 4);
            if (NearSpawn(cx, 0.04) || InCoast(cx - 20) || InCoast(cx + 20))
                continue;
            var rx = random.Next(6, Math.Max(7, Math.Min(16, _width / 30)));
            var ry = random.Next(3, Math.Max(4, Math.Min(8, _height / 25)));
            var minY = _rockLine[cx] + ry + 2;
            var maxY = _moltenTop[cx] - ry - 3;
            if (minY > maxY)
                continue;
            var cy = random.Next(minY, maxY + 1);
            var placed = 0;
            for (var y = cy - ry; y <= cy + ry; y++)
            {
                var reach = rx * Math.Sqrt(Math.Max(0, 1 - Math.Pow((y - cy) / (ry + 0.5), 2))) + random.Next(-1, 2);
                for (var x = cx - (int)reach; x <= cx + (int)reach; x++)
                {
                    if (!grid.Contains(x, y) || y >= _height - 1)
                        continue;
                    var tile = grid.TileAt(x, y);
                    if (IsProtected(tile))
                        continue;
                    if (_blight == WorldBlight.Radiated)
                    {
                        if (tile is TileId.Rock or TileId.Soil or TileId.Snow or TileId.DesertSand or TileId.Sand)
                        {
                            grid.SetTile(new GridPoint(x, y), TileId.CrimsonSoil);
                            placed++;
                        }
                    }
                    else
                    {
                        // Overgrown caverns: hollow interior, mossy shell.
                        var dy = (y - cy) / (double)ry;
                        var dx = (x - cx) / (double)Math.Max(1, reach);
                        var inner = dx * dx + dy * dy < 0.45;
                        grid.SetTile(new GridPoint(x, y), inner ? TileId.Air : TileId.OvergrowthRock);
                        placed++;
                    }
                }
            }
            if (placed == 0)
                continue;
            _pockets.Add(new RadiatedPocket(new GridPoint(cx, cy), rx, ry));
            var veins = random.Next(1, 3);
            for (var v = 0; v < veins; v++)
            {
                var vein = _blight == WorldBlight.Radiated ? TileId.CorruptRock : TileId.OvergrowthSoil;
                var host = _blight == WorldBlight.Radiated ? TileId.CrimsonSoil : TileId.OvergrowthRock;
                Blob(grid, random, cx + random.Next(-rx / 2, rx / 2 + 1), cy, 1.6, random.Next(4, 9),
                    random.NextDouble() - 0.5, random.NextDouble() - 0.5, tile => tile == host ? vein : null);
            }
        }
    }

    // ---------------------------------------------------------------- resources

    private void OreVeins(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        void Veins(TileId ore, int count, Func<int, (int Top, int Bottom)> band, double radius, (int, int) steps, bool soilToo)
        {
            for (var i = 0; i < count; i++)
            {
                var x = random.Next(_width);
                var (top, bottom) = band(x);
                if (bottom <= top + 1)
                    continue;
                var y = RandomRow(random, top, bottom);
                Blob(grid, random, x, y, radius * (0.7 + random.NextDouble() * 0.6), random.Next(steps.Item1, steps.Item2),
                    random.NextDouble() - 0.5, random.NextDouble() - 0.5,
                    tile => tile == TileId.Rock || (soilToo && tile == TileId.Soil) ? ore : null, minY: top, maxY: bottom);
            }
        }

        token.ThrowIfCancellationRequested();
        Veins(TileId.CopperOre, N(WorldGenDensity.CopperVeins), x => (_surface[x] + 3, _cavernRow + _height / 10), 1.6, (4, 11), true);
        Veins(TileId.CobaltOre, N(WorldGenDensity.CobaltVeins), x => (_cavernRow, _moltenTop[x] - 3), 1.4, (4, 10), false);
        Veins(TileId.ViridiumOre, N(WorldGenDensity.ViridiumVeins), x => ((int)(_height * 0.68), _moltenTop[x] - 2), 1.3, (3, 9), false);
    }

    private void BiomeOres(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var frozenWidth = Math.Max(1, _frozenEdge[_cavernRow]);
        for (var i = (int)Math.Max(WorldGenDensity.FrozenOreBlobs.Minimum, WorldGenDensity.FrozenOreBlobs.Rate * frozenWidth * _height); i > 0; i--)
        {
            var x = random.Next(frozenWidth);
            var y = RandomRow(random, _surface[x] + 3, _moltenTop[x] - 4);
            Blob(grid, random, x, y, 1.6, random.Next(4, 9), random.NextDouble() - 0.5, random.NextDouble() - 0.5,
                tile => tile is TileId.Snow or TileId.Rock && x < _frozenEdge[Math.Clamp(y, 0, _height - 1)] ? TileId.GlacialOre : null);
        }
        var junkLeft = _junkStart[_cavernRow];
        for (var i = (int)Math.Max(WorldGenDensity.JunkOreBlobs.Minimum, WorldGenDensity.JunkOreBlobs.Rate * (_width - junkLeft) * _height); i > 0; i--)
        {
            var x = random.Next(Math.Min(_width - 1, junkLeft), _width);
            var y = RandomRow(random, _surface[x] + 2, _cavernRow + 10);
            Blob(grid, random, x, y, 1.6, random.Next(4, 9), random.NextDouble() - 0.5, random.NextDouble() - 0.5,
                tile => tile is TileId.DesertSand or TileId.Rock or TileId.Soil ? TileId.ScrapIronOre : null);
        }
        foreach (var pocket in _pockets)
        {
            var host = _blight == WorldBlight.Radiated ? TileId.UraniumOre : TileId.ViridiumOre;
            Blob(grid, random, pocket.Center.X, pocket.Center.Y, 1.8, random.Next(5, 10), random.NextDouble() - 0.5,
                random.NextDouble() - 0.5, tile => tile is TileId.CrimsonSoil or TileId.CorruptRock or TileId.OvergrowthRock ? host : null);
        }
        if (_blight == WorldBlight.Radiated)
        {
            foreach (var zone in Features.BlightZones)
                Blob(grid, random, (zone.Left + zone.Right) / 2, _surface[(zone.Left + zone.Right) / 2] + zone.Depth / 2, 1.6,
                    random.Next(4, 8), random.NextDouble() - 0.5, random.NextDouble() - 0.5,
                    tile => tile is TileId.CrimsonSoil or TileId.CorruptRock ? TileId.UraniumOre : null);
        }
        for (var i = N(WorldGenDensity.PyrociteBlobs); i > 0; i--)
        {
            var x = random.Next(_width);
            var y = RandomRow(random, _moltenTop[x], _height - 2);
            Blob(grid, random, x, y, 1.6, random.Next(4, 9), random.NextDouble() - 0.5, random.NextDouble() - 0.5,
                tile => tile == TileId.IrradiatedRock ? TileId.PyrociteOre : null);
        }
        // Glowing uranium veins threaded through the Irradiated Core.
        for (var i = N(WorldGenDensity.CoreUraniumVeins); i > 0; i--)
        {
            var x = random.Next(_width);
            var y = RandomRow(random, _moltenTop[x], _height - 2);
            Blob(grid, random, x, y, 1.3, random.Next(5, 12), random.NextDouble() - 0.5, random.NextDouble() - 0.5,
                tile => tile is TileId.IrradiatedRock or TileId.CoreCrust ? TileId.UraniumOre : null);
        }
    }

    private void CrystalCaves(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var caves = N(WorldGenDensity.CrystalCaves);
        for (var i = 0; i < caves; i++)
        {
            var x = random.Next(6, _width - 6);
            var y = RandomRow(random, _cavernRow, _moltenTop[x] - 8);
            var r = 3 + random.NextDouble() * 3;
            Stamp(grid, x, y, r, CarveAir);
            var reach = (int)Math.Ceiling(r) + 1;
            for (var py = y - reach; py <= y + reach; py++)
            for (var px = x - reach; px <= x + reach; px++)
            {
                if (!grid.Contains(px, py) || py >= _height - 1)
                    continue;
                var tile = grid.TileAt(px, py);
                if (tile == TileId.Air)
                {
                    if (grid.GetCell(px, py).WallId == 0)
                        grid.SetWall(new GridPoint(px, py), WallIds.Crystal);
                    continue;
                }
                if (tile is TileId.Rock or TileId.Soil or TileId.Snow && random.Next(2) == 0 &&
                    (grid.TileAt(px - 1, py) == TileId.Air || grid.TileAt(px + 1, py) == TileId.Air ||
                     grid.TileAt(px, py - 1) == TileId.Air || grid.TileAt(px, py + 1) == TileId.Air))
                    grid.SetTile(new GridPoint(px, py), TileId.EnergyCrystal);
            }
        }
        // A few lone clusters on ordinary cave faces too.
        for (var i = N(WorldGenDensity.CrystalClusters); i > 0; i--)
        {
            var x = random.Next(1, _width - 1);
            var y = RandomRow(random, _rockLine[x], _moltenTop[x]);
            if (grid.TileAt(x, y) == TileId.Rock && grid.TileAt(x, y - 1) == TileId.Air)
                grid.SetTile(new GridPoint(x, y), TileId.EnergyCrystal);
        }
    }

    private void LifeCores(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var wanted = N(WorldGenDensity.LifeCores);
        for (var attempt = 0; attempt < wanted * 400 && Features.LifeCores.Count < wanted; attempt++)
        {
            var x = random.Next(2, _width - 2);
            var y = RandomRow(random, _rockLine[x], _moltenTop[x] - 2);
            if (!IsCaveFloor(grid, catalog, x, y))
                continue;
            grid.SetTile(new GridPoint(x, y), TileId.LifeCore);
            Features.LifeCores.Add(new GridPoint(x, y));
        }
    }

    private void SupplyCaches(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var caches = N(WorldGenDensity.SupplyCaches);
        for (var attempt = 0; attempt < caches * 200 && Features.SupplyCaches.Count < caches; attempt++)
        {
            var x = random.Next(2, _width - 2);
            var y = RandomRow(random, _surface[x] + 6, _moltenTop[x] - 2);
            var buried = true;
            for (var dy = -1; dy <= 1 && buried; dy++)
            for (var dx = -1; dx <= 1 && buried; dx++)
            {
                var tile = grid.TileAt(x + dx, y + dy);
                buried = tile is TileId.Soil or TileId.Rock or TileId.Snow or TileId.DesertSand or TileId.Sand;
            }
            if (!buried)
                continue;
            grid.SetTile(new GridPoint(x, y), TileId.SupplyCache);
            Features.SupplyCaches.Add(new GridPoint(x, y));
        }

        var pods = N(WorldGenDensity.SalvagePods);
        var placed = 0;
        for (var attempt = 0; attempt < pods * 300 && placed < pods; attempt++)
        {
            var x = random.Next(2, _width - 2);
            var y = RandomRow(random, _surface[x] + 4, _moltenTop[x] - 2);
            if (!IsCaveFloor(grid, catalog, x, y))
                continue;
            grid.SetTile(new GridPoint(x, y), TileId.SalvagePod);
            Features.SalvagePods.Add(new GridPoint(x, y));
            placed++;
        }
    }

    // ---------------------------------------------------------------- structures

    private void SkyIslands(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var islands = N(WorldGenDensity.SkyIslands);
        var minSurface = _surface.Min();
        var ceiling = Math.Max(3, (int)(_height * 0.03));
        var floor = minSurface - 14;
        if (floor - ceiling < 6)
            return;
        for (var attempt = 0; attempt < islands * 20 && Features.Islands.Count < islands; attempt++)
        {
            var width = random.Next(16, Math.Max(17, Math.Min(32, _width / 10)));
            var left = random.Next(4, Math.Max(5, _width - width - 4));
            if (Features.Islands.Any(island => left < island.Right + 12 && left + width > island.Left - 12))
                continue;
            // Keep the sky over the landing site (the centre of the world) clear, as Terraria keeps islands off the spawn.
            var landingGuard = Math.Max(40, _width / 20);
            if (left < _width / 2 + landingGuard && left + width > _width / 2 - landingGuard)
                continue;
            var top = random.Next(ceiling + 6, Math.Max(ceiling + 7, floor - 2));
            var depth = Math.Max(4, width / 4);
            var mid = left + width / 2.0;
            for (var x = left; x < left + width; x++)
            {
                var t = Math.Abs(x - mid) / (width / 2.0);
                var columnDepth = (int)Math.Round(depth * Math.Sqrt(Math.Max(0, 1 - t * t))) + random.Next(0, 2);
                for (var y = top; y <= top + columnDepth; y++)
                    grid.SetTile(new GridPoint(x, y), y == top ? TileId.Grass : y > top + columnDepth - 2 ? TileId.Rock : TileId.Soil);
            }
            // Hut: hull-panel walls, derelict plating roof, a hatch on one side and a cache inside.
            var hutLeft = (int)mid - 3;
            var hutTop = top - 5;
            for (var y = hutTop; y < top; y++)
            for (var x = hutLeft; x <= hutLeft + 6; x++)
            {
                var wall = x == hutLeft || x == hutLeft + 6;
                var roof = y == hutTop;
                var tile = roof ? TileId.DungeonBrick : wall ? TileId.Wood : TileId.Air;
                if (wall && !roof && y >= top - 3 && x == hutLeft + 6)
                    tile = TileId.ShipDoor;
                grid.SetTile(new GridPoint(x, y), tile);
                if (!roof)
                    grid.SetWall(new GridPoint(x, y), WallIds.DerelictHull);
            }
            grid.SetTile(new GridPoint(hutLeft + 2, top - 1), TileId.SupplyCache);
            Features.SupplyCaches.Add(new GridPoint(hutLeft + 2, top - 1));
            grid.SetTile(new GridPoint(hutLeft + 4, top - 1), TileId.SalvagePod);
            Features.SalvagePods.Add(new GridPoint(hutLeft + 4, top - 1));
            // The island house keeps a sky chest by the door.
            grid.SetTile(new GridPoint(hutLeft + 5, top - 1), TileId.SkyChest);
            Features.AddSite(FeatureNames.IslandHouses, hutLeft, hutTop, hutLeft + 6, top - 1);
            Features.Islands.Add(new SkyIsland(left, left + width - 1, top));
        }
    }

    private void Derelict(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var length = Math.Clamp(_width / 30, 24, 52);
        var height = Math.Clamp(length / 4, 7, 12);
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var left = random.Next(_width / 10, Math.Max(_width / 10 + 1, _width - _width / 10 - length));
            var right = left + length;
            if (NearSpawn(left, 0.1) || NearSpawn(right, 0.1) || Features.BlightZones.Any(z => left < z.Right + 6 && right > z.Left - 6))
                continue;
            // The hull ploughed in nose-down: the bottom sinks along the ship.
            var ground = _surface[left];
            var slope = 0.18;
            var top = ground - height / 2;
            for (var i = 0; i <= length; i++)
            {
                var x = left + i;
                var shipTop = top + (int)Math.Round(i * slope);
                var shipBottom = shipTop + height;
                for (var y = shipTop; y <= shipBottom; y++)
                {
                    if (!grid.Contains(x, y) || y >= _height - 2)
                        continue;
                    var shell = y == shipTop || y == shipBottom || i == 0 || i == length;
                    var deck = y == shipTop + height / 2 && i % 9 != 4;
                    var bulkhead = i % 12 == 0 && i > 0 && i < length;
                    TileId tile;
                    if (shell)
                        tile = TileId.DungeonBrick;
                    else if (bulkhead)
                        tile = y >= shipBottom - 3 || (y >= shipTop + height / 2 - 3 && y < shipTop + height / 2) ? TileId.ShipDoor : TileId.Wood;
                    else if (deck)
                        tile = TileId.Wood;
                    else
                        tile = TileId.Air;
                    // A breach on the upper hull lets explorers in.
                    if (y == shipTop && i > length / 3 && i < length / 3 + 3)
                        tile = TileId.Air;
                    grid.SetTile(new GridPoint(x, y), tile);
                    grid.SetWall(new GridPoint(x, y), shell ? WallIds.None : WallIds.DerelictHull);
                }
            }
            for (var c = 0; c < 2; c++)
            {
                var i = length / 4 + c * length / 2;
                var x = left + i;
                var floorY = top + (int)Math.Round(i * slope) + height - 1;
                if (grid.TileAt(x, floorY) == TileId.Air)
                {
                    grid.SetTile(new GridPoint(x, floorY), TileId.SupplyCache);
                    Features.SupplyCaches.Add(new GridPoint(x, floorY));
                }
                var podX = x + 3;
                var podY = top + (int)Math.Round((i + 3) * slope) + height / 2 - 1;
                if (grid.TileAt(podX, podY) == TileId.Air)
                {
                    grid.SetTile(new GridPoint(podX, podY), TileId.SalvagePod);
                    Features.SalvagePods.Add(new GridPoint(podX, podY));
                }
            }
            Features.Derelict = (left, top, right, top + (int)Math.Round(length * slope) + height);
            return;
        }
    }

    private void Greening(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var meadow = Noise(random, _width, 9);
        for (var x = 0; x < _width; x++)
        {
            var y = TopSolid(grid, catalog, x);
            if (grid.TileAt(x, y) != TileId.Soil || IsFrozen(x, y) || IsJunk(x, y))
                continue;
            grid.SetTile(new GridPoint(x, y), meadow[x] > 0.6 && !NearSpawn(x, 0.02) ? TileId.JungleGrass : TileId.Grass);
        }
    }

    // ---------------------------------------------------------------- walls, water, cleanup

    private static readonly (int Dx, int Dy)[] WallProbeOffsets = [(0, 1), (0, -1), (-1, 0), (1, 0), (0, 2), (0, -2)];

    private ushort NaturalWallFor(TileGrid grid, int x, int y)
    {
        var tile = grid.TileAt(x, y);
        if (tile == TileId.Air)
        {
            // Open cells borrow the wall of the nearest solid neighbour.
            foreach (var (dx, dy) in WallProbeOffsets)
            {
                var neighbor = grid.TileAt(x + dx, y + dy, TileId.Air);
                if (neighbor != TileId.Air)
                {
                    tile = neighbor;
                    break;
                }
            }
        }
        // Tile-specific walls come from a table built once per world (20 M lookups on Large).
        var table = _wallTable ??= BuildWallTable();
        if ((int)tile < table.Length && table[(int)tile] != 0)
            return table[(int)tile];
        return y >= _moltenTop[x] ? WallIds.Ash : y < _rockLine[x] ? WallIds.Regolith : WallIds.Rock;
    }

    private ushort[]? _wallTable;

    private ushort[] BuildWallTable()
    {
        var ids = Enum.GetValues<TileId>();
        var table = new ushort[ids.Max(id => (int)id) + 1];
        foreach (var tile in ids)
        {
            if (BlightRules.BlightOf(tile) is not null || BlightRules.IsHeart(tile) || BlightRules.IsAltar(tile))
                table[(int)tile] = (BlightRules.BlightOf(tile) ?? _blight) == WorldBlight.Radiated ? WallIds.Radiated : WallIds.Overgrowth;
            else if (ContentWallFor(tile) is { } contentWall)
                table[(int)tile] = contentWall;
            else
                table[(int)tile] = tile switch
                {
                    TileId.Snow or TileId.GlacialOre => WallIds.Ice,
                    TileId.DesertSand or TileId.ScrapIronOre => WallIds.Scrap,
                    TileId.FungalSoil => WallIds.Spore,
                    TileId.Sand => WallIds.Sand,
                    TileId.EnergyCrystal => WallIds.Crystal,
                    TileId.IrradiatedRock or TileId.CoreCrust or TileId.VitrifiedGlass or TileId.PyrociteOre or
                        TileId.EmberRock or TileId.Ash or TileId.Obsidian => WallIds.Ash,
                    TileId.Soil or TileId.Grass or TileId.JungleGrass => WallIds.Regolith,
                    _ => 0
                };
        }
        return table;
    }

    private void BackgroundWalls(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var depthNoise = Noise(random, _width, 6);
        var wallTop = new int[_width];
        for (var x = 0; x < _width; x++)
            wallTop[x] = _surface[x] + 3 + (int)Math.Round((depthNoise[x] + 1) * 1.5);
        // Bulk write during generation, row by row for cache-friendly access: no per-cell change events (nothing listens yet).
        var cells = grid.RawCells;
        var table = _wallTable ??= BuildWallTable();
        for (var y = 0; y < _height - 1; y++)
        {
            if ((y & 63) == 0)
                token.ThrowIfCancellationRequested();
            var row = y * _width;
            for (var x = 0; x < _width; x++)
            {
                var index = row + x;
                if (y < wallTop[x] || cells[index].WallId != 0)
                    continue;
                var tile = cells[index].TileId;
                if (tile == TileId.Air)
                {
                    // Same probe order as NaturalWallFor, read straight from the cell array.
                    cells[index].WallId = NaturalWallFor(grid, x, y);
                    continue;
                }
                var wall = (int)tile < table.Length ? table[(int)tile] : (ushort)0;
                cells[index].WallId = wall != 0 ? wall : y >= _moltenTop[x] ? WallIds.Ash : y < _rockLine[x] ? WallIds.Regolith : WallIds.Rock;
            }
        }

        // Hole filling near the ragged wall line: an unwalled cell held by walls on three sides gets one.
        for (var pass = 0; pass < 2; pass++)
        {
            for (var x = 1; x < _width - 1; x++)
            {
            var topSolid = TopSolid(grid, catalog, x);
            for (var y = Math.Max(1, wallTop[x] - 5); y < Math.Min(_height - 1, wallTop[x] + 1); y++)
            {
                if (grid.GetCell(x, y).WallId != 0 || y < topSolid)
                    continue;
                var walled = 0;
                if (grid.GetCell(x - 1, y).WallId != 0) walled++;
                if (grid.GetCell(x + 1, y).WallId != 0) walled++;
                if (grid.GetCell(x, y - 1).WallId != 0) walled++;
                if (grid.GetCell(x, y + 1).WallId != 0) walled++;
                if (walled >= 3)
                    grid.SetWall(new GridPoint(x, y), NaturalWallFor(grid, x, y));
            }
            }
        }
    }

    private void SurfaceLakes(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var lakes = N(WorldGenDensity.SurfaceLakes);
        var made = 0;
        for (var attempt = 0; attempt < lakes * 30 && made < lakes; attempt++)
        {
            var center = random.Next(_width / 12, _width - _width / 12);
            var half = random.Next(6, Math.Max(7, Math.Min(16, _width / 40)));
            if (NearSpawn(center, 0.06) || InBlightZone(center, half + 6) ||
                (Features.Dungeon is { } dungeon && center + half > dungeon.Left - 6 && center - half < dungeon.Right + 6) ||
                (Features.Derelict is { } ship && center + half > ship.Left - 4 && center - half < ship.Right + 4))
                continue;
            var level = int.MaxValue;
            for (var x = center - half; x <= center + half; x++)
                level = Math.Min(level, TopSolid(grid, catalog, Math.Clamp(x, 0, _width - 1)));
            var depth = random.Next(3, 7);
            var frozen = IsFrozen(center, level);
            for (var x = center - half; x <= center + half; x++)
            {
                if ((uint)x >= (uint)_width)
                    continue;
                var t = Math.Abs(x - center) / (double)(half + 1);
                var bowl = level + (int)Math.Round(depth * Math.Sqrt(Math.Max(0, 1 - t * t)));
                var top = TopSolid(grid, catalog, x);
                for (var y = top; y <= bowl; y++)
                {
                    if (IsProtected(grid.TileAt(x, y)))
                        continue;
                    grid.SetTile(new GridPoint(x, y), TileId.Air);
                    if (y > level)
                        grid.TryAddLiquid(new GridPoint(x, y), LiquidKind.Water, 1f, catalog);
                }
                if (grid.TileAt(x, bowl + 1) is TileId.Soil or TileId.Grass or TileId.DesertSand)
                    grid.SetTile(new GridPoint(x, bowl + 1), frozen ? TileId.Snow : TileId.Sand);
            }
            Features.AddSite("surface-lakes", center - half, level, center + half, level + depth);
            made++;
        }

        // Cave water: small pools poured onto cave floors.
        var pools = N(WorldGenDensity.CavePools);
        for (var i = 0; i < pools; i++)
        {
            var x = random.Next(1, _width - 1);
            for (var y = RandomRow(random, _rockLine[x], _moltenTop[x] - 2); y < _moltenTop[x] - 1; y++)
            {
                if (IsCaveFloor(grid, catalog, x, y))
                {
                    for (var dx = -2; dx <= 2; dx++)
                        grid.TryAddLiquid(new GridPoint(Math.Clamp(x + dx, 0, _width - 1), y), LiquidKind.Water,
                            0.4f + (float)random.NextDouble() * 0.5f, catalog);
                    break;
                }
            }
        }
    }

    /// <summary>Caves larger than this stay dry when pockets are flooded; a share of the smaller ones fill with water.</summary>
    private const int FloodMaxRegion = 2500;
    private const double FloodChance = 0.35;

    /// <summary>
    /// Water-filled caves (0.3.0): about a third of the enclosed pockets and small caves underground fill with water, as a
    /// share of Terraria's small holes and soil caves do; big caverns stay dry. Pockets touching anything built (furniture,
    /// a chest, a structure, a survivor's spot) or holding liquid already are left alone, and the seas are out of reach.
    /// </summary>
    private void FloodCavePockets(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var seen = new bool[_width * _height];
        var queue = new Queue<int>();
        var cells = new List<int>();
        var survivors = Features.SurvivorSpots.ToHashSet();
        int left = CoastReach, right = _width - CoastReach;
        for (var x = left; x < right; x++)
        {
            if ((x & 63) == 0)
                token.ThrowIfCancellationRequested();
            for (var y = _surface[x] + 12; y < _moltenTop[x] - 10; y++)
            {
                var start = y * _width + x;
                if (seen[start] || !Open(x, y))
                    continue;
                cells.Clear();
                var usable = true;
                int minX = x, maxX = x, minY = y, maxY = y;
                seen[start] = true;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    var cell = queue.Dequeue();
                    int cx = cell % _width, cy = cell / _width;
                    cells.Add(cell);
                    var here = grid.GetCell(cx, cy);
                    if (here.TileId != TileId.Air || here.LiquidUnits > 0 || survivors.Contains(new GridPoint(cx, cy)) ||
                        TilePhysics.Falls(grid.TileAt(cx, cy - 1)))
                        usable = false; // built, wet, a survivor's spot, or sand overhead that would sink into the water
                    minX = Math.Min(minX, cx); maxX = Math.Max(maxX, cx); minY = Math.Min(minY, cy); maxY = Math.Max(maxY, cy);
                    foreach (var (nx, ny) in new[] { (cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1) })
                    {
                        if (nx < left || nx >= right || ny < _surface[nx] + 12 || ny >= _moltenTop[nx] - 10)
                        {
                            usable = false; // reaches the surface band, the seas or the core: not an enclosed pocket
                            continue;
                        }
                        var index = ny * _width + nx;
                        if (seen[index] || !Open(nx, ny))
                            continue;
                        seen[index] = true;
                        queue.Enqueue(index);
                    }
                }
                if (!usable || cells.Count < 6 || cells.Count > FloodMaxRegion || InStructure(minX, minY, maxX, maxY, 4) ||
                    random.NextDouble() >= FloodChance)
                    continue;
                foreach (var cell in cells)
                    grid.TryAddLiquid(new GridPoint(cell % _width, cell / _width), LiquidKind.Water, 1f, catalog);
                Features.AddCount(FeatureNames.FloodedPockets);
            }
        }

        bool Open(int x, int y) => !catalog[grid.TileAt(x, y)].IsSolid;
    }

    private void SettleLiquids(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var simulator = new ActiveLiquidSimulator(grid, catalog) { StepCap = 60_000, PanicThreshold = int.MaxValue };
        try
        {
            simulator.WakeMovable();
            // Bounded: big worlds finish flowing in play, near the player, through the active simulator.
            simulator.RunUntilSettled(Math.Clamp(_height / 4, 120, 400), token);
        }
        finally
        {
            simulator.Detach();
        }
    }

    private void Cleanup(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        for (var x = 0; x < _width; x++)
        {
            token.ThrowIfCancellationRequested();
            // Lone floating specks above the surface and one-cell spikes on it.
            for (var y = 1; y < Math.Min(_height - 1, _surface[x] + 2); y++)
            {
                var tile = grid.TileAt(x, y);
                if (tile == TileId.Air || IsProtected(tile) || !catalog[tile].IsSolid)
                    continue;
                var solidNeighbors = 0;
                if (catalog[grid.TileAt(x - 1, y, TileId.Air)].IsSolid) solidNeighbors++;
                if (catalog[grid.TileAt(x + 1, y, TileId.Air)].IsSolid) solidNeighbors++;
                if (catalog[grid.TileAt(x, y - 1, TileId.Air)].IsSolid) solidNeighbors++;
                if (catalog[grid.TileAt(x, y + 1, TileId.Air)].IsSolid) solidNeighbors++;
                if (solidNeighbors == 0 || (solidNeighbors == 1 && catalog[grid.TileAt(x, y + 1, TileId.Air)].IsSolid &&
                                            !catalog[grid.TileAt(x - 1, y, TileId.Air)].IsSolid &&
                                            !catalog[grid.TileAt(x + 1, y, TileId.Air)].IsSolid && random.Next(2) == 0))
                    grid.SetTile(new GridPoint(x, y), TileId.Air);
            }
            // Bare regolith open to the sky regrows moss in the grassland.
            var top = TopSolid(grid, catalog, x);
            if (grid.TileAt(x, top) == TileId.Soil && !IsFrozen(x, top) && !IsJunk(x, top))
                grid.SetTile(new GridPoint(x, top), TileId.Grass);
            if (grid.TileAt(x, _height - 1) != TileId.Bedrock)
                grid.SetTile(new GridPoint(x, _height - 1), TileId.Bedrock);
        }

        SettleLooseSand(grid, catalog, random, token);
        Layout = BuildLayout();
    }

    private void ChooseLandingSite(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var mid = _width / 2;
        var best = mid;
        for (var offset = 0; offset < _width / 4; offset++)
        {
            foreach (var x in new[] { mid + offset, mid - offset })
            {
                if (x < 3 || x >= _width - 3 || InBlightZone(x, 10) ||
                    Features.Islands.Any(island => x >= island.Left - 3 && x <= island.Right + 3))
                    continue;
                var top = TopSolid(grid, catalog, x);
                if (grid.TileAt(x, top) is not (TileId.Grass or TileId.JungleGrass) ||
                    grid.GetCell(x, top - 1).LiquidDepth > 0f ||
                    Math.Abs(TopSolid(grid, catalog, x - 2) - top) > 1 || Math.Abs(TopSolid(grid, catalog, x + 2) - top) > 1)
                    continue;
                best = x;
                goto Found;
            }
        }
        Found:
        Features.SpawnColumn = best;
        var ground = TopSolid(grid, catalog, best);
        // Clear headroom for the crew, and flatten any smoothing on the landing pad.
        for (var x = best - 6; x <= best + 6; x++)
        for (var y = Math.Max(0, ground - 4); y < ground; y++)
            if (grid.Contains(x, y) && catalog[grid.TileAt(x, y)].IsSolid && !IsProtected(grid.TileAt(x, y)))
                grid.SetTile(new GridPoint(x, y), TileId.Air);
        for (var x = Math.Max(0, best - 10); x <= Math.Min(_width - 1, best + 10); x++)
        for (var y = Math.Max(0, ground - 6); y <= Math.Min(_height - 1, ground + 2); y++)
        {
            var cell = grid.GetCell(x, y);
            if (cell.Shape != TileShape.Full && !TilePhysics.UsesStateBits(cell.TileId))
            {
                cell.Shape = TileShape.Full;
                grid.SetCell(new GridPoint(x, y), cell);
            }
        }
    }

    private BiomeLayout BuildLayout()
    {
        var layout = new BiomeLayout(_width, _height, _frozenEdge, _junkStart, _surface, _moltenTop);
        foreach (var pocket in _pockets)
            layout.AddPocket(pocket);
        return layout;
    }
}
