using System.Numerics;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Colony work (0.2.1): work priorities, the Guard, Tend and Haul job givers, staying home, saves and the Work tab.</summary>
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
        var crewmate = sample.Colonists[0];
        var defaultsRight = WorkCatalog.All.All(info =>
            crewmate.Work.Priority(info.Type) == (crewmate.ColonistStats.Passion(info.Skill) == PassionLevel.None ? WorkCatalog.Default : WorkCatalog.Passionate));
        var settings = ColonyWorkSettings.For(crewmate.ColonistStats);
        settings.Set(WorkType.Haul, 3);
        var cycle = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            settings.Cycle(WorkType.Haul);
            cycle.Add(settings.Priority(WorkType.Haul));
        }
        foreach (var type in Enum.GetValues<WorkType>())
            settings.Set(type, WorkCatalog.Off);
        settings.Set(WorkType.Haul, 1);
        settings.Set(WorkType.Guard, 2);
        settings.Set(WorkType.Tend, 2);
        check(defaultsRight && cycle.SequenceEqual([4, 0, 1, 2, 3]) &&
              settings.Order().SequenceEqual([WorkType.Haul, WorkType.Guard, WorkType.Tend]) && !crewmate.FollowsPlayer,
            "work priorities: 3 by default, 2 for a passion; clicking cycles 1-2-3-4-off; 1 is done first, ties by work type; crew stay home");

        // ---- Haul: loose items near home go into a chest; items by the player are left for the player ----
        var haul = Arena(9602);
        var chestCell = new GridPoint(70, Floor - 1);
        haul.World.SetTile(chestCell, TileId.StorageCrate);
        haul.Colonists[0].Work.Set(WorkType.Haul, 1);
        var loose = haul.SpawnGroundItem(ItemId.RockBlock, 5, new Vector2(66.5f, Floor - 1.5f));
        haul.SpawnGroundItem(ItemId.Berry, 3, new Vector2(10.5f, Floor - 1.5f)); // beside the player: left alone
        var hauled = false;
        for (var i = 0; i < 1800 && !hauled; i += 30)
        {
            Tick(haul, 30);
            hauled = haul.GetContainer(chestCell)!.Count(ItemId.RockBlock) == 5;
        }
        check(hauled && loose is not null && !haul.GroundItems.Contains(loose) &&
              haul.GetContainer(chestCell)!.Count(ItemId.Berry) == 0 && haul.Colonists[0].Carried.IsEmpty,
            $"a hauler carries loose items near home into a chest, and leaves what lies by the player (chest {haul.GetContainer(chestCell)!.Count(ItemId.RockBlock)}, " +
            $"left on the ground {haul.GroundItems.Count(item => item.ItemId == ItemId.RockBlock)}, carrying {haul.Colonists[0].Carried.Count}, {haul.Colonists[0].Activity})");

        // ---- Tend: a hurt crewmate heals faster with a tender than resting alone ----
        float Tended(bool tender)
        {
            var game = Arena(9603);
            var patient = game.Colonists[1];
            patient.TakeDamage(patient.MaximumHitPoints * 0.6f);
            var before = patient.HitPoints;
            if (tender)
                game.Colonists[0].Work.Set(WorkType.Tend, 1);
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
                game.Colonists[0].Work.Set(WorkType.Guard, 1);
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
        kept.Colonists[0].Work.Set(WorkType.Haul, 1);
        kept.Colonists[0].Work.Set(WorkType.Guard, 4);
        kept.Colonists[1].FollowsPlayer = true;
        kept.Colonists[0].Carried = new ItemStack(ItemId.RockBlock, 7);
        var reloaded = GameSimulation.Restore(kept.CaptureSnapshot());
        check(reloaded.Colonists[0].Work.Capture().SequenceEqual(kept.Colonists[0].Work.Capture()) && reloaded.Colonists[1].FollowsPlayer &&
              !reloaded.Colonists[0].FollowsPlayer && reloaded.Colonists[0].Carried == new ItemStack(ItemId.RockBlock, 7),
            "work priorities, following and a hauled stack are saved with the world");

        // ---- the Work tab in the inventory hub ----
        var tabbed = Arena(9606);
        var hub = new Rimtera.UI.InventoryUiState(tabbed.Player.Inventory, game: tabbed);
        Rimtera.UI.UiButtonElement? Button(string id) =>
            hub.DescendantsAndSelf().OfType<Rimtera.UI.UiButtonElement>().FirstOrDefault(button => button.Id == id);
        Button("tab-work")?.Command?.Invoke();
        var first = tabbed.Colonists[0];
        Button($"work-{first.Id}-{WorkType.Haul}")?.Command?.Invoke();
        Button($"work-{first.Id}-follow")?.Command?.Invoke();
        check(first.Work.Priority(WorkType.Haul) == WorkCatalog.Highest && first.FollowsPlayer &&
              Button($"work-{first.Id}-{WorkType.Guard}") is not null && Button($"work-{first.Id}-{WorkType.Tend}") is not null,
            "the hub's Work tab lists each crew member's Guard, Tend and Haul priorities and their stay-home toggle, and clicking changes them");
    }
}
