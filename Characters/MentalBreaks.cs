namespace Rimtera.Characters;

public enum MentalBreakSeverity : byte
{
    None,
    Minor,
    Major,
    Extreme
}

/// <summary>Crew break behaviours. Append only: values are persisted in saves.</summary>
public enum MentalBreakKind : byte
{
    None,
    SadWander,
    HideInRoom,
    FoodBinge,
    Tantrum,
    Daze
}

/// <summary>Mood levels below which each break severity becomes possible.</summary>
public readonly record struct MentalBreakThresholds(float Minor, float Major, float Extreme);

/// <summary>Persisted breaker state.</summary>
public readonly record struct MentalBreakSnapshot(
    MentalBreakKind Kind,
    MentalBreakSeverity Severity,
    float HoursRemaining,
    float HoursBelowMinor,
    float HoursBelowMajor,
    float HoursBelowExtreme);

/// <summary>
/// Decides when low mood turns into a mental break. Mood must stay below a threshold for a gate time,
/// then a seeded roll on the story cadence (chance grows with elapsed hours and severity) may start a
/// break. Breaks run for a duration, then end; the caller adds the catharsis memory. Original tuning.
/// </summary>
public sealed class MentalBreaker
{
    public const float BaseMinor = 0.30f;
    public const float BaseMajor = 0.18f;
    public const float BaseExtreme = 0.08f;
    public const float MinorGateHours = 1.0f;
    public const float MajorGateHours = 0.6f;
    public const float ExtremeGateHours = 0.3f;

    public MentalBreaker(MentalBreakThresholds thresholds) => Thresholds = thresholds;

    public MentalBreakThresholds Thresholds { get; }
    public MentalBreakKind ActiveKind { get; private set; }
    public MentalBreakSeverity ActiveSeverity { get; private set; }
    public float HoursRemaining { get; private set; }
    public float HoursBelowMinor { get; private set; }
    public float HoursBelowMajor { get; private set; }
    public float HoursBelowExtreme { get; private set; }
    public bool IsBreaking => ActiveKind != MentalBreakKind.None;
    /// <summary>The last mood seen by <see cref="Track"/>.</summary>
    public float LastMood { get; private set; } = 1f;

    /// <summary>Which break band the current mood sits in (None when above the minor threshold).</summary>
    public MentalBreakSeverity Risk => LastMood < Thresholds.Extreme ? MentalBreakSeverity.Extreme
        : LastMood < Thresholds.Major ? MentalBreakSeverity.Major
        : LastMood < Thresholds.Minor ? MentalBreakSeverity.Minor
        : MentalBreakSeverity.None;

    /// <summary>Below the minor threshold: the player sees an "on the edge" warning.</summary>
    public bool OnTheEdge => Risk != MentalBreakSeverity.None;

    public string StateLabel => IsBreaking
        ? $"{Describe(ActiveKind)} ({HoursRemaining:0.0}h)"
        : Risk switch
        {
            MentalBreakSeverity.Extreme => "On the edge: extreme break risk",
            MentalBreakSeverity.Major => "On the edge: major break risk",
            MentalBreakSeverity.Minor => "On the edge: minor break risk",
            _ => "Steady"
        };

    public static MentalBreakThresholds ThresholdsFor(IEnumerable<ColonistTraitId>? traits)
    {
        var shift = 0f;
        foreach (var trait in traits ?? Array.Empty<ColonistTraitId>())
        {
            shift += trait switch
            {
                ColonistTraitId.IronWilled => -0.07f,
                ColonistTraitId.Nervous => 0.07f,
                ColonistTraitId.Psychopath => -0.02f,
                _ => 0f
            };
        }
        return new MentalBreakThresholds(
            Math.Clamp(BaseMinor + shift, 0.05f, 0.6f),
            Math.Clamp(BaseMajor + shift, 0.03f, 0.45f),
            Math.Clamp(BaseExtreme + shift, 0.01f, 0.3f));
    }

    public static string Describe(MentalBreakKind kind) => kind switch
    {
        MentalBreakKind.SadWander => "Sad wandering",
        MentalBreakKind.HideInRoom => "Hiding away",
        MentalBreakKind.FoodBinge => "Food binge",
        MentalBreakKind.Tantrum => "Tantrum",
        MentalBreakKind.Daze => "Dazed",
        _ => "None"
    };

    /// <summary>Accumulates time spent below each threshold; resets a counter as soon as mood rises above it.</summary>
    public void Track(float mood, float hours)
    {
        LastMood = Math.Clamp(float.IsFinite(mood) ? mood : 1f, 0f, 1f);
        if (!float.IsFinite(hours) || hours < 0f)
            return;
        HoursBelowMinor = LastMood < Thresholds.Minor ? HoursBelowMinor + hours : 0f;
        HoursBelowMajor = LastMood < Thresholds.Major ? HoursBelowMajor + hours : 0f;
        HoursBelowExtreme = LastMood < Thresholds.Extreme ? HoursBelowExtreme + hours : 0f;
    }

