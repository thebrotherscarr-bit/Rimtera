namespace Rimtera.Systems;

/// <summary>How a run stands: still going, lost (the colony cannot launch any more), or won (it launched).</summary>
public enum RunOutcome : byte { Ongoing, Lost, Won }

/// <summary>One line of the run's story: when it happened and what happened.</summary>
public sealed record ChronicleEntry(int Day, float Hour, string Text);

/// <summary>The run's outcome and its chronicle, saved with the world.</summary>
public sealed record RunStateSnapshot(RunOutcome Outcome, string? Reason, ChronicleEntry[]? Chronicle);

/// <summary>
/// The run as a story (docs/DESIGN_COLONY_ESCAPE.md §3): a chronicle of the moments that matter (landing, arrivals,
/// deaths, bosses, the Fracture) and the run's outcome. A run is lost when the colony can no longer launch; for now
/// that is the station AI's destruction, since it holds the ship's plans (rebuilding it is planned for later).
/// </summary>
public sealed partial class GameSimulation
{
    /// <summary>Most chronicle entries kept; the oldest go first.</summary>
    public const int ChronicleCapacity = 400;
    /// <summary>Least game hours between two "the station AI is under attack" warnings.</summary>
    public const float StationAiWarningHours = 0.5f;

    private readonly List<ChronicleEntry> _chronicle = new();
    private float _lastStationAiWarningHour = float.NegativeInfinity;

    public IReadOnlyList<ChronicleEntry> Chronicle => _chronicle;
    public RunOutcome Outcome { get; private set; } = RunOutcome.Ongoing;
    /// <summary>Why the run ended, for the end screen; null while it goes on.</summary>
    public string? OutcomeReason { get; private set; }
    public bool RunOver => Outcome != RunOutcome.Ongoing;

    /// <summary>Adds a line to the run's story.</summary>
    public void Record(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        _chronicle.Add(new ChronicleEntry(Day, ClockHour, text));
        if (_chronicle.Count > ChronicleCapacity)
            _chronicle.RemoveAt(0);
    }

    /// <summary>Ends the run once (later calls are ignored), recording why.</summary>
    public void EndRun(RunOutcome outcome, string reason)
    {
        if (RunOver || outcome == RunOutcome.Ongoing)
            return;
        Outcome = outcome;
        OutcomeReason = reason;
        Record(reason);
        PostMessage(reason);
    }

    /// <summary>
    /// The station AI is the colony's heart: while it is hurt the player is warned (not every hit). Since 0.3.8 its death
    /// on a setting with stakes takes it down rather than ending the run: the machines go dark until an AI core rebuilds it
    /// (GameSimulation.Power.cs). On Explorer and Creative it comes back like any resident.
    /// </summary>
    private bool StationAiDefeat(TownNpc npc)
    {
        if (npc.Kind != TownNpcKind.StationAi || !PeopleDieForGood)
            return false;
        TakeStationAiDown(npc); // 0.3.8: no longer the end of the run; the machines go dark until it is rebuilt
        return true;
    }

    private void WarnStationAiHurt(TownNpc npc)
    {
        if (npc.Kind != TownNpcKind.StationAi || ElapsedGameHours - _lastStationAiWarningHour < StationAiWarningHours)
            return;
        _lastStationAiWarningHour = ElapsedGameHours;
        PostMessage($"The station AI is under attack! ({npc.HitPoints:0} / {npc.MaximumHitPoints:0})");
    }

    private RunStateSnapshot CaptureRun() => new(Outcome, OutcomeReason, _chronicle.ToArray());

    private void RestoreRun(RunStateSnapshot? run)
    {
        _chronicle.Clear();
        if (run is null)
            return;
        Outcome = Enum.IsDefined(run.Outcome) ? run.Outcome : RunOutcome.Ongoing;
        OutcomeReason = Outcome == RunOutcome.Ongoing ? null : run.Reason;
        foreach (var entry in (run.Chronicle ?? []).TakeLast(ChronicleCapacity))
            if (entry is not null && !string.IsNullOrWhiteSpace(entry.Text) && float.IsFinite(entry.Hour))
                _chronicle.Add(entry);
    }
}
