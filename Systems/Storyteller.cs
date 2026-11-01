namespace Rimtera.Systems;

/// <summary>
/// The storyteller's personality (0.6.1, docs/VISION.md §4.3), chosen when the world is made: Steady paces threats evenly
/// (the storyteller every world had before), Chaotic fires more often with shorter breathers, more major threats and wildly
/// varying strength, Builder leaves long quiet stretches, gentler threats and more boons.
/// </summary>
public enum StorytellerKind : byte { Steady, Chaotic, Builder }

/// <summary>Incident families the storyteller chooses between before picking a concrete incident.</summary>
public enum IncidentCategory : byte
{
    MajorThreat,
    MinorThreat,
    Boon
}

/// <summary>Snapshot of the colony that incidents read when deciding whether and how to fire.</summary>
public readonly record struct StoryContext(
    int Day,
    float ClockHour,
    BiomeZone Biome,
    int ColonistCount,
    int DefeatedBosses,
    float ColonyWealth,
    bool BossActive,
    bool RadiatedNearby,
    float PlayerHealthFraction,
    int CrewShortage = 0,
    int LostResidents = 0,
    bool FreeHouse = false,
    // 0.6.2: the run's act (1 Landfall ... 7 Launch).
    int Act = 1);

/// <summary>Parameters handed to one incident execution.</summary>
public readonly record struct IncidentParms(float ThreatPoints, BiomeZone Biome, bool Forced);

/// <summary>One fired incident for the HUD log.</summary>
public sealed record StoryIncidentRecord(int Day, float Hour, string IncidentId, string DisplayName, string Summary, float ThreatPoints);

/// <summary>Per-incident "last fired" bookkeeping, persisted with the storyteller.</summary>
public readonly record struct IncidentCooldownSnapshot(string IncidentId, long LastFiredHour);

/// <summary>Everything a restored game needs for the storyteller to continue identically.</summary>
public sealed record StorytellerSnapshot(
    ulong RngState,
    long HoursElapsed,
    float CooldownHours,
    float QuietHours,
    float TemperatureOffset,
    float TemperatureOffsetHoursRemaining,
    float FalloutHoursRemaining,
    float SolarFlareHoursRemaining,
    int WanderersJoined,
    int IncidentsFired,
    IncidentCooldownSnapshot[]? IncidentCooldowns = null,
    StoryIncidentRecord[]? RecentIncidents = null,
    // 0.6.1: the personality and threat dial picked at world creation (older saves: Steady, ×1).
    StorytellerKind? Kind = null,
    float? ThreatScale = null,
    // 0.6.3: how far threats are eased after losses (older saves: none).
    float? Adaptation = null);

/// <summary>The world-facing side of incidents; implemented by the simulation.</summary>
public interface IStoryHost
{
    StoryContext CaptureStoryContext();
    /// <summary>Plans a raid (0.4.3: announced ahead, it arrives later at its target) and returns how many will come.</summary>
    int SpawnRaidWave(BiomeZone biome, float threatPoints, out string description);
    /// <summary>Adds supplies to the player's inventory and returns a short manifest.</summary>
    string DeliverCargo(BiomeZone biome, float threatPoints);
    bool TryAddWanderer(int wandererIndex, out string name);
    /// <summary>
    /// Brings one arrival into a free house: a town resident whose role was lost to a death, or else a new crew
    /// member. <paramref name="joinedCrew"/> says which. False if nobody could come.
    /// </summary>
    bool TryWelcomeArrival(out string summary, out bool joinedCrew);
    void Announce(string message);
    /// <summary>0.6.3: an incident as a letter (the host decides which deserve one); by default just announced.</summary>
    void Report(string incidentId, IncidentCategory category, string title, string summary) => Announce(summary);
}

/// <summary>
/// A pluggable incident: an eligibility gate, a selection weight, and an execution that
/// returns a log summary (or null if it could not happen after all).
/// </summary>
public interface IStoryIncident
{
    string Id { get; }
    string DisplayName { get; }
    IncidentCategory Category { get; }
    /// <summary>Minimum in-game hours between two firings of this incident.</summary>
    float RefireHours { get; }
    bool CanFire(StoryContext context, Storyteller teller);
    float ChanceWeight(StoryContext context);
    string? TryExecute(IncidentParms parms, Storyteller teller, IStoryHost host);
}

