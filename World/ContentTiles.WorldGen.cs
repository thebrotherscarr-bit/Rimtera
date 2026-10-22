using Rimtera.Systems;

namespace Rimtera.World;

/// <summary>
/// World-generation completion tiles: hardened dune sand and slush, gem-studded rock, speleothems, rubble and loot
/// piles, old-world statues, fallen logs, water plants and coral, graves, boulders and spikes, the slag forge,
/// mud brick, world chests, brood pods, solar lilies, dye blossoms, long moss, burrower nests, the vault altar,
/// crystal shrubs, wild gourds and mushrooms and campfires. Names and numbers are original.
/// </summary>
public static partial class ContentTiles
{
    /// <summary>Gem tiles and the shard each drops, shallow to deep.</summary>
    public static (TileId Tile, ItemId Shard)[] GemTiles => GemTable.All;

    // Held in a nested class so it is initialised before the definition table that reads it.
    private static class GemTable
    {
        public static readonly (TileId Tile, ItemId Shard)[] All =
        [
            (TileId.GemAmethyst, ItemId.AmethystShard), (TileId.GemTopaz, ItemId.TopazShard),
            (TileId.GemSapphire, ItemId.SapphireShard), (TileId.GemEmerald, ItemId.EmeraldShard),
            (TileId.GemRuby, ItemId.RubyShard), (TileId.GemDiamond, ItemId.DiamondShard)
        ];
    }

    public static bool IsGem(TileId tile) => tile is >= TileId.GemAmethyst and <= TileId.GemDiamond;

    public static bool IsSpeleothem(TileId tile) => tile is >= TileId.StalactiteStone and <= TileId.StalagmiteHive;

    public static bool IsStatue(TileId tile) => tile is TileId.StatueHopper or TileId.StatueCrab or TileId.StatueBat;

    public static bool IsDyeBlossom(TileId tile) => tile is TileId.DyeBlossomIndigo or TileId.DyeBlossomCrimson or TileId.DyeBlossomGold;

    /// <summary>Chests placed by world generation (loot tables in GameSimulation.WorldGenContent).</summary>
    public static bool IsWorldChest(TileId tile) => tile is >= TileId.SunkenChest and <= TileId.SlagChest;

