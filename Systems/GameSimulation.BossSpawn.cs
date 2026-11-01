using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>How a boss may leave the fight without being beaten (BO-6). Flags combine.</summary>
[Flags]
public enum BossLeaveRule : byte
{
    None = 0,
    /// <summary>Night bosses leave when dawn breaks (Terraria: the Eye, the mechanical bosses, the moon bosses).</summary>
    AtDawn = 1,
    /// <summary>Day bosses leave when dusk falls (the Eclipse Lord of the Dark Sun).</summary>
    AtDusk = 2,
    /// <summary>
    /// The boss loses its target: more than <see cref="BossLeaveRules.TargetLostDistance"/> tiles away, or out of sight
    /// (beyond <see cref="BossLeaveRules.SightCheckDistance"/> tiles) for <see cref="BossLeaveRules.SightLostSeconds"/>.
    /// </summary>
    OnTargetLost = 4,
    /// <summary>The boss leaves once the player has stayed out of its biome for a while (the Fallout Hive-mind outside the blight).</summary>
    LeaveBiome = 8
}

/// <summary>Where a boss spawn came from (BO-9: every boss arrives through <see cref="GameSimulation.TrySpawnBoss"/>).</summary>
public enum BossSpawnSource : byte
{
    SummonItem,
    DebugKey,
    EventFinale,
    DuskIncursion,
    NightArrival,
    Patrol,
    /// <summary>World triggers: canopy bulbs, hives, brood pods, crystal and vault altars, blight hearts.</summary>
    WorldTrigger,
    Ritual,
    Fishing,
    Countdown
}

/// <summary>The per-boss leave rules (BO-6). Kept beside the spawn path rather than on the archetype table.</summary>
public static class BossLeaveRules
{
    /// <summary>Beyond this many tiles from the player a boss has lost its target.</summary>
    public const float TargetLostDistance = 120f;
    /// <summary>Seconds a boss may stay beyond <see cref="TargetLostDistance"/> before it leaves.</summary>
    public const float TargetLostSeconds = 3f;
    /// <summary>Seconds without line of sight before a boss leaves.</summary>
    public const float SightLostSeconds = 30f;
    /// <summary>
    /// Line of sight only counts beyond this distance, so a boss fighting the player around a wall or through a cave
    /// ceiling (worms, fliers) never gives up mid-fight.
    /// </summary>
    public const float SightCheckDistance = 30f;
    /// <summary>Seconds the player may stay out of a <see cref="BossLeaveRule.LeaveBiome"/> boss's biome.</summary>
    public const float OutOfBiomeSeconds = 10f;

    /// <summary>
    /// Night bosses: the Overseer mech, the hard trio, the Moon Eater, the Hollow Regent, the Rime Seraph and the dusk four.
    /// </summary>
    public static IReadOnlySet<EnemyKind> NightBosses { get; } = new HashSet<EnemyKind>(
        new[]
        {
            EnemyKind.StoneSentinel, EnemyKind.MoonEater, EnemyKind.HollowRegent, EnemyKind.RimeSeraph
        }.Concat(GameSimulation.HardTrio).Concat(EnemyArchetypes.EventBosses));

    /// <summary>Day bosses: the Eclipse Lord (the Dark Sun's boss) leaves at dusk.</summary>
    public static IReadOnlySet<EnemyKind> DayBosses { get; } = new HashSet<EnemyKind> { EnemyKind.EclipseLord };

    /// <summary>The rules of one boss kind (none for creatures that are not bosses).</summary>
    public static BossLeaveRule For(EnemyKind kind)
    {
        if (!EnemyArchetypes.Contains(kind) || !EnemyArchetypes.Get(kind).IsBoss)
            return BossLeaveRule.None;
        var rule = BossLeaveRule.None;
        if (NightBosses.Contains(kind))
            rule |= BossLeaveRule.AtDawn;
        if (DayBosses.Contains(kind))
            rule |= BossLeaveRule.AtDusk;
        // The Astro-God is the finale: it never gives up (Terraria's Moon Lord leaves only when the player dies).
        if (kind != EnemyKind.AstroGod)
            rule |= BossLeaveRule.OnTargetLost;
        if (kind == EnemyKind.FalloutHivemind)
            rule |= BossLeaveRule.LeaveBiome;
        return rule;
    }
}

/// <summary>
/// One spawn path for every boss (BO-9, D-11) and the boss leave rules (BO-6). A boss is refused only while another
/// boss of the same kind lives; different kinds may share the fight (the bar shows the most recent until BO-10's stacked
/// bars). A boss that leaves posts "X has left (reason)", is recorded as despawned (not a defeat) and drops nothing.
/// </summary>
public sealed partial class GameSimulation
{
    private readonly Dictionary<long, float> _bossFarSeconds = new();
    private readonly Dictionary<long, float> _bossBlindSeconds = new();
    private float _bossOutOfBiomeSeconds;

