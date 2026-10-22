using Rimtera.AI;

namespace Rimtera.Characters;

/// <summary>Memories last for a duration; situational thoughts exist while their condition holds.</summary>
public enum ThoughtKind : byte
{
    Memory,
    Situational
}

/// <summary>Original Rimtera thought catalog. Append only: values are persisted in saves.</summary>
public enum ThoughtId : ushort
{
    // Situational: body.
    MinorPain,
    SeriousPain,
    IntensePain,
    MindShatteringPain,
    Bleeding,
    MissingBodyPart,
    Disfigured,
    Infected,
    RadiationSick,
    Downed,
    // Situational: needs and surroundings.
    Hungry,
    Starving,
    Tired,
    Exhausted,
    Chilly,
    Freezing,
    Sweltering,
    Overheating,
    ToxicFalloutOutdoors,
    ShelteredAtNight,
    ExposedAtNight,
    // Situational: social.
    FriendInColony,
    RivalInColony,
    LoverNearby,
    // Memories: events.
    AteNanoBanana,
    RestedWell,
    KilledCreature,
    BossDefeated,
    RaidSurvived,
    CargoDropArrived,
    Catharsis,
    FriendDied,
    LoverDied,
    SiblingDied,
    RivalDied,
    CrewmateDied,
    // Memories: social interactions (carry an opinion offset toward the other pawn).
    Chitchat,
    DeepTalk,
    KindWords,
    Slighted,
    Insulted,
    Rebuffed,
    NewLovers,
    SocialFight,
    LostLimb
}

/// <summary>
/// Static definition of a thought. Mood offsets are fractions of the 0..1 mood bar; opinion offsets are
/// on the -100..100 opinion scale. Each extra stacked copy contributes <see cref="StackFalloff"/> times the
/// previous one; a new copy younger than <see cref="MergeWindowHours"/> refreshes the existing one instead.
/// </summary>
public sealed record ThoughtDefinition(
    ThoughtId Id,
    string Label,
    string Description,
    ThoughtKind Kind,
    float MoodOffset,
    float DurationHours = 0f,
    int StackLimit = 1,
    float StackFalloff = 0.5f,
    float OpinionOffset = 0f,
    float MergeWindowHours = 0f)
{
    public bool IsSocial => OpinionOffset != 0f;
}

