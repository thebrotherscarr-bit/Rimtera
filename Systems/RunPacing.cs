using Rimtera.Entities;

namespace Rimtera.Systems;

/// <summary>One step of the run's ladder, for the pacing model.</summary>
/// <param name="Name">What the step is.</param>
/// <param name="AfterFracture">On the second (hardmode) ladder.</param>
/// <param name="GatherMinutes">Estimated minutes to explore, mine and craft for it (an assumption, see <see cref="RunPacing"/>).</param>
/// <param name="FightMinutes">Estimated minutes of fighting, attempts included (an assumption).</param>
/// <param name="ResearchId">The research tier this step waits on, read from the game's data.</param>
/// <param name="NightWait">Whether it waits for night (a night boss), read as part of the day from the game's clock.</param>
/// <param name="ObeliskSieges">How many obelisk sieges it holds, sized from the game's shield kills.</param>
public sealed record PacingStep(string Name, bool AfterFracture, float GatherMinutes, float FightMinutes,
    string? ResearchId = null, bool NightWait = false, int ObeliskSieges = 0);

/// <summary>
/// The run's pacing model (0.8.1, docs/VISION.md §4.5): the ladder from landfall to launch, step by step, priced in real
/// minutes for a player who knows Terraria but not this world. What the game decides is read from its own data: research
/// time (the tier's minutes at the study rate of the AI and one researcher; half of it blocks, since you play on while it
/// studies), the wait for a night boss (a quarter of the day), and obelisk sieges (their shield kills at
/// <see cref="SecondsPerSiegeKill"/>). What it cannot know, how long exploring and fighting take, is an explicit estimate per
/// step; playtests should replace those numbers. The checks: the whole run lands in 10–14 hours, and the second ladder is
/// shorter than the first.
/// </summary>
public static class RunPacing
{
    /// <summary>The run-length target, in hours (docs/DESIGN_COLONY_ESCAPE.md §1).</summary>
    public const float TargetLowHours = 10f, TargetHighHours = 14f;
    /// <summary>Researchers assumed at home: the station AI plus one.</summary>
    public const int AssumedResearchers = 1;
    /// <summary>The share of research time that holds you up.</summary>
    public const float ResearchBlockingShare = 0.5f;
    /// <summary>Seconds to kill one siege creature, an assumption.</summary>
    public const float SecondsPerSiegeKill = 10f;

    /// <summary>The ladder. Gather and fight minutes are estimates; the rest comes from the game.</summary>
    public static IReadOnlyList<PacingStep> Ladder { get; } =
    [
        new("Landfall: shelter, a house, the first settler", false, 25f, 0f),
        new("Ferrite gear; the Stone Sentinel", false, 60f, 10f, "overseer"),
        new("The Alpha Skitter", false, 35f, 8f, NightWait: true),
        new("The Damaged Mining Drone", false, 40f, 8f),
        new("The Plant Overgrowth", false, 45f, 10f),
        new("The Scout Mech", false, 45f, 10f, "scout", NightWait: true),
        new("The descent to the irradiated core", false, 50f, 0f),
        new("The Fission Tyrant (the gate)", false, 30f, 12f, "fission"),
        new("Hardmode ores and gear at the alloy anvil", true, 45f, 0f),
        // 0.8.1: the bulbs open after HardTrioNeeded of the three; the gather and fight estimates are per boss.
        new($"The hard trio ({GameSimulation.HardTrioNeeded} of 3 needed)", true, 25f * GameSimulation.HardTrioNeeded, 12f * GameSimulation.HardTrioNeeded, "essences", NightWait: true),
        new("The canopy bulbs; the Mutated Flora", true, 30f, 12f, "flora"),
        new("The vault; the Abomination", true, 25f, 12f),
        new("The ritual; the Void Cantor", true, 15f, 10f),
        new("The four obelisk sieges", true, 10f, 20f, ObeliskSieges: 4),
        new("The Astro-God", true, 25f, 15f, "astral"),
        new("Fuel the ship and launch", true, 25f, 0f)
    ];

    /// <summary>Real minutes in one in-game day.</summary>
    public static float DayMinutes => 24f / GameSimulation.GameHoursPerRealSecond / 60f;

    /// <summary>A step's minutes: the estimates plus what the game's data adds.</summary>
    public static float Minutes(PacingStep step)
    {
        var minutes = step.GatherMinutes + step.FightMinutes;
        if (step.ResearchId is { } id && GameSimulation.FindTier(id) is { } tier)
            minutes += tier.Minutes / (GameSimulation.BaseStudyRate + GameSimulation.ResidentStudyRate * AssumedResearchers) * ResearchBlockingShare;
        if (step.NightWait)
            minutes += DayMinutes / 4f;
        minutes += step.ObeliskSieges * WorldEventSystem.ObeliskShieldKills * SecondsPerSiegeKill / 60f;
        return minutes;
    }

    /// <summary>The first ladder's hours, the second's, and the whole run's.</summary>
    public static (float Before, float After, float Total) Hours()
    {
        var before = Ladder.Where(step => !step.AfterFracture).Sum(Minutes) / 60f;
        var after = Ladder.Where(step => step.AfterFracture).Sum(Minutes) / 60f;
        return (before, after, before + after);
    }

    /// <summary>The model, one line a step, for the console and the docs.</summary>
    public static IEnumerable<string> Report()
    {
        foreach (var step in Ladder)
            yield return $"{(step.AfterFracture ? "II" : "I ")} {Minutes(step),5:0} min  {step.Name}";
        var (before, after, total) = Hours();
        yield return $"First ladder {before:0.0} h · second ladder {after:0.0} h · run {total:0.0} h (target {TargetLowHours:0}–{TargetHighHours:0} h)";
    }
}