/// <summary>
/// Hourly incident director. Each check it counts down a shared cooldown, then rolls a chance
/// that grows the longer the colony has been quiet; on success it picks a category, then a
/// weighted incident in that category whose own refire timer has elapsed. Threat points scale
/// with day, colony wealth, crew size, and bosses defeated. Original Rimtera design and tuning.
/// </summary>
public sealed class Storyteller
{
    public const int RecentLogSize = 5;
    public const float MinimumThreatPoints = 35f;
    public const float MaximumThreatPoints = 900f;
    /// <summary>Three in-game days of quiet at the start, as Terraria gives a new world: build a house first.</summary>
    private const float OpeningGraceHours = 72f;

    /// <summary>The first day weather incidents (cold snaps, heat waves, fallout, flares) may happen.</summary>
    public const int FirstWeatherDay = 4;

    private readonly List<IStoryIncident> _incidents = new();
    private readonly Dictionary<string, long> _lastFired = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<StoryIncidentRecord> _recent = new();
    private ulong _rngState;

    public Storyteller(int seed)
    {
        _rngState = unchecked((ulong)seed * 0x9E3779B97F4A7C15UL ^ 0xC0FFEE5EEDUL);
        CooldownHours = OpeningGraceHours;
        Register(new RaidIncident());
        Register(new TemperatureShiftIncident(cold: true));
        Register(new TemperatureShiftIncident(cold: false));
        Register(new ToxicFalloutIncident());
        Register(new SolarFlareIncident());
        Register(new CargoDropIncident());
        // Arrivals (new crew, and town residents replacing the dead) come through their own stream, OnHour's TryArrival,
        // not this roll. WandererJoinsIncident stays defined; WanderersJoined counts every crew arrival.
    }

    /// <summary>Id under which the last arrival's hour is kept with the incident cooldowns.</summary>
    public const string ArrivalId = "arrival";
    /// <summary>Least hours between two arrivals.</summary>
    public const float ArrivalGapHours = 8f;
    /// <summary>Hourly arrival chance for each person the colony is short (capped at half).</summary>
    public const float ArrivalChancePerNeed = 0.05f;

    public IReadOnlyList<IStoryIncident> Incidents => _incidents;

    // ---- 0.6.1: personality and threat dial ----
    public StorytellerKind Kind { get; private set; } = StorytellerKind.Steady;
    /// <summary>The threat dial: every incident's strength is multiplied by it (0.5 to 2).</summary>
    public float ThreatScale { get; private set; } = 1f;
    public const float MinimumThreatScale = 0.5f, MaximumThreatScale = 2f;

    /// <summary>Sets the personality and threat dial (world creation, the dev console).</summary>
    public void Configure(StorytellerKind kind, float threatScale)
    {
        Kind = Enum.IsDefined(kind) ? kind : StorytellerKind.Steady;
        ThreatScale = float.IsFinite(threatScale) ? Math.Clamp(threatScale, MinimumThreatScale, MaximumThreatScale) : 1f;
    }

    // ---- 0.6.3: adaptation after losses ----
    /// <summary>The most a storyteller eases threats after losses (half strength).</summary>
    public const float MaximumAdaptation = 0.5f;
    /// <summary>Adaptation added by one death (a settler or resident).</summary>
    public const float AdaptationPerDeath = 0.15f;
    /// <summary>Adaptation lost per hour: a full ease fades over about four days.</summary>
    public const float AdaptationDecayPerHour = MaximumAdaptation / 96f;

    /// <summary>
    /// How much the next threats are eased after losses (0 to <see cref="MaximumAdaptation"/>): a colony that just lost
    /// people gets weaker threats while it recovers, and the ease fades over a few days. A builder eases more, a chaotic
    /// storyteller less.
    /// </summary>
    public float Adaptation { get; private set; }

    /// <summary>Notes a loss (<paramref name="weight"/> 1 is one death): the next threats are eased.</summary>
    public void NoteLoss(float weight = 1f)
    {
        if (!float.IsFinite(weight) || weight <= 0f)
            return;
        var factor = Kind switch { StorytellerKind.Builder => 1.5f, StorytellerKind.Chaotic => 0.5f, _ => 1f };
        Adaptation = Math.Clamp(Adaptation + AdaptationPerDeath * weight * factor, 0f, MaximumAdaptation);
    }

