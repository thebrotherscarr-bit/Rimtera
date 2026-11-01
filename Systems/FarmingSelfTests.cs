using System.Numerics;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Farming and brewing (0.2.5; 0.3.11: no grow zones or Farm tab, farmers tend the planters near home): the Farm job giver, Brew bills, saves.</summary>
public static class FarmingSelfTests
{
    private const int Floor = 40;
    private static readonly GridPoint Chest = new(58, Floor - 1), Lab = new(74, Floor - 1);

    private static GameSimulation Field(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 120, height: 56) { FamilySpawnsEnabled = false };
        for (var y = 1; y < game.World.Height - 1; y++)
        for (var x = 1; x < game.World.Width - 1; x++)
        {
            var cell = new GridPoint(x, y);
            game.World.SetTile(cell, y > Floor ? TileId.Rock : y == Floor ? TileId.Grass : TileId.Air);
            var data = game.World.GetCell(cell);
            data.LiquidDepth = 0f;
            data.LiquidKind = LiquidKind.None;
            game.World.SetCell(cell, data);
        }
        game.World.SetTile(Chest, TileId.StorageCrate);
        game.Player.Motion.Position = new Vector2(8f, Floor - game.Player.Motion.Size.Y - 0.01f);
        game.Player.Motion.Velocity = Vector2.Zero;
        game.SetTimeOfDay(9f);
        game.AddStartingCrewForChecks();
        for (var i = 0; i < game.Settlers.Count; i++)
        {
            var agent = game.Settlers[i];
            agent.Motion.Position = new Vector2(60f + i * 3f, Floor - agent.Motion.Size.Y - 0.01f);
            agent.Motion.Velocity = Vector2.Zero;
            agent.HomePosition = agent.Motion.Position;
            agent.WanderTarget = agent.Motion.Position;
            foreach (var type in Enum.GetValues<WorkType>())
                agent.Work.Set(type, WorkCatalog.Off);
        }
        Clear(game);
        return game;
    }

    private static void Clear(GameSimulation game)
    {
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e6f);
    }

    private static bool TickUntil(GameSimulation game, Func<bool> done, int limit)
    {
        for (var i = 0; i < limit; i++)
        {
            if (i % 30 == 0 && done())
                return true;
            game.Tick(new GameInput(new MovementInput(0f)));
            Clear(game);
        }
        return done();
    }

    /// <summary>Planter boxes on the ground from <paramref name="left"/>, <paramref name="width"/> wide; returns the cells above them.</summary>
    private static GridPoint[] Planters(GameSimulation game, int left, int width)
    {
        for (var x = left; x < left + width; x++)
            game.World.SetTile(new GridPoint(x, Floor), TileId.HerbPlanter);
        return Enumerable.Range(left, width).Select(x => new GridPoint(x, Floor - 1)).ToArray();
    }

    private static int Growing(GameSimulation game, IEnumerable<GridPoint> cells, TileId herb) => cells.Count(cell => game.World.TileAt(cell.X, cell.Y) == herb);

    public static void Run(Action<bool, string> check)
    {
        // ---- planting: a farmer fetches seeds from a chest and plants the empty planters near home, not bare ground ----
        var plant = Field(9902);
        var farmer = plant.Settlers[0];
        farmer.Work.Set(WorkType.Farm, 1);
        plant.GetContainer(Chest)!.Add(ItemId.SunpetalSeeds, 10);
        var field = Planters(plant, 63, 5);
        var planted = TickUntil(plant, () => Growing(plant, field, TileId.HerbSunpetal) == 5, 3600);
        check(planted && plant.World.TileAt(62, Floor - 1) == TileId.Air && plant.World.TileAt(69, Floor - 1) == TileId.Air &&
              plant.GetContainer(Chest)!.Count(ItemId.SunpetalSeeds) == 0 && farmer.Pouch.GetValueOrDefault(ItemId.SunpetalSeeds) == 5,
            $"with no zones to mark, a farmer fetches seeds from a chest and plants every empty planter near home, leaving bare grass alone " +
            $"({Growing(plant, field, TileId.HerbSunpetal)} planted, pouch {farmer.Pouch.GetValueOrDefault(ItemId.SunpetalSeeds)}, {farmer.Activity})");

        // ---- harvesting: blooming herbs near home are harvested, the herbs stored and the planters replanted ----
        var crop = Field(9903);
        var grower = crop.Settlers[0];
        grower.Work.Set(WorkType.Farm, 1);
        var bed = Planters(crop, 63, 5);
        foreach (var cell in bed)
        {
            crop.World.SetTile(cell, TileId.HerbSunpetal);
            var data = crop.World.GetCell(cell);
            data.State = TilePhysics.HerbMatureBit;
            crop.World.SetCell(cell, data);
        }
        var stored = TickUntil(crop, () => crop.GetContainer(Chest)!.Count(ItemId.SunpetalHerb) >= 5 && Growing(crop, bed, TileId.HerbSunpetal) == 5, 5400);
        check(stored && bed.All(cell => (crop.World.GetCell(cell).State & TilePhysics.HerbMatureBit) == 0),
            $"a farmer harvests blooming herbs near home, stores them and replants the planters with the seeds they drop " +
            $"(stored {crop.GetContainer(Chest)!.Count(ItemId.SunpetalHerb)}, growing {Growing(crop, bed, TileId.HerbSunpetal)}, {grower.Activity})");

        // ---- a herb out of bloom waits; taken off Farm, a farmer sets down what they carry ----
        var dusk = Field(9904);
        dusk.Settlers[0].Work.Set(WorkType.Farm, 1);
        var moon = new[] { new GridPoint(63, Floor - 1), new GridPoint(64, Floor - 1) };
        foreach (var cell in moon)
        {
            dusk.World.SetTile(new GridPoint(cell.X, Floor), TileId.Mud);
            dusk.World.SetTile(cell, TileId.HerbMoonglow);
            var data = dusk.World.GetCell(cell);
            data.State = TilePhysics.HerbMatureBit;
            dusk.World.SetCell(cell, data);
        }
        dusk.Settlers[0].Pouch[ItemId.SunpetalSeeds] = 3;
        TickUntil(dusk, () => false, 300);
        var waited = Growing(dusk, moon, TileId.HerbMoonglow) == 2;
        dusk.Settlers[0].Work.Set(WorkType.Farm, WorkCatalog.Off);
        TickUntil(dusk, () => false, 30);
        check(waited && dusk.Settlers[0].Pouch.Count == 0 && dusk.GroundItems.Any(item => item.ItemId == ItemId.SunpetalSeeds),
            "a mature herb out of bloom (moonglow by day) is left to bloom, and a farmer taken off Farm sets their seeds down");

        // ---- skill: Plants speeds farm work and gives extra herbs above 10 ----
        var stats = Field(9905).Settlers[0].Stats;
        ColonistStats WithPlants(int level)
        {
            var levels = stats.SkillLevels.ToArray();
            levels[(int)ColonistSkill.Plants] = level;
            return stats with { SkillLevels = levels, PlantWorkSpeed = 0.60f + level * 0.04f };
        }
        check(GameSimulation.FarmSeconds(WithPlants(20)) < GameSimulation.FarmSeconds(WithPlants(0)) * 0.5f &&
              GameSimulation.ExtraHerbChance(WithPlants(10)) == 0f && Math.Abs(GameSimulation.ExtraHerbChance(WithPlants(20)) - 0.3f) < 1e-4f,
            $"Plants skill speeds farm work ({GameSimulation.FarmSeconds(WithPlants(0)):0.0} s at 0, {GameSimulation.FarmSeconds(WithPlants(20)):0.0} s at 20) and gives a 3% per level extra herb above 10");

        // ---- brewing: bills at a chem lab are Brew work, not Operate ----
        int Brewed(WorkType work)
        {
            var game = Field(9906);
            game.World.SetTile(Lab, TileId.ChemLab);
            game.GetContainer(Chest)!.Add(ItemId.SunpetalHerb, 4);
            game.GetContainer(Chest)!.Add(ItemId.Berry, 4);
            game.Settlers[0].Work.Set(work, 1);
            var bill = game.AddBill(Lab, "phys-sunpetal-stim", BillMode.DoCount, 2);
            if (bill is null)
                return -1;
            TickUntil(game, () => bill.Done >= 2 && game.Settlers[0].Carried.IsEmpty, 5400);
            return game.GetContainer(Chest)!.Count(ItemId.MedStim);
        }
        var byBrewer = Brewed(WorkType.Brew);
        var byOperator = Brewed(WorkType.Operate);
        var labWork = Field(9907);
        labWork.World.SetTile(Lab, TileId.ChemLab);
        check(byBrewer >= 4 && byOperator == 0 && labWork.BillWork(Lab) == WorkType.Brew &&
              WorkCatalog.Get(WorkType.Brew).HasJobs && WorkCatalog.Get(WorkType.Farm).HasJobs,
            $"bills at a chem lab are worked by Brew crew (on Medical), not Operate crew (stims: brewer {byBrewer}, operator {byOperator})");

        // ---- saves keep a farmer's pouch; a save's grow zones (from before 0.3.11) load and are dropped ----
        var kept = Field(9908);
        kept.Settlers[0].Pouch[ItemId.FrostbellSeeds] = 4;
        var oldSave = kept.CaptureSnapshot() with { GrowZones = [new GrowZoneSnapshot(63, Floor - 1, 5, TileId.HerbFrostbell)] };
        var reloaded = GameSimulation.Restore(oldSave);
        check(reloaded.Settlers[0].Pouch.GetValueOrDefault(ItemId.FrostbellSeeds) == 4 && reloaded.CaptureSnapshot().GrowZones is null,
            "a farmer's seeds are saved with the world; grow zones in an older save load and are dropped");

        // ---- no Farm tab (0.3.11) or Work tab (0.3.9) ----
        var ui = Field(9909);
        var hub = new Rimtera.UI.InventoryUiState(ui.Player.Inventory, game: ui);
        Rimtera.UI.UiButtonElement? Button(string id) =>
            hub.DescendantsAndSelf().OfType<Rimtera.UI.UiButtonElement>().FirstOrDefault(button => button.Id == id);
        check(Button("tab-farm") is null && Button("tab-work") is null && Button("tab-research") is not null &&
              WorkCatalog.Get(WorkType.Farm).HasJobs && WorkCatalog.Get(WorkType.Brew).HasJobs,
            "there is no Farm tab: farmers tend the planters near home by themselves; Farm and Brew are work people choose by skill");
    }
}
