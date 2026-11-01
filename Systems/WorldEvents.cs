using Rimtera.Entities;

namespace Rimtera.Systems;

/// <summary>World events of the content expansion (append-only; saved as bytes).</summary>
public enum WorldEventKind : byte
{
    Rain,
    AcidRain,
    DustStorm,
    Blizzard,
    RustMoon,
    DarkSun,
    SwarmRain,
    ScavengerRaid,
    PirateBoarding,
    DefenseProtocol,
    PrecursorProbes,
    AuroraNight,
    ColonyFestival,
    MeteorFall,
    // ---- Hardmode endgame: the four obelisk sieges after the Void Cantor (append-only) ----
    EmberObelisk,
    RiftObelisk,
    GaleObelisk,
    ShardObelisk,
    // ---- World & leisure (Systems/GameSimulation.Seasons.cs): two wave moons, the cryo brigade and windy days ----
    HollowMoon,
    RimeMoon,
    CryoBrigade,
    WindyDay,
    // ---- Creature coverage (Systems/CreatureCoverage.cs): the light-hoard event (append-only) ----
    LumenTempest
}

public enum WorldEventCategory : byte
{
    Weather,
    Night,
    Day,
    Invasion,
    Calm,
    Party,
    World
}

/// <summary>When an event may start naturally.</summary>
public enum WorldEventMoment : byte
{
    /// <summary>Only started by an item, trigger or weather.</summary>
    Never,
    Dawn,
    Dusk
}

/// <summary>What the event system needs from the world (implemented by <see cref="GameSimulation"/>).</summary>
public interface IWorldEventHost
{
    WorldFlags Flags { get; }
    int Day { get; }
    bool IsNight { get; }
    BiomeZone CurrentZone { get; }
    WeatherState Weather { get; }
    int TownNpcCount { get; }
    /// <summary>Spawns <paramref name="count"/> creatures from the roster near the player, tagged with the event.</summary>
    int SpawnEventCreatures(WorldEventKind kind, IReadOnlyList<(EnemyKind Kind, float Weight)> roster, int count);
    /// <summary>Spawns a boss tagged with the event; returns false if one is already active.</summary>
    bool SpawnEventBoss(WorldEventKind kind, EnemyKind boss);
    void GrantRewards(IReadOnlyList<(ItemId Item, int Count)> rewards);
    void Announce(string message);
    void MeteorStrike();
    void ApplyWeatherEvent(WorldEventKind kind, bool active);
}

/// <summary>
/// The shared event shape (CONTENT_EXPANSION.md §2.5, FISHING_EVENTS_WEATHER.md §3.4): eligibility,
/// natural roll, duration or kill quota, waves, a spawn-table override, rewards, cooldown and save state.
/// </summary>
public sealed record WorldEventSpec(
    WorldEventKind Kind,
    string Name,
    WorldEventCategory Category,
    WorldEventMoment Moment,
    float Chance,
    float DurationHours,
    Func<IWorldEventHost, bool> Eligible,
    (EnemyKind Kind, float Weight)[] Roster,
    float SpawnMultiplier = 1f,
    float PopulationMultiplier = 1f,
    bool NoHostileSpawns = false,
    int Waves = 0,
    int KillsPerWave = 0,
    EnemyKind? FinalBoss = null,
    (ItemId Item, int Count)[]? Rewards = null,
    int CooldownDays = 2,
    bool SheltersTown = false)
{
    public bool IsInvasion => Waves > 0;
    public string FlagName => "event." + WorldFlags.Key(Kind) + ".cleared";

    /// <summary>
    /// The boundary that ends the event whatever time it has left, as in Terraria: night events (the moons) end at dawn,
    /// day events (the Dark Sun, Swarm Rain) at dusk. Null for the rest.
    /// </summary>
    public WorldEventMoment? EndsAt => Category switch
    {
        WorldEventCategory.Night => WorldEventMoment.Dawn,
        WorldEventCategory.Day => WorldEventMoment.Dusk,
        _ => null
    };

    /// <summary>
    /// Whether running out of time counts as clearing it (weather, calm nights, festivals). Moons, the Dark Sun and
    /// invasions are cleared only by beating them; outlasting them is not a clear.
    /// </summary>
    public bool ClearsOnTimeout => EndsAt is null && !IsInvasion && KillsPerWave == 0;
}

