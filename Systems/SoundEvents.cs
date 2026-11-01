using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// What the simulation heard (PE-4, D-59): every action that makes a sound in Terraria emits one of these, with where it
/// happened. The audio director plays them (pan and falloff by position); the animator turns digs and breaks into dust.
/// A plain jump or landing emits nothing.
/// </summary>
public enum SoundEventKind : byte
{
    /// <summary>A block broke. Variant: its material (stone, soil, plant, glass, metal); Detail: the tile id.</summary>
    Break,
    /// <summary>A pick, drill, cutter or hammer hit a block or wall (channelled drills included). Variant: material.</summary>
    Dig,
    /// <summary>A background wall was placed. Variant: "wall".</summary>
    Place,
    /// <summary>A Tech or Command item was used. Variant: "tech" or "command"; Detail: the item id.</summary>
    Use,
    /// <summary>A recipe was crafted. Variant: the station family (craft, smelt, forge, brew, weave); Detail: output item.</summary>
    Craft,
    /// <summary>Credits changed hands at a shop. Variant: "buy" or "sell".</summary>
    Coin,
    /// <summary>A potion, stim or charge cell was drunk.</summary>
    Drink,
    /// <summary>Food was eaten.</summary>
    Eat,
    DoorOpen,
    DoorClose,
    ChestOpen,
    ChestClose,
    /// <summary>The body entered or left a liquid. Variant: the liquid (water, lava, acid, …); Detail: 1 entering, 0 leaving.</summary>
    Splash,
    Explosion,
    PlayerDeath,
    DoubleJump,
    /// <summary>An item was picked up off the ground or dropped straight into the bag. Variant: "coin" for credits.</summary>
    Pickup,
    /// <summary>A bug net caught a critter (a soft catch, never a death).</summary>
    Catch
}

/// <summary>One simulation sound: what, which variant, where (world tiles), and an optional id (tile, item).</summary>
public readonly record struct SoundEvent(SoundEventKind Kind, string Variant, Vector2 Position, int Detail = 0, long Tick = 0);

/// <summary>Why a creature left the world (PE-5): only <see cref="Died"/> gets a death sound and a burst.</summary>
public enum DeathReason : byte
{
    Died,
    Despawned,
    Caught
}

/// <summary>A creature that left the world: where it was, its body and why.</summary>
public readonly record struct DeathRecord(long Id, EnemyKind Kind, Vector2 Position, Vector2 Size, DeathReason Reason);

/// <summary>How a number is shown (PE-14): a direct hit (crits larger and orange), damage over time, a heal, a charge gain.</summary>
public enum HitKind : byte
{
    Direct,
    Dot,
    Heal,
    Charge
}

/// <summary>One hit, heal or charge gain. <see cref="TargetId"/> is the creature id, or -1 for the player.</summary>
public readonly record struct HitRecord(long TargetId, Vector2 Position, float Amount, bool Critical, HitKind Kind);

/// <summary>
/// A bounded ring of feedback records with a running sequence number. Readers (the audio director, the animator, tests)
/// keep their own cursor and drain what is new since they last looked, so several readers share one log and a headless
/// simulation that nobody reads never grows past <see cref="Capacity"/>.
/// </summary>
public class FeedbackLog<T>
{
    private readonly T[] _ring;
    private long _next;

    public FeedbackLog(int capacity = 256)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _ring = new T[capacity];
    }

    public int Capacity => _ring.Length;

    /// <summary>Sequence number the next record will get (a fresh reader starts its cursor here to skip the past).</summary>
    public long Next => _next;

    /// <summary>Records still held, oldest first.</summary>
    public int Count => (int)Math.Min(_next, _ring.Length);

    public void Add(T record)
    {
        _ring[_next % _ring.Length] = record;
        _next++;
    }

    /// <summary>
    /// Appends every record from <paramref name="cursor"/> on to <paramref name="into"/> and moves the cursor to the end.
    /// Records older than the ring are lost (a reader that falls far behind only skips, it never sees stale data twice).
    /// </summary>
    public int Drain(ref long cursor, List<T> into)
    {
        var from = Math.Max(cursor, _next - _ring.Length);
        if (from < 0)
            from = 0;
        for (var at = from; at < _next; at++)
            into.Add(_ring[at % _ring.Length]);
        cursor = _next;
        return (int)Math.Max(0, _next - from);
    }

    /// <summary>The records still held, oldest first.</summary>
    public IReadOnlyList<T> Snapshot()
    {
        var list = new List<T>(Count);
        long cursor = 0;
        Drain(ref cursor, list);
        return list;
    }

    public void Clear() => _next = 0;
}