    /// <summary>The parts of the threat budget, for the dev console and the History tab.</summary>
    public static IReadOnlyList<(string Part, float Points)> ThreatBreakdown(int day, float wealth, int colonists, int defeatedBosses, int act) =>
    [
        ("base", 30f),
        ("wealth", 2.4f * MathF.Sqrt(Math.Max(0f, wealth))),
        ("people", colonists * 16f),
        ("days", Math.Max(0, day - 1) * 7f),
        ("bosses", defeatedBosses * 32f),
        ("act", Math.Max(0, act - 1) * 20f)
    ];

    public static string Name(StorytellerKind kind) => kind switch
    {
        StorytellerKind.Chaotic => "Chaotic",
        StorytellerKind.Builder => "Builder",
        _ => "Steady"
    };

    public static string Describe(StorytellerKind kind) => kind switch
    {
        StorytellerKind.Chaotic => "Anything can happen: events come often, with short breathers, and their strength swings wildly.",
        StorytellerKind.Builder => "Long quiet stretches to build in; gentler threats and more good fortune.",
        _ => "Threats rise steadily with your progress, with time to recover between them."
    };

    /// <summary>The hourly chance of an incident after <paramref name="quietHours"/> without one.</summary>
    private float IncidentChance(float quietHours) => Kind switch
    {
        StorytellerKind.Chaotic => Math.Min(0.75f, 0.18f + 0.04f * quietHours),
        StorytellerKind.Builder => Math.Min(0.4f, 0.06f + 0.015f * quietHours),
        _ => Math.Min(0.6f, 0.1f + 0.025f * quietHours)
    };
    public IReadOnlyList<StoryIncidentRecord> RecentIncidents => _recent.ToArray();
    public long HoursElapsed { get; private set; }
    /// <summary>Hours left before the next incident roll is allowed.</summary>
    public float CooldownHours { get; private set; }
    /// <summary>Hours since the last incident; raises the per-hour chance.</summary>
    public float QuietHours { get; private set; }
    public float LastThreatPoints { get; private set; }
    public float TemperatureOffset { get; internal set; }
    public float TemperatureOffsetHoursRemaining { get; internal set; }
    public float FalloutHoursRemaining { get; internal set; }
    public float SolarFlareHoursRemaining { get; internal set; }
    public int WanderersJoined { get; internal set; }
    public int IncidentsFired { get; private set; }
    public bool FalloutActive => FalloutHoursRemaining > 0f;
    public bool SolarFlareActive => SolarFlareHoursRemaining > 0f;

