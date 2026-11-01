using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// The History tab (0.6.6): the run's story so far: where it stands (day, act, people, losses, bosses, raids, the
/// storyteller), the acts it passed through and when, and the chronicle newest first; its counts are saved; the inventory
/// has a History tab.
/// </summary>
public static class HistorySelfTests
{
    private static GameSimulation World(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 160, height: 80) { FamilySpawnsEnabled = false };
        game.SetTimeOfDay(10f);
        return game;
    }

    private static void NextHour(GameSimulation game)
    {
        game.ProbeAdvanceHours(1f);
        game.Player.Heal(1e6f);
        game.Tick(new GameInput(new MovementInput(0f)));
    }

    public static void Run(Action<bool, string> check)
    {
        // ---- a fresh run: where it stands, Landfall from day 1, an empty chronicle ----
        var game = World(9961);
        var fresh = game.History().Select(line => line.Text).ToArray();
        check(game.History()[0].Heading && fresh.Any(line => line.StartsWith("Day 1 · Act 1: Landfall")) &&
              fresh.Contains("Act 1: Landfall · began on day 1") && fresh.Any(line => line.Contains("0 lost")) &&
              fresh.Any(line => line.StartsWith("Storyteller: Steady")) && game.History().Count(line => line.Heading) == 4,
            $"a fresh run's history: where it stands, Landfall from day 1, and its chronicle ({fresh.Length} lines)");

        // ---- deaths and acts are kept; the chronicle reads newest first ----
        var settler = game.AddTownNpc(TownNpcKind.Settler, game.Player.Motion.Position, bound: false);
        settler.HomeCell = new GridPoint((int)settler.Motion.Position.X, (int)settler.Motion.Position.Y);
        NextHour(game);
        var descentDay = game.ActBeganDay(RunAct.Descent);
        var other = game.AddTownNpc(TownNpcKind.Settler, game.Player.Motion.Position, bound: false);
        game.HurtTownNpc(other, 1e6f);
        game.Record("The last thing that happened.");
        var lines = game.History().Select(line => line.Text).ToList();
        var chronicleStart = lines.IndexOf("Chronicle, newest first") + 1;
        check(game.Act == RunAct.Descent && descentDay == game.Day && lines.Contains($"Act 2: Descent · began on day {descentDay}") &&
              game.Deaths == 1 && lines.Any(line => line.Contains("1 lost")) && lines[chronicleStart].EndsWith("The last thing that happened."),
            $"the acts a run passed through are kept with their day, the dead are counted, and the chronicle reads newest first");

        // ---- raids weathered; the counts are saved ----
        var raided = World(9962);
        raided.Flags.Increment("history.raids");
        var saved = GameSimulation.Restore(game.CaptureSnapshot());
        check(raided.RaidsWeathered == 1 && raided.History().Any(line => line.Text.StartsWith("Raids weathered: 1")) &&
              saved.Deaths == 1 && saved.ActBeganDay(RunAct.Descent) == descentDay,
            "raids weathered are counted; the counts and the acts' days are saved with the world");

        // ---- the inventory has a History tab ----
        var hub = new Rimtera.UI.InventoryUiState(game.Player.Inventory, game: game);
        var opened = false;
        hub.HistoryRequested += () => opened = true;
        hub.DescendantsAndSelf().OfType<Rimtera.UI.UiButtonElement>().FirstOrDefault(button => button.Id == "tab-history")?.Command?.Invoke();
        var loadout = hub.DescendantsAndSelf().OfType<Rimtera.UI.UiButtonElement>().Any(button => button.Id == "loadout-1");
        check(opened && loadout, "the inventory has a History tab beside Research (the loadouts move down a row)");
    }
}