public static class ThoughtCatalog
{
    private static readonly Dictionary<ThoughtId, ThoughtDefinition> _table = new[]
    {
        S(ThoughtId.MinorPain, "Aching", "Something hurts, and it will not stop mentioning it.", -0.04f),
        S(ThoughtId.SeriousPain, "Serious pain", "Every movement comes with a cost.", -0.08f),
        S(ThoughtId.IntensePain, "Intense pain", "The pain has become the whole world.", -0.13f),
        S(ThoughtId.MindShatteringPain, "Unbearable pain", "Thinking past the pain is impossible.", -0.20f),
        S(ThoughtId.Bleeding, "Bleeding", "Watching their own blood leave is unsettling.", -0.05f),
        S(ThoughtId.MissingBodyPart, "Missing a body part", "Part of them is simply gone.", -0.06f, stack: 4, falloff: 0.7f),
        S(ThoughtId.Disfigured, "Disfigured", "Their face no longer looks like their own.", -0.05f),
        S(ThoughtId.Infected, "Feverish", "A wound has turned hot and wrong.", -0.06f),
        S(ThoughtId.RadiationSick, "Radiation sick", "Nausea and a metallic taste that never fades.", -0.06f),
        S(ThoughtId.Downed, "Helpless", "Unable to stand while the world carries on.", -0.06f),
        S(ThoughtId.Hungry, "Hungry", "A hollow, distracting hunger.", -0.04f),
        S(ThoughtId.Starving, "Starving", "The body is eating itself.", -0.15f),
        S(ThoughtId.Tired, "Tired", "Eyelids heavy, thoughts slow.", -0.02f),
        S(ThoughtId.Exhausted, "Exhausted", "Running on nothing at all.", -0.06f),
        S(ThoughtId.Chilly, "Chilly", "Can't quite get warm.", -0.03f),
        S(ThoughtId.Freezing, "Freezing", "The cold has teeth.", -0.09f),
        S(ThoughtId.Sweltering, "Sweltering", "Sticky, sweaty, irritable.", -0.03f),
        S(ThoughtId.Overheating, "Overheating", "The heat is pressing the life out of them.", -0.09f),
        S(ThoughtId.ToxicFalloutOutdoors, "Toxic fallout", "Out in the poisoned air, breathing it in.", -0.06f),
        S(ThoughtId.ShelteredAtNight, "Sheltered for the night", "Walls between them and the dark.", 0.03f),
        S(ThoughtId.ExposedAtNight, "Exposed at night", "Out in the open after dark on an alien world.", -0.03f),
        S(ThoughtId.FriendInColony, "Friend in the colony", "Someone here has their back.", 0.02f, stack: 3, falloff: 0.6f),
        S(ThoughtId.RivalInColony, "Rival in the colony", "Has to share the colony with someone they can't stand.", -0.03f, stack: 3, falloff: 0.6f),
        S(ThoughtId.LoverNearby, "Lover in the colony", "Not alone out here.", 0.04f),

        M(ThoughtId.AteNanoBanana, "Ate a nano-banana", "Sweet, starchy, faintly glowing. Good.", 0.03f, 6f, stack: 2, merge: 1f),
        M(ThoughtId.RestedWell, "Rested well", "Woke up feeling human again.", 0.04f, 10f, merge: 6f),
        M(ThoughtId.KilledCreature, "Killed a creature", "One fewer thing out there that wants them dead.", 0.02f, 8f, stack: 5, falloff: 0.6f),
        M(ThoughtId.BossDefeated, "A titan has fallen", "The colony brought down something enormous.", 0.10f, 48f, stack: 2, falloff: 0.5f),
        M(ThoughtId.RaidSurvived, "Survived a raid", "They came, and the colony is still here.", 0.06f, 24f, stack: 2, falloff: 0.5f),
        M(ThoughtId.CargoDropArrived, "Supplies arrived", "A cargo pod full of things they needed.", 0.04f, 18f, stack: 2, falloff: 0.5f),
        M(ThoughtId.Catharsis, "Catharsis", "The storm has passed. Things feel lighter.", 0.10f, 20f),
        M(ThoughtId.FriendDied, "Friend died", "A friend is gone and will not come back.", -0.10f, 96f, stack: 3, falloff: 0.7f),
        M(ThoughtId.LoverDied, "Lover died", "The person they loved is gone.", -0.20f, 144f, stack: 2, falloff: 0.7f),
        M(ThoughtId.SiblingDied, "Sibling died", "Family, lost out here.", -0.15f, 120f, stack: 2, falloff: 0.7f),
        M(ThoughtId.RivalDied, "Rival died", "A guilty kind of relief.", 0.04f, 48f, stack: 2, falloff: 0.5f),
        M(ThoughtId.CrewmateDied, "Crewmate died", "The colony is smaller now.", -0.04f, 48f, stack: 4, falloff: 0.6f),

        M(ThoughtId.Chitchat, "Chatted", "A little small talk.", 0.01f, 12f, stack: 12, falloff: 0.9f, opinion: 5f),
        M(ThoughtId.DeepTalk, "Deep talk", "Talked about things that matter.", 0.02f, 24f, stack: 5, falloff: 0.8f, opinion: 12f),
        M(ThoughtId.KindWords, "Kind words", "Someone said something genuinely kind.", 0.04f, 18f, stack: 4, falloff: 0.7f, opinion: 10f),
        M(ThoughtId.Slighted, "Slighted", "A cutting little remark.", -0.02f, 18f, stack: 5, falloff: 0.8f, opinion: -8f),
        M(ThoughtId.Insulted, "Insulted", "Someone went out of their way to be cruel.", -0.05f, 24f, stack: 5, falloff: 0.8f, opinion: -18f),
        M(ThoughtId.Rebuffed, "Rebuffed", "Their affection was turned away.", -0.05f, 24f, stack: 3, falloff: 0.6f, opinion: -10f),
        M(ThoughtId.NewLovers, "New lovers", "Something wonderful has started.", 0.10f, 72f, opinion: 20f),
        M(ThoughtId.SocialFight, "Had a fight", "Fists flew. Nobody won.", -0.06f, 48f, stack: 3, falloff: 0.6f, opinion: -25f),
        M(ThoughtId.LostLimb, "Lost a body part", "It happened so fast.", -0.08f, 72f, stack: 3, falloff: 0.7f)
    }.ToDictionary(definition => definition.Id);

