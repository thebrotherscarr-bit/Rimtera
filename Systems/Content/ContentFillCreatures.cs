using System.Numerics;
using Rimtera.Entities;

namespace Rimtera.Systems;

/// <summary>
/// Content fill (docs/TERRARIA_GAPS.md plan item 4): turns <see cref="ContentFillData"/> into archetypes, spawn rosters,
/// swimmer rules and drops. Critters are passive and catchable (gold variants are rare and valuable); hostiles use the
/// existing AI families so each behaves distinctly. Rosters are keyed by a short biome slot name.
/// </summary>
public static class ContentFillCreatures
{
    /// <summary>The roster slot each table-driven biome reads its content-fill creatures from.</summary>
    public static readonly IReadOnlyDictionary<BiomeZone, string> ZoneRosters = new Dictionary<BiomeZone, string>
    {
        [BiomeZone.Grassland] = "grass", [BiomeZone.Caverns] = "cave", [BiomeZone.FrozenWasteland] = "frozen",
        [BiomeZone.JunkWasteland] = "junk", [BiomeZone.AcidCoast] = "coast", [BiomeZone.VerdantCanopy] = "canopy",
        [BiomeZone.BloomHives] = "hive", [BiomeZone.FungalHollows] = "fungal", [BiomeZone.CrystalSpires] = "crystal",
        [BiomeZone.RadiatedDeeps] = "rad", [BiomeZone.Overgrowth] = "over", [BiomeZone.MoltenChasm] = "core",
        [BiomeZone.MiteWarrens] = "mite", [BiomeZone.PrecursorVault] = "vault"
    };

    private static readonly HashSet<EnemyKind> SwimmerSet =
        ContentFillData.Critters.Where(critter => critter.Swims).Select(critter => critter.Kind)
            .Concat(ContentFillData.Hostiles.Where(hostile => hostile.Swims).Select(hostile => hostile.Kind)).ToHashSet();

    private static readonly Dictionary<EnemyKind, NpcDropDefinition[]> DropTable = ContentFillData.Hostiles.ToDictionary(
        hostile => hostile.Kind,
        hostile => hostile.Drops.Select(drop => new NpcDropDefinition(hostile.Kind, drop.Item, drop.Count)).ToArray());

    /// <summary>Every kind this pass adds (critters, gold variants and hostiles).</summary>
    public static IReadOnlyList<EnemyKind> AllKinds { get; } =
        ContentFillData.Critters.Select(critter => critter.Kind).Concat(ContentFillData.Hostiles.Select(hostile => hostile.Kind)).ToArray();

    /// <summary>Gold variants and the critter each one is a rare recolour of.</summary>
    public static IEnumerable<(EnemyKind Gold, EnemyKind Base)> GoldVariants =>
        ContentFillData.Critters.Where(critter => critter.GoldOf is not null).Select(critter => (critter.Kind, critter.GoldOf!.Value));

    public static bool IsGold(EnemyKind kind) => ContentFillData.Critters.Any(critter => critter.Kind == kind && critter.GoldOf is not null);

    /// <summary>Content-fill creatures that live in water or sea fuel (spawned inside it, like the ocean swimmers).</summary>
    public static bool Swims(EnemyKind kind) => SwimmerSet.Contains(kind);

    /// <summary>Biome-material drops of the content-fill hostiles (null for other kinds).</summary>
    public static NpcDropDefinition[]? Drops(EnemyKind kind) => DropTable.GetValueOrDefault(kind);

    /// <summary>Archetypes for every content-fill creature (appended to the roster by Entities/ContentCreatures.cs).</summary>
    public static IEnumerable<EnemyArchetype> Archetypes()
    {
        foreach (var critter in ContentFillData.Critters)
        {
            var gold = critter.GoldOf is not null;
            yield return new EnemyArchetype(critter.Kind, critter.Name, new Vector2(critter.Width, critter.Height), gold ? 12f : 6f, 0f,
                critter.Flying)
            {
                Passive = true, Catchable = true, MoveSpeedMultiplier = gold ? 1.2f : 0.9f,
                Hops = !critter.Flying && critter.Height >= 0.45f
            };
        }
        foreach (var hostile in ContentFillData.Hostiles)
        {
            var hopper = hostile.Family is EnemyAiFamily.Hopper or EnemyAiFamily.LeapingHopper or EnemyAiFamily.SkitteringHopper;
            yield return new EnemyArchetype(hostile.Kind, hostile.Name, new Vector2(hostile.Width, hostile.Height), hostile.HitPoints,
                hostile.Damage, hostile.Flying)
            {
                Family = hostile.Family,
                Hops = hopper,
                Stationary = hostile.Stationary,
                CreditDrop = hostile.Credits,
                SegmentCount = hostile.Segments,
                Underground = hostile.Family is EnemyAiFamily.Worm or EnemyAiFamily.Caster && hostile.Roster is "under" or "cave" or "core",
                ContactCooldown = hostile.Family is EnemyAiFamily.Worm or EnemyAiFamily.Mimic ? 1f : hopper ? 1.2f : 1.5f,
                MoveSpeedMultiplier = hostile.Family == EnemyAiFamily.Mimic ? 1.5f : hostile.Width >= 1.5f ? 0.75f : 1.1f
            };
        }
    }

    /// <summary>
    /// Weighted entries for a roster slot: its critters (plus existing critters given a slot there) and its hostiles,
    /// either the regular roster or (when <paramref name="hard"/>) the post-Fracture one.
    /// </summary>
    public static IEnumerable<(EnemyKind Kind, float Day, float Night)> Roster(string roster, bool hard)
    {
        if (!hard)
        {
            foreach (var critter in ContentFillData.Critters.Where(critter => critter.Roster == roster))
                yield return (critter.Kind, critter.DayWeight, critter.NightWeight);
            foreach (var slot in ContentFillData.ExistingCritterSlots.Where(slot => slot.Roster == roster))
                yield return (slot.Kind, slot.Day, slot.Night);
        }
        foreach (var hostile in ContentFillData.Hostiles.Where(hostile => hostile.Roster == roster && hostile.Hard == hard))
            yield return (hostile.Kind, hostile.DayWeight, hostile.NightWeight);
    }

    /// <summary>Catch-item value of a critter: gold variants are worth far more.</summary>
    public static int CatchValue(FillCritter critter) => critter.GoldOf is not null ? 600 : 10 + critter.BaitPower;
}
