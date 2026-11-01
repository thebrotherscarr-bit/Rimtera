using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// World-generation completion checks (docs/WORLDGEN_DIFF.md): every new pass leaves its feature on a Small world,
/// loose sand never floats, exposed edges are smoothed, the phase pool transmutes items and phases the player, wired
/// statues and brood pods react, chests are topped up, the new feature data survives a save, and feature counts
/// scale with area (underground) and width (surface) between a Tiny and a Medium world.
/// </summary>
public static class WorldGenSelfTests
{
    public static void Run(Action<bool, string> check)
    {
        var small = new GameSimulation(seed: 31337, width: 4200, height: 1200, blight: WorldBlight.Overgrowth);
        var features = small.Features!;
        var counts = new int[Enum.GetValues<TileId>().Max(id => (int)id) + 1];
        int phaseCells = 0, shaped = 0, floating = 0;
        var cells = small.World.Cells;
        var width = small.World.Width;
        for (var index = 0; index < cells.Length; index++)
        {
            var cell = cells[index];
            counts[(int)cell.TileId]++;
            if (cell.LiquidKind == LiquidKind.Phase && cell.LiquidUnits > 0)
                phaseCells++;
            if (cell.TileId != TileId.Air && cell.Shape != TileShape.Full && TilePhysics.IsShapeable(cell.TileId, small.Tiles) &&
                !TilePhysics.UsesStateBits(cell.TileId))
                shaped++;
            if (TilePhysics.Falls(cell.TileId) && index + width < cells.Length && cells[index + width].TileId == TileId.Air)
                floating++;
        }
        int Tiles(params TileId[] ids) => ids.Sum(id => counts[(int)id]);

        // ---- 0. World shape matches the Terraria-style reference (0.3.0, docs/WORLDGEN_DIFF.md §4) ----
        var shape = WorldShape.Measure(small);
        var reference = WorldShapeReference.Measure(4200, 1200, 31337);
        var depthsMatch = Enumerable.Range(3, shape.OpenByDepth.Length - 3)
            .All(i => Math.Abs(shape.OpenByDepth[i] - reference.OpenByDepth[i]) <= 0.08);
        check(shape.MeanStep <= 0.8 && shape.SteepShare <= 0.12 && shape.InlandRange is >= 100 and <= 300 && shape.HillsPer1000 is >= 6 and <= 14 &&
              shape.SpawnRange >= 30,
            $"the surface rolls in hills like the reference and spawn is no longer a plateau (mean step {shape.MeanStep:0.00}/{reference.MeanStep:0.00}, " +
            $"steep {shape.SteepShare:P0}/{reference.SteepShare:P0}, range {shape.InlandRange}/{reference.InlandRange}, hills {shape.HillsPer1000:0.0}/{reference.HillsPer1000:0.0}, spawn range {shape.SpawnRange}/{reference.SpawnRange})");
        check(depthsMatch && shape.OpenByDepth[0] is >= 0.06 and <= 0.16 && shape.OpenByDepth[3] >= 0.22,
            $"the ground is as open as the reference at every depth from 120 tiles down, within 8 points ({string.Join(", ", shape.OpenByDepth.Select((open, i) => $"{open:P0}/{reference.OpenByDepth[i]:P0}"))})");
        check(shape.BigCaveShare >= 0.45 && Math.Abs(shape.BigCaveShare - reference.BigCaveShare) <= 0.12 && shape.PocketShare <= 0.22 &&
              shape.UndergroundWaterShare is >= 0.07 and <= 0.16,
            $"caves join up into large systems and some are water-filled, as in the reference (large {shape.BigCaveShare:P0}/{reference.BigCaveShare:P0}, " +
            $"pockets {shape.PocketShare:P0}/{reference.PocketShare:P0}, water {shape.UndergroundWaterShare:P0}/{reference.UndergroundWaterShare:P0})");
        check(shape.OpeningsPer1000 > reference.OpeningsPer1000 && shape.OpeningsPer1000 is >= 2 and <= 9 && shape.WideOpeningsPer1000 >= 1.5,
            $"the surface has a few more natural openings into the caves than the reference ({shape.OpeningsPer1000:0.0} per 1000 columns, " +
            $"{shape.WideOpeningsPer1000:0.0} wide; reference {reference.OpeningsPer1000:0.0})");

        // ---- 1. Every new pass leaves its mark ----
        string[] sites =
        [
            FeatureNames.ScrapDunes, FeatureNames.BuriedLanders, FeatureNames.CoolantSprings, FeatureNames.ScrapHive,
            FeatureNames.TideGrottos, FeatureNames.PhasePool, FeatureNames.MountainCaves, FeatureNames.UndergroundLakes,
            FeatureNames.MossCaves, FeatureNames.Graveyards, FeatureNames.SlagFoundries, FeatureNames.IslandHouses,
            FeatureNames.Campsites, FeatureNames.RiggedChests, FeatureNames.MiningCaches, FeatureNames.Statues,
            FeatureNames.WiredStatues, FeatureNames.CanopyShrines, FeatureNames.TempleAltar, FeatureNames.UndergroundCanopy,
            FeatureNames.CanopyPools, FeatureNames.SurfacePonds
        ];
        string[] tallies =
        [
            FeatureNames.SurfacePockets, FeatureNames.SiltPockets, FeatureNames.SlushPockets, FeatureNames.GemVeins,
            FeatureNames.ExposedGems, FeatureNames.LongMoss, FeatureNames.Speleothems, FeatureNames.CrystalShrubs,
            FeatureNames.RubblePiles, FeatureNames.ShellPiles, FeatureNames.FallenLogs, FeatureNames.WaterPlants, FeatureNames.CoralFans,
            FeatureNames.BoulderTraps, FeatureNames.DartTraps, FeatureNames.ExplosiveTraps, FeatureNames.SpikePits,
            FeatureNames.SurfaceOre, FeatureNames.BroodPods, FeatureNames.SolarLilies, FeatureNames.DyeBlossoms,
            FeatureNames.TempleRooms, FeatureNames.UndergroundCanopyTrees, FeatureNames.WaterSeeps, FeatureNames.SlopedEdges,
            FeatureNames.HalfBlocks, FeatureNames.CaveWallRuns, FeatureNames.EnclosedWalls, FeatureNames.GrassWalls,
            FeatureNames.ClearedSkyWalls, FeatureNames.SurfaceCrates, FeatureNames.SunkenChests, FeatureNames.ImprovedChests,
            FeatureNames.WildGourds, FeatureNames.WildMushrooms, FeatureNames.BurrowerNests
        ];
        var missingSites = sites.Where(name => features.SitesOf(name).Count == 0).ToArray();
        var missingTallies = tallies.Where(name => features.CountOf(name) <= 0).ToArray();
        check(missingSites.Length == 0 && missingTallies.Length == 0,
            $"a Small world gets every new generation feature (missing sites: {string.Join(",", missingSites)}; tallies: {string.Join(",", missingTallies)})");

        TileId[] newTiles =
        [
            TileId.ScrapSandstone, TileId.Slush, TileId.MudBrick, TileId.RubblePile, TileId.LootPile, TileId.ShellPile,
            TileId.FallenLog, TileId.Lilypad, TileId.Reed, TileId.Seaweed, TileId.CoralFan, TileId.GraveMarker, TileId.Boulder,
            TileId.SpikeStrip, TileId.SlagForge, TileId.SunkenChest, TileId.SurfaceCrate, TileId.ShrineChest, TileId.SkyChest,
            TileId.LanderChest, TileId.SlagChest, TileId.BroodPod, TileId.SolarLily, TileId.LongMoss, TileId.BurrowerNest,
            TileId.VaultAltar, TileId.CrystalShrub, TileId.WildGourd, TileId.WildMushroom, TileId.Campfire
        ];
        var absent = newTiles.Where(id => counts[(int)id] == 0).ToArray();
        var gems = ContentTiles.GemTiles.Count(gem => counts[(int)gem.Tile] > 0);
        var speleothemKinds = Enumerable.Range((int)TileId.StalactiteStone, 10).Count(id => counts[id] > 0);
        var statueKinds = Tiles(TileId.StatueHopper) > 0 && Tiles(TileId.StatueCrab) > 0 && Tiles(TileId.StatueBat) > 0;
        var dyeKinds = Tiles(TileId.DyeBlossomIndigo, TileId.DyeBlossomCrimson, TileId.DyeBlossomGold) > 0;
        var newWalls = new HashSet<ushort>();
        foreach (var cell in cells)
            if (cell.WallId is WallIds.GrassRoots or WallIds.CaveLayered or WallIds.CaveRough or WallIds.MossyRock)
                newWalls.Add(cell.WallId);
        check(absent.Length == 0 && gems == 6 && speleothemKinds >= 4 && statueKinds && dyeKinds && newWalls.Count == 4 && phaseCells > 20 &&
              features.Chests.Count > 50 && features.Islands.All(island => island.Right > island.Left) &&
              Tiles(TileId.SkyChest) >= features.Islands.Count,
            $"every new tile, all six gems, speleothem variants, statues, dye blossoms, the new cave walls and the phase pool appear " +
            $"(absent: {string.Join(",", absent)}; gems {gems}, speleothem kinds {speleothemKinds}, walls {newWalls.Count}, phase cells {phaseCells})");

        // ---- 2. Cleanup and smoothing ----
        check(floating == 0 && shaped > 1000 && features.CountOf(FeatureNames.SlopedEdges) > 1000,
            $"no falling block rests on air after generation ({floating} floating) and exposed edges are sloped ({shaped} shaped cells)");

        // ---- 3. The phase pool: items transmute, bathers phase through floors ----
        var pool = features.SitesOf(FeatureNames.PhasePool)[0];
        GridPoint? phaseCell = null;
        for (var y = pool.Bottom; y >= pool.Top && phaseCell is null; y--)
        for (var x = pool.Left; x <= pool.Right && phaseCell is null; x++)
            if (small.World.GetCell(x, y).LiquidKind == LiquidKind.Phase && small.World.GetCell(x, y).LiquidDepth > 0.9f)
                phaseCell = new GridPoint(x, y);
        small.SpawnGroundItem(ItemId.RockBlock, 5, new Vector2(phaseCell!.Value.X + 0.5f, phaseCell.Value.Y + 0.5f));
        void Idle(GameSimulation sim, int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                sim.Player.Heal(sim.Player.MaximumHitPoints);
                sim.Tick(new GameInput(new MovementInput(0f)));
            }
        }
        Idle(small, 40);
        var transmuted = small.GroundItems.Any(item => item.ItemId == ItemId.EnergyCrystal && item.Count == 5) &&
                         small.Flags.Get("phase.transmutes") >= 1 && GameSimulation.PhaseTransmute(ItemId.AmethystShard) == ItemId.SapphireShard;
        var home = small.Player.Motion.Position;
        small.Player.Motion.Position = new Vector2(phaseCell.Value.X + 0.5f - small.Player.Motion.Size.X / 2f, phaseCell.Value.Y + 0.5f - small.Player.Motion.Size.Y / 2f);
        Idle(small, 3);
        var phased = small.PlayerPhased && small.Player.Statuses.IsActive(StatusId.Phased);
        // Stand on a one-tile sky ledge while phased: the body sinks through it.
        var ledgeX = small.SpawnColumn + 40;
        const int ledgeY = 30;
        for (var x = ledgeX - 4; x <= ledgeX + 4; x++)
            small.World.SetTile(new GridPoint(x, ledgeY), TileId.Rock);
        small.Player.Motion.Position = new Vector2(ledgeX + 0.5f - small.Player.Motion.Size.X / 2f, ledgeY - small.Player.Motion.Size.Y - 0.01f);
        small.Player.Motion.Velocity = Vector2.Zero;
        for (var i = 0; i < 40 && small.Player.Motion.Position.Y < ledgeY; i++)
        {
            small.Player.Statuses.Apply(StatusId.Phased, 1f);
            Idle(small, 1);
        }
        var sank = small.Player.Motion.Position.Y > ledgeY && small.Flags.Get("phase.drops") >= 1;
        small.Player.Motion.Position = home;
        small.Player.Motion.Velocity = Vector2.Zero;
        small.Player.Statuses.Remove(StatusId.Phased);
        check(transmuted && phased && sank,
            $"the phase pool transmutes a dropped item ({transmuted}), phases a bather ({phased}) who then sinks through a floor ({sank})");

