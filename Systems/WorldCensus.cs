using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// What generation put in a world (0.3.1), by layer: chests by kind, pots, life cores, altars and ore. Layers follow
/// Terraria's: sky (well above the ground line), surface (from there down 40 tiles), underground (to 10% of the height
/// below the ground line), caverns (down to the core's top) and the underworld (the Irradiated Core). Counts are given
/// per world and, for ore, in cells per million cells of the layer, so worlds of any size compare.
/// </summary>
public sealed record WorldCensusReport(
    IReadOnlyDictionary<string, int> ChestsByLayer,
    IReadOnlyDictionary<TileId, int> ChestsByKind,
    int Pots,
    int LifeCores,
    int Altars,
    IReadOnlyDictionary<string, IReadOnlyDictionary<TileId, double>> OrePerMillion)
{
    public static readonly string[] Layers = ["sky", "surface", "underground", "caverns", "underworld"];

    public int Chests => ChestsByLayer.Values.Sum();

    public IEnumerable<string> Lines()
    {
        yield return $"chests {Chests}: " + string.Join(", ", Layers.Select(layer => $"{layer} {ChestsByLayer.GetValueOrDefault(layer)}"));
        yield return "chest kinds: " + string.Join(", ", ChestsByKind.OrderByDescending(pair => pair.Value).Select(pair => $"{pair.Key} {pair.Value}"));
        yield return $"pots {Pots}, life cores {LifeCores}, altars {Altars}";
        foreach (var layer in Layers)
            if (OrePerMillion.TryGetValue(layer, out var ores) && ores.Count > 0)
                yield return $"ore per million cells, {layer}: " + string.Join(", ", ores.OrderByDescending(pair => pair.Value).Select(pair => $"{pair.Key} {pair.Value:0}"));
    }
}

public static class WorldCensus
{
    /// <summary>The ore tiles a census counts.</summary>
    public static readonly TileId[] Ores =
    [
        TileId.ScrapIronOre, TileId.CopperOre, TileId.GlacialOre, TileId.UraniumOre, TileId.PyrociteOre, TileId.CobaltOre,
        TileId.ViridiumOre, TileId.LumeniteOre
    ];

    public static WorldCensusReport Take(GameSimulation game)
    {
        var world = game.World;
        var tiles = game.Tiles;
        int width = world.Width, height = world.Height;
        var ground = WorldShape.Ground(width, height, (x, y) =>
            world.GetCell(new GridPoint(x, y)) is var cell && (cell.LiquidDepth > 0.2f ||
                (cell.TileId != TileId.Air && tiles.Contains(cell.TileId) && tiles[cell.TileId].IsSolid)));
        var molten = game.BiomeLayout.MoltenRows;
        string Layer(int x, int y) =>
            y < ground[x] - 20 ? "sky"
            : y < ground[x] + 40 ? "surface"
            : y < ground[x] + height / 10 ? "underground"
            : y < molten[x] ? "caverns"
            : "underworld";

        var chestsByLayer = new Dictionary<string, int>();
        var chestsByKind = new Dictionary<TileId, int>();
        var oreCells = new Dictionary<string, Dictionary<TileId, int>>();
        var layerCells = new Dictionary<string, long>();
        var ores = Ores.ToHashSet();
        int pots = 0, cores = 0, altars = 0;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var tile = world.GetCell(new GridPoint(x, y)).TileId;
            var layer = Layer(x, y);
            layerCells[layer] = layerCells.GetValueOrDefault(layer) + 1;
            if (tile == TileId.Air)
                continue;
            if (ContentTiles.Has(tile, TileTraits.Container))
            {
                chestsByLayer[layer] = chestsByLayer.GetValueOrDefault(layer) + 1;
                chestsByKind[tile] = chestsByKind.GetValueOrDefault(tile) + 1;
            }
            else if (TilePhysics.IsPot(tile))
                pots++;
            else if (tile == TileId.LifeCore)
                cores++;
            else if (tile is TileId.RadAltar or TileId.RootAltar)
                altars++;
            else if (ores.Contains(tile))
            {
                if (!oreCells.TryGetValue(layer, out var counts))
                    oreCells[layer] = counts = new Dictionary<TileId, int>();
                counts[tile] = counts.GetValueOrDefault(tile) + 1;
            }
        }
        var orePerMillion = oreCells.ToDictionary(pair => pair.Key, pair => (IReadOnlyDictionary<TileId, double>)pair.Value
            .ToDictionary(ore => ore.Key, ore => ore.Value * 1_000_000.0 / Math.Max(1, layerCells.GetValueOrDefault(pair.Key))));
        // Altars are several cells each: count the generator's record of them where there is one.
        if (game.Features is { } features)
            altars = features.Altars.Count;
        return new WorldCensusReport(chestsByLayer, chestsByKind, pots, cores, altars, orePerMillion);
    }
}

/// <summary>
/// What a Terraria 1.4 world of the same size holds (0.3.1), as ranges for comparing a <see cref="WorldCensusReport"/>.
/// Reconstructed from the structure of 1.4's chest, pot, life crystal, altar and "Shinies" passes and scaled by area from a
/// small (4200×1200) world; every figure is approximate and the ranges are deliberately wide. Used only for measuring.
/// </summary>
public static class WorldCensusReference
{
    public sealed record Target(string Name, double Low, double High, Func<WorldCensusReport, double> Measure);

    /// <summary>Total ore cells per million cells of each layer (all metals together), small world.</summary>
    public static readonly IReadOnlyDictionary<string, double> OrePerMillion = new Dictionary<string, double>
    {
        ["surface"] = 22_554, ["underground"] = 23_589, ["caverns"] = 24_534
    };

    public static IReadOnlyList<Target> Targets(int width, int height)
    {
        var scale = width * (double)height / (4200.0 * 1200.0);
        double Ore(WorldCensusReport report, string layer) =>
            report.OrePerMillion.TryGetValue(layer, out var ores) ? ores.Values.Sum() : 0;
        int Chests(WorldCensusReport report, string layer) => report.ChestsByLayer.GetValueOrDefault(layer);
        return
        [
            new("chests", 150 * scale, 300 * scale, report => report.Chests),
            new("chests underground and in the caverns", 100 * scale, 220 * scale,
                report => Chests(report, "underground") + Chests(report, "caverns")),
            new("dungeon chests", 15 * Math.Sqrt(scale), 35 * Math.Sqrt(scale), report => report.ChestsByKind.GetValueOrDefault(TileId.DungeonChest)),
            new("underworld chests", 5 * Math.Sqrt(scale), 14 * Math.Sqrt(scale), report => Chests(report, "underworld")),
            new("pots", 600 * scale, 1500 * scale, report => report.Pots),
            new("life cores", 35 * scale, 110 * scale, report => report.LifeCores),
            new("altars", 10 * scale, 32 * scale, report => report.Altars),
            .. OrePerMillion.Select(pair => new Target($"ore per million, {pair.Key}", pair.Value * 0.8, pair.Value * 1.2,
                report => Ore(report, pair.Key)))
        ];
    }

    /// <summary>One line per target: the measured value, the reference range and whether it is in range.</summary>
    public static IEnumerable<(bool InRange, string Line)> Compare(WorldCensusReport report, int width, int height) =>
        Targets(width, height).Select(target => target.Measure(report) is var value && value >= target.Low && value <= target.High
            ? (true, $"{target.Name}: {value:0} (reference {target.Low:0}–{target.High:0})")
            : (false, $"{target.Name}: {value:0} (reference {target.Low:0}–{target.High:0}) OUT OF RANGE"));
}
