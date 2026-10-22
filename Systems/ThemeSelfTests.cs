using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Neo-Terra theme checks (docs/BIOME_THEMES.md): fuel seas with beaches and sloping coastal shelves cap both world edges, the bottom band is the
/// Irradiated Core (liquid uranium, irradiated rock, crust, vitrified glass and uranium veins, no ember rock or
/// obsidian), core spawns are radiation-themed, and ocean swimmers only appear inside water.
/// </summary>
public static class ThemeSelfTests
{
    public static void Run(Action<bool, string> check)
    {
        var game = new GameSimulation(seed: 77031, width: 1750, height: 900, blight: WorldBlight.Radiated) { FamilySpawnsEnabled = false };
        var world = game.World;
        var features = game.Features!;

        // ---- 1. Fuel seas at both edges: deep fuel at the edge, a dry sand beach and a shelf sloping down to it ----
        int WaterRun(int x)
        {
            var run = 0;
            for (var y = 0; y < world.Height / 2; y++)
                if (world.GetCell(x, y).LiquidKind == LiquidKind.Fuel && world.GetCell(x, y).LiquidDepth > 0.5f)
                    run++;
            return run;
        }
        // Ground (or sea floor) below the layout surface, so a sky island over the beach is not mistaken for it.
        int Top(int x)
        {
            for (var y = Math.Max(0, game.BiomeLayout.SurfaceAt(x) - 2); y < world.Height; y++)
                if (game.Tiles[world.TileAt(x, y)].IsSolid)
                    return y;
            return world.Height - 1;
        }
        bool SandTop(int x) => world.TileAt(x, Top(x)) is TileId.ShellBeach or TileId.Sand or TileId.SaltCrust or TileId.ShellSand or TileId.CoralReef;
        var oceans = features.AcidCoasts;
        var oceansOk = oceans.Count == 2 && oceans.Any(o => o.Left == 0) && oceans.Any(o => o.Right == world.Width - 1);
        var acidInOceans = 0;
        foreach (var (left, right) in oceans)
        {
            var edge = left == 0 ? 2 : world.Width - 3;
            var middle = (left + right) / 2;
            var beach = left == 0 ? right - 3 : left + 3;
            oceansOk &= WaterRun(edge) >= 12 && WaterRun(middle) >= 6 && WaterRun(edge) >= WaterRun(middle) - 2;
            oceansOk &= SandTop(edge) && SandTop(middle) && SandTop(beach) && WaterRun(beach) == 0;
            for (var x = left; x <= right; x++)
            for (var y = 0; y < world.Height / 2; y++)
                if (world.GetCell(x, y).LiquidKind == LiquidKind.Acid && world.GetCell(x, y).LiquidUnits > 0)
                    acidInOceans++;
        }
        // The coastal shelf: walking from inland toward each sea, the ground only ever steps down (allowing small bumps).
        var shelf = WorldGenDensity.CoastShelf.Count(world.Width, world.Height);
        var shelfOk = true;
        var shelfDrops = new List<int>();
        foreach (var (left, right) in oceans)
        {
            var shore = left == 0 ? right : left;
            var step = left == 0 ? -1 : 1;
            var inland = shore - step * shelf;
            var previous = Top(inland);
            var start = previous;
            for (var i = 1; i <= 8; i++)
            {
                var row = Top(inland + step * shelf * i / 8);
                shelfOk &= row >= previous - 4;
                previous = Math.Max(previous, row);
            }
            shelfOk &= previous >= start;
            shelfDrops.Add(previous - start);
        }
        int fuelInland = 0, waterInland = 0;
        for (var x = 0; x < world.Width; x++)
        for (var y = 0; y < world.Height; y++)
        {
            var cell = world.GetCell(x, y);
            if (cell.LiquidUnits == 0 || BiomeScanner.InOceanBand(world, x))
                continue;
            if (cell.LiquidKind == LiquidKind.Fuel) fuelInland++;
            if (cell.LiquidKind == LiquidKind.Water) waterInland++;
        }
        check(shelfOk && fuelInland == 0 && waterInland > 50 &&
              ActiveLiquidSimulator.MergeTile(LiquidKind.Lava, LiquidKind.Fuel) == TileId.CharSlag &&
              ActiveLiquidSimulator.MergeTile(LiquidKind.Water, LiquidKind.Fuel) is null && game.Items[ItemId.BucketFuel].Name == "Fuel bucket",
            $"the land slopes down over a coastal shelf to each fuel sea (drops {string.Join("/", shelfDrops)}), fuel stays in the seas " +
            $"({fuelInland} inland) while inland lakes keep water ({waterInland}), and uranium burns fuel into char slag");

        var oceanCell = new GridPoint(oceans.Count > 0 ? oceans[0].Right / 2 : 10, oceans.Count > 0 ? Top(oceans[0].Right / 2) - 3 : 10);
        var oceanReading = BiomeScanner.Scan(world, oceanCell);
        check(oceansOk && acidInOceans == 0 && oceanReading.Zone == BiomeZone.AcidCoast && oceanReading.DisplayName == "Fuel Sea" &&
              features.SitesOf(FeatureNames.TideGrottos).Count > 0,
            $"fuel seas with sand beaches cap both world edges, deepen toward the edge and read as the Fuel Sea biome " +
            $"(edge depths {string.Join("/", oceans.Select(o => WaterRun(o.Left == 0 ? 2 : world.Width - 3)))}, acid {acidInOceans}, zone {oceanReading.Zone})");

        // ---- 2. The Irradiated Core: uranium liquid and irradiated tiles, nothing ember or obsidian ----
        int uranium = 0, irradiated = 0, crust = 0, glass = 0, veins = 0, legacy = 0;
        for (var x = 0; x < world.Width; x++)
        for (var y = 0; y < world.Height; y++)
        {
            var cell = world.GetCell(x, y);
            if (cell.TileId is TileId.EmberRock or TileId.Obsidian or TileId.Ash)
                legacy++;
            if (y < game.BiomeLayout.MoltenTopAt(x) - 6)
                continue;
            if (cell.LiquidKind == LiquidKind.Lava && cell.LiquidUnits > 0) uranium++;
            switch (cell.TileId)
            {
                case TileId.IrradiatedRock: irradiated++; break;
                case TileId.CoreCrust: crust++; break;
                case TileId.VitrifiedGlass: glass++; break;
                case TileId.UraniumOre: veins++; break;
            }
        }
        var coreCell = new GridPoint(world.Width / 3, (game.BiomeLayout.MoltenTopAt(world.Width / 3) + world.Height) / 2);
        var coreReading = BiomeScanner.Scan(world, coreCell);
        check(uranium > 200 && irradiated > 5000 && crust > 100 && glass > 20 && veins > 20 && legacy == 0 &&
              coreReading.Zone == BiomeZone.MoltenChasm && coreReading.DisplayName == "Irradiated Core" &&
              ActiveLiquidSimulator.MergeTile(LiquidKind.Water, LiquidKind.Lava) == TileId.VitrifiedGlass &&
              world.TileAt(0, 0) == TileId.Air && game.Tiles[TileId.VitrifiedGlass].Name.Contains("Vitrified"),
            "the bottom band is the Irradiated Core: liquid uranium, irradiated rock, crust, vitrified glass and uranium veins");

        // ---- 3. Core spawns are radiation-themed ----
        EnemyKind[] radiation = [EnemyKind.CoreHusk, EnemyKind.FissionWisp, EnemyKind.GlowStalker, EnemyKind.RadLeech,
            EnemyKind.RadSkitter, EnemyKind.GeigerBug, EnemyKind.MutantRat,
            .. ContentFillCreatures.Roster("core", hard: false).Select(entry => entry.Kind)]; // content fill: core-native radiation fauna
        var coreKinds = BiomeSpawnTables.KindsFor(BiomeZone.MoltenChasm);
        var coreRolls = Enumerable.Range(0, 200).Select(i => BiomeSpawnTables.Pick(BiomeZone.MoltenChasm, i / 200.0, i % 2 == 0)).ToArray();
        check(coreKinds.Count >= 4 && coreKinds.All(radiation.Contains) && coreRolls.All(radiation.Contains) &&
              coreKinds.Contains(EnemyKind.CoreHusk) && coreKinds.Contains(EnemyKind.FissionWisp) &&
              !coreKinds.Intersect(BiomeSpawnTables.KindsFor(BiomeZone.FrozenWasteland)).Any() &&
              EnemyArchetypes.Get(EnemyKind.CoreHusk).DisplayName == "Irradiated husk" &&
              BiomeSpawnTables.DungeonKinds.Count >= 3 && !BiomeSpawnTables.DungeonKinds.Intersect(coreKinds).Any(),
            "Irradiated Core spawns are radiation creatures only (husks, fission wisps and the rad fauna)");

        // ---- 4. Ocean swimmers appear inside water; shore life never spawns underwater ----
        var oceanTable = BiomeSpawnTables.KindsFor(BiomeZone.AcidCoast);
        var swimmerX = oceans.Count > 0 ? oceans[0].Right / 2 : 10;
        game.Player.Motion.Position = new Vector2(swimmerX, Top(swimmerX) - WaterRun(swimmerX) - 1.9f);
        game.Player.Motion.Velocity = Vector2.Zero;
        var seen = new HashSet<long>();
        int swimmers = 0, dry = 0, wetSwimmers = 0, wetWalkers = 0;
        for (var tick = 0; tick < 6000 && swimmers < 6; tick++)
        {
            game.ProbeSpawnTick(0.65f);
            foreach (var enemy in game.Enemies.ToArray())
            {
                if (!seen.Add(enemy.Id) || enemy.EventTag is not null)
                    continue;
                var center = enemy.Motion.Position + enemy.Motion.Size / 2f;
                var cell = world.GetCell((int)MathF.Floor(center.X), (int)MathF.Floor(center.Y));
                var wet = cell.LiquidKind is LiquidKind.Water or LiquidKind.Fuel && cell.LiquidUnits > 0;
                if (BiomeSpawnTables.IsSwimmer(enemy.Kind))
                {
                    swimmers++;
                    if (wet) wetSwimmers++;
                }
                else if (!enemy.Archetype.Flying)
                {
                    dry++;
                    if (wet) wetWalkers++;
                }
                enemy.TakeDamage(1e6f);
            }
        }
        check(oceanTable.Count(BiomeSpawnTables.IsSwimmer) >= 4 && swimmers >= 3 && wetSwimmers == swimmers && wetWalkers == 0 &&
              FishingTables.Pick(new FishingContext(LiquidKind.Fuel, BiomeZone.AcidCoast, FishingDepth.Surface, 75, 30f, false, 0f),
                  FishingRarity.Rare, new Random(1)) == ItemId.CoastCrate,
            $"fuel-sea swimmers spawn inside the fuel ({wetSwimmers}/{swimmers}), shore life stays dry ({dry} walkers), and ocean fishing uses the sea pool");
    }
}