        // ---- 4. Wired statues, brood pods, piles and chests ----
        var statue = features.SitesOf(FeatureNames.WiredStatues)[0];
        var plateX = small.World.TileAt(statue.Left + 3, statue.Top) == TileId.PressurePlate ? statue.Left + 3 : statue.Left - 3;
        var before = small.Enemies.Count;
        var reacted = small.PulseWires(new GridPoint(plateX, statue.Top));
        var spawned = small.Enemies.Count > before;
        int FirstOf(TileId tile)
        {
            var all = small.World.Cells;
            for (var i = 0; i < all.Length; i++)
                if (all[i].TileId == tile)
                    return i;
            return -1;
        }
        var rubble = FirstOf(TileId.RubblePile);
        var rockBefore = small.Player.Inventory.Count(ItemId.RockBlock);
        small.ForceBreakTile(new GridPoint(rubble % width, rubble / width));
        var rubbleLoot = small.Player.Inventory.Count(ItemId.RockBlock) > rockBefore;
        var pod = FirstOf(TileId.BroodPod);
        small.ForceBreakTile(new GridPoint(pod % width, pod / width));
        // Before the Fracture the pod only releases its guards; the matriarch waits (PROGRESSION_DIFF G2).
        var matriarch = !small.FractureActive && !small.Enemies.Any(enemy => enemy.Kind == EnemyKind.BloomMatriarch) &&
                        small.Enemies.Any(enemy => enemy.Kind == EnemyKind.HiveGuard);
        var (chestCell, _) = features.Chests.First(chest => small.World.TileAt(chest.Cell.X, chest.Cell.Y) == TileId.SunkenChest);
        var chest = small.GetContainer(chestCell);
        var chestLoot = chest is not null && chest.Slots.Count(stack => !stack.IsEmpty) >= 2 && features.ChestTier(chestCell) >= 0;
        check(reacted >= 1 && spawned && rubbleLoot && matriarch && chestLoot,
            $"a pulsed statue wakes a creature ({spawned}), rubble drops stone ({rubbleLoot}), a brood pod releases guards but no matriarch before the Fracture " +
            $"({matriarch}) and a sunken chest holds topped-up loot ({chestLoot})");

