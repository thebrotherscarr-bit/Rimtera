using Rimtera.AI;
using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Real-date seasonal windows that swap a few spawns, quest fish and event odds.</summary>
public enum SeasonWindow : byte
{
    None,
    /// <summary>Harvest festival (1 Oct – 10 Nov): festivals likelier, harvest decorations.</summary>
    HarvestFestival,
    /// <summary>Starfall (10 Dec – 6 Jan): meteor falls likelier, starfall minnows bite.</summary>
    Starfall
}

/// <summary>Content-expansion save state; one trailing optional field on <see cref="GameSnapshot"/>.</summary>
public sealed record ContentSaveState(
    float Charge = -1f,
    ContainerSnapshot[]? Containers = null,
    WorldEventSnapshot? Events = null,
    WeatherState? Weather = null,
    Dictionary<string, long>? Flags = null,
    TownNpcSnapshot[]? TownNpcs = null,
    FishingSaveState? Fishing = null,
    float AstralCountdown = 0f);

/// <summary>
/// World flags and triggers, the event framework host, weather effects, time rate and seasons
/// (CONTENT_EXPANSION.md §2.5 and FISHING_EVENTS_WEATHER.md §3.2–3.4).
/// </summary>
public sealed partial class GameSimulation : IWorldEventHost
{
    public const float AstralCountdownSeconds = 60f;
    public const float SleepTimeRate = 4f;

    private WorldEventSystem? _worldEventsField;
    private WeatherSystem? _weatherField;
    private WorldFlags? _flagsField;
    private Random? _triggerRandomField;
    private Random? _weatherRandomField;
    private readonly List<LightningStrike> _lightning = new();
    private float _astralCountdown;
    private bool _sleeping;
    private bool _triggersWired;

    private WorldEventSystem _worldEvents => _worldEventsField ??= new WorldEventSystem(Seed ^ 0x3E7E47) { InvasionsByStoryteller = true };
    private WeatherSystem _weather => _weatherField ??= new WeatherSystem(Seed ^ 0x3EA7E5);
    private Random TriggerRandom => _triggerRandomField ??= new Random(Seed ^ 0x7216E5);
    private Random WeatherRandom => _weatherRandomField ??= new Random(Seed ^ 0x11F7A1);

    /// <summary>Persisted progression flags and counters with first-clear hooks.</summary>
    public WorldFlags Flags
    {
        get
        {
            if (_flagsField is null)
            {
                _flagsField = new WorldFlags();
                WireTriggers();
            }
            return _flagsField;
        }
    }

    /// <summary>The event framework (natural rolls, waves, quotas, rewards).</summary>
    public WorldEventSystem WorldEvents => _worldEvents;
    /// <summary>Progress of every running content event, for an invasion/event bar.</summary>
    public IReadOnlyList<WorldEventProgress> EventProgress => _worldEvents.Progress;
    /// <summary>Current weather (cloud, rain, wind, storm, dust, fog, snow).</summary>
    public WeatherState Weather => _weather.State;
    public WeatherSystem WeatherControl => _weather;
    /// <summary>Fog level 0–1 for the renderer's light map.</summary>
    public float FogLevel => Weather.Fog;
    /// <summary>Recent lightning strikes (flash positions) for the renderer.</summary>
    public IReadOnlyList<LightningStrike> LightningStrikes => _lightning;
    /// <summary>Seconds left before the Astro-God answers an astral sigil (0 when idle).</summary>
    public float AstralCountdown => _astralCountdown;
    /// <summary>Game-time multiplier: ×4 while the player sleeps in a bed at night.</summary>
    public float TimeRate => _sleeping ? SleepTimeRate : 1f;
    public bool IsSleeping => _sleeping;
    /// <summary>Forces a season (tests, debug); null follows the real date.</summary>
    public SeasonWindow? SeasonOverride { get; set; }
    public SeasonWindow Season => SeasonOverride ?? SeasonFor(DateTime.UtcNow);
    public bool IsNight => ClockHour >= 18f || ClockHour < 6f;