/// <summary>Live state of one running event (saved).</summary>
public sealed record ActiveEventState(
    WorldEventKind Kind,
    float HoursLeft,
    int Wave,
    int Kills,
    int KillsNeeded,
    bool BossSpawned,
    bool Forced);

/// <summary>Progress line for a UI bar.</summary>
public readonly record struct WorldEventProgress(WorldEventKind Kind, string Name, float Progress, int Wave, int Waves, float HoursLeft,
    WorldEventCategory Category);

public sealed record WorldEventSnapshot(ActiveEventState[] Active, Dictionary<WorldEventKind, int> LastEndedDay, Dictionary<WorldEventKind, int> Completions);

/// <summary>Runs every content-expansion world event.</summary>
public sealed class WorldEventSystem
{
    /// <summary>Creatures of an obelisk's own roster that must fall before its shield drops.</summary>
    public const int ObeliskShieldKills = 20;

    private readonly int _seed;
    private readonly Dictionary<WorldEventKind, WorldEventSpec> _specs;
    private readonly List<ActiveEventState> _active = new();
    private readonly Dictionary<WorldEventKind, int> _lastEnded = new();
    private readonly Dictionary<WorldEventKind, int> _completions = new();

    public WorldEventSystem(int seed)
    {
        _seed = seed;
        _specs = BuildSpecs().ToDictionary(spec => spec.Kind);
    }

    public IReadOnlyCollection<WorldEventSpec> Specs => _specs.Values;
    public WorldEventSpec Spec(WorldEventKind kind) => _specs[kind];
    public IReadOnlyList<ActiveEventState> Active => _active;
    public bool IsActive(WorldEventKind kind) => _active.Any(state => state.Kind == kind);
    public int Completions(WorldEventKind kind) => _completions.GetValueOrDefault(kind);

    /// <summary>True while a night, day or invasion event runs (sleeping cannot skip these).</summary>
    public bool BlocksSleep => _active.Any(state => _specs[state.Kind].Category is WorldEventCategory.Night or WorldEventCategory.Day or
        WorldEventCategory.Invasion);

    /// <summary>Progress for every running event (name, 0–1, wave).</summary>
    public IReadOnlyList<WorldEventProgress> Progress => _active.Select(state =>
    {
        var spec = _specs[state.Kind];
        float progress;
        if (spec.IsInvasion)
            progress = Math.Clamp((state.Wave - 1 + (state.KillsNeeded == 0 ? 1f : state.Kills / (float)state.KillsNeeded)) /
                                  (spec.Waves + (spec.FinalBoss is null ? 0 : 1)), 0f, 1f);
        else if (spec.KillsPerWave > 0)
            progress = Math.Clamp(state.Kills / (float)Math.Max(1, state.KillsNeeded), 0f, 1f);
        else
            progress = Math.Clamp(1f - state.HoursLeft / Math.Max(0.01f, spec.DurationHours), 0f, 1f);
        return new WorldEventProgress(state.Kind, spec.Name, progress, state.Wave, spec.Waves, state.HoursLeft, spec.Category);
    }).ToArray();

    /// <summary>Spawn multipliers and whether hostile spawning is suppressed.</summary>
    public (float Spawn, float Population, bool NoHostiles, bool Shelter) Modifiers
    {
        get
        {
            float spawn = 1f, population = 1f;
            bool none = false, shelter = false;
            foreach (var state in _active)
            {
                var spec = _specs[state.Kind];
                spawn *= spec.SpawnMultiplier;
                population *= spec.PopulationMultiplier;
                none |= spec.NoHostileSpawns;
                shelter |= spec.SheltersTown;
            }
            return (spawn, population, none, shelter);
        }
    }

    /// <summary>Picks a creature from a running event's roster, or null to keep the biome roster.</summary>
    public EnemyKind? PickSpawn(double roll)
    {
        var rosters = _active.Select(state => _specs[state.Kind]).Where(spec => spec.Roster.Length > 0).ToArray();
        if (rosters.Length == 0 || roll >= 0.7)
            return null;
        var spec = rosters[(int)(roll / 0.7 * rosters.Length) % rosters.Length];
        return PickFrom(spec.Roster, roll / 0.7 * rosters.Length % 1.0);
    }

