using System.Numerics;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Outposts (0.4.2): residents whose homes are far from the base live at outposts, grouped by nearness; new arrivals settle
/// at the base first; people at an outpost guard and work there.
/// </summary>
public static class OutpostSelfTests
{
    private const int Floor = 40;

    private static GameSimulation Flat(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 400, height: 64) { FamilySpawnsEnabled = false };
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
        var game = Flat(9871);
        var ai = Living(game, TownNpcKind.StationAi, 60);
        var clerk = Living(game, TownNpcKind.Quartermaster, 75);
        var east = Living(game, TownNpcKind.Armorer, 220);
        var eastToo = Living(game, TownNpcKind.Medic, 240);
        var far = Living(game, TownNpcKind.Cook, 360);
        var outposts = game.Outposts;
        check(game.BaseCenter == ai.HomePosition && !game.AtOutpost(clerk) && game.AtOutpost(east) && outposts.Count == 2 &&
              outposts[0].People.Count == 2 && outposts[0].People.Contains(eastToo) && outposts[1].People.Single() == far &&
              game.OutpostOf(far)?.Number == 2 && game.OutpostOf(clerk) is null,
            $"homes more than {GameSimulation.OutpostDistance:0} tiles from the base (the station AI's home) are outposts, and near ones group together " +
            $"({string.Join("; ", outposts.Select(outpost => $"{outpost.Label}: {outpost.People.Count}"))})");

        game.Player.Inventory.Add(ItemId.ScrapPistol, 1);
        game.EquipResident(east, ItemId.ScrapPistol);
        var lines = new DevConsole(game).Execute("outposts");
        var card = game.Inspect(new InspectTarget(InspectKind.Resident, east.Id));
        check(game.Outposts[0].Guards == 1 && lines.Count == 3 && lines[1].Contains(east.Name) && lines[1].Contains("1 armed") &&
              card!.Lines.Any(line => line.Contains("(Outpost 1)")),
            "an outpost counts its armed guards; the console lists the outposts, and a resident's card says which they live at");

        // ---- arrivals settle at the base first ----
        var houses = Flat(9872);
        Living(houses, TownNpcKind.StationAi, 60);
        Build(houses, 300);
        Build(houses, 90);
        houses.Player.Motion.Position = new Vector2(300f, Floor - houses.Player.Motion.Size.Y - 0.01f); // the player stands by the far house
        houses.Flags.Set($"npc.{WorldFlags.Key(TownNpcKind.Quartermaster)}.unlocked");
        for (var hour = 0; hour < 4; hour++)
            houses.CheckTownArrivals();
        var arrived = houses.TownNpcs.FirstOrDefault(npc => npc.Kind == TownNpcKind.Quartermaster);
        check(arrived?.HomeCell is { } home && home.X < 150 && !houses.AtOutpost(arrived),
            $"a new arrival moves into the free house nearest the base, not the one by you (home {arrived?.HomeCell})");
    }

    /// <summary>A valid walled 14×10 room standing on the floor (door, light, table, chair), as the content checks build them.</summary>
    private static void Build(GameSimulation game, int x0)
    {
        var bottom = Floor - 1;
        var top = bottom - 9;
        for (var y = top; y <= bottom; y++)
        for (var x = x0; x <= x0 + 13; x++)
        {
            var shell = y == top || y == bottom || x == x0 || x == x0 + 13;
            game.World.SetTile(new GridPoint(x, y), shell ? TileId.Plate : TileId.Air);
            if (!shell)
                game.World.SetWall(new GridPoint(x, y), WallIds.PlatingGrey);
        }
        for (var y = bottom - 3; y <= bottom - 1; y++)
            game.World.SetTile(new GridPoint(x0, y), TileId.PlankDoor);
        game.World.SetTile(new GridPoint(x0 + 3, bottom - 1), TileId.WallTorch);
        game.World.SetTile(new GridPoint(x0 + 5, bottom - 1), TileId.PlankTable);
        game.World.SetTile(new GridPoint(x0 + 6, bottom - 1), TileId.PlankChair);
        game.Regions.ForceFullRebuild();
    }
}
