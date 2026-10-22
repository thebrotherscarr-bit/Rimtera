using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Live score of a wave moon (HUD and tests).</summary>
public readonly record struct MoonProgress(WorldEventKind Kind, string Name, int Wave, int Waves, int Points, int PointsNeeded);

/// <summary>
/// The seasonal events of the world &amp; leisure pass: two item-summoned wave moons (the Hollow Moon and the Rime Moon: a night of
/// scored waves where each kill earns points, a wave clears at a points threshold, mini-bosses join from the middle waves and a
/// boss from the final waves, and the wave reached sets the loot tier), the Cryo Brigade invasion (a kill-count siege) and the
/// windy day. Event specs plug into <see cref="WorldEventSystem"/>; wave scoring lives in <see cref="GameSimulation"/>.
/// Each moon, the brigade and the windy day have their own creatures (docs/CREATURE_COVERAGE.md); every name and number is original.
/// </summary>
public static class SeasonalEvents
{
    public const int MoonWaves = 10;
    public const int MiniBossWave = 4;
    public const int BossWave = 8;
    public const int BrigadeKills = 30;

    public static bool IsMoon(WorldEventKind kind) => kind is WorldEventKind.HollowMoon or WorldEventKind.RimeMoon;

    public static IEnumerable<WorldEventSpec> Specs()
    {
        static bool Fractured(IWorldEventHost host) => host.Flags.IsSet("blight.fracture");
        static bool MoonNight(IWorldEventHost host) => host.IsNight && Fractured(host);
        yield return new(WorldEventKind.HollowMoon, "Hollow Moon", WorldEventCategory.Night, WorldEventMoment.Never, 0f, 9f, MoonNight,
            [], NoHostileSpawns: true, CooldownDays: 0, SheltersTown: true);
        yield return new(WorldEventKind.RimeMoon, "Rime Moon", WorldEventCategory.Night, WorldEventMoment.Never, 0f, 9f, MoonNight,
            [], NoHostileSpawns: true, CooldownDays: 0, SheltersTown: true);
        yield return new(WorldEventKind.CryoBrigade, "Cryo Brigade", WorldEventCategory.Invasion, WorldEventMoment.Never, 0f, 12f, Fractured,
            // Creature coverage: the brigade's own troops (Terraria's frost legion: gangsta, stabby, balla analogs).
            [(EnemyKind.BrigadeTrooper, 0.45f), (EnemyKind.BrigadeShiv, 0.3f), (EnemyKind.BrigadeLobber, 0.25f)],
            Waves: 1, KillsPerWave: BrigadeKills,
            Rewards: [(ItemId.Credits, 150), (ItemId.RimeShard, 6), (ItemId.RimeBeacon, 1)], CooldownDays: 0, SheltersTown: true);
        yield return new(WorldEventKind.WindyDay, "Windy day", WorldEventCategory.Calm, WorldEventMoment.Dawn, 0.1f, 10f,
            // Creature coverage: drifting thistledown and gust mites ride the wind (Terraria's windy-day dandelions).
            host => host.Weather.Rain < 0.3f && host.Weather.Snow < 0.3f, [(EnemyKind.ThistledownDrifter, 0.6f), (EnemyKind.GustMite, 0.4f)],
            CooldownDays: 2);
    }

    /// <summary>Points that clear a wave (rising per wave).</summary>
    public static int WaveThreshold(int wave) => 12 + wave * 8;

    /// <summary>Wave of the second mini-boss (Terraria: the Headless Horseman and Santa-NK1 join from the middle waves).</summary>
    public const int SecondMiniBossWave = 6;

    /// <summary>
    /// Wave rosters: each moon has its own units (creature coverage, docs/CREATURE_COVERAGE.md), heavier ones joining as the
    /// waves rise. Hollow Moon: effigies, splinters, cinder hounds, poltergeists. Rime Moon: sappers, shellcrackers, flurry
    /// flakes, snow hulks and cargo mimics.
    /// </summary>
    public static (EnemyKind Kind, float Weight)[] WaveRoster(WorldEventKind moon, int wave) => moon == WorldEventKind.HollowMoon
        ? wave switch
        {
            <= 3 => [(EnemyKind.HollowEffigy, 0.45f), (EnemyKind.EmberSplinter, 0.4f), (EnemyKind.CinderHound, 0.15f)],
            <= 6 => [(EnemyKind.HollowEffigy, 0.25f), (EnemyKind.EmberSplinter, 0.25f), (EnemyKind.CinderHound, 0.25f), (EnemyKind.StaticPoltergeist, 0.25f)],
            _ => [(EnemyKind.EmberSplinter, 0.2f), (EnemyKind.CinderHound, 0.3f), (EnemyKind.StaticPoltergeist, 0.3f), (EnemyKind.HollowEffigy, 0.2f)]
        }
        : wave switch
        {
            <= 3 => [(EnemyKind.FrostSapper, 0.5f), (EnemyKind.FlurryFlake, 0.3f), (EnemyKind.Shellcracker, 0.2f)],
            <= 6 => [(EnemyKind.FrostSapper, 0.25f), (EnemyKind.FlurryFlake, 0.25f), (EnemyKind.Shellcracker, 0.25f), (EnemyKind.SnowHulk, 0.15f),
                     (EnemyKind.CargoMimic, 0.1f)],
            _ => [(EnemyKind.FlurryFlake, 0.25f), (EnemyKind.Shellcracker, 0.25f), (EnemyKind.SnowHulk, 0.3f), (EnemyKind.CargoMimic, 0.2f)]
        };

