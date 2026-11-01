using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Purposeful events (0.6.5): each act's key event pushes toward the next step, once, after the act has gone on a while:
/// building supplies at Landfall, the core's signal in the Descent, the gate boss's summon at the Core, the cult's ritual
/// site in the Zealots' act; it is a letter, it is saved, and the console fires it.
/// </summary>
public static class PurposeSelfTests
{
    private static GameSimulation World(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 160, height: 80) { FamilySpawnsEnabled = false };
        game.SetTimeOfDay(10f);
        return game;
    }

    public static void Run(Action<bool, string> check)
    {
        // ---- Landfall: not at once; after a while the storyteller sends a survivor's call and building supplies, once ----
        var game = World(9951);
        var early = game.ForceIncident("purpose");
        game.ProbeAdvanceHours(GameSimulation.PurposeAfterHours + 1f);
        var wood = game.Player.Inventory.Count(ItemId.WoodBlock);
        var sent = game.ForceIncident("purpose");
        var gained = game.Player.Inventory.Count(ItemId.WoodBlock) - wood;
        var letter = game.Letters.LastOrDefault();
        var again = game.ForceIncident("purpose");
        var saved = GameSimulation.Restore(game.CaptureSnapshot());
        check(early is null && sent is not null && gained == 60 && letter is { Kind: LetterKind.Good, Title: "A survivor's call" } &&
              again is null && saved.PurposeSent(RunAct.Landfall),
            $"the act's key event waits {GameSimulation.PurposeAfterHours:0} hours into it, then comes once: Landfall's survivor's call brings building supplies ('{letter?.Title}'); saved");

        // ---- the Descent: a signal from the core, how far down, and supplies for the dig ----
        var descent = World(9952);
        var settler = descent.AddTownNpc(TownNpcKind.Settler, descent.Player.Motion.Position, bound: false);
        settler.HomeCell = new GridPoint((int)settler.Motion.Position.X, (int)settler.Motion.Position.Y);
        var signal = descent.SendPurpose();
        check(descent.Act == RunAct.Descent && signal is not null && signal.Contains("tiles below") &&
              descent.Letters.Any(entry => entry.Title == "A signal from the core") && descent.Player.Inventory.Count(ItemId.RopeCoil) >= 2,
            $"the Descent's key event is a signal from the core: how far down it lies, and supplies for the dig");

        // ---- the Core: the summon for the gate boss; the Zealots: where the ritual waits ----
        var core = World(9953);
        core.Flags.Set("core.reached");
        var summon = core.Items.All.First(item => item.SummonsBoss == GameSimulation.CoreGateBoss).Id;
        var gate = core.SendPurpose();
        var zealots = World(9954);
        zealots.Flags.Set("ritual.ready");
        var defector = zealots.SendPurpose();
        check(core.Act == RunAct.Core && gate is not null && core.Player.Inventory.Count(summon) == 1 &&
              zealots.Act == RunAct.Zealots && defector is not null && defector.Contains("dungeon gate") &&
              (defector.Contains("tiles") || defector.Contains("right here")),
            $"the Core's key event is the gate boss's summon ({core.Items[summon].Name}); the Zealots' is where their ritual waits");

        // ---- an act's clock starts when it begins; the console fires the key event ----
        var clock = World(9955);
        clock.ProbeAdvanceHours(30f);
        clock.Tick(new GameInput(new MovementInput(0f)));
        var console = new DevConsole(clock);
        var fired = console.Execute("incident purpose");
        check(clock.Storyteller.Incidents.Any(incident => incident.Id == "purpose") && fired.Any(line => line.Contains("survivor")),
            "the storyteller holds the act's key event, and the console's incident purpose fires it");
    }
}
