using Rimtera.Entities;

namespace Rimtera.Systems;

/// <summary>
/// Weighted creature rosters per hazard biome. A single roll in [0,1) picks from the
/// cumulative weights, so spawns stay on the simulation's seeded stream. Weights are Rimtera tuning.
/// </summary>
public static class BiomeSpawnTables
{
    private readonly record struct Entry(EnemyKind Kind, float DayWeight, float NightWeight);

    private static readonly Dictionary<BiomeZone, Entry[]> Tables = new()
    {
        [BiomeZone.FrozenWasteland] =
        [
            new(EnemyKind.SnowWolf, 0.24f, 0.36f),
            new(EnemyKind.Cryopterous, 0.28f, 0.2f),
            new(EnemyKind.SnowMite, 0.38f, 0.3f),
            new(EnemyKind.GlacialGolem, 0.1f, 0.14f)
        ],
        [BiomeZone.RadiatedDeeps] =
        [
            new(EnemyKind.RadSkitter, 0.26f, 0.24f),
            new(EnemyKind.MutantRat, 0.26f, 0.22f),
            new(EnemyKind.GeigerBug, 0.22f, 0.22f),
            new(EnemyKind.GlowStalker, 0.18f, 0.22f),
            new(EnemyKind.RadLeech, 0.08f, 0.1f)
        ],
        // The Irradiated Core (zone id MoltenChasm): its own husks and fission wisps lead a deeper, nastier mix of
        // the radiation fauna; nothing fire- or frost-themed lives down here.
        [BiomeZone.MoltenChasm] =
        [
            new(EnemyKind.CoreHusk, 0.2f, 0.22f),
            new(EnemyKind.FissionWisp, 0.18f, 0.2f),
            new(EnemyKind.GlowStalker, 0.18f, 0.18f),
            new(EnemyKind.RadLeech, 0.14f, 0.14f),
            new(EnemyKind.RadSkitter, 0.16f, 0.14f),
            new(EnemyKind.GeigerBug, 0.14f, 0.12f)
        ],
        [BiomeZone.JunkWasteland] =
        [
            new(EnemyKind.JunkSkitter, 0.26f, 0.22f),
            new(EnemyKind.ScavengerCrab, 0.24f, 0.18f),
            new(EnemyKind.SalvageDrone, 0.2f, 0.22f),
            new(EnemyKind.ScrapMite, 0.22f, 0.24f),
            new(EnemyKind.WastelandBeast, 0.08f, 0.14f)
        ],
        // Overgrowth blight: spore-ridden vermin, spore bats and swarm bugs, with the odd astro-beast.
        [BiomeZone.Overgrowth] =
        [
            new(EnemyKind.SporeRat, 0.32f, 0.28f),
            new(EnemyKind.CaveBat, 0.2f, 0.3f),
            new(EnemyKind.MossSlime, 0.3f, 0.24f),
            new(EnemyKind.AstroBeast, 0.18f, 0.18f)
        ],
        // Raid rosters for the calmer biomes; ambient spawns there keep the original context bands.
        [BiomeZone.Grassland] =
        [
            new(EnemyKind.MossSlime, 0.3f, 0.2f),
            new(EnemyKind.SporeRat, 0.3f, 0.3f),
            new(EnemyKind.SkyRay, 0.2f, 0f),
            new(EnemyKind.CaveBat, 0f, 0.3f),
            new(EnemyKind.AstroBeast, 0.2f, 0.2f)
        ],
        [BiomeZone.Caverns] =
        [
            new(EnemyKind.RockMite, 0.4f, 0.4f),
            new(EnemyKind.RockGolem, 0.25f, 0.25f),
            new(EnemyKind.CaveBat, 0.2f, 0.2f),
            new(EnemyKind.Wraith, 0.15f, 0.15f)
        ],
        // ---- Content-expansion zones (critters share the roster as passive wildlife) ----
        // The oceans (zone id AcidCoast): swimmers (sharks, jellies, leeches, fish) appear in the water, shore life on the beach.
        [BiomeZone.AcidCoast] =
        [
            new(EnemyKind.AcidShark, 0.12f, 0.14f), new(EnemyKind.GlowJelly, 0.08f, 0.12f), new(EnemyKind.TideLeech, 0.1f, 0.12f),
            new(EnemyKind.FishSilver, 0.06f, 0.03f), new(EnemyKind.FishGlow, 0.02f, 0.05f),
            new(EnemyKind.ReefCrawler, 0.1f, 0.08f), new(EnemyKind.AcidSkitterer, 0.08f, 0.08f), new(EnemyKind.SpittingBarnacle, 0.06f, 0.04f),
            new(EnemyKind.StormRay, 0.06f, 0.08f), new(EnemyKind.DrownedDiver, 0.02f, 0.08f), new(EnemyKind.BrineGolem, 0.03f, 0.04f),
            new(EnemyKind.CoralSentinel, 0.03f, 0.03f), new(EnemyKind.ShellCrab, 0.08f, 0.03f), new(EnemyKind.BeachSkitter, 0.06f, 0.02f),
            new(EnemyKind.CrabSmall, 0.04f, 0.02f), new(EnemyKind.SeaBird, 0.06f, 0f)
        ],
        [BiomeZone.FungalHollows] =
        [
            new(EnemyKind.SporeZombie, 0.18f, 0.18f), new(EnemyKind.FungalCrawler, 0.16f, 0.16f), new(EnemyKind.PuffDrifter, 0.12f, 0.12f),
            new(EnemyKind.MycelidShaman, 0.06f, 0.06f), new(EnemyKind.ShroomStalker, 0.08f, 0.08f), new(EnemyKind.CapTurtle, 0.08f, 0.08f),
            new(EnemyKind.LanternBat, 0.12f, 0.12f), new(EnemyKind.SporeGolem, 0.04f, 0.04f), new(EnemyKind.GlowSnail, 0.06f, 0.06f),
            new(EnemyKind.SporeMoth, 0.05f, 0.05f), new(EnemyKind.CaveFrog, 0.03f, 0.03f), new(EnemyKind.TruffleMite, 0.02f, 0.02f)
        ],
        [BiomeZone.VerdantCanopy] =
        [
            new(EnemyKind.ThornLurker, 0.14f, 0.16f), new(EnemyKind.CanopyApe, 0.1f, 0.08f), new(EnemyKind.SnapjawPlant, 0.1f, 0.1f),
            new(EnemyKind.MudCrawler, 0.12f, 0.12f), new(EnemyKind.SpittingOrchid, 0.08f, 0.06f), new(EnemyKind.JungleStalker, 0.06f, 0.14f),
            new(EnemyKind.PollenDroneSwarm, 0.1f, 0.06f), new(EnemyKind.CanopyParrot, 0.08f, 0.02f), new(EnemyKind.LeafFrog, 0.06f, 0.06f),
            new(EnemyKind.VineSnake, 0.06f, 0.06f), new(EnemyKind.PollenBee, 0.06f, 0.02f)
        ],
        [BiomeZone.BloomHives] =
        [
            new(EnemyKind.HiveGuard, 0.4f, 0.4f), new(EnemyKind.PollenDroneSwarm, 0.35f, 0.35f), new(EnemyKind.MudCrawler, 0.15f, 0.15f),
            new(EnemyKind.PollenBee, 0.1f, 0.1f)
        ],
        [BiomeZone.CrystalSpires] =
        [
            new(EnemyKind.PrismKnight, 0.12f, 0.14f), new(EnemyKind.ShardUnicorn, 0.08f, 0.06f), new(EnemyKind.LightMimic, 0.03f, 0.03f),
            new(EnemyKind.CrystalWraith, 0.06f, 0.14f), new(EnemyKind.RefractorTurret, 0.06f, 0.06f), new(EnemyKind.HaloBat, 0.08f, 0.14f),
            // Pearl golems (lumenite ore, the drill-power-85 ore) roam only the post-Fracture roster below.
            new(EnemyKind.SpireDragonfly, 0.12f, 0.06f), new(EnemyKind.PrismButterfly, 0.12f, 0.04f),
            new(EnemyKind.LightWispBird, 0.08f, 0.04f), new(EnemyKind.CrystalBunny, 0.12f, 0.06f), new(EnemyKind.HaloSnail, 0.08f, 0.08f)
        ],
        [BiomeZone.MiteWarrens] =
        [
            new(EnemyKind.SilkSpiderMite, 0.6f, 0.6f), new(EnemyKind.MiteBroodmother, 0.1f, 0.1f), new(EnemyKind.RockMite, 0.3f, 0.3f)
        ],
        [BiomeZone.PrecursorVault] =
        [
            new(EnemyKind.VaultSentinel, 0.35f, 0.35f), new(EnemyKind.GlyphDrone, 0.4f, 0.4f), new(EnemyKind.AncientGuardian, 0.1f, 0.1f),
            new(EnemyKind.AlabasterHoplite, 0.15f, 0.15f)
        ]
    };

