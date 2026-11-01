using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Raiders hit relays (0.7.8): hostiles near a relay wear its integrity down; at nothing it is destroyed, with a letter, and
/// what it powered goes dark; charging a damaged relay repairs it first; a raid on an outpost aims at its relay.
/// </summary>
public static class RelayRaidSelfTests
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
        game.Player.Motion.Position = new Vector2(30f, Floor - game.Player.Motion.Size.Y - 0.01f);
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
        var relay = new GridPoint(300, Floor - 1);

        // ---- a hostile at a relay wears it down; at nothing it is destroyed, with a letter ----
        var game = Flat(9821);
        Living(game, TownNpcKind.StationAi, 60);
        game.PlaceTravelTileForTests(relay, TileId.FrostPylon);
        game.Player.Motion.Position = new Vector2(285f, Floor - game.Player.Motion.Size.Y - 0.01f); // near, so it stays spawned
        var slime = game.SpawnCreatureAt(EnemyKind.MossSlime, new Vector2(300.5f, Floor - 1.2f));
        var hold = slime.Motion.Position;
        var first = -1;
        for (var i = 0; i < 60 * 600 && game.World.GetCell(relay).TileId == TileId.FrostPylon; i++)
        {
            slime.Motion.Position = hold;
            game.Player.Heal(1e6f);
            game.Tick(new GameInput(new MovementInput(0f)));
            if (first < 0 && game.RelayIntegrity(relay) < GameSimulation.RelayMaxIntegrity)
                first = game.RelayIntegrity(relay);
        }
        var gone = game.World.GetCell(relay).TileId == TileId.Air && game.Pylons.All(pylon => pylon.Cell != relay) &&
                   game.Letters.Any(letter => letter.Title.StartsWith("Relay destroyed")) &&
                   game.History().Any(line => line.Text.Contains("relays lost: 1"));
        check(first is >= 0 and < GameSimulation.RelayMaxIntegrity && gone,
            $"a hostile at a relay wears it down ({GameSimulation.RelayMaxIntegrity} → {first} after its first hit); worn to nothing it is destroyed, with a letter, and History counts it");

        // ---- charging a damaged relay repairs it first ----
        var repair = Flat(9822);
        Living(repair, TownNpcKind.Settler, 296);
        Living(repair, TownNpcKind.Settler, 304);
        repair.PlaceTravelTileForTests(relay, TileId.FrostPylon);
        var bruiser = repair.SpawnCreatureAt(EnemyKind.MossSlime, new Vector2(300.5f, Floor - 1.2f));
        var spot = bruiser.Motion.Position;
        for (var i = 0; i < 60 * 3; i++)
        {
            bruiser.Motion.Position = spot;
            repair.Player.Heal(1e6f);
            repair.Tick(new GameInput(new MovementInput(0f)));
        }
        bruiser.TakeDamage(1e6f);
        var damaged = repair.RelayIntegrity(relay);
        var chargeBefore = repair.RelayCharge(relay);
        repair.Player.Motion.Position = new Vector2(298f, Floor - repair.Player.Motion.Size.Y - 0.01f);
        repair.Player.Inventory.Add(ItemId.FuelCrystalT1, 20);
        repair.Interact(relay);
        check(damaged < GameSimulation.RelayMaxIntegrity && repair.RelayIntegrity(relay) == GameSimulation.RelayMaxIntegrity &&
              repair.RelayCharge(relay) > chargeBefore && repair.RelayStatus(relay).Contains("online"),
            $"charging a damaged relay repairs it first (integrity {damaged} → {repair.RelayIntegrity(relay)}), then charges it");

        // ---- a raid on an outpost aims at its relay ----
        var raided = Flat(9823);
        Living(raided, TownNpcKind.StationAi, 60);
        foreach (var x in new[] { 290, 296, 304, 310, 316, 320 })
            Living(raided, TownNpcKind.Settler, x);
        raided.PlaceTravelTileForTests(relay, TileId.FrostPylon);
        var aimed = false;
        for (var attempt = 0; attempt < 12 && !aimed; attempt++)
        {
            raided.PlanRaid(200f, out _);
            aimed = raided.PendingRaid is { TargetName: "Outpost 1" } plan && MathF.Abs(plan.Target.X - 300.5f) < 0.01f;
            raided.Flags.Clear("raid.due");
        }
        check(aimed, "a raid on an outpost aims at its relay");
    }
}
