namespace Rimtera.Systems;

/// <summary>
/// <c>--worldgen-diff</c> (0.3.0): generates a Rimtera world and the Terraria-style reference at the same size and seed,
/// and prints their shape numbers side by side, then (0.3.1) its contents against the reference ranges (docs/WORLDGEN_DIFF.md §4–5).
/// </summary>
public static class WorldShapeDiff
{
    public static int Report(string[] args)
    {
        string? Option(string name) => args.SkipWhile(arg => !string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var (width, height) = (Option("--size") ?? "Small").ToLowerInvariant() switch
        {
            "tiny" => (1750, 900),
            "medium" => (6400, 1800),
            "large" => (8400, 2400),
            _ => (4200, 1200)
        };
        var seed = int.TryParse(Option("--seed"), out var parsed) ? parsed : 31337;
        Console.WriteLine($"World shape, {width}×{height}, seed {seed}");
        var reference = WorldShapeReference.Measure(width, height, seed);
        var game = new GameSimulation(seed: seed, width: width, height: height, blight: WorldBlight.Overgrowth);
        var rimtera = WorldShape.Measure(game);
        foreach (var (theirs, ours) in reference.Lines().Zip(rimtera.Lines()))
        {
            Console.WriteLine($"  reference  {theirs}");
            Console.WriteLine($"  rimtera    {ours}");
        }
        Console.WriteLine("World contents (0.3.1; reference figures approximate)");
        var census = WorldCensus.Take(game);
        foreach (var line in census.Lines())
            Console.WriteLine($"  rimtera    {line}");
        foreach (var (inRange, line) in WorldCensusReference.Compare(census, width, height))
            Console.WriteLine($"  {(inRange ? "ok " : "OFF")}        {line}");
        return 0;
    }
}
