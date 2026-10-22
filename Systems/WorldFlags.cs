namespace Rimtera.Systems;

/// <summary>
/// Persisted named flags and counters for world progression (FISHING_EVENTS_WEATHER.md §3.2), e.g.
/// <c>boss.stone_sentinel.down</c>, <c>event.scavenger_raid.cleared</c>, <c>blight.fracture</c>,
/// <c>hearts.broken</c>, <c>npc.medic.unlocked</c>, <c>quests.fishing</c>. A flag is "set" when its value is
/// non-zero. First-set hooks fire exactly once per world (they are not replayed after a load).
/// </summary>
public sealed class WorldFlags
{
    private readonly Dictionary<string, long> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Action>> _firstHooks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised the first time any flag becomes non-zero (name).</summary>
    public event Action<string>? FirstSet;

    public IReadOnlyDictionary<string, long> All => _values;

    public bool IsSet(string name) => _values.TryGetValue(name, out var value) && value != 0;

    public long Get(string name) => _values.GetValueOrDefault(name);

    /// <summary>Sets a flag; returns true (and fires first-clear hooks) only the first time.</summary>
    public bool Set(string name) => SetValue(name, Math.Max(1, Get(name)));

    /// <summary>Adds to a counter; the first increment counts as the first set.</summary>
    public long Increment(string name, long by = 1)
    {
        SetValue(name, Get(name) + by);
        return Get(name);
    }

    private bool SetValue(string name, long value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var wasSet = IsSet(name);
        _values[name] = value;
        if (wasSet || value == 0)
            return false;
        FirstSet?.Invoke(name);
        if (_firstHooks.Remove(name, out var hooks))
            foreach (var hook in hooks)
                hook();
        return true;
    }

    /// <summary>Clears a transient flag (pending triggers); first-clear hooks do not re-arm.</summary>
    public void Clear(string name) => _values.Remove(name);

    /// <summary>Registers a one-time action for the first time <paramref name="name"/> is set (OnFirstClear).</summary>
    public void OnFirstSet(string name, Action action)
    {
        if (IsSet(name))
            return;
        if (!_firstHooks.TryGetValue(name, out var hooks))
            _firstHooks[name] = hooks = new List<Action>();
        hooks.Add(action);
    }

    public Dictionary<string, long> Capture() => new(_values, StringComparer.OrdinalIgnoreCase);

    /// <summary>Restores values silently (no hooks fire; hooks for already-set flags are dropped).</summary>
    public void Restore(IReadOnlyDictionary<string, long>? values)
    {
        _values.Clear();
        if (values is null)
            return;
        foreach (var (name, value) in values)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidDataException("A saved world flag has no name.");
            _values[name] = value;
            if (value != 0)
                _firstHooks.Remove(name);
        }
    }

    /// <summary>"StoneSentinel" → "stone_sentinel" for flag names.</summary>
    public static string Key(object value) =>
        System.Text.RegularExpressions.Regex.Replace(value.ToString()!, "(?<=[a-z0-9])([A-Z])", "_$1").ToLowerInvariant();
}