    private static ThoughtDefinition S(ThoughtId id, string label, string description, float mood, int stack = 1, float falloff = 0.5f) =>
        new(id, label, description, ThoughtKind.Situational, mood, 0f, stack, falloff);

    private static ThoughtDefinition M(ThoughtId id, string label, string description, float mood, float hours,
        int stack = 1, float falloff = 0.5f, float opinion = 0f, float merge = 0f) =>
        new(id, label, description, ThoughtKind.Memory, mood, hours, stack, falloff, opinion, merge);

    public static IEnumerable<ThoughtDefinition> All => _table.Values.OrderBy(definition => definition.Id);

    public static ThoughtDefinition Get(ThoughtId id) =>
        _table.TryGetValue(id, out var definition) ? definition : throw new ArgumentOutOfRangeException(nameof(id));

    public static bool Contains(ThoughtId id) => _table.ContainsKey(id);
}

/// <summary>One remembered event, optionally about another pawn (identified by social key).</summary>
public sealed class ThoughtMemory
{
    internal ThoughtMemory(ThoughtDefinition definition, int otherKey, string? otherName, float scale)
    {
        Definition = definition;
        OtherKey = otherKey;
        OtherName = otherName;
        Scale = scale;
    }

    public ThoughtDefinition Definition { get; }
    public ThoughtId Id => Definition.Id;
    /// <summary>Social key of the pawn this memory is about, or -1.</summary>
    public int OtherKey { get; }
    public string? OtherName { get; }
    /// <summary>Strength multiplier (e.g. grief scaled by how close they were).</summary>
    public float Scale { get; }
    public float AgeHours { get; internal set; }
    public float RemainingHours => Math.Max(0f, Definition.DurationHours - AgeHours);
    public bool Expired => AgeHours >= Definition.DurationHours;
    /// <summary>Social opinion fades from full strength to a quarter over the memory's life.</summary>
    public float OpinionFade => Math.Clamp(1f - 0.75f * AgeHours / Math.Max(0.01f, Definition.DurationHours), 0.25f, 1f);
}

/// <summary>A thought row for the UI: one stacked group with its combined offset.</summary>
public readonly record struct ThoughtView(
    ThoughtId Id,
    string Label,
    string Description,
    float MoodOffset,
    float? RemainingHours,
    int Count,
    string? OtherName,
    bool Situational);

/// <summary>Persisted memory.</summary>
public readonly record struct MemorySnapshot(ThoughtId Id, float AgeHours, int OtherKey, string? OtherName, float Scale);

/// <summary>
/// Holds a pawn's memories and current situational thoughts and derives the total mood offset with
/// stacking limits, diminishing returns and merging, plus per-pawn social opinion offsets.
/// </summary>
public sealed class ThoughtTracker
{
    private readonly List<ThoughtMemory> _memories = new();
    private readonly List<(ThoughtDefinition Definition, float Scale, string? OtherName)> _situational = new();

    public IReadOnlyList<ThoughtMemory> Memories => _memories;
    public IEnumerable<ThoughtId> SituationalIds => _situational.Select(item => item.Definition.Id);

    /// <summary>Adds a memory, merging into a fresh copy or replacing the oldest copy at the stack limit.</summary>
    public ThoughtMemory AddMemory(ThoughtId id, int otherKey = -1, string? otherName = null, float scale = 1f)
    {
        var definition = ThoughtCatalog.Get(id);
        if (definition.Kind != ThoughtKind.Memory)
            throw new ArgumentException($"{id} is not a memory thought.", nameof(id));
        if (!float.IsFinite(scale) || scale <= 0f)
            scale = 1f;

        var same = _memories.Where(memory => memory.Id == id && memory.OtherKey == otherKey).ToList();
        var fresh = same.FirstOrDefault(memory => memory.AgeHours < definition.MergeWindowHours);
        if (fresh is not null)
        {
            fresh.AgeHours = 0f;
            return fresh;
        }
        if (same.Count >= definition.StackLimit)
        {
            var oldest = same.OrderByDescending(memory => memory.AgeHours).First();
            _memories.Remove(oldest);
        }
        var created = new ThoughtMemory(definition, otherKey, otherName, scale);
        _memories.Add(created);
        return created;
    }

