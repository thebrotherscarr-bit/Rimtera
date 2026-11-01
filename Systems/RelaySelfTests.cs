using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Pylons are relays (0.7.6): each holds its own charge and tier; online when staffed and charged; charged in person with
/// fuel crystals; travel costs charge at both ends; upkeep drains it and a dry relay goes offline with a letter; an online
/// relay powers the machines near it at its tier, while the base draws on the station AI; it is all saved.
/// </summary>
public static class RelaySelfTests
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

    private static void StandAt(GameSimulation game, float x) =>
        game.Player.Motion.Position = new Vector2(x, Floor - game.Player.Motion.Size.Y - 0.01f);

    public static void Run(Action<bool, string> check)
    {
        // ---- a relay: online when staffed and charged; charged in person; its tier is its best crystal ----
        var game = Flat(9801);
        Living(game, TownNpcKind.StationAi, 60);
        Living(game, TownNpcKind.Quartermaster, 66);
        Living(game, TownNpcKind.Settler, 296);
        Living(game, TownNpcKind.Settler, 304);
        var basePylon = new GridPoint(64, Floor - 1);
        var outpost = new GridPoint(300, Floor - 1);
        game.PlaceTravelTileForTests(basePylon, TileId.ColonyPylon);
        game.PlaceTravelTileForTests(outpost, TileId.FrostPylon);
        var start = game.RelayCharge(outpost);
        var online = game.RelayOnline(outpost);
        StandAt(game, 298f);
        game.Player.Inventory.Add(ItemId.FuelCrystalT2, 3);
        var charged = game.Interact(outpost).Message;
        check(start == GameSimulation.RelayStartCharge && online && game.RelayCharge(outpost) == start + 3 * 40 && game.RelayTier(outpost) == 2 &&
              charged.Contains("Charged the relay"),
            $"a relay starts with {GameSimulation.RelayStartCharge} charge and is online while staffed; charging it in person with fuel crystals fills it and sets its tier ('{charged}')");

        // ---- powered machines: near an online relay at its tier; the base draws on the station AI ----
        var near = new Vector2(306f, Floor - 1f);
        var wilds = new Vector2(200f, Floor - 1f);
        new DevConsole(game).Execute("power tier 1");
        var outpostForge = game.StationPoweredAt(CraftingStationId.PowerForgeMk2, near);
        var outpostAlloy = game.StationPoweredAt(CraftingStationId.AlloyAnvil, near);
        var baseForge = game.StationPoweredAt(CraftingStationId.PowerForgeMk2, new Vector2(70f, Floor - 1f));
        var baseBench = game.StationPoweredAt(CraftingStationId.Workbench, new Vector2(70f, Floor - 1f));
        var wild = game.StationPoweredAt(CraftingStationId.Workbench, wilds);
        check(outpostForge && !outpostAlloy && !baseForge && baseBench && !wild,
            "an online relay powers machines near it at its tier (the Mk2 forge, not the alloy anvil); the base draws on the station AI's tier; the wilds have no power");

        // ---- travel costs charge at both ends; too little and it is refused ----
        var before = (game.RelayCharge(basePylon), game.RelayCharge(outpost));
        StandAt(game, 63f);
        var jumped = game.TryPylonTeleport(outpost);
        var after = (game.RelayCharge(basePylon), game.RelayCharge(outpost));
        new DevConsole(game).Execute("relay set 5");
        StandAt(game, 63f);
        var refused = !game.TryPylonTeleport(outpost) && game.LastMessage.Contains("Not enough charge");
        check(jumped && after.Item1 == before.Item1 - GameSimulation.RelayTravelCost && after.Item2 == before.Item2 - GameSimulation.RelayTravelCost && refused,
            $"a trip costs {GameSimulation.RelayTravelCost} charge at both ends ({before} → {after}); without it travel is refused");

        // ---- upkeep: an online relay drains; dry, it goes offline with a letter, until charged again ----
        var drain = Flat(9802);
        Living(drain, TownNpcKind.Settler, 296);
        Living(drain, TownNpcKind.Settler, 304);
        drain.PlaceTravelTileForTests(outpost, TileId.FrostPylon);
        for (var hour = 0; hour < GameSimulation.RelayStartCharge + 2; hour++)
            drain.CheckTownArrivals();
        var dry = drain.RelayCharge(outpost) == 0 && !drain.RelayOnline(outpost) && drain.Letters.Any(letter => letter.Title.StartsWith("Relay offline"));
        StandAt(drain, 298f);
        drain.Player.Inventory.Add(ItemId.FuelCrystalT1, 2);
        drain.Interact(outpost);
        var back = drain.RelayOnline(outpost);
        var unstaffed = Flat(9803);
        unstaffed.PlaceTravelTileForTests(outpost, TileId.FrostPylon);
        check(dry && back && !unstaffed.RelayOnline(outpost) && unstaffed.RelayStatus(outpost).Contains("needs 2 people"),
            "an online relay burns its upkeep; dry, it goes offline with a letter until you charge it in person; unstaffed, it is offline");

        // ---- saved ----
        var saved = GameSimulation.Restore(game.CaptureSnapshot());
        check(saved.RelayCharge(outpost) == game.RelayCharge(outpost) && saved.RelayTier(outpost) == 2,
            "a relay's charge and tier are saved with the world");
    }
}
