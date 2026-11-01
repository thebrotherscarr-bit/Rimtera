using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Gear slots a dye can colour (the armour pieces and the accessory row).</summary>
public enum DyeSlot : byte { Head, Body, Legs, Accessory }

/// <summary>
/// Player and combat systems pass (docs/TERRARIA_GAPS.md, plan item 2): the sifter, the dye vat and its dyes, the
/// fortune tonic and luck/aggro accessories, plus one salvage cache, trophy, relic and exclusive core-chip per boss.
/// Boss items use dynamic ids inside the 8200–8399 band (like kill banners), indexed by the boss's place in
/// <see cref="Bosses"/>. All names and numbers are original Rimtera data.
/// </summary>
public static class PlayerSystemsContent
{
    public const int BagBase = 8240, TrophyBase = 8280, RelicBase = 8320, ExclusiveBase = 8360, BossSlots = 40;

    /// <summary>Every boss kind in enum order (stable: new bosses append), capped to the id band.</summary>
    public static IReadOnlyList<EnemyKind> Bosses => _bosses ??= EnemyArchetypes.All.Where(archetype => archetype.IsBoss)
        .Select(archetype => archetype.Kind).OrderBy(kind => (int)kind).Take(BossSlots).ToArray();
    private static EnemyKind[]? _bosses;

    private static int BossIndex(EnemyKind kind)
    {
        var bosses = Bosses;
        for (var i = 0; i < bosses.Count; i++)
            if (bosses[i] == kind)
                return i;
        return -1;
    }

    public static ItemId? BagFor(EnemyKind kind) => BossIndex(kind) is var i and >= 0 ? (ItemId)(BagBase + i) : null;
    public static ItemId? TrophyFor(EnemyKind kind) => BossIndex(kind) is var i and >= 0 ? (ItemId)(TrophyBase + i) : null;
    public static ItemId? RelicFor(EnemyKind kind) => BossIndex(kind) is var i and >= 0 ? (ItemId)(RelicBase + i) : null;
    public static ItemId? ExclusiveFor(EnemyKind kind) => BossIndex(kind) is var i and >= 0 ? (ItemId)(ExclusiveBase + i) : null;

    private static EnemyKind? BossIn(ItemId item, int baseId)
    {
        var index = (int)item - baseId;
        return index >= 0 && index < Bosses.Count ? Bosses[index] : null;
    }

    /// <summary>The boss a salvage cache comes from, or null.</summary>
    public static EnemyKind? BagBoss(ItemId item) => BossIn(item, BagBase);
    public static EnemyKind? TrophyBoss(ItemId item) => BossIn(item, TrophyBase);
    public static EnemyKind? RelicBoss(ItemId item) => BossIn(item, RelicBase);
    public static EnemyKind? ExclusiveBoss(ItemId item) => BossIn(item, ExclusiveBase);

    /// <summary>Dye colours (0xRRGGBB).</summary>
    public static IReadOnlyDictionary<ItemId, int> DyeColors { get; } = new Dictionary<ItemId, int>
    {
        [ItemId.CrimsonDye] = 0xC8283C, [ItemId.AzureDye] = 0x326EE6, [ItemId.VerdantDye] = 0x3CB450,
        [ItemId.AmberDye] = 0xEBAA28, [ItemId.VioletDye] = 0x9646D2, [ItemId.TealDye] = 0x28B4B4,
        [ItemId.ShadowDye] = 0x373746, [ItemId.PrismaticDye] = 0xFF8CE6
    };

    public static bool IsDye(ItemId item) => DyeColors.ContainsKey(item);

    /// <summary>Luck granted by worn gear (−1..+1 scale; summed, then clamped by the simulation).</summary>
    public static float GearLuck(ItemId item) => item switch
    {
        ItemId.LuckyCharm => 0.1f,
        ItemId.FortuneMatrix => 0.15f,
        ItemId.CloverCircuit => 0.25f,
        _ when ExclusiveBoss(item) is not null => 0.05f,
        _ => 0f
    };

    /// <summary>Aggro from worn gear: taunt gear draws hostiles, stealth gear lets them pass you by.</summary>
    public static int GearAggro(ItemId item)
    {
        if (item == ItemId.BeaconHarness)
            return 400;
        if (item == ItemId.ShroudCloak)
            return -400;
        if ((int)item >= 8200 && (int)item < 8400)
            return 0;
        var name = item.ToString();
        if (name.Contains("Bulwark", StringComparison.Ordinal) || name.Contains("Paladin", StringComparison.Ordinal))
            return 150;
        if (name.Contains("Ghost", StringComparison.Ordinal) || name.Contains("Stalker", StringComparison.Ordinal) ||
            name.Contains("Wraith", StringComparison.Ordinal))
            return -150;
        return 0;
    }

