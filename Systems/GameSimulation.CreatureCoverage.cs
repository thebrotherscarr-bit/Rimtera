using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Creature coverage, simulation side (docs/CREATURE_COVERAGE.md): biome-variant spawns (sky layer, underground junk and
/// frost, the grave field and the late dungeon), event mini-bosses, the post-Mutated-Flora Dark Sun units, the Lumen Tempest,
/// and the summon and starter gates for the new bosses and event items.
/// </summary>
public sealed partial class GameSimulation
{
    private readonly HashSet<WorldEventKind> _coverageMiniBossesSpawned = new();
    private float _coverageTimer;
    private float _lumenScanTimer;

    // ------------------------------------------------------------------ biome variants

    /// <summary>The biome-variant slot the player's surroundings select, or null when none applies.</summary>
    public string? CoverageVariantSlot(BiomeReading reading, bool inDungeon)
    {
        if (inDungeon)
            return Flags.IsSet("boss.mutated_flora.down") ? "dungeon-hard" : null;
        var cell = PlayerCell();
        if (!reading.Underground)
        {
            var surface = BiomeLayout.SurfaceAt(cell.X);
            // The sky layer: the upper part of the air column, well above the surface (Terraria: space).
            if (surface - cell.Y >= 25 && cell.Y < surface * 0.45f)
                return "sky";
            if (InGraveField(cell))
                return "grave";
            return null;
        }
        return reading.Zone switch
        {
            BiomeZone.JunkWasteland => "junk-under",
            BiomeZone.FrozenWasteland => "frozen-under",
            _ => null
        };
    }

    /// <summary>True when at least <see cref="CreatureCoverage.GraveFieldMarkers"/> memorial markers stand near the cell.</summary>
    public bool InGraveField(GridPoint cell)
    {
        var markers = 0;
        for (var y = Math.Max(0, cell.Y - 15); y <= Math.Min(World.Height - 1, cell.Y + 15); y++)
            for (var x = Math.Max(0, cell.X - 30); x <= Math.Min(World.Width - 1, cell.X + 30); x++)
                if (World.TileAt(x, y) == TileId.Tombstone && ++markers >= CreatureCoverage.GraveFieldMarkers)
                    return true;
        return false;
    }

    /// <summary>
    /// Ambient spawn hook: where a biome variant applies, <see cref="CreatureCoverage.VariantShare"/> of the rolls (chosen by a
    /// hash of the roll, so the random stream is unchanged) come from the variant's roster.
    /// </summary>
    private EnemyKind CoverageSpawn(BiomeReading reading, bool inDungeon, bool night, double roll, EnemyKind kind)
    {
        if (!CreatureCoverage.VariantTakes(roll) || CoverageVariantSlot(reading, inDungeon) is not { } slot)
            return kind;
        return CreatureCoverage.PickVariant(slot, FractureActive, roll, night) ?? kind;
    }

    /// <summary>Test hook: the coverage spawn decision for the player's current spot.</summary>
    internal EnemyKind ProbeCoverageSpawn(double roll, bool night, EnemyKind fallback)
    {
        var inDungeon = World.GetCell(PlayerCell()).WallId == WallIds.Dungeon;
        return CoverageSpawn(BiomeScanner.Scan(World, PlayerCell()), inDungeon, night, roll, fallback);
    }

    // ------------------------------------------------------------------ events

    /// <summary>Dark Sun units after the Mutated Flora: 35% of eclipse picks become slashers or spheres.</summary>
    private EnemyKind? LateEventCreature(EnemyKind kind, double roll)
    {
        if (!Flags.IsSet("boss.mutated_flora.down") || kind is not (EnemyKind.EventEclipseShade or EnemyKind.EventEclipseReaper or EnemyKind.EventEclipseMothMech))
            return null;
        var late = CreatureCoverage.LateDarkSun;
        return roll * 11.0 % 1.0 < 0.35 ? late[(int)(roll * 17.0 % 1.0 * late.Count) % late.Count] : null;
    }

    /// <summary>Every creature an event can field: its roster, hardmode and late upgrades, wave rosters, mini-bosses and boss.</summary>
    public IReadOnlyList<EnemyKind> EventCreatures(WorldEventKind kind)
    {
        var kinds = new List<EnemyKind>();
        var spec = _worldEvents.Spec(kind);
        foreach (var (creature, _) in spec.Roster)
        {
            kinds.Add(creature);
            if (HardEventUpgrades.TryGetValue(creature, out var hard))
                kinds.AddRange(hard);
        }
        if (kind == WorldEventKind.DarkSun)
            kinds.AddRange(CreatureCoverage.LateDarkSun);
        if (SeasonalEvents.IsMoon(kind))
        {
            for (var wave = 1; wave <= SeasonalEvents.MoonWaves; wave++)
                kinds.AddRange(SeasonalEvents.WaveRoster(kind, wave).Select(entry => entry.Kind));
            kinds.Add(SeasonalEvents.MiniBoss(kind));
            kinds.Add(SeasonalEvents.SecondMiniBoss(kind));
            kinds.Add(SeasonalEvents.Boss(kind));
        }
        kinds.AddRange(CreatureCoverage.EventMiniBosses.Where(entry => entry.Event == kind).Select(entry => entry.MiniBoss));
        if (spec.FinalBoss is { } boss)
            kinds.Add(boss);
        if (Obelisks.Any(entry => entry.Event == kind))
            kinds.Add(Obelisks.First(entry => entry.Event == kind).Obelisk);
        return kinds.Distinct().ToArray();
    }

    /// <summary>The mini-bosses and boss an event brings (for the coverage table and tests).</summary>
    public IReadOnlyList<EnemyKind> EventLeaders(WorldEventKind kind) =>
        EventCreatures(kind).Where(creature => EnemyArchetypes.Get(creature).IsBoss || CreatureCoverage.IsMiniBoss(creature) ||
                                               Obelisks.Any(entry => entry.Obelisk == creature) ||
                                               SeasonalEvents.IsMoon(kind) && (creature == SeasonalEvents.MiniBoss(kind) ||
                                                                               creature == SeasonalEvents.SecondMiniBoss(kind)))
            .ToArray();

    private void UpdateCreatureCoverage(float deltaSeconds)
    {
        _coverageTimer += deltaSeconds;
        if (_coverageTimer < 1f)
            return;
        _coverageTimer = 0f;
        _coverageMiniBossesSpawned.RemoveWhere(kind => !_worldEvents.IsActive(kind));
        SpawnEventMiniBosses();
        UpdateLumenTempest(1f);
    }

    /// <summary>After the Fracture, each running event with a mini-boss brings it once its progress passes the mark.</summary>
    private int SpawnEventMiniBosses()
    {
        if (!FractureActive)
            return 0;
        var spawned = 0;
        foreach (var (kind, miniBoss, atProgress) in CreatureCoverage.EventMiniBosses)
        {
            if (!_worldEvents.IsActive(kind) || _coverageMiniBossesSpawned.Contains(kind))
                continue;
            var progress = _worldEvents.Progress.FirstOrDefault(line => line.Kind == kind).Progress;
            if (progress < atProgress)
                continue;
            _coverageMiniBossesSpawned.Add(kind);
            SpawnSeasonCreature(kind, miniBoss, 0);
            PostMessage($"A {EnemyArchetypes.Get(miniBoss).DisplayName} rises with the {_worldEvents.Spec(kind).Name}!");
            spawned++;
        }
        return spawned;
    }

    /// <summary>Test hook: runs the mini-boss check now.</summary>
    internal int ProbeEventMiniBosses() => SpawnEventMiniBosses();

    /// <summary>Light fixtures (torches, lamps, glow lamps) within the Lumen Tempest box around a cell.</summary>
    public int LightFixturesNear(GridPoint cell)
    {
        var lights = 0;
        for (var y = Math.Max(0, cell.Y - 25); y <= Math.Min(World.Height - 1, cell.Y + 25); y++)
            for (var x = Math.Max(0, cell.X - 40); x <= Math.Min(World.Width - 1, cell.X + 40); x++)
                if (ContentTiles.Has(World.TileAt(x, y), TileTraits.Light))
                    lights++;
        return lights;
    }

    /// <summary>
    /// The Lumen Tempest (Terraria's Torch God): a hoard of <see cref="CreatureCoverage.LumenTempestLights"/> light fixtures
    /// near an underground player calls a swarm of light creatures; the first clear grants the Lumen accord.
    /// </summary>
    private void UpdateLumenTempest(float deltaSeconds)
    {
        if (Flags.IsSet(_worldEvents.Spec(WorldEventKind.LumenTempest).FlagName) && Flags.Set("lumen_tempest.accord"))
        {
            CollectOrDrop(ItemId.LumenAccord, 1, PlayerCenter);
            PostMessage("The light settles into your hands: a Lumen accord.");
        }
        _lumenScanTimer += deltaSeconds;
        if (_lumenScanTimer < 3f || _worldEvents.IsActive(WorldEventKind.LumenTempest))
            return;
        _lumenScanTimer = 0f;
        if (!CurrentBiome.Underground || PlayerCenter.Y < World.Height * 0.45f || LightFixturesNear(PlayerCell()) < CreatureCoverage.LumenTempestLights)
            return;
        if (_worldEvents.TryStart(WorldEventKind.LumenTempest, this, forced: false, out _))
            PostMessage("So much light in the dark draws the attention of something made of it...");
    }

    /// <summary>Test hook: runs the Lumen Tempest check now (skips the scan timer).</summary>
    internal void ProbeLumenTempest() => UpdateLumenTempest(3f);

    // ------------------------------------------------------------------ summon and starter gates

    /// <summary>Why a coverage boss summon refuses right now, or null (Terraria: biome and time rules of each summon item).</summary>
    private string? CoverageSummonRefusal(EnemyKind boss) => boss switch
    {
        EnemyKind.MoonEater when !FractureActive => "The lure stays dark until the Fracture.", // PC-1 (D-3, D-24)
        // PC-2: the Protocol Dragon is the Defense Protocol's top tier; its key waits for the same gate.
        EnemyKind.ProtocolDragon when !Flags.IsSet($"boss.{WorldFlags.Key(EnemyKind.VaultColossus)}.down") =>
            "The key turns, but the protocol's dragon stays dormant until the Vault Colossus has fallen.",
        EnemyKind.HoarfrostBehemoth when CurrentBiome.Zone != BiomeZone.FrozenWasteland =>
            "The lure needs the bite of the frozen wastes. Carry it into the snow.",
        EnemyKind.GelSovereign when !FractureActive => "The lattice hums, but nothing answers until the crust fractures.",
        EnemyKind.GelSovereign when CurrentBiome.Zone != BiomeZone.CrystalSpires => "The lattice needs the crystal spires' light to resonate.",
        EnemyKind.HollowRegent or EnemyKind.RimeSeraph when !FractureActive =>
            "The relic stays inert. Its master only wakes after the Fracture.",
        EnemyKind.HollowRegent or EnemyKind.RimeSeraph when !IsNight => "It flickers weakly in daylight. Use it after dusk.",
        _ => null
    };

    /// <summary>Why an event starter item refuses right now, or null (Terraria: the Bloody Tear at night, the Solar Tablet by day).</summary>
    internal string? CoverageStarterRefusal(WorldEventKind kind) => kind switch
    {
        WorldEventKind.RustMoon when !IsNight => "The flare fizzles in daylight. Light it after dusk.",
        WorldEventKind.DarkSun when IsNight => "The lens finds no sun to darken. Raise it by day.",
        _ => null
    };
}