    /// <summary>
    /// Post-Fracture rosters: every biome gains stronger creatures (existing hard kinds, never scaled copies) that take
    /// <see cref="HardShare"/> of the rolls once the world has fractured. Weights are Rimtera tuning.
    /// </summary>
    private static readonly Dictionary<BiomeZone, Entry[]> HardTables = new()
    {
        [BiomeZone.Grassland] =
        [
            new(EnemyKind.NeoTerraReaver, 0.3f, 0.3f), new(EnemyKind.TerraformerDrone, 0.3f, 0.15f), new(EnemyKind.HaloBat, 0.1f, 0.2f),
            new(EnemyKind.CrystalWraith, 0f, 0.2f), new(EnemyKind.StormRay, 0.15f, 0f), new(EnemyKind.WastelandBeast, 0.15f, 0.15f)
        ],
        [BiomeZone.Caverns] =
        [
            new(EnemyKind.TunnelWyrm, 0.2f, 0.2f), new(EnemyKind.RiftCaster, 0.2f, 0.2f), new(EnemyKind.CacheMimic, 0.08f, 0.08f),
            new(EnemyKind.VengefulWisp, 0.2f, 0.2f), new(EnemyKind.BasaltGolem, 0.2f, 0.2f), new(EnemyKind.GeodeCrab, 0.12f, 0.12f)
        ],
        [BiomeZone.FrozenWasteland] =
        [
            new(EnemyKind.GlacialGolem, 0.35f, 0.35f), new(EnemyKind.NeoTerraReaver, 0.25f, 0.25f), new(EnemyKind.HaloBat, 0.2f, 0.2f),
            new(EnemyKind.DiveKite, 0.2f, 0.2f)
        ],
        [BiomeZone.RadiatedDeeps] =
        [
            new(EnemyKind.RadLeech, 0.3f, 0.3f), new(EnemyKind.GlowStalker, 0.25f, 0.25f), new(EnemyKind.EventAbomination, 0.25f, 0.25f),
            new(EnemyKind.EventVoidSiphon, 0.2f, 0.2f)
        ],
        [BiomeZone.MoltenChasm] =
        [
            new(EnemyKind.CoreHusk, 0.3f, 0.3f), new(EnemyKind.FissionWisp, 0.3f, 0.3f), new(EnemyKind.TunnelWyrm, 0.2f, 0.2f),
            new(EnemyKind.EventAbomination, 0.2f, 0.2f)
        ],
        [BiomeZone.JunkWasteland] =
        [
            new(EnemyKind.WastelandBeast, 0.3f, 0.3f), new(EnemyKind.TerraformerDrone, 0.3f, 0.3f), new(EnemyKind.EventScrapElemental, 0.2f, 0.2f),
            new(EnemyKind.NeoTerraReaver, 0.2f, 0.2f)
        ],
        [BiomeZone.Overgrowth] =
        [
            new(EnemyKind.AstroBeast, 0.2f, 0.2f), new(EnemyKind.EventAbomination, 0.3f, 0.3f), new(EnemyKind.EventCorruptedSurvivor, 0.3f, 0.3f),
            new(EnemyKind.EventVoidSiphon, 0.2f, 0.2f)
        ],
        [BiomeZone.AcidCoast] =
        [
            new(EnemyKind.AcidShark, 0.3f, 0.3f), new(EnemyKind.BrineGolem, 0.2f, 0.2f), new(EnemyKind.CoralSentinel, 0.2f, 0.2f),
            new(EnemyKind.StormRay, 0.15f, 0.15f), new(EnemyKind.BloodfinAmbusher, 0.15f, 0.15f)
        ],
        [BiomeZone.FungalHollows] =
        [
            new(EnemyKind.SporeGolem, 0.3f, 0.3f), new(EnemyKind.ShroomStalker, 0.3f, 0.3f), new(EnemyKind.MycelidShaman, 0.2f, 0.2f),
            new(EnemyKind.RiftCaster, 0.2f, 0.2f)
        ],
        [BiomeZone.VerdantCanopy] =
        [
            new(EnemyKind.JungleStalker, 0.35f, 0.35f), new(EnemyKind.CanopyApe, 0.3f, 0.3f), new(EnemyKind.ThornLurker, 0.2f, 0.2f),
            new(EnemyKind.HiveGuard, 0.15f, 0.15f)
        ],
        [BiomeZone.BloomHives] =
        [
            new(EnemyKind.HiveGuard, 0.6f, 0.6f), new(EnemyKind.PollenDroneSwarm, 0.3f, 0.3f), new(EnemyKind.EventSwarmQueen, 0.1f, 0.1f)
        ],
        [BiomeZone.CrystalSpires] =
        [
            new(EnemyKind.PrismKnight, 0.3f, 0.3f), new(EnemyKind.ShardUnicorn, 0.25f, 0.25f), new(EnemyKind.PearlGolem, 0.2f, 0.2f),
            new(EnemyKind.CrystalWraith, 0.15f, 0.15f), new(EnemyKind.VengefulWisp, 0.1f, 0.1f)
        ],
        [BiomeZone.MiteWarrens] =
        [
            new(EnemyKind.MiteBroodmother, 0.4f, 0.4f), new(EnemyKind.SilkSpiderMite, 0.4f, 0.4f), new(EnemyKind.TunnelWyrm, 0.2f, 0.2f)
        ],
        [BiomeZone.PrecursorVault] =
        [
            new(EnemyKind.AncientGuardian, 0.35f, 0.35f), new(EnemyKind.VaultSentinel, 0.45f, 0.45f), new(EnemyKind.EventPrecursorProbe, 0.2f, 0.2f)
        ]
    };

