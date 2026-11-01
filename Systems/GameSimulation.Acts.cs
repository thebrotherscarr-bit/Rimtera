namespace Rimtera.Systems;

/// <summary>The run's acts (0.6.2, docs/DESIGN_COLONY_ESCAPE.md §2), which follow Terraria's progress.</summary>
public enum RunAct : byte { Landfall = 1, Descent, Core, Fracture, Zealots, Collapse, Launch }

/// <summary>
/// Acts (0.6.2): the storyteller's chapters, read from the run's progress. Landfall (just you and the station AI) → Descent
/// (someone has settled in) → the Core (you have reached the irradiated core) → the Fracture (the gate boss fell; hardmode) →
/// the Zealots (the cult's ritual is ready, or its cantor or sieges have begun) → the Collapse (the four obelisks are down) →
/// Launch (the Astro-God has fallen). An act never goes back. Each one raises the storyteller's threat floor and the share of
/// major threats, and opens incidents of its own (toxic fallout from the Descent, solar flares from the Core).
/// </summary>
public sealed partial class GameSimulation
{
    private const string ActFlag = "story.act", CoreReachedFlag = "core.reached", ActHourFlag = "story.act.hour";

    /// <summary>Game hours since the current act began (0.6.5).</summary>
    public float HoursInAct => Math.Max(0f, ElapsedGameHours - Flags.Get(ActHourFlag));

    /// <summary>The act the run has reached.</summary>
    public RunAct Act => (RunAct)Math.Max((int)RunAct.Landfall, Math.Max((int)Flags.Get(ActFlag), (int)ActFromProgress()));

    public static string ActName(RunAct act) => act switch
    {
        RunAct.Landfall => "Landfall",
        RunAct.Descent => "Descent",
        RunAct.Core => "The Core",
        RunAct.Fracture => "The Fracture",
        RunAct.Zealots => "The Zealots",
        RunAct.Collapse => "Collapse",
        _ => "Launch"
    };

    public static string ActGoal(RunAct act) => act switch
    {
        RunAct.Landfall => "Build a shelter and a house; someone will come to live in it.",
        RunAct.Descent => "Dig down through the biomes to the irradiated core.",
        RunAct.Core => "The core's gate boss wakes. Defeat it.",
        RunAct.Fracture => "The surface has fractured. Climb the hardmode ladder.",
        RunAct.Zealots => "The Astro-God's zealots have come. Take the four obelisks.",
        RunAct.Collapse => "Collapse the fuel cores into the core and face the Astro-God.",
        _ => "Fuel the ship and launch."
    };

    private RunAct ActFromProgress()
    {
        if (Flags.IsSet("boss.astro_god.down"))
            return RunAct.Launch;
        if (Flags.IsSet("obelisks.down"))
            return RunAct.Collapse;
        if (Flags.IsSet("ritual.ready") || Flags.IsSet("boss.void_cantor.down") || Flags.IsSet("obelisks.started"))
            return RunAct.Zealots;
        if (FractureActive || _defeatedBosses.Contains(CoreGateBoss))
            return RunAct.Fracture;
        if (Flags.IsSet(CoreReachedFlag))
            return RunAct.Core;
        if (_townNpcs.Any(npc => !npc.IsBound && !npc.IsPrisoner && npc.HomeCell is not null && npc.Kind != TownNpcKind.StationAi))
            return RunAct.Descent;
        return RunAct.Landfall;
    }

    /// <summary>Hourly: notes reaching the core, and announces a new act.</summary>
    private void UpdateActs()
    {
        if (!Flags.IsSet(CoreReachedFlag) && InIrradiatedCore)
            Flags.Set(CoreReachedFlag);
        var act = Act;
        if ((int)act <= Flags.Get(ActFlag))
            return;
        var first = Flags.Get(ActFlag) == 0;
        Flags.Clear(ActFlag);
        Flags.Increment(ActFlag, (int)act);
        if (first && act == RunAct.Landfall)
            return; // the run begins in Landfall without a fanfare (its clock starts at hour 0)
        SetFlagValue(ActHourFlag, (long)MathF.Floor(ElapsedGameHours)); // 0.6.5: when the act began
        SetFlagValue(ActDayFlag(act), Day); // 0.6.6: the History tab's act timeline
        PostMessage($"Act {(int)act}: {ActName(act)}. {ActGoal(act)}");
        Record($"Act {(int)act} began: {ActName(act)}.");
    }
}
