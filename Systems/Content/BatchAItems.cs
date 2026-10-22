using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Batch A items (world physics): timber and saplings, platforms, ropes and ladders, the shaping hammer,
/// herbs and seeds, wiring parts, explosives and dungeon loot, plus their recipes. Original names and numbers.
/// </summary>
public static class BatchAItems
{
    public static void RegisterAll(Dictionary<ItemId, ItemDefinition> items)
    {
        void Add(ItemDefinition definition) => items[definition.Id] = definition;
        void Material(ItemId id, string name, int value, int stack = 999) =>
            Add(new ItemDefinition(id, name, ItemUseKind.Material, stack) { IsMaterial = true, Value = value });
        void Place(ItemId id, string name, TileId tile, int value, int stack = 999, bool material = false) =>
            Add(new ItemDefinition(id, name, ItemUseKind.PlaceTile, stack) { PlacedTile = tile, Value = value, IsMaterial = material });
        void Tool(ItemId id, string name, int value, int stack = 1, int cooldown = 12) =>
            Add(new ItemDefinition(id, name, ItemUseKind.Utility, stack) { Tier = 1, Value = value, UseCooldownTicks = cooldown });

        Material(ItemId.Timber, "Timber", 1);
        Place(ItemId.Sapling, "Sapling", TileId.Sapling, 2, 99);
        Material(ItemId.CactusChunk, "Cactus chunk", 1);
        Material(ItemId.DyePetals, "Dye petals", 3);
        Place(ItemId.PlankPlatform, "Plank platform", TileId.PlankPlatform, 1);
        Place(ItemId.MetalPlatform, "Metal platform", TileId.MetalPlatform, 2);
        Place(ItemId.ClimbingRope, "Climbing rope", TileId.Rope, 1);
        Place(ItemId.Ladder, "Ladder", TileId.Ladder, 3, 99);
        Place(ItemId.StandingTorch, "Standing torch", TileId.StandingTorch, 3, 99);
        Place(ItemId.PrecursorDungeonBrick, "Precursor dungeon brick", TileId.DungeonBrickPrecursor, 4, material: true);
        Place(ItemId.LivingWood, "Living wood", TileId.GiantTrunk, 2, material: true);
        Tool(ItemId.ShapingHammer, "Shaping hammer", 40, cooldown: 10);

        foreach (var herb in TilePhysics.AllHerbs)
        {
            var name = herb.Herb.ToString().Replace("Herb", string.Empty);
            Material(herb.Herb, name, 6, 99);
            Place(herb.Seeds, $"{name} seeds", herb.Tile, 2, 99);
        }

        Material(ItemId.WireSpool, "Wire spool", 1);
        Tool(ItemId.WireCutter, "Wire cutter", 30, cooldown: 4);
        Tool(ItemId.Actuator, "Actuator", 4, 999, 6);
        Place(ItemId.Switch, "Switch", TileId.Switch, 4, 99);
        Place(ItemId.Lever, "Lever", TileId.Lever, 4, 99);
        Place(ItemId.PressurePlate, "Pressure plate", TileId.PressurePlate, 5, 99);
        Place(ItemId.Timer1s, "One-second timer", TileId.Timer1s, 8, 99);
        Place(ItemId.Timer3s, "Three-second timer", TileId.Timer3s, 8, 99);
        Place(ItemId.Timer5s, "Five-second timer", TileId.Timer5s, 8, 99);
        Place(ItemId.GateAnd, "AND gate", TileId.GateAnd, 10, 99);
        Place(ItemId.GateOr, "OR gate", TileId.GateOr, 10, 99);
        Place(ItemId.GateNot, "NOT gate", TileId.GateNot, 10, 99);
        Place(ItemId.GateXor, "XOR gate", TileId.GateXor, 10, 99);
        Place(ItemId.DartTrap, "Dart trap", TileId.DartTrap, 20, 99);
        Place(ItemId.ExplosiveTrap, "Explosive trap", TileId.ExplosiveTrap, 25, 99);
        Place(ItemId.TeleporterPad, "Teleporter pad", TileId.TeleporterPad, 60, 99);

        Tool(ItemId.Bomb, "Bomb", 6, 99, 20);
        Tool(ItemId.Dynamite, "Dynamite", 15, 99, 20);
        Tool(ItemId.StickyBomb, "Sticky bomb", 8, 99, 20);

        Add(new ItemDefinition(ItemId.ShrineBlade, "Shrine blade", ItemUseKind.Melee, 1)
        {
            Damage = 24, Knockback = 4.5f, UseCooldownTicks = 15, Tier = 2, Rarity = ContentItems.RarityFor(3), Value = 180,
            Class = DamageClass.Melee
        });
    }

