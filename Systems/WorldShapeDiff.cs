namespace Rimtera.Systems;

/// <summary>
/// <c>--worldgen-diff</c> (0.3.0): generates a Rimtera world and the Terraria-style reference at the same size and seed,
/// and prints their shape numbers side by side (docs/WORLDGEN_DIFF.md §4 explains each one).
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
        var rimtera = WorldShape.Measure(new GameSimulation(seed: seed, width: width, height: height, blight: WorldBlight.Overgrowth));
        foreach (var (theirs, ours) in reference.Lines().Zip(rimtera.Lines()))
        {
            Console.WriteLine($"  reference  {theirs}");
            Console.WriteLine($"  rimtera    {ours}");
        }
        return 0;
    }
}