    public void Register(IStoryIncident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);
        _incidents.Add(incident);
    }

    /// <summary>Wealth follows a square-root curve so hoarding raises threats with diminishing returns.</summary>
    public static float ComputeThreatPoints(int day, float wealth, int colonists, int defeatedBosses, int act = 1)
    {
        // 0.6.2: each act past Landfall raises the floor; 0.6.3: the parts are named (ThreatBreakdown).
        var points = ThreatBreakdown(day, wealth, colonists, defeatedBosses, act).Sum(part => part.Points);
        return Math.Clamp(points, MinimumThreatPoints, MaximumThreatPoints);
    }

    /// <summary>Runs one hourly check: timers, cooldown, roll, category, incident.</summary>
    public StoryIncidentRecord? OnHour(IStoryHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        HoursElapsed++;
        Adaptation = Math.Max(0f, Adaptation - AdaptationDecayPerHour);
        AdvanceTimers(host, 1f);
        var context = host.CaptureStoryContext();
        LastThreatPoints = ComputeThreatPoints(context.Day, context.ColonyWealth, context.ColonistCount, context.DefeatedBosses, context.Act);
        if (TryArrival(context, host) is { } arrival)
            return arrival;
        if (CooldownHours > 0f)
        {
            CooldownHours = Math.Max(0f, CooldownHours - 1f);
            return null;
        }

        QuietHours++;
        if (NextUnit() >= IncidentChance(QuietHours))
            return null;

        var category = PickCategory(context);
        if (category is null)
            return null;
        var incident = PickIncident(context, category.Value);
        return incident is null ? null : Execute(incident, context, host, forced: false);
    }

    /// <summary>Fires a named incident immediately if its gate allows it (debug/console use).</summary>
    public StoryIncidentRecord? ForceIncident(string id, IStoryHost host)
    {
        var incident = _incidents.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
        if (incident is null)
            return null;
        var context = host.CaptureStoryContext();
        LastThreatPoints = ComputeThreatPoints(context.Day, context.ColonyWealth, context.ColonistCount, context.DefeatedBosses, context.Act);
        return incident.CanFire(context, this) ? Execute(incident, context, host, forced: true) : null;
    }

    /// <summary>
    /// The arrival stream, a background component beside the threat roll (it ignores the opening grace and threat
    /// cooldowns). Population intent in small: the more people the colony is short (crew below what its housing and
    /// victories support, plus town residents lost to deaths), the likelier someone turns up, by day, into a free house.
    /// Permadeath stays playable because the dead are replaced this way, over time.
    /// </summary>
    private StoryIncidentRecord? TryArrival(StoryContext context, IStoryHost host)
    {
        var needed = Math.Max(0, context.CrewShortage) + Math.Max(0, context.LostResidents);
        if (needed == 0 || !context.FreeHouse || context.ClockHour < 6f || context.ClockHour >= 19f)
            return null;
        if (_lastFired.TryGetValue(ArrivalId, out var last) && HoursElapsed - last < ArrivalGapHours)
            return null;
        if (NextUnit() >= Math.Min(0.5f, ArrivalChancePerNeed * needed))
            return null;
        if (!host.TryWelcomeArrival(out var summary, out var joinedCrew))
            return null;
        if (joinedCrew)
            WanderersJoined++;
        _lastFired[ArrivalId] = HoursElapsed;
        IncidentsFired++;
        var record = new StoryIncidentRecord(context.Day, context.ClockHour, ArrivalId, "Arrival", summary, LastThreatPoints);
        _recent.Enqueue(record);
        while (_recent.Count > RecentLogSize)
            _recent.Dequeue();
        host.Report(ArrivalId, IncidentCategory.Boon, "Arrival", summary);
        return record;
    }

    public bool FiredTooRecently(IStoryIncident incident) =>
        _lastFired.TryGetValue(incident.Id, out var last) && HoursElapsed - last < incident.RefireHours;

    /// <summary>Uniform [0,1) from the persisted SplitMix-style stream.</summary>
    internal float NextUnit()
    {
        unchecked
        {
            _rngState += 0x9E3779B97F4A7C15UL;
            var z = _rngState;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (z >> 40) / (float)(1UL << 24);
        }
    }

    internal float NextRange(float min, float max) => min + (max - min) * NextUnit();

    private void AdvanceTimers(IStoryHost host, float hours)
    {
        if (TemperatureOffsetHoursRemaining > 0f)
        {
            TemperatureOffsetHoursRemaining = Math.Max(0f, TemperatureOffsetHoursRemaining - hours);
            if (TemperatureOffsetHoursRemaining <= 0f)
            {
                host.Announce(TemperatureOffset < 0f ? "The cold snap has broken." : "The heat wave has passed.");
                TemperatureOffset = 0f;
            }
        }
        if (FalloutHoursRemaining > 0f)
        {
            FalloutHoursRemaining = Math.Max(0f, FalloutHoursRemaining - hours);
            if (FalloutHoursRemaining <= 0f)
                host.Announce("The toxic fallout has settled.");
        }
        if (SolarFlareHoursRemaining > 0f)
        {
            SolarFlareHoursRemaining = Math.Max(0f, SolarFlareHoursRemaining - hours);
            if (SolarFlareHoursRemaining <= 0f)
                host.Announce("The solar flare has faded; energy weapons are back online.");
        }
    }

    private IncidentCategory? PickCategory(StoryContext context)
    {
        // Early days lean toward boons; threats grow more common as the colony ages.
        var threatBias = Math.Min(1f, context.Day / 6f);
        var (major, boon) = Kind switch { StorytellerKind.Chaotic => (1.5f, 0.8f), StorytellerKind.Builder => (0.6f, 1.3f), _ => (1f, 1f) };
        major *= 1f + 0.1f * Math.Max(0, context.Act - 1); // 0.6.2: later acts lean toward major threats
        var weights = new (IncidentCategory Category, float Weight)[]
        {
            (IncidentCategory.MajorThreat, (0.15f + 0.3f * threatBias) * major),
            (IncidentCategory.MinorThreat, 0.3f + 0.1f * threatBias),
            (IncidentCategory.Boon, (0.55f - 0.25f * threatBias) * boon)
        };
        var available = weights.Where(entry => _incidents.Any(incident =>
            incident.Category == entry.Category && Eligible(incident, context))).ToArray();
        if (available.Length == 0)
            return null;
        var total = available.Sum(entry => entry.Weight);
        var roll = NextUnit() * total;
        foreach (var entry in available)
        {
            roll -= entry.Weight;
            if (roll < 0f)
                return entry.Category;
        }
        return available[^1].Category;
    }

    private IStoryIncident? PickIncident(StoryContext context, IncidentCategory category)
    {
        var candidates = _incidents
            .Where(incident => incident.Category == category && Eligible(incident, context))
            .Select(incident => (Incident: incident, Weight: Math.Max(0f, incident.ChanceWeight(context))))
            .Where(entry => entry.Weight > 0f)
            .ToArray();
        if (candidates.Length == 0)
            return null;
        var roll = NextUnit() * candidates.Sum(entry => entry.Weight);
        foreach (var entry in candidates)
        {
            roll -= entry.Weight;
            if (roll < 0f)
                return entry.Incident;
        }
        return candidates[^1].Incident;
    }

    private bool Eligible(IStoryIncident incident, StoryContext context) =>
        !FiredTooRecently(incident) && incident.CanFire(context, this);

    private StoryIncidentRecord? Execute(IStoryIncident incident, StoryContext context, IStoryHost host, bool forced)
    {
        // 0.6.1: the threat dial, and a chaotic storyteller's swings (×0.6 to ×1.6); a steady one at ×1 changes nothing.
        var swing = Kind == StorytellerKind.Chaotic ? 0.6f + NextUnit() : Kind == StorytellerKind.Builder ? 0.8f : 1f;
        // 0.6.3: threats are eased while the colony recovers from losses.
        var ease = incident.Category == IncidentCategory.Boon ? 1f : 1f - Adaptation;
        LastThreatPoints = Math.Clamp(LastThreatPoints * ThreatScale * swing * ease, MinimumThreatPoints, MaximumThreatPoints * MaximumThreatScale);
        var summary = incident.TryExecute(new IncidentParms(LastThreatPoints, context.Biome, forced), this, host);
        if (summary is null)
            return null;

        _lastFired[incident.Id] = HoursElapsed;
        IncidentsFired++;
        QuietHours = 0f;
        // Adaptive rest: threats buy a longer breather, more so when the player is hurt.
        var rest = incident.Category switch
        {
            IncidentCategory.MajorThreat => 10f,
            IncidentCategory.MinorThreat => 6f,
            _ => 4f
        };
        if (incident.Category != IncidentCategory.Boon)
            rest += (1f - Math.Clamp(context.PlayerHealthFraction, 0f, 1f)) * 8f;
        rest *= Kind switch { StorytellerKind.Chaotic => 0.6f, StorytellerKind.Builder => 1.5f, _ => 1f };
        CooldownHours = Math.Max(CooldownHours, rest);

        var record = new StoryIncidentRecord(context.Day, context.ClockHour, incident.Id, incident.DisplayName, summary, LastThreatPoints);
        _recent.Enqueue(record);
        while (_recent.Count > RecentLogSize)
            _recent.Dequeue();
        host.Report(incident.Id, incident.Category, incident.DisplayName, summary);
        return record;
    }

    public StorytellerSnapshot Capture() => new(
        _rngState, HoursElapsed, CooldownHours, QuietHours,
        TemperatureOffset, TemperatureOffsetHoursRemaining, FalloutHoursRemaining, SolarFlareHoursRemaining,
        WanderersJoined, IncidentsFired,
        _lastFired.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new IncidentCooldownSnapshot(pair.Key, pair.Value)).ToArray(),
        _recent.ToArray(),
        Kind == StorytellerKind.Steady ? null : Kind,
        ThreatScale == 1f ? null : ThreatScale,
        Adaptation == 0f ? null : Adaptation);

    public void Restore(StorytellerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.HoursElapsed < 0 || snapshot.WanderersJoined < 0 || snapshot.IncidentsFired < 0 ||
            !Finite(snapshot.CooldownHours) || !Finite(snapshot.QuietHours) || !Finite(snapshot.TemperatureOffset) ||
            !Finite(snapshot.TemperatureOffsetHoursRemaining) || !Finite(snapshot.FalloutHoursRemaining) ||
            !Finite(snapshot.SolarFlareHoursRemaining))
            throw new InvalidDataException("The saved storyteller state is invalid.");
        _rngState = snapshot.RngState;
        HoursElapsed = snapshot.HoursElapsed;
        CooldownHours = Math.Max(0f, snapshot.CooldownHours);
        QuietHours = Math.Max(0f, snapshot.QuietHours);
        TemperatureOffset = Math.Clamp(snapshot.TemperatureOffset, -60f, 60f);
        TemperatureOffsetHoursRemaining = Math.Max(0f, snapshot.TemperatureOffsetHoursRemaining);
        FalloutHoursRemaining = Math.Max(0f, snapshot.FalloutHoursRemaining);
        SolarFlareHoursRemaining = Math.Max(0f, snapshot.SolarFlareHoursRemaining);
        WanderersJoined = snapshot.WanderersJoined;
        IncidentsFired = snapshot.IncidentsFired;
        Configure(snapshot.Kind ?? StorytellerKind.Steady, snapshot.ThreatScale ?? 1f);
        Adaptation = snapshot.Adaptation is { } adaptation && float.IsFinite(adaptation) ? Math.Clamp(adaptation, 0f, MaximumAdaptation) : 0f;
        _lastFired.Clear();
        foreach (var entry in snapshot.IncidentCooldowns ?? Array.Empty<IncidentCooldownSnapshot>())
            if (!string.IsNullOrWhiteSpace(entry.IncidentId))
                _lastFired[entry.IncidentId] = entry.LastFiredHour;
        _recent.Clear();
        foreach (var record in (snapshot.RecentIncidents ?? Array.Empty<StoryIncidentRecord>()).TakeLast(RecentLogSize))
            if (record is not null)
                _recent.Enqueue(record);

        static bool Finite(float value) => float.IsFinite(value);
    }
}