    /// <summary>
    /// Night hours tick this much faster than day hours (real time), so a day lasts 15 real minutes and a night 9, as in
    /// Terraria, while every clock-hour rule (18:00 dusk, 06:00 dawn) stays put. Needs, statuses and regen use vital hours.
    /// </summary>
    public const float NightClockPace = 900f / 540f;
    /// <summary>Clock-only multiplier on game hours for the current time of day.</summary>
    public float ClockPace => IsNight ? FastClock ? 1.5f : NightClockPace : 1f;

    /// <summary>
    /// For tests and development only (<c>--fast-clock</c>): the old 250-second cycle (a 150 s day and a 100 s night), so
    /// checks that wait out days stay quick. Vital hours are unaffected.
    /// </summary>
    public static bool FastClock { get; set; }

    /// <summary>Game (clock) hours per real second while awake.</summary>
    public static float ClockHoursPerRealSecond => FastClock ? VitalHoursPerRealSecond : GameHoursPerRealSecond;
    public const int MoonPhaseCount = 8;

    /// <summary>
    /// Moon phase 0–7 (0 full … 4 dark … 7 waxing gibbous). It is fixed for a whole night and advances by one each
    /// night (the phase flips at dawn), derived from the clock so saves need no extra field.
    /// </summary>
    public int MoonPhase
    {
        get
        {
            // Nights are numbered from dawn to dawn: night n runs 18:00 on day n to 06:00 on day n+1.
            var night = (int)MathF.Floor((_elapsedGameHours + 8f - 6f) / 24f);
            return ((night % MoonPhaseCount) + MoonPhaseCount) % MoonPhaseCount;
        }
    }

    /// <summary>Phase names for the HUD.</summary>
    public static string MoonPhaseName(int phase) => (phase % MoonPhaseCount) switch
    {
        0 => "Full moon", 1 => "Waning gibbous", 2 => "Last quarter", 3 => "Waning crescent",
        4 => "Dark moon", 5 => "Waxing crescent", 6 => "First quarter", _ => "Waxing gibbous"
    };

    /// <summary>
    /// Surface sky light 0–1 for the light map: full by day, dim at night (brighter under a full moon), and cut by
    /// cloud cover, rain and storms. Renderers should use this instead of a clock-only curve.
    /// </summary>
    public float SkyLightLevel
    {
        get
        {
            var hour = ClockHour;
            float night = hour is >= 7f and <= 18f ? 0f
                : hour is > 18f and < 21f ? (hour - 18f) / 3f
                : hour is > 4f and < 7f ? 1f - (hour - 4f) / 3f
                : 1f;
            var moon = 1f - MathF.Abs(MoonPhase - 4) / 4f; // 1 at full moon, 0 at dark moon
            var nightFloor = 0.24f + 0.12f * moon;
            var clear = 1f - night * (1f - nightFloor);
            var weather = Weather;
            var dim = Math.Clamp(weather.CloudCover * 0.12f + weather.Rain * 0.22f + weather.Snow * 0.12f + weather.Dust * 0.15f +
                                 (weather.Storm ? 0.15f : 0f), 0f, 0.5f);
            return Math.Clamp(clear * (1f - dim), 0.12f, 1f);
        }
    }

    /// <summary>True while an event is running that you cannot sleep through (night, day and invasion events).</summary>
    public bool EventBlocksSleep => _worldEvents.BlocksSleep;

    public static SeasonWindow SeasonFor(DateTime date) => (date.Month, date.Day) switch
    {
        (10, _) or (11, <= 10) => SeasonWindow.HarvestFestival,
        (12, >= 10) or (1, <= 6) => SeasonWindow.Starfall,
        _ => SeasonWindow.None
    };

    /// <summary>Projectile push from the wind (only applied to content weapons).</summary>
    private Vector2 WindDrift => new(Weather.Wind * 0.25f, 0f);

    // ------------------------------------------------------------------ IWorldEventHost

    int IWorldEventHost.Day => Day;
    BiomeZone IWorldEventHost.CurrentZone => CurrentBiome.Zone;
    WeatherState IWorldEventHost.Weather => Weather;
    int IWorldEventHost.TownNpcCount => _townNpcs.Count(npc => !npc.IsBound && npc.HomeCell is not null);

