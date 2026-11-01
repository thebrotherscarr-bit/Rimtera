using System.Diagnostics;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// `dotnet run -- --engine-bench`: generation timings per world size, tick cost on a Large world,
/// and compressed versus JSON save size. Diagnostics only; not part of the self-test.
/// </summary>
public static class EngineBenchmark
{
    public static int Run(string[] args)
    {
        if (args.Contains("--leak", StringComparer.OrdinalIgnoreCase))
            return EngineLeakCheck.Run(args.Contains("--small") ? 800 : 8400, args.Contains("--small") ? 300 : 2400);
        if (args.Contains("--scan", StringComparer.OrdinalIgnoreCase))
            return Scan();
        var mapIndex = Array.FindIndex(args, arg => string.Equals(arg, "--map", StringComparison.OrdinalIgnoreCase));
        var sizes = new (string Name, int Width, int Height)[]
        {
            ("Tiny", 1750, 900), ("Small", 4200, 1200), ("Medium", 6400, 1800), ("Large", 8400, 2400)
        };
        if (mapIndex >= 0 && mapIndex + 1 < args.Length)
        {
            var sizeIndex = Array.FindIndex(args, arg => string.Equals(arg, "--size", StringComparison.OrdinalIgnoreCase));
            var mapSize = args.Contains("--small") ? (160, 72) : (1600, 500);
            if (sizeIndex >= 0 && sizeIndex + 1 < args.Length &&
                sizes.FirstOrDefault(size => string.Equals(size.Name, args[sizeIndex + 1], StringComparison.OrdinalIgnoreCase)) is { Width: > 0 } named)
                mapSize = (named.Width, named.Height);
            return Map(args[mapIndex + 1], args.Contains("--radiated") ? WorldBlight.Radiated : WorldBlight.Overgrowth, mapSize);
        }
        var onlyIndex = Array.FindIndex(args, arg => string.Equals(arg, "--only", StringComparison.OrdinalIgnoreCase));
        if (onlyIndex >= 0 && onlyIndex + 1 < args.Length)
            sizes = sizes.Where(size => args[onlyIndex + 1].Split(',').Contains(size.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
        var titleMegabytes = Process.GetCurrentProcess().PrivateMemorySize64 / 1048576.0;
        var generationSeconds = 0.0;
        GameSimulation? large = null;
        foreach (var (name, width, height) in sizes)
        {
            var watch = Stopwatch.StartNew();
            var sim = new GameSimulation(seed: 20261020, width: width, height: height, blight: WorldBlight.Random);
            watch.Stop();
            generationSeconds = watch.Elapsed.TotalSeconds;
            Console.WriteLine($"{name} {width}x{height}: {watch.Elapsed.TotalSeconds:0.00} s total, blight {sim.Blight}, " +
                              $"hearts {sim.Features!.Hearts.Count}, altars {sim.Features.Altars.Count}, zones {sim.Features.BlightZones.Count}, " +
                              $"life cores {sim.Features.LifeCores.Count}, caches {sim.Features.SupplyCaches.Count}, pods {sim.Features.SalvagePods.Count}, " +
                              $"islands {sim.Features.Islands.Count}, derelict {(sim.Features.Derelict is null ? "no" : "yes")}");
            Console.WriteLine("    " + FeatureSummary(sim.Features));
            foreach (var (pass, ms) in sim.Features.PassMilliseconds)
                Console.WriteLine($"    {pass,-32} {ms,8:0.0} ms");
            large = sim;
        }

        var game = large!;
        for (var i = 0; i < 120; i++)
            game.Tick(new GameInput(new Entities.MovementInput(0f)));
        var ticks = 600;
        var tickWatch = Stopwatch.StartNew();
        for (var i = 0; i < ticks; i++)
        {
            game.Player.Heal(game.Player.MaximumHitPoints);
            game.Tick(new GameInput(new Entities.MovementInput(i % 200 < 100 ? 1f : -1f)));
        }
        tickWatch.Stop();
        Console.WriteLine($"Large tick: {tickWatch.Elapsed.TotalMilliseconds / ticks:0.000} ms/tick over {ticks} ticks " +
                          $"(liquid active {game.Liquids.ActiveCount}, fallbacks {game.Liquids.FallbackSettles}, " +
                          $"grass {game.Ticker.GrassRegrowths}, blight {game.Ticker.BlightConversions})");

        // Flood: dump a 60×50 block of water into the sky near the colony and time the ticks while it flows.
        var floodLeft = game.SpawnColumn + 30;
        var floodTop = Math.Max(1, game.BiomeLayout.SurfaceAt(floodLeft) - 60);
        for (var y = floodTop; y < floodTop + 50; y++)
        for (var x = floodLeft; x < floodLeft + 60; x++)
            game.World.TryAddLiquid(new GridPoint(x, y), LiquidKind.Water, 1f, game.Tiles);
        var floodWatch = Stopwatch.StartNew();
        var worst = 0.0;
        for (var i = 0; i < 600; i++)
        {
            var one = Stopwatch.StartNew();
            game.Player.Heal(game.Player.MaximumHitPoints);
            game.Tick(new GameInput(new Entities.MovementInput(0f)));
            worst = Math.Max(worst, one.Elapsed.TotalMilliseconds);
        }
        floodWatch.Stop();
        Console.WriteLine($"Large tick while 3000 water cells flow: {floodWatch.Elapsed.TotalMilliseconds / 600:0.000} ms/tick " +
                          $"(worst {worst:0.0} ms, peak active {game.Liquids.PeakActiveCount}, still active {game.Liquids.ActiveCount}, " +
                          $"fallbacks {game.Liquids.FallbackSettles})");

        Console.WriteLine($"Before save: {Megabytes()} MiB managed, {Process.GetCurrentProcess().PrivateMemorySize64 / 1048576} MiB private, 64-bit {Environment.Is64BitProcess}");
        var saveWatch = Stopwatch.StartNew();
        GameSnapshot snapshot;
        try
        {
            snapshot = game.CaptureSnapshot();
        }
        catch (OutOfMemoryException error)
        {
            Console.WriteLine(error);
            return 1;
        }
        saveWatch.Stop();
        var options = new System.Text.Json.JsonSerializerOptions
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
        // Raw cells as JSON would be gigabytes on Large (the old comparison ran out of memory); report the raw size instead.
        var compressedJson = System.Text.Json.JsonSerializer.Serialize(snapshot, options);
        Console.WriteLine($"Save: compressed snapshot {compressedJson.Length / 1024.0 / 1024.0:0.0} MiB (encode {saveWatch.Elapsed.TotalMilliseconds:0} ms) " +
                          $"vs raw cells {game.World.CellBytes / 1024.0 / 1024.0:0} MiB; saved generation metadata: {snapshot.WorldMeta is not null}");
        Console.WriteLine($"Memory with one Large world: {Megabytes()} MiB managed, {Process.GetCurrentProcess().PrivateMemorySize64 / 1048576} MiB private");
        var loadWatch = Stopwatch.StartNew();
        var restored = GameSimulation.Restore(System.Text.Json.JsonSerializer.Deserialize<GameSnapshot>(compressedJson, options)!);
        loadWatch.Stop();
        var sameFeatures = restored.Features is { } loaded && loaded.Dungeon == game.Features!.Dungeon && loaded.Cabins.Count == game.Features.Cabins.Count &&
                           loaded.Hearts.Count == game.Features.Hearts.Count;
        Console.WriteLine($"Load (decode, no regeneration): {loadWatch.Elapsed.TotalSeconds:0.00} s; identical cells: " +
                          $"{restored.World.Cells.SequenceEqual(game.World.Cells)}; features restored: {sameFeatures}");
        var oldWorld = new WeakReference(game);
        large = null;
        game = null!;
        snapshot = null!;
        compressedJson = null;
        GC.KeepAlive(restored);
        Console.WriteLine($"Memory after load (old world and save text released): {Megabytes()} MiB managed; old world collected: {!oldWorld.IsAlive}");
        return args.Contains("--budget", StringComparer.OrdinalIgnoreCase)
            ? Budget(generationSeconds, tickWatch.Elapsed.TotalMilliseconds / ticks, worst, titleMegabytes)
            : 0;
    }

    /// <summary>
    /// The soft performance budgets (roadmap 0.2.0 PE-20, for a Small world): generation 8 s, a tick 5 ms, the worst flood
    /// tick 50 ms, and 450 MB before any world exists. Every number is reported (and written to the CI step summary);
    /// only a value past twice its budget fails.
    /// </summary>
    public static int Budget(double generationSeconds, double tickMilliseconds, double floodWorstMilliseconds, double titleMegabytes)
    {
        var rows = new (string Name, double Value, double Limit, string Unit)[]
        {
            ("World generation", generationSeconds, 8, "s"), ("Tick", tickMilliseconds, 5, "ms"),
            ("Worst tick in a flood", floodWorstMilliseconds, 50, "ms"), ("Memory before a world", titleMegabytes, 450, "MB")
        };
        var lines = new List<string> { "| Measure | Value | Budget | |", "|---|---|---|---|" };
        var failed = false;
        foreach (var (name, value, limit, unit) in rows)
        {
            var verdict = value <= limit ? "within" : value <= 2 * limit ? "over (soft)" : "over 2× (fails)";
            failed |= value > 2 * limit;
            lines.Add($"| {name} | {value:0.00} {unit} | {limit:0} {unit} | {verdict} |");
        }
        foreach (var line in lines)
            Console.WriteLine(line);
        if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary)
            File.AppendAllLines(summary, new[] { "### Engine bench (Small)", "" }.Concat(lines));
        return failed ? 1 : 0;
    }

    /// <summary>Every feature count a detailed world records, on one line (structures, sites and scattered tallies).</summary>
    public static string FeatureSummary(WorldFeatures features)
    {
        var parts = new List<string>
        {
            $"cabins {features.Cabins.Count}", $"giant trees {features.GiantTrees.Count}", $"shrines {features.Shrines.Count}",
            $"survivors {features.SurvivorSpots.Count}", $"pots {features.Pots}", $"trees {features.Trees}", $"herbs {features.Herbs}",
            $"canopies {features.Canopies.Count}", $"hives {features.BloomHives.Count}", $"fungal hollows {features.FungalHollows.Count}",
            $"geodes {features.GeodeChambers.Count}", $"basalt {features.BasaltColumns.Count}", $"alabaster {features.AlabasterHalls.Count}",
            $"warrens {features.MiteWarrens.Count}", $"coasts {features.AcidCoasts.Count} ({string.Join("/", features.AcidCoasts.Select(c => c.Right - c.Left + 1))} wide)",
            $"dungeon {(features.Dungeon is null ? "no" : "yes")}", $"vault {(features.Vault is null ? "no" : "yes")}",
            $"chests {features.Chests.Count}", $"trap wires {features.TrapWires.Count}"
        };
        parts.AddRange(features.Sites.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key} {pair.Value.Count}"));
        parts.AddRange(features.Counts.OrderBy(pair => pair.Key).Select(pair => $"#{pair.Key} {pair.Value}"));
        return string.Join(", ", parts);
    }

    private static long Megabytes()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: true) / 1048576;
    }

    /// <summary>Writes a one-pixel-per-tile PNG overview of a generated world (walls dimmed behind air).</summary>
    private static int Map(string path, WorldBlight blight, (int Width, int Height) size)
    {
        var sim = new GameSimulation(seed: 20261020, width: size.Width, height: size.Height, blight: blight);
        using var bitmap = new System.Drawing.Bitmap(sim.World.Width, sim.World.Height);
        for (var y = 0; y < sim.World.Height; y++)
        for (var x = 0; x < sim.World.Width; x++)
        {
            var cell = sim.World.GetCell(x, y);
            var color = cell.TileId switch
            {
                _ when cell.LiquidKind == LiquidKind.Phase && cell.LiquidUnits > 0 => System.Drawing.Color.FromArgb(255, 80, 255),
                TileId.Air when cell.LiquidKind == LiquidKind.Water => System.Drawing.Color.FromArgb(40, 90, 220),
                TileId.Air when cell.LiquidKind == LiquidKind.Fuel => System.Drawing.Color.FromArgb(225, 170, 40),
                TileId.CharSlag => System.Drawing.Color.FromArgb(50, 40, 30),
                TileId.Air when cell.LiquidKind == LiquidKind.Lava => System.Drawing.Color.FromArgb(90, 255, 60), // liquid uranium
                TileId.Air when cell.LiquidKind == LiquidKind.Acid => System.Drawing.Color.FromArgb(190, 230, 20),
                TileId.Air when cell.LiquidKind == LiquidKind.Nectar => System.Drawing.Color.FromArgb(240, 180, 40),
                TileId.Air when cell.WallId != 0 => System.Drawing.Color.FromArgb(45, 40, 38),
                TileId.Air => System.Drawing.Color.FromArgb(150, 200, 240),
                TileId.Soil => System.Drawing.Color.FromArgb(120, 85, 55),
                TileId.Grass or TileId.JungleGrass => System.Drawing.Color.FromArgb(60, 170, 60),
                TileId.Rock => System.Drawing.Color.FromArgb(110, 110, 115),
                TileId.Snow => System.Drawing.Color.FromArgb(225, 235, 245),
                TileId.DesertSand => System.Drawing.Color.FromArgb(170, 120, 70),
                TileId.Sand => System.Drawing.Color.FromArgb(220, 200, 130),
                TileId.EmberRock => System.Drawing.Color.FromArgb(90, 40, 35),
                TileId.Ash => System.Drawing.Color.FromArgb(70, 70, 70),
                TileId.IrradiatedRock => System.Drawing.Color.FromArgb(45, 70, 45),
                TileId.CoreCrust => System.Drawing.Color.FromArgb(85, 100, 70),
                TileId.VitrifiedGlass => System.Drawing.Color.FromArgb(30, 120, 90),
                TileId.UraniumOre => System.Drawing.Color.FromArgb(170, 255, 90),
                TileId.PyrociteOre => System.Drawing.Color.FromArgb(230, 120, 60),
                TileId.HullPlatingHazard or TileId.HullPlatingGreen => System.Drawing.Color.FromArgb(230, 210, 40),
                TileId.FungalSoil => System.Drawing.Color.FromArgb(90, 80, 160),
                TileId.CrimsonSoil or TileId.CorruptRock or TileId.RadiatedSand or TileId.RadiatedIce => System.Drawing.Color.FromArgb(120, 230, 60),
                TileId.OvergrowthSoil or TileId.OvergrowthRock or TileId.OvergrowthSand or TileId.OvergrowthIce => System.Drawing.Color.FromArgb(160, 40, 140),
                TileId.ReactorCore or TileId.SporeHeart or TileId.RadAltar or TileId.RootAltar => System.Drawing.Color.Red,
                TileId.LifeCore or TileId.SupplyCache or TileId.SalvagePod => System.Drawing.Color.Yellow,
                TileId.DungeonBrick or TileId.Wood or TileId.ShipDoor => System.Drawing.Color.FromArgb(200, 200, 255),
                TileId.EnergyCrystal => System.Drawing.Color.Cyan,
                TileId.Bedrock => System.Drawing.Color.Black,
                TileId.Mud => System.Drawing.Color.FromArgb(80, 60, 45),
                TileId.CanopyGrass or TileId.GlowshroomGrass => System.Drawing.Color.FromArgb(30, 130, 70),
                TileId.ScrapSandstone => System.Drawing.Color.FromArgb(140, 95, 55),
                TileId.Slush => System.Drawing.Color.FromArgb(160, 175, 195),
                TileId.Silt or TileId.Clay or TileId.Gravel => System.Drawing.Color.FromArgb(140, 125, 110),
                TileId.Slate or TileId.BasaltColumn => System.Drawing.Color.FromArgb(75, 80, 90),
                TileId.Alabaster => System.Drawing.Color.FromArgb(235, 230, 220),
                TileId.HiveComb or TileId.Nectar => System.Drawing.Color.FromArgb(230, 160, 30),
                TileId.AcidSand or TileId.ShellSand or TileId.ShellBeach or TileId.SaltCrust => System.Drawing.Color.FromArgb(235, 215, 160),
                TileId.CoralReef or TileId.CoralFan => System.Drawing.Color.FromArgb(255, 120, 140),
                TileId.Obsidian => System.Drawing.Color.FromArgb(40, 20, 50),
                TileId.MossBlue or TileId.MossGold or TileId.MossRed or TileId.MossViolet or TileId.LongMoss => System.Drawing.Color.FromArgb(90, 220, 140),
                TileId.MyceliumStone or TileId.SporeBlock or TileId.MushroomCap => System.Drawing.Color.FromArgb(110, 90, 190),
                TileId.PrecursorBrick or TileId.PrecursorGlyph or TileId.DungeonBrickPrecursor => System.Drawing.Color.FromArgb(60, 170, 170),
                TileId.MudBrick or TileId.BasaltBrick or TileId.AlabasterBrick => System.Drawing.Color.FromArgb(170, 150, 120),
                TileId.GiantTrunk or TileId.GiantLeaf or TileId.TreeTrunk or TileId.TreeCanopy or TileId.PineTrunk or TileId.PineCanopy or
                    TileId.PalmTrunk or TileId.PalmCanopy => System.Drawing.Color.FromArgb(40, 110, 40),
                TileId.SpikeStrip or TileId.DartTrap or TileId.ExplosiveTrap or TileId.Boulder => System.Drawing.Color.FromArgb(230, 30, 30),
                TileId.SlagForge or TileId.VaultAltar or TileId.BroodPod => System.Drawing.Color.FromArgb(255, 0, 128),
                _ when ContentTiles.IsGem(cell.TileId) => System.Drawing.Color.FromArgb(255, 255, 255),
                _ when ContentTiles.IsSpeleothem(cell.TileId) => System.Drawing.Color.FromArgb(175, 175, 180),
                _ when ContentTiles.Has(cell.TileId, TileTraits.Container) => System.Drawing.Color.Yellow,
                _ => System.Drawing.Color.FromArgb(200, 140, 60)
            };
            bitmap.SetPixel(x, y, color);
        }
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"Wrote {path}; spawn column {sim.SpawnColumn}");
        return 0;
    }

    private static int Scan()
    {
        var world = new GameSimulation(seed: 48271, width: 160, height: 72);
        var layout = world.BiomeLayout;
        var frozenX = Math.Max(2, layout.FrozenEdgeAt(layout.SurfaceAt(4)) / 2);
        var junkX = (layout.JunkStartAt(layout.SurfaceAt(150)) + world.World.Width) / 2;
        Console.WriteLine(BiomeScanner.Scan(world.World, new GridPoint(frozenX, layout.SurfaceAt(frozenX) - 1)));
        Console.WriteLine(BiomeScanner.Scan(world.World, new GridPoint(junkX, layout.SurfaceAt(junkX) - 1)));
        Console.WriteLine(BiomeScanner.Scan(world.World, new GridPoint(world.World.Width / 2, world.World.Height - 3)));
        Console.WriteLine(world.CurrentBiome);
        return 0;
    }
}

