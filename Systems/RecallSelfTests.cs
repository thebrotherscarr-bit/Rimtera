using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Pylons and recall (0.7.2): recall items take you home (your bed, else the base, else the landing site); a pylon needs a
/// staffed home around it (the base or an outpost; prisoners do not staff it), and names where it stands.
/// </summary>
public static class RecallSelfTests
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
        // ---- recall: to the base when there is one (no bed), by the capsule or the beacon ----
        var game = Flat(9981);
        Living(game, TownNpcKind.StationAi, 100);
        game.Player.Motion.Position = new Vector2(400f, Floor - game.Player.Motion.Size.Y - 0.01f);
        game.Player.Inventory.Add(ItemId.RecallCapsule, 1);
        game.TrySelectInventorySlot(game.Player.Inventory.FindSlot(ItemId.RecallCapsule));
        game.Tick(new GameInput(new MovementInput(0f), Action: PlayerAction.UseSelected, Target: new GridPoint(1, 1)));
        var home = MathF.Abs(game.Player.Motion.Position.X - 100f) < 3f && game.Player.Inventory.Count(ItemId.RecallCapsule) == 0 &&
                   game.LastMessage.Contains("the base");
        var bare = Flat(9982);
        bare.Player.Motion.Position = new Vector2(400f, Floor - bare.Player.Motion.Size.Y - 0.01f);
        var where = bare.RecallHome();
        check(home && where == "the landing site",
            $"recall takes you home: to the base (x {game.Player.Motion.Position.X:0}, '{game.LastMessage}'), or the landing site with no base or bed");

        // ---- pylons: a staffed home around them; prisoners do not count; they name where they stand ----
        var travel = Flat(9983);
        travel.PlaceTravelTileForTests(new GridPoint(100, Floor - 1), TileId.ColonyPylon);
        travel.PlaceTravelTileForTests(new GridPoint(300, Floor - 1), TileId.FrostPylon);
        travel.PlaceTravelTileForTests(new GridPoint(420, Floor - 1), TileId.ScrapPylon);
        Living(travel, TownNpcKind.StationAi, 96);
        Living(travel, TownNpcKind.Quartermaster, 104);
        Living(travel, TownNpcKind.Settler, 296);
        Living(travel, TownNpcKind.Settler, 304);
        var held = Living(travel, TownNpcKind.Settler, 416);
        held.IsPrisoner = true;
        Living(travel, TownNpcKind.Settler, 424);
        var pylons = travel.Pylons.ToDictionary(pylon => pylon.Cell.X);
        var problem = travel.PylonPlacementProblem(TileId.CavernPylon, new GridPoint(200, Floor - 1), checkBiome: false);
        check(pylons[100].Active && travel.PylonPlace(pylons[100]) == "the base" &&
              pylons[300].Active && travel.PylonPlace(pylons[300]) == "Outpost 1" &&
              !pylons[420].Active && problem is not null && problem.Contains("staffed"),
            $"a pylon needs a staffed home (the base, or an outpost like Outpost 1); a prisoner does not staff one ('{problem}')");
    }
}
