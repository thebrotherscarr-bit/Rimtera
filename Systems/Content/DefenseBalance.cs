using Rimtera.Entities;

namespace Rimtera.Systems;

/// <summary>
/// Defense rules: worn defense subtracts a share of every creature or projectile hit (half on Explorer and
/// Classic, three quarters on Brutal, all of it on Annihilation), never below one point. Tougher creatures hit harder so armor from
/// the matching tier removes roughly a third to a half of their damage instead of all of it.
/// </summary>
public static class DefenseBalance
{
    /// <summary>Most defense a single accessory gives; keeps five accessories from out-armoring a full set.</summary>
    public const int MaximumAccessoryDefense = 6;

    /// <summary>Creatures below this many hit points (early game) keep their authored damage.</summary>
    public const float EarlyGameHitPoints = 150f;

    /// <summary>Share of defense subtracted from each hit.</summary>
    public static float DefenseFactor(WorldDifficulty difficulty) => difficulty switch
    {
        WorldDifficulty.Brutal => 0.75f,
        WorldDifficulty.Annihilation => 1f,
        _ => 0.5f
    };

    /// <summary>Damage after defense: the defense share comes off first, and a hit always does at least 1.</summary>
    public static float ApplyDefense(float amount, int defense, WorldDifficulty difficulty) =>
        defense <= 0 ? amount : Math.Max(1f, amount - defense * DefenseFactor(difficulty));

    /// <summary>
    /// Hit scaling. Creatures scale with toughness (authored hit points): 1 below <see cref="EarlyGameHitPoints"/>,
    /// rising with each doubling of hit points to at most 2.2. Bosses scale by where they sit in progression,
    /// so the armor of the tier that usually meets them still matters. The Dungeon Guardian keeps its lethal hit.
    /// </summary>
    public static float EnemyHitScale(EnemyArchetype archetype)
    {
        if (archetype.Kind == EnemyKind.DungeonGuardian)
            return 1f;
        if (archetype.IsBoss)
            return archetype.Tier switch
            {
                BossTier.Easy => 1.15f,
                BossTier.Hard or BossTier.Event => 1.6f,
                BossTier.Final => 2f,
                _ => 1f
            };
        return archetype.HitPoints <= EarlyGameHitPoints
            ? 1f
            : Math.Clamp(1f + 0.35f * MathF.Log2(archetype.HitPoints / EarlyGameHitPoints), 1f, 2.2f);
    }

    /// <summary>Catalog pass: caps accessory defense at <see cref="MaximumAccessoryDefense"/>.</summary>
    public static void Apply(Dictionary<ItemId, ItemDefinition> items)
    {
        foreach (var (id, item) in items.ToArray())
            if (item.EquipmentSlot is >= EquipmentSlot.Accessory && item.Defense > MaximumAccessoryDefense)
                items[id] = item with { Defense = MaximumAccessoryDefense };
    }
}
