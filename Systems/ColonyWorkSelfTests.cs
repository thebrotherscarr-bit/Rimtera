using System.Numerics;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Colony work (0.2.1): work priorities, the Guard and Tend job givers, staying home, saves and the Work tab (Haul removed in 0.2.9).</summary>
public static class ColonyWorkSelfTests
{
    private const int Floor = 40;

    private static GameSimulation Arena(int seed)
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
        ClearEnemies(game);
        return game;
    }

    private static void ClearEnemies(GameSimulation game, EnemyAgent? keep = null)
    {
        foreach (var enemy in game.Enemies.ToArray())
            if (!ReferenceEquals(enemy, keep))
                enemy.TakeDamage(1e6f);
    }

    private static void Tick(GameSimulation game, int ticks, EnemyAgent? keep = null)
    {
        for (var i = 0; i < ticks; i++)
        {
            game.Tick(new GameInput(new MovementInput(0f)));
            ClearEnemies(game, keep);
        }
    }

    public static void Run(Action<bool, string> check)
    {
        // ---- priorities: defaults from passions, the Work tab's cycle, the order work is considered in ----
        var sample = new GameSimulation(seed: 9601, width: 80, height: 48);
        sample.AddStartingCrewForChecks();
        var crewmate = sample.Settlers[0];
        // 0.3.9: preferred work comes from the person's skills: their two best first, then anything they are good at
        // (skill 6+) or love, then the rest; nothing is refused.
        var stats = crewmate.Stats;
        var best = Enum.GetValues<ColonistSkill>().OrderByDescending(skill => stats.Level(skill) + (stats.Passion(skill) == PassionLevel.None ? 0f : 0.5f)).Take(2).ToHashSet();
        var derivedSettings = ColonyWorkSettings.For(stats);
        var defaultsRight = crewmate.Work.Capture().SequenceEqual(TownPeople.WorkFor(TownNpcKind.Settler, stats).Capture()) &&
                            WorkCatalog.All.All(info => derivedSettings.Priority(info.Type) ==
            (best.Contains(info.Skill) ? WorkCatalog.Highest
                : stats.Level(info.Skill) >= ColonyWorkSettings.GoodSkill || stats.Passion(info.Skill) != PassionLevel.None ? WorkCatalog.Passionate
                : WorkCatalog.Default));
        var settings = ColonyWorkSettings.For(crewmate.Stats);
        settings.Set(WorkType.Fish, 3);
        var cycle = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            settings.Cycle(WorkType.Fish);
            cycle.Add(settings.Priority(WorkType.Fish));
        }
        foreach (var type in Enum.GetValues<WorkType>())
            settings.Set(type, WorkCatalog.Off);
        settings.Set(WorkType.Fish, 1);
        settings.Set(WorkType.Guard, 2);
        settings.Set(WorkType.Tend, 2);
        check(defaultsRight && cycle.SequenceEqual([4, 0, 1, 2, 3]) &&
              settings.Order().SequenceEqual([WorkType.Fish, WorkType.Guard, WorkType.Tend]),
            "work priorities come from skills (best two first, then good or loved work); the cycle still runs 1-2-3-4-off; 1 is done first, ties by work type");

        // ---- old saves: 0.2.1-0.2.8 priorities held Haul (at 2) and Build (at 8); 0.2.9 drops those two ----
        var migrated = new ColonyWorkSettings();
        migrated.Restore([1, 2, 4, 3, 3, 3, 3, 3, 4, 2, 3]);
        check(migrated.Priority(WorkType.Guard) == 1 && migrated.Priority(WorkType.Tend) == 2 && migrated.Priority(WorkType.Operate) == 3 &&
              migrated.Priority(WorkType.Research) == 2 && migrated.Priority(WorkType.Trade) == 3,
            "work priorities from saves before 0.2.9 load without the removed Haul and Build columns");

        // ---- Tend: a hurt crewmate heals faster with a tender than resting alone ----
        float Tended(bool tender)
        {
            var game = Arena(9603);
            var patient = game.Settlers[1];
            patient.HitPoints -= patient.MaximumHitPoints * 0.6f;
            var before = patient.HitPoints;
            if (tender)
                game.Settlers[0].Work.Set(WorkType.Tend, 1);
            Tick(game, 600);
            return patient.HitPoints - before;
        }
        var withTender = Tended(true);
        var alone = Tended(false);
        check(withTender > alone + 10f,
            $"a tender treats a hurt crewmate (healed {withTender:0} in 10 s with a tender, {alone:0} without)");

        // ---- Guard: a creature near home is fought; without a guard it is left alone ----
        float Guarded(bool guard)
        {
            var game = Arena(9604);
            if (guard)
                game.Settlers[0].Work.Set(WorkType.Guard, 1);
            var intruder = game.SpawnCreatureAt(EnemyKind.MossSlime, new Vector2(56f, Floor - 1.2f));
            var health = intruder.HitPoints;
            Tick(game, 600, keep: intruder);
            return intruder.IsAlive ? health - intruder.HitPoints : health;
        }
        var guarded = Guarded(true);
        var unguarded = Guarded(false);
        check(guarded > unguarded + 10f,
            $"a guard fights a creature that comes near home ({guarded:0} damage with a guard, {unguarded:0} without)");

        // ---- saves keep priorities, following and a carried stack ----
        var kept = Arena(9605);
        kept.Settlers[0].Work.Set(WorkType.Fish, 1);
        kept.Settlers[0].Work.Set(WorkType.Guard, 4);
        kept.Settlers[0].Carried = new ItemStack(ItemId.RockBlock, 7);
        var reloaded = GameSimulation.Restore(kept.CaptureSnapshot());
        var derived = ColonyWorkSettings.For(reloaded.Settlers[0].Stats).Capture();
        check(reloaded.Settlers.Count == 2 && reloaded.Settlers[0].Work.Capture().SequenceEqual(derived) &&
              reloaded.Settlers[0].Name == kept.Settlers[0].Name && reloaded.Settlers[0].Stats.SkillLevels.SequenceEqual(kept.Settlers[0].Stats.SkillLevels) &&
              reloaded.Settlers[0].Carried == new ItemStack(ItemId.RockBlock, 7),
            "settlers, their skills and a carried stack are saved with the world; work priorities come from each person's skills again on load (0.3.9)");

        // ---- 0.3.9: no Work tab; each person's preferred work comes from their background's skills ----
        var tabbed = Arena(9606);
        var hub = new Rimtera.UI.InventoryUiState(tabbed.Player.Inventory, game: tabbed);
        Rimtera.UI.UiButtonElement? Button(string id) =>
            hub.DescendantsAndSelf().OfType<Rimtera.UI.UiButtonElement>().FirstOrDefault(button => button.Id == id);
        var first = tabbed.Settlers[0];
        var firstStats = first.Stats;
        var auto = ColonyWorkSettings.For(firstStats);
        var bestSkill = Enum.GetValues<ColonistSkill>().MaxBy(skill => firstStats.Level(skill) + (firstStats.Passion(skill) == PassionLevel.None ? 0f : 0.5f));
        var bestWork = WorkCatalog.All.Where(info => info.Skill == bestSkill).Select(info => info.Type).ToArray();
        check(Button("tab-work") is null && Button("tab-bills") is null &&
              bestWork.All(type => auto.Priority(type) == WorkCatalog.Highest) && WorkCatalog.All.All(info => auto.Priority(info.Type) != WorkCatalog.Off),
            $"there is no Work or Bills tab; each person puts the work of their best skill first ({bestSkill}: {string.Join(", ", bestWork)}) and never refuses work");
    }
}