/// <summary>The simulation's sound queue (<see cref="GameSimulation.SoundEvents"/>).</summary>
public sealed class SoundEventQueue() : FeedbackLog<SoundEvent>(512)
{
    private long _tick = -1;
    private readonly HashSet<(SoundEventKind, string)> _thisTick = new();

    /// <summary>The simulation tick the next events belong to (set once per tick by the simulation).</summary>
    public long Tick
    {
        get => _tick;
        internal set
        {
            if (value != _tick)
                _thisTick.Clear();
            _tick = value;
        }
    }

    /// <summary>
    /// Queues a sound. The same kind and variant twice in one tick is one sound (a drink handled by two code paths, a
    /// door toggled as a pair), so the queue never doubles up.
    /// </summary>
    public void Emit(SoundEventKind kind, string variant, Vector2 position, int detail = 0)
    {
        variant ??= string.Empty;
        if (!_thisTick.Add((kind, variant)))
            return;
        Add(new SoundEvent(kind, variant, position, detail, _tick));
    }

    /// <summary>How many of the held events are of <paramref name="kind"/> (tests).</summary>
    public int CountOf(SoundEventKind kind) => Snapshot().Count(sound => sound.Kind == kind);

    /// <summary>
    /// The material a tile sounds and crumbles like: stone, soil, plant, glass or metal (the audio director maps each to a
    /// break_ and dig_ cue; the animator colours its dust from the tile's art).
    /// </summary>
    public static string Material(TileDefinition tile)
    {
        var name = tile.Name.ToLowerInvariant();
        // Whole words for glass: "pane" must not catch "Hull panel", nor "ice" catch "lattice".
        var words = name.Split([' ', '-', '_', '(', ')'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Any(word => word is "pane" or "panes" or "ice" or "gem" or "gems" ||
                              Has(word, "glass", "window", "crystal", "prism", "shard", "frost", "glacial")))
            return "glass";
        if (tile.Station != CraftingStationId.None ||
            Has(name, "plating", "panel", "hull", "metal", "steel", "alloy", "pipe", "grate", "scrap", "plate", "beam", "girder"))
            return "metal";
        if (Has(name, "wood", "log", "timber", "plank", "bark", "leaf", "vine", "spore", "mushroom", "root", "flesh", "hive", "comb",
                "web", "silk", "gel", "moss", "grass", "plant", "flower", "herb", "tree", "trunk", "cactus", "sapling", "coral", "kelp",
                "fungal", "bloom", "petal", "reed"))
            return "plant";
        if (Has(name, "ore", "rock", "basalt", "stone", "granite", "marble", "obsidian", "bedrock", "magma", "brick", "slate", "cobble"))
            return "stone";
        return tile.IsSolid ? "soil" : "plant";
    }

    /// <summary>The station family a recipe is made at, for its craft sound (smelt, forge, brew, weave, or plain craft).</summary>
    public static string CraftVariant(RecipeDefinition? recipe)
    {
        foreach (var station in recipe?.RequiredStations ?? (IEnumerable<CraftingStationId>)Array.Empty<CraftingStationId>())
        {
            switch (station)
            {
                case CraftingStationId.Furnace or CraftingStationId.OreRefineryMk2 or CraftingStationId.Kiln:
                    return "smelt";
                case CraftingStationId.Anvil or CraftingStationId.PowerForgeMk2 or CraftingStationId.AlloyAnvil or CraftingStationId.VoidForge:
                    return "forge";
                case CraftingStationId.ChemLab or CraftingStationId.BioLab or CraftingStationId.CookingStation or CraftingStationId.DyeVat:
                    return "brew";
                case CraftingStationId.Loom:
                    return "weave";
            }
        }
        return "craft";
    }

    /// <summary>The liquid's name as a splash variant (water, lava, acid, nectar, phase, fuel).</summary>
    public static string LiquidVariant(LiquidKind liquid) => liquid == LiquidKind.None ? "water" : liquid.ToString().ToLowerInvariant();

    private static bool Has(string name, params string[] words) => words.Any(word => name.Contains(word, StringComparison.Ordinal));
}

/// <summary>
/// Per-kind rate limits for the audio director (sound spam risk in the 0.1.9 plan): a held drill or a swarm of pickups
/// plays at most a few sounds a second of each kind, however many events arrive.
/// </summary>
public sealed class SoundRateLimiter
{
    private readonly Dictionary<string, double> _last = new(StringComparer.Ordinal);

