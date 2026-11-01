using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Rimtera-owned versioned snapshot. It is deliberately independent from any reference game's file format.
/// New saves store the grid in <see cref="CompressedCells"/> (<see cref="CellCodec"/>: run-length binary,
/// Deflate, base64) and leave <see cref="Cells"/> null; older saves with a JSON cell array still load.
/// The blight fields are null in older saves (no blight, no Fracture, nothing broken).
/// </summary>
public sealed record GameSnapshot(
    int FormatVersion,
    int Seed,
    int WorldWidth,
    int WorldHeight,
    WorldCell[]? Cells,
    long TickNumber,
    int Day,
    float ClockHour,
    float PlayerX,
    float PlayerY,
    float VelocityX,
    float VelocityY,
    int Facing,
    float HitPoints,
    float FoodReserve,
    float RestReserve,
    ItemStack[] Inventory,
    int SelectedInventorySlot,
    EquipmentSnapshot? Equipment = null,
    StatusSnapshot[]? Statuses = null,
    float? JoyReserve = null,
    float? ComfortReserve = null,
    ColonistSnapshot[]? Colonists = null,
    EnemyKind[]? DefeatedBosses = null,
    StorytellerSnapshot? Storyteller = null,
    PawnStateSnapshot? PlayerState = null,
    PawnStateSnapshot[]? ColonistStates = null,
    string? CompressedCells = null,
    WorldBlight? Blight = null,
    WorldHazard? Hazard = null,
    bool? FractureActive = null,
    int? HeartsBroken = null,
    int? AltarsSmashed = null,
    int? LifeCoresUsed = null,
    ContentSaveState? Content = null,
    WorldMetaSnapshot? WorldMeta = null,
    PlayerWorldSnapshot? PlayerWorld = null,
    CharacterFileData? Character = null,
    EnemySnapshot[]? Enemies = null,
    ProjectileSnapshot[]? Projectiles = null,
    long[]? RaidEnemyIds = null,
    long[]? NightBossIds = null,
    long? WhipMarkedEnemy = null,
    float? WhipBuffSeconds = null,
    RunStateSnapshot? Run = null,
    ItemId?[]? MiscEquipment = null,
    int? BedSpawnX = null,
    int? BedSpawnY = null,
    PlayerPlacement[]? Players = null)
{
    /// <summary>
    /// 3 (0.2.2): <see cref="Players"/> keeps every character's place in the world (PU-9). Older versions load through
    /// <see cref="SaveMigrations"/>; newer ones are refused.
    /// </summary>
    public const int CurrentFormatVersion = 3;

    /// <summary>
    /// Deflated cells as raw bytes (same stream as <see cref="CompressedCells"/>, without base64). Not part of
    /// the JSON: the save repository writes it to a sidecar file so large worlds never pass through the serializer.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public byte[]? CellData { get; init; }

    /// <summary>
    /// The cells' run stream before compression (<see cref="CellCodec.EncodeRuns"/>), set by an autosave capture so the slow
    /// compression runs on the save worker (PU-12). <see cref="WithCompressedCells"/> turns it into <see cref="CellData"/>.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public byte[]? CellRuns { get; init; }

    /// <summary>This snapshot with deferred cell runs compressed into <see cref="CellData"/> (itself when there are none).</summary>
    public GameSnapshot WithCompressedCells() =>
        CellData is null && CellRuns is { } runs ? this with { CellData = CellCodec.DeflateRuns(runs), CellRuns = null } : this;
}

/// <summary>
/// One character's place in a world (PU-9, D-43: any character may play any world): where they stood, facing, hit points
/// and their bed spawn. The character file stays the authority for what they carry. A character with no entry arrives at
/// the landing site.
/// </summary>
public sealed record PlayerPlacement(
    Guid CharacterId,
    float X,
    float Y,
    int Facing,
    float HitPoints,
    int? BedSpawnX = null,
    int? BedSpawnY = null);

/// <summary>
/// Save-format migrations (PU-11): each step upgrades a snapshot from its version to the next. Every save whose
/// <see cref="GameSnapshot.FormatVersion"/> is at most the current one loads through them; newer saves are refused with a
/// message. Each format change adds a step here and a fixture under <c>tests/fixtures/saves/&lt;version&gt;/</c>.
/// </summary>
public static class SaveMigrations
{
    /// <summary>Oldest snapshot version that still loads (version 2 is the oldest format any release wrote).</summary>
    public const int OldestSupported = 2;