/// <summary>A wave of local creatures, sized by threat points, announced ahead and aimed at the base or an outpost (0.4.3).</summary>
public sealed class RaidIncident : IStoryIncident
{
    public string Id => "raid";
    public string DisplayName => "Raid";
    public IncidentCategory Category => IncidentCategory.MajorThreat;
    public float RefireHours => 18f;
    // Terraria's invasions wait for progress: raids start once the first boss is down.
    public bool CanFire(StoryContext context, Storyteller teller) => !context.BossActive && context.DefeatedBosses >= 1;
    public float ChanceWeight(StoryContext context) => 1f;

    public string? TryExecute(IncidentParms parms, Storyteller teller, IStoryHost host)
    {
        // 0.4.3: the host plans the raid and warns; it arrives at its target later.
        var count = host.SpawnRaidWave(parms.Biome, parms.ThreatPoints, out var description);
        return count <= 0 ? null : $"Raid warning! {description}";
    }
}

/// <summary>Cold snap or heat wave: a temporary offset on outdoor air.</summary>
public sealed class TemperatureShiftIncident(bool cold) : IStoryIncident
{
    public string Id => cold ? "cold-snap" : "heat-wave";
    public string DisplayName => cold ? "Cold snap" : "Heat wave";
    public IncidentCategory Category => IncidentCategory.MinorThreat;
    public float RefireHours => 30f;

