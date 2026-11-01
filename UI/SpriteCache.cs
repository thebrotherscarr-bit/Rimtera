namespace Rimtera.UI;

/// <summary>
/// PE-2: a least-recently-used cache with a byte budget, used by <see cref="SpriteAtlas"/> for decoded and scaled
/// bitmaps. It knows nothing of System.Drawing, so the bookkeeping is testable on any platform.
/// <para>
/// Frames pin: an entry used since the last <see cref="BeginFrame"/> is never evicted, so a bitmap handed out
/// earlier in a draw stays valid for the rest of that draw. Evicted values are not disposed at once either: they
/// are retired and released <see cref="RetireMilliseconds"/> later (and at least one frame on), because a few callers
/// (gore chunks cut from a creature frame) keep a bitmap for a second or so.
/// </para>
/// </summary>
internal sealed class SpriteCache<TKey, TValue> where TKey : notnull where TValue : class
{
    private sealed class Entry(TKey key, TValue value, long bytes, long frame)
    {
        public readonly TKey Key = key;
        public readonly TValue Value = value;
        public readonly long Bytes = bytes;
        public long Frame = frame;
    }

    private readonly Dictionary<TKey, LinkedListNode<Entry>> _entries;
    private readonly LinkedList<Entry> _order = new();
    private readonly Queue<(TValue Value, long Frame, long Milliseconds)> _retired = new();
    private readonly Action<TValue>? _release;
    private readonly Func<long> _clock;
    private long _frame = 1;

    /// <param name="clock">Milliseconds now (tests pass their own); defaults to <see cref="Environment.TickCount64"/>.</param>
    public SpriteCache(long budgetBytes, Action<TValue>? release = null, IEqualityComparer<TKey>? comparer = null, Func<long>? clock = null)
    {
        BudgetBytes = budgetBytes;
        _release = release;
        _clock = clock ?? (() => Environment.TickCount64);
        _entries = new Dictionary<TKey, LinkedListNode<Entry>>(comparer);
    }

    /// <summary>
    /// How long an evicted value waits before it is released: longer than a gore chunk (the longest holder, 1.5 s)
    /// lives, and short enough that retired bitmaps stay a small share of memory. Real time, not frames, so a
    /// 144 Hz screen does not shorten it.
    /// </summary>
    public const long RetireMilliseconds = 3000;

    public long BudgetBytes { get; }
    public long Bytes { get; private set; }
    public int Count => _entries.Count;
    public int RetiredCount => _retired.Count;
    /// <summary>Total values ever added (decodes or scalings).</summary>
    public long Loads { get; private set; }
    public long Evictions { get; private set; }

    public bool ContainsKey(TKey key) => _entries.ContainsKey(key);

    /// <summary>Looks a value up and marks it used (and pinned) this frame.</summary>
    public bool TryGet(TKey key, out TValue value)
    {
        if (_entries.TryGetValue(key, out var node))
        {
            Touch(node);
            value = node.Value.Value;
            return true;
        }
        value = null!;
        return false;
    }

    /// <summary>Adds (or replaces) a value pinned to this frame, then evicts unpinned entries over the budget.</summary>
    public void Add(TKey key, TValue value, long bytes)
    {
        if (_entries.TryGetValue(key, out var old))
            Remove(old);
        var node = _order.AddLast(new Entry(key, value, Math.Max(0, bytes), _frame));
        _entries[key] = node;
        Bytes += node.Value.Bytes;
        Loads++;
        Trim();
    }

    /// <summary>
    /// Starts a new draw: entries used before now may be evicted, and values retired long enough ago are released.
    /// Anything the last frame left over the budget (everything was pinned) is trimmed here.
    /// </summary>
    public void BeginFrame()
    {
        _frame++;
        var now = _clock();
        while (_retired.Count > 0 && _frame > _retired.Peek().Frame && now - _retired.Peek().Milliseconds >= RetireMilliseconds)
            _release?.Invoke(_retired.Dequeue().Value);
        Trim();
    }

    /// <summary>Retires every entry (the zoom changed, so every scaled copy is the wrong size).</summary>
    public void Clear()
    {
        while (_order.First is { } first)
            Remove(first, retire: true);
    }

    /// <summary>Releases everything now, retired values included.</summary>
    public void ReleaseAll()
    {
        foreach (var entry in _order)
            _release?.Invoke(entry.Value);
        while (_retired.Count > 0)
            _release?.Invoke(_retired.Dequeue().Value);
        _order.Clear();
        _entries.Clear();
        Bytes = 0;
    }

    private void Touch(LinkedListNode<Entry> node)
    {
        node.Value.Frame = _frame;
        if (node != _order.Last)
        {
            _order.Remove(node);
            _order.AddLast(node);
        }
    }

    private void Trim()
    {
        // The list runs from least to most recently used, so once the head is pinned every entry is.
        while (Bytes > BudgetBytes && _order.First is { } first && first.Value.Frame < _frame)
        {
            Remove(first, retire: true);
            Evictions++;
        }
    }

    private void Remove(LinkedListNode<Entry> node, bool retire = true)
    {
        _order.Remove(node);
        _entries.Remove(node.Value.Key);
        Bytes -= node.Value.Bytes;
        if (retire)
            _retired.Enqueue((node.Value.Value, _frame, _clock()));
    }
}
