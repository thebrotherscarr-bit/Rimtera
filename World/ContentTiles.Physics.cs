using Rimtera.Systems;

namespace Rimtera.World;

/// <summary>Batch A tile definitions: platforms, climbables, plants, wiring devices, dungeon and loot props.</summary>
public static partial class ContentTiles
{
    private static void AddPhysicsTiles(List<ContentTileDefinition> list)
    {
        void Tile(TileId id, string name, bool solid, byte hp, float resistance, ushort group, ItemId? item, TileTraits traits,
            int value = 1, int minTool = 0, bool furniture = false, ItemId? drop = null, int tier = 0) =>
            list.Add(new(id, name, solid, hp, resistance, group, item, traits, minTool, Furniture: furniture, Value: value, Tier: tier, Drop: drop));

        // Platforms and climbables (never solid in the catalog; movement treats them specially).
        Tile(TileId.PlankPlatform, "Plank platform", false, 10, 0.5f, 37, ItemId.PlankPlatform, TileTraits.Platform | TileTraits.Organic);
        Tile(TileId.MetalPlatform, "Metal platform", false, 16, 0.8f, 37, ItemId.MetalPlatform, TileTraits.Platform, 2);
        Tile(TileId.Rope, "Rope", false, 4, 0.3f, 38, ItemId.ClimbingRope, TileTraits.Climbable | TileTraits.Organic);
        Tile(TileId.Ladder, "Ladder", false, 8, 0.4f, 38, ItemId.Ladder, TileTraits.Climbable | TileTraits.Organic);
        Tile(TileId.Vine, "Vine", false, 2, 0.2f, 38, null, TileTraits.Climbable | TileTraits.Plant | TileTraits.Organic | TileTraits.Fragile);

        // Trees and plants.
        const TileTraits plant = TileTraits.Plant | TileTraits.Organic;
        Tile(TileId.TreeTrunk, "Tree trunk", false, 30, 0.8f, 39, null, plant, drop: ItemId.Timber);
        Tile(TileId.TreeCanopy, "Tree canopy", false, 6, 0.3f, 39, null, plant);
        Tile(TileId.PineTrunk, "Frost pine trunk", false, 30, 0.8f, 39, null, plant, drop: ItemId.Timber);
        Tile(TileId.PineCanopy, "Frost pine needles", false, 6, 0.3f, 39, null, plant);
        Tile(TileId.PalmTrunk, "Rust palm trunk", false, 30, 0.8f, 39, null, plant, drop: ItemId.Timber);
        Tile(TileId.PalmCanopy, "Rust palm fronds", false, 6, 0.3f, 39, null, plant);
        Tile(TileId.Sapling, "Sapling", false, 2, 0.2f, 39, ItemId.Sapling, plant);
        Tile(TileId.Cactus, "Spine cactus", false, 14, 0.5f, 39, null, plant, drop: ItemId.CactusChunk);
        Tile(TileId.HerbSunpetal, "Sunpetal", false, 1, 0.1f, 39, null, plant);
        Tile(TileId.HerbMoonglow, "Moonglow", false, 1, 0.1f, 39, null, plant | TileTraits.Light);
        Tile(TileId.HerbRainreed, "Rainreed", false, 1, 0.1f, 39, null, plant);
        Tile(TileId.HerbFrostbell, "Frostbell", false, 1, 0.1f, 39, null, plant);
        Tile(TileId.HerbEmberroot, "Emberroot", false, 1, 0.1f, 39, null, plant | TileTraits.Light);
        Tile(TileId.HerbSporecap, "Sporecap", false, 1, 0.1f, 39, null, plant);
        Tile(TileId.GiantTrunk, "Living wood", true, 40, 1.1f, 4, ItemId.LivingWood, TileTraits.Organic);
        Tile(TileId.GiantLeaf, "Living leaves", true, 8, 0.4f, 1, null, TileTraits.Organic | TileTraits.Fragile);

        // Wiring devices.
        Tile(TileId.Switch, "Switch", false, 10, 0.6f, 41, ItemId.Switch, TileTraits.Wired, 4, furniture: true);
        Tile(TileId.Lever, "Lever", false, 10, 0.6f, 41, ItemId.Lever, TileTraits.Wired, 4, furniture: true);
        Tile(TileId.PressurePlate, "Pressure plate", false, 10, 0.6f, 41, ItemId.PressurePlate, TileTraits.Wired, 5, furniture: true);
        Tile(TileId.Timer1s, "One-second timer", false, 12, 0.6f, 41, ItemId.Timer1s, TileTraits.Wired, 8, furniture: true);
        Tile(TileId.Timer3s, "Three-second timer", false, 12, 0.6f, 41, ItemId.Timer3s, TileTraits.Wired, 8, furniture: true);
        Tile(TileId.Timer5s, "Five-second timer", false, 12, 0.6f, 41, ItemId.Timer5s, TileTraits.Wired, 8, furniture: true);
        Tile(TileId.GateAnd, "AND gate", false, 12, 0.6f, 41, ItemId.GateAnd, TileTraits.Wired, 10, furniture: true);
        Tile(TileId.GateOr, "OR gate", false, 12, 0.6f, 41, ItemId.GateOr, TileTraits.Wired, 10, furniture: true);
        Tile(TileId.GateNot, "NOT gate", false, 12, 0.6f, 41, ItemId.GateNot, TileTraits.Wired, 10, furniture: true);
        Tile(TileId.GateXor, "XOR gate", false, 12, 0.6f, 41, ItemId.GateXor, TileTraits.Wired, 10, furniture: true);
        Tile(TileId.DartTrap, "Dart trap", true, 50, 1.8f, 17, ItemId.DartTrap, TileTraits.Wired, 20);
        Tile(TileId.ExplosiveTrap, "Explosive trap", true, 30, 1.2f, 17, ItemId.ExplosiveTrap, TileTraits.Wired, 25);
        Tile(TileId.TeleporterPad, "Teleporter pad", false, 30, 1.2f, 41, ItemId.TeleporterPad, TileTraits.Wired, 60, furniture: true, tier: 2);

        // Precursor Dungeon, cabins and world loot.
        Tile(TileId.DungeonBrickPrecursor, "Precursor dungeon brick", true, 120, 3f, 17, ItemId.PrecursorDungeonBrick, TileTraits.None,
            4, minTool: 70, tier: 4);
        Tile(TileId.DungeonChest, "Precursor strongbox", false, 120, 2f, 42, null, TileTraits.Container | TileTraits.Locked, 120, furniture: true);
        Tile(TileId.CabinChest, "Cabin chest", false, 30, 1f, 42, null, TileTraits.Container | TileTraits.Organic, 20, furniture: true);
        foreach (var (pot, name) in new[]
                 {
                     (TileId.PotSurface, "Clay pot"), (TileId.PotFrozen, "Frost-glazed pot"), (TileId.PotJunk, "Rusted canister"),
                     (TileId.PotCavern, "Cavern urn"), (TileId.PotMolten, "Shielded isotope canister"), (TileId.PotDungeon, "Precursor jar"),
                     (TileId.PotCanopy, "Woven basket")
                 })
            Tile(pot, name, false, 2, 0.2f, 43, null, TileTraits.Fragile, 0, furniture: true);
        Tile(TileId.WeaponShrine, "Weapon shrine", false, 60, 1.5f, 44, null, TileTraits.Light, 0, furniture: true, drop: ItemId.ShrineBlade);
        Tile(TileId.StandingTorch, "Standing torch", false, 8, 0.3f, 45, ItemId.StandingTorch, TileTraits.Light, 2, furniture: true);
    }
}
