using Rimtera.Entities;

namespace Rimtera.Systems;

/// <summary>
/// The Fracture as hardmode (0.8.1): the pacing model puts the run in 10–14 hours with a shorter second ladder; the second
/// ladder's levers hold: any two of the hard trio open the bulbs, post-Fracture research is shorter than before, the
/// obelisks need fewer kills; the console shows the model.
/// </summary>
public static class PacingSelfTests
{
    public static void Run(Action<bool, string> check)
    {
        var (before, after, total) = RunPacing.Hours();
        check(total >= RunPacing.TargetLowHours && total <= RunPacing.TargetHighHours && after < before * 0.9f,
            $"the pacing model puts the run at {total:0.0} h (target {RunPacing.TargetLowHours:0}–{RunPacing.TargetHighHours:0}), the second ladder ({after:0.0} h) shorter than the first ({before:0.0} h)");

        var game = new GameSimulation(seed: 9841, width: 80, height: 48);
        var console = new DevConsole(game);
        console.Execute($"defeat {GameSimulation.CoreGateBoss}"); // the Fracture first: the trio only counts after it
        console.Execute($"defeat {GameSimulation.HardTrio[0]}");
        var one = game.HardTrioDefeated;
        console.Execute($"defeat {GameSimulation.HardTrio[1]}");
        var two = game.HardTrioDefeated;
        var tiers = GameSimulation.ResearchTiers.ToDictionary(tier => tier.Id, tier => tier.Minutes);
        check(game.FractureActive && !one && two && GameSimulation.HardTrioNeeded == 2 &&
              tiers["essences"] + tiers["flora"] + tiers["astral"] < tiers["overseer"] + tiers["scout"] + tiers["fission"] + 4f &&
              WorldEventSystem.ObeliskShieldKills == 12,
            "the second ladder is shorter by design: any two of the hard trio open the bulbs, post-Fracture research is short, and an obelisk's shield falls after 12 kills");

        var report = console.Execute("pacing");
        check(report.Count >= RunPacing.Ladder.Count + 1 && report[^1].StartsWith("First ladder"),
            "the console's pacing shows each step of the ladder in minutes and the run's hours");
    }
}
