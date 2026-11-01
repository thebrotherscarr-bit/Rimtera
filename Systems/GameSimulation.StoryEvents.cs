using Rimtera.Characters;

namespace Rimtera.Systems;

/// <summary>
/// The event catalogue (0.6.4, docs/VISION.md §4.3): Terraria's events and invasions under the storyteller, plus sickness,
/// power outages and traders (supply drops and arrivals were already its own). The invasions that used to roll at dusk on
/// their own (the scavengers, the pirates, the precursor probes) are now sent by the storyteller, with a warning letter and
/// a countdown; the moons and other events still come on Terraria's schedule, and now write a letter when they begin.
/// </summary>
public sealed partial class GameSimulation
{
    /// <summary>How long rust fever lasts (game hours), before a doctor's help.</summary>
    public const float ShortestSicknessHours = 24f, LongestSicknessHours = 48f;
    /// <summary>How fast a sick person works and walks.</summary>
    public const float SickFactor = 0.6f;
    /// <summary>The Medical level of someone who can tend the sick (they recover twice as fast).</summary>
    public const int DoctorMedicalLevel = 6;
    /// <summary>Hours of warning before an invasion the storyteller sends.</summary>
    public const float InvasionWarningHours = 2f;
    /// <summary>The station AI's reserve below which an outage has nothing to take.</summary>
    public const int OutageMinimumPower = 100;

    /// <summary>The invasions the storyteller sends, in the order progress opens them.</summary>
    public static readonly IReadOnlyList<WorldEventKind> StoryInvasions =
        [WorldEventKind.ScavengerRaid, WorldEventKind.PirateBoarding, WorldEventKind.PrecursorProbes];

    private const string InvasionDueFlag = "invasion.due", InvasionKindFlag = "invasion.kind";

    /// <summary>Work and walking pace while sick.</summary>
    public static float SicknessFactor(TownNpc npc) => npc.IsSick ? SickFactor : 1f;

    // ---- rust fever ----

    /// <summary>Whether someone well, other than <paramref name="patient"/>, can tend the sick.</summary>
    public bool HasDoctor(TownNpc? patient = null) => _townNpcs.Any(npc => !ReferenceEquals(npc, patient) && !npc.IsBound &&
        !npc.IsPrisoner && !npc.IsSick && npc.Kind != TownNpcKind.StationAi && npc.Stats.Level(ColonistSkill.Medical) >= DoctorMedicalLevel);

    /// <summary>Gives someone rust fever for <paramref name="hours"/>.</summary>
    public void MakeSick(TownNpc npc, float hours)
    {
        ArgumentNullException.ThrowIfNull(npc);
        npc.SickHours = Math.Clamp(hours, 0f, LongestSicknessHours);
    }

    private void UpdateSickness(TownNpc npc, float deltaSeconds)
    {
        if (!npc.IsSick)
            return;
        var hours = deltaSeconds * GameHoursPerRealSecond * (HasDoctor(npc) ? 2f : 1f);
        npc.SickHours = Math.Max(0f, npc.SickHours - hours);
        if (!npc.IsSick)
            PostMessage($"{npc.Name} has recovered from rust fever.");
    }

    string? IStoryHost.StrikeSickness(float threatPoints)
    {
        var people = _townNpcs.Where(npc => Eats(npc) && !npc.IsBound && !npc.IsPrisoner && !npc.IsSick && npc.HomeCell is not null).ToArray();
        if (people.Length == 0)
            return null;
        var patient = people[(int)(_storyteller.NextUnit() * people.Length) % people.Length];
        MakeSick(patient, _storyteller.NextRange(ShortestSicknessHours, LongestSicknessHours));
        Record($"{patient.Name} fell sick with rust fever.");
        return $"Rust fever: {patient.Name} has fallen sick and will be slow for a day or two" +
               (HasDoctor(patient) ? "; the colony's doctor will see them through sooner." : ". A doctor (Medical 6) would help.");
    }

    // ---- power outages ----

    string? IStoryHost.CutPower(float threatPoints)
    {
        if (!PowerCanFail)
            return null;
        var share = Math.Clamp(0.25f + threatPoints / 2000f, 0.25f, 0.6f);
        var before = StationPower;
        var lost = (int)MathF.Round(before * share);
        SetPower(before - lost);
        return $"Power outage: a surge burned through {lost} of the station AI's power ({before} → {before - lost}). Feed it to get the machines back up to speed.";
    }

    private bool PowerCanFail => Flags.IsSet(PowerInitFlag) && StationAiOnline && Flags.Get(PowerFlag) >= OutageMinimumPower &&
                                 _townNpcs.Any(npc => npc.Kind == TownNpcKind.StationAi && !npc.IsBound);

    // ---- traders ----