    public static void RegisterRecipes(CraftingSystem crafting)
    {
        var none = (IReadOnlySet<CraftingStationId>?)null;
        IReadOnlySet<CraftingStationId> At(CraftingStationId station) => new HashSet<CraftingStationId> { station };
        void Add(string id, ItemId output, int count, IReadOnlySet<CraftingStationId>? stations, params (ItemId Item, int Count)[] ingredients) =>
            crafting.Register(new RecipeDefinition("phys-" + id, output, count,
                ingredients.Select(entry => IngredientRequirement.Exact(entry.Item, entry.Count)).ToArray(), stations));
        var fab = At(CraftingStationId.Workbench);
        var tinker = At(CraftingStationId.TinkerBench);
        var chem = At(CraftingStationId.ChemLab);

        Add("timber-planks", ItemId.AstroTimberPlanksBlock, 2, none, (ItemId.Timber, 1));
        Add("plank-platform", ItemId.PlankPlatform, 2, none, (ItemId.Timber, 1));
        Add("metal-platform", ItemId.MetalPlatform, 4, fab, (ItemId.FerriteBar, 1));
        Add("climbing-rope", ItemId.ClimbingRope, 10, none, (ItemId.RopeCoil, 1));
        Add("ladder", ItemId.Ladder, 1, none, (ItemId.Timber, 3));
        Add("timber-torch", ItemId.WallTorch, 4, none, (ItemId.Timber, 1), (ItemId.CopperOre, 1));
        Add("standing-torch", ItemId.StandingTorch, 1, none, (ItemId.WallTorch, 1), (ItemId.Timber, 1));
        Add("shaping-hammer", ItemId.ShapingHammer, 1, fab, (ItemId.Timber, 6), (ItemId.CopperBar, 3));
        Add("wire-spool", ItemId.WireSpool, 12, fab, (ItemId.CopperBar, 1));
        Add("wire-cutter", ItemId.WireCutter, 1, fab, (ItemId.CopperBar, 3));
        Add("actuator", ItemId.Actuator, 5, fab, (ItemId.CopperBar, 1), (ItemId.RockBlock, 2));
        Add("switch", ItemId.Switch, 1, fab, (ItemId.RockBlock, 2), (ItemId.WireSpool, 1));
        Add("lever", ItemId.Lever, 1, fab, (ItemId.RockBlock, 3), (ItemId.WireSpool, 1));
        Add("pressure-plate", ItemId.PressurePlate, 1, fab, (ItemId.RockBlock, 2), (ItemId.CopperBar, 1));
        Add("timer-1s", ItemId.Timer1s, 1, fab, (ItemId.CopperBar, 2), (ItemId.WireSpool, 2));
        Add("timer-3s", ItemId.Timer3s, 1, fab, (ItemId.CopperBar, 2), (ItemId.WireSpool, 2));
        Add("timer-5s", ItemId.Timer5s, 1, fab, (ItemId.CopperBar, 2), (ItemId.WireSpool, 2));
        foreach (var gate in new[] { ItemId.GateAnd, ItemId.GateOr, ItemId.GateNot, ItemId.GateXor })
            Add(gate.ToString().ToLowerInvariant(), gate, 1, tinker, (ItemId.CopperBar, 2), (ItemId.WireSpool, 3));
        Add("dart-trap", ItemId.DartTrap, 1, fab, (ItemId.RockBlock, 10), (ItemId.CopperBar, 2));
        Add("explosive-trap", ItemId.ExplosiveTrap, 1, fab, (ItemId.Dynamite, 1), (ItemId.RockBlock, 5));
        Add("teleporter-pad", ItemId.TeleporterPad, 1, tinker, (ItemId.PowerCell, 2), (ItemId.LumeniteBar, 4));
        Add("bomb", ItemId.Bomb, 1, none, (ItemId.ExplosiveCharge, 3), (ItemId.ScrapIronOre, 1));
        Add("dynamite", ItemId.Dynamite, 1, fab, (ItemId.ExplosiveCharge, 6), (ItemId.ClothRoll, 1));
        Add("sticky-bomb", ItemId.StickyBomb, 1, none, (ItemId.Bomb, 1), (ItemId.BioGel, 1));
        Add("dungeon-brick", ItemId.PrecursorDungeonBrick, 1, fab, (ItemId.RockBlock, 2), (ItemId.CopperBar, 1));

        // Herbal remedies at the chem lab.
        Add("sunpetal-stim", ItemId.MedStim, 2, chem, (ItemId.SunpetalHerb, 1), (ItemId.Berry, 1));
        Add("rainreed-stim", ItemId.SpeedStim, 1, chem, (ItemId.RainreedHerb, 1), (ItemId.GlassBlock, 1));
        Add("moonglow-drops", ItemId.NightOwlDrops, 1, chem, (ItemId.MoonglowHerb, 1), (ItemId.GlassBlock, 1));
        Add("frostbell-pill", ItemId.FeatherfallPill, 1, chem, (ItemId.FrostbellHerb, 1), (ItemId.GlassBlock, 1));
        Add("emberroot-stim", ItemId.ArmorStim, 1, chem, (ItemId.EmberrootHerb, 2), (ItemId.GlassBlock, 1));
        Add("sporecap-patch", ItemId.RegenPatch, 1, chem, (ItemId.SporecapHerb, 1), (ItemId.ClothRoll, 1));
        Add("rainreed-capsule", ItemId.WaterBreathCapsule, 1, chem, (ItemId.RainreedHerb, 1), (ItemId.MoonglowHerb, 1));
    }
}
