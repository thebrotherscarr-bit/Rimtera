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
    int? BedSpawnY = null)
{
    public const int CurrentFormatVersion = 2;

    /// <summary>
    /// Deflated cells as raw bytes (same stream as <see cref="CompressedCells"/>, without base64). Not part of
    /// the JSON: the save repository writes it to a sidecar file so large worlds never pass through the serializer.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public byte[]? CellData { get; init; }
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
    HitImmunitySnapshot[]? HitImmunity = null);

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
