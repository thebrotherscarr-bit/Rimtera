using System.Numerics;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Cooking and fishing (0.2.6): Cook bills, crew eating from storage, crew fishing, and the Work tab columns.</summary>
public static class KitchenSelfTests
{
    private const int Floor = 40;
    private static readonly GridPoint Chest = new(58, Floor - 1), Stove = new(66, Floor - 1);

    private static GameSimulation Camp(int seed, bool pond = false)
    {
        var game = new GameSimulation(seed: seed, width: 120, height: 56) { FamilySpawnsEnabled = false };
        for (var y = 1; y < game.World.Height - 1; y++)
        for (var x = 1; x < game.World.Width - 1; x++)
        {
            var cell = new GridPoint(x, y);
            var water = pond && x >= 72 && x <= 83 && y >= Floor && y <= Floor + 3;
            game.World.SetTile(cell, y >= Floor && !water ? TileId.Rock : TileId.Air);
            var data = game.World.GetCell(cell);
            data.LiquidDepth = water ? 1f : 0f;
            data.LiquidKind = water ? LiquidKind.Water : LiquidKind.None;
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
            agent.Motion.Position = new Vector2(62f + i * 2f, Floor - agent.Motion.Size.Y - 0.01f);
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

    private static void Starve(ColonistAgent agent, float below)
    {
        for (var i = 0; i < 400 && agent.Needs.FoodReserve >= below; i++)
            agent.Needs.Advance(0.25f);
    }

    public static void Run(Action<bool, string> check)
    {
        // ---- Cook bills: a cooking station's bills are Cook work, not Operate ----
        int Cooked(WorkType work)
        {
            var game = Camp(9951);
            game.World.SetTile(Stove, TileId.CookingStation);
            var recipe = game.BillRecipesAt(Stove).First(candidate => candidate.Output == ItemId.ProteinRation);
            foreach (var group in recipe.Ingredients)
                game.GetContainer(Chest)!.Add(group.Alternatives[0], group.Quantity * 2);
            game.Colonists[0].Work.Set(work, 1);
            var bill = game.AddBill(Stove, recipe.Id, BillMode.DoCount, 2)!;
            TickUntil(game, () => bill.Done >= 2 && game.Colonists[0].Carried.IsEmpty, 5400);
            return game.GetContainer(Chest)!.Count(ItemId.ProteinRation);
        }
        var byCook = Cooked(WorkType.Cook);
        var byOperator = Cooked(WorkType.Operate);
        var stoveGame = Camp(9952);
        stoveGame.World.SetTile(Stove, TileId.CookingStation);
        check(byCook >= 2 && byOperator == 0 && stoveGame.BillWork(Stove) == WorkType.Cook && WorkCatalog.Get(WorkType.Cook).HasJobs,
            $"bills at a cooking station are worked by Cook crew, not Operate crew (rations: cook {byCook}, operator {byOperator})");

        // ---- eating: a hungry colonist eats from a chest near home, the most filling food first ----
        var meal = Camp(9953);
        var hungry = meal.Colonists[0];
        Starve(hungry, 0.3f);
        meal.GetContainer(Chest)!.Add(ItemId.Berry, 5);
        meal.GetContainer(Chest)!.Add(ItemId.ProteinRation, 1);
        var before = hungry.Needs.FoodReserve;
        var fed = TickUntil(meal, () => hungry.Needs.FoodReserve >= GameSimulation.EatBelow, 3600);
        check(fed && before < 0.3f && meal.GetContainer(Chest)!.Count(ItemId.ProteinRation) == 0 && meal.Flags.Get("colony.meals") >= 1,
            $"a hungry colonist eats from a chest near home, the most filling food first (food {before:0.00} → {hungry.Needs.FoodReserve:0.00}, " +
            $"rations left {meal.GetContainer(Chest)!.Count(ItemId.ProteinRation)}, berries left {meal.GetContainer(Chest)!.Count(ItemId.Berry)})");

        // ---- fishing: a Fish colonist fishes the pond near home and carries the catch to storage ----
        var pond = Camp(9954, pond: true);
        var fisher = pond.Colonists[0];
        fisher.Work.Set(WorkType.Fish, 1);
        var spot = pond.FishingSpotNear(fisher.HomePosition);
        var landed = TickUntil(pond, () => pond.GetContainer(Chest)!.Slots.Count(stack => !stack.IsEmpty) > 0 && pond.Flags.Get("colony.fish") >= GameSimulation.CatchLoad, 9000);
        var dry = Camp(9955);
        dry.Colonists[0].Work.Set(WorkType.Fish, 1);
        TickUntil(dry, () => false, 600);
        check(spot is { Y: Floor } && landed && dry.FishingSpotNear(dry.Colonists[0].HomePosition) is null && dry.Flags.Get("colony.fish") == 0,
            $"a Fish colonist fishes open water near home and stores the catch; with no water near home there is no fishing (catches {pond.Flags.Get("colony.fish")}, {fisher.Activity})");

        // ---- skill: Animals speeds fishing and raises fishing power ----
        var stats = Camp(9956).Colonists[0].ColonistStats;
        ColonistStats WithAnimals(int level)
        {
            var levels = stats.SkillLevels.ToArray();
            levels[(int)ColonistSkill.Animals] = level;
            return stats with { SkillLevels = levels };
        }
        check(GameSimulation.FishSeconds(WithAnimals(20)) < GameSimulation.FishSeconds(WithAnimals(0)) * 0.6f &&
              GameSimulation.CrewFishingPower(WithAnimals(20)) > GameSimulation.CrewFishingPower(WithAnimals(0)),
            $"Animals skill speeds fishing ({GameSimulation.FishSeconds(WithAnimals(0)):0.0} s at 0, {GameSimulation.FishSeconds(WithAnimals(20)):0.0} s at 20) and raises fishing power");

        // ---- the Work tab has Cook and Fish ----
        var ui = Camp(9957);
        var hub = new Rimtera.UI.InventoryUiState(ui.Player.Inventory, game: ui);
        Rimtera.UI.UiButtonElement? Button(string id) =>
            hub.DescendantsAndSelf().OfType<Rimtera.UI.UiButtonElement>().FirstOrDefault(button => button.Id == id);
        Button("tab-work")?.Command?.Invoke();
        var id = ui.Colonists[0].Id;
        Button($"work-{id}-{WorkType.Fish}")?.Command?.Invoke();
        check(Button($"work-{id}-{WorkType.Cook}") is not null && ui.Colonists[0].Work.Priority(WorkType.Fish) == WorkCatalog.Highest,
            "the Work tab has Cook and Fish columns, and clicking sets them");
    }
}