    /// <summary>Share of spawn rolls that use the hardmode roster once the world has fractured.</summary>
    public const double HardShare = 0.55;

    /// <summary>Hardmode-aware pick: after the Fracture the first <see cref="HardShare"/> of the roll draws from the hard roster.</summary>
    public static EnemyKind Pick(BiomeZone zone, double roll, bool night, bool hardmode)
    {
        if (!hardmode || !HardTables.TryGetValue(zone, out var hard))
            return Pick(zone, roll, night);
        if (roll >= HardShare)
            return Pick(zone, (roll - HardShare) / (1d - HardShare), night);
        return PickFrom(hard, roll / HardShare, night);
    }

    private static EnemyKind PickFrom(Entry[] table, double roll, bool night)
    {
        var total = table.Sum(entry => night ? entry.NightWeight : entry.DayWeight);
        var target = Math.Clamp(roll, 0d, 0.999999d) * total;
        foreach (var entry in table)
        {
            target -= night ? entry.NightWeight : entry.DayWeight;
            if (target < 0d)
                return entry.Kind;
        }
        return table[^1].Kind;
    }

    /// <summary>Every kind a zone's hardmode roster adds (for tests and the bestiary).</summary>
    public static IReadOnlyList<EnemyKind> HardKindsFor(BiomeZone zone) =>
        HardTables.TryGetValue(zone, out var entries) ? entries.Select(entry => entry.Kind).ToArray() : Array.Empty<EnemyKind>();