    public static EnemyKind PickFrom(IReadOnlyList<(EnemyKind Kind, float Weight)> roster, double roll)
    {
        var total = roster.Sum(entry => entry.Weight);
        var target = Math.Clamp(roll, 0d, 0.999999d) * total;
        foreach (var entry in roster)
        {
            target -= entry.Weight;
            if (target < 0d)
                return entry.Kind;
        }
        return roster[^1].Kind;
    }

    /// <summary>Deterministic roll in [0,1) for a natural start (seed, day, boundary, event).</summary>
    public double NaturalRoll(int day, WorldEventMoment moment, WorldEventKind kind)
    {
        unchecked
        {
            var hash = (uint)_seed * 2654435761u ^ (uint)day * 2246822519u ^ (uint)moment * 374761393u ^ ((uint)kind + 1u) * 668265263u;
            hash ^= hash >> 15;
            hash *= 2246822519u;
            hash ^= hash >> 13;
            hash *= 3266489917u;
            hash ^= hash >> 16;
            return (hash & 0xFFFFFF) / (double)0x1000000;
        }
    }

    /// <summary>True when a natural or forced start is allowed (eligible and off cooldown).</summary>
    public bool CanStart(WorldEventKind kind, IWorldEventHost host, bool forced, out string reason)
    {
        var spec = _specs[kind];
        reason = string.Empty;
        if (IsActive(kind))
        {
            reason = $"{spec.Name} is already under way.";
            return false;
        }
        if (_active.Any(state => _specs[state.Kind].IsInvasion) && spec.IsInvasion)
        {
            reason = "Another invasion is already under way.";
            return false;
        }
        if (!spec.Eligible(host))
        {
            reason = $"The conditions for {spec.Name} are not met.";
            return false;
        }
        if (!forced && _lastEnded.TryGetValue(kind, out var ended) && host.Day - ended < spec.CooldownDays)
        {
            reason = $"{spec.Name} happened too recently.";
            return false;
        }
        return true;
    }

    public bool TryStart(WorldEventKind kind, IWorldEventHost host, bool forced, out string reason)
    {
        if (!CanStart(kind, host, forced, out reason))
            return false;
        var spec = _specs[kind];
        var state = new ActiveEventState(kind, spec.DurationHours, spec.IsInvasion ? 1 : 0, 0,
            spec.IsInvasion ? spec.KillsPerWave : spec.KillsPerWave, false, forced);
        _active.Add(state);
        host.Announce($"Event started: {spec.Name}.");
        if (spec.Category == WorldEventCategory.Weather)
            host.ApplyWeatherEvent(kind, true);
        if (kind == WorldEventKind.MeteorFall)
            host.MeteorStrike();
        if (spec.IsInvasion)
            host.SpawnEventCreatures(kind, spec.Roster, spec.KillsPerWave);
        reason = string.Empty;
        return true;
    }

    /// <summary>Advances durations, runs natural rolls on a boundary, and syncs weather-driven events.</summary>
    public void Advance(float hours, WorldEventMoment? boundary, IWorldEventHost host)
    {
        foreach (var state in _active.ToArray())
        {
            var spec = _specs[state.Kind];
            var next = state with { HoursLeft = state.HoursLeft - hours };
            Replace(state, next);
            // Invasions end by clearing waves, not by time (they give up after a long while). Moons end at dawn and day
            // events at dusk, whatever time they have left. Neither way counts as a clear unless the event clears on timeout.
            var expired = next.HoursLeft <= 0f && (!spec.IsInvasion || next.HoursLeft <= -spec.DurationHours * 2f) ||
                          boundary is { } passed && spec.EndsAt == passed;
            if (expired)
                End(next, host, completed: spec.ClearsOnTimeout);
        }

        SyncWeather(host);

        if (boundary is not { } moment)
            return;
        foreach (var spec in _specs.Values.Where(spec => spec.Moment == moment).OrderBy(spec => spec.Kind))
        {
            if (spec.Chance <= 0f || NaturalRoll(host.Day, moment, spec.Kind) >= spec.Chance)
                continue;
            if (TryStart(spec.Kind, host, forced: false, out _))
                break;
        }
    }

