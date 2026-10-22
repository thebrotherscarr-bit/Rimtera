using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Hardmode progression items (docs/HARDMODE_DIFF.md): the Fission Tyrant's core breaker and the igniter that wakes it,
/// the three post-Fracture essences, and the Void Cantor's hymnal, plus their recipes. Names and numbers are original.
/// </summary>
internal static class HardmodeContent
{
    /// <summary>Tool power of the core breaker. Altars need the altar-breaker capability, not power (GameSimulation.CanSmashAltars).</summary>
    public const int CoreBreakerPower = 75;

    public static void RegisterItems(Dictionary<ItemId, ItemDefinition> items)
    {
        void Add(ItemDefinition definition) => items[definition.Id] = definition;
        Add(new ItemDefinition(ItemId.CoreBreaker, "Core breaker", ItemUseKind.Pickaxe, 1)
        {
            Tier = 4, Rarity = ContentItems.RarityFor(4), ToolPower = CoreBreakerPower, UseCooldownTicks = 14, Damage = 20, Value = 420
        });
        Add(new ItemDefinition(ItemId.CoreIgniter, "Core igniter", ItemUseKind.Consumable, 20)
            { SummonsBoss = EnemyKind.FissionTyrant, Tier = 3, Rarity = ContentItems.RarityFor(3), Value = 130 });
        Add(new ItemDefinition(ItemId.VoidHymnal, "Void hymnal", ItemUseKind.Consumable, 20)
            { SummonsBoss = EnemyKind.VoidCantor, Tier = 6, Rarity = ContentItems.RarityFor(6), Value = 260 });
        foreach (var (id, name) in new[]
                 {
                     (ItemId.PrismEssence, "Prism essence"), (ItemId.BlightEssence, "Blight essence"), (ItemId.FlightEssence, "Flight essence")
                 })
            Add(new ItemDefinition(id, name, ItemUseKind.Material, 999)
                { IsMaterial = true, Tier = 4, Rarity = ContentItems.RarityFor(4), Value = 30 });
    }

    public static void RegisterRecipes(Action<string, ItemId, int, CraftingStationId[], (ItemId Item, int Count)[]> add)
    {
        add("hm-core-igniter", ItemId.CoreIgniter, 1, [CraftingStationId.PowerForgeMk2],
            [(ItemId.UraniumRod, 8), (ItemId.CausticShard, 6), (ItemId.HeartFragment, 1)]);
        add("hm-void-hymnal", ItemId.VoidHymnal, 1, [CraftingStationId.VoidForge],
            [(ItemId.VoidsteelBar, 6), (ItemId.BlightEssence, 8), (ItemId.PrismEssence, 8)]);
    }
}
