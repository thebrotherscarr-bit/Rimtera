using System.Numerics;
using Rimtera.Entities;
using Rimtera.UI;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Raids with warning (0.4.3): the storyteller announces a raid ahead (size, target, side, time); it arrives later at its
/// target (the base or an outpost), not at the player; with nobody housed it comes for the player; the plan survives a save.
/// </summary>
public static class RaidSelfTests
{
    private const int Floor = 40;

    private static GameSimulation Flat(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 500, height: 64) { FamilySpawnsEnabled = false };
        for (var y = 1; y < game.World.Height - 1; y++)
        for (var x = 1; x < game.World.Width - 1; x++)
            game.World.SetTile(new GridPoint(x, y), y >= Floor ? TileId.Rock : TileId.Air);
        game.SetTimeOfDay(10f);
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e6f);
        return game;
    }

    private static TownNpc Living(GameSimulation game, TownNpcKind kind, int x)
    {
        var npc = game.AddTownNpc(kind, new Vector2(x, Floor - 3f), bound: false);
        npc.Motion.Position = new Vector2(x, Floor - npc.Motion.Size.Y - 0.01f);
        npc.HomeCell = new GridPoint(x, Floor - 2);
        return npc;
    }

    public static void Run(Action<bool, string> check)
    {
        // ---- a raid is announced first: target, side, size, time; nothing arrives yet ----
        var game = Flat(9881);
        var ai = Living(game, TownNpcKind.StationAi, 100);
        Living(game, TownNpcKind.Quartermaster, 110);
        game.Player.Motion.Position = new Vector2(400f, Floor - game.Player.Motion.Size.Y - 0.01f); // far from the base
        var before = game.Enemies.Count;
        var count = game.PlanRaid(200f, out var warning, warningHours: 0.05f);
        var raid = game.PendingRaid;
        var hud = HudStatusLine.Build(game);
        var twice = game.PlanRaid(200f, out _);
        check(count >= 2 && raid is { TargetName: "the base" } && raid.Value.Target == game.BaseCenter && warning.Contains("the base") &&
              warning.Contains(raid.Value.SideName) && game.Enemies.Count == before && hud.Contains("RAID") && twice == 0,
            $"a raid is announced before it comes: how many, where (the base) and from which side, with a countdown on the HUD; one raid at a time ('{warning}')");

        // ---- the plan survives a save; then it arrives at its target, not at you ----
        var reloaded = GameSimulation.Restore(game.CaptureSnapshot());
        var kept = reloaded.PendingRaid is { } again && again.Count == count && again.Target == raid!.Value.Target;
        var arrived = false;
        var hit = string.Empty;
        for (var i = 0; i < 20000 && !arrived; i++)
        {
            var last = game.LastMessage;
            game.Player.Heal(1e6f);
            game.Tick(new GameInput(new MovementInput(0f)));
            if (game.LastMessage != last && game.LastMessage.StartsWith("Raid!"))
            {
                arrived = true;
                hit = game.LastMessage;
            }
        }
        var raiders = game.Enemies.Where(enemy => enemy.IsAlive && enemy.Motion.Position.X < 250f).ToArray();
        check(kept && arrived && game.PendingRaid is null && raiders.Length >= 2 &&
              raiders.All(enemy => MathF.Abs(enemy.Motion.Position.X - ai.HomePosition.X) < 60f),
            $"the raid plan is saved with the world; when its hour comes it arrives at the base, not at you ({hit}; {raiders.Length} near the base)");

        // ---- an outpost can be the target ----
        var outpostGame = Flat(9882);
        Living(outpostGame, TownNpcKind.StationAi, 60);
        foreach (var x in new[] { 300, 306, 312, 318, 324, 330 })
            Living(outpostGame, TownNpcKind.Settler, x);
        var targets = new HashSet<string>();
        for (var attempt = 0; attempt < 12 && targets.Count < 2; attempt++)
        {
            outpostGame.PlanRaid(200f, out _);
            targets.Add(outpostGame.PendingRaid!.Value.TargetName);
            outpostGame.Flags.Clear("raid.due");
        }
        check(targets.Contains("Outpost 1"),
            $"raids go for the outposts too, the more people there the likelier (targets seen: {string.Join(", ", targets)})");

        // ---- with nobody housed, a raid comes for you ----
        var alone = Flat(9883);
        alone.Player.Motion.Position = new Vector2(250f, Floor - alone.Player.Motion.Size.Y - 0.01f);
        alone.PlanRaid(200f, out var aloneWarning);
        check(alone.PendingRaid is { TargetName: "you" } && aloneWarning.Contains("you"),
            $"with nobody housed yet, a raid comes for you ('{aloneWarning}')");

        // ---- 0.4.4 balance: residents are Terraria's town NPCs (250 life, 15 defense); raids answer the colony's guards ----
        int RaidSize(int guards)
        {
            var world = Flat(9885);
            Living(world, TownNpcKind.StationAi, 60);
            for (var i = 0; i < guards; i++)
            {
                var guard = Living(world, TownNpcKind.Settler, 70 + i * 3);
                world.Player.Inventory.Add(ItemId.ScrapPistol, 1);
                world.EquipResident(guard, ItemId.ScrapPistol);
            }
            return world.PlanRaid(135f, out _);
        }
        var resident = Flat(9886).AddTownNpc(TownNpcKind.Medic, new Vector2(30f, Floor - 3f), bound: false);
        var (unguarded, guarded) = (RaidSize(0), RaidSize(6));
        check(resident.MaximumHitPoints == 250f && TownNpc.BaseDefense == 15 && guarded == unguarded + 3 && RaidSize(40) == GameSimulation.MaxRaiders,
            $"residents have Terraria's town-NPC 250 life and 15 defense; a raid brings one more for every two armed residents, up to {GameSimulation.MaxRaiders} ({unguarded} → {guarded} with six guards)");

        // ---- the console shows and sends raids ----
        var console = new DevConsole(Flat(9884));
        var none = console.Execute("raid")[1];
        var warned = console.Execute("raid warn 1")[1];
        var shown = console.Execute("raid")[1];
        check(none.StartsWith("No raid") && warned.Contains("coming for") && shown.Contains("in 1.0 hours"),
            "the console's raid shows the raid on its way and raid warn announces one");
    }
}