    private bool TraderCanCome => ClockHour >= ContentFillTown.MerchantArriveFrom && ClockHour < ContentFillTown.MerchantLeaveHour - 2f &&
                                  _townNpcs.All(npc => npc.Kind != TownNpcKind.TravellingMerchant) &&
                                  _townNpcs.Count(npc => !npc.IsBound && npc.HomeCell is not null) >= ContentFillTown.MerchantMinimumResidents;

    string? IStoryHost.CallTrader()
    {
        if (!TraderCanCome || ContentFillArrivalSpot(4) is not { } spot)
            return null;
        var npc = AddTownNpc(TownNpcKind.TravellingMerchant, StandingAt(spot), bound: false);
        npc.HomeCell = spot;
        return $"A trader passing through: {npc.Name} the {npc.Definition.Role} has come by with wares until dusk.";
    }

    // ---- invasions ----

    /// <summary>The invasion the storyteller has announced, or null.</summary>
    public WorldEventKind? PendingInvasion => Flags.IsSet(InvasionDueFlag) ? (WorldEventKind)(Flags.Get(InvasionKindFlag) - 1) : null;

    /// <summary>The storyteller's invasions the colony's progress allows now.</summary>
    public IEnumerable<WorldEventKind> InvasionsReady => StoryInvasions.Where(kind => _worldEvents.CanStart(kind, this, forced: false, out _));

    private bool InvasionReady => PendingInvasion is null && InvasionsReady.Any();

    /// <summary>
    /// Announces an invasion <paramref name="warningHours"/> ahead (the latest progress opened is the likeliest) and returns
    /// the warning; null when none can come or one is already on its way.
    /// </summary>
    public string? ScheduleInvasion(float warningHours = InvasionWarningHours)
    {
        if (PendingInvasion is not null)
            return null;
        var ready = InvasionsReady.ToArray();
        if (ready.Length == 0)
            return null;
        // Weights 1, 2, 3 by how late progress opens them.
        var weights = ready.Select(kind => 1f + StoryInvasions.ToList().IndexOf(kind)).ToArray();
        var roll = _storyteller.NextUnit() * weights.Sum();
        var chosen = ready[^1];
        for (var i = 0; i < ready.Length; i++)
        {
            roll -= weights[i];
            if (roll <= 0f)
            {
                chosen = ready[i];
                break;
            }
        }
        var due = ElapsedGameHours + warningHours;
        SetFlagValue(InvasionDueFlag, Math.Max(1L, (long)MathF.Round(due * 100f)));
        SetFlagValue(InvasionKindFlag, (long)chosen + 1);
        var name = _worldEvents.Spec(chosen).Name;
        SendLetter(LetterKind.Threat, $"{name} coming", $"Scouts report a {name} gathering. It will hit in about {warningHours:0.#} hours: arm your people and get home.",
            due, EventLetterTag(chosen));
        Record($"Scouts warned of a {name}.");
        return $"Invasion warning! A {name} will hit in about {warningHours:0.#} hours.";
    }

    string? IStoryHost.ScheduleInvasion(float threatPoints) => ScheduleInvasion();

    /// <summary>Each step: an announced invasion whose hour has come begins.</summary>
    private void UpdateInvasions()
    {
        if (PendingInvasion is not { } kind || ElapsedGameHours < Flags.Get(InvasionDueFlag) / 100f)
            return;
        Flags.Clear(InvasionDueFlag);
        Flags.Clear(InvasionKindFlag);
        if (_worldEvents.TryStart(kind, this, forced: true, out _))
            return;
        _letters.RemoveAll(letter => letter.Tag == EventLetterTag(kind));
        PostMessage($"The {_worldEvents.Spec(kind).Name} never came.");
    }

    private static string EventLetterTag(WorldEventKind kind) => $"event.{WorldFlags.Key(kind)}";

    /// <summary>Events that matter write a letter when they begin (weather does not); an invasion's replaces its warning.</summary>
    void IWorldEventHost.EventStarted(WorldEventSpec spec)
    {
        var (kind, body) = spec.Category switch
        {
            WorldEventCategory.Invasion => (LetterKind.Threat, $"The {spec.Name} has begun. Hold the base until its waves are beaten."),
            WorldEventCategory.Night => (LetterKind.BadNews, $"The {spec.Name} is up. Stay near home until dawn."),
            WorldEventCategory.Day => (LetterKind.BadNews, $"The {spec.Name} has begun. It lasts until dusk."),
            WorldEventCategory.Calm or WorldEventCategory.Party => (LetterKind.Good, $"The {spec.Name} has begun."),
            WorldEventCategory.World => (LetterKind.Neutral, $"The {spec.Name} has begun."),
            _ => (LetterKind.Neutral, string.Empty)
        };
        if (spec.Category != WorldEventCategory.Weather)
            SendLetter(kind, spec.Name, body, tag: EventLetterTag(spec.Kind));
    }
}