        // ---- 4b. World contents match the Terraria-style reference ranges (0.3.1, docs/WORLDGEN_DIFF.md §5) ----
        var census = WorldCensus.Take(small);
        var off = WorldCensusReference.Compare(census, 4200, 1200).Where(entry => !entry.InRange).Select(entry => entry.Line).ToArray();
        check(off.Length == 0 && census.ChestsByLayer.GetValueOrDefault("caverns") > census.ChestsByLayer.GetValueOrDefault("surface"),
            $"chests, pots, life cores, altars, ore per layer and the ore tier mix (0.3.3) are within the reference ranges, with most chests in the caves " +
            $"({census.Chests} chests; {(off.Length == 0 ? "all in range" : string.Join("; ", off))})");
        var families = new HashSet<GameSimulation.ChestFamily>();
        var missing = new List<string>();
        foreach (var (cell, tier) in features.Chests.Where((_, index) => index % 3 == 0))
        {
            var tile = small.World.TileAt(cell.X, cell.Y);
            if (GameSimulation.FamilyOf(tile, tier, BiomeScanner.Scan(small.World, cell).Zone) is not { } family)
                continue;
            families.Add(family);
            if (small.GetContainer(cell) is not { } opened || !GameSimulation.ChestPrimaries[family].Any(item => opened.Count(item) > 0))
                missing.Add($"{tile} at {cell}");
        }
        check(missing.Count == 0 && families.Count >= 5,
            $"every generated chest holds a primary find of its family, as Terraria's chests do ({families.Count} families seen" +
            $"{(missing.Count == 0 ? "" : "; missing in " + string.Join(", ", missing.Take(4)))})");