    /// <summary>Sediments the sifter accepts, with their depth tier (deeper sediment holds better finds).</summary>
    public static IReadOnlyDictionary<ItemId, int> Sediments { get; } = new Dictionary<ItemId, int>
    {
        [ItemId.SiltBlock] = 1, [ItemId.GravelBlock] = 1, [ItemId.AshBlock] = 3
    };

    /// <summary>Sifting finds: item, weight, min/max count and the lowest sediment tier that holds it.</summary>
    public static IReadOnlyList<(ItemId Item, int Weight, int Min, int Max, int Tier, bool Fractured)> SiftTable { get; } =
    [
        (ItemId.CopperOre, 30, 1, 3, 1, false), (ItemId.ScrapIronOre, 22, 1, 3, 1, false), (ItemId.CobaltOre, 12, 1, 2, 1, false),
        (ItemId.ViridiumOre, 7, 1, 2, 1, false), (ItemId.GlacialOre, 6, 1, 2, 1, false),
        (ItemId.TopazShard, 6, 1, 1, 1, false), (ItemId.AmethystShard, 6, 1, 1, 1, false), (ItemId.SapphireShard, 3, 1, 1, 1, false),
        (ItemId.EmeraldShard, 3, 1, 1, 1, false), (ItemId.RubyShard, 3, 1, 1, 1, false), (ItemId.DiamondShard, 1, 1, 1, 1, false),
        (ItemId.Credits, 14, 5, 25, 1, false),
        (ItemId.UraniumOre, 16, 1, 3, 3, false), (ItemId.PyrociteOre, 16, 1, 3, 3, false), (ItemId.ObsidianShard, 12, 1, 2, 3, false),
        (ItemId.CausticShard, 8, 1, 2, 3, false),
        (ItemId.LumeniteOre, 6, 1, 2, 1, true), (ItemId.VoidsteelOre, 4, 1, 2, 3, true)
    ];

    public static void RegisterAll(Dictionary<ItemId, ItemDefinition> items)
    {
        void Add(ItemDefinition definition) => items[definition.Id] = definition;
        ItemDefinition Accessory(ItemId id, string name, int tier) => new(id, name, ItemUseKind.Equipment, 1)
            { EquipmentSlot = EquipmentSlot.Accessory, Tier = tier, Rarity = tier >= 3 ? ItemRarity.Rare : ItemRarity.Uncommon, Value = 60 + tier * tier * 25 };

        Add(new ItemDefinition(ItemId.Sifter, "Sediment sifter", ItemUseKind.PlaceTile, 99) { PlacedTile = TileId.Sifter, Tier = 1, Value = 80 });
        Add(new ItemDefinition(ItemId.DyeVat, "Dye vat", ItemUseKind.PlaceTile, 99) { PlacedTile = TileId.DyeVat, Tier = 1, Value = 60 });
        Add(new ItemDefinition(ItemId.FortuneTonic, "Fortune tonic", ItemUseKind.Consumable, 30)
            { GrantsStatus = StatusId.Lucky, StatusDurationHours = 8f, Tier = 2, Rarity = ItemRarity.Uncommon, Value = 40 });
        // 0.3.4 (EN-8): Terraria's battle potion (more spawns) and calming potion (fewer); BuffCurve sets their minutes.
        Add(new ItemDefinition(ItemId.BattleStim, "Battle stim", ItemUseKind.Consumable, 30)
            { GrantsStatus = StatusId.Battle, StatusDurationHours = 1f, Tier = 1, Rarity = ItemRarity.Common, Value = 20 });
        Add(new ItemDefinition(ItemId.CalmingStim, "Calming stim", ItemUseKind.Consumable, 30)
            { GrantsStatus = StatusId.Calm, StatusDurationHours = 1f, Tier = 1, Rarity = ItemRarity.Common, Value = 20 });
        Add(Accessory(ItemId.CloverCircuit, "Clover circuit", 3) with { CritBonus = 2 });
        Add(Accessory(ItemId.BeaconHarness, "Beacon harness", 2) with { Defense = 3 });
        Add(Accessory(ItemId.ShroudCloak, "Shroud cloak", 2) with { MoveSpeedMultiplier = 1.04f });
        foreach (var (dye, color) in DyeColors)
        {
            var name = dye.ToString()[..^3];
            Add(new ItemDefinition(dye, $"{name} dye", ItemUseKind.Utility, 99)
                { Tier = 1, Value = 30, UseCooldownTicks = 12, Rarity = dye == ItemId.PrismaticDye ? ItemRarity.Rare : ItemRarity.Common });
        }
        Add(new ItemDefinition(ItemId.DyeSolvent, "Dye solvent", ItemUseKind.Utility, 99) { Tier = 1, Value = 10, UseCooldownTicks = 12 });

        for (var i = 0; i < Bosses.Count; i++)
        {
            var archetype = EnemyArchetypes.Get(Bosses[i]);
            var tier = archetype.Tier switch { BossTier.Gate => 2, BossTier.Easy => 3, BossTier.Hard => 5, BossTier.Final => 7, _ => 4 };
            Add(new ItemDefinition((ItemId)(BagBase + i), $"{archetype.DisplayName} salvage cache", ItemUseKind.Utility, 99)
                { Tier = tier, Rarity = ItemRarity.Epic, Value = 200 * tier, UseCooldownTicks = 12 });
            Add(new ItemDefinition((ItemId)(TrophyBase + i), $"{archetype.DisplayName} trophy", ItemUseKind.PlaceTile, 99)
                { PlacedTile = TileId.BossTrophy, Tier = tier, Rarity = ItemRarity.Rare, Value = 100 * tier });
            Add(new ItemDefinition((ItemId)(RelicBase + i), $"{archetype.DisplayName} relic", ItemUseKind.PlaceTile, 99)
                { PlacedTile = TileId.BossRelic, Tier = tier, Rarity = ItemRarity.Mythic, Value = 300 * tier });
            var chip = Accessory((ItemId)(ExclusiveBase + i), $"{archetype.DisplayName} core-chip", tier) with { Rarity = ItemRarity.Epic };
            Add((i % 5) switch
            {
                0 => chip with { CritBonus = 3 + tier },
                1 => chip with { Defense = 2 + tier },
                2 => chip with { MoveSpeedMultiplier = 1.04f + tier * 0.01f },
                3 => chip with { MaximumLifeBonus = 10f + tier * 6f },
                _ => chip with { ChargeRegenBonus = 1f + tier * 0.5f }
            });
        }
    }

