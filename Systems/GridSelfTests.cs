using System.Numerics;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Grid feedback and balance (0.7.9): a relay running low writes a warning, once; the grid's state is on the HUD line,
/// in History and on the console; the charge numbers keep a relay useful between visits.
/// </summary>
public static class GridSelfTests
{
    private const int Floor = 40;

    public static void Run(Action<bool, string> check)
    {
        var game = new GameSimulation(seed: 9831, width: 500, height: 64) { FamilySpawnsEnabled = false };
        for (var y = 1; y < game.World.Height - 1; y++)
        for (var x = 1; x < game.World.Width - 1; x++)
            game.World.SetTile(new GridPoint(x, y), y >= Floor ? TileId.Rock : TileId.Air);
        foreach (var x in new[] { 296, 304 })
        {
            var npc = game.AddTownNpc(TownNpcKind.Settler, new Vector2(x, Floor - 3f), bound: false);
            npc.HomeCell = new GridPoint(x, Floor - 2);
        }
        var relay = new GridPoint(300, Floor - 1);
        var noRelays = game.GridSummary;
        game.PlaceTravelTileForTests(relay, TileId.FrostPylon);
        game.Player.Motion.Position = new Vector2(299f, Floor - game.Player.Motion.Size.Y - 0.01f);

        // ---- a relay running low warns once ----
        new DevConsole(game).Execute($"relay set {GameSimulation.RelayLowCharge + 2}");
        for (var hour = 0; hour < 6; hour++)
            game.CheckTownArrivals();
        var warnings = game.Letters.Count(letter => letter.Title.StartsWith("Relay low"));
        var summary = game.GridSummary;
        check(noRelays.Contains("no relays") && warnings == 1 && summary.Contains("relays 1/1 online") && summary.Contains("1 low"),
            $"a relay running low writes one warning letter, and the grid line shows it ('{summary}')");

        // ---- History and the console show the grid ----
        var history = game.History().Select(line => line.Text).ToArray();
        var console = new DevConsole(game).Execute("grid");
        check(history.Contains("Power grid") && history.Any(line => line.StartsWith("Station AI: power")) &&
              history.Any(line => line.Contains("Frost pylon: online")) && console.Any(line => line.StartsWith("Grid:")),
            "History has a power grid section (the station AI and each relay) and the console's grid sums it up");

        // ---- balance: a full relay lasts days between visits; a trip is cheap; a crystal pays for a round trip ----
        var fullDays = GameSimulation.RelayCapacity / (float)GameSimulation.RelayUpkeepPerHour / 24f;
        var roundTrip = 2 * GameSimulation.RelayTravelCost;
        var t1 = GameSimulation.PowerFuels.First(fuel => fuel.Tier == 1).Energy;
        check(fullDays >= 10f && roundTrip <= GameSimulation.RelayCapacity / 10 && t1 >= roundTrip &&
              GameSimulation.RelayStartCharge >= 2 * roundTrip,
            $"a full relay lasts {fullDays:0.#} days of upkeep; a round trip costs {roundTrip} ({roundTrip * 100 / GameSimulation.RelayCapacity}% of a relay); one tier 1 crystal ({t1}) pays for it; a new relay holds a couple of trips");
    }
}