    int IWorldEventHost.SpawnEventCreatures(WorldEventKind kind, IReadOnlyList<(EnemyKind Kind, float Weight)> roster, int count)
    {
        var spawned = 0;
        var playerX = (int)MathF.Floor(PlayerCenter.X);
        for (var index = 0; index < count && roster.Count > 0; index++)
        {
            var roll = TriggerRandom.NextDouble();
            var creature = HardmodeEventCreature(WorldEventSystem.PickFrom(roster, roll), roll);
            var archetype = EnemyArchetypes.Get(creature);
            var side = index % 2 == 0 ? 1 : -1;
            var x = Math.Clamp(playerX + side * ((int)ViewHalfWidth + 2 + index % 6 * 2), 2, World.Width - 3);
            if (!TryFindSpawnGround(x, CurrentBiome.Underground, out var groundY))
                groundY = FindSurface(x);
            var lift = archetype.Flying ? 3f : 0f;
            var enemy = SpawnCreature(creature, new Vector2(x, groundY - archetype.Size.Y - lift));
            enemy.EventTag = kind;
            spawned++;
        }
        return spawned;
    }

    bool IWorldEventHost.SpawnEventBoss(WorldEventKind kind, EnemyKind boss)
    {
        // BO-9 (D-11): the finale comes through the one spawn path. A different boss may share the fight; when one of the
        // same kind already lives (summoned by its item), the event adopts it as its finale instead of doubling it.
        if (!TrySpawnBoss(boss, BossSpawnSource.EventFinale, out var enemy, out _))
        {
            enemy = _enemies.FirstOrDefault(candidate => candidate.Kind == boss && candidate.IsAlive);
            if (enemy is null)
                return false;
            enemy.EventTag ??= kind;
            PostMessage($"The {enemy.DisplayName} turns to end the {_worldEvents.Spec(kind).Name}!");
            return true;
        }
        enemy!.EventTag = kind;
        PostMessage($"The {enemy.DisplayName} arrives to end the {_worldEvents.Spec(kind).Name}!");
        return true;
    }

    void IWorldEventHost.GrantRewards(IReadOnlyList<(ItemId Item, int Count)> rewards)
    {
        foreach (var (item, count) in rewards)
            Player.Inventory.Add(item, count);
    }

    void IWorldEventHost.Announce(string message) => PostMessage(message);

    void IWorldEventHost.MeteorStrike() => MeteorStrike();

    void IWorldEventHost.ApplyWeatherEvent(WorldEventKind kind, bool active)
    {
        switch (kind)
        {
            case WorldEventKind.Rain:
                _weather.Force(rain: active ? 0.7f : 0f, acid: false);
                break;
            case WorldEventKind.AcidRain:
                _weather.Force(rain: active ? 0.7f : 0f, acid: active);
                break;
            case WorldEventKind.Blizzard:
                _weather.Force(snow: active ? 0.85f : 0f, wind: active ? -5f : 0f);
                break;
            case WorldEventKind.DustStorm:
                _weather.Force(dust: active ? 0.85f : 0f, wind: active ? 6f : 0f);
                break;
        }
    }

    // ------------------------------------------------------------------ triggers

    /// <summary>Registers the one-time first-clear hooks (unlocks and messages).</summary>
    private void WireTriggers()
    {
        if (_triggersWired || _flagsField is null)
            return;
        _triggersWired = true;
        var flags = _flagsField;
        flags.OnFirstSet("boss.stone_sentinel.down", () => PostMessage("The Overseer is scrap. Swarm Rain can now fall on rainy dawns."));
        flags.OnFirstSet("blight.fracture", () => PostMessage("Crystal spires pierce the crust. A Dark Sun may now rise."));
        flags.OnFirstSet("hearts.broken", () => PostMessage("Scavengers have heard the blight crack. Raids may come at dusk."));
        flags.OnFirstSet("ore.voidsteel", () => PostMessage("Voidsteel stirs in the deep. Something above has noticed."));
        flags.OnFirstSet("event.scavenger_raid.cleared", () => flags.Set("npc.demolitionist.unlocked"));
        flags.OnFirstSet("event.pirate_boarding.cleared", () => flags.Set("npc.pilot.unlocked"));
        flags.OnFirstSet("boss.bloom_matriarch.down", () => flags.Set("npc.arborist.unlocked"));
        flags.OnFirstSet("vault.opened", () => flags.Set("npc.archivist.unlocked"));
    }

