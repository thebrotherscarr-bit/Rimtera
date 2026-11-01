using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Content-fill flora (docs/TERRARIA_GAPS.md plan item 4): a tree species for every surface biome (canopy vinewood, fuel-coast
/// palms, fungal shroom trees, crystal prismwood, blightwood; the grassland tree, frost pine and rust palm stay), gem trees in
/// the caverns, and harvestable wild plants per biome. Trees reuse the shared tree physics (<see cref="TilePhysics"/>): the
/// species table here only adds ground, palette, crown shape and fell drops. Names, palettes and numbers are original.
/// </summary>
public static class ContentFillFlora
{
    /// <summary>One tree species: ground tiles it grows on, its trunk and crown tiles, crown shape, palette and timber.</summary>
    public sealed record TreeSpecies(string Biome, TileId[] Grounds, TileId Trunk, TileId Canopy, string TrunkName, string CanopyName,
        TileId Shape, (byte R, byte G, byte B) Bark, (byte R, byte G, byte B) Leaf, ItemId Timber);

    public static readonly TreeSpecies[] Species =
    [
        new("canopy", [TileId.CanopyGrass, TileId.Mud], TileId.VinewoodTrunk, TileId.VinewoodCanopy, "Vinewood trunk", "Vinewood crown",
            TileId.TreeCanopy, (74, 58, 34), (34, 120, 46), ItemId.Vinewood),
        new("coast", [TileId.ShellBeach, TileId.ShellSand], TileId.PalmwoodTrunk, TileId.PalmwoodFronds, "Fuel-palm trunk", "Fuel-palm fronds",
            TileId.PalmCanopy, (150, 118, 72), (196, 168, 60), ItemId.Palmwood),
        new("fungal", [TileId.GlowshroomGrass, TileId.FungalSoil, TileId.MyceliumStone], TileId.ShroomStalk, TileId.ShroomCap,
            "Shroom stalk", "Glowing shroom cap", TileId.PalmCanopy, (190, 196, 210), (60, 130, 230), ItemId.Stalkwood),
        new("crystal", [TileId.PrismaticGrass, TileId.HallowGrass, TileId.PearlSand], TileId.PrismwoodTrunk, TileId.PrismwoodCanopy,
            "Prismwood trunk", "Prismwood crown", TileId.PineCanopy, (200, 190, 220), (236, 140, 220), ItemId.Prismwood),
        new("blight", [TileId.OvergrowthSoil, TileId.OvergrowthSand, TileId.OvergrowthIce, TileId.RadiatedSand, TileId.RadiatedIce, TileId.CrimsonSoil],
            TileId.BlightwoodTrunk, TileId.BlightwoodCanopy, "Blightwood trunk", "Blightwood crown", TileId.TreeCanopy, (60, 52, 44),
            (110, 150, 40), ItemId.Blightwood),
    ];

    /// <summary>Gem-tree crowns and the shard each one sheds when felled.</summary>
    public static readonly (TileId Canopy, ItemId Gem, (byte R, byte G, byte B) Leaf)[] GemCrowns =
    [
        (TileId.GemCanopyAmethyst, ItemId.AmethystShard, (170, 90, 230)), (TileId.GemCanopyTopaz, ItemId.TopazShard, (240, 190, 60)),
        (TileId.GemCanopySapphire, ItemId.SapphireShard, (70, 110, 240)), (TileId.GemCanopyEmerald, ItemId.EmeraldShard, (50, 200, 110)),
        (TileId.GemCanopyRuby, ItemId.RubyShard, (220, 50, 70)), (TileId.GemCanopyDiamond, ItemId.DiamondShard, (220, 240, 255))
    ];

    /// <summary>Harvestable wild plants: the plant tile, its name, the material it drops, the grounds it grows on, and whether it glows.</summary>
    public static readonly (TileId Tile, string Name, ItemId Drop, TileId[] Grounds, bool Glows)[] WildPlants =
    [
        (TileId.WildFiberTuft, "Fiber tuft", ItemId.PlantFiber, [TileId.Grass, TileId.JungleGrass], false),
        (TileId.WildFrostLichen, "Frost lichen", ItemId.FrostLichen, [TileId.Snow, TileId.Slush], false),
        (TileId.WildScrapThistle, "Scrap thistle", ItemId.ScrapThistle, [TileId.DesertSand, TileId.ScrapSandstone], false),
        (TileId.WildSeaFennel, "Sea fennel", ItemId.SeaFennel, [TileId.ShellBeach, TileId.ShellSand], false),
        (TileId.WildCanopyPod, "Canopy pod", ItemId.CanopyPod, [TileId.CanopyGrass, TileId.Mud], false),
        (TileId.WildGlowcap, "Glowcap cluster", ItemId.GlowcapCluster, [TileId.GlowshroomGrass, TileId.MyceliumStone], true),
        (TileId.WildPrismBloom, "Prism bloom", ItemId.PrismBloom, [TileId.PrismaticGrass, TileId.HallowGrass, TileId.PearlSand], true),
        (TileId.WildBlightBulb, "Blight bulb", ItemId.BlightBulb, [TileId.OvergrowthSoil, TileId.RadiatedSand, TileId.CrimsonSoil], false)
    ];

    /// <summary>The tree each surface biome grows (the test checks they are all distinct): biome, a ground tile, its trunk.</summary>
    public static readonly (string Biome, TileId Ground, TileId Trunk)[] BiomeTrees =
    [
        ("grassland", TileId.Grass, TileId.TreeTrunk), ("frozen", TileId.Snow, TileId.PineTrunk), ("junk", TileId.DesertSand, TileId.PalmTrunk),
        ("coast", TileId.ShellBeach, TileId.PalmwoodTrunk), ("canopy", TileId.CanopyGrass, TileId.VinewoodTrunk),
        ("fungal", TileId.GlowshroomGrass, TileId.ShroomStalk), ("crystal", TileId.PrismaticGrass, TileId.PrismwoodTrunk),
        ("blight", TileId.OvergrowthSoil, TileId.BlightwoodTrunk), ("caverns", TileId.Rock, TileId.GemTreeTrunk)
    ];

    private static readonly Dictionary<TileId, TreeSpecies> ByGround = Species
        .SelectMany(species => species.Grounds.Select(ground => (ground, species))).ToDictionary(pair => pair.ground, pair => pair.species);

    private static readonly HashSet<TileId> Trunks = Species.Select(species => species.Trunk).Append(TileId.GemTreeTrunk).ToHashSet();
    private static readonly HashSet<TileId> Canopies = Species.Select(species => species.Canopy).Concat(GemCrowns.Select(gem => gem.Canopy)).ToHashSet();


    public static bool IsTrunk(TileId tile) => Trunks.Contains(tile);
    public static bool IsCanopy(TileId tile) => Canopies.Contains(tile);
    public static bool IsWildPlant(TileId tile) => tile is >= TileId.WildFiberTuft and <= TileId.WildBlightBulb;

    /// <summary>The content-fill species a sapling grows into on <paramref name="ground"/>, or null for the legacy species.</summary>
    public static (TileId Trunk, TileId Canopy)? TreeFor(TileId ground) =>
        ByGround.TryGetValue(ground, out var species) ? (species.Trunk, species.Canopy) : null;

    /// <summary>Crown shape a canopy tile uses (the legacy round, pine or palm layout).</summary>
    public static TileId ShapeOf(TileId canopy) =>
        Species.FirstOrDefault(species => species.Canopy == canopy) is { } species ? species.Shape : canopy;

    /// <summary>Bark palette of a content-fill trunk (null keeps the legacy bark).</summary>
    public static (byte R, byte G, byte B)? BarkColor(TileId trunk) =>
        trunk == TileId.GemTreeTrunk ? (96, 96, 108) : Species.FirstOrDefault(species => species.Trunk == trunk)?.Bark;

    /// <summary>Leaf palette of a content-fill crown (null keeps the legacy leaves).</summary>
    public static (byte R, byte G, byte B)? CanopyColor(TileId canopy)
    {
        foreach (var gem in GemCrowns)
            if (gem.Canopy == canopy)
                return gem.Leaf;
        return Species.FirstOrDefault(species => species.Canopy == canopy)?.Leaf;
    }

    /// <summary>The timber a felled trunk yields (legacy trunks keep ordinary timber).</summary>
    public static ItemId TimberFor(TileId trunk) =>
        trunk == TileId.GemTreeTrunk ? ItemId.Gemwood : Species.FirstOrDefault(species => species.Trunk == trunk)?.Timber ?? ItemId.Timber;

    /// <summary>Tile definitions for the trunks, crowns and wild plants.</summary>
    public static IEnumerable<ContentTileDefinition> TileDefinitions()
    {
        const TileTraits plant = TileTraits.Plant | TileTraits.Organic;
        foreach (var species in Species)
        {
            yield return new(species.Trunk, species.TrunkName, false, 30, 0.8f, 39, null, plant, Drop: species.Timber);
            yield return new(species.Canopy, species.CanopyName, false, 6, 0.3f, 39, null,
                species.Canopy is TileId.ShroomCap or TileId.PrismwoodCanopy ? plant | TileTraits.Light : plant);
        }
        yield return new(TileId.GemTreeTrunk, "Geode tree trunk", false, 40, 1.2f, 39, null, plant, Drop: ItemId.Gemwood);
        foreach (var (canopy, gem, _) in GemCrowns)
            yield return new(canopy, $"{gem.ToString().Replace("Shard", string.Empty)} geode crown", false, 8, 0.4f, 39, null,
                plant | TileTraits.Light, Drop: gem);
        foreach (var (tile, name, drop, _, glows) in WildPlants)
            yield return new(tile, name, false, 2, 0.4f, 46, null, glows ? TileTraits.Organic | TileTraits.Light : TileTraits.Organic,
                Furniture: true, Value: 1, Drop: drop);
        // Flora parity: grass plants (the reference's grass, flowers and tall grass) are real tiles that swings and shots cut.
        yield return new(TileId.GrassPlant, "Grass sprouts", false, 1, 0.1f, 39, null, plant | TileTraits.Fragile);
        yield return new(TileId.TallGrassPlant, "Tall grass", false, 1, 0.1f, 39, null, plant | TileTraits.Fragile);
    }

    /// <summary>The wild plant that grows on <paramref name="ground"/>, if any.</summary>
    public static TileId? WildPlantFor(TileId ground)
    {
        foreach (var plant in WildPlants)
            if (Array.IndexOf(plant.Grounds, ground) >= 0)
                return plant.Tile;
        return null;
    }

    /// <summary>
    /// Grows a tree of the given species with its base at (x, y): a trunk of <paramref name="minHeight"/>–<paramref name="maxHeight"/>
    /// cells and a crown of the species' shape. Changes nothing when the space is blocked.
    /// </summary>
    public static bool TryGrowSpecies(TileGrid grid, int x, int y, TileId trunk, TileId canopy, TileId shape, Random random,
        List<GridPoint>? changed = null, int minHeight = 4, int maxHeight = 7)
    {
        if (!grid.Contains(x, y + 1) || !grid.Contains(x, y))
            return false;
        var height = random.Next(minHeight, maxHeight + 1);
        var free = 0;
        while (free < height + 3 && grid.Contains(x, y - free) && grid.TileAt(x, y - free) is TileId.Air or TileId.Sapling &&
               grid.GetCell(x, y - free).LiquidUnits == 0)
            free++;
        height = Math.Min(height, free - 3);
        if (height < minHeight - 1)
            return false;
        var top = y - height + 1;
        var crown = Crown(shape, x, top);
        foreach (var (cx, cy) in crown)
            if (!grid.Contains(cx, cy) || grid.TileAt(cx, cy) != TileId.Air)
                return false;
        for (var ty = y; ty >= top; ty--)
            Place(grid, x, ty, trunk, changed);
        foreach (var (cx, cy) in crown)
            Place(grid, cx, cy, canopy, changed);
        return true;
    }

    private static List<(int X, int Y)> Crown(TileId shape, int x, int top)
    {
        var cells = new List<(int, int)>();
        if (shape == TileId.PalmCanopy)
        {
            cells.Add((x, top - 1));
            for (var dx = -3; dx <= 3; dx++)
                if (dx != 0)
                    cells.Add((x + dx, top + (Math.Abs(dx) == 3 ? 1 : 0)));
            return cells;
        }
        for (var dy = -2; dy <= -1; dy++)
        for (var dx = -1; dx <= 1; dx++)
            cells.Add((x + dx, top + dy));
        cells.Add((x - 1, top));
        cells.Add((x + 1, top));
        return cells;
    }

    private static void Place(TileGrid grid, int x, int y, TileId tile, List<GridPoint>? changed)
    {
        var point = new GridPoint(x, y);
        grid.SetTile(point, tile);
        changed?.Add(point);
    }

    /// <summary>Grows a gem tree (crown picked from the column) on cave rock at (x, y).</summary>
    public static bool TryGrowGemTree(TileGrid grid, int x, int y, Random random, List<GridPoint>? changed = null)
    {
        var crown = GemCrowns[Math.Abs(x * 7 + y * 13) % GemCrowns.Length].Canopy;
        return TryGrowSpecies(grid, x, y, TileId.GemTreeTrunk, crown, TileId.TreeCanopy, random, changed, 3, 5);
    }

    private static bool NearAny(TileGrid grid, int x, int y, int radius, Func<TileId, bool> match)
    {
        for (var dx = -radius; dx <= radius; dx++)
        for (var dy = -1; dy <= 1; dy++)
            if (grid.Contains(x + dx, y + dy) && match(grid.TileAt(x + dx, y + dy)))
                return true;
        return false;
    }

    private static bool OpenFloor(TileGrid grid, TileCatalog catalog, int x, int y) =>
        grid.Contains(x, y) && grid.Contains(x, y + 1) && grid.TileAt(x, y) == TileId.Air && grid.GetCell(x, y).LiquidUnits == 0 &&
        catalog[grid.TileAt(x, y + 1)].IsSolid && grid.GetCell(x, y + 1).Shape == TileShape.Full;

    /// <summary>
    /// World-generation pass: biome trees where the legacy flora pass leaves gaps (blight scars and coasts), shroom trees on
    /// glowshroom floors, gem trees on deep cave rock, and wild plants on every biome's open ground.
    /// </summary>
    public static void Generate(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var width = grid.Width;
        var height = grid.Height;
        // Surface: biome trees and wild plants on the first solid ground of each column.
        // The Fracture's two scars start from the open surface of these columns (GameSimulation.TriggerFracture), so they stay clear.
        var scarColumns = new[] { (int)(width * 0.22), (int)(width * 0.78) };
        for (var x = 4; x < width - 4; x++)
        {
            token.ThrowIfCancellationRequested();
            if (scarColumns.Any(scar => Math.Abs(scar - x) <= 3))
                continue;
            var top = 1;
            while (top < height - 1 && !catalog[grid.TileAt(x, top)].IsSolid)
                top++;
            if (top >= height - 1 || !OpenFloor(grid, catalog, x, top - 1))
                continue;
            var ground = grid.TileAt(x, top);
            if (ByGround.TryGetValue(ground, out var species) && random.Next(5) == 0 &&
                !NearAny(grid, x, top - 1, 3, tile => TilePhysics.IsTrunk(tile)))
            {
                TryGrowSpecies(grid, x, top - 1, species.Trunk, species.Canopy, species.Shape, random);
                continue;
            }
            if (WildPlantFor(ground) is { } wild && random.Next(7) == 0 && !NearAny(grid, x, top - 1, 2, IsWildPlant))
                grid.SetTile(new GridPoint(x, top - 1), wild);
        }
        // Underground: shroom trees on glowshroom floors, gem trees on deep cave rock, glowcaps and other wild plants.
        var attempts = width * height / 90;
        for (var i = 0; i < attempts; i++)
        {
            var x = random.Next(4, width - 4);
            var y = random.Next(height / 4, height - 4);
            if (!OpenFloor(grid, catalog, x, y))
                continue;
            var ground = grid.TileAt(x, y + 1);
            if (ground is TileId.GlowshroomGrass or TileId.MyceliumStone && random.Next(3) == 0 &&
                !NearAny(grid, x, y, 3, tile => TilePhysics.IsTrunk(tile)))
            {
                if (TryGrowSpecies(grid, x, y, TileId.ShroomStalk, TileId.ShroomCap, TileId.PalmCanopy, random, null, 3, 6))
                    continue;
            }
            if (ground == TileId.Rock && y > height * 0.45 && random.Next(10) == 0 &&
                !NearAny(grid, x, y, 4, tile => TilePhysics.IsTrunk(tile)) && TryGrowGemTree(grid, x, y, random))
                continue;
            if (WildPlantFor(ground) is { } wild && random.Next(4) == 0 && !NearAny(grid, x, y, 2, IsWildPlant))
                grid.SetTile(new GridPoint(x, y), wild);
        }
    }

    /// <summary>
    /// World-generation grass plants (the reference's "Grass Plants" and jungle and mushroom plant passes): the open top of
    /// most plant-bearing ground on the surface carries a grass plant, some of them grown tall, and cave jungle and glowshroom
    /// floors get theirs too. The ticker keeps them growing afterwards (see <see cref="FloraParity.PlantOneIn"/>).
    /// </summary>
    public static void GenerateGrassPlants(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        var width = grid.Width;
        var height = grid.Height;
        var surface = FloraParity.SurfaceRow(height);
        var cells = grid.RawCells; // row-major; read directly so the scan over every cell stays cheap on large worlds
        for (var y = 1; y < height - 1; y++)
        {
            token.ThrowIfCancellationRequested();
            var row = y * width;
            for (var x = 1; x < width - 1; x++)
            {
                ref readonly var cell = ref cells[row + x];
                var ground = cell.TileId;
                if (!FloraParity.IsPlantGround(ground))
                    continue;
                ref readonly var above = ref cells[row - width + x];
                if (above.TileId != TileId.Air || above.LiquidUnits > 0 || cell.Shape != TileShape.Full || !catalog[ground].IsSolid)
                    continue;
                // Surface ground is carpeted; below the surface line only jungle and glowshroom floors grow plants, more sparsely.
                var chance = y < surface ? 3 : ground is TileId.JungleGrass or TileId.CanopyGrass or TileId.GlowshroomGrass ? 4 : 0;
                if (chance == 0 || random.Next(chance) == 0)
                    continue;
                grid.SetTile(new GridPoint(x, y - 1), random.Next(4) == 0 ? TileId.TallGrassPlant : TileId.GrassPlant);
            }
        }
    }

    /// <summary>
    /// World-ticker growth for the cell (x, y) holding <paramref name="tile"/>: open ground of a plant biome slowly regrows its
    /// wild plant, and bare deep cave rock very rarely sprouts a gem tree. Returns true when something grew. Rolls come from
    /// <paramref name="growth"/>, the world's own stream (the world ticker's), never a shared one.
    /// </summary>
    public static bool TickGrowth(TileGrid grid, TileCatalog catalog, int x, int y, TileId tile, List<GridPoint> changed, Random growth)
    {
        if (!grid.Contains(x, y - 1) || grid.TileAt(x, y - 1) != TileId.Air || grid.GetCell(x, y - 1).LiquidUnits > 0 ||
            !catalog[tile].IsSolid)
            return false;
        if (WildPlantFor(tile) is { } wild)
        {
            if (growth.Next(160) != 0 || NearAny(grid, x, y - 1, 4, IsWildPlant))
                return false;
            grid.SetTile(new GridPoint(x, y - 1), wild);
            changed.Add(new GridPoint(x, y - 1));
            return true;
        }
        if (tile == TileId.Rock && y > grid.Height * 0.45 && growth.Next(400) == 0 &&
            !NearAny(grid, x, y - 1, 4, candidate => TilePhysics.IsTrunk(candidate)))
            return TryGrowGemTree(grid, x, y - 1, growth, changed);
        return false;
    }
}