        // ---- 5. New feature data survives a save without regeneration ----
        var restored = GameSimulation.Restore(small.CaptureSnapshot()).Features!;
        check(restored.Sites.Count == features.Sites.Count && restored.Counts.Count == features.Counts.Count &&
              restored.Chests.Count == features.Chests.Count &&
              restored.SitesOf(FeatureNames.PhasePool)[0] == pool && restored.ChestTier(chestCell) == features.ChestTier(chestCell),
            "generation sites, tallies and chest tiers are saved with the world");

        // ---- 6. Scaling: Tiny against Medium ----
        WorldFeatures Generate(int w, int h)
        {
            var grid = new TileGrid(w, h);
            var generator = new LargeWorldGenerator(w, h, WorldBlight.Overgrowth, null);
            var pipeline = new WorldGenerationPipeline();
            generator.AddPasses(pipeline);
            pipeline.Generate(grid, new TileCatalog(), 20261020);
            return generator.Features;
        }
        var tiny = Generate(1750, 900);
        var medium = Generate(6400, 1800);
        double Ratio(Func<WorldFeatures, int> count) => count(medium) / (double)Math.Max(1, count(tiny));
        (string Name, double Ratio)[] underground =
        [
            ("life cores", Ratio(f => f.LifeCores.Count)), ("pods", Ratio(f => f.SalvagePods.Count)), ("cabins", Ratio(f => f.Cabins.Count)),
            ("altars", Ratio(f => f.Altars.Count)), ("statues", Ratio(f => f.SitesOf(FeatureNames.Statues).Count)),
            ("lakes", Ratio(f => f.SitesOf(FeatureNames.UndergroundLakes).Count)), ("geodes", Ratio(f => f.GeodeChambers.Count))
        ];
        (string Name, double Ratio)[] surface =
        [
            ("islands", Ratio(f => f.Islands.Count)), ("blight zones", Ratio(f => f.BlightZones.Count)),
            ("surface lakes", Ratio(f => f.SitesOf("surface-lakes").Count)), ("coast width", Ratio(f => f.AcidCoasts[0].Right - f.AcidCoasts[0].Left))
        ];
        // Medium has 7.3× Tiny's area and 3.7× its width.
        var badUnder = underground.Where(r => r.Ratio is < 5.0 or > 10.0).ToArray();
        var badSurface = surface.Where(r => r.Ratio is < 2.4 or > 5.0).ToArray();
        check(badUnder.Length == 0 && badSurface.Length == 0,
            "feature counts scale with area underground and with width on the surface (out of range: " +
            string.Join(", ", badUnder.Concat(badSurface).Select(r => $"{r.Name} {r.Ratio:0.0}×")) + ")");
    }
}