    /// <summary>Upgrade steps by source version: <c>Steps[from]</c> turns a version-<c>from</c> snapshot into version <c>from + 1</c>.</summary>
    public static readonly IReadOnlyDictionary<int, Func<GameSnapshot, Guid?, GameSnapshot>> Steps =
        new Dictionary<int, Func<GameSnapshot, Guid?, GameSnapshot>>
        {
            // 2 → 3 (PU-9): the single saved player becomes the first entry of the per-character placement map, under the
            // character that saved it (the embedded character copy, else the save envelope's character).
            [2] = (snapshot, savedBy) => snapshot with
            {
                FormatVersion = 3,
                Players = snapshot.Players ?? ((snapshot.Character?.CharacterId ?? savedBy) is { } id && id != Guid.Empty
                    ? [new PlayerPlacement(id, snapshot.PlayerX, snapshot.PlayerY, snapshot.Facing, snapshot.HitPoints,
                        snapshot.BedSpawnX, snapshot.BedSpawnY)]
                    : null)
            },
        };

    /// <summary>True when a save of this version can be read (possibly after migration).</summary>
    public static bool CanRead(int version) => version >= OldestSupported && version <= GameSnapshot.CurrentFormatVersion;

    /// <summary>Why a save of <paramref name="version"/> cannot be read, or null when it can.</summary>
    public static string? RefusalFor(int version) =>
        version > GameSnapshot.CurrentFormatVersion
            ? $"it was saved by a newer version of Rimtera (save format {version}; this version reads up to {GameSnapshot.CurrentFormatVersion}). Update the game to play it"
            : version < OldestSupported ? $"its save format ({version}) is too old to read"
            : null;

    /// <summary>
    /// Brings a snapshot up to <see cref="GameSnapshot.CurrentFormatVersion"/>. <paramref name="savedBy"/> is the character the
    /// save envelope names, used when the snapshot has no embedded character. Throws <see cref="InvalidDataException"/> for
    /// versions that cannot be read.
    /// </summary>
    public static GameSnapshot Upgrade(GameSnapshot snapshot, Guid? savedBy = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (RefusalFor(snapshot.FormatVersion) is { } refusal)
            throw new InvalidDataException($"The world cannot be loaded: {refusal}.");
        while (snapshot.FormatVersion < GameSnapshot.CurrentFormatVersion)
        {
            var from = snapshot.FormatVersion;
            if (!Steps.TryGetValue(from, out var step))
                throw new InvalidDataException($"No migration from save format {from}.");
            snapshot = step(snapshot, savedBy);
            if (snapshot.FormatVersion != from + 1)
                throw new InvalidDataException($"The migration from save format {from} did not advance the version.");
        }
        return snapshot;
    }
}

/// <summary>
/// Generation metadata saved with the world so loading restores the saved cells directly instead of
/// regenerating: the biome layout (per-column surface and chasm rows, per-row frozen/junk borders,
/// radiated pockets) and, for detailed worlds, the placed features. Rectangles and points are flattened
/// into int arrays. Null in older saves, which regenerate the world once to rebuild this data.
/// </summary>
public sealed record WorldMetaSnapshot(
    int[] Surface,
    int[] MoltenTop,
    int[] FrozenEdge,
    int[] JunkStart,
    int[]? Pockets = null,
    bool Detailed = false,
    int SpawnColumn = 0,
    int CavernRow = 0,
    int[]? SurvivorSpots = null,
    int[]? BlightZones = null,
    int[]? Hearts = null,
    int[]? Altars = null,
    int[]? LifeCores = null,
    int[]? SupplyCaches = null,
    int[]? SalvagePods = null,
    int[]? Islands = null,
    int[]? Derelict = null,
    int[]? AcidCoasts = null,
    int[]? FungalHollows = null,
    int[]? Canopies = null,
    int[]? BloomHives = null,
    int[]? GeodeChambers = null,
    int[]? BasaltColumns = null,
    int[]? AlabasterHalls = null,
    int[]? MiteWarrens = null,
    int[]? Vault = null,
    int[]? Cabins = null,
    int[]? Dungeon = null,
    int[]? GiantTrees = null,
    int[]? Shrines = null,
    int[]? PlantCounts = null,
    Dictionary<string, int[]>? Sites = null,
    Dictionary<string, int>? Counts = null,
    int[]? Chests = null);

