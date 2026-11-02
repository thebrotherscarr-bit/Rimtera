using System.Diagnostics;
using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// The late-game bench (0.8.2, the performance pass): a Small world set up the way a run looks near its end: a colony of
/// residents at the base, outposts with relays, armed people, a raid and a crowd of creatures in the air. It times the
/// ticks while that plays out, and the per-frame queries the HUD and travel UI make (the grid line, the pylon in reach,
/// the outposts), so a regression in the systems the colony grew late shows up as a number.
/// </summary>
public static class LateGameBench
{
    /// <summary>
    /// The late-game budgets: a tick, the worst tick (an hour crossed included), and one frame's worth of HUD queries. Set
    /// tighter than the engine's own (measured after the 0.8.2 fixes: about 1.2 ms, 12 ms and 0.05 ms), so a regression in
    /// the colony's late systems trips them; as with every bench row, only a value past twice its budget fails.
    /// </summary>
    public const double TickBudgetMs = 4, WorstTickBudgetMs = 30, FrameQueriesBudgetMs = 0.25;

    /// <summary>Base residents, outposts and people per outpost in the scenario.</summary>
    public const int BaseResidents = 16, OutpostCount = 4, OutpostResidents = 3, Creatures = 30;

    public sealed record Result(double TickMs, double WorstTickMs, double FrameQueriesMs, int Residents, int Relays, int Hostiles);

    /// <summary>Builds the late-game colony on <paramref name="game"/> (any generated world).</summary>
    public static void Build(GameSimulation game)
    {
        var spawn = game.SpawnColumn;
        Vector2 Ground(int x) => new(x + 0.5f, game.BiomeLayout.SurfaceAt(x) - 2f);
        TownNpc House(TownNpcKind kind, int x)
        {
            var npc = game.AddTownNpc(kind, Ground(x), bound: false);
            npc.HomeCell = new GridPoint(x, game.BiomeLayout.SurfaceAt(x) - 2);
            return npc;
        }

        var ai = game.TownNpcs.FirstOrDefault(npc => npc.Kind == TownNpcKind.StationAi) ?? game.AddTownNpc(TownNpcKind.StationAi, Ground(spawn), bound: false);
        ai.HomeCell = new GridPoint(spawn, game.BiomeLayout.SurfaceAt(spawn) - 2);
        var kinds = new[] { TownNpcKind.Settler, TownNpcKind.Armorer, TownNpcKind.Medic, TownNpcKind.Quartermaster };
        for (var i = 0; i < BaseResidents; i++)
        {
            var npc = House(kinds[i % kinds.Length], spawn - 30 + i * 4);
            if (i % 2 == 0)
            {
                game.Player.Inventory.Add(ItemId.ScrapPistol, 1);
                game.EquipResident(npc, ItemId.ScrapPistol);
            }
        }
        var pylons = new[] { TileId.ColonyPylon, TileId.FrostPylon, TileId.ScrapPylon, TileId.CanopyPylon, TileId.CavernPylon };
        game.PlaceTravelTileForTests(new GridPoint(spawn + 6, game.BiomeLayout.SurfaceAt(spawn + 6) - 1), pylons[0]);
        for (var o = 0; o < OutpostCount; o++)
        {
            var center = spawn + (o % 2 == 0 ? 1 : -1) * (150 + o / 2 * 200);
            center = Math.Clamp(center, 40, game.World.Width - 40);
            for (var r = 0; r < OutpostResidents; r++)
            {
                var npc = House(TownNpcKind.Settler, center - 4 + r * 4);
                if (r == 0)
                {
                    game.Player.Inventory.Add(ItemId.ScrapPistol, 1);
                    game.EquipResident(npc, ItemId.ScrapPistol);
                }
            }
            var cell = new GridPoint(center + 1, game.BiomeLayout.SurfaceAt(center + 1) - 1);
            game.PlaceTravelTileForTests(cell, pylons[1 + o % (pylons.Length - 1)]);
            game.SetRelayChargeForConsole(cell, GameSimulation.RelayCapacity);
        }
        game.SetTimeOfDay(22f);
        var console = new DevConsole(game);
        console.Execute("event start ScavengerRaid");
        console.Execute($"spawn mossslime {Creatures}");
    }

    /// <summary>Times the scenario: <paramref name="ticks"/> ticks, then the HUD's per-frame queries.</summary>
    public static Result Measure(GameSimulation game, int ticks = 1800)
    {
        for (var i = 0; i < 60; i++)
            game.Tick(new GameInput(new MovementInput(0f)));
        // Cross an hour inside the timed ticks: the storyteller, arrivals, outposts and relays run on the hour, and a hitch
        // there (the free-house search was 60 ms) is what a player feels.
        var hours = game.ElapsedGameHours;
        game.ProbeAdvanceHours(MathF.Floor(hours) + 1f - hours - 0.002f);
        GC.Collect(); // the setup's garbage (a second world generated, the scenario built) is not the game's
        GC.WaitForPendingFinalizers();
        var worst = 0.0;
        var all = Stopwatch.StartNew();
        for (var i = 0; i < ticks; i++)
        {
            var one = Stopwatch.StartNew();
            game.Player.Heal(game.Player.MaximumHitPoints);
            game.Tick(new GameInput(new MovementInput(i % 240 < 120 ? 1f : -1f)));
            worst = Math.Max(worst, one.Elapsed.TotalMilliseconds);
        }
        all.Stop();
        const int frames = 600;
        var queries = Stopwatch.StartNew();
        for (var i = 0; i < frames; i++)
        {
            _ = game.GridSummary;
            _ = game.PylonInRange;
            _ = game.Outposts.Count;
        }
        queries.Stop();
        return new Result(all.Elapsed.TotalMilliseconds / ticks, worst, queries.Elapsed.TotalMilliseconds / frames,
            game.TownNpcs.Count(npc => !npc.IsBound), game.Pylons.Count, game.Enemies.Count(enemy => enemy.IsAlive));
    }

    /// <summary>The bench's rows, in the engine bench's budget format.</summary>
    public static IEnumerable<(string Name, double Value, double Limit, string Unit)> Rows(Result result) =>
    [
        ("Late-game tick", result.TickMs, TickBudgetMs, "ms"),
        ("Late-game worst tick", result.WorstTickMs, WorstTickBudgetMs, "ms"),
        ("Late-game HUD queries a frame", result.FrameQueriesMs, FrameQueriesBudgetMs, "ms")
    ];
}