    public bool HasMemory(ThoughtId id, int otherKey = int.MinValue) =>
        _memories.Any(memory => memory.Id == id && (otherKey == int.MinValue || memory.OtherKey == otherKey));

    public int RemoveMemories(ThoughtId id) => _memories.RemoveAll(memory => memory.Id == id);

    /// <summary>Ages memories and drops expired ones.</summary>
    public void Advance(float hours)
    {
        if (!float.IsFinite(hours) || hours < 0f)
            throw new ArgumentOutOfRangeException(nameof(hours));
        foreach (var memory in _memories)
            memory.AgeHours += hours;
        _memories.RemoveAll(memory => memory.Expired);
    }

    /// <summary>Replaces the situational set (recomputed on the slow story cadence).</summary>
    public void SetSituational(IEnumerable<(ThoughtId Id, float Scale, string? OtherName)> thoughts)
    {
        _situational.Clear();
        foreach (var (id, scale, other) in thoughts)
        {
            var definition = ThoughtCatalog.Get(id);
            if (definition.Kind != ThoughtKind.Situational)
                throw new ArgumentException($"{id} is not a situational thought.", nameof(thoughts));
            var copies = _situational.Count(item => item.Definition.Id == id);
            if (copies < definition.StackLimit)
                _situational.Add((definition, scale, other));
        }
    }

    /// <summary>Sum of all thought mood offsets, with each extra stacked copy diminished.</summary>
    public float TotalMoodOffset => CurrentThoughts.Sum(view => view.MoodOffset);

    /// <summary>Grouped thought rows, strongest first.</summary>
    public IReadOnlyList<ThoughtView> CurrentThoughts
    {
        get
        {
            var rows = new List<ThoughtView>();
            foreach (var group in _memories.GroupBy(memory => memory.Id))
            {
                var definition = ThoughtCatalog.Get(group.Key);
                var ordered = group.OrderBy(memory => memory.AgeHours).ToArray();
                var offset = 0f;
                var factor = 1f;
                foreach (var memory in ordered)
                {
                    offset += definition.MoodOffset * memory.Scale * factor;
                    factor *= definition.StackFalloff;
                }
                var others = ordered.Select(memory => memory.OtherName).Where(name => name is not null).Distinct().ToArray();
                rows.Add(new ThoughtView(definition.Id, definition.Label, definition.Description, offset,
                    ordered.Min(memory => memory.RemainingHours), ordered.Length,
                    others.Length == 0 ? null : string.Join(", ", others), false));
            }
            foreach (var group in _situational.GroupBy(item => item.Definition.Id))
            {
                var definition = ThoughtCatalog.Get(group.Key);
                var offset = 0f;
                var factor = 1f;
                foreach (var item in group)
                {
                    offset += definition.MoodOffset * item.Scale * factor;
                    factor *= definition.StackFalloff;
                }
                var others = group.Select(item => item.OtherName).Where(name => name is not null).Distinct().ToArray();
                rows.Add(new ThoughtView(definition.Id, definition.Label, definition.Description, offset, null,
                    group.Count(), others.Length == 0 ? null : string.Join(", ", others), true));
            }
            return rows.OrderBy(row => -MathF.Abs(row.MoodOffset)).ToArray();
        }
    }

    /// <summary>Σ fading social-memory opinion offsets this pawn holds about another pawn.</summary>
    public float OpinionOffsetToward(int otherKey)
    {
        var total = 0f;
        foreach (var group in _memories.Where(memory => memory.OtherKey == otherKey && memory.Definition.IsSocial)
                     .GroupBy(memory => memory.Id))
        {
            var factor = 1f;
            foreach (var memory in group.OrderBy(memory => memory.AgeHours))
            {
                total += memory.Definition.OpinionOffset * memory.Scale * memory.OpinionFade * factor;
                factor *= memory.Definition.StackFalloff;
            }
        }
        return total;
    }

