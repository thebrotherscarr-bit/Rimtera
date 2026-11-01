using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// The colony by default (0.3.9): with no bills to set, the labs keep healing in stock and the cooking station keeps food,
/// worked by the people whose trade or skill it is, within what research and the station AI's power allow.
/// </summary>
public static class StockingSelfTests
{
    private const int Floor = 40;
    private static readonly GridPoint Chest = new(58, Floor - 1), Station = new(66, Floor - 1);

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
        game.World.SetTile(Chest, TileId.StorageCrate);
        game.Player.Motion.Position = new Vector2(8f, Floor - game.Player.Motion.Size.Y - 0.01f);
        game.Player.Motion.Velocity = Vector2.Zero;
        game.SetTimeOfDay(9f);
        Clear(game);
        return game;
    }

    private static void Clear(GameSimulation game)
    {
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e6f);
    }

    private static void Tick(GameSimulation game, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            game.Tick(new GameInput(new MovementInput(0f)));
            Clear(game);
        }
    }

    private static (GameSimulation Game, TownNpc Worker) Kept(int seed, TileId station, TownNpcKind trade, ItemId product, int crafts)
    {
        var game = Yard(seed);
        game.World.SetTile(Station, station);
        var recipe = game.Crafting.FindByOutput(product).First();
        foreach (var group in recipe.Ingredients)
            game.GetContainer(Chest)!.Add(group.Alternatives[0], group.Quantity * crafts);
        var worker = game.AddTownNpc(trade, new Vector2(62f, Floor - 3f), bound: false);
        worker.HomeCell = new GridPoint(62, Floor - 2);
        return (game, worker);
    }

    public static void Run(Action<bool, string> check)
    {
        // ---- the chemist keeps healing in stock, the cook keeps food; nothing is set ----
        var (lab, _) = Kept(9690, TileId.ChemLab, TownNpcKind.Medic, ItemId.MedStim, 3);
        Tick(lab, 60 * 60);
        var order = lab.BillsAt(Station).SingleOrDefault();
        var stims = lab.GetContainer(Chest)!.Count(ItemId.MedStim);
        var (kitchen, _) = Kept(9691, TileId.CookingStation, TownNpcKind.Cook, ItemId.ProteinRation, 2);
        Tick(kitchen, 60 * 60);
        var meals = kitchen.GetContainer(Chest)!.Slots.Where(stack => !stack.IsEmpty && kitchen.Items[stack.ItemId].UseKind == ItemUseKind.Food)
            .Sum(stack => stack.Count);
        check(order is { Auto: true, Mode: BillMode.UntilHave, Target: GameSimulation.StockTarget } && stims >= 3 && meals >= 1,
            $"with nothing set, the medic keeps the chem lab's healing in stock and the cook keeps food (med stims {stims}, meals {meals}; " +
            $"order: keep {order?.Target} {order?.RecipeId})");

        // ---- 0.7.5: every station crafts unpowered, so an empty reserve no longer pauses the order ----
        var (dark, _) = Kept(9692, TileId.ChemLab, TownNpcKind.Medic, ItemId.MedStim, 3);
        new DevConsole(dark).Execute("power set 0");
        Tick(dark, 6 * 60);
        check(dark.BillsAt(Station).Count == 1,
            "a station's standing order keeps going when the station AI is out of power: the station simply works unpowered");

        // ---- a bill set another way (scripts, tests) is left alone ----
        var (manual, _) = Kept(9693, TileId.ChemLab, TownNpcKind.Medic, ItemId.MedStim, 3);
        var set = manual.AddBill(Station, manual.Crafting.FindByOutput(ItemId.MedStim).First().Id, BillMode.DoCount, 1)!;
        Tick(manual, 6 * 60);
        check(manual.BillsAt(Station) is [{ Auto: false } only] && ReferenceEquals(only, set),
            "a bill set another way is left as it is");
    }
}
