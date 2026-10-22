using Rimtera.Entities;

namespace Rimtera.Systems;

/// <summary>What the colony knows about one creature kind.</summary>
public sealed class BestiaryEntry
{
    internal BestiaryEntry(EnemyKind kind) => Kind = kind;

    public EnemyKind Kind { get; }
    public string DisplayName => EnemyArchetypes.Get(Kind).DisplayName;
    public bool Seen { get; internal set; }
    public int Kills { get; internal set; }
    internal HashSet<ItemId> DropSet { get; } = new();
    internal HashSet<BiomeZone> BiomeSet { get; } = new();
    /// <summary>Items observed dropping from this kind.</summary>
    public IReadOnlyCollection<ItemId> DropsSeen => DropSet;
    /// <summary>Biomes it was seen or killed in.</summary>
    public IReadOnlyCollection<BiomeZone> Biomes => BiomeSet;
    /// <summary>Unlock tier: 0 unknown, 1 seen, 2 killed once (stats), 3 killed 10+ times (drops shown).</summary>
    public int UnlockLevel => Kills >= 10 ? 3 : Kills > 0 ? 2 : Seen ? 1 : 0;
}

public sealed record BestiarySnapshot(EnemyKind Kind, bool Seen, int Kills, ItemId[]? Drops = null, BiomeZone[]? Biomes = null);

/// <summary>Per-world creature log: sightings, kills, observed drops and biomes.</summary>
public sealed class BestiaryLog
{
    private readonly Dictionary<EnemyKind, BestiaryEntry> _entries = new();

    /// <summary>Raised the first time a kind is seen.</summary>
    public event Action<BestiaryEntry>? Discovered;

    public IReadOnlyCollection<BestiaryEntry> Entries => _entries.Values.OrderBy(entry => entry.Kind).ToArray();
    public int SeenCount => _entries.Values.Count(entry => entry.Seen);
    public int TotalKinds => Enum.GetValues<EnemyKind>().Length;
    /// <summary>Share of all creature kinds discovered, 0–1.</summary>
    public float Completion => TotalKinds == 0 ? 0f : _entries.Values.Sum(entry => entry.UnlockLevel / 3f) / TotalKinds;

    public BestiaryEntry? Get(EnemyKind kind) => _entries.GetValueOrDefault(kind);

    private BestiaryEntry Entry(EnemyKind kind)
    {
        if (!_entries.TryGetValue(kind, out var entry))
            _entries[kind] = entry = new BestiaryEntry(kind);
        return entry;
    }

    public void RecordSeen(EnemyKind kind, BiomeZone zone)
    {
        var entry = Entry(kind);
        entry.BiomeSet.Add(zone);
        if (entry.Seen)
            return;
        entry.Seen = true;
        Discovered?.Invoke(entry);
    }

    public void RecordKill(EnemyKind kind, BiomeZone zone)
    {
        RecordSeen(kind, zone);
        Entry(kind).Kills++;
    }

    public void RecordDrop(EnemyKind kind, ItemId item) => Entry(kind).DropSet.Add(item);

    public BestiarySnapshot[] Capture() => _entries.Values.OrderBy(entry => entry.Kind)
        .Select(entry => new BestiarySnapshot(entry.Kind, entry.Seen, entry.Kills,
            entry.DropSet.OrderBy(id => id).ToArray(), entry.BiomeSet.OrderBy(zone => zone).ToArray()))
        .ToArray();

    public void Restore(IEnumerable<BestiarySnapshot>? saved)
    {
        foreach (var row in saved ?? [])
        {
            if (!Enum.IsDefined(row.Kind) || row.Kills < 0)
                continue;
            var entry = Entry(row.Kind);
            entry.Seen = row.Seen || row.Kills > 0;
            entry.Kills = row.Kills;
            foreach (var drop in row.Drops ?? [])
                entry.DropSet.Add(drop);
            foreach (var zone in row.Biomes ?? [])
                if (Enum.IsDefined(zone))
                    entry.BiomeSet.Add(zone);
        }
    }
}

public sealed partial class GameSimulation
{
    private BestiaryLog? _bestiary;

    /// <summary>The world's creature log.</summary>
    public BestiaryLog Bestiary
    {
        get
        {
            if (_bestiary is null)
            {
                _bestiary = new BestiaryLog();
                _bestiary.Discovered += entry =>
                {
                    if (_bestiary.SeenCount >= 25)
                        Achievements.TryUnlock("field-naturalist");
                };
            }
            return _bestiary;
        }
    }
}