    /// <summary>Living bosses in the order they arrived (the most recent last).</summary>
    public IReadOnlyList<EnemyAgent> ActiveBosses =>
        _enemies.Where(enemy => enemy.IsBoss && enemy.IsAlive).OrderBy(enemy => enemy.Id).ToArray();

    /// <summary>The most recently arrived living boss (the one the boss bar shows until BO-10), or null.</summary>
    public EnemyAgent? LatestBoss => _enemies.Where(enemy => enemy.IsBoss && enemy.IsAlive).MaxBy(enemy => enemy.Id);

    /// <summary>The D-11 refusal: a boss of the same kind is already in the fight.</summary>
    public string? SameKindBossRefusal(EnemyKind kind) =>
        EnemyArchetypes.Get(kind).IsBoss && _enemies.Any(enemy => enemy.Kind == kind && enemy.IsAlive)
            ? $"The {EnemyArchetypes.Get(kind).DisplayName} is already here."
            : null;

    /// <summary>
    /// The one boss spawn path. Refuses (with <paramref name="refusal"/>) while a boss of the same kind lives; otherwise
    /// places the boss near the player (or at <paramref name="at"/>) and returns it. Callers keep their own gates
    /// (summon refusals, natural-arrival rules) and their own messages.
    /// </summary>
    public bool TrySpawnBoss(EnemyKind kind, BossSpawnSource source, out EnemyAgent? boss, out string? refusal, Vector2? at = null)
    {
        boss = null;
        refusal = SameKindBossRefusal(kind);
        if (refusal is not null)
            return false;
        boss = at is { } position ? SpawnCreature(kind, position) : PlaceBoss(kind);
        Flags.Increment($"boss.spawns.{WorldFlags.Key(source)}");
        return true;
    }

    /// <summary>Spawns through <see cref="TrySpawnBoss"/>, posting the refusal when there is one. Returns the boss or null.</summary>
    private EnemyAgent? SpawnBossOrSay(EnemyKind kind, BossSpawnSource source, Vector2? at = null)
    {
        if (TrySpawnBoss(kind, source, out var boss, out var refusal, at))
            return boss;
        PostMessage(refusal!);
        return null;
    }

    // ------------------------------------------------------------------ the challenge key (BO-18)

    /// <summary>The challenge key is a play-test tool: it works for creative characters and in a <c>--kit</c> quickstart.</summary>
    public bool DebugSummonKeyEnabled { get; set; }

    /// <summary>The challenge key: summons the next boss through the item refusals (creative and <c>--kit</c> only).</summary>
    public bool TrySummonSentinel()
    {
        if (!IsCreative && !DebugSummonKeyEnabled)
        {
            PostMessage("The challenge key only answers in creative or a --kit play-test.");
            return false;
        }
        return TrySummonNextBoss();
    }

    /// <summary>
    /// Summons the first undefeated progression boss (gate, easy, the core gate boss, hard, then the Astro-God) whose summon
    /// item would work right now: the same refusals as items (<see cref="HardmodeSummonRefusal"/>, which ends in the
    /// coverage refusals). Only one boss at a time. When every remaining boss refuses, posts the first refusal.
    /// </summary>
    public bool TrySummonNextBoss()
    {
        if (_enemies.Any(enemy => enemy.IsBoss && enemy.IsAlive))
        {
            PostMessage("A boss is already here.");
            return false;
        }
        var candidates = EnemyArchetypes.BossProgression.Where(kind => !_defeatedBosses.Contains(kind)).ToList();
        if (candidates.Count == 0)
            candidates.Add(EnemyKind.AstroGod);
        string? first = null;
        foreach (var kind in candidates)
        {
            if (HardmodeSummonRefusal(kind) is { } refusal)
            {
                first ??= refusal;
                continue;
            }
            if (!TrySpawnBoss(kind, BossSpawnSource.DebugKey, out _, out var sameKind))
            {
                first ??= sameKind;
                continue;
            }
            PostMessage($"The {EnemyArchetypes.Get(kind).DisplayName} has answered the challenge.");
            return true;
        }
        PostMessage(first ?? "Nothing answers the challenge.");
        return false;
    }

    // ------------------------------------------------------------------ leaving (BO-6)

