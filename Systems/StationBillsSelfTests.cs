using System.Numerics;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Station bills (0.2.3): bill queues, the Operate job giver (fetch, craft, store), skill, saves and the Bills tab.</summary>
public static class StationBillsSelfTests
{
    private const int Floor = 40;
    private static readonly GridPoint Bench = new(72, Floor - 1), Chest = new(66, Floor - 1);

    private static GameSimulation Workshop(int seed, int rock = 60, int wood = 15)
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
        game.World.SetTile(Bench, TileId.Workbench);
        game.World.SetTile(Chest, TileId.StorageCrate);
        var chest = game.GetContainer(Chest)!;
        if (rock > 0) chest.Add(ItemId.RockBlock, rock);
        if (wood > 0) chest.Add(ItemId.WoodBlock, wood);
        game.Player.Motion.Position = new Vector2(8f, Floor - game.Player.Motion.Size.Y - 0.01f);
        game.Player.Motion.Velocity = Vector2.Zero;
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
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e6f);
        return game;
    }

    private static void Tick(GameSimulation game, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            game.Tick(new GameInput(new MovementInput(0f)));
            foreach (var enemy in game.Enemies.ToArray())
                enemy.TakeDamage(1e6f);
        }
    }

    /// <summary>Ticks until <paramref name="done"/> holds or <paramref name="limit"/> ticks pass.</summary>
    private static bool TickUntil(GameSimulation game, Func<bool> done, int limit = 3600)
    {
        for (var i = 0; i < limit; i += 30)
        {
            if (done())
                return true;
            Tick(game, 30);
        }
        return done();
    }

    public static void Run(Action<bool, string> check)
    {
        // ---- bill recipes: station recipes the station area can make; hand recipes get no bills ----
        var shop = Workshop(9701);
        var offered = shop.BillRecipesAt(Bench).Select(recipe => recipe.Id).ToArray();
        check(offered.Contains("furnace") && !offered.Contains("workbench") && !offered.Contains("hull-panel") &&
              shop.BillRecipesAt(Chest).Count == 0 && shop.AddBill(Chest, "furnace") is null && shop.AddBill(Bench, "hull-panel") is null,
            $"a station offers bills for its own recipes (furnace at the fabricator), not hand recipes, and a chest takes none ({offered.Length} offered)");

        // ---- the queue: add, cap, reorder, remove; modes cycle; a "make N" bill stops at N ----
        var first = shop.AddBill(Bench, "furnace")!;
        var second = shop.AddBill(Bench, "anvil", BillMode.Forever);
        for (var i = shop.BillsAt(Bench).Count; i < GameSimulation.MaxBillsPerStation; i++)
            shop.AddBill(Bench, "furnace");
        var capped = shop.AddBill(Bench, "furnace") is null && shop.BillsAt(Bench).Count == GameSimulation.MaxBillsPerStation;
        var moved = second is not null && shop.MoveBill(Bench, second, -1) && shop.BillsAt(Bench)[0] == second && !shop.MoveBill(Bench, second, -1);
        var modes = new List<BillMode>();
        for (var i = 0; i < 3; i++)
        {
            first.CycleMode();
            modes.Add(first.Mode);
        }
        var wants = new StationBill("furnace", BillMode.DoCount, 2) { Done = 2 };
        var keep = new StationBill("furnace", BillMode.UntilHave, 3);
        check(capped && moved && modes.SequenceEqual([BillMode.UntilHave, BillMode.Forever, BillMode.DoCount]) &&
              !wants.Wants(0) && keep.Wants(2) && !keep.Wants(3) && !new StationBill("furnace", BillMode.Forever) { Suspended = true }.Wants(0) &&
              shop.RemoveBill(Bench, first) && !shop.BillsAt(Bench).Contains(first),
            "bills queue up to the cap, move up the queue, cycle make → keep → forever, stop when done or paused, and can be deleted");

        // ---- Operate: an operator fetches from the chest, crafts at the station, and stores the product ----
        var work = Workshop(9702);
        var operatorAgent = work.Settlers[0];
        operatorAgent.Work.Set(WorkType.Operate, 1);
        var bill = work.AddBill(Bench, "furnace", BillMode.DoCount, 2)!;
        var store = work.GetContainer(Chest)!;
        var made = TickUntil(work, () => bill.Done >= 2 && operatorAgent.Carried.IsEmpty, 7200);
        Tick(work, 600); // the bill is done: nothing more is made
        check(made && store.Count(ItemId.FurnaceBlock) >= 2 && bill.Done == 2 && store.Count(ItemId.RockBlock) == 20 &&
              store.Count(ItemId.WoodBlock) == 5 && operatorAgent.Held.Count == 0 && operatorAgent.Carried.IsEmpty,
            $"an Operate colonist fetches ingredients from a chest, crafts at the station and stores the product, and stops at the bill's count " +
            $"(done {bill.Done}, furnaces {store.Count(ItemId.FurnaceBlock)}, rock {store.Count(ItemId.RockBlock)}, wood {store.Count(ItemId.WoodBlock)}, {operatorAgent.Activity})");

        // ---- nobody works a bill without Operate, while paused, or once storage has enough ----
        int RockUsed(Action<GameSimulation, StationBill> setup)
        {
            var game = Workshop(9703);
            game.Settlers[0].Work.Set(WorkType.Operate, 1);
            var order = game.AddBill(Bench, "furnace", BillMode.UntilHave, 1)!;
            setup(game, order);
            Tick(game, 1800);
            return 60 - game.GetContainer(Chest)!.Count(ItemId.RockBlock) - game.Settlers.Sum(agent => agent.Held.GetValueOrDefault(ItemId.RockBlock));
        }
        var offOperate = RockUsed((game, _) => game.Settlers[0].Work.Set(WorkType.Operate, WorkCatalog.Off));
        var paused = RockUsed((_, order) => order.Suspended = true);
        var stocked = RockUsed((game, _) => game.GetContainer(Chest)!.Add(ItemId.FurnaceBlock, 1));
        var wanted = RockUsed((_, _) => { });
        check(offOperate == 0 && paused == 0 && stocked == 0 && wanted == 20,
            $"bills wait for an Operate colonist, stay idle while paused, and a keep-N bill stops once storage holds N (rock used: off {offOperate}, paused {paused}, stocked {stocked}, wanted {wanted})");

        // ---- short ingredients: the bill waits, and nothing is taken from the chest ----
        var short_ = Workshop(9704, rock: 10, wood: 15);
        short_.Settlers[0].Work.Set(WorkType.Operate, 1);
        var waiting = short_.AddBill(Bench, "furnace")!;
        Tick(short_, 900);
        check(waiting.Done == 0 && short_.GetContainer(Chest)!.Count(ItemId.RockBlock) == 10 && short_.Settlers[0].Held.Count == 0,
            "a bill whose ingredients the chests near the station can't cover waits, and its operator takes nothing");

        // ---- skill: higher Crafting crafts faster, and above 10 can make an extra batch ----
        var crew = Workshop(9705).Settlers[0].Stats;
        Rimtera.Characters.ColonistStats WithCrafting(int level)
        {
            var levels = crew.SkillLevels.ToArray();
            levels[(int)ColonistSkill.Crafting] = level;
            return crew with { SkillLevels = levels, CraftingSpeed = 0.65f + level * 0.035f };
        }
        var novice = WithCrafting(0);
        var master = WithCrafting(20);
        check(GameSimulation.OperateSeconds(master) < GameSimulation.OperateSeconds(novice) * 0.6f &&
              GameSimulation.ExtraBatchChance(WithCrafting(10)) == 0f && Math.Abs(GameSimulation.ExtraBatchChance(master) - 0.2f) < 1e-4f,
            $"Crafting skill speeds bills ({GameSimulation.OperateSeconds(novice):0.0} s at 0, {GameSimulation.OperateSeconds(master):0.0} s at 20) and gives a 2% per level extra-batch chance above 10");

        // ---- taken off Operate mid-job: fetched ingredients are set down for the haulers ----
        var dropped = Workshop(9706);
        var quitter = dropped.Settlers[0];
        quitter.Work.Set(WorkType.Operate, 1);
        dropped.AddBill(Bench, "furnace", BillMode.Forever);
        var fetched = TickUntil(dropped, () => quitter.Held.Count > 0);
        quitter.Work.Set(WorkType.Operate, WorkCatalog.Off);
        Tick(dropped, 30);
        check(fetched && quitter.Held.Count == 0 && quitter.OperateStation is null &&
              dropped.GroundItems.Any(item => item.ItemId == ItemId.RockBlock),
            "a colonist taken off Operate sets fetched ingredients down where haulers can pick them up");

        // ---- saves keep bills (mode, target, progress, pause) and fetched ingredients; a removed station loses its bills ----
        var kept = Workshop(9707);
        var saved = kept.AddBill(Bench, "furnace", BillMode.UntilHave, 12)!;
        saved.Done = 4;
        saved.Suspended = true;
        kept.AddBill(Bench, "anvil");
        kept.Settlers[0].Held[ItemId.RockBlock] = 7;
        var reloaded = GameSimulation.Restore(kept.CaptureSnapshot());
        var back = reloaded.BillsAt(Bench);
        var gone = Workshop(9708);
        gone.AddBill(Bench, "furnace");
        gone.World.SetTile(Bench, TileId.Air);
        check(back.Count == 2 && back[0].RecipeId == "furnace" && back[0].Mode == BillMode.UntilHave && back[0].Target == 12 &&
              back[0].Done == 4 && back[0].Suspended && back[1].RecipeId == "anvil" &&
              reloaded.Settlers[0].Held.GetValueOrDefault(ItemId.RockBlock) == 7 &&
              GameSimulation.Restore(gone.CaptureSnapshot()).Bills.Count == 0,
            "bills and fetched ingredients are saved with the world; a station that is gone takes its bills with it");

        // ---- 0.3.9: no Bills tab; nobody sets bills, the colony keeps its own stock ----
        var tabbed = Workshop(9709);
        var hub = new Rimtera.UI.InventoryUiState(tabbed.Player.Inventory, game: tabbed);
        check(hub.DescendantsAndSelf().OfType<Rimtera.UI.UiButtonElement>().All(button => button.Id is not ("tab-bills" or "bills-add")) &&
              WorkCatalog.Get(WorkType.Operate).HasJobs,
            "there is no Bills tab: stations keep their stock by themselves (StockingSelfTests), and Operate is still work people do");
    }
}