    /// <summary>Weather events follow the weather state (and a started weather event pushes the weather).</summary>
    private void SyncWeather(IWorldEventHost host)
    {
        var weather = host.Weather;
        void Sync(WorldEventKind kind, bool wanted)
        {
            var active = _active.FirstOrDefault(state => state.Kind == kind);
            if (wanted && active is null && _specs[kind].Eligible(host))
                _active.Add(new ActiveEventState(kind, _specs[kind].DurationHours, 0, 0, 0, false, false));
            else if (!wanted && active is not null && !active.Forced)
                End(active, host, completed: true, quiet: true);
        }
        Sync(WorldEventKind.Rain, weather.Rain > 0.35f && !weather.AcidRain);
        Sync(WorldEventKind.AcidRain, weather.Rain > 0.35f && weather.AcidRain);
        Sync(WorldEventKind.Blizzard, weather.Snow > 0.5f);
        Sync(WorldEventKind.DustStorm, weather.Dust > 0.45f);
    }

    /// <summary>Counts an event-tagged kill: advances quotas, waves and final bosses.</summary>
    public void RecordKill(WorldEventKind kind, EnemyKind killed, bool wasBoss, IWorldEventHost host)
    {
        if (host is GameSimulation seasonal && seasonal.RecordSeasonKill(kind, killed, wasBoss)) return; // wave moons score their own kills
        var state = _active.FirstOrDefault(candidate => candidate.Kind == kind);
        if (state is null)
            return;
        var spec = _specs[kind];
        if (wasBoss && spec.FinalBoss == killed)
        {
            End(state, host, completed: true);
            return;
        }
        if (spec.KillsPerWave <= 0)
            return;
        var next = state with { Kills = state.Kills + 1 };
        Replace(state, next);
        if (next.Kills < next.KillsNeeded)
            return;

        if (spec.IsInvasion)
        {
            if (next.Wave < spec.Waves)
            {
                var wave = next.Wave + 1;
                var needed = spec.KillsPerWave + (wave - 1) * Math.Max(2, spec.KillsPerWave / 3);
                Replace(next, next with { Wave = wave, Kills = 0, KillsNeeded = needed });
                host.Announce($"{spec.Name}: wave {wave} of {spec.Waves}!");
                host.SpawnEventCreatures(kind, spec.Roster, needed);
            }
            else if (spec.FinalBoss is { } boss && !next.BossSpawned)
            {
                Replace(next, next with { BossSpawned = true, Kills = 0, KillsNeeded = 1, Wave = spec.Waves + 1 });
                host.SpawnEventBoss(kind, boss);
            }
            else if (spec.FinalBoss is null)
                End(next, host, completed: true);
        }
        else if (spec.FinalBoss is { } boss && !next.BossSpawned)
        {
            // Quota events (Swarm Rain, Rust Moon, Dark Sun) summon their boss and keep running.
            Replace(next, next with { BossSpawned = true, Wave = next.Wave + 1 });
            host.SpawnEventBoss(kind, boss);
        }
    }

    /// <summary>Ends an event (completed events grant rewards and set the first-clear flag).</summary>
    public void End(ActiveEventState state, IWorldEventHost host, bool completed, bool quiet = false)
    {
        if (!_active.Remove(state))
            return;
        var spec = _specs[state.Kind];
        _lastEnded[state.Kind] = host.Day;
        if (spec.Category == WorldEventCategory.Weather)
            host.ApplyWeatherEvent(state.Kind, false);
        if (completed)
        {
            _completions[state.Kind] = _completions.GetValueOrDefault(state.Kind) + 1;
            if (spec.Rewards is { Length: > 0 } rewards)
                host.GrantRewards(rewards);
            host.Flags.Set(spec.FlagName);
        }
        if (!quiet)
            host.Announce(completed ? $"Event cleared: {spec.Name}." : $"Event ended: {spec.Name}.");
    }

    /// <summary>Stops an event by kind (commands, tests).</summary>
    public bool Stop(WorldEventKind kind, IWorldEventHost host)
    {
        var state = _active.FirstOrDefault(candidate => candidate.Kind == kind);
        if (state is null)
            return false;
        End(state with { Forced = false }, host, completed: false);
        _active.Remove(state);
        return true;
    }

    private void Replace(ActiveEventState old, ActiveEventState next)
    {
        var index = _active.IndexOf(old);
        if (index >= 0)
            _active[index] = next;
    }

    public WorldEventSnapshot Capture() => new(_active.ToArray(), new Dictionary<WorldEventKind, int>(_lastEnded),
        new Dictionary<WorldEventKind, int>(_completions));

