using Rimtera.Systems;
using Rimtera.UI;

if (args.Contains("--demo", StringComparer.OrdinalIgnoreCase))
{
    ConsoleSandbox.PrintDemo();
    return 0;
}

if (args.Contains("--quickstart", StringComparer.OrdinalIgnoreCase))
{
    // --size Tiny|Small|Medium|Large picks the quickstart world size (default: a small 800x300 test world).
    var sizeIndex = Array.FindIndex(args, arg => string.Equals(arg, "--size", StringComparison.OrdinalIgnoreCase));
    var size = sizeIndex >= 0 && sizeIndex + 1 < args.Length ? args[sizeIndex + 1].ToLowerInvariant() switch
    {
        "tiny" => (1750, 900),
        "small" => (4200, 1200),
        "medium" => (6400, 1800),
        "large" => (8400, 2400),
        _ => (800, 300)
    } : (800, 300);
    // --kit arsenal fills the inventory with one weapon of every style, ammo, armor and wings (play-testing).
    var kitIndex = Array.FindIndex(args, arg => string.Equals(arg, "--kit", StringComparison.OrdinalIgnoreCase));
    var kit = kitIndex >= 0 && kitIndex + 1 < args.Length ? args[kitIndex + 1] : null;
    return GraphicalSandbox.Run(quickstart: true, quickstartSize: size, quickstartKit: kit);
}

if (args.Contains("--continue", StringComparer.OrdinalIgnoreCase))
    return GraphicalSandbox.Run(continueLast: true);

if (args.Contains("--graphics", StringComparer.OrdinalIgnoreCase) || args.Contains("--game", StringComparer.OrdinalIgnoreCase))
    return GraphicalSandbox.Run();

if (args.Contains("--play", StringComparer.OrdinalIgnoreCase))
    return ConsoleSandbox.Run();

if (args.Contains("--menu", StringComparer.OrdinalIgnoreCase))
    return ConsoleMenu.Run();

var previewIndex = Array.FindIndex(args, arg => string.Equals(arg, "--music-preview", StringComparison.OrdinalIgnoreCase));
if (previewIndex >= 0)
    return AudioEngine.RenderMusicPreviews(previewIndex + 1 < args.Length ? args[previewIndex + 1] : "music-preview");

if (args.Contains("--flora-sprites", StringComparer.OrdinalIgnoreCase))
    return TileTextureExport.PrintFlora();

if (args.Contains("--tile-sprites", StringComparer.OrdinalIgnoreCase))
    return TileTextureExport.Print();

if (args.Contains("--entity-sprites", StringComparer.OrdinalIgnoreCase))
    return AnimationCatalog.Print();

if (args.Contains("--balance", StringComparer.OrdinalIgnoreCase))
    return ItemCensus.Balance();

if (args.Contains("--item-census", StringComparer.OrdinalIgnoreCase))
    return ItemCensus.Run();

if (args.Contains("--engine-bench", StringComparer.OrdinalIgnoreCase))
    return EngineBenchmark.Run(args);

if (args.Contains("--batch-a-test", StringComparer.OrdinalIgnoreCase))
    return BatchASelfTests.RunStandalone();

if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
    return CoreSelfTests.Run();

if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("-h", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine("Rimtera - original 2D sandbox simulation");
    Console.WriteLine("  dotnet run -- --graphics   Windowed graphical game (default)");
    Console.WriteLine("  dotnet run -- --quickstart [--size Large]  Graphical game straight into a fresh world");
    Console.WriteLine("  dotnet run -- --continue   Load the most recently played world");
    Console.WriteLine("  dotnet run -- --play       Interactive terminal game");
    Console.WriteLine("  dotnet run -- --menu       Main menu, profile selection, options, and controls");
    Console.WriteLine("  dotnet run -- --demo       Headless simulation summary");
    Console.WriteLine("  dotnet run -- --self-test  Run deterministic core checks");
    return 0;
}

return GraphicalSandbox.Run();
