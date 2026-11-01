using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Storage tags and residents at stations (0.2.4): chest filters, delivery by tag, town NPCs working bills, saves, the UI.</summary>
public static class StorageSelfTests
{
    private const int Floor = 40;
    private static readonly GridPoint Bench = new(72, Floor - 1), Near = new(66, Floor - 1), Far = new(80, Floor - 1);

    private static GameSimulation Yard(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 120, height: 56) { FamilySpawnsEnabled = false };
        for (var y = 1; y < game.World.Height - 1; y++)
        for (var x = 1; x < game.World.Width - 1; x++)
        {
            var cell = new GridPoint(x, y);
            game.World.SetTile(cell, y >= Floor ? TileId.Rock : TileId.Air);
            var data = game.World.GetCell(cell);
            data.LiquidDepth = 0f;
            data.LiquidKind = LiquidKind.None;
            game.World.SetCell(cell, data);
        }
        game.World.SetTile(Near, TileId.StorageCrate);
        game.World.SetTile(Far, TileId.StorageCrate);
        game.Player.Motion.Position = new Vector2(8f, Floor - game.Player.Motion.Size.Y - 0.01f);
        game.Player.Motion.Velocity = Vector2.Zero;
        game.SetTimeOfDay(10f);
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

    private static void Tick(GameSimulation game, int ticks) => TickUntil(game, () => false, ticks);