    /// <summary>True when the clock passed <paramref name="moment"/> (dawn 06:00, dusk 18:00) between two readings. EV-4 reuses it.</summary>
    public static bool BoundaryPassed(WorldEventMoment moment, float previousHour, float currentHour, float elapsedHours)
    {
        var hour = moment switch
        {
            WorldEventMoment.Dawn => 6f,
            WorldEventMoment.Dusk => 18f,
            _ => throw new ArgumentOutOfRangeException(nameof(moment), "Only dawn and dusk are boundaries.")
        };
        if (elapsedHours >= 24f)
            return true;
        return currentHour >= previousHour
            ? previousHour < hour && currentHour >= hour
            : previousHour < hour || currentHour >= hour;
    }

    /// <summary>Night bosses leave at dawn, day bosses at dusk (called by the clock on a boundary).</summary>
    private void BossesLeaveAtBoundary(WorldEventMoment moment)
    {
        var rule = moment == WorldEventMoment.Dawn ? BossLeaveRule.AtDawn : BossLeaveRule.AtDusk;
        foreach (var boss in _enemies.Where(enemy => enemy.IsBoss && enemy.IsAlive && BossLeaveRules.For(enemy.Kind).HasFlag(rule)).ToArray())
            BossLeaves(boss, moment == WorldEventMoment.Dawn ? "dawn breaks" : "dusk falls");
        if (moment == WorldEventMoment.Dawn)
            _nightBossIds.Clear();
    }

    /// <summary>Per tick: bosses that lost their target, or whose biome the player left, give up and leave.</summary>
    private void UpdateBossLeaving(float deltaSeconds)
    {
        var bosses = _enemies.Where(enemy => enemy.IsBoss && enemy.IsAlive).ToArray();
        if (bosses.Length == 0)
        {
            _bossFarSeconds.Clear();
            _bossBlindSeconds.Clear();
            _bossOutOfBiomeSeconds = 0f;
            return;
        }
        var center = PlayerCenter;
        var outOfBlight = CurrentBiome.Zone is not (BiomeZone.RadiatedDeeps or BiomeZone.Overgrowth);
        _bossOutOfBiomeSeconds = outOfBlight ? _bossOutOfBiomeSeconds + deltaSeconds : 0f;
        foreach (var boss in bosses)
        {
            var rule = BossLeaveRules.For(boss.Kind);
            if (rule.HasFlag(BossLeaveRule.LeaveBiome) && _bossOutOfBiomeSeconds >= BossLeaveRules.OutOfBiomeSeconds)
            {
                BossLeaves(boss, "you left the blight");
                continue;
            }
            if (!rule.HasFlag(BossLeaveRule.OnTargetLost))
                continue;
            var bossCenter = boss.Motion.Position + boss.Motion.Size / 2f;
            var distance = Vector2.Distance(bossCenter, center);
            var far = distance > BossLeaveRules.TargetLostDistance ? _bossFarSeconds.GetValueOrDefault(boss.Id) + deltaSeconds : 0f;
            _bossFarSeconds[boss.Id] = far;
            var blind = distance > BossLeaveRules.SightCheckDistance && !ClearSight(bossCenter, center)
                ? _bossBlindSeconds.GetValueOrDefault(boss.Id) + deltaSeconds
                : 0f;
            _bossBlindSeconds[boss.Id] = blind;
            if (far >= BossLeaveRules.TargetLostSeconds || blind >= BossLeaveRules.SightLostSeconds)
                BossLeaves(boss, "it lost track of you");
        }
    }

    /// <summary>True when no solid tile blocks the straight line between two points (half-tile steps).</summary>
    private bool ClearSight(Vector2 from, Vector2 to)
    {
        var delta = to - from;
        var steps = (int)MathF.Ceiling(delta.Length() * 2f);
        for (var step = 1; step < steps; step++)
        {
            var point = from + delta * (step / (float)steps);
            var x = (int)MathF.Floor(point.X);
            var y = (int)MathF.Floor(point.Y);
            if (!World.Contains(x, y))
                continue;
            var cell = World.GetCell(new GridPoint(x, y));
            if (cell.TileId != TileId.Air && Tiles[cell.TileId].IsSolid && !cell.IsInactive)
                return false;
        }
        return true;
    }

    /// <summary>
    /// A boss leaves without being beaten: "X has left" (with the reason), a despawn record (not a defeat), no drops. A
    /// running event whose finale it was re-arms, so the next event kill calls it back.
    /// </summary>
    private void BossLeaves(EnemyAgent boss, string reason)
    {
        RecordDeath(boss, DeathReason.Despawned);
        _enemies.Remove(boss);
        _nightBossIds.Remove(boss.Id);
        _bossFarSeconds.Remove(boss.Id);
        _bossBlindSeconds.Remove(boss.Id);
        if (boss.EventTag is { } tag)
            _worldEvents.RearmFinales(tag);
        PostMessage($"The {boss.DisplayName} has left ({reason}).");
    }
}
