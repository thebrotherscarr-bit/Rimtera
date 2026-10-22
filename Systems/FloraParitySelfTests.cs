using System.Numerics;
using Rimtera.Entities;
using Rimtera.UI;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Flora parity (docs/FLORA_PARITY.md): random-update rates, felling and its drops, tree shaking, sapling ground and room,
/// herb blooms, growth and drops, grass spread and grass plants, vines, cacti, cutting plants with swings, placement over
/// plants, and world generation of real grass plants.
/// </summary>
public static class FloraParitySelfTests
{
    private const int Left = 4, Top = 2, Right = 75, Floor = 30;

    /// <summary>An 80×80 arena (surface line at row 36, so the floor at row 30 is above it) with a grass floor.</summary>
    private static GameSimulation Arena(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 80, height: 80) { FamilySpawnsEnabled = false };
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e6f);
        for (var y = Top; y <= Floor + 3; y++)
        for (var x = Left; x <= Right; x++)
        {
            var point = new GridPoint(x, y);
            game.World.SetTile(point, y == Floor ? TileId.Grass : y > Floor ? TileId.Rock : TileId.Air);
            var cell = game.World.GetCell(point);
            cell.LiquidDepth = 0f;
            cell.LiquidKind = LiquidKind.None;
            cell.WallId = WallIds.None;
            cell.Flags = 0;
            game.World.SetCell(point, cell);
        }
        game.Player.Motion.Position = new Vector2(30f, Floor - game.Player.Motion.Size.Y - 0.01f);
        game.Player.Motion.Velocity = Vector2.Zero;
        Idle(game, 2);
        return game;
    }

    private static void Idle(GameSimulation game, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            game.Tick(new GameInput(new MovementInput(0f)));
    }

    private static void Use(GameSimulation game, ItemId item, GridPoint target, int ticks = 1)
    {
        if (game.Player.Inventory.Count(item) == 0)
            game.Player.Inventory.Add(item, 1);
        if (game.Player.Inventory.SelectedItem?.Id != item)
        {
            game.TrySelectInventorySlot(game.Player.Inventory.FindSlot(item));
            Idle(game, 1); // switching items spends the tick
        }
        while (game.Player.ActionCooldownTicks > 0)
            Idle(game, 1);
        for (var i = 0; i < ticks; i++)
            game.Tick(new GameInput(new MovementInput(0f), PlayerAction.UseSelected, target));
    }

    private static int Owned(GameSimulation game, ItemId id) =>
        game.Player.Inventory.Count(id) + game.GroundItems.Where(drop => drop.ItemId == id).Sum(drop => drop.Count);

    private static int TreeParts(GameSimulation game, int x0, int x1) =>
        Enumerable.Range(Top, Floor - Top).Sum(y => Enumerable.Range(x0, x1 - x0 + 1).Count(x => TilePhysics.IsTreePart(game.World.TileAt(x, y))));

    public static void Run(Action<bool, string> check)
    {
        Rates(check);
        Felling(check);
        Shaking(check);
        Saplings(check);
        Herbs(check);
        Grass(check);
        VinesAndCacti(check);
        Cutting(check);
        StyledCutting(check);
        WorldGeneration(check);
    }

    // ---- random updates: the reference's per-area rates, rain boost and the wild-herb roll ----
    private static void Rates(Action<bool, string> check)
    {
        var catalog = new TileCatalog();
        var grid = new TileGrid(1000, 400);
        var raining = false;
        var ticker = new WorldTicker(grid, catalog, 7) { Conditions = () => FloraConditions.Default with { Raining = raining } };
        for (var i = 0; i < 100; i++)
            ticker.Tick();
        var dry = ticker.FloraUpdates;
        raining = true;
        for (var i = 0; i < 100; i++)
            ticker.Tick();
        var wet = ticker.FloraUpdates - dry;
        // 400 000 cells: 3e-5 → 12 above-ground and 1.5e-5 → 6 below-ground updates per tick; rain makes it 18 + 6.
        check(dry == 1800 && wet == 2400 && FloraParity.HerbSpawnOneIn(4200) == 15100 && FloraParity.HerbSpawnOneIn(1000) == 15100 &&
              FloraParity.HerbSpawnOneIn(8400) == 42200 && FloraParity.SurfaceRow(80) == 36,
            $"flora random updates run at 3e-5 (above) and 1.5e-5 (below) of the world's cells per tick, ×1.5 above in rain ({dry}, {wet})");
    }

    // ---- felling: any segment fells what is above it; every felled segment, top and branch drops its own wood ----
    private static void Felling(Action<bool, string> check)
    {
        var game = Arena(9101);
        var grew = TilePhysics.TryGrowTree(game.World, game.Tiles, 40, Floor - 1, new Random(3), null, 10, 10);
        var timberBefore = game.Player.Inventory.Count(ItemId.Timber);
        game.ForceBreakTile(new GridPoint(40, Floor - 4));
        var stump = Enumerable.Range(Floor - 3, 3).All(y => TilePhysics.IsTrunk(game.World.TileAt(40, y)));
        var above = TreeParts(game, 36, 44) - 3;
        var timber = game.Player.Inventory.Count(ItemId.Timber) - timberBefore;
        // 7 felled trunk segments + the leafy top + 2 leafy branches = 10 drops of 1–2 wood each.
        check(grew && stump && above == 0 && timber >= 10 && timber <= 20,
            $"hitting a mid-trunk segment fells it and everything above, leaving the stump; each felled segment drops wood ({timber})");

        var random = new Random(11);
        double Mean(int axe)
        {
            var total = 0;
            for (var i = 0; i < 35000; i++)
                total += FloraParity.SegmentWood(axe, random);
            return total / 35000.0;
        }
        var copper = Mean(7);
        var bare = Mean(0);
        var strong = Mean(34);
        check(Math.Abs(copper - (1 + 17.0 / 35)) < 0.02 && Math.Abs(bare - (1 + 1.0 / 35 + 34.0 / 105)) < 0.02 && strong == 2.0 &&
              FloraParity.AxeField(35) == 7,
            $"a felled segment's wood gains one when Next(35) ≤ axe or on 1 in 3 (axe 7: {copper:0.000}, axe 0: {bare:0.000}, axe 34: {strong})");

        check(FloraParity.FellDropsSaplings(TreeFamily.Grove) && FloraParity.FellDropsSaplings(TreeFamily.Palm) &&
              FloraParity.FellDropsSaplings(TreeFamily.Blight) && !FloraParity.FellDropsSaplings(TreeFamily.Canopy) &&
              !FloraParity.FellDropsSaplings(TreeFamily.Shroom) && FloraParity.TopSaplingOneIn == 2,
            "felled tops and branches drop a sapling on 1 in 2, except canopy (jungle) and shroom trees");

        // Saplings across many fells: 3 leafy rolls at 1 in 2 → about 1.5 per round tree.
        var saplings = 0;
        var fells = 0;
        for (var i = 0; i < 40; i++)
        {
            var x = 10 + i % 6 * 10;
            if (!TilePhysics.TryGrowTree(game.World, game.Tiles, x, Floor - 1, new Random(100 + i), null, 6, 6))
                continue;
            var before = game.Player.Inventory.Count(ItemId.Sapling);
            game.ForceBreakTile(new GridPoint(x, Floor - 1));
            saplings += game.Player.Inventory.Count(ItemId.Sapling) - before;
            fells++;
        }
        var shroomOk = ContentFillFlora.TryGrowSpecies(game.World, 60, Floor - 1, TileId.ShroomStalk, TileId.ShroomCap, TileId.PalmCanopy,
            new Random(5), null, 4, 4);
        var stalkBefore = game.Player.Inventory.Count(ItemId.Stalkwood);
        var saplingBefore = game.Player.Inventory.Count(ItemId.Sapling);
        game.ForceBreakTile(new GridPoint(60, Floor - 1));
        check(fells >= 30 && saplings > fells * 0.9 && saplings < fells * 2.1 && shroomOk &&
              game.Player.Inventory.Count(ItemId.Stalkwood) > stalkBefore && game.Player.Inventory.Count(ItemId.Sapling) == saplingBefore,
            $"felling drops saplings from the crown ({saplings} from {fells} trees) but shroom trees drop none");
    }

    // ---- shaking: a cutter hit shakes a grown tree once per tree per day ----
    private static void Shaking(Action<bool, string> check)
    {
        var random = new Random(21);
        int saplings = 0, wood = 0, canopySaplings = 0;
        const int rolls = 70000;
        for (var i = 0; i < rolls; i++)
        {
            var roll = FloraParity.RollShake(TreeFamily.Grove, FloraConditions.Default, random);
            if (roll.Kind == ShakeKind.Sapling) saplings++;
            if (roll.Kind == ShakeKind.Wood) wood++;
            if (FloraParity.RollShake(TreeFamily.Canopy, FloraConditions.Default, random).Kind == ShakeKind.Sapling) canopySaplings++;
        }
        check(Math.Abs(saplings / (double)rolls - 1.0 / 7) < 0.006 && Math.Abs(wood / (double)rolls - 6.0 / 7 / 12) < 0.004 && canopySaplings == 0,
            $"a shake drops a sapling on 1 in 7 (grove), else wood on 1 in 12, and canopy trees shake no saplings ({saplings}, {wood})");

        var game = Arena(9102);
        var grew = TilePhysics.TryGrowTree(game.World, game.Tiles, 33, Floor - 1, new Random(4), null, 6, 6);
        // A cutter hit shakes the tree (ref. KillTile fail → ShakeTree); further hits the same day do not.
        Use(game, ItemId.FerriteCutter, new GridPoint(33, Floor - 1), 50);
        var standing = TilePhysics.IsTrunk(game.World.TileAt(33, Floor - 1));
        var shakenByCutter = game.TreesShakenToday;
        var second = game.ShakeTree(new GridPoint(33, Floor - 3));
        check(grew && standing && shakenByCutter == 1 && second.Length == 0 && game.TreesShakenToday == 1,
            $"a cutter hit shakes a grown tree once per tree per day ({shakenByCutter} shaken, standing {standing})");
    }

    // ---- saplings: tree ground with tree ground beside it, room to grow, one update in 20 ----
    private static void Saplings(Action<bool, string> check)
    {
        var game = Arena(9103);
        var tiles = game.Tiles;
        bool Grows(int x, int attempts)
        {
            for (var i = 0; i < attempts; i++)
                if (WorldTicker.TryGrowSapling(game.World, tiles, x, Floor - 1, new Random(i)))
                    return true;
            return false;
        }
        // Bare soil is not tree ground.
        for (var x = 9; x <= 11; x++)
            game.World.SetTile(new GridPoint(x, Floor), TileId.Soil);
        game.World.SetTile(new GridPoint(10, Floor - 1), TileId.Sapling);
        var onSoil = Grows(10, 60);
        // A lone grass tile between rock is not enough (the reference needs tree ground beside it too).
        game.World.SetTile(new GridPoint(19, Floor), TileId.Rock);
        game.World.SetTile(new GridPoint(21, Floor), TileId.Rock);
        game.World.SetTile(new GridPoint(20, Floor - 1), TileId.Sapling);
        var lone = Grows(20, 60);
        // No room: a block three rows up.
        game.World.SetTile(new GridPoint(30, Floor - 4), TileId.Rock);
        game.World.SetTile(new GridPoint(30, Floor - 1), TileId.Sapling);
        var cramped = Grows(30, 60);
        // Open grass: grows within the reference's 1-in-20 updates.
        game.World.SetTile(new GridPoint(50, Floor - 1), TileId.Sapling);
        var updates = 0;
        while (updates < 2000 && game.World.TileAt(50, Floor - 1) == TileId.Sapling)
        {
            game.Ticker.UpdateCell(50, Floor - 1);
            updates++;
        }
        var grew = TilePhysics.IsTrunk(game.World.TileAt(50, Floor - 1));
        var height = 0;
        while (TilePhysics.IsTrunk(game.World.TileAt(50, Floor - 1 - height)))
            height++;
        // Placing a sapling: refused on soil, taken on grass.
        Use(game, ItemId.Sapling, new GridPoint(10, Floor - 2));
        game.World.SetTile(new GridPoint(10, Floor - 1), TileId.Air);
        Idle(game, 10);
        Use(game, ItemId.Sapling, new GridPoint(10, Floor - 1));
        var soilPlaced = game.World.TileAt(10, Floor - 1) == TileId.Sapling;
        Idle(game, 10);
        Use(game, ItemId.Sapling, new GridPoint(26, Floor - 1));
        var grassPlaced = game.World.TileAt(26, Floor - 1) == TileId.Sapling;
        check(!onSoil && !lone && !cramped && grew && height >= FloraParity.TreeMinHeight && height <= FloraParity.TreeMaxHeight &&
              !soilPlaced && grassPlaced && FloraParity.SaplingGrowOneIn == 20,
            $"saplings need tree ground with more beside it and room to grow, and grow on 1 update in 20 (grew after {updates}, {height} tall)");
    }

    // ---- herbs: bloom rules, growth, liquids, drops, planters ----
    private static void Herbs(Action<bool, string> check)
    {
        static WorldCell Herb(TileId tile, byte state) => new() { TileId = tile, State = state };
        var day = new HerbConditions(true, 0f, 0f, 0.5f, 3);
        var dusk = new HerbConditions(true, 0f, 0f, 0.85f, 3);
        var night = new HerbConditions(false, 0f, 0f, 0f, 3);
        var fullMoon = new HerbConditions(false, 0f, 0f, 0f, 0);
        var rain = new HerbConditions(true, 0.6f, 0f, 0.85f, 3);
        bool B(TileId tile, byte state, HerbConditions conditions, bool below = false) => TilePhysics.IsBlooming(Herb(tile, state), conditions, below);
        var rules = B(TileId.HerbSunpetal, 1, day) && !B(TileId.HerbSunpetal, 1, night) && !B(TileId.HerbSunpetal, 0, day) &&
                    B(TileId.HerbMoonglow, 1, night) && !B(TileId.HerbMoonglow, 1, day) &&
                    B(TileId.HerbRainreed, 1, rain) && !B(TileId.HerbRainreed, 1, day) &&
                    B(TileId.HerbEmberroot, 1, dusk) && !B(TileId.HerbEmberroot, 1, day) && !B(TileId.HerbEmberroot, 1, rain) &&
                    B(TileId.HerbEmberroot, 1, rain, below: true) &&
                    B(TileId.HerbSporecap, 3, day) && !B(TileId.HerbSporecap, 1, night) &&
                    B(TileId.HerbFrostbell, 3, night) && !B(TileId.HerbFrostbell, 1, fullMoon);
        check(rules, "herb blooms: sun by day, moon by night, rain while it rains, ember at dusk unless rained on, spore and frost by their bloom bit");

        var game = Arena(9104);
        // Growth: a young sunpetal matures (1 in 50, plus 1 in 50 by day); a young frostbell takes about twice as long.
        var cell = new GridPoint(44, Floor - 1);
        game.World.SetTile(cell, TileId.HerbSunpetal);
        var updates = 0;
        while (updates < 3000 && (game.World.GetCell(cell).State & TilePhysics.HerbMatureBit) == 0)
        {
            game.Ticker.UpdateCell(cell.X, cell.Y);
            updates++;
        }
        var matured = (game.World.GetCell(cell).State & TilePhysics.HerbMatureBit) != 0;
        // Frostbell settles into bloom for good; sporecap flickers.
        var frost = new GridPoint(46, Floor - 1);
        game.World.SetTile(new GridPoint(46, Floor), TileId.Snow);
        game.World.SetTile(frost, TileId.HerbFrostbell);
        var frostCell = game.World.GetCell(frost);
        frostCell.State = TilePhysics.HerbMatureBit;
        game.World.SetCell(frost, frostCell);
        for (var i = 0; i < 1500; i++)
            game.Ticker.UpdateCell(frost.X, frost.Y);
        var settled = (game.World.GetCell(frost).State & TilePhysics.HerbBloomBit) != 0;
        var spore = new GridPoint(48, Floor - 1);
        game.World.SetTile(new GridPoint(48, Floor), TileId.FungalSoil);
        game.World.SetTile(spore, TileId.HerbSporecap);
        var sporeCell = game.World.GetCell(spore);
        sporeCell.State = TilePhysics.HerbMatureBit;
        game.World.SetCell(spore, sporeCell);
        int on = 0, off = 0;
        for (var i = 0; i < 300; i++)
        {
            game.Ticker.UpdateCell(spore.X, spore.Y);
            if ((game.World.GetCell(spore).State & TilePhysics.HerbBloomBit) != 0) on++; else off++;
        }
        check(matured && updates < 3000 && settled && on > 50 && off > 50,
            $"young herbs mature over random updates ({updates}); the frost herb settles into bloom, the spore herb flickers ({on}/{off})");

        // Liquids: water drowns a sunpetal but not a rainreed.
        var wetSun = new GridPoint(52, Floor - 1);
        var wetRain = new GridPoint(54, Floor - 1);
        game.World.SetTile(wetSun, TileId.HerbSunpetal);
        game.World.SetTile(wetRain, TileId.HerbRainreed);
        foreach (var point in new[] { wetSun, wetRain })
        {
            var wet = game.World.GetCell(point);
            wet.LiquidKind = LiquidKind.Water;
            wet.LiquidUnits = 120;
            game.World.SetCell(point, wet);
            game.Ticker.UpdateCell(point.X, point.Y);
        }
        check(game.World.TileAt(wetSun.X, wetSun.Y) == TileId.Air && game.World.TileAt(wetRain.X, wetRain.Y) == TileId.HerbRainreed,
            "water kills herbs except the night and rain herbs (and lava spares only the dusk herb)");

        // Drops: young → nothing; mature out of bloom → the herb; in bloom → the herb and 1–3 seeds.
        var daytime = !game.IsNight;
        var bloomingTile = daytime ? TileId.HerbSunpetal : TileId.HerbMoonglow;
        var idleTile = daytime ? TileId.HerbMoonglow : TileId.HerbSunpetal;
        var bloomingHerb = TilePhysics.HerbFor(bloomingTile)!;
        var idleHerb = TilePhysics.HerbFor(idleTile)!;
        int Count(ItemId id) => game.Player.Inventory.Count(id);
        void PlaceHerb(int x, TileId tile, byte state)
        {
            game.World.SetTile(new GridPoint(x, Floor - 1), tile);
            var herbCell = game.World.GetCell(x, Floor - 1);
            herbCell.State = state;
            game.World.SetCell(new GridPoint(x, Floor - 1), herbCell);
        }
        var (h0, s0, ih0, is0) = (Count(bloomingHerb.Herb), Count(bloomingHerb.Seeds), Count(idleHerb.Herb), Count(idleHerb.Seeds));
        PlaceHerb(60, bloomingTile, 0);
        game.ForceBreakTile(new GridPoint(60, Floor - 1));
        var youngNothing = Count(bloomingHerb.Herb) == h0 && Count(bloomingHerb.Seeds) == s0;
        PlaceHerb(62, idleTile, 1);
        game.ForceBreakTile(new GridPoint(62, Floor - 1));
        var idleHerbOnly = Count(idleHerb.Herb) == ih0 + 1 && Count(idleHerb.Seeds) == is0;
        PlaceHerb(64, bloomingTile, 1);
        game.ForceBreakTile(new GridPoint(64, Floor - 1));
        var seeds = Count(bloomingHerb.Seeds) - s0;
        check(youngNothing && idleHerbOnly && Count(bloomingHerb.Herb) == h0 + 1 && seeds is >= 1 and <= 3,
            $"harvests: a young herb drops nothing, a mature one its herb, a blooming one its herb and 1–3 seeds ({seeds})");

        // Planters: seeds take in a herb planter, and a swing's cut leaves what grows in one alone.
        game.World.SetTile(new GridPoint(33, Floor - 1), TileId.HerbPlanter);
        Idle(game, 10);
        Use(game, ItemId.RainreedSeeds, new GridPoint(33, Floor - 2));
        var planted = game.World.TileAt(33, Floor - 2) == TileId.HerbRainreed;
        game.World.SetTile(new GridPoint(35, Floor - 1), TileId.HerbSunpetal);
        var cut = game.CutPlantsInSquare(new GridPoint(34, Floor - 2), 1);
        check(planted && game.World.TileAt(33, Floor - 2) == TileId.HerbRainreed && game.World.TileAt(35, Floor - 1) == TileId.Air && cut == 1,
            "herb seeds grow in a planter (any herb), and swings cut herbs on the ground but not what grows in a planter");
    }

    // ---- grass: spreads into exposed soil, grows plants that grow tall, grassland grass stays above the surface line ----
    private static void Grass(Action<bool, string> check)
    {
        var game = Arena(9105);
        for (var x = 40; x <= 43; x++)
            game.World.SetTile(new GridPoint(x, Floor), TileId.Soil);
        game.World.SetTile(new GridPoint(40, Floor + 1), TileId.Soil); // buried: never exposed
        game.Ticker.UpdateCell(39, Floor);
        var spread = game.World.TileAt(40, Floor) == TileId.Grass && game.World.TileAt(41, Floor) == TileId.Soil &&
                     game.World.TileAt(40, Floor + 1) == TileId.Soil;
        // Below the surface line: jungle grass spreads into mud, grassland grass does not spread into soil.
        var deep = 60;
        for (var x = 30; x <= 60; x++)
        {
            game.World.SetTile(new GridPoint(x, deep), TileId.Rock);
            game.World.SetTile(new GridPoint(x, deep - 1), TileId.Air);
        }
        game.World.SetTile(new GridPoint(40, deep), TileId.JungleGrass);
        game.World.SetTile(new GridPoint(41, deep), TileId.Mud);
        game.World.SetTile(new GridPoint(50, deep), TileId.Grass);
        game.World.SetTile(new GridPoint(51, deep), TileId.Soil);
        game.Ticker.UpdateCell(40, deep);
        game.Ticker.UpdateCell(50, deep);
        var underground = game.World.TileAt(41, deep) == TileId.JungleGrass && game.World.TileAt(51, deep) == TileId.Soil;
        check(spread && underground,
            "grass spreads into neighbouring soil open to the air (jungle grass into mud anywhere, grassland grass only above the surface)");

        // Plants: grass sprouts a grass plant (1 in 12), which grows tall (1 in 20); it pops off when its ground goes.
        var plantAt = new GridPoint(36, Floor - 1);
        var updates = 0;
        while (updates < 1000 && game.World.TileAt(plantAt.X, plantAt.Y) == TileId.Air)
        {
            game.Ticker.UpdateCell(36, Floor);
            updates++;
        }
        var sprouted = game.World.TileAt(plantAt.X, plantAt.Y) == TileId.GrassPlant;
        var tallUpdates = 0;
        while (tallUpdates < 1000 && game.World.TileAt(plantAt.X, plantAt.Y) == TileId.GrassPlant)
        {
            game.Ticker.UpdateCell(plantAt.X, plantAt.Y);
            tallUpdates++;
        }
        var tall = game.World.TileAt(plantAt.X, plantAt.Y) == TileId.TallGrassPlant;
        game.ForceBreakTile(new GridPoint(36, Floor));
        Idle(game, 3);
        check(sprouted && tall && game.World.TileAt(plantAt.X, plantAt.Y) == TileId.Air && FloraParity.PlantOneIn(TileId.Grass) == 12 &&
              FloraParity.PlantOneIn(TileId.Snow) is null,
            $"grass grows grass plants ({updates} updates) that grow tall ({tallUpdates}) and pop off with their ground");
    }

    // ---- vines and cacti ----
    private static void VinesAndCacti(Action<bool, string> check)
    {
        var game = Arena(9106);
        // A grassy ceiling at row 10 over open air.
        for (var x = 50; x <= 70; x++)
            game.World.SetTile(new GridPoint(x, 10), TileId.Grass);
        game.Ticker.UpdateCell(52, 10);
        var sprouted = game.World.TileAt(52, 11) == TileId.Vine;
        for (var i = 0; i < 40; i++)
        {
            var tip = 11;
            while (game.World.TileAt(52, tip + 1) == TileId.Vine)
                tip++;
            game.Ticker.UpdateCell(52, tip);
        }
        var length = 0;
        while (game.World.TileAt(52, 11 + length) == TileId.Vine)
            length++;
        // A dense curtain: no new vine starts beside it.
        for (var x = 60; x <= 64; x++)
        for (var y = 11; y <= 18; y++)
            game.World.SetTile(new GridPoint(x, y), TileId.Vine);
        for (var i = 0; i < 20; i++)
            game.Ticker.UpdateCell(66, 10);
        check(sprouted && length >= 10 && game.World.TileAt(66, 11) == TileId.Air,
            $"vines grow from grass undersides and their tips ({length} long) until nearby vines are too dense");

        // Cacti: grow on 1 update in 15 up to their height cap.
        for (var x = 18; x <= 22; x++)
            game.World.SetTile(new GridPoint(x, Floor), TileId.DesertSand);
        game.World.SetTile(new GridPoint(20, Floor - 1), TileId.Cactus);
        for (var i = 0; i < 3000; i++)
        {
            var top = Floor - 1;
            while (game.World.TileAt(20, top - 1) == TileId.Cactus)
                top--;
            game.Ticker.UpdateCell(20, top);
        }
        var height = 0;
        while (game.World.TileAt(20, Floor - 1 - height) == TileId.Cactus)
            height++;
        check(height >= 3 && height <= 7 && FloraParity.CactusGrowOneIn == 15,
            $"cacti grow on 1 update in 15 and stop at their height cap ({height} tall)");
    }

    // ---- cutting: swings cut grass plants and vines; blocks replace grass plants ----
    private static void Cutting(Action<bool, string> check)
    {
        var game = Arena(9107);
        // Within the blade's reach of the player (standing at x 30): tall grass on the floor and a vine hanging from a ledge.
        var plant = new GridPoint(32, Floor - 1);
        game.World.SetTile(plant, TileId.TallGrassPlant);
        game.World.SetTile(new GridPoint(31, Floor - 3), TileId.Rock);
        game.World.SetTile(new GridPoint(31, Floor - 2), TileId.Vine);
        Use(game, ItemId.WoodenSword, plant, 12);
        Idle(game, 3);
        var cut = game.World.TileAt(plant.X, plant.Y) == TileId.Air && game.World.TileAt(31, Floor - 2) == TileId.Air;
        check(cut && FloraParity.IsCuttable(TileId.GrassPlant) && FloraParity.IsCuttable(TileId.HerbMoonglow) &&
              FloraParity.IsCuttable(TileId.PotSurface) && !FloraParity.IsCuttable(TileId.Sapling) && !FloraParity.IsCuttable(TileId.TreeTrunk),
            $"a melee swing cuts tall grass and vines (and herbs and pots), but not saplings or trunks ({game.World.TileAt(plant.X, plant.Y)}, {game.World.TileAt(31, Floor - 2)})");

        Idle(game, 30);
        var spot = new GridPoint(34, Floor - 1);
        game.World.SetTile(spot, TileId.GrassPlant);
        Use(game, ItemId.RockBlock, spot, 3);
        check(game.World.TileAt(spot.X, spot.Y) == TileId.Rock, "a placed block replaces the grass plant in its cell");
    }

    // ---- shaped melee projectiles cut what they pass (ref. Projectile.CutTilesAt) ----
    private static void StyledCutting(Action<bool, string> check)
    {
        var game = Arena(9108);
        // A yo-yo held out through a tall grass cell.
        var yoyoPlant = new GridPoint(33, Floor - 1);
        game.World.SetTile(yoyoPlant, TileId.TallGrassPlant);
        Use(game, ItemId.YoyoDrone, new GridPoint(35, Floor - 1), 60);
        var yoyoCut = game.World.TileAt(yoyoPlant.X, yoyoPlant.Y) == TileId.Air;
        Idle(game, 120);

        // A boomerang on its way out and back through a grass plant.
        var rangPlant = new GridPoint(36, Floor - 1);
        game.World.SetTile(rangPlant, TileId.GrassPlant);
        Use(game, ItemId.ReturnBoomerang, new GridPoint(40, Floor - 1));
        Idle(game, 150);
        var rangCut = game.World.TileAt(rangPlant.X, rangPlant.Y) == TileId.Air;

        // A flail launched along its chain.
        var flailPlant = new GridPoint(32, Floor - 1);
        game.World.SetTile(flailPlant, TileId.TallGrassPlant);
        Use(game, ItemId.ChainFlail, new GridPoint(35, Floor - 1));
        Idle(game, 120);
        var flailCut = game.World.TileAt(flailPlant.X, flailPlant.Y) == TileId.Air;
        check(yoyoCut && rangCut && flailCut,
            "shaped melee projectiles (yo-yos, boomerangs, flails) cut the plants they pass, unlike arrows and bullets");
    }

    // ---- world generation: grass plants are real tiles; the render layer keeps only debris on that ground ----
    private static void WorldGeneration(Action<bool, string> check)
    {
        var generated = new GameSimulation(seed: 8803, width: 400, height: 160) { FamilySpawnsEnabled = false };
        var plants = generated.World.CountTiles(TileId.GrassPlant) + generated.World.CountTiles(TileId.TallGrassPlant);
        var grassDebris = SpriteCatalog.DebrisFor(TileId.Grass) ?? [];
        var grassPlants = SpriteCatalog.PlantSpritesFor(TileId.Grass) ?? [];
        check(plants > 50 && grassPlants.Contains("fern_plant") && !grassDebris.Contains("fern_plant") && grassDebris.Contains("mossy_stone") &&
              SpriteCatalog.DebrisFor(TileId.Snow) is { Length: > 0 },
            $"world generation grows real grass plants ({plants}); the render-only layer keeps rocks and debris");
    }
}
