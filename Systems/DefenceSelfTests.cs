using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Outpost defences (0.7.3): a sentry set down by an armed resident's home goes to the colony (no slot of yours, saved) and
/// fires only while someone armed is near to man it; elsewhere it stays yours; a dart trap near an armed resident fires at
/// hostiles in its line, and stays quiet with nobody to man it.
/// </summary>
public static class DefenceSelfTests
{
    private const int Floor = 40;

    private static GameSimulation Flat(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 300, height: 64) { FamilySpawnsEnabled = false };
        for (var y = 1; y < game.World.Height - 1; y++)
        for (var x = 1; x < game.World.Width - 1; x++)
            game.World.SetTile(new GridPoint(x, y), y >= Floor ? TileId.Rock : TileId.Air);
        game.SetTimeOfDay(10f);
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e6f);
        return game;
    }

    private static TownNpc Guard(GameSimulation game, int x, bool armed = true)
    {
        var npc = game.AddTownNpc(TownNpcKind.Armorer, new Vector2(x, Floor - 3f), bound: false);
        npc.Motion.Position = new Vector2(x, Floor - npc.Motion.Size.Y - 0.01f);
        npc.HomeCell = new GridPoint(x, Floor - 2);
        npc.SetStory(Rimtera.Characters.Backgrounds.Childhoods[0], Rimtera.Characters.Backgrounds.Adulthoods.First(story => story.Id == "soldier"));
        if (armed)
        {
            game.Player.Inventory.Add(ItemId.ScrapPistol, 1);
            game.EquipResident(npc, ItemId.ScrapPistol);
        }
        return npc;
    }

    private static void Tick(GameSimulation game, int ticks, GameInput? input = null)
    {
        for (var i = 0; i < ticks; i++)
        {
            game.Player.Heal(1e6f);
            game.Tick(input ?? new GameInput(new MovementInput(0f)));
        }
    }

    private static string Deploy(GameSimulation game, ItemId remote, int x)
    {
        game.Player.Motion.Position = new Vector2(x - 4f, Floor - game.Player.Motion.Size.Y - 0.01f);
        game.Player.Inventory.Add(remote, 1);
        game.TrySelectInventorySlot(game.Player.Inventory.FindSlot(remote));
        Tick(game, 2); // let the hour's story (an act's announcement) pass first
        Tick(game, 1, new GameInput(new MovementInput(0f), PlayerAction.UseSelected, new GridPoint(x, Floor - 2)));
        var message = game.LastMessage;
        Tick(game, 40);
        return message;
    }

    public static void Run(Action<bool, string> check)
    {
        var remote = new GameSimulation(seed: 1, width: 80, height: 48).Items.All
            .First(item => item.Summons is { } kind && MinionCatalog.Get(kind).IsSentry).Id;

        // ---- a sentry set down by an armed resident's home goes to the colony; elsewhere it stays yours ----
        var game = Flat(9991);
        var guard = Guard(game, 100);
        var deployed = Deploy(game, remote, 104);
        var handed = game.ColonySentries.Count == 1 && game.Minions.All(minion => !minion.IsSentry) && deployed.Contains(guard.Name);
        Deploy(game, remote, 220);
        var kept = game.Minions.Count(minion => minion.IsSentry) == 1 && game.ColonySentries.Count == 1;
        var saved = GameSimulation.Restore(game.CaptureSnapshot());
        check(handed && kept && saved.ColonySentries.Count == 1 && saved.Minions.Count == 0,
            $"a sentry set down by an armed resident's home goes to the colony ('{deployed}'); far from anyone armed it stays yours; the colony's is saved, yours is not");

        // ---- a colony sentry fires while manned, and not without someone armed near ----
        var slime = game.SpawnCreatureAt(EnemyKind.MossSlime, new Vector2(108f, Floor - 1.2f));
        var health = slime.HitPoints;
        Tick(game, 120);
        var hit = !slime.IsAlive || slime.HitPoints < health;
        var quiet = Flat(9992);
        var unarmed = Guard(quiet, 100);
        Deploy(quiet, remote, 104);
        quiet.UnequipResident(unarmed);
        var other = quiet.SpawnCreatureAt(EnemyKind.MossSlime, new Vector2(108f, Floor - 1.2f));
        var otherHealth = other.HitPoints;
        quiet.Player.Motion.Position = new Vector2(250f, Floor - quiet.Player.Motion.Size.Y - 0.01f);
        Tick(quiet, 120);
        check(hit && quiet.ColonySentries.Count == 1 && other.IsAlive && other.HitPoints >= otherHealth,
            $"a colony sentry fires while an armed resident mans it (slime {(slime.IsAlive ? $"{slime.HitPoints:0}/{health:0}" : "dead")}), and falls silent with nobody armed near");

        // ---- a dart trap near an armed resident fires down its line at hostiles; unmanned, it waits ----
        int TrapHits(bool armed)
        {
            var world = Flat(armed ? 9993 : 9994);
            var keeper = Guard(world, 100, armed);
            world.World.SetTile(new GridPoint(106, Floor - 1), TileId.DartTrap);
            world.Player.Motion.Position = new Vector2(30f, Floor - world.Player.Motion.Size.Y - 0.01f);
            var target = world.SpawnCreatureAt(EnemyKind.MossSlime, new Vector2(116f, Floor - 1.2f));
            var before = target.HitPoints;
            Tick(world, 90);
            return keeper.HitPoints < keeper.MaximumHitPoints ? -1 : (int)MathF.Round(before - (target.IsAlive ? target.HitPoints : 0f));
        }
        var manned = TrapHits(true);
        var unmanned = TrapHits(false);
        check(manned > 0 && unmanned == 0,
            $"a dart trap near an armed resident fires at hostiles in its line ({manned} damage), sparing your people; with nobody armed near it waits");
    }
}
