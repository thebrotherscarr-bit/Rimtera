using System.Text.RegularExpressions;
using Rimtera.Entities;

namespace Rimtera.Systems;

/// <summary>`--item-census`: counts the item catalog by category (weapons by style, armor by slot, tools, consumables).</summary>
public static class ItemCensus
{
    public static IReadOnlyDictionary<string, int> Count(ItemCatalog catalog)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in catalog.All)
        {
            var key = Categorize(item);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        return counts;
    }

    public static string Categorize(ItemDefinition item)
    {
        var name = item.Id.ToString();
        bool Has(string pattern) => Regex.IsMatch(name, pattern);
        if (item.Style != WeaponStyle.None)
            return "weapon: " + item.Style.ToString().ToLowerInvariant();
        return item.UseKind switch
        {
            ItemUseKind.Pickaxe => Has("Drill") ? "tool: drill" : "tool: pickaxe",
            ItemUseKind.Melee => "weapon: melee (generic)",
            ItemUseKind.Ranged => "weapon: ranged (generic)",
            ItemUseKind.Tech => "weapon: tech (generic)",
            ItemUseKind.Command => item.Summons is not null ? "weapon: summon" : "weapon: command",
            ItemUseKind.Ammunition => "ammo",
            ItemUseKind.Food => "food",
            ItemUseKind.Consumable => item.GrantsStatus is not null ? "potion/buff" : "consumable",
            ItemUseKind.Equipment => item.EquipmentSlot switch
            {
                EquipmentSlot.Head => "armor: head",
                EquipmentSlot.Body => "armor: body",
                EquipmentSlot.Legs => "armor: legs",
                _ => Has("Wing") ? "accessory: wings" : "accessory"
            },
            ItemUseKind.Vanity => "vanity",
            ItemUseKind.PlaceTile => "placeable",
            ItemUseKind.PlaceWall => "wall",
            ItemUseKind.Material => "material",
            _ => item.UseKind.ToString().ToLowerInvariant()
        };
    }

    /// <summary>Armor set ids that have a head, a body and a legs piece.</summary>
    public static int CompleteArmorSets(ItemCatalog catalog) => catalog.All
        .Where(item => item.ArmorSetId is not null && item.EquipmentSlot is EquipmentSlot.Head or EquipmentSlot.Body or EquipmentSlot.Legs)
        .GroupBy(item => item.ArmorSetId!, StringComparer.OrdinalIgnoreCase)
        .Count(group => group.Select(item => item.EquipmentSlot).Distinct().Count() == 3);

    /// <summary>`--balance`: best full-set defense per armor tier beside enemy hit damage, bucketed by enemy hit points.</summary>
    public static int Balance()
    {
        var catalog = new ItemCatalog();
        var sets = catalog.All.Where(item => item.ArmorSetId is not null && item.EquipmentSlot is EquipmentSlot.Head or EquipmentSlot.Body or EquipmentSlot.Legs)
            .GroupBy(item => item.ArmorSetId!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(item => item.EquipmentSlot).Distinct().Count() == 3)
            .Select(group => (Tier: group.Max(item => item.Tier), Defense: group.Sum(item => item.Defense) + group.Max(item => item.SetBonusDefense)));
        Console.WriteLine("Armor tier: best full-set defense (sets)");
        foreach (var tier in sets.GroupBy(set => set.Tier).OrderBy(group => group.Key))
            Console.WriteLine($"  T{tier.Key}: {tier.Max(set => set.Defense),3}  ({tier.Count()} sets, lowest {tier.Min(set => set.Defense)})");
        var accessories = catalog.All.Where(item => item.EquipmentSlot is >= EquipmentSlot.Accessory && item.UseKind == ItemUseKind.Equipment)
            .OrderByDescending(item => item.Defense).Take(5).Sum(item => item.Defense);
        Console.WriteLine($"Best five accessories' defense: {accessories}");
        Console.WriteLine("Accessory defense values: " + string.Join(", ", catalog.All
            .Where(item => item.EquipmentSlot is >= EquipmentSlot.Accessory && item.UseKind == ItemUseKind.Equipment && item.Defense > 0)
            .GroupBy(item => item.Defense).OrderBy(group => group.Key).Select(group => $"{group.Key}x{group.Count()}")));
        Console.WriteLine("Enemies by hit points: count, contact damage min / median / max");
        var enemies = Rimtera.Entities.EnemyArchetypes.All.Where(archetype => !archetype.Passive).ToArray();
        foreach (var bucket in new[] { (0f, 60f), (60f, 150f), (150f, 400f), (400f, 1000f), (1000f, 5000f), (5000f, float.MaxValue) })
        {
            var inBucket = enemies.Where(archetype => archetype.HitPoints >= bucket.Item1 && archetype.HitPoints < bucket.Item2)
                .Select(archetype => archetype.AttackDamage).OrderBy(damage => damage).ToArray();
            if (inBucket.Length > 0)
                Console.WriteLine($"  HP {bucket.Item1,5:0}-{(bucket.Item2 > 1e9 ? "max" : bucket.Item2.ToString("0")),-5}: {inBucket.Length,3}  {inBucket[0],5:0.#} / {inBucket[inBucket.Length / 2],5:0.#} / {inBucket[^1],5:0.#}");
        }

        Console.WriteLine("Bosses and 400+ HP creatures: tier, hit points, authored hit, scaled hit");
        foreach (var archetype in enemies.Where(archetype => archetype.IsBoss || archetype.HitPoints >= 400f).OrderBy(archetype => archetype.Tier).ThenBy(archetype => archetype.HitPoints))
                Console.WriteLine($"    {archetype.Kind,-24}{archetype.Tier,-7}{archetype.HitPoints,7:0}{archetype.AttackDamage,6:0}{archetype.AttackDamage * DefenseBalance.EnemyHitScale(archetype),7:0}");
        // Each armor tier against the median non-boss creature of its band, after toughness scaling and defense (Classic).
        float Median(float low, float high, BossTier boss = BossTier.None)
        {
            var hits = enemies.Where(archetype => archetype.Kind != Rimtera.Entities.EnemyKind.DungeonGuardian &&
                                                  (boss == BossTier.None
                                                      ? !archetype.IsBoss && archetype.HitPoints >= low && archetype.HitPoints < high
                                                      : archetype.Tier == boss))
                .Select(archetype => archetype.AttackDamage * DefenseBalance.EnemyHitScale(archetype)).OrderBy(damage => damage).ToArray();
            return hits.Length == 0 ? 0f : hits[hits.Length / 2];
        }
        var bestByTier = sets.GroupBy(set => set.Tier).ToDictionary(group => group.Key, group => group.Max(set => set.Defense));
        Console.WriteLine("Tier armor vs its band (Classic): band median hit -> after defense (cut)");
        var bands = new (int Tier, string Label, float Low, float High, BossTier Boss)[]
        {
            (0, "HP 0-60", 0f, 60f, BossTier.None), (1, "HP 0-60", 0f, 60f, BossTier.None), (1, "gate boss", 0f, 0f, BossTier.Gate),
            (2, "HP 60-150", 60f, 150f, BossTier.None), (3, "HP 150-400", 150f, 400f, BossTier.None), (3, "easy bosses", 0f, 0f, BossTier.Easy),
            (4, "HP 150-400", 150f, 400f, BossTier.None), (5, "HP 400+", 400f, float.MaxValue, BossTier.None),
            (5, "hard bosses", 0f, 0f, BossTier.Hard), (6, "hard bosses", 0f, 0f, BossTier.Hard), (6, "event bosses", 0f, 0f, BossTier.Event),
            (7, "final boss", 0f, 0f, BossTier.Final)
        };
        foreach (var band in bands)
        {
            if (!bestByTier.TryGetValue(band.Tier, out var defense))
                continue;
            var hit = Median(band.Low, band.High, band.Boss);
            var after = DefenseBalance.ApplyDefense(hit, defense, WorldDifficulty.Classic);
            Console.WriteLine($"  T{band.Tier} def {defense,2} vs {band.Label,-12} hit {hit,5:0.#} -> {after,5:0.#}  ({1f - after / hit:P0} cut)");
        }
        var endgame = bestByTier.Values.Max() + accessories;
        var finalHit = Median(0f, 0f, BossTier.Final);
        var finalAfter = DefenseBalance.ApplyDefense(finalHit, endgame, WorldDifficulty.Classic);
        Console.WriteLine($"  Endgame armor + best accessories def {endgame} vs final boss hit {finalHit:0.#} -> {finalAfter:0.#}  ({1f - finalAfter / finalHit:P0} cut)");
        return 0;
    }

    public static int Run()
    {
        var catalog = new ItemCatalog();
        var counts = Count(catalog);
        foreach (var (category, count) in counts)
            Console.WriteLine($"{category,-28}{count,6}");
        Console.WriteLine($"{"total",-28}{counts.Values.Sum(),6}");
        Console.WriteLine($"{"armor sets (complete)",-28}{CompleteArmorSets(catalog),6}");
        return 0;
    }
}
