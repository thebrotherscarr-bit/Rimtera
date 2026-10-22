using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Creature coverage pass (docs/CREATURE_COVERAGE.md): every biome variant, event and boss gets its own creatures,
/// mini-bosses and summon items, matching Terraria's structure with original Rimtera content. This class turns
/// <see cref="CreatureCoverageData"/> into archetypes, variant spawn rosters, drops, items and recipes, and lists which
/// creatures and mini-bosses each event brings. The simulation side lives in GameSimulation.CreatureCoverage.cs.
/// </summary>
public static class CreatureCoverage
{
    /// <summary>Share of spawn rolls a biome variant (sky, underground junk or frost, grave field, late dungeon) takes over.</summary>
    public const double VariantShare = 0.55;
    /// <summary>Memorial markers within the grave-field box that turn the surface into a grave field.</summary>
    public const int GraveFieldMarkers = 6;
    /// <summary>Light fixtures near an underground player that call the Lumen Tempest.</summary>
    public const int LumenTempestLights = 40;

    /// <summary>The variant slots and what they stand for (biome, Terraria variant).</summary>
    public static readonly IReadOnlyDictionary<string, string> VariantSlots = new Dictionary<string, string>
    {
        ["sky"] = "Sky layer above any surface biome (Terraria: space)",
        ["junk-under"] = "Underground Junk Wasteland (Terraria: underground desert)",
        ["frozen-under"] = "Underground Frozen Wasteland (Terraria: ice caverns)",
        ["grave"] = "Grave field: six memorial markers near the surface (Terraria: graveyard)",
        ["dungeon-hard"] = "Precursor dungeon after the Mutated Flora (Terraria: post-Plantera dungeon)"
    };

    /// <summary>Hardmode extras appended to thin biome hard rosters.</summary>
    public static readonly IReadOnlyDictionary<string, BiomeZone> ExtraSlots = new Dictionary<string, BiomeZone>
    {
        ["mite"] = BiomeZone.MiteWarrens, ["hive"] = BiomeZone.BloomHives
    };

    private static readonly Dictionary<EnemyKind, CoverageCreature> CreatureByKind = CreatureCoverageData.Creatures.ToDictionary(row => row.Kind);
    private static readonly Dictionary<EnemyKind, CoverageBoss> BossByKind = CreatureCoverageData.Bosses.ToDictionary(row => row.Kind);

    /// <summary>Every kind this pass adds (creatures, mini-bosses and bosses).</summary>
    public static IReadOnlyList<EnemyKind> AllKinds { get; } =
        CreatureCoverageData.Creatures.Select(row => row.Kind).Concat(CreatureCoverageData.Bosses.Select(row => row.Kind)).ToArray();

    public static bool Contains(EnemyKind kind) => CreatureByKind.ContainsKey(kind) || BossByKind.ContainsKey(kind);
    public static CoverageCreature? Creature(EnemyKind kind) => CreatureByKind.GetValueOrDefault(kind);
    public static CoverageBoss? Boss(EnemyKind kind) => BossByKind.GetValueOrDefault(kind);
    public static bool IsMiniBoss(EnemyKind kind) => CreatureByKind.TryGetValue(kind, out var row) && row.Role == CoverageRole.MiniBoss;

    // ------------------------------------------------------------------ archetypes and drops

    public static IEnumerable<EnemyArchetype> Archetypes()
    {
        foreach (var row in CreatureCoverageData.Creatures)
        {
            var hopper = row.Family is EnemyAiFamily.Hopper or EnemyAiFamily.LeapingHopper or EnemyAiFamily.SkitteringHopper;
            var mini = row.Role == CoverageRole.MiniBoss;
            yield return new EnemyArchetype(row.Kind, row.Name, new Vector2(row.Width, row.Height), row.HitPoints, row.Damage, row.Flying)
            {
                Family = row.Family,
                Hops = hopper,
                Stationary = row.Stationary,
                CreditDrop = row.Credits,
                SegmentCount = row.Segments,
                Underground = row.Family is EnemyAiFamily.Worm or EnemyAiFamily.Caster && row.Slot.EndsWith("-under", StringComparison.Ordinal),
                ContactCooldown = row.Family is EnemyAiFamily.Worm or EnemyAiFamily.Mimic ? 1f : hopper ? 1.2f : mini ? 1.3f : 1.5f,
                AttackRange = mini ? 11f : 9f,
                MoveSpeedMultiplier = row.Speed > 0f ? row.Speed
                    : row.Family == EnemyAiFamily.Mimic ? 1.5f : row.Width >= 1.5f ? 0.75f : 1.1f
            };
        }
        foreach (var boss in CreatureCoverageData.Bosses)
            yield return new EnemyArchetype(boss.Kind, boss.Name, new Vector2(boss.Width, boss.Height), boss.HitPoints, boss.Damage, boss.Flying,
                boss.Tier, ProjectileSpeed: boss.ProjectileSpeed, VolleyCount: boss.Volley)
            {
                CreditDrop = boss.Credits, AttackRange = 13f, MoveSpeedMultiplier = boss.Flying ? 1.1f : 0.85f
            };
    }