/// <summary>
/// `--engine-bench --leak [--small]`: generates a world, saves it through the real save repository (JSON envelope
/// plus binary cells sidecar), loads it back, and checks that each world is collected once released and that
/// saving leaves no world-sized buffers behind.
/// </summary>
public static class EngineLeakCheck
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (WeakReference World, long CellBytes) BuildAndSave(UI.WorldSaveRepository saves, Guid id, int width, int height)
    {
        var sim = new GameSimulation(seed: 20261020, width: width, height: height, blight: WorldBlight.Random);
        for (var i = 0; i < 60; i++)
            sim.Tick(new GameInput(new Entities.MovementInput(0f)));
        var watch = Stopwatch.StartNew();
        saves.Save(id, sim.CaptureSnapshot(binaryCells: true));
        Console.WriteLine($"Saved {width}x{height} in {watch.Elapsed.TotalMilliseconds:0} ms");
        return (new WeakReference(sim), sim.World.CellBytes);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference Load(UI.WorldSaveRepository saves, Guid id)
    {
        var watch = Stopwatch.StartNew();
        if (!saves.TryLoad(id, out var snapshot) || snapshot is null)
            throw new InvalidDataException("The save could not be read back.");
        var game = GameSimulation.Restore(snapshot);
        Console.WriteLine($"Loaded (no regeneration) in {watch.Elapsed.TotalSeconds:0.00} s; features {(game.Features?.Dungeon is not null ? "with" : "without")} dungeon");
        for (var i = 0; i < 60; i++)
            game.Tick(new GameInput(new Entities.MovementInput(0f)));
        return new WeakReference(game);
    }

    public static int Run(int width, int height)
    {
        var directory = Path.Combine(Path.GetTempPath(), "rimtera-leak-" + Guid.NewGuid().ToString("N"));
        var saves = new UI.WorldSaveRepository(directory);
        var id = Guid.NewGuid();
        try
        {
            var (first, cellBytes) = BuildAndSave(saves, id, width, height);
            var files = Directory.GetFiles(directory).Select(file => $"{Path.GetFileName(file)} {new FileInfo(file).Length / 1024} KiB");
            Console.WriteLine($"Files: {string.Join(", ", files)}");
            var afterSave = Collect();
            Console.WriteLine($"After save: world collected {!first.IsAlive}; {afterSave} MiB still allocated (cells were {cellBytes / 1048576} MiB)");
            var loaded = Load(saves, id);
            var afterLoad = Collect();
            Console.WriteLine($"After load and release: world collected {!loaded.IsAlive}; {afterLoad} MiB still allocated");
            // Pass when both worlds are collected and less than a tenth of a world's cells is left behind.
            return first.IsAlive || loaded.IsAlive || Math.Max(afterSave, afterLoad) * 1048576L > cellBytes / 10 + 16L * 1048576 ? 1 : 0;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static long Collect()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        return GC.GetTotalMemory(forceFullCollection: true) / 1048576;
    }
}
