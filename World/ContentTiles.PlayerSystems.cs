using Rimtera.Systems;

namespace Rimtera.World;

/// <summary>
/// Player and combat systems pass: the sediment sifter and the dye vat (stations), and the boss trophy and relic
/// decor tiles. Trophies and relics have no generic drop: the simulation remembers which boss item was placed
/// in each cell and returns that item when the cell is broken (GameSimulation.PlayerSystems).
/// </summary>
public static partial class ContentTiles
{
    private static void AddPlayerSystemsTiles(List<ContentTileDefinition> list)
    {
        list.Add(new(TileId.Sifter, "Sediment sifter", false, 36, 1.2f, 40, ItemId.Sifter, TileTraits.None,
            Furniture: true, Value: 80, Tier: 1));
        list.Add(new(TileId.DyeVat, "Dye vat", false, 36, 1.2f, 41, ItemId.DyeVat, TileTraits.None,
            Station: CraftingStationId.DyeVat, Furniture: true, Value: 60, Tier: 1));
        list.Add(new(TileId.BossTrophy, "Boss trophy", false, 20, 0.8f, 42, null, TileTraits.None, Furniture: true, Value: 100));
        list.Add(new(TileId.BossRelic, "Boss relic", false, 30, 1f, 43, null, TileTraits.None, Furniture: true, Value: 300));
    }
}