    /// <summary>Zones with a hardmode roster.</summary>
    public static IEnumerable<BiomeZone> HardZones => HardTables.Keys;

    /// <summary>Ocean creatures that only appear inside deep-enough water (the spawner looks for water, not ground).</summary>
    public static bool IsSwimmer(EnemyKind kind) => kind is EnemyKind.AcidShark or EnemyKind.GlowJelly or EnemyKind.TideLeech
        or EnemyKind.FishSilver or EnemyKind.FishGlow or EnemyKind.BloodfinAmbusher || ContentFillCreatures.Swims(kind);

    /// <summary>True for biomes whose ambient spawns come from these tables.</summary>
    public static bool UsesTable(BiomeZone zone) => zone is BiomeZone.FrozenWasteland or BiomeZone.RadiatedDeeps
        or BiomeZone.MoltenChasm or BiomeZone.JunkWasteland or BiomeZone.Overgrowth || zone >= BiomeZone.AcidCoast;

    /// <summary>Picks a hostile creature for <paramref name="zone"/> from a roll in [0,1).</summary>
    public static EnemyKind Pick(BiomeZone zone, double roll, bool night)
    {
        var table = Tables.TryGetValue(zone, out var entries) ? entries : Tables[BiomeZone.Grassland];
        var total = table.Sum(entry => night ? entry.NightWeight : entry.DayWeight);
        var target = Math.Clamp(roll, 0d, 0.999999d) * total;
        foreach (var entry in table)
        {
            target -= night ? entry.NightWeight : entry.DayWeight;
            if (target < 0d)
                return entry.Kind;
        }
        return table[^1].Kind;
    }