    public MemorySnapshot[] Capture() => _memories
        .Select(memory => new MemorySnapshot(memory.Id, memory.AgeHours, memory.OtherKey, memory.OtherName, memory.Scale))
        .ToArray();

    public void Restore(IEnumerable<MemorySnapshot>? memories)
    {
        _memories.Clear();
        foreach (var saved in memories ?? Array.Empty<MemorySnapshot>())
        {
            if (!ThoughtCatalog.Contains(saved.Id) || ThoughtCatalog.Get(saved.Id).Kind != ThoughtKind.Memory ||
                !float.IsFinite(saved.AgeHours) || !float.IsFinite(saved.Scale))
                throw new InvalidDataException("A saved memory is invalid.");
            var memory = new ThoughtMemory(ThoughtCatalog.Get(saved.Id), saved.OtherKey, saved.OtherName,
                saved.Scale <= 0f ? 1f : saved.Scale)
            {
                AgeHours = Math.Max(0f, saved.AgeHours)
            };
            if (!memory.Expired)
                _memories.Add(memory);
        }
    }
}

/// <summary>Current mood easing toward its target (needs baseline + Σ thoughts).</summary>
public sealed class MoodState
{
    /// <summary>Hours for the mood to close ~63% of the gap to its target.</summary>
    public const float EaseHours = 2f;

    public MoodState(float initial)
    {
        Current = Math.Clamp(float.IsFinite(initial) ? initial : 0.5f, 0f, 1f);
        Target = Current;
        Baseline = Current;
    }

    public float Current { get; private set; }
    public float Target { get; private set; }
    /// <summary>Needs + trait/backstory part of the target, before thoughts.</summary>
    public float Baseline { get; private set; }
    public float ThoughtOffset { get; private set; }

    public void SetTarget(float baseline, float thoughtOffset)
    {
        Baseline = Math.Clamp(float.IsFinite(baseline) ? baseline : 0.5f, 0f, 1f);
        ThoughtOffset = float.IsFinite(thoughtOffset) ? thoughtOffset : 0f;
        Target = Math.Clamp(Baseline + ThoughtOffset, 0f, 1f);
    }

    public void Advance(float hours)
    {
        if (!float.IsFinite(hours) || hours <= 0f)
            return;
        var blend = 1f - MathF.Exp(-hours / EaseHours);
        Current = Math.Clamp(Current + (Target - Current) * blend, 0f, 1f);
    }

    /// <summary>Sets the current mood directly (saves, tests, scripted events).</summary>
    public void Force(float value) => Current = Math.Clamp(float.IsFinite(value) ? value : Current, 0f, 1f);
}

/// <summary>
/// Everything that makes up a pawn's inner life: thoughts, mood, the mental breaker, direct relations
/// and the social interaction timer. Shared by the player character and colonist NPCs.
/// </summary>
public sealed class ColonistMind
{
    public ColonistMind(CharacterProfile profile, float initialMood)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Mood = new MoodState(initialMood);
        Breaker = new MentalBreaker(MentalBreaker.ThresholdsFor(profile.Traits));
    }

    public ThoughtTracker Thoughts { get; } = new();
    public MoodState Mood { get; }
    public MentalBreaker Breaker { get; }
    public RelationsTracker Relations { get; } = new();
    /// <summary>Game hour at which this pawn may start its next social interaction.</summary>
    public float NextInteractionHour { get; set; }

    public IReadOnlyList<ThoughtView> CurrentThoughts => Thoughts.CurrentThoughts;

    /// <summary>Recomputes the mood target from the needs baseline and thoughts, then eases toward it.</summary>
    public void UpdateMood(SurvivalNeeds needs, ColonistStats stats, float hours)
    {
        Mood.SetTarget(ColonistMoodCalculator.Calculate(needs, stats), Thoughts.TotalMoodOffset);
        Mood.Advance(hours);
    }
}