    public static void Run(Action<bool, string> check)
    {
        // ---- categories: every item falls in exactly one ----
        var catalog = new GameSimulation(seed: 9801, width: 64, height: 48).Items;
        var single = catalog.All.All(item => StorageCategories.Of(item) is var category && category != StorageCategory.None &&
                                             ((ushort)category & ((ushort)category - 1)) == 0);
        check(single && StorageCategories.Of(catalog[ItemId.Berry]) == StorageCategory.Food &&
              StorageCategories.Of(catalog[ItemId.RockBlock]) == StorageCategory.Blocks &&
              StorageCategories.Listed.Aggregate(StorageCategory.None, (all, entry) => all | entry.Category) == StorageCategory.All,
            "every item falls in exactly one storage category (food, blocks, materials, potions, arms, tools, gear, critters)");

        // ---- filters: toggling, what a chest takes ----
        var tags = Yard(9802);
        tags.ToggleChestCategory(Far, StorageCategory.Food);
        var allButFood = tags.ChestFilter(Far) == (StorageCategory.All & ~StorageCategory.Food);
        tags.SetChestFilter(Far, StorageCategory.Food);
        check(allButFood && tags.ChestTakes(Far, ItemId.Berry) && !tags.ChestTakes(Far, ItemId.RockBlock) &&
              tags.ChestFilter(Near) == StorageCategory.All && tags.ChestTakes(Near, ItemId.RockBlock),
            "a chest takes everything until tagged; the toggles turn categories on and off");

        // ---- haulers deliver by tag: food to the food chest (though it is farther), the rest to the general chest ----
        var haul = Yard(9803);
        haul.SetChestFilter(Far, StorageCategory.Food);
        haul.Colonists[0].Work.Set(WorkType.Haul, 1);
        haul.SpawnGroundItem(ItemId.Berry, 4, new Vector2(64.5f, Floor - 1.5f));
        haul.SpawnGroundItem(ItemId.RockBlock, 6, new Vector2(65.5f, Floor - 1.5f));
        var near = haul.GetContainer(Near)!;
        var far = haul.GetContainer(Far)!;
        var sorted = TickUntil(haul, () => far.Count(ItemId.Berry) == 4 && near.Count(ItemId.RockBlock) == 6, 3600);
        check(sorted && near.Count(ItemId.Berry) == 0 && far.Count(ItemId.RockBlock) == 0,
            $"haulers carry food to the chest tagged for food and blocks to the general chest (food chest: {far.Count(ItemId.Berry)} berries, " +
            $"{far.Count(ItemId.RockBlock)} rock; general: {near.Count(ItemId.Berry)} berries, {near.Count(ItemId.RockBlock)} rock)");

        // ---- an item no chest takes stays where it is ----
        var refuse = Yard(9804);
        refuse.SetChestFilter(Near, StorageCategory.Food);
        refuse.SetChestFilter(Far, StorageCategory.Food);
        refuse.Colonists[0].Work.Set(WorkType.Haul, 1);
        refuse.SpawnGroundItem(ItemId.RockBlock, 3, new Vector2(64.5f, Floor - 1.5f));
        Tick(refuse, 900);
        check(refuse.GetContainer(Near)!.Count(ItemId.RockBlock) == 0 && refuse.GetContainer(Far)!.Count(ItemId.RockBlock) == 0 &&
              refuse.GroundItems.Any(item => item.ItemId == ItemId.RockBlock) && refuse.Colonists[0].Carried.IsEmpty,
            "an item no chest takes is left on the ground");

        // ---- a resident works a bill near home by day, and stops when told ----
        int ResidentCrafts(bool works, float hour)
        {
            var game = Yard(9805);
            game.World.SetTile(Bench, TileId.Workbench);
            game.GetContainer(Near)!.Add(ItemId.RockBlock, 60);
            game.GetContainer(Near)!.Add(ItemId.WoodBlock, 15);
            game.SetTimeOfDay(hour);
            var npc = game.AddTownNpc(TownNpcKind.Mechanic, new Vector2(68f, Floor - 3f), bound: false);
            npc.HomeCell = new GridPoint(68, Floor - 2);
            npc.WorksStations = works;
            var bill = game.AddBill(Bench, "furnace", BillMode.DoCount, 1)!;
            TickUntil(game, () => bill.Done >= 1 && npc.Carried.IsEmpty, 3600);
            return game.GetContainer(Near)!.Count(ItemId.FurnaceBlock) + game.GetContainer(Far)!.Count(ItemId.FurnaceBlock);
        }
        var byDay = ResidentCrafts(true, 9f);
        var toldNot = ResidentCrafts(false, 9f);
        var atNight = ResidentCrafts(true, 22f);
        check(byDay >= 1 && toldNot == 0 && atNight == 0,
            $"a housed resident works bills at a station near home by day and stores the product; not when told not to, nor at night (day {byDay}, off {toldNot}, night {atNight})");

        // ---- saves keep chest tags and residents' work toggles and loads ----
        var kept = Yard(9806);
        kept.SetChestFilter(Far, StorageCategory.Food | StorageCategory.Potions);
        var keeper = kept.AddTownNpc(TownNpcKind.Mechanic, new Vector2(68f, Floor - 3f), bound: false);
        keeper.HomeCell = new GridPoint(68, Floor - 2);
        keeper.WorksStations = false;
        keeper.Held[ItemId.RockBlock] = 5;
        keeper.Carried = new ItemStack(ItemId.FurnaceBlock, 1);
        var reloaded = GameSimulation.Restore(kept.CaptureSnapshot());
        var back = reloaded.TownNpcs.FirstOrDefault(npc => npc.Kind == TownNpcKind.Mechanic);
        check(reloaded.ChestFilter(Far) == (StorageCategory.Food | StorageCategory.Potions) && reloaded.ChestFilter(Near) == StorageCategory.All &&
              back is { WorksStations: false } && back.Held.GetValueOrDefault(ItemId.RockBlock) == 5 && back.Carried == new ItemStack(ItemId.FurnaceBlock, 1),
            "chest tags and residents' bill toggles, fetched ingredients and carried products are saved with the world");

        // ---- the UI: a chest's tag toggles, and the residents' toggle in the Work tab ----
        var ui = Yard(9807);
        var resident = ui.AddTownNpc(TownNpcKind.Mechanic, new Vector2(68f, Floor - 3f), bound: false);
        resident.HomeCell = new GridPoint(68, Floor - 2);
        var chestHub = new Rimtera.UI.InventoryUiState(ui.Player.Inventory, chest: ui.GetContainer(Near), game: ui, chestCell: Near);
        Rimtera.UI.UiButtonElement? Button(Rimtera.UI.InventoryUiState hub, string id) =>
            hub.DescendantsAndSelf().OfType<Rimtera.UI.UiButtonElement>().FirstOrDefault(button => button.Id == id);
        var buttons = StorageCategories.Listed.All(entry => Button(chestHub, $"chest-filter-{entry.Category}") is not null);
        Button(chestHub, $"chest-filter-{StorageCategory.Blocks}")?.Command?.Invoke();
        var hub = new Rimtera.UI.InventoryUiState(ui.Player.Inventory, game: ui);
        Button(hub, "tab-work")?.Command?.Invoke();
        Button(hub, $"work-npc-{resident.Id}")?.Command?.Invoke();
        check(buttons && ui.ChestFilter(Near) == (StorageCategory.All & ~StorageCategory.Blocks) && !resident.WorksStations,
            "an open chest shows a toggle per category, and the Work tab turns a resident's bill work on and off");
    }
}