    /// <summary>The precursor dungeon's own roster (rolled while the player stands in front of dungeon wall).</summary>
    private static readonly Entry[] DungeonTable = new Entry[]
    {
        new(EnemyKind.Wraith, 0.3f, 0.36f), new(EnemyKind.GlyphDrone, 0.26f, 0.22f), new(EnemyKind.AlabasterHoplite, 0.26f, 0.22f),
        new(EnemyKind.VengefulWisp, 0.18f, 0.2f)
    }.Concat(FillEntries("dungeon", hard: false)).ToArray();

    // ---- Content fill (docs/TERRARIA_GAPS.md plan item 4) ----

    private static IEnumerable<Entry> FillEntries(string roster, bool hard) =>
        ContentFillCreatures.Roster(roster, hard).Select(entry => new Entry(entry.Kind, entry.Day, entry.Night));

    /// <summary>
    /// Share of ambient spawns in the context-band biomes (grassland surface, the soil layer and the caverns) that draw from
    /// the content-fill rosters instead of the legacy bands.
    /// </summary>
    public const double FillShare = 0.5;

    private static readonly Dictionary<string, Entry[]> FillRosters = new[] { "grass", "under", "cave" }
        .ToDictionary(key => key, key => FillEntries(key, hard: false).ToArray());

    static BiomeSpawnTables()
    {
        // Every table biome gains its content-fill creatures (critters and hostiles) and post-Fracture versions. The grassland
        // and cavern regular tables stay the raid rosters; their ambient fill comes through ContentFill below.
        foreach (var (zone, roster) in ContentFillCreatures.ZoneRosters)
        {
            if (zone is not (BiomeZone.Grassland or BiomeZone.Caverns) && Tables.TryGetValue(zone, out var regular))
                Tables[zone] = regular.Concat(FillEntries(roster, hard: false)).ToArray();
            if (HardTables.TryGetValue(zone, out var hard))
                HardTables[zone] = hard.Concat(FillEntries(roster, hard: true)).ToArray();
        }
        // Creature coverage: hardmode extras for the thinnest hard rosters (mite warrens, bloom hives).
        foreach (var (zone, extras) in CreatureCoverage.HardExtras())
            if (HardTables.TryGetValue(zone, out var hard))
                HardTables[zone] = hard.Concat(extras.Select(entry => new Entry(entry.Kind, entry.Day, entry.Night))).ToArray();
    }