    /// <summary>Shortest gap in seconds between two plays of the same key.</summary>
    public static double MinimumGap(string key) => key switch
    {
        "dig" => 0.07,
        "break" => 0.05,
        "pickup" => 0.06,
        "splash" => 0.25,
        "hit" or "impact" => 0.03,
        "death" => 0.05,
        "door" or "chest" => 0.08,
        _ => 0.04
    };

    /// <summary>True (and the key's clock restarts) when <paramref name="key"/> may play at <paramref name="now"/> seconds.</summary>
    public bool Allow(string key, double now)
    {
        if (_last.TryGetValue(key, out var last) && now - last < MinimumGap(key))
            return false;
        _last[key] = now;
        return true;
    }

    public void Reset() => _last.Clear();
}

/// <summary>
/// How a loss or gain of hit points is shown and heard (PE-14): crits larger and orange, damage over time smaller and
/// dimmer with no hit sound, heals green, charge gains blue. Shared by the animator and the audio director.
/// </summary>
public static class CombatTextRules
{
    /// <summary>
    /// A creature's loss this frame: a recorded direct hit (crit if any was), otherwise damage over time when it carries an
    /// affliction, otherwise an unrecorded direct hit (thorns, falling blocks, minion shots).
    /// </summary>
    public static (HitKind Kind, bool Critical) ClassifyCreatureLoss(bool directRecorded, bool criticalRecorded, bool afflicted) =>
        directRecorded ? (HitKind.Direct, criticalRecorded) : afflicted ? (HitKind.Dot, false) : (HitKind.Direct, false);

    /// <summary>
    /// The player's loss this frame: a hit starts hit immunity; without one, the loss is damage over time when a damaging
    /// status is running or the body is in a harmful liquid (falls and crushes still count as hits).
    /// </summary>
    public static HitKind ClassifyPlayerLoss(bool immunityStarted, bool damagingStatus, bool inHarmfulLiquid) =>
        !immunityStarted && (damagingStatus || inHarmfulLiquid) ? HitKind.Dot : HitKind.Direct;

    /// <summary>Seconds within which a new number on the same target adds to the last one instead of stacking a new line.</summary>
    public const double MergeSeconds = 0.3;
}

/// <summary>The simulation's side of the feedback (PE-4, PE-5, PE-14): the sound queue, recent deaths and hit records.</summary>
public sealed partial class GameSimulation
{
    /// <summary>Sounds the simulation made, newest last (PE-4). The audio director drains it each frame.</summary>
    public SoundEventQueue SoundEvents { get; } = new();

    /// <summary>Creatures that left the world and why (PE-5): only <see cref="DeathReason.Died"/> is a kill.</summary>
    public FeedbackLog<DeathRecord> RecentDeaths { get; } = new(256);

    /// <summary>Direct hits on creatures (with their crit flag) and charge gains (PE-14).</summary>
    public FeedbackLog<HitRecord> HitRecords { get; } = new(512);

    private bool _feedbackPrimed;
    private bool _feedbackDead;
    private long _feedbackDoubleJumps;
    private LiquidKind _feedbackLiquid;