    private void OnCreatureDefeatedForEvents(EnemyAgent enemy)
    {
        if (enemy.IsBoss)
            Flags.Set($"boss.{WorldFlags.Key(enemy.Kind)}.down");
        Flags.Increment("kills.total");
        switch (enemy.Kind)
        {
            case EnemyKind.StoneSentinel:
                Player.Inventory.Add(ItemId.OverseerCircuit, 1);
                break;
            case EnemyKind.AstroGod:
                Player.Inventory.Add(ItemId.AstralOre, 20);
                break;
            case EnemyKind.VoidStalker:
                Player.Inventory.Add(ItemId.VoidEssence, 4);
                break;
        }
        if (enemy.EventTag is { } tag)
            _worldEvents.RecordKill(tag, enemy.Kind, enemy.IsBoss, this);
    }

    /// <summary>Heart trigger: a fragment, the raid becomes eligible, and a 50% meteor fall that night.</summary>
    private void OnHeartBrokenContent()
    {
        Player.Inventory.Add(ItemId.HeartFragment, 1);
        Flags.Increment("hearts.broken");
        if (TriggerRandom.Next(2) == 0)
            Flags.Set("meteor.pending");
    }

    /// <summary>
    /// Altar trigger: void essence and sometimes a vengeful wisp. The ore itself (including voidsteel, the third
    /// tier of the cycle) is seeded by <see cref="SmashAltar"/>.
    /// </summary>
    private void OnAltarSmashedContent()
    {
        Flags.Increment("altars.smashed");
        Player.Inventory.Add(ItemId.VoidEssence, 2);
        if (TriggerRandom.Next(3) == 0)
        {
            var wisp = SpawnCreature(EnemyKind.VengefulWisp, PlayerCenter + new Vector2(6f, -4f));
            PostMessage($"A {wisp.DisplayName} rises from the shattered altar!");
        }
    }

    /// <summary>Fracture trigger: the flag, Crystal Spires (on detailed worlds), and Dark Sun eligibility.</summary>
    private void OnFractureContent()
    {
        Flags.Set("blight.fracture");
        if (World.Width >= DetailedGenerationMinimumWidth)
            GrowCrystalSpires();
    }