    private static void AddWorldGenTiles(List<ContentTileDefinition> list)
    {
        void Block(TileId id, string name, byte hp, float resistance, ItemId? drop, int minTool = 0) =>
            list.Add(new(id, name, true, hp, resistance, 2, null, TileTraits.None, minTool, Value: 1, Drop: drop));
        void Prop(TileId id, string name, byte hp, TileTraits traits, ItemId? drop = null, int value = 0,
            CraftingStationId station = CraftingStationId.None, int minTool = 0) =>
            list.Add(new(id, name, false, hp, 0.6f, 46, null, traits, minTool, station, Furniture: true, Value: value, Drop: drop));

        // Natural blocks.
        list.Add(new(TileId.ScrapSandstone, "Hardened dune sand", true, 34, 1.3f, 10, null, TileTraits.None, Drop: ItemId.DesertGlassBlock));
        list.Add(new(TileId.Slush, "Slush", true, 14, 0.7f, 9, null, TileTraits.None, Drop: ItemId.SnowBlock));
        foreach (var (gem, shard) in GemTiles)
            Block(gem, shard.ToString().Replace("Shard", "") + "-studded rock", 50, 1.7f, shard);
        list.Add(new(TileId.MudBrick, "Mud brick", true, 44, 1.4f, 17, null, TileTraits.None, Drop: ItemId.MudBlock));
        Block(TileId.Boulder, "Boulder", 60, 2f, ItemId.RockBlock);

        // Speleothems: brittle cave formations that break into nothing.
        foreach (var (tile, name) in new[]
                 {
                     (TileId.StalactiteStone, "Stone stalactite"), (TileId.StalagmiteStone, "Stone stalagmite"),
                     (TileId.StalactiteIce, "Ice stalactite"), (TileId.StalagmiteIce, "Ice stalagmite"),
                     (TileId.StalactiteSand, "Dune-sand stalactite"), (TileId.StalagmiteSand, "Dune-sand stalagmite"),
                     (TileId.StalactiteAsh, "Core-crust stalactite"), (TileId.StalagmiteAsh, "Core-crust stalagmite"),
                     (TileId.StalactiteHive, "Comb drip"), (TileId.StalagmiteHive, "Comb mound")
                 })
            Prop(tile, name, 4, TileTraits.Fragile);

        // Piles and debris (loot rolled when broken).
        Prop(TileId.RubblePile, "Rubble pile", 6, TileTraits.None);
        Prop(TileId.LootPile, "Scrap-and-coin pile", 6, TileTraits.None);
        Prop(TileId.ShellPile, "Shell pile", 4, TileTraits.Organic);
        Prop(TileId.BurrowerNest, "Burrower nest", 14, TileTraits.Organic);
        Prop(TileId.FallenLog, "Fallen log", 20, TileTraits.Organic, ItemId.Timber);
        Prop(TileId.GraveMarker, "Weathered grave marker", 30, TileTraits.None);
        Prop(TileId.SpikeStrip, "Spike strip", 30, TileTraits.None);

        // Statues of old-world creatures: wired ones wake a creature on every pulse.
        Prop(TileId.StatueHopper, "Hopper statue", 50, TileTraits.None, ItemId.RockBlock);
        Prop(TileId.StatueCrab, "Crab statue", 50, TileTraits.None, ItemId.RockBlock);
        Prop(TileId.StatueBat, "Bat statue", 50, TileTraits.None, ItemId.RockBlock);

        // Plants.
        const TileTraits plant = TileTraits.Organic | TileTraits.Fragile;
        Prop(TileId.Lilypad, "Lilypad", 1, plant);
        Prop(TileId.Reed, "Marsh reed", 1, plant);
        Prop(TileId.Seaweed, "Seaweed", 1, plant);
        Prop(TileId.CoralFan, "Coral fan", 2, TileTraits.Organic, ItemId.CoralShard);
        Prop(TileId.SolarLily, "Solar lily", 1, TileTraits.Organic | TileTraits.Light);
        Prop(TileId.DyeBlossomIndigo, "Indigo dye blossom", 1, TileTraits.Organic, ItemId.DyePetals);
        Prop(TileId.DyeBlossomCrimson, "Crimson dye blossom", 1, TileTraits.Organic, ItemId.DyePetals);
        Prop(TileId.DyeBlossomGold, "Gold dye blossom", 1, TileTraits.Organic, ItemId.DyePetals);
        Prop(TileId.LongMoss, "Long moss", 1, TileTraits.Organic | TileTraits.Light);
        Prop(TileId.CrystalShrub, "Crystal shrub", 10, TileTraits.Light);
        Prop(TileId.WildGourd, "Wild gourd", 6, TileTraits.Organic, ItemId.Berry);
        Prop(TileId.WildMushroom, "Wild mushroom", 1, TileTraits.Organic, ItemId.GlowSpore);
        Prop(TileId.BroodPod, "Brood pod", 12, TileTraits.Organic);
        Prop(TileId.CanopyBulb, "Canopy bulb", 30, TileTraits.Organic | TileTraits.Light);

        // Structures and their furnishings.
        Prop(TileId.SlagForge, "Reactor forge", 60, TileTraits.Light, ItemId.PowerForgeMk2, 120, CraftingStationId.PowerForgeMk2);
        Prop(TileId.VaultAltar, "Precursor altar", 200, TileTraits.Light, minTool: 85);
        Prop(TileId.Campfire, "Campfire", 20, TileTraits.Light, ItemId.Timber, 4);
        Prop(TileId.SunkenChest, "Sunken chest", 30, TileTraits.Container | TileTraits.Organic, value: 20);
        Prop(TileId.SurfaceCrate, "Supply crate", 24, TileTraits.Container | TileTraits.Organic, value: 10);
        Prop(TileId.ShrineChest, "Shrine chest", 30, TileTraits.Container | TileTraits.Organic, value: 30);
        Prop(TileId.SkyChest, "Sky chest", 30, TileTraits.Container, value: 40);
        Prop(TileId.LanderChest, "Lander strongbox", 40, TileTraits.Container, value: 40);
        Prop(TileId.SlagChest, "Lead-lined reactor chest", 50, TileTraits.Container, value: 60);
    }
}
