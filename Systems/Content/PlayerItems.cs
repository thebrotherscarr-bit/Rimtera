using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Batch B player items: charge crystals, the Cargo Pod (personal storage), pets and light pets, the
/// hover-bike key, recall and phase items, the memorial marker, and the kill banners (one dynamic item id
/// per hostile creature kind). All names and numbers are original Rimtera data.
/// </summary>
public static class PlayerItems
{
    /// <summary>Kill banners use <c>BannerBase + (int)EnemyKind</c> as their item id.</summary>
    public const int BannerBase = 20000;

    /// <summary>Maximum Charge each charge crystal adds.</summary>
    public const int ChargeCrystalBonus = 20;
    public const int MaximumChargeCrystals = 9;

    public static ItemId BannerFor(EnemyKind kind) => (ItemId)(BannerBase + (int)kind);

    /// <summary>The creature a banner item honours, or null when the item is not a banner.</summary>
    public static EnemyKind? BannerKindOf(ItemId item)
    {
        var raw = (int)item - BannerBase;
        return raw >= 0 && raw <= 1999 && Enum.IsDefined((EnemyKind)raw) ? (EnemyKind)raw : null;
    }

    /// <summary>Creatures that earn banners: every hostile, non-boss kind.</summary>
    public static bool EarnsBanner(EnemyKind kind) =>
        EnemyArchetypes.Contains(kind) && EnemyArchetypes.Get(kind) is { IsBoss: false, Passive: false };

    public static void RegisterAll(Dictionary<ItemId, ItemDefinition> items)
    {
        void Add(ItemDefinition definition) => items[definition.Id] = definition;
        static HashSet<string> Tags(params string[] tags) => new(tags, StringComparer.OrdinalIgnoreCase);

        Add(new ItemDefinition(ItemId.ChargeCrystal, "Charge crystal", ItemUseKind.Consumable, 20)
            { Tier = 2, Rarity = ItemRarity.Uncommon, Value = 150 });
        Add(new ItemDefinition(ItemId.CargoPod, "Cargo pod", ItemUseKind.Utility, 1)
            { Tier = 2, Rarity = ItemRarity.Uncommon, Value = 120, UseCooldownTicks = 20 });
        Add(new ItemDefinition(ItemId.CargoPodStation, "Cargo pod station", ItemUseKind.PlaceTile, 99)
            { PlacedTile = TileId.CargoPodStation, Tier = 1, Value = 60 });
        Add(new ItemDefinition(ItemId.PetDroneChip, "Pocket drone chip", ItemUseKind.Utility, 1)
            { Tier = 2, Rarity = ItemRarity.Uncommon, Value = 90, UseCooldownTicks = 20 });
        Add(new ItemDefinition(ItemId.PetCrabShell, "Pebble-crab shell", ItemUseKind.Utility, 1)
            { Tier = 1, Value = 60, UseCooldownTicks = 20 });
        Add(new ItemDefinition(ItemId.LightPetWisp, "Lumen wisp jar", ItemUseKind.Utility, 1)
            { Tier = 2, Rarity = ItemRarity.Uncommon, Value = 110, UseCooldownTicks = 20 });
        Add(new ItemDefinition(ItemId.LightPetLantern, "Lantern bot", ItemUseKind.Utility, 1)
            { Tier = 3, Rarity = ItemRarity.Rare, Value = 160, UseCooldownTicks = 20 });
        Add(new ItemDefinition(ItemId.HoverBikeKey, "Hover-bike key", ItemUseKind.Utility, 1)
            { Tier = 3, Rarity = ItemRarity.Rare, Value = 400, UseCooldownTicks = 20 });
        Add(new ItemDefinition(ItemId.RecallCapsule, "Recall capsule", ItemUseKind.Utility, 30)
            { Tier = 1, Value = 20, UseCooldownTicks = 30 });
        Add(new ItemDefinition(ItemId.PhaseRod, "Phase rod", ItemUseKind.Utility, 1)
            { Tier = 5, Rarity = ItemRarity.Legendary, Value = 900, UseCooldownTicks = 24 });
        Add(new ItemDefinition(ItemId.MemorialMarker, "Memorial marker", ItemUseKind.PlaceTile, 99)
            { PlacedTile = TileId.Tombstone, Tier = 1, Value = 5 });

        // Existing gear now drives real movement: jet boots lift, the featherfall pill and water-breath
        // capsule grant their own statuses.
        if (items.TryGetValue(ItemId.JetBoots, out var jet))
            Add(jet with { ImmunityTags = Tags("rocket-boots") });
        if (items.TryGetValue(ItemId.FeatherfallPill, out var pill))
            Add(pill with { GrantsStatus = StatusId.Featherfall, StatusDurationHours = 4f });
        if (items.TryGetValue(ItemId.WaterBreathCapsule, out var capsule))
            Add(capsule with { GrantsStatus = StatusId.Gills, StatusDurationHours = 4f });

        foreach (var kind in Enum.GetValues<EnemyKind>().Where(EarnsBanner))
            Add(new ItemDefinition(BannerFor(kind), $"{EnemyArchetypes.Get(kind).DisplayName} banner", ItemUseKind.Utility, 99)
                { Tier = 1, Value = 50, UseCooldownTicks = 12 });
    }

    public static void RegisterRecipes(CraftingSystem crafting)
    {
        void Add(string id, ItemId output, int count, CraftingStationId? station, params (ItemId Item, int Count)[] ingredients) =>
            crafting.Register(new RecipeDefinition("pb-" + id, output, count,
                ingredients.Select(entry => IngredientRequirement.Exact(entry.Item, entry.Count)).ToArray(),
                station is { } required ? new HashSet<CraftingStationId> { required } : null));

        Add("charge-crystal", ItemId.ChargeCrystal, 1, CraftingStationId.Workbench, (ItemId.EnergyCrystal, 6), (ItemId.PowerCell, 1));
        Add("cargo-pod", ItemId.CargoPod, 1, CraftingStationId.Anvil, (ItemId.CopperBar, 8), (ItemId.CircuitBoard, 2));
        Add("cargo-pod-station", ItemId.CargoPodStation, 1, CraftingStationId.Workbench, (ItemId.FerriteBar, 10), (ItemId.WoodBlock, 6));
        Add("pet-drone", ItemId.PetDroneChip, 1, CraftingStationId.Workbench, (ItemId.DroneSpareParts, 6), (ItemId.CircuitBoard, 1));
        Add("pet-crab", ItemId.PetCrabShell, 1, null, (ItemId.CoralShard, 8));
        Add("light-wisp", ItemId.LightPetWisp, 1, CraftingStationId.Workbench, (ItemId.GlowSpore, 10), (ItemId.EnergyCrystal, 2));
        Add("light-lantern", ItemId.LightPetLantern, 1, CraftingStationId.Anvil, (ItemId.FerriteBar, 6), (ItemId.GlowSticks, 5));
        Add("hover-bike", ItemId.HoverBikeKey, 1, CraftingStationId.Anvil, (ItemId.CobaltBar, 15), (ItemId.PowerCell, 6), (ItemId.CircuitBoard, 4));
        Add("recall", ItemId.RecallCapsule, 2, null, (ItemId.GlowSpore, 2), (ItemId.Berry, 1));
        Add("phase-rod", ItemId.PhaseRod, 1, CraftingStationId.Anvil, (ItemId.PrismShard, 20), (ItemId.LumeniteBar, 8));
        Add("memorial", ItemId.MemorialMarker, 1, CraftingStationId.Anvil, (ItemId.RockBlock, 10), (ItemId.CopperBar, 1));
    }
}
