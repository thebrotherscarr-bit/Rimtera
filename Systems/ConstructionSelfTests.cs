using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Build and research (0.2.7): blueprints, the Build job giver, research projects and their effects, saves and the tabs.</summary>
public static class ConstructionSelfTests
{
    private const int Floor = 40;
    private static readonly GridPoint Chest = new(58, Floor - 1), Bench = new(66, Floor - 1);

    private static GameSimulation Site(int seed)
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
        game.AddStartingCrewForChecks();
        for (var i = 0; i < game.Colonists.Count; i++)
        {
            var agent = game.Colonists[i];
            agent.Motion.Position = new Vector2(60f + i * 2f, Floor - agent.Motion.Size.Y - 0.01f);
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

    public static void Run(Action<bool, string> check)
    {
        // ---- blueprints: a block marks a cell; again unmarks it; only placeable blocks ----
        var marks = Site(9971);
        var rock = marks.Items[ItemId.RockBlock];
        var cell = new GridPoint(30, Floor - 4);
        var marked = marks.TryToggleBlueprint(rock, cell) && marks.Blueprints.GetValueOrDefault(cell) == ItemId.RockBlock;
        var unmarked = marks.TryToggleBlueprint(rock, cell) && !marks.Blueprints.ContainsKey(cell);
        check(marked && unmarked && !marks.CanBlueprint(marks.Items[ItemId.Berry]) && marks.Player.Inventory.Count(ItemId.RockBlock) == 0,
            "a block marks a blueprint at a cell and marking it again removes it; only placeable blocks can be blueprinted, and nothing is spent");

        // ---- building: a builder fetches blocks from a chest, places every blueprint, and returns the rest ----
        var build = Site(9972);
        var builder = build.Colonists[0];
        builder.Work.Set(WorkType.Build, 1);
        build.GetContainer(Chest)!.Add(ItemId.RockBlock, 10);
        var shelf = Enumerable.Range(64, 4).Select(x => new GridPoint(x, Floor - 4)).ToArray();
        foreach (var spot in shelf)
            build.TryToggleBlueprint(build.Items[ItemId.RockBlock], spot);
        var built = TickUntil(build, () => shelf.All(spot => build.World.TileAt(spot.X, spot.Y) == TileId.Rock) && builder.BuildStock.IsEmpty, 5400);
        check(built && build.Blueprints.Count == 0 && build.GetContainer(Chest)!.Count(ItemId.RockBlock) == 6 && build.Flags.Get("colony.built") == 4,
            $"a builder fetches blocks from a chest near home, places every blueprint and puts the spare blocks back " +
            $"(built {shelf.Count(spot => build.World.TileAt(spot.X, spot.Y) == TileId.Rock)}/4, chest {build.GetContainer(Chest)!.Count(ItemId.RockBlock)}, {builder.Activity})");

        // ---- without blocks in storage a blueprint waits ----
        var waiting = Site(9973);
        waiting.Colonists[0].Work.Set(WorkType.Build, 1);
        waiting.TryToggleBlueprint(waiting.Items[ItemId.RockBlock], new GridPoint(64, Floor - 4));
        TickUntil(waiting, () => false, 600);
        check(waiting.Blueprints.Count == 1 && waiting.World.TileAt(64, Floor - 4) == TileId.Air,
            "a blueprint with no blocks in storage waits");

        // ---- research: studying at a bench adds points; prerequisites; finishing applies the effect ----
        var lab = Site(9974);
        lab.World.SetTile(Bench, TileId.ResearchTerminal);
        lab.Colonists[0].Work.Set(WorkType.Research, 1);
        var chosen = lab.ChooseResearch(GameSimulation.FieldManuals);
        TickUntil(lab, () => false, 900);
        var studied = lab.ResearchProgress(GameSimulation.FieldManuals);
        var locked = !lab.CanResearch(GameSimulation.CobaltStudies) && !lab.ChooseResearch(GameSimulation.CobaltStudies);
        lab.FinishResearch(GameSimulation.FindProject(GameSimulation.Metallurgy)!);
        var copperRecipes = lab.Crafting.Recipes.Where(recipe => recipe.RequiresGuideUnlock && recipe.GuideUnlockMaterials?.Contains(ItemId.CopperBar) == true).ToArray();
        var taught = copperRecipes.Length > 0 && copperRecipes.All(recipe => lab.Crafting.KnownGuideRecipes.Contains(recipe.Id));
        lab.FinishResearch(GameSimulation.FindProject(GameSimulation.FieldManuals)!);
        check(chosen && studied > 3f && locked && lab.CanResearch(GameSimulation.CobaltStudies) && taught &&
              lab.ResearchBillSpeed > 1.1f && lab.CurrentResearch is null && lab.Flags.IsSet("research.metallurgy"),
            $"a Research colonist studies the chosen project at a research terminal ({studied:0} points in 15 s); a project waits for its prerequisite; " +
            "finishing teaches recipes or speeds work");

        // ---- saves keep blueprints and research, and re-teach researched recipes ----
        var kept = Site(9975);
        kept.TryToggleBlueprint(kept.Items[ItemId.RockBlock], new GridPoint(40, Floor - 4));
        kept.FinishResearch(GameSimulation.FindProject(GameSimulation.Metallurgy)!);
        kept.ChooseResearch(GameSimulation.CobaltStudies);
        var reloaded = GameSimulation.Restore(kept.CaptureSnapshot());
        var relearned = reloaded.Crafting.Recipes.Where(recipe => recipe.RequiresGuideUnlock && recipe.GuideUnlockMaterials?.Contains(ItemId.CopperBar) == true)
            .All(recipe => reloaded.Crafting.KnownGuideRecipes.Contains(recipe.Id));
        check(reloaded.Blueprints.GetValueOrDefault(new GridPoint(40, Floor - 4)) == ItemId.RockBlock && reloaded.ResearchDone(GameSimulation.Metallurgy) &&
              reloaded.CurrentResearch == GameSimulation.CobaltStudies && relearned,
            "blueprints, finished research and the chosen project are saved, and researched recipes are known again after loading");

        // ---- the Build and Research tabs, and the Work tab's ten columns ----
        var ui = Site(9976);
        var hub = new Rimtera.UI.InventoryUiState(ui.Player.Inventory, game: ui);
        Rimtera.UI.UiButtonElement? Button(string id) =>
            hub.DescendantsAndSelf().OfType<Rimtera.UI.UiButtonElement>().FirstOrDefault(button => button.Id == id);
        Button("tab-build")?.Command?.Invoke();
        Button("build-mode")?.Command?.Invoke();
        var modeOn = ui.BlueprintMode;
        Button("tab-research")?.Command?.Invoke();
        Button($"research-{GameSimulation.Herbalism}")?.Command?.Invoke();
        Button("tab-work")?.Command?.Invoke();
        var id = ui.Colonists[0].Id;
        check(modeOn && ui.CurrentResearch == GameSimulation.Herbalism &&
              Button($"work-{id}-{WorkType.Build}") is not null && Button($"work-{id}-{WorkType.Research}") is not null,
            "the Build tab turns blueprint mode on, the Research tab chooses a project, and the Work tab has Build and Research");
    }
}