    public bool CanFire(StoryContext context, Storyteller teller) =>
        context.Day >= Storyteller.FirstWeatherDay && teller.TemperatureOffsetHoursRemaining <= 0f &&
        (cold ? context.Biome != BiomeZone.MoltenChasm : context.Biome != BiomeZone.FrozenWasteland);

    public float ChanceWeight(StoryContext context) => (cold, context.Biome) switch
    {
        (true, BiomeZone.FrozenWasteland) => 1.6f,
        (false, BiomeZone.JunkWasteland or BiomeZone.MoltenChasm) => 1.6f,
        _ => 0.8f
    };

    public string? TryExecute(IncidentParms parms, Storyteller teller, IStoryHost host)
    {
        var magnitude = teller.NextRange(10f, 20f);
        teller.TemperatureOffset = cold ? -magnitude : magnitude;
        teller.TemperatureOffsetHoursRemaining = MathF.Round(teller.NextRange(8f, 16f));
        return cold
            ? $"Cold snap: outdoor air drops {magnitude:0} °C for {teller.TemperatureOffsetHoursRemaining:0} hours."
            : $"Heat wave: outdoor air climbs {magnitude:0} °C for {teller.TemperatureOffsetHoursRemaining:0} hours.";
    }
}

/// <summary>Radioactive dust drifts up from the Radiated Deeps; being outdoors poisons the unmasked.</summary>
public sealed class ToxicFalloutIncident : IStoryIncident
{
    public string Id => "toxic-fallout";
    public string DisplayName => "Toxic fallout";
    public IncidentCategory Category => IncidentCategory.MinorThreat;
    public float RefireHours => 36f;
    public bool CanFire(StoryContext context, Storyteller teller) =>
        context.Day >= Storyteller.FirstWeatherDay && context.RadiatedNearby && !teller.FalloutActive && context.Act >= 2; // 0.6.2: from the Descent
    public float ChanceWeight(StoryContext context) => context.Biome == BiomeZone.RadiatedDeeps ? 1.5f : 0.9f;