    private static readonly Dictionary<EnemyKind, NpcDropDefinition[]> DropTable =
        CreatureCoverageData.Creatures.Select(row => (row.Kind, row.Drops))
            .Concat(CreatureCoverageData.Bosses.Select(row => (row.Kind, row.Drops)))
            .ToDictionary(pair => pair.Kind, pair => pair.Drops.Select(drop => new NpcDropDefinition(pair.Kind, drop.Item, drop.Count)).ToArray());

    /// <summary>Drops of the coverage creatures and bosses (null for other kinds).</summary>
    public static NpcDropDefinition[]? Drops(EnemyKind kind) => DropTable.GetValueOrDefault(kind);

    // ------------------------------------------------------------------ biome variants

    /// <summary>Weighted entries of a variant or extra slot: the regular roster, or the post-Fracture one.</summary>
    public static IEnumerable<(EnemyKind Kind, float Day, float Night)> Roster(string slot, bool hard) =>
        CreatureCoverageData.Creatures.Where(row => row.Slot == slot && row.Hard == hard).Select(row => (row.Kind, row.Day, row.Night));

    /// <summary>Every kind a variant slot can produce (both rosters).</summary>
    public static IReadOnlyList<EnemyKind> VariantKinds(string slot) =>
        CreatureCoverageData.Creatures.Where(row => row.Slot == slot).Select(row => row.Kind).ToArray();

    /// <summary>Hardmode extras per zone (appended to <see cref="BiomeSpawnTables"/>' hard rosters).</summary>
    public static IEnumerable<(BiomeZone Zone, (EnemyKind Kind, float Day, float Night)[] Entries)> HardExtras() =>
        ExtraSlots.Select(pair => (pair.Value, Roster(pair.Key, hard: true).ToArray()));

    private static double Fraction(double value) => value - Math.Floor(value);

    /// <summary>True when a spawn roll is handed to the active variant (a hash of the roll, so the stream is unchanged).</summary>
    public static bool VariantTakes(double roll) => Fraction(roll * 6151.0 + 0.29) < VariantShare;

    /// <summary>
    /// Picks from a variant slot. After the Fracture <see cref="BiomeSpawnTables.HardShare"/> of the rolls use the hard roster;
    /// slots with only one roster always use it.
    /// </summary>
    public static EnemyKind? PickVariant(string slot, bool fractured, double roll, bool night)
    {
        var regular = Roster(slot, hard: false).ToArray();
        var hard = fractured ? Roster(slot, hard: true).ToArray() : [];
        if (regular.Length == 0 && hard.Length == 0)
            return null;
        var useHard = hard.Length > 0 && (regular.Length == 0 || Fraction(roll * 3571.0 + 0.71) < BiomeSpawnTables.HardShare);
        var table = useHard ? hard : regular;
        var total = table.Sum(entry => night ? entry.Night : entry.Day);
        var target = Fraction(roll * 15485863.0 + 0.37) * (total > 0f ? total : table.Length);
        foreach (var entry in table)
        {
            target -= total > 0f ? (night ? entry.Night : entry.Day) : 1f;
            if (target < 0d)
                return entry.Kind;
        }
        return table[^1].Kind;
    }

    // ------------------------------------------------------------------ events

    /// <summary>The coverage creatures native to an event (its "event:Kind" slot).</summary>
    public static IReadOnlyList<EnemyKind> EventNatives(WorldEventKind kind) =>
        CreatureCoverageData.Creatures.Where(row => row.Slot == "event:" + kind).Select(row => row.Kind).ToArray();

    /// <summary>
    /// Event mini-bosses the simulation brings once per event, after the Fracture, when the event's progress reaches the
    /// given share (Terraria: the Ice Golem in a blizzard, the Sand Elemental in a sandstorm, the Dreadnautilus on a blood moon,
    /// the Goblin Summoner in a hardmode goblin army). The wave moons bring theirs by wave (<see cref="SeasonalEvents"/>).
    /// </summary>
    public static IReadOnlyList<(WorldEventKind Event, EnemyKind MiniBoss, float AtProgress)> EventMiniBosses { get; } =
    [
        (WorldEventKind.Blizzard, EnemyKind.BlizzardColossus, 0.25f),
        (WorldEventKind.DustStorm, EnemyKind.GritElemental, 0.25f),
        (WorldEventKind.RustMoon, EnemyKind.CorrosionNautilus, 0.4f),
        (WorldEventKind.ScavengerRaid, EnemyKind.ScavengerHexer, 0.3f)
    ];

