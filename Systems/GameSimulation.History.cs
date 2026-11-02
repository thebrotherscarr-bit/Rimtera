using System.Numerics;
using Rimtera.Entities;

namespace Rimtera.Systems;

/// <summary>One line of the History tab; a heading starts a section.</summary>
public sealed record HistoryLine(string Text, bool Heading = false);

/// <summary>
/// The History tab (0.6.6, docs/VISION.md §4.3): the run's story so far. Where it stands (the day, the act and what it asks,
/// the people, the losses, the bosses, the raids and invasions weathered, the storyteller and its next threat), the acts it
/// has passed through and when, and its chronicle, newest first. The counts are kept in the world's flags, so they are saved.
/// </summary>
public sealed partial class GameSimulation
{
    private const string DeathsFlag = "history.deaths", RaidsFlag = "history.raids";

    private static string ActDayFlag(RunAct act) => $"story.act.{(int)act}.day";

    /// <summary>People lost (settlers and residents; prisoners do not count).</summary>
    public int Deaths => (int)Flags.Get(DeathsFlag);

    /// <summary>Raids that reached their target.</summary>
    public int RaidsWeathered => (int)Flags.Get(RaidsFlag);

    /// <summary>The day an act began (Landfall on day 1); null when the run passed it before 0.6.6 kept the day.</summary>
    public int? ActBeganDay(RunAct act) =>
        act == RunAct.Landfall ? 1 : Flags.Get(ActDayFlag(act)) is > 0 and var day ? (int)day : null;

    /// <summary>The run's story so far, as the History tab shows it.</summary>
    public IReadOnlyList<HistoryLine> History()
    {
        var act = Act;
        var people = _townNpcs.Count(npc => !npc.IsBound && !npc.IsPrisoner && npc.Kind != TownNpcKind.StationAi);
        var prisoners = _townNpcs.Count(npc => npc.IsPrisoner);
        var bosses = EnemyArchetypes.BossProgression.Where(_defeatedBosses.Contains).Select(boss => EnemyArchetypes.Get(boss).DisplayName).ToArray();
        var invasions = StoryInvasions.Sum(_worldEvents.Completions);
        var threat = Storyteller.ComputeThreatPoints(Day, ColonyWealth, SettlerCount, _defeatedBosses.Count, (int)act);
        var lines = new List<HistoryLine>
        {
            new("The run so far", Heading: true),
            new($"Day {Day} · Act {(int)act}: {ActName(act)}. {ActGoal(act)}"),
            new($"People: {people} living here{(prisoners > 0 ? $", {prisoners} held prisoner" : "")} · {Deaths} lost"),
            new(bosses.Length == 0 ? "Bosses defeated: none yet" : $"Bosses defeated ({bosses.Length}): {string.Join(", ", bosses)}"),
            new($"Raids weathered: {RaidsWeathered} · invasions beaten: {invasions} · outposts founded: {OutpostsFounded}" +
                (Flags.Get("history.relays_lost") is > 0 and var lostRelays ? $" · relays lost: {lostRelays}" : "")),
            new($"Storyteller: {Storyteller.Name(_storyteller.Kind)}, threat ×{_storyteller.ThreatScale:0.##} · next threat about {threat * _storyteller.ThreatScale * (1f - _storyteller.Adaptation):0} points" +
                (_storyteller.Adaptation > 0f ? $" (eased {_storyteller.Adaptation:P0} after losses)" : "")),
            new("Acts", Heading: true)
        };
        for (var passed = RunAct.Landfall; passed <= act; passed++)
            lines.Add(new($"Act {(int)passed}: {ActName(passed)} · " + (ActBeganDay(passed) is { } day ? $"began on day {day}" : "began earlier")));
        // 0.7.1: the outposts, where they lie, and who guards them.
        var outposts = Outposts;
        lines.Add(new("Outposts", Heading: true));
        if (outposts.Count == 0)
            lines.Add(new(OutpostsFounded == 0 ? "None yet: move people to a house far from the base to found one." : "None standing now."));
        foreach (var outpost in outposts)
            lines.Add(new($"{outpost.Label} · {OutpostBearing(outpost)} · {outpost.People.Count} living there, " +
                          (outpost.Guards == 0 ? "unguarded" : $"{outpost.Guards} armed") +
                          (_colonySentries.Count(sentry => Vector2.Distance(sentry.Position, outpost.Center) <= OutpostSpread) is > 0 and var sentries
                              ? $", {sentries} sentr{(sentries == 1 ? "y" : "ies")}" : "")));
        // 0.7.9: the power grid: the station AI and each relay.
        lines.Add(new("Power grid", Heading: true));
        lines.Add(new(StationAiOnline ? $"Station AI: power {StationPower}/{MaxPower}, tier {StationPowerTier}" : "Station AI: down (machines run unpowered)"));
        var relays = Pylons;
        if (relays.Count == 0)
            lines.Add(new("No relays yet: a pylon is a relay once two people live around it."));
        foreach (var relay in relays)
            lines.Add(new($"{PylonPlace(relay)} · {RelayStatus(relay.Cell)}"));
        lines.Add(new("Chronicle, newest first", Heading: true));
        if (_chronicle.Count == 0)
            lines.Add(new("Nothing has happened yet."));
        for (var i = _chronicle.Count - 1; i >= 0; i--)
        {
            var entry = _chronicle[i];
            lines.Add(new($"Day {entry.Day}, {(int)entry.Hour:00}:{(int)(entry.Hour % 1f * 60f):00} · {entry.Text}"));
        }
        return lines;
    }
}
