using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Research (0.2.7): projects and their effects, saves and the Research tab. (Build and blueprints were removed in 0.2.9.)</summary>
public static class ResearchSelfTests
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

        // ---- saves keep research, and re-teach researched recipes ----
        var kept = Site(9975);
        kept.FinishResearch(GameSimulation.FindProject(GameSimulation.Metallurgy)!);
        kept.ChooseResearch(GameSimulation.CobaltStudies);
        var reloaded = GameSimulation.Restore(kept.CaptureSnapshot());
        var relearned = reloaded.Crafting.Recipes.Where(recipe => recipe.RequiresGuideUnlock && recipe.GuideUnlockMaterials?.Contains(ItemId.CopperBar) == true)
            .All(recipe => reloaded.Crafting.KnownGuideRecipes.Contains(recipe.Id));
        check(reloaded.ResearchDone(GameSimulation.Metallurgy) &&
              reloaded.CurrentResearch == GameSimulation.CobaltStudies && relearned,
            "finished research and the chosen project are saved, and researched recipes are known again after loading");

        // ---- the Research tab, and the Work tab's columns (no Haul or Build since 0.2.9) ----
        var ui = Site(9976);
        var hub = new Rimtera.UI.InventoryUiState(ui.Player.Inventory, game: ui);
        Rimtera.UI.UiButtonElement? Button(string id) =>
            hub.DescendantsAndSelf().OfType<Rimtera.UI.UiButtonElement>().FirstOrDefault(button => button.Id == id);
        var noBuildTab = Button("tab-build") is null;
        Button("tab-research")?.Command?.Invoke();
        Button($"research-{GameSimulation.Herbalism}")?.Command?.Invoke();
        Button("tab-work")?.Command?.Invoke();
        var id = ui.Colonists[0].Id;
        check(noBuildTab && ui.CurrentResearch == GameSimulation.Herbalism && Button($"work-{id}-{WorkType.Research}") is not null &&
              Button($"work-{id}-{WorkType.Trade}") is not null && !Enum.GetNames<WorkType>().Contains("Haul") && !Enum.GetNames<WorkType>().Contains("Build"),
            "the Research tab chooses a project; the Work tab has Research and Trade, and there is no Build tab, Haul or Build work");
    }
}