    public void Restore(WorldEventSnapshot? snapshot)
    {
        _active.Clear();
        _lastEnded.Clear();
        _completions.Clear();
        if (snapshot is null)
            return;
        foreach (var state in snapshot.Active ?? Array.Empty<ActiveEventState>())
        {
            if (state is null || !Enum.IsDefined(state.Kind) || !float.IsFinite(state.HoursLeft) || state.Kills < 0)
                throw new InvalidDataException("A saved world event is invalid.");
            _active.Add(state);
        }
        foreach (var (kind, day) in snapshot.LastEndedDay ?? new())
            _lastEnded[kind] = day;
        foreach (var (kind, count) in snapshot.Completions ?? new())
            _completions[kind] = count;
    }

    private static IEnumerable<WorldEventSpec> BuildSpecs()
    {
        static bool Always(IWorldEventHost _) => true;
        foreach (var seasonal in SeasonalEvents.Specs()) yield return seasonal; // GameSimulation.Seasons.cs
        // Creature coverage: rain brings its own creatures (Terraria's umbrella slimes, flying fish and raincoat zombies).
        yield return new(WorldEventKind.Rain, "Rainfall", WorldEventCategory.Weather, WorldEventMoment.Never, 0f, 8f, Always,
            [(EnemyKind.DrizzleGel, 0.4f), (EnemyKind.RainSkipper, 0.35f), (EnemyKind.SlickerHusk, 0.25f)],
            SpawnMultiplier: 1.1f, CooldownDays: 0);
        yield return new(WorldEventKind.AcidRain, "Acid rain", WorldEventCategory.Weather, WorldEventMoment.Never, 0f, 6f, Always,
            [(EnemyKind.EventAcidSlime, 0.5f), (EnemyKind.EventAcidBird, 0.3f), (EnemyKind.EventToxicCloud, 0.2f)],
            SpawnMultiplier: 1.2f, CooldownDays: 0, SheltersTown: true);
        yield return new(WorldEventKind.DustStorm, "Dust storm", WorldEventCategory.Weather, WorldEventMoment.Never, 0f, 6f, Always,
            [(EnemyKind.EventDustDevil, 0.3f), (EnemyKind.EventSandRay, 0.25f), (EnemyKind.EventScrapElemental, 0.15f), (EnemyKind.EventDustRaider, 0.3f)],
            SpawnMultiplier: 1.35f, PopulationMultiplier: 1.25f, CooldownDays: 0);
        yield return new(WorldEventKind.Blizzard, "Blizzard", WorldEventCategory.Weather, WorldEventMoment.Never, 0f, 6f, Always,
            // Creature coverage: whiteout wisps and drift stalkers ride the storm beside the frozen natives.
            [(EnemyKind.WhiteoutWisp, 0.3f), (EnemyKind.DriftStalker, 0.25f), (EnemyKind.SnowWolf, 0.25f), (EnemyKind.Cryopterous, 0.2f)],
            SpawnMultiplier: 1.15f,
            CooldownDays: 0, SheltersTown: true);
        yield return new(WorldEventKind.RustMoon, "Rust Moon", WorldEventCategory.Night, WorldEventMoment.Dusk, 0.12f, 11f,
            host => host.Flags.IsSet("player.health.120") || host.Flags.IsSet("boss.stone_sentinel.down"),
            [(EnemyKind.EventHusk, 0.35f), (EnemyKind.EventRustWraith, 0.2f), (EnemyKind.EventRustHound, 0.25f),
             (EnemyKind.EventEyeDrone, 0.12f), (EnemyKind.EventCorrodedMech, 0.08f)],
            SpawnMultiplier: 2.2f, PopulationMultiplier: 1.8f, KillsPerWave: 25, FinalBoss: EnemyKind.MoonEater,
            Rewards: [(ItemId.Credits, 120), (ItemId.MoonEaterLure, 1)], CooldownDays: 3, SheltersTown: true);
        yield return new(WorldEventKind.DarkSun, "Dark Sun", WorldEventCategory.Day, WorldEventMoment.Dawn, 0.06f, 9f,
            host => host.Flags.IsSet("blight.fracture"),
            [(EnemyKind.EventEclipseShade, 0.45f), (EnemyKind.EventEclipseReaper, 0.25f), (EnemyKind.EventEclipseMothMech, 0.3f)],
            SpawnMultiplier: 2.4f, PopulationMultiplier: 1.8f, KillsPerWave: 30, FinalBoss: EnemyKind.EclipseLord,
            Rewards: [(ItemId.Credits, 250), (ItemId.VoidEssence, 5)], CooldownDays: 4, SheltersTown: true);
        yield return new(WorldEventKind.SwarmRain, "Swarm Rain", WorldEventCategory.Day, WorldEventMoment.Dawn, 0.08f, 8f,
            host => host.Flags.IsSet("boss.stone_sentinel.down") && host.Weather.Rain > 0.2f,
            // Creature coverage: armored swarmers join the falling swarm (prism swarmers after the Fracture, GameSimulation.Hardmode.cs).
            [(EnemyKind.EventSwarmer, 0.65f), (EnemyKind.ArmoredSwarmer, 0.2f), (EnemyKind.EventSwarmQueen, 0.05f), (EnemyKind.MossSlime, 0.1f)],
            SpawnMultiplier: 2.6f, PopulationMultiplier: 2f, KillsPerWave: 30, FinalBoss: EnemyKind.AlphaSkitter,
            Rewards: [(ItemId.Credits, 80), (ItemId.SummonSwarmPheromone, 1)], CooldownDays: 3);
        yield return new(WorldEventKind.ScavengerRaid, "Scavenger Raid", WorldEventCategory.Invasion, WorldEventMoment.Dusk, 0.1f, 12f,
            host => host.Flags.Get("hearts.broken") > 0,
            [(EnemyKind.EventScavengerGrunt, 0.4f), (EnemyKind.EventScavengerBrute, 0.15f), (EnemyKind.EventScavengerSniper, 0.2f),
             (EnemyKind.EventScavengerRigger, 0.15f), (EnemyKind.EventScout, 0.1f)],
            Waves: 3, KillsPerWave: 8, FinalBoss: EnemyKind.ScavengerWarlord,
            Rewards: [(ItemId.Credits, 200), (ItemId.ScrapCrate, 2)], CooldownDays: 3, SheltersTown: true);
        yield return new(WorldEventKind.PirateBoarding, "Pirate Boarding", WorldEventCategory.Invasion, WorldEventMoment.Dusk, 0.05f, 12f,
            host => host.Flags.IsSet("blight.fracture"),
            [(EnemyKind.EventPirateDeckhand, 0.45f), (EnemyKind.EventPirateGunner, 0.3f), (EnemyKind.EventPirateParrotDrone, 0.15f),
             (EnemyKind.EventPirateCannon, 0.1f)],
            Waves: 3, KillsPerWave: 10, FinalBoss: EnemyKind.PirateDreadnought,
            Rewards: [(ItemId.Credits, 500), (ItemId.AlloyCrate, 2), (ItemId.BountyNotice, 1)], CooldownDays: 4, SheltersTown: true);
        yield return new(WorldEventKind.DefenseProtocol, "Defense Protocol", WorldEventCategory.Invasion, WorldEventMoment.Never, 0f, 12f,
            Always,
            [(EnemyKind.EventProtocolGrunt, 0.4f), (EnemyKind.EventProtocolShieldbearer, 0.2f), (EnemyKind.EventProtocolWyvern, 0.15f),
             (EnemyKind.EventProtocolOgre, 0.1f), (EnemyKind.EventProtocolMage, 0.15f)],
            Waves: 3, KillsPerWave: 10, FinalBoss: EnemyKind.ProtocolDragon,
            Rewards: [(ItemId.Credits, 300), (ItemId.CircuitBoard, 8)], CooldownDays: 0, SheltersTown: true);
        yield return new(WorldEventKind.PrecursorProbes, "Precursor Probes", WorldEventCategory.Invasion, WorldEventMoment.Dusk, 0.05f, 12f,
            host => host.Flags.IsSet("ore.voidsteel"),
            [(EnemyKind.EventPrecursorProbe, 0.35f), (EnemyKind.EventProbeGunner, 0.3f), (EnemyKind.EventProbeTeslaWalker, 0.15f),
             (EnemyKind.EventProbeBrainDrone, 0.2f)],
            Waves: 3, KillsPerWave: 12, FinalBoss: EnemyKind.MothershipCore,
            Rewards: [(ItemId.Credits, 800), (ItemId.AstralCrate, 1)], CooldownDays: 5, SheltersTown: true);
        yield return new(WorldEventKind.AuroraNight, "Aurora Night", WorldEventCategory.Calm, WorldEventMoment.Dusk, 0.1f, 10f,
            host => host.Weather.Rain < 0.2f && host.Weather.CloudCover < 0.6f,
            [(EnemyKind.EventAuroraWisp, 1f)], NoHostileSpawns: true, CooldownDays: 4);
        yield return new(WorldEventKind.ColonyFestival, "Colony Festival", WorldEventCategory.Party, WorldEventMoment.Dawn, 0.08f, 12f,
            host => host.TownNpcCount >= 3,
            [(EnemyKind.EventFestivalBalloonDrone, 1f)], NoHostileSpawns: true,
            Rewards: [(ItemId.FestivalMask, 1), (ItemId.NanoBananaSplit, 3), (ItemId.Credits, 50)], CooldownDays: 5);
        // The obelisk sieges: started together when the Void Cantor falls; each obelisk's shield holds until
        // ObeliskShieldKills of its roster die (kill quota, no final boss — the obelisk itself ends the event). Creature coverage:
        // each siege fields three natives of its own (Terraria's pillars each have their own roster).
        static bool CantorDown(IWorldEventHost host) => host.Flags.IsSet("boss.void_cantor.down");
        yield return new(WorldEventKind.EmberObelisk, "Ember obelisk siege", WorldEventCategory.Invasion, WorldEventMoment.Never, 0f, 48f,
            CantorDown, [(EnemyKind.CinderImp, 0.4f), (EnemyKind.SlagCentipede, 0.25f), (EnemyKind.PyreLancer, 0.35f)],
            SpawnMultiplier: 1.6f, PopulationMultiplier: 1.5f, KillsPerWave: ObeliskShieldKills, CooldownDays: 0);
        yield return new(WorldEventKind.RiftObelisk, "Rift obelisk siege", WorldEventCategory.Invasion, WorldEventMoment.Never, 0f, 48f,
            CantorDown, [(EnemyKind.RiftSeer, 0.3f), (EnemyKind.MindLeech, 0.4f), (EnemyKind.RiftFloater, 0.3f)],
            SpawnMultiplier: 1.6f, PopulationMultiplier: 1.5f, KillsPerWave: ObeliskShieldKills, CooldownDays: 0);
        yield return new(WorldEventKind.GaleObelisk, "Gale obelisk siege", WorldEventCategory.Invasion, WorldEventMoment.Never, 0f, 48f,
            CantorDown, [(EnemyKind.GaleDiver, 0.35f), (EnemyKind.SquallHornet, 0.35f), (EnemyKind.TempestLarva, 0.3f)],
            SpawnMultiplier: 1.6f, PopulationMultiplier: 1.5f, KillsPerWave: ObeliskShieldKills, CooldownDays: 0);
        yield return new(WorldEventKind.ShardObelisk, "Shard obelisk siege", WorldEventCategory.Invasion, WorldEventMoment.Never, 0f, 48f,
            CantorDown, [(EnemyKind.StarCell, 0.4f), (EnemyKind.LatticeWeaver, 0.2f), (EnemyKind.ShardInvader, 0.4f)],
            SpawnMultiplier: 1.6f, PopulationMultiplier: 1.5f, KillsPerWave: ObeliskShieldKills, CooldownDays: 0);
        // Creature coverage: the Lumen Tempest (torch-god analog). A light hoard deep underground calls swarms of light
        // creatures; clearing the swarm grants the Lumen accord the first time (GameSimulation.CreatureCoverage.cs).
        yield return new(WorldEventKind.LumenTempest, "Lumen Tempest", WorldEventCategory.World, WorldEventMoment.Never, 0f, 2f, Always,
            [(EnemyKind.LumenFlare, 0.6f), (EnemyKind.GlareMote, 0.4f)], SpawnMultiplier: 1.5f,
            Waves: 1, KillsPerWave: 12, Rewards: [(ItemId.GlowSpore, 10), (ItemId.Credits, 40)], CooldownDays: 1);
        yield return new(WorldEventKind.MeteorFall, "Meteor fall", WorldEventCategory.World, WorldEventMoment.Dusk, 0f, 2f,
            Always, [(EnemyKind.EventMeteorHead, 0.6f), (EnemyKind.EventMeteorCrawler, 0.4f)], SpawnMultiplier: 1.4f, CooldownDays: 0);
    }
}
