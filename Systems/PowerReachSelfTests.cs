using System.Numerics;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// The station AI's power reaches outposts (0.7.4): a machine runs at the base or at a staffed outpost, not in the wilds;
/// an outpost's machines go dark first when the reserve runs low, and draw a little more for the long line out; until the
/// AI has a home there is no grid, and machines run wherever they stand.
/// </summary>
public static class PowerReachSelfTests
{
    private const int Floor = 40;

    private static GameSimulation Flat(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 500, height: 64) { FamilySpawnsEnabled = false };
        for (var y = 1; y < game.World.Height - 1; y++)
        for (var x = 1; x < game.World.Width - 1; x++)
            game.World.SetTile(new GridPoint(x, y), y >= Floor ? TileId.Rock : TileId.Air);
        game.SetTimeOfDay(10f);
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
        var game = Flat(9995);
        var circuit = game.Crafting.FindByOutput(ItemId.CircuitBoard).First();
        var gridless = game.PowerLock(circuit, new Vector2(400f, Floor - 1f)) is null;
        Living(game, TownNpcKind.StationAi, 60);
        Living(game, TownNpcKind.Settler, 300);
        game.SetStationPower(500, 5);
        Vector2 At(float x) => new(x, Floor - 1f);
        var atBase = game.PowerLock(circuit, At(70f));
        var atOutpost = game.PowerLock(circuit, At(305f));
        var inWilds = game.PowerLock(circuit, At(200f));
        var line = game.PowerCostAt(circuit, At(305f)) - game.PowerCostAt(circuit, At(70f));
        check(gridless && atBase is null && atOutpost is null && inWilds is { } far && far.Contains("beyond the station AI's reach") &&
              line == GameSimulation.OutpostLinePower,
            $"a machine runs at the base and at a staffed outpost (a little dearer out there), not in the wilds ('{inWilds}'); with no AI home yet, anywhere");

        // ---- low power: the outposts go dark first ----
        game.SetStationPower(GameSimulation.OutpostMinimumPower - 10);
        var lowBase = game.PowerLock(circuit, At(70f));
        var lowOutpost = game.PowerLock(circuit, At(305f));
        var history = game.History().Select(entry => entry.Text).ToArray();
        check(lowBase is null && lowOutpost is { } dim && dim.Contains("too low to reach the outposts") &&
              history.Any(entry => entry.StartsWith("Outpost 1") && entry.EndsWith("machines dark")),
            $"when the reserve runs low (below {GameSimulation.OutpostMinimumPower}) the outposts go dark first ('{lowOutpost}'), and the History tab says so");

        // ---- an outpost is staffed by its people: an empty one has no power ----
        var empty = Flat(9996);
        Living(empty, TownNpcKind.StationAi, 60);
        empty.SetStationPower(500, 5);
        check(empty.PowerReachAt(new Vector2(305f, Floor - 1f)) == GameSimulation.PowerReach.None,
            "where nobody lives there is no outpost for the power to reach");
    }
}
