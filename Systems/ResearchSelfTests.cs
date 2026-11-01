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

    private static ResearchTier Tier(string id) => GameSimulation.FindTier(id)!;

    public static void Run(Action<bool, string> check)
    {
        // ---- a boss opens its tier; the station AI studies it on its own; the recipes stay locked until it is done ----
        var game = Site(9974);
        var forge = game.Crafting.FindByOutput(ItemId.PowerForgeMk2).First();
        var trauma = game.Crafting.FindByOutput(ItemId.TraumaStim).First();
        var lockedBefore = game.ResearchLock(forge);
        var closed = !game.ResearchOpen(Tier("overseer")) && game.CurrentResearch is null;
        new DevConsole(game).Execute("defeat StoneSentinel");
        var opened = game.ResearchOpen(Tier("overseer")) && game.CurrentResearch?.Id == "overseer";
        var lockedWhileStudying = game.ResearchLock(forge) is { } studying && studying.Contains("% done") && game.ResearchLock(trauma) is not null;
        TickUntil(game, () => game.ResearchDone("overseer"), (int)(Tier("overseer").Cost * 60f) + 600);
        check(lockedBefore is not null && closed && opened && lockedWhileStudying && game.ResearchDone("overseer") &&
              game.ResearchLock(forge) is null && game.ResearchLock(trauma) is null && game.Flags.IsSet("research.overseer") &&
              game.ResearchLock(game.Crafting.FindByOutput(ItemId.AlloyAnvil).First()) is not null,
            $"defeating the Overseer opens its research tier; the station AI studies it in {Tier("overseer").Minutes:0} minutes; " +
            "the Power Forge Mk2 and the Overseer-circuit recipes unlock only when it is done, and later tiers stay locked");

        // ---- researchers speed it up: research crew at a terminal, and housed research residents ----
        var lab = Site(9975);
        lab.World.SetTile(Bench, TileId.ResearchTerminal);
        new DevConsole(lab).Execute("defeat ScoutMech");
        var aiOnly = lab.StudyRate;
        var resident = lab.AddTownNpc(TownNpcKind.HeadResearcher, new Vector2(64f, Floor - 3f), bound: false);
        resident.HomeCell = new GridPoint(64, Floor - 2);
        var withResident = lab.StudyRate;
        lab.Colonists[0].Work.Set(WorkType.Research, 1);
        var start = lab.ResearchProgress("scout");
        TickUntil(lab, () => false, 900);
        var gained = lab.ResearchProgress("scout") - start;
        check(withResident > aiOnly && Math.Abs(aiOnly - GameSimulation.BaseStudyRate) < 1e-4f && gained > 15f * withResident + 3f,
            $"research residents and research crew speed the study (AI {aiOnly:0.0}/s, with a resident {withResident:0.0}/s, {gained:0} points in 15 s with crew too)");

        // ---- saves keep research; a save from before the tiers counts its defeated bosses' tiers as done ----
        var kept = Site(9976);
        new DevConsole(kept).Execute("defeat StoneSentinel");
        TickUntil(kept, () => false, 300);
        var progress = kept.ResearchProgress("overseer");
        var reloaded = GameSimulation.Restore(kept.CaptureSnapshot());
        var old = kept.CaptureSnapshot();
        var legacy = GameSimulation.Restore(old with
        {
            Research = new ResearchSnapshot("field-manuals", new Dictionary<string, float> { ["metallurgy"] = 1000f }, ["metallurgy"]),
            Content = old.Content! with
            {
                Flags = old.Content!.Flags?.Where(pair => !pair.Key.StartsWith("research.", StringComparison.Ordinal)).ToDictionary(pair => pair.Key, pair => pair.Value)
            }
        });
        check(Math.Abs(reloaded.ResearchProgress("overseer") - progress) < 0.01f && !reloaded.ResearchDone("overseer") &&
              legacy.ResearchDone("overseer") && !legacy.ResearchDone("fission"),
            "research progress is saved; an older save keeps every recipe its defeated bosses already opened");

        // ---- the Research tab shows the tiers; the console finishes and resets them ----
        var ui = Site(9977);
        var console = new DevConsole(ui);
        console.Execute("research done all");
        var allDone = GameSimulation.ResearchTiers.All(tier => ui.ResearchDone(tier.Id));
        console.Execute("research reset fission");
        var hub = new Rimtera.UI.InventoryUiState(ui.Player.Inventory, game: ui);
        Rimtera.UI.UiButtonElement? Button(string id) =>
            hub.DescendantsAndSelf().OfType<Rimtera.UI.UiButtonElement>().FirstOrDefault(button => button.Id == id);
        Button("tab-research")?.Command?.Invoke();
        var rows = GameSimulation.ResearchTiers.All(tier => hub.DescendantsAndSelf().OfType<Rimtera.UI.UiTextElement>().Any(text => text.Id == $"research-name-{tier.Id}"));
        check(allDone && !ui.ResearchDone("fission") && rows && Button("tab-build") is null,
            "the Research tab lists every tier, and the console finishes and resets tiers");
    }
}