    public static void RegisterRecipes(CraftingSystem crafting)
    {
        void Add(string id, ItemId output, int count, CraftingStationId? station, params (ItemId Item, int Count)[] ingredients) =>
            crafting.Register(new RecipeDefinition("ps-" + id, output, count,
                ingredients.Select(entry => IngredientRequirement.Exact(entry.Item, entry.Count)).ToArray(),
                station is { } required ? new HashSet<CraftingStationId> { required } : null));

        Add("sifter", ItemId.Sifter, 1, CraftingStationId.Workbench, (ItemId.FerriteBar, 4), (ItemId.WoodBlock, 8));
        Add("dye-vat", ItemId.DyeVat, 1, CraftingStationId.Workbench, (ItemId.WoodBlock, 10), (ItemId.CopperBar, 2));
        Add("fortune-tonic", ItemId.FortuneTonic, 1, CraftingStationId.ChemLab, (ItemId.DyePetals, 1), (ItemId.EmeraldShard, 1), (ItemId.Berry, 2));
        Add("battle-stim", ItemId.BattleStim, 1, CraftingStationId.ChemLab, (ItemId.BioGel, 1), (ItemId.EmberrootHerb, 1), (ItemId.GlowSpore, 1));
        Add("calming-stim", ItemId.CalmingStim, 1, CraftingStationId.ChemLab, (ItemId.BioGel, 1), (ItemId.MoonglowHerb, 1), (ItemId.Berry, 2));
        Add("clover-circuit", ItemId.CloverCircuit, 1, CraftingStationId.TinkerBench, (ItemId.LuckyCharm, 1), (ItemId.EmeraldShard, 5));
        Add("beacon-harness", ItemId.BeaconHarness, 1, CraftingStationId.Anvil, (ItemId.FerriteBar, 8), (ItemId.EnergyCrystal, 4));
        Add("shroud-cloak", ItemId.ShroudCloak, 1, CraftingStationId.Loom, (ItemId.ClothRoll, 6), (ItemId.ObsidianShard, 2));
        foreach (var (dye, extra) in new[]
                 {
                     (ItemId.CrimsonDye, ItemId.RubyShard), (ItemId.AzureDye, ItemId.SapphireShard), (ItemId.VerdantDye, ItemId.EmeraldShard),
                     (ItemId.AmberDye, ItemId.TopazShard), (ItemId.VioletDye, ItemId.AmethystShard), (ItemId.TealDye, ItemId.CoralShard),
                     (ItemId.ShadowDye, ItemId.ObsidianShard), (ItemId.PrismaticDye, ItemId.PrismShard)
                 })
            Add("dye-" + dye, dye, 1, CraftingStationId.DyeVat, (ItemId.DyePetals, 2), (extra, 1));
        Add("dye-solvent", ItemId.DyeSolvent, 2, CraftingStationId.DyeVat, (ItemId.DyePetals, 1), (ItemId.SandBlock, 2));
    }
}