    /// <summary>Raises one Crystal Spires zone on the surface away from the landing site.</summary>
    public int GrowCrystalSpires()
    {
        var width = Math.Max(12, World.Width / 20);
        var left = -1;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var candidate = TriggerRandom.Next(World.Width / 8, Math.Max(World.Width / 8 + 1, World.Width - World.Width / 8 - width));
            if (Math.Abs(candidate + width / 2 - SpawnColumn) < World.Width / 8)
                continue;
            left = candidate;
            break;
        }
        if (left < 0)
            return 0;
        var changed = new List<GridPoint>();
        for (var x = left; x < left + width && x < World.Width; x++)
        {
            var top = FindSurface(x);
            for (var depth = 0; depth < 10; depth++)
            {
                var y = top + depth;
                if (y >= World.Height - 1)
                    break;
                var tile = World.TileAt(x, y);
                if (CrystalRules.Convert(tile, exposed: depth == 0) is { } crystal)
                {
                    World.SetTile(new GridPoint(x, y), crystal);
                    changed.Add(new GridPoint(x, y));
                }
            }
            if (TriggerRandom.Next(5) == 0)
            {
                var height = TriggerRandom.Next(3, 7);
                for (var h = 1; h <= height && top - h > 1; h++)
                {
                    if (World.TileAt(x, top - h) != TileId.Air)
                        break;
                    World.SetTile(new GridPoint(x, top - h), TileId.CrystalSpire);
                    changed.Add(new GridPoint(x, top - h));
                }
            }
        }
        // Lumenite veins under the spires.
        for (var vein = 0; vein < Math.Max(4, width / 4); vein++)
        {
            var x = TriggerRandom.Next(left, Math.Min(World.Width - 1, left + width));
            var y = Math.Min(World.Height - 3, FindSurface(x) + TriggerRandom.Next(8, 30));
            for (var step = 0; step < 6; step++)
            {
                var point = new GridPoint(Math.Clamp(x + TriggerRandom.Next(-1, 2), 1, World.Width - 2), Math.Clamp(y + TriggerRandom.Next(-1, 2), 1, World.Height - 2));
                if (Tiles[World.GetCell(point).TileId].IsSolid && World.GetCell(point).TileId != TileId.Bedrock)
                {
                    World.SetTile(point, TileId.LumeniteOre);
                    changed.Add(point);
                }
                x = point.X;
                y = point.Y;
            }
        }
        var altarX = left + width / 2;
        var altarY = FindSurface(altarX) - 1;
        if (World.Contains(altarX, altarY) && World.TileAt(altarX, altarY) == TileId.Air)
        {
            World.SetTile(new GridPoint(altarX, altarY), TileId.CrystalAltar);
            changed.Add(new GridPoint(altarX, altarY));
        }
        CrystalSpiresLeft = left;
        CrystalSpiresRight = left + width - 1;
        _framing.ReframeRegion(World, Tiles, TileFramingSystem.GetDirtyRegion(World, changed));
        return changed.Count;
    }

    /// <summary>Column span of the Crystal Spires zone grown at the Fracture (-1 when none).</summary>
    public int CrystalSpiresLeft { get; private set; } = -1;
    public int CrystalSpiresRight { get; private set; } = -1;

    /// <summary>Meteor fall: a crater of meteorite on the surface away from the colony. Returns the impact column.</summary>
    public int MeteorStrike()
    {
        var x = SpawnColumn;
        for (var attempt = 0; attempt < 30 && Math.Abs(x - SpawnColumn) < Math.Max(20, World.Width / 10); attempt++)
            x = TriggerRandom.Next(8, World.Width - 8);
        var surface = FindSurface(x);
        var changed = new List<GridPoint>();
        const int radius = 4;
        for (var dy = -radius; dy <= radius; dy++)
        for (var dx = -radius - 2; dx <= radius + 2; dx++)
        {
            var point = new GridPoint(x + dx, surface + dy);
            if (!World.Contains(point) || point.Y >= World.Height - 1 || World.GetCell(point).TileId == TileId.Bedrock)
                continue;
            var distance = MathF.Sqrt(dx * dx * 0.6f + dy * dy);
            if (distance <= radius - 1.5f && dy < 1)
                World.SetTile(point, TileId.Air);
            else if (distance <= radius + 0.5f && dy >= -1)
                World.SetTile(point, TileId.Meteorite);
            else
                continue;
            changed.Add(point);
        }
        _framing.ReframeRegion(World, Tiles, TileFramingSystem.GetDirtyRegion(World, changed));
        LastMeteorColumn = x;
        Flags.Increment("meteors.fallen");
        PostMessage("A meteor screams down and craters the land!");
        return x;
    }

    public int LastMeteorColumn { get; private set; } = -1;

    private void StartAstralCountdown()
    {
        _astralCountdown = AstralCountdownSeconds;
        Flags.Set("astral.sigil.used");
        PostMessage("The astral sigil flares. Something vast turns its gaze toward you... (60 s)");
    }

    // ------------------------------------------------------------------ time, sleep and boundaries

    /// <summary>
    /// Using a bed: as in Terraria, a bed in a valid room becomes the spawn point (day or night). At night the player
    /// also sleeps there; time runs ×4 until dawn or any action.
    /// </summary>
    public bool TrySleep()
    {
        if (NearestTileWith(TileTraits.Bed, 2) is not { } bed)
        {
            PostMessage("You can only sleep in a bed at night.");
            return false;
        }
        if (_bedSpawn != bed)
        {
            _regions.EnsureCurrent();
            if (HousingValidator.Check(World, _regions, PlayerCell()).Valid)
            {
                _bedSpawn = bed;
                PostMessage("Spawn point set.");
            }
            else
                PostMessage("This bed is not in a proper room, so it can't be your spawn point.");
        }
        if (!IsNight)
        {
            PostMessage("You can only sleep at night.");
            return false;
        }
        if (EventBlocksSleep)
        {
            PostMessage("You can't sleep while an event is under way.");
            return false;
        }
        _sleeping = true;
        PostMessage("You settle into the bed. Time slips by faster.");
        return true;
    }

    public void WakeUp() => _sleeping = false;

    /// <summary>The bed the player respawns beside, once set (Terraria's spawn point); null means the landing site.</summary>
    private GridPoint? _bedSpawn;

    public GridPoint? BedSpawn => _bedSpawn;

    /// <summary>Where to stand beside the spawn bed, or null when it is gone or the space is blocked.</summary>
    private Vector2? BedSpawnPosition()
    {
        if (_bedSpawn is not { } bed || !World.Contains(bed) || !ContentTiles.Has(World.TileAt(bed.X, bed.Y), TileTraits.Bed))
            return null;
        for (var y = bed.Y; y < Math.Min(World.Height - 1, bed.Y + 4); y++)
        {
            if (!Tiles[World.TileAt(bed.X, y + 1)].IsSolid)
                continue;
            var position = new Vector2(bed.X + 0.5f - Player.Motion.Size.X / 2f, y + 1 - Player.Motion.Size.Y);
            return IsSolidActorPosition(position, Player.Motion.Size) ? null : position;
        }
        return null;
    }

    private GridPoint? NearestTileWith(TileTraits trait, int radius)
    {
        var cell = PlayerCell();
        GridPoint? best = null;
        var bestDistance = int.MaxValue;
        for (var dy = -radius; dy <= radius; dy++)
        for (var dx = -radius; dx <= radius; dx++)
            if (World.Contains(cell.X + dx, cell.Y + dy) && ContentTiles.Has(World.TileAt(cell.X + dx, cell.Y + dy), trait) &&
                dx * dx + dy * dy < bestDistance)
            {
                bestDistance = dx * dx + dy * dy;
                best = new GridPoint(cell.X + dx, cell.Y + dy);
            }
        return best;
    }

    private bool NearTileWith(TileTraits trait, int radius)
    {
        var cell = PlayerCell();
        for (var dy = -radius; dy <= radius; dy++)
        for (var dx = -radius; dx <= radius; dx++)
            if (World.Contains(cell.X + dx, cell.Y + dy) && ContentTiles.Has(World.TileAt(cell.X + dx, cell.Y + dy), trait))
                return true;
        return false;
    }

    /// <summary>Called by the clock: weather rolls at dawn/noon/dusk/midnight, event boundaries and pending triggers.</summary>
    private void AdvanceContentClock(float previousHour, float elapsedHours, WorldEventMoment? moment)
    {
        if (_sleeping && !IsNight)
        {
            _sleeping = false;
            Player.Needs.Restore(NeedKind.Rest, 0.5f);
            PostMessage("You wake rested at dawn.");
        }
        for (var slot = 0; slot < WeatherSystem.RollHours.Length; slot++)
        {
            if (CrossedBoundary(previousHour, ClockHour, WeatherSystem.RollHours[slot], elapsedHours))
                _weather.RollTargets(Day, slot, CurrentBiome.Zone, FractureActive);
        }
        _weather.Ease(elapsedHours);

        if (moment == WorldEventMoment.Dusk && Flags.IsSet("meteor.pending"))
        {
            Flags.Clear("meteor.pending");
            _worldEvents.TryStart(WorldEventKind.MeteorFall, this, forced: true, out _);
        }
        if (Season == SeasonWindow.Starfall && moment == WorldEventMoment.Dusk && TriggerRandom.Next(8) == 0)
            _worldEvents.TryStart(WorldEventKind.MeteorFall, this, forced: false, out _);
        if (Season == SeasonWindow.HarvestFestival && moment == WorldEventMoment.Dawn && TriggerRandom.Next(6) == 0)
            _worldEvents.TryStart(WorldEventKind.ColonyFestival, this, forced: false, out _);
        if (Player.MaximumHitPoints >= 120f)
            Flags.Set("player.health.120");
        _worldEvents.Advance(elapsedHours, moment, this);
        if (_sleeping && EventBlocksSleep)
        {
            _sleeping = false;
            PostMessage("You are jolted awake!");
        }
    }

    /// <summary>Real-time content updates: the astral countdown and lightning flash ageing.</summary>
    private void UpdateWorldRealtime(float deltaSeconds)
    {
        if (_astralCountdown > 0f)
        {
            var before = _astralCountdown;
            _astralCountdown = Math.Max(0f, _astralCountdown - deltaSeconds);
            if (before > 10f && _astralCountdown <= 10f)
                PostMessage("The sky tears open... 10 seconds.");
            if (_astralCountdown <= 0f)
            {
                if (TrySpawnBoss(EnemyKind.AstroGod, BossSpawnSource.Countdown, out var god, out _))
                    PostMessage($"The {god!.DisplayName} descends!");
            }
        }
        for (var i = _lightning.Count - 1; i >= 0; i--)
        {
            var aged = _lightning[i] with { AgeSeconds = _lightning[i].AgeSeconds + deltaSeconds };
            if (aged.AgeSeconds > 0.6f)
                _lightning.RemoveAt(i);
            else
                _lightning[i] = aged;
        }
    }

    // ------------------------------------------------------------------ weather effects (story cadence)

    /// <summary>Rain fills open basins, snow settles in the Frozen zone, storms throw lightning, wet pawns lose comfort.</summary>
    private void ApplyWeatherEffects(float hours)
    {
        if (hours <= 0f)
            return;
        var weather = Weather;
        var outdoors = PlayerRoom is null && !CurrentBiome.Underground;
        if (weather.Rain > 0.15f)
            FillBasinsWithRain(weather.Rain * hours, weather.AcidRain ? LiquidKind.Acid : LiquidKind.Water);
        if (weather.Snow > 0.3f)
            SettleSnow(weather.Snow);
        if (outdoors && weather.Rain > 0.2f)
        {
            // No comfort drain for the player in the rain: the player has no needs upkeep (D-48, NC-2).
            if (weather.AcidRain && !Player.Stats.ImmunityTags.Contains("acid"))
                Player.TakeDamage(hours * 6f * weather.Rain, DamageKind.Burn);
        }
        if (weather.Storm && weather.Rain > 0.5f && WeatherRandom.NextDouble() < 0.25)
            StrikeLightning();
        // Calm nights and festivals lift everyone's spirits.
        if (_worldEvents.IsActive(WorldEventKind.AuroraNight) || _worldEvents.IsActive(WorldEventKind.ColonyFestival))
            foreach (var pawn in ColonyPawns.Where(pawn => pawn.IsAlive))
                pawn.Needs.Restore(NeedKind.Joy, hours * 0.12f);
    }

    /// <summary>How far the player can see (0–1): dust storms and fog close in; the renderer can scale its view.</summary>
    public float Visibility => Math.Clamp(1f - 0.6f * Weather.Dust - 0.4f * Weather.Fog, 0.2f, 1f);

    /// <summary>Adds rain to open-sky cells that sit in a basin (solid on both sides) or already hold the same liquid.</summary>
    public int FillBasinsWithRain(float amount, LiquidKind kind = LiquidKind.Water, int radius = 40)
    {
        if (amount <= 0f)
            return 0;
        var filled = 0;
        var center = PlayerCell().X;
        for (var x = Math.Max(1, center - radius); x <= Math.Min(World.Width - 2, center + radius); x++)
        {
            // First non-air cell from the top: rain lands on it or on the liquid above it.
            var y = 0;
            while (y < World.Height - 1 && World.TileAt(x, y) == TileId.Air && World.GetCell(x, y).LiquidDepth <= 0.01f)
                y++;
            var target = y;
            if (World.GetCell(x, y).LiquidDepth > 0.01f)
            {
                if (World.GetCell(x, y).LiquidKind != kind)
                    continue;
                target = y;
            }
            else
            {
                target = y - 1;
                if (target < 1 || !Tiles[World.TileAt(x, y)].IsSolid)
                    continue;
                if (!IsBasin(x, target))
                    continue;
            }
            if (World.TryAddLiquid(new GridPoint(x, target), kind, Math.Min(0.25f, amount * 0.1f), Tiles))
                filled++;
        }
        return filled;
    }

    /// <summary>True when walls close a floored stretch on both sides within eight cells (water would pool here).</summary>
    private bool IsBasin(int x, int y)
    {
        foreach (var direction in new[] { -1, 1 })
        {
            var closed = false;
            for (var step = 1; step <= 8; step++)
            {
                var cx = x + direction * step;
                if (!World.Contains(cx, y + 1))
                    return false;
                if (Tiles[World.TileAt(cx, y)].IsSolid)
                {
                    closed = true;
                    break;
                }
                if (!Tiles[World.TileAt(cx, y + 1)].IsSolid && World.GetCell(cx, y + 1).LiquidDepth < 0.9f)
                    return false;
            }
            if (!closed)
                return false;
        }
        return true;
    }

    private void SettleSnow(float intensity)
    {
        var center = PlayerCell().X;
        for (var i = 0; i < 4; i++)
        {
            var x = Math.Clamp(center + WeatherRandom.Next(-30, 31), 0, World.Width - 1);
            var y = FindSurface(x);
            if (!BiomeLayout.IsFrozen(x, y) || WeatherRandom.NextDouble() > intensity)
                continue;
            if (World.TileAt(x, y) is TileId.Grass or TileId.Soil or TileId.Sand or TileId.Gravel or TileId.Mud)
            {
                World.SetTile(new GridPoint(x, y), TileId.Snow);
                _framing.ReframeRegion(World, Tiles, TileFramingSystem.GetDirtyRegion(World, new[] { new GridPoint(x, y) }));
            }
        }
    }

    /// <summary>Lightning favours tall metal (plating, beacon towers); a strike near the player hurts.</summary>
    public LightningStrike StrikeLightning()
    {
        var center = PlayerCell().X;
        var x = Math.Clamp(center + WeatherRandom.Next(-25, 26), 0, World.Width - 1);
        for (var probe = -6; probe <= 6; probe++)
        {
            var candidate = Math.Clamp(x + probe, 0, World.Width - 1);
            var top = World.TileAt(candidate, FindSurface(candidate));
            if (top is TileId.HullPlatingGrey or TileId.HullPlatingBlue or TileId.Plate or TileId.BeaconTower or TileId.Grate)
            {
                x = candidate;
                break;
            }
        }
        var strike = new LightningStrike(new Vector2(x + 0.5f, FindSurface(x) - 0.5f), 0f);
        _lightning.Add(strike);
        if (Vector2.Distance(strike.Position, PlayerCenter) < 2.5f)
            Player.TakeDamage(25f, DamageKind.Burn);
        foreach (var enemy in _enemies.Where(enemy => enemy.IsAlive && Vector2.Distance(enemy.Motion.Position, strike.Position) < 2.5f))
            enemy.TakeDamage(60f);
        return strike;
    }

    private ContentSaveState CaptureContent() => new(
        _charge,
        CaptureContainers(),
        _worldEvents.Capture(),
        _weather.State,
        Flags.Capture(),
        CaptureTownNpcs(),
        CaptureFishing(),
        _astralCountdown);

    private void RestoreContent(ContentSaveState? state)
    {
        if (state is null)
            return;
        if (float.IsFinite(state.Charge) && state.Charge >= 0f)
            _charge = state.Charge;
        RestoreContainers(state.Containers);
        _worldEvents.Restore(state.Events);
        if (state.Weather is { } weather)
            _weather.State = weather;
        Flags.Restore(state.Flags);
        RestoreTownNpcs(state.TownNpcs);
        RestoreFishing(state.Fishing);
        _astralCountdown = float.IsFinite(state.AstralCountdown) ? Math.Max(0f, state.AstralCountdown) : 0f;
    }
}

/// <summary>Crystal Spires conversion: natural ground and blight both turn crystalline (purifying the blight).</summary>
public static class CrystalRules
{
    public static TileId? Convert(TileId tile, bool exposed) => tile switch
    {
        TileId.Grass or TileId.JungleGrass or TileId.CanopyGrass or TileId.CrimsonSoil or TileId.OvergrowthSoil =>
            TileId.PrismaticGrass,
        TileId.Soil or TileId.Mud => exposed ? TileId.PrismaticGrass : TileId.PearlStone,
        TileId.Rock or TileId.CorruptRock or TileId.OvergrowthRock or TileId.Slate => TileId.PearlStone,
        TileId.Sand or TileId.DesertSand or TileId.RadiatedSand or TileId.OvergrowthSand => TileId.PearlSand,
        TileId.Snow or TileId.RadiatedIce or TileId.OvergrowthIce => TileId.PearlSand,
        _ => null
    };

    public static bool IsCrystal(TileId tile) => ContentTiles.Has(tile, TileTraits.Crystal);
}