    public static EnemyKind MiniBoss(WorldEventKind moon) => moon == WorldEventKind.HollowMoon ? EnemyKind.EmberRootwalker : EnemyKind.FrostspirePine;
    public static EnemyKind SecondMiniBoss(WorldEventKind moon) => moon == WorldEventKind.HollowMoon ? EnemyKind.HollowLancer : EnemyKind.CryoTank;
    public static EnemyKind Boss(WorldEventKind moon) => moon == WorldEventKind.HollowMoon ? EnemyKind.HollowRegent : EnemyKind.RimeSeraph;

    public static int PointsFor(WorldEventKind moon, EnemyKind kind, bool boss)
    {
        if (boss || kind == Boss(moon))
            return 60;
        if (kind == MiniBoss(moon) || kind == SecondMiniBoss(moon))
            return 18;
        var hp = EnemyArchetypes.Contains(kind) ? EnemyArchetypes.Get(kind).HitPoints : 40f;
        return Math.Clamp(1 + (int)(hp / 60f), 1, 6);
    }

    /// <summary>Loot for the wave reached when the moon sets (tier rises with the wave).</summary>
    public static (ItemId Item, int Count)[] TierRewards(WorldEventKind moon, int wave)
    {
        var material = moon == WorldEventKind.HollowMoon ? ItemId.HollowEmber : ItemId.RimeShard;
        var rewards = new List<(ItemId, int)> { (ItemId.Credits, 25 * wave), (material, 2 * wave) };
        if (wave >= MiniBossWave)
            rewards.Add((ItemId.LumeniteBar, wave / 2));
        if (wave >= BossWave)
            rewards.Add((ItemId.VoidEssence, wave - 5));
        if (wave >= MoonWaves)
            rewards.Add((moon == WorldEventKind.HollowMoon ? ItemId.HollowLantern : ItemId.RimeBeacon, 1));
        return rewards.ToArray();
    }
}

public sealed partial class GameSimulation
{
    private sealed class MoonState
    {
        public int Wave = 1;
        public int Points;
        public float SpawnTimer;
        public bool MiniBossSpawned;
        public bool SecondMiniBossSpawned;
        public bool BossSpawned;
        public bool Finished;
    }

    private readonly Dictionary<WorldEventKind, MoonState> _moons = new();

    /// <summary>Score lines for every running wave moon.</summary>
    public IReadOnlyList<MoonProgress> MoonProgress => _moons.Where(pair => _worldEvents.IsActive(pair.Key))
        .Select(pair => new MoonProgress(pair.Key, _worldEvents.Spec(pair.Key).Name, pair.Value.Wave, SeasonalEvents.MoonWaves,
            pair.Value.Points, SeasonalEvents.WaveThreshold(pair.Value.Wave))).ToArray();

    private static string MoonFlag(WorldEventKind kind, string field) => $"moon.{WorldFlags.Key(kind)}.{field}";

    private MoonState Moon(WorldEventKind kind)
    {
        if (_moons.TryGetValue(kind, out var state))
            return state;
        // A moon running when the world was saved resumes at its recorded wave.
        state = new MoonState
        {
            Wave = (int)Math.Clamp(Flags.Get(MoonFlag(kind, "wave")), 1, SeasonalEvents.MoonWaves),
            Points = (int)Math.Max(0, Flags.Get(MoonFlag(kind, "points")))
        };
        _moons[kind] = state;
        return state;
    }

    private void SaveMoon(WorldEventKind kind, MoonState state)
    {
        SetFlagValue(MoonFlag(kind, "wave"), state.Wave);
        SetFlagValue(MoonFlag(kind, "points"), state.Points);
    }