/// <summary>Converts the live layout and features to and from <see cref="WorldMetaSnapshot"/>.</summary>
public static class WorldMetaCodec
{
    public static WorldMetaSnapshot Capture(BiomeLayout layout, WorldFeatures? features)
    {
        var pockets = layout.RadiatedPockets.SelectMany(p => new[] { p.Center.X, p.Center.Y, p.RadiusX, p.RadiusY }).ToArray();
        if (features is null)
            return new WorldMetaSnapshot(layout.SurfaceRows, layout.MoltenRows, layout.FrozenEdges, layout.JunkStarts, pockets);
        static int[] Points(IEnumerable<GridPoint> points) => points.SelectMany(p => new[] { p.X, p.Y }).ToArray();
        static int[] Boxes(IEnumerable<(int Left, int Top, int Right, int Bottom)> boxes) =>
            boxes.SelectMany(b => new[] { b.Left, b.Top, b.Right, b.Bottom }).ToArray();
        static int[] Spans(IEnumerable<(int Left, int Right)> spans) => spans.SelectMany(b => new[] { b.Left, b.Right }).ToArray();
        return new WorldMetaSnapshot(layout.SurfaceRows, layout.MoltenRows, layout.FrozenEdges, layout.JunkStarts, pockets,
            Detailed: true,
            SpawnColumn: features.SpawnColumn,
            CavernRow: features.CavernRow,
            SurvivorSpots: Points(features.SurvivorSpots),
            BlightZones: features.BlightZones.SelectMany(z => new[] { z.Left, z.Right, z.Depth }).ToArray(),
            Hearts: Points(features.Hearts),
            Altars: Points(features.Altars),
            LifeCores: Points(features.LifeCores),
            SupplyCaches: Points(features.SupplyCaches),
            SalvagePods: Points(features.SalvagePods),
            Islands: features.Islands.SelectMany(i => new[] { i.Left, i.Right, i.Top }).ToArray(),
            Derelict: features.Derelict is { } ship ? [ship.Left, ship.Top, ship.Right, ship.Bottom] : null,
            AcidCoasts: Spans(features.AcidCoasts),
            FungalHollows: Boxes(features.FungalHollows),
            Canopies: Spans(features.Canopies),
            BloomHives: Boxes(features.BloomHives),
            GeodeChambers: Boxes(features.GeodeChambers),
            BasaltColumns: Boxes(features.BasaltColumns),
            AlabasterHalls: Boxes(features.AlabasterHalls),
            MiteWarrens: Boxes(features.MiteWarrens),
            Vault: features.Vault is { } vault ? [vault.Left, vault.Top, vault.Right, vault.Bottom] : null,
            Cabins: Boxes(features.Cabins),
            Dungeon: features.Dungeon is { } dungeon ? [dungeon.Left, dungeon.Top, dungeon.Right, dungeon.Bottom] : null,
            GiantTrees: Boxes(features.GiantTrees),
            Shrines: Points(features.Shrines),
            PlantCounts: [features.Pots, features.Trees, features.Herbs],
            Sites: features.Sites.ToDictionary(pair => pair.Key, pair => Boxes(pair.Value)),
            Counts: new Dictionary<string, int>(features.Counts),
            Chests: features.Chests.SelectMany(c => new[] { c.Cell.X, c.Cell.Y, c.Tier }).ToArray());
    }

    /// <summary>The saved layout, or null when the metadata does not fit this world (caller regenerates).</summary>
    public static BiomeLayout? RestoreLayout(WorldMetaSnapshot meta, int width, int height) =>
        BiomeLayout.FromSaved(width, height, meta.Surface, meta.MoltenTop, meta.FrozenEdge, meta.JunkStart, meta.Pockets);