    /// <summary>
    /// Once per tick, before anything moves: stamps the sound queue with the tick and turns changes since the last tick
    /// into sounds (the player died, a double jump fired, the body entered or left a liquid).
    /// </summary>
    private void ObserveFeedback()
    {
        SoundEvents.Tick = TickNumber + 1; // the tick about to run (a dead player's ticks keep the number)
        var dead = Player.IsDead;
        var doubleJumps = Flags.Get("player.double_jumps");
        var liquid = _bodyLiquid;
        if (_feedbackPrimed)
        {
            if (dead && !_feedbackDead)
                SoundEvents.Emit(SoundEventKind.PlayerDeath, string.Empty, PlayerCenter);
            if (doubleJumps > _feedbackDoubleJumps)
                SoundEvents.Emit(SoundEventKind.DoubleJump, string.Empty, PlayerCenter);
            if (liquid != _feedbackLiquid && !dead)
                SoundEvents.Emit(SoundEventKind.Splash, SoundEventQueue.LiquidVariant(liquid == LiquidKind.None ? _feedbackLiquid : liquid),
                    PlayerCenter, liquid == LiquidKind.None ? 0 : 1);
        }
        _feedbackPrimed = true;
        _feedbackDead = dead;
        _feedbackDoubleJumps = doubleJumps;
        _feedbackLiquid = liquid;
    }

    private static Vector2 CellCenter(GridPoint point) => new(point.X + 0.5f, point.Y + 0.5f);

    /// <summary>A tool struck a block or wall: the dig sound and its dust.</summary>
    private void EmitDig(GridPoint point, TileId tile) =>
        SoundEvents.Emit(SoundEventKind.Dig, SoundEventQueue.Material(Tiles[tile]), CellCenter(point), (int)tile);

    /// <summary>A block broke: the break sound and its burst of dust.</summary>
    private void EmitBreak(GridPoint point, TileId tile) =>
        SoundEvents.Emit(SoundEventKind.Break, SoundEventQueue.Material(Tiles[tile]), CellCenter(point), (int)tile);

    /// <summary>A creature left the world (PE-5).</summary>
    private void RecordDeath(EnemyAgent enemy, DeathReason reason) =>
        RecentDeaths.Add(new DeathRecord(enemy.Id, enemy.Kind, enemy.Motion.Position + enemy.Motion.Size / 2f, enemy.Motion.Size, reason));

    /// <summary>A direct player hit landed on a creature (PE-14).</summary>
    private void RecordHit(EnemyAgent enemy, float amount, bool critical) =>
        HitRecords.Add(new HitRecord(enemy.Id, enemy.Motion.Position + enemy.Motion.Size / 2f, amount, critical, HitKind.Direct));

    /// <summary>Charge restored by an item (PE-14: a blue number).</summary>
    private void RecordChargeGain(float amount) =>
        HitRecords.Add(new HitRecord(-1, PlayerCenter, amount, false, HitKind.Charge));

    /// <summary>The player closed a container's panel (the UI owns the panel; the lid sound is the simulation's).</summary>
    public void CloseContainer(GridPoint cell) => SoundEvents.Emit(SoundEventKind.ChestClose, string.Empty, CellCenter(cell));

    /// <summary>A quick-use key drank a potion or charge cell (the quick keys live in the UI; the gulp is the simulation's).</summary>
    public void EmitQuickDrink() => SoundEvents.Emit(SoundEventKind.Drink, string.Empty, PlayerCenter);

    /// <summary>True while a status that deals damage over time is running on the player.</summary>
    public bool PlayerHasDamagingStatus => Player.Statuses.Active.Any(status => GearStatusDefinitions[status.Id].DamagePerTick > 0f);

    /// <summary>True while the body is in a liquid that hurts (lava, acid).</summary>
    public bool PlayerInHarmfulLiquid => _bodyLiquid is LiquidKind.Lava or LiquidKind.Acid;

    /// <summary>Test probe: one rolled player hit through the real hit pipeline, with the crit forced on or off.</summary>
    internal float ProbeStrike(EnemyAgent enemy, float damage, bool critical) => StrikeEnemy(enemy, damage, critical, DamageClass.Melee);
}
