using System.Numerics;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Farming and brewing (0.2.5): grow zones, the Farm job giver, Brew bills at chem labs, saves and the Farm tab.</summary>
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
        for (var i = 0; i < game.Colonists.Count; i++)
        {
            var agent = game.Colonists[i];
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

    private static int Growing(GameSimulation game, GrowZone zone) => zone.Cells.Count(cell => game.World.TileAt(cell.X, cell.Y) == zone.Herb);

    public static void Run(Action<bool, string> check)
    {
        // ---- zones: the soil picks the herb; no overlaps; a cap; resize and herb cycling ----
        var zones = Field(9901);
        zones.Player.Motion.Position = new Vector2(30f, Floor - zones.Player.Motion.Size.Y - 0.01f);
        var marked = zones.AddGrowZoneAtPlayer(8);
        var overlap = zones.AddGrowZoneAtPlayer(4);
        var cycled = marked is not null && Herb(marked) == TileId.HerbSunpetal && CycleTo(marked) == TileId.HerbMoonglow;
        static TileId Herb(GrowZone zone) => zone.Herb;
        static TileId CycleTo(GrowZone zone)
        {
            zone.CycleHerb();
            return zone.Herb;
        }
        var resized = marked is not null && zones.ResizeGrowZone(marked, 12) && marked.Width == 12;
        for (var i = 0; zones.GrowZones.Count < GameSimulation.MaxGrowZones; i++)
            zones.AddGrowZone(new GridPoint(2 + i * 2, 20), 1, TileId.HerbSunpetal);
        check(marked is { Y: Floor - 1, Width: 12 } && overlap is null && cycled && resized &&
              zones.AddGrowZone(new GridPoint(100, 20), 2, TileId.HerbSunpetal) is null && zones.RemoveGrowZone(marked) && zones.GrowZoneAt(new GridPoint(30, Floor - 1)) is null,
            "a grow zone is marked at the player's feet for the herb the soil suits, never overlaps another, is capped at 12, resizes, cycles its herb and can be removed");

        // ---- planting: a farmer fetches seeds from a chest and plants every cell that takes the herb ----
        var plant = Field(9902);
        var farmer = plant.Colonists[0];
        farmer.Work.Set(WorkType.Farm, 1);
        plant.GetContainer(Chest)!.Add(ItemId.SunpetalSeeds, 10);
        var field = plant.AddGrowZone(new GridPoint(63, Floor - 1), 6, TileId.HerbSunpetal)!;
        plant.World.SetTile(new GridPoint(66, Floor), TileId.Rock); // bare rock: no sunpetal grows there
        var planted = TickUntil(plant, () => Growing(plant, field) == 5, 3600);
        check(planted && plant.World.TileAt(66, Floor - 1) == TileId.Air && plant.GetContainer(Chest)!.Count(ItemId.SunpetalSeeds) == 0 &&
              farmer.Pouch.GetValueOrDefault(ItemId.SunpetalSeeds) == 5,
            $"a farmer fetches seeds from a chest and plants each zone cell on the herb's soil, skipping bare rock ({Growing(plant, field)} planted, pouch {farmer.Pouch.GetValueOrDefault(ItemId.SunpetalSeeds)}, {farmer.Activity})");

        // ---- harvesting: blooming herbs are harvested, the herbs stored and the cells replanted from the seeds ----
        var crop = Field(9903);
        var grower = crop.Colonists[0];
        grower.Work.Set(WorkType.Farm, 1);
        var bed = crop.AddGrowZone(new GridPoint(63, Floor - 1), 5, TileId.HerbSunpetal)!;
        foreach (var cell in bed.Cells)
        {
            crop.World.SetTile(cell, TileId.HerbSunpetal);
            var data = crop.World.GetCell(cell);
            data.State = TilePhysics.HerbMatureBit;
            crop.World.SetCell(cell, data);
        }
        var stored = TickUntil(crop, () => crop.GetContainer(Chest)!.Count(ItemId.SunpetalHerb) >= 5 && Growing(crop, bed) == 5, 5400);
        check(stored && bed.Cells.All(cell => (crop.World.GetCell(cell).State & TilePhysics.HerbMatureBit) == 0),
            $"a farmer harvests blooming herbs, stores them and replants with the seeds they drop (stored {crop.GetContainer(Chest)!.Count(ItemId.SunpetalHerb)}, growing {Growing(crop, bed)}, {grower.Activity})");

        // ---- a herb out of bloom waits; taken off Farm, a farmer sets down what they carry ----
        var dusk = Field(9904);
        dusk.Colonists[0].Work.Set(WorkType.Farm, 1);
        var moon = dusk.AddGrowZone(new GridPoint(63, Floor - 1), 2, TileId.HerbMoonglow)!;
        foreach (var cell in moon.Cells)
        {
            dusk.World.SetTile(new GridPoint(cell.X, Floor), TileId.Mud);
            dusk.World.SetTile(cell, TileId.HerbMoonglow);
            var data = dusk.World.GetCell(cell);
            data.State = TilePhysics.HerbMatureBit;
            dusk.World.SetCell(cell, data);
        }
        dusk.Colonists[0].Pouch[ItemId.SunpetalSeeds] = 3;
        TickUntil(dusk, () => false, 300);
        var waited = Growing(dusk, moon) == 2;
        dusk.Colonists[0].Work.Set(WorkType.Farm, WorkCatalog.Off);
        TickUntil(dusk, () => false, 30);
        check(waited && dusk.Colonists[0].Pouch.Count == 0 && dusk.GroundItems.Any(item => item.ItemId == ItemId.SunpetalSeeds),
            "a mature herb out of bloom (moonglow by day) is left to bloom, and a farmer taken off Farm sets their seeds down");

        // ---- skill: Plants speeds farm work and gives extra herbs above 10 ----
        var stats = Field(9905).Colonists[0].ColonistStats;
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
            game.Colonists[0].Work.Set(work, 1);
            var bill = game.AddBill(Lab, "phys-sunpetal-stim", BillMode.DoCount, 2);
            if (bill is null)
                return -1;
            TickUntil(game, () => bill.Done >= 2 && game.Colonists[0].Carried.IsEmpty, 5400);
            return game.GetContainer(Chest)!.Count(ItemId.MedStim);
        }
        var byBrewer = Brewed(WorkType.Brew);
        var byOperator = Brewed(WorkType.Operate);
        var labWork = Field(9907);
        labWork.World.SetTile(Lab, TileId.ChemLab);
        check(byBrewer >= 4 && byOperator == 0 && labWork.BillWork(Lab) == WorkType.Brew &&
              WorkCatalog.Get(WorkType.Brew).HasJobs && WorkCatalog.Get(WorkType.Farm).HasJobs,
            $"bills at a chem lab are worked by Brew crew (on Medical), not Operate crew (stims: brewer {byBrewer}, operator {byOperator})");

        // ---- saves keep zones and a farmer's pouch ----
        var kept = Field(9908);
        kept.AddGrowZone(new GridPoint(63, Floor - 1), 5, TileId.HerbFrostbell);
        kept.Colonists[0].Pouch[ItemId.FrostbellSeeds] = 4;
        var reloaded = GameSimulation.Restore(kept.CaptureSnapshot());
        check(reloaded.GrowZones.Count == 1 && reloaded.GrowZones[0] is { X: 63, Y: Floor - 1, Width: 5, Herb: TileId.HerbFrostbell } &&
              reloaded.Colonists[0].Pouch.GetValueOrDefault(ItemId.FrostbellSeeds) == 4,
            "grow zones and a farmer's seeds are saved with the world");

        // ---- the Farm tab, and Farm and Brew in the Work tab ----
        var ui = Field(9909);
        ui.Player.Motion.Position = new Vector2(30f, Floor - ui.Player.Motion.Size.Y - 0.01f);
        var hub = new Rimtera.UI.InventoryUiState(ui.Player.Inventory, game: ui);
        Rimtera.UI.UiButtonElement? Button(string id) =>
            hub.DescendantsAndSelf().OfType<Rimtera.UI.UiButtonElement>().FirstOrDefault(button => button.Id == id);
        Button("tab-farm")?.Command?.Invoke();
        Button("farm-add")?.Command?.Invoke();
        var added = ui.GrowZones.Count == 1;
        Button("farm-zone-0-herb")?.Command?.Invoke();
        var herbChanged = added && ui.GrowZones[0].Herb == TileId.HerbMoonglow;
        Button("farm-zone-0-more")?.Command?.Invoke();
        var wider = added && ui.GrowZones[0].Width == 9;
        Button("farm-zone-0-delete")?.Command?.Invoke();
        Button("tab-work")?.Command?.Invoke();
        var id = ui.Colonists[0].Id;
        check(added && herbChanged && wider && ui.GrowZones.Count == 0 &&
              Button($"work-{id}-{WorkType.Farm}") is not null && Button($"work-{id}-{WorkType.Brew}") is not null,
            "the hub's Farm tab marks a zone, cycles its herb, widens and removes it; the Work tab has Farm and Brew");
    }
}