    /// <summary>The saved features for detailed worlds (null for legacy small worlds).</summary>
    public static WorldFeatures? RestoreFeatures(WorldMetaSnapshot meta, WorldBlight blight)
    {
        if (!meta.Detailed)
            return null;
        var features = new WorldFeatures { Blight = blight, SpawnColumn = meta.SpawnColumn, CavernRow = meta.CavernRow };
        static IEnumerable<int[]> Groups(int[]? values, int size)
        {
            if (values is null)
                yield break;
            for (var i = 0; i + size <= values.Length; i += size)
                yield return values[i..(i + size)];
        }
        static void AddPoints(List<GridPoint> list, int[]? values) => list.AddRange(Groups(values, 2).Select(v => new GridPoint(v[0], v[1])));
        static void AddBoxes(List<(int, int, int, int)> list, int[]? values) => list.AddRange(Groups(values, 4).Select(v => (v[0], v[1], v[2], v[3])));
        static void AddSpans(List<(int, int)> list, int[]? values) => list.AddRange(Groups(values, 2).Select(v => (v[0], v[1])));
        AddPoints(features.SurvivorSpots, meta.SurvivorSpots);
        features.BlightZones.AddRange(Groups(meta.BlightZones, 3).Select(v => new BlightZone(v[0], v[1], v[2])));
        AddPoints(features.Hearts, meta.Hearts);
        AddPoints(features.Altars, meta.Altars);
        AddPoints(features.LifeCores, meta.LifeCores);
        AddPoints(features.SupplyCaches, meta.SupplyCaches);
        AddPoints(features.SalvagePods, meta.SalvagePods);
        features.Islands.AddRange(Groups(meta.Islands, 3).Select(v => new SkyIsland(v[0], v[1], v[2])));
        if (meta.Derelict is { Length: 4 } ship)
            features.Derelict = (ship[0], ship[1], ship[2], ship[3]);
        AddSpans(features.AcidCoasts, meta.AcidCoasts);
        AddBoxes(features.FungalHollows, meta.FungalHollows);
        AddSpans(features.Canopies, meta.Canopies);
        AddBoxes(features.BloomHives, meta.BloomHives);
        AddBoxes(features.GeodeChambers, meta.GeodeChambers);
        AddBoxes(features.BasaltColumns, meta.BasaltColumns);
        AddBoxes(features.AlabasterHalls, meta.AlabasterHalls);
        AddBoxes(features.MiteWarrens, meta.MiteWarrens);
        if (meta.Vault is { Length: 4 } vault)
            features.Vault = (vault[0], vault[1], vault[2], vault[3]);
        AddBoxes(features.Cabins, meta.Cabins);
        if (meta.Dungeon is { Length: 4 } dungeon)
            features.Dungeon = (dungeon[0], dungeon[1], dungeon[2], dungeon[3]);
        AddBoxes(features.GiantTrees, meta.GiantTrees);
        AddPoints(features.Shrines, meta.Shrines);
        if (meta.PlantCounts is { Length: 3 } counts)
            (features.Pots, features.Trees, features.Herbs) = (counts[0], counts[1], counts[2]);
        foreach (var (name, values) in meta.Sites ?? new())
            foreach (var box in Groups(values, 4))
                features.AddSite(name, box[0], box[1], box[2], box[3]);
        foreach (var (name, count) in meta.Counts ?? new())
            features.Counts[name] = count;
        features.Chests.AddRange(Groups(meta.Chests, 3).Select(v => (new GridPoint(v[0], v[1]), v[2])));
        return features;
    }
}

/// <summary>Position and vitals for a deterministic colony NPC. Profiles are regenerated from the world seed.</summary>
public readonly record struct ColonistSnapshot(
    int Index,
    float X,
    float Y,
    float HitPoints,
    float FoodReserve,
    float RestReserve,
    float JoyReserve,
    float ComfortReserve,
    int[]? WorkPriorities = null,
    bool FollowsPlayer = false,
    ItemStack Carried = default);

/// <summary>
/// Body, mind and social state for one pawn: per-part conditions, memories, explicit relations, mental-break
/// state and mood. <see cref="Index"/> is -1 for the player, otherwise the colonist roster index.
/// </summary>
public sealed record PawnStateSnapshot(
    int Index,
    HealthSnapshot? Health = null,
    MemorySnapshot[]? Memories = null,
    RelationSnapshot[]? Relations = null,
    MentalBreakSnapshot? Break = null,
    float? Mood = null,
    float? NextInteractionHour = null);