    /// <summary>
    /// Rolls for a break after <see cref="Track"/>. Returns the started kind, or None. Deterministic for a
    /// seeded random stream. Only the worst gated severity is rolled.
    /// </summary>
    public MentalBreakKind TryStartBreak(Random random, float hours, bool canBreak = true, bool hasFood = true)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (IsBreaking || !canBreak || !float.IsFinite(hours) || hours <= 0f)
            return MentalBreakKind.None;

        var (severity, rate) =
            HoursBelowExtreme >= ExtremeGateHours ? (MentalBreakSeverity.Extreme, 1.0f) :
            HoursBelowMajor >= MajorGateHours ? (MentalBreakSeverity.Major, 0.5f) :
            HoursBelowMinor >= MinorGateHours ? (MentalBreakSeverity.Minor, 0.25f) :
            (MentalBreakSeverity.None, 0f);
        if (severity == MentalBreakSeverity.None)
            return MentalBreakKind.None;
        var chance = 1f - MathF.Exp(-rate * hours);
        if (random.NextDouble() >= chance)
            return MentalBreakKind.None;

        MentalBreakKind[] options = severity switch
        {
            MentalBreakSeverity.Minor => [MentalBreakKind.SadWander, MentalBreakKind.Daze],
            MentalBreakSeverity.Major => hasFood
                ? [MentalBreakKind.HideInRoom, MentalBreakKind.FoodBinge, MentalBreakKind.SadWander]
                : [MentalBreakKind.HideInRoom, MentalBreakKind.SadWander],
            _ => hasFood ? [MentalBreakKind.Tantrum, MentalBreakKind.FoodBinge] : [MentalBreakKind.Tantrum]
        };
        var kind = options[random.Next(options.Length)];
        var baseHours = severity switch
        {
            MentalBreakSeverity.Minor => 3f,
            MentalBreakSeverity.Major => 4.5f,
            _ => 6f
        };
        Start(kind, severity, baseHours * (0.75f + 0.5f * (float)random.NextDouble()));
        return kind;
    }

    /// <summary>Starts a break directly (scripted events, tests).</summary>
    public void Start(MentalBreakKind kind, MentalBreakSeverity severity, float hours)
    {
        if (kind == MentalBreakKind.None || !Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ActiveKind = kind;
        ActiveSeverity = severity == MentalBreakSeverity.None ? MentalBreakSeverity.Minor : severity;
        HoursRemaining = Math.Max(0.1f, float.IsFinite(hours) ? hours : 1f);
    }

    /// <summary>Counts down an active break. Returns true on the call where the break ends.</summary>
    public bool Advance(float hours)
    {
        if (!IsBreaking || !float.IsFinite(hours) || hours <= 0f)
            return false;
        HoursRemaining -= hours;
        if (HoursRemaining > 0f)
            return false;
        End();
        return true;
    }

    /// <summary>Ends any break immediately (e.g. the pawn was downed).</summary>
    public void End()
    {
        ActiveKind = MentalBreakKind.None;
        ActiveSeverity = MentalBreakSeverity.None;
        HoursRemaining = 0f;
        HoursBelowMinor = 0f;
        HoursBelowMajor = 0f;
        HoursBelowExtreme = 0f;
    }

    public MentalBreakSnapshot Capture() =>
        new(ActiveKind, ActiveSeverity, HoursRemaining, HoursBelowMinor, HoursBelowMajor, HoursBelowExtreme);

    public void Restore(MentalBreakSnapshot snapshot)
    {
        if (!Enum.IsDefined(snapshot.Kind) || !Enum.IsDefined(snapshot.Severity) ||
            !float.IsFinite(snapshot.HoursRemaining) || !float.IsFinite(snapshot.HoursBelowMinor) ||
            !float.IsFinite(snapshot.HoursBelowMajor) || !float.IsFinite(snapshot.HoursBelowExtreme))
            throw new InvalidDataException("The saved mental-break state is invalid.");
        ActiveKind = snapshot.Kind;
        ActiveSeverity = snapshot.Kind == MentalBreakKind.None ? MentalBreakSeverity.None : snapshot.Severity;
        HoursRemaining = snapshot.Kind == MentalBreakKind.None ? 0f : Math.Max(0.1f, snapshot.HoursRemaining);
        HoursBelowMinor = Math.Max(0f, snapshot.HoursBelowMinor);
        HoursBelowMajor = Math.Max(0f, snapshot.HoursBelowMajor);
        HoursBelowExtreme = Math.Max(0f, snapshot.HoursBelowExtreme);
    }
}
