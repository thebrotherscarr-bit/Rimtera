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
        Console.WriteLine("Enemies by hit points: count, contact damage min / median / max, defense min / median / max");
        var enemies = Rimtera.Entities.EnemyArchetypes.All.Where(archetype => !archetype.Passive).ToArray();
        foreach (var bucket in new[] { (0f, 60f), (60f, 150f), (150f, 400f), (400f, 1000f), (1000f, 5000f), (5000f, float.MaxValue) })
        {
            var members = enemies.Where(archetype => archetype.HitPoints >= bucket.Item1 && archetype.HitPoints < bucket.Item2).ToArray();
            var inBucket = members.Select(archetype => archetype.AttackDamage).OrderBy(damage => damage).ToArray();
            var defenses = members.Select(archetype => archetype.Defense).OrderBy(defense => defense).ToArray();
            if (inBucket.Length > 0)
                Console.WriteLine($"  HP {bucket.Item1,5:0}-{(bucket.Item2 > 1e9 ? "max" : bucket.Item2.ToString("0")),-5}: {inBucket.Length,3}  {inBucket[0],5:0.#} / {inBucket[inBucket.Length / 2],5:0.#} / {inBucket[^1],5:0.#}" +
                                  $"   def {defenses[0],2} / {defenses[defenses.Length / 2],2} / {defenses[^1],2}");
        }

        Console.WriteLine("Bosses and 400+ HP creatures: tier, hit points, authored hit, scaled hit, defense");
        foreach (var archetype in enemies.Where(archetype => archetype.IsBoss || archetype.HitPoints >= 400f).OrderBy(archetype => archetype.Tier).ThenBy(archetype => archetype.HitPoints))
                Console.WriteLine($"    {archetype.Kind,-24}{archetype.Tier,-7}{archetype.HitPoints,7:0}{archetype.AttackDamage,6:0}{archetype.AttackDamage * DefenseBalance.EnemyHitScale(archetype),7:0}{archetype.Defense,5}");
        PrintTimeToKill(catalog, enemies);
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

    /// <summary>
    /// Time to kill per weapon tier (Classic, no crits, no armor penetration): the tier's median weapon damage against
    /// its creature band, after half the creature's defense, with the Fracture's rescale on the post-Fracture bands.
    /// </summary>
    private static void PrintTimeToKill(ItemCatalog catalog, Rimtera.Entities.EnemyArchetype[] enemies)
    {
        var weaponDamage = catalog.All.Where(item => item.Damage > 0 && item.UseKind is ItemUseKind.Melee or ItemUseKind.Ranged or ItemUseKind.Tech)
            .GroupBy(item => item.Tier).ToDictionary(group => group.Key, group =>
            {
                var sorted = group.Select(item => item.Damage).OrderBy(damage => damage).ToArray();
                return sorted[sorted.Length / 2];
            });
        var (rescaleHealth, _) = GameSimulation.HardmodeScale(WorldDifficulty.Classic);
        var regular = enemies.Where(archetype => !archetype.IsBoss && archetype.Kind != Rimtera.Entities.EnemyKind.DungeonGuardian).ToArray();
        bool Late(Rimtera.Entities.EnemyArchetype archetype) => Rimtera.Entities.CreatureStatTable.IsLate(archetype.Kind);
        bool Post(Rimtera.Entities.EnemyArchetype archetype) => !Late(archetype) && Rimtera.Entities.CreatureStatTable.IsPostFracture(archetype.Kind);
        bool Pre(Rimtera.Entities.EnemyArchetype archetype) => !Late(archetype) && !Post(archetype);
        var bands = new (int Tier, string Label, Rimtera.Entities.EnemyArchetype[] Members, bool Fractured)[]
        {
            (1, "pre-Fracture HP 0-60", regular.Where(a => Pre(a) && a.HitPoints < 60f).ToArray(), false),
            (2, "pre-Fracture HP 60-150", regular.Where(a => Pre(a) && a.HitPoints is >= 60f and < 150f).ToArray(), false),
            (3, "pre-Fracture HP 150+", regular.Where(a => Pre(a) && a.HitPoints >= 150f).ToArray(), false),
            (4, "post-Fracture HP <300", regular.Where(a => Post(a) && a.HitPoints < 300f).ToArray(), true),
            (5, "post-Fracture HP 300+", regular.Where(a => Post(a) && a.HitPoints >= 300f).ToArray(), true),
            (6, "late tier", regular.Where(Late).ToArray(), true),
            (7, "late tier", regular.Where(Late).ToArray(), true),
            (1, "gate bosses", enemies.Where(a => a.Tier == Rimtera.Entities.BossTier.Gate).ToArray(), false),
            (3, "easy bosses", enemies.Where(a => a.Tier == Rimtera.Entities.BossTier.Easy).ToArray(), false),
            (5, "hard bosses", enemies.Where(a => a.Tier == Rimtera.Entities.BossTier.Hard).ToArray(), true),
            (6, "final boss", enemies.Where(a => a.Tier == Rimtera.Entities.BossTier.Final).ToArray(), true)
        };
        Console.WriteLine("Time to kill per tier (Classic): weapon median damage vs band median HP (after rescale) and defense -> hits min / median / max");
        foreach (var band in bands)
        {
            if (band.Members.Length == 0 || !weaponDamage.TryGetValue(band.Tier, out var damage))
                continue;
            float Health(Rimtera.Entities.EnemyArchetype archetype) =>
                archetype.HitPoints * (band.Fractured && GameSimulation.HardmodeRescales(archetype) ? rescaleHealth : 1f);
            var hits = band.Members.Select(archetype =>
                    (int)MathF.Ceiling(Health(archetype) / CombatTuning.CreatureDamageTaken(damage, archetype.Defense, 0, false)))
                .OrderBy(count => count).ToArray();
            var health = band.Members.Select(Health).OrderBy(value => value).ToArray();
            var defenses = band.Members.Select(archetype => archetype.Defense).OrderBy(value => value).ToArray();
            Console.WriteLine($"  T{band.Tier} dmg {damage,3} vs {band.Label,-24} ({band.Members.Length,3}) HP {health[health.Length / 2],6:0} def {defenses[defenses.Length / 2],2}" +
                              $" -> hits {hits[0],3} / {hits[hits.Length / 2],3} / {hits[^1],4}");
        }
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