    /// <summary>Dark Sun units that join after the Mutated Flora (Terraria's post-Plantera eclipse).</summary>
    public static IReadOnlyList<EnemyKind> LateDarkSun { get; } = [EnemyKind.EclipseSlasher, EnemyKind.EclipseSphere];

    /// <summary>Bosses whose only trigger is a world condition rather than a summon item (with the condition).</summary>
    public static IReadOnlyDictionary<EnemyKind, string> ConditionalBosses { get; } = new Dictionary<EnemyKind, string>
    {
        [EnemyKind.AbyssLeviathan] = "Fished up with abyss bait in acid (Terraria: Duke Fishron from truffle-worm bait)"
    };

    /// <summary>Event starter items this pass adds, and the night/day rule each enforces.</summary>
    public static IReadOnlyDictionary<ItemId, WorldEventKind> Starters { get; } = new Dictionary<ItemId, WorldEventKind>
    {
        [ItemId.CorrodedFlare] = WorldEventKind.RustMoon, [ItemId.UmbralLens] = WorldEventKind.DarkSun
    };

    // ------------------------------------------------------------------ items and recipes

    public static void RegisterItems(Dictionary<ItemId, ItemDefinition> items)
    {
        void Summon(ItemId id, string name, EnemyKind boss, int tier) =>
            items[id] = new ItemDefinition(id, name, ItemUseKind.Consumable, 20)
                { SummonsBoss = boss, Tier = tier, Rarity = ContentItems.RarityFor(tier), Value = 40 + tier * 30 };
        void Starter(ItemId id, string name, WorldEventKind kind, int tier) =>
            items[id] = new ItemDefinition(id, name, ItemUseKind.Consumable, 20)
                { StartsEvent = kind, Tier = tier, Rarity = ContentItems.RarityFor(tier), Value = 50 + tier * 30 };
        Summon(ItemId.FrostbittenLure, "Frostbitten lure", EnemyKind.HoarfrostBehemoth, 3);
        Summon(ItemId.PrismGelLattice, "Prism gel lattice", EnemyKind.GelSovereign, 4);
        Summon(ItemId.RegentWick, "Regent's wick", EnemyKind.HollowRegent, 6);
        Summon(ItemId.SeraphFrostCore, "Seraph frost-core", EnemyKind.RimeSeraph, 6);
        Starter(ItemId.CorrodedFlare, "Corroded flare", WorldEventKind.RustMoon, 2);
        Starter(ItemId.UmbralLens, "Umbral lens", WorldEventKind.DarkSun, 5);
    }

    /// <summary>Summon and starter recipes, each at the station of its progression tier.</summary>
    public static IReadOnlyList<(ItemId Output, CraftingStationId Station, (ItemId Item, int Count)[] Inputs)> Recipes { get; } =
    [
        // Pre-Fracture, any time: frost materials at the Chem Lab (Terraria: the Deer Thing at a demon altar).
        (ItemId.FrostbittenLure, CraftingStationId.ChemLab, [(ItemId.FrostCrystal, 12), (ItemId.GlacialOre, 10), (ItemId.BioGel, 4)]),
        // Early hardmode: the Alloy Anvil already needs the Fission Tyrant's slag (Terraria: the Gelatin Crystal in the hallow).
        (ItemId.PrismGelLattice, CraftingStationId.AlloyAnvil, [(ItemId.PrismShard, 10), (ItemId.BioGel, 20), (ItemId.PrismEssence, 3)]),
        // Moon bosses outside their moons: the moons' own drops at the Nano-Fabricator Mk2.
        (ItemId.RegentWick, CraftingStationId.NanoFabricatorMk2, [(ItemId.HollowEmber, 20), (ItemId.LumeniteBar, 6)]),
        (ItemId.SeraphFrostCore, CraftingStationId.NanoFabricatorMk2, [(ItemId.RimeShard, 20), (ItemId.LumeniteBar, 6)]),
        // Event starters (Terraria: the Bloody Tear and the Solar Tablet).
        (ItemId.CorrodedFlare, CraftingStationId.ChemLab, [(ItemId.OxidizedShard, 6), (ItemId.BioGel, 4)]),
        (ItemId.UmbralLens, CraftingStationId.NanoFabricatorMk2, [(ItemId.VoidEssence, 6), (ItemId.LumeniteBar, 2), (ItemId.GlassBlock, 10)])
    ];

    public static void RegisterRecipes(CraftingSystem crafting)
    {
        foreach (var (output, station, inputs) in Recipes)
            crafting.Register(new RecipeDefinition("cc-" + output, output, 1,
                inputs.Select(entry => IngredientRequirement.Exact(entry.Item, entry.Count)).ToArray(),
                new HashSet<CraftingStationId> { station }));
    }
}