    /// <summary>Wave-moon kill scoring (hooked from <see cref="WorldEventSystem.RecordKill"/>). False for other events.</summary>
    internal bool RecordSeasonKill(WorldEventKind kind, EnemyKind killed, bool wasBoss)
    {
        if (!SeasonalEvents.IsMoon(kind))
            return false;
        if (!_worldEvents.IsActive(kind))
            return true;
        var state = Moon(kind);
        var name = _worldEvents.Spec(kind).Name;
        state.Points += SeasonalEvents.PointsFor(kind, killed, wasBoss);
        if (wasBoss || killed == SeasonalEvents.MiniBoss(kind) || killed == SeasonalEvents.SecondMiniBoss(kind))
            foreach (var (item, count) in SeasonalEvents.TierRewards(kind, Math.Max(1, state.Wave / 2)).Take(2))
                CollectOrDrop(item, count, PlayerCenter);
        while (state.Wave < SeasonalEvents.MoonWaves && state.Points >= SeasonalEvents.WaveThreshold(state.Wave))
        {
            state.Points -= SeasonalEvents.WaveThreshold(state.Wave);
            state.Wave++;
            state.MiniBossSpawned = false;
            state.SecondMiniBossSpawned = false;
            state.BossSpawned = false;
            PostMessage(state.Wave == SeasonalEvents.MoonWaves ? $"{name}: final wave!" : $"{name}: wave {state.Wave}!");
        }
        if (state.Wave == SeasonalEvents.MoonWaves && wasBoss && state.Points >= SeasonalEvents.WaveThreshold(state.Wave))
        {
            state.Finished = true;
            PostMessage($"{name}: you held through the final wave!");
            if (_worldEvents.Active.FirstOrDefault(active => active.Kind == kind) is { } active)
                _worldEvents.End(active, this, completed: true);
        }
        SaveMoon(kind, state);
        return true;
    }

    private void UpdateSeasons(float deltaSeconds)
    {
        foreach (var kind in new[] { WorldEventKind.HollowMoon, WorldEventKind.RimeMoon })
        {
            if (!_worldEvents.IsActive(kind))
            {
                if (_moons.Remove(kind, out var ended))
                    EndMoon(kind, ended);
                continue;
            }
            var state = Moon(kind);
            state.SpawnTimer -= deltaSeconds;
            if (state.SpawnTimer > 0f)
                continue;
            state.SpawnTimer = 1.2f;
            var tagged = _enemies.Where(enemy => enemy.IsAlive && enemy.EventTag == kind).ToArray();
            if (state.Wave >= SeasonalEvents.MiniBossWave && !state.MiniBossSpawned)
            {
                state.MiniBossSpawned = true;
                SpawnSeasonCreature(kind, SeasonalEvents.MiniBoss(kind), 0);
                PostMessage($"A {EnemyArchetypes.Get(SeasonalEvents.MiniBoss(kind)).DisplayName} stalks the {_worldEvents.Spec(kind).Name}!");
            }
            if (state.Wave >= SeasonalEvents.SecondMiniBossWave && !state.SecondMiniBossSpawned)
            {
                state.SecondMiniBossSpawned = true;
                SpawnSeasonCreature(kind, SeasonalEvents.SecondMiniBoss(kind), 1);
                PostMessage($"A {EnemyArchetypes.Get(SeasonalEvents.SecondMiniBoss(kind)).DisplayName} joins the {_worldEvents.Spec(kind).Name}!");
            }
            if (state.Wave >= SeasonalEvents.BossWave && !state.BossSpawned && !tagged.Any(enemy => enemy.IsBoss))
            {
                state.BossSpawned = true;
                ((IWorldEventHost)this).SpawnEventBoss(kind, SeasonalEvents.Boss(kind));
            }
            var wanted = Math.Min(12, 5 + state.Wave / 2);
            if (tagged.Count(enemy => !enemy.IsBoss) < wanted)
            {
                var roster = SeasonalEvents.WaveRoster(kind, state.Wave);
                SpawnSeasonCreature(kind, WorldEventSystem.PickFrom(roster, TriggerRandom.NextDouble()), tagged.Length);
            }
        }
    }

    private EnemyAgent SpawnSeasonCreature(WorldEventKind tag, EnemyKind kind, int index)
    {
        var archetype = EnemyArchetypes.Get(kind);
        var side = index % 2 == 0 ? 1 : -1;
        var x = Math.Clamp((int)MathF.Floor(PlayerCenter.X) + side * ((int)ViewHalfWidth + 2 + index % 5 * 2), 2, World.Width - 3);
        if (!TryFindSpawnGround(x, CurrentBiome.Underground, out var groundY))
            groundY = FindSurface(x);
        var enemy = SpawnCreature(kind, new Vector2(x, groundY - archetype.Size.Y - (archetype.Flying ? 3f : 0f)));
        enemy.EventTag = tag;
        return enemy;
    }

    private void EndMoon(WorldEventKind kind, MoonState state)
    {
        SetFlagValue(MoonFlag(kind, "wave"), 0);
        SetFlagValue(MoonFlag(kind, "points"), 0);
        foreach (var (item, count) in SeasonalEvents.TierRewards(kind, state.Wave))
            CollectOrDrop(item, count, PlayerCenter);
        var name = kind == WorldEventKind.HollowMoon ? "Hollow Moon" : "Rime Moon";
        PostMessage(state.Finished ? $"The {name} sets in triumph." : $"The {name} sets. You reached wave {state.Wave}.");
        // Stragglers melt away at dawn.
        foreach (var enemy in _enemies.Where(enemy => enemy.IsAlive && enemy.EventTag == kind && !enemy.IsBoss))
            enemy.EventTag = null;
    }
}