    public string? TryExecute(IncidentParms parms, Storyteller teller, IStoryHost host)
    {
        teller.FalloutHoursRemaining = MathF.Round(teller.NextRange(3f, 6f));
        return $"Toxic fallout: stay under a roof for {teller.FalloutHoursRemaining:0} hours.";
    }
}

/// <summary>A solar flare scrambles energy weapons for a few hours.</summary>
public sealed class SolarFlareIncident : IStoryIncident
{
    public string Id => "solar-flare";
    public string DisplayName => "Solar flare";
    public IncidentCategory Category => IncidentCategory.MinorThreat;
    public float RefireHours => 40f;
    // 0.6.2: solar flares belong to the Core and after.
    public bool CanFire(StoryContext context, Storyteller teller) => !teller.SolarFlareActive && context.Day >= Storyteller.FirstWeatherDay && context.Act >= 3;
    public float ChanceWeight(StoryContext context) => 0.7f;

    public string? TryExecute(IncidentParms parms, Storyteller teller, IStoryHost host)
    {
        teller.SolarFlareHoursRemaining = MathF.Round(teller.NextRange(2f, 4f));
        return $"Solar flare: energy weapons are offline for {teller.SolarFlareHoursRemaining:0} hours.";
    }
}

/// <summary>An orbital supply pod lands; contents scale gently with threat points.</summary>
public sealed class CargoDropIncident : IStoryIncident
{
    public string Id => "cargo-drop";
    public string DisplayName => "Cargo drop";
    public IncidentCategory Category => IncidentCategory.Boon;
    public float RefireHours => 12f;
    public bool CanFire(StoryContext context, Storyteller teller) => true;
    public float ChanceWeight(StoryContext context) => 1f;
    public string? TryExecute(IncidentParms parms, Storyteller teller, IStoryHost host) =>
        $"Cargo drop: a supply pod delivered {host.DeliverCargo(parms.Biome, parms.ThreatPoints)}.";
}

/// <summary>A lone survivor asks to join the crew.</summary>
public sealed class WandererJoinsIncident : IStoryIncident
{
    public const int MaximumCrew = 5;
    public string Id => "wanderer-joins";
    public string DisplayName => "Wanderer joins";
    public IncidentCategory Category => IncidentCategory.Boon;
    public float RefireHours => 48f;
    public bool CanFire(StoryContext context, Storyteller teller) => context.ColonistCount < MaximumCrew;
    public float ChanceWeight(StoryContext context) => 0.5f;

    public string? TryExecute(IncidentParms parms, Storyteller teller, IStoryHost host)
    {
        if (!host.TryAddWanderer(teller.WanderersJoined, out var name))
            return null;
        teller.WanderersJoined++;
        return $"Wanderer joins: {name} walks into camp and asks to stay.";
    }
}