/// <summary>
/// One live creature in a world snapshot. The ID is the save-time ID; loading remaps it if the rebuilt
/// town/colonist roster already owns that number. Segment positions are flattened X/Y pairs.
/// </summary>
public sealed record EnemySnapshot(
    long Id,
    EnemyKind Kind,
    float X,
    float Y,
    float VelocityX,
    float VelocityY,
    int Facing,
    bool Grounded,
    float MaximumHitPoints,
    float HitPoints,
    float AttackDamage,
    bool StatsScaled,
    bool Shielded,
    EnemyState State,
    float StateTime,
    float AttackCooldown,
    int Phase,
    float FleeSeconds,
    WorldEventKind? EventTag = null,
    bool Disguised = false,
    bool Lunging = false,
    float AiTimer = 0f,
    int AiStep = 0,
    float[]? Segments = null,
    AfflictionSnapshot[]? Afflictions = null,
    HitImmunitySnapshot[]? HitImmunity = null,
    bool FromStatue = false);

/// <summary>An affliction carried by a live creature and the seconds it has left.</summary>
public sealed record AfflictionSnapshot(StatusId Status, float Seconds);

/// <summary>Per-attacker hit immunity carried by a live creature.</summary>
public sealed record HitImmunitySnapshot(long Attacker, float Seconds);

/// <summary>
/// One live projectile. Constructor fields are stored explicitly; transient targeting/cooldown state is
/// remapped from save-time enemy IDs when the world loads.
/// </summary>
public sealed record ProjectileSnapshot(
    long Id,
    float X,
    float Y,
    float VelocityX,
    float VelocityY,
    float Damage,
    float Knockback,
    float RemainingSeconds,
    ProjectileTeam Team,
    string? SourceName,
    DamageKind Kind,
    float PurifyRadius,
    float Homing,
    DamageClass Class,
    int PierceRemaining,
    int CritChance,
    bool Critical,
    string? SpriteName,
    StatusId? InflictStatus,
    int InflictChance,
    WeaponStyle Style,
    ItemId? SourceItem,
    bool Tethered,
    bool Returning,
    float Gravity,
    float ExplosionRadius,
    float MaxRange,
    float Traveled,
    long[]? HitEnemies = null,
    HitCooldownSnapshot[]? HitCooldowns = null);

/// <summary>A projectile's per-enemy repeat-hit cooldown.</summary>
public sealed record HitCooldownSnapshot(long EnemyId, float Seconds);

/// <summary>
/// Atomic writes with one backup generation (PU-11): every save writes a temporary file, moves the current file to
/// <c>X.bak</c>, then moves the new one into place. A crash leaves either the new file or the previous one (as the backup);
/// readers fall back to the backup when the main file is missing or unreadable.
/// </summary>
public static class SafeFile
{
    public const string BackupSuffix = ".bak";

    public static string BackupPath(string path) => path + BackupSuffix;

    /// <summary>Writes through <paramref name="write"/> to a temporary file, then rotates it in (the old file becomes the backup).</summary>
    public static void Write(string path, Action<Stream> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } directory)
            Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        using (var stream = File.Create(temporary))
            write(stream);
        Replace(temporary, path);
    }

    public static void WriteAllText(string path, string text) =>
        Write(path, stream =>
        {
            using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
            writer.Write(text);
        });

    /// <summary>Moves a finished temporary file over <paramref name="path"/>, keeping the previous file as the backup.</summary>
    public static void Replace(string temporary, string path)
    {
        if (File.Exists(path))
            File.Move(path, BackupPath(path), overwrite: true);
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// Reads <paramref name="path"/> with <paramref name="read"/> (null or an exception means unreadable), falling back to the
    /// backup. <paramref name="fromBackup"/> says which one answered.
    /// </summary>
    public static T? ReadWithBackup<T>(string path, Func<string, T?> read, out bool fromBackup) where T : class
    {
        fromBackup = false;
        if (TryRead(path, read) is { } main)
            return main;
        if (TryRead(BackupPath(path), read) is { } backup)
        {
            fromBackup = true;
            return backup;
        }
        return null;
    }

    private static T? TryRead<T>(string path, Func<string, T?> read) where T : class
    {
        if (!File.Exists(path))
            return null;
        try
        {
            return read(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or
                                       InvalidDataException or NotSupportedException or EndOfStreamException or ArgumentException)
        {
            return null;
        }
    }
}
