namespace Rimtera.Systems;

public enum WorldEventBoundary
{
    Dusk,
    Dawn
}

public sealed record WorldEventDefinition(
    string Id,
    WorldEventBoundary Boundary,
    float ChancePerBoundary,
    float DurationHours,
    float SpawnAttemptMultiplier = 1f,
    float PopulationCapMultiplier = 1f,
    bool TownsSeekShelter = false,
    bool RequiresProgression = false,
    string? RequiredBiomeTag = null,
    string? DisplayName = null);

public readonly record struct WorldEventContext(
    bool ProgressionUnlocked = false,
    IReadOnlySet<string>? BiomeTags = null);

public readonly record struct WorldEventModifiers(
    float SpawnAttemptMultiplier,
    float PopulationCapMultiplier,
    bool TownsSeekShelter);

public sealed record WorldEventTickResult(
    IReadOnlyList<string> Started,
    IReadOnlyList<string> Ended);

public readonly record struct ActiveWorldEvent(string Id, float RemainingHours);

/// <summary>
/// Seedable, configurable world-event scheduling. Probabilities and effects are
/// project data; they are intentionally not copied from a reference game's tables.
/// </summary>
public sealed class EventManager
{
    private readonly Dictionary<string, WorldEventDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _random;

    public EventManager(int seed) => _random = new Random(seed);

    public IReadOnlyList<ActiveWorldEvent> ActiveEvents => _active
        .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
        .Select(pair => new ActiveWorldEvent(pair.Key, pair.Value))
        .ToArray();

    public void Register(WorldEventDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Validate(definition);
        if (!_definitions.TryAdd(definition.Id, definition))
            throw new InvalidOperationException($"World event '{definition.Id}' is already registered.");
    }

    /// <summary>
    /// Advances active durations, then performs at most one eligible random start
    /// on the supplied dawn/dusk boundary.
    /// </summary>
    public WorldEventTickResult Advance(
        float elapsedHours,
        WorldEventBoundary? boundary,
        in WorldEventContext context)
    {
        if (!float.IsFinite(elapsedHours) || elapsedHours < 0f)
            throw new ArgumentOutOfRangeException(nameof(elapsedHours));

        var ended = new List<string>();
        foreach (var id in _active.Keys.ToArray())
        {
            var remaining = _active[id] - elapsedHours;
            if (remaining <= 0f)
            {
                _active.Remove(id);
                ended.Add(id);
            }
            else
            {
                _active[id] = remaining;
            }
        }

        var started = new List<string>(capacity: 1);
        if (boundary is { } moment)
        {
            foreach (var definition in _definitions.Values)
            {
                if (definition.Boundary != moment || _active.ContainsKey(definition.Id) || !IsEligible(definition, context))
                    continue;

                if (_random.NextDouble() >= definition.ChancePerBoundary)
                    continue;

                _active.Add(definition.Id, definition.DurationHours);
                started.Add(definition.Id);
                break;
            }
        }

        return new WorldEventTickResult(started, ended);
    }

    /// <summary>Player-facing name for an event ID; unknown or unnamed events fall back to the ID.</summary>
    public string GetDisplayName(string id) =>
        _definitions.TryGetValue(id, out var definition) && !string.IsNullOrWhiteSpace(definition.DisplayName)
            ? definition.DisplayName
            : id;

    /// <summary>Starts a registered event through a scripted or administrative route.</summary>
    public bool TryStart(string id)
    {
        if (!_definitions.TryGetValue(id, out var definition) || _active.ContainsKey(definition.Id))
            return false;

        _active.Add(definition.Id, definition.DurationHours);
        return true;
    }

    /// <summary>Multipliers can be consumed by a future entity-spawn scheduler.</summary>
    public WorldEventModifiers GetSpawnModifiers()
    {
        var spawnMultiplier = 1f;
        var populationMultiplier = 1f;
        var shelter = false;

        foreach (var (id, _) in _active)
        {
            var definition = _definitions[id];
            spawnMultiplier *= definition.SpawnAttemptMultiplier;
            populationMultiplier *= definition.PopulationCapMultiplier;
            shelter |= definition.TownsSeekShelter;
        }

        return new WorldEventModifiers(spawnMultiplier, populationMultiplier, shelter);
    }

    private static bool IsEligible(WorldEventDefinition definition, in WorldEventContext context)
    {
        if (definition.RequiresProgression && !context.ProgressionUnlocked)
            return false;

        return definition.RequiredBiomeTag is null ||
            (context.BiomeTags?.Contains(definition.RequiredBiomeTag) ?? false);
    }

    private static void Validate(WorldEventDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Id))
            throw new ArgumentException("World event ID cannot be empty.", nameof(definition));
        if (!float.IsFinite(definition.ChancePerBoundary) || definition.ChancePerBoundary is < 0f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(definition), "Chance must be between zero and one.");
        if (!float.IsFinite(definition.DurationHours) || definition.DurationHours <= 0f)
            throw new ArgumentOutOfRangeException(nameof(definition), "Duration must be finite and positive.");
        if (!float.IsFinite(definition.SpawnAttemptMultiplier) || definition.SpawnAttemptMultiplier <= 0f ||
            !float.IsFinite(definition.PopulationCapMultiplier) || definition.PopulationCapMultiplier <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(definition), "Spawn multipliers must be finite and positive.");
        }
    }
}