    private static double Fraction(double value) => value - Math.Floor(value);

    /// <summary>The content-fill slot name for a reading: surface grassland, the soil layer, or the deep caverns.</summary>
    public static string FillSlot(bool readingUnderground, bool deep) => deep ? "cave" : readingUnderground ? "under" : "grass";

    /// <summary>
    /// Ambient spawn hook for the context-band biomes: half of the rolls (decided by a hash of the same roll, so the stream is
    /// unchanged) are replaced by a pick from the slot's content-fill roster. Table biomes, the dungeon and hardmode rolls
    /// that already went to the hard roster are left alone.
    /// </summary>
    public static EnemyKind ContentFill(BiomeReading reading, bool inDungeon, bool deep, bool night, bool hardmode, double roll, EnemyKind kind)
    {
        if (inDungeon || UsesTable(reading.Zone) || hardmode && roll < HardShare)
            return kind;
        if (Fraction(roll * 7919.0 + 0.137) >= FillShare)
            return kind;
        return PickFrom(FillRosters[FillSlot(reading.Underground, deep)], Fraction(roll * 104729.0 + 0.61), night);
    }

    /// <summary>Kinds in a content-fill context slot ("grass", "under", "cave"), for tests and the bestiary.</summary>
    public static IReadOnlyList<EnemyKind> FillKinds(string slot) =>
        FillRosters.TryGetValue(slot, out var entries) ? entries.Select(entry => entry.Kind).ToArray() : Array.Empty<EnemyKind>();

    /// <summary>Spawn weight of <paramref name="kind"/> in a zone table, a fill slot or the dungeon (tests): (day, night).</summary>
    public static (float Day, float Night) WeightIn(string where, EnemyKind kind, bool hard = false)
    {
        Entry[] table = where == "dungeon" ? DungeonTable
            : FillRosters.TryGetValue(where, out var fill) && !hard ? fill
            : Enum.TryParse<BiomeZone>(where, out var zone) ? (hard ? HardTables : Tables).GetValueOrDefault(zone) ?? Array.Empty<Entry>()
            : Array.Empty<Entry>();
        return (table.Where(entry => entry.Kind == kind).Sum(entry => entry.DayWeight),
            table.Where(entry => entry.Kind == kind).Sum(entry => entry.NightWeight));
    }

    /// <summary>Picks from a fill slot directly (tests).</summary>
    public static EnemyKind PickFill(string slot, double roll, bool night) => PickFrom(FillRosters[slot], roll, night);

    /// <summary>Picks a dungeon dweller from a roll in [0,1).</summary>
    public static EnemyKind PickDungeon(double roll, bool night)
    {
        var total = DungeonTable.Sum(entry => night ? entry.NightWeight : entry.DayWeight);
        var target = Math.Clamp(roll, 0d, 0.999999d) * total;
        foreach (var entry in DungeonTable)
        {
            target -= night ? entry.NightWeight : entry.DayWeight;
            if (target < 0d)
                return entry.Kind;
        }
        return DungeonTable[^1].Kind;
    }

    /// <summary>Every kind the dungeon roster can produce.</summary>
    public static IReadOnlyList<EnemyKind> DungeonKinds => DungeonTable.Select(entry => entry.Kind).ToArray();

    /// <summary>Every kind a zone's table can produce (for tests and the bestiary).</summary>
    public static IReadOnlyList<EnemyKind> KindsFor(BiomeZone zone) =>
        Tables.TryGetValue(zone, out var entries) ? entries.Select(entry => entry.Kind).ToArray() : Array.Empty<EnemyKind>();
}
