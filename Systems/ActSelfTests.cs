using System.Numerics;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Acts (0.6.2): the run's chapters follow progress, never go back, are announced, raise threats and open incidents.</summary>
public static class ActSelfTests
{
    public static void Run(Action<bool, string> check)
    {
        var game = new GameSimulation(seed: 9911, width: 120, height: 56) { FamilySpawnsEnabled = false };
        var start = game.Act;
        var settler = game.AddTownNpc(TownNpcKind.Settler, new Vector2(30f, 20f), bound: false);
        settler.HomeCell = new GridPoint(30, 21);
        var descent = game.Act;
        game.Flags.Set("core.reached");
        var core = game.Act;
        game.TriggerFracture();
        var fracture = game.Act;
        game.Flags.Set("ritual.ready");
        var zealots = game.Act;
        game.Flags.Set("obelisks.down");
        var collapse = game.Act;
        game.Flags.Set("boss.astro_god.down");
        var launch = game.Act;
        check(start == RunAct.Landfall && descent == RunAct.Descent && core == RunAct.Core && fracture == RunAct.Fracture &&
              zealots == RunAct.Zealots && collapse == RunAct.Collapse && launch == RunAct.Launch,
            "the run moves through its seven acts with progress: Landfall, Descent (someone settles), the Core, the Fracture, the Zealots, Collapse, Launch");

        // ---- an act is announced once and never goes back ----
        var announced = new GameSimulation(seed: 9912, width: 120, height: 56) { FamilySpawnsEnabled = false };
        var person = announced.AddTownNpc(TownNpcKind.Settler, new Vector2(30f, 20f), bound: false);
        person.HomeCell = new GridPoint(30, 21);
        var messages = new List<string>();
        for (var i = 0; i < 60 * 80; i++)
        {
            var last = announced.LastMessage;
            announced.Tick(new GameInput(new Rimtera.Entities.MovementInput(0f)));
            if (announced.LastMessage != last)
                messages.Add(announced.LastMessage);
        }
        announced.SendAway(person);
        check(messages.Count(message => message.StartsWith("Act 2: Descent")) == 1 && announced.Act == RunAct.Descent &&
              new DevConsole(announced).Execute("act")[1].StartsWith("Act 2"),
            "a new act is announced once, with what it asks of you, and never goes back (the settler leaving keeps the Descent)");

        // ---- acts raise threats and open incidents ----
        var landfall = Storyteller.ComputeThreatPoints(10, 400f, 3, 2, act: 1);
        var later = Storyteller.ComputeThreatPoints(10, 400f, 3, 2, act: 4);
        var context = new StoryContext(10, 12f, BiomeZone.Grassland, 3, 2, 400f, false, true, 1f);
        var teller = new Storyteller(1);
        check(later > landfall && !new SolarFlareIncident().CanFire(context with { Act = 2 }, teller) &&
              new SolarFlareIncident().CanFire(context with { Act = 3 }, teller) &&
              !new ToxicFalloutIncident().CanFire(context, teller) && new ToxicFalloutIncident().CanFire(context with { Act = 2 }, teller),
            $"each act raises the threat floor ({landfall:0} → {later:0} by the Fracture) and opens incidents: fallout from the Descent, solar flares from the Core");
    }
}
