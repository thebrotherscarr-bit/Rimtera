using Rimtera.Systems;

namespace Rimtera.World;

/// <summary>Behaviour flags for content tiles (housing, interaction, lighting, tools).</summary>
[Flags]
public enum TileTraits : ushort
{
    None = 0,
    /// <summary>A closed door (solid); interacting swaps it to its open counterpart.</summary>
    Door = 1 << 0,
    /// <summary>An open door (non-solid).</summary>
    OpenDoor = 1 << 1,
    /// <summary>Emits light (housing needs one).</summary>
    Light = 1 << 2,
    /// <summary>Chair-like furniture (housing needs one).</summary>
    Seat = 1 << 3,
    /// <summary>Table-like furniture (housing needs one).</summary>
    Table = 1 << 4,
    Bed = 1 << 5,
    /// <summary>Holds a container inventory.</summary>
    Container = 1 << 6,
    /// <summary>Wood, fungus, hive and coral: cutters fell these quickly.</summary>
    Organic = 1 << 7,
    /// <summary>A container that needs the vault key to open.</summary>
    Locked = 1 << 8,
    /// <summary>Crystal Spires ground (spreads after the Fracture, purifies blight).</summary>
    Crystal = 1 << 9,
    /// <summary>Moves actors standing on it sideways.</summary>
    Conveyor = 1 << 10,
    /// <summary>Breaks into nothing (silk, thin ice).</summary>
    Fragile = 1 << 11,
    // ---- Batch A (append-only) ----
    /// <summary>One-way platform: stood on from above, passed from below and the sides.</summary>
    Platform = 1 << 12,
    /// <summary>Ropes, ladders and vines: actors climb them with up/down.</summary>
    Climbable = 1 << 13,
    /// <summary>A wiring device (switch, lever, plate, timer, gate, trap, teleporter).</summary>
    Wired = 1 << 14,
    /// <summary>A growing plant (trees, saplings, herbs, vines, cacti).</summary>
    Plant = 1 << 15
}

/// <summary>One content tile: the grid definition plus its item, value and traits.</summary>
public sealed record ContentTileDefinition(
    TileId Id,
    string Name,
    bool Solid,
    byte HitPoints,
    float Resistance,
    ushort FrameGroup,
    ItemId? ItemId,
    TileTraits Traits = TileTraits.None,
    int MinimumToolPower = 0,
    CraftingStationId Station = CraftingStationId.None,
    bool Furniture = false,
    int Value = 1,
    int Tier = 0,
    /// <summary>Item dropped when broken if it differs from <see cref="ItemId"/> (grasses, ores).</summary>
    ItemId? Drop = null);

/// <summary>
/// Content-expansion tiles: natural blocks, crafted blocks, stations, doors and furniture, biome
/// structures; plus the crafted wall items. All names and numbers are original.
/// </summary>
public static partial class ContentTiles
{
    private static readonly ContentTileDefinition[] _definitions = Build();
    private static readonly Dictionary<TileId, ContentTileDefinition> _byId = _definitions.ToDictionary(definition => definition.Id);

    public static IReadOnlyList<ContentTileDefinition> Definitions => _definitions;

    /// <summary>Drill power the precursor temple's brick and glyph need (WT-1, D-35): Terraria's 210 on the gear curve.</summary>
    public static int PrecursorBrickToolPower => GearCurve.DrillPowerFromTerraria(210);

    /// <summary>What a drill too weak for the temple brick says (WT-1).</summary>
    public const string PrecursorSealMessage = "The precursor brick needs a stronger drill. Its makers sealed this temple against anything weaker.";

    /// <summary>Crafted wall items and the player wall id each places.</summary>
    public static IReadOnlyList<(ItemId Item, ushort Wall, string Name)> WallItems { get; } =
    [
        (Systems.ItemId.PlankWall, WallIds.Plank, "Plank wall"),
        (Systems.ItemId.PlatingGreyWall, WallIds.PlatingGrey, "Grey plating wall"),
        (Systems.ItemId.PlatingBlueWall, WallIds.PlatingBlue, "Blue plating wall"),
        (Systems.ItemId.GlassWall, WallIds.Glass, "Glass wall"),
        (Systems.ItemId.FerriteBrickWall, WallIds.BrickFerrite, "Ferrite brick wall"),
        (Systems.ItemId.AlabasterBrickWall, WallIds.BrickAlabaster, "Alabaster brick wall"),
        (Systems.ItemId.LatticeWall, WallIds.Lattice, "Lattice wall"),
        (Systems.ItemId.CircuitWall, WallIds.Circuit, "Circuit wall"),
        (Systems.ItemId.PearlWall, WallIds.PearlBuilt, "Pearl wall"),
        (Systems.ItemId.HiveWall, WallIds.HiveBuilt, "Hive wall"),
        (Systems.ItemId.CoralWall, WallIds.CoralBuilt, "Coral wall"),
        (Systems.ItemId.BasaltWall, WallIds.BasaltBuilt, "Basalt wall"),
        (Systems.ItemId.MudWall, WallIds.MudBuilt, "Mud wall"),
        (Systems.ItemId.PanelWall, WallIds.Panel, "Panel wall"),
        (Systems.ItemId.WindowWall, WallIds.Window, "Window wall"),
        (Systems.ItemId.BlastWall, WallIds.Blast, "Blast wall"),
        (Systems.ItemId.LeadWall, WallIds.Lead, "Lead-lined wall")
    ];

    public static bool TryGet(TileId id, out ContentTileDefinition definition) => _byId.TryGetValue(id, out definition!);

    public static TileTraits TraitsOf(TileId id) => _byId.TryGetValue(id, out var definition) ? definition.Traits : TileTraits.None;

    public static bool Has(TileId id, TileTraits trait) => (TraitsOf(id) & trait) != 0;

    /// <summary>Item a broken content tile drops (null for legacy tiles, which keep their own table).</summary>
    public static ItemId? GetDrop(TileId id) =>
        _byId.TryGetValue(id, out var definition) ? definition.Drop ?? definition.ItemId : null;

    /// <summary>The wall item that refunds a player wall id when demolished.</summary>
    public static ItemId? WallItemFor(ushort wallId)
    {
        foreach (var entry in WallItems)
            if (entry.Wall == wallId)
                return entry.Item;
        return null;
    }

    /// <summary>Closed/open counterpart of a door tile.</summary>
    public static TileId? ToggledDoor(TileId door) => door switch
    {
        TileId.PlankDoor => TileId.PlankDoorOpen,
        TileId.PlankDoorOpen => TileId.PlankDoor,
        TileId.HullDoor => TileId.HullDoorOpen,
        TileId.HullDoorOpen => TileId.HullDoor,
        TileId.Trapdoor => TileId.TrapdoorOpen,
        TileId.TrapdoorOpen => TileId.Trapdoor,
        TileId.VentDoor => TileId.VentDoorOpen,
        TileId.VentDoorOpen => TileId.VentDoor,
        _ => null
    };

    /// <summary>Adds every content tile to a catalog.</summary>
    public static void RegisterAll(TileCatalog catalog)
    {
        foreach (var definition in _definitions)
            catalog.Register(new TileDefinition(definition.Id, definition.Name, definition.Solid, definition.Solid,
                definition.HitPoints, definition.Resistance, definition.FrameGroup, definition.Station, definition.MinimumToolPower));
    }

    private static ContentTileDefinition[] Build()
    {
        var list = new List<ContentTileDefinition>();
        void Natural(TileId id, string name, byte hp, float resistance, ushort group, ItemId? item, int minTool = 0,
            TileTraits traits = TileTraits.None, ItemId? drop = null, int tier = 0) =>
            list.Add(new(id, name, true, hp, resistance, group, item, traits, minTool, Value: 1, Tier: tier, Drop: drop));
        void Crafted(TileId id, string name, byte hp, float resistance, ushort group, ItemId item, int tier = 0,
            TileTraits traits = TileTraits.None) =>
            list.Add(new(id, name, true, hp, resistance, group, item, traits, Value: 2 + tier * 2, Tier: tier));
        void Prop(TileId id, string name, ItemId? item, TileTraits traits, int value, byte hp = 24,
            CraftingStationId station = CraftingStationId.None, int tier = 0, bool solid = false, ItemId? drop = null) =>
            list.Add(new(id, name, solid, hp, 1f, (ushort)(40 + list.Count % 40), item, traits, Station: station,
                Furniture: true, Value: value, Tier: tier, Drop: drop));

        // ---- Natural blocks ----
        Natural(TileId.Mud, "Mud", 18, 0.9f, 1, ItemId.MudBlock);
        Natural(TileId.Clay, "Clay", 22, 1f, 1, ItemId.ClayBlock);
        Natural(TileId.Gravel, "Gravel", 20, 0.9f, 3, ItemId.GravelBlock);
        Natural(TileId.Silt, "Silt", 14, 0.7f, 3, ItemId.SiltBlock);
        Natural(TileId.SaltCrust, "Salt crust", 20, 0.9f, 3, ItemId.SaltCrustBlock);
        Natural(TileId.AcidSand, "Acid sand", 16, 0.8f, 3, ItemId.AcidSandBlock);
        Natural(TileId.ShellSand, "Shell sand", 16, 0.8f, 3, ItemId.ShellSandBlock);
        Natural(TileId.CoralReef, "Coral reef", 30, 1.2f, 2, ItemId.CoralReefBlock, traits: TileTraits.Organic);
        Natural(TileId.Slate, "Slate", 46, 1.6f, 2, ItemId.SlateBlock);
        Natural(TileId.Alabaster, "Alabaster", 50, 1.7f, 2, ItemId.AlabasterBlock);
        Natural(TileId.BasaltColumn, "Basalt column", 56, 1.9f, 2, ItemId.BasaltColumnBlock);
        Natural(TileId.HiveComb, "Hive comb", 26, 1f, 2, ItemId.HiveCombBlock, traits: TileTraits.Organic);
        Natural(TileId.Nectar, "Nectar", 12, 0.6f, 3, ItemId.NectarBlock, traits: TileTraits.Organic);
        Natural(TileId.PearlStone, "Pearl stone", 52, 1.7f, 2, ItemId.PearlStoneBlock, traits: TileTraits.Crystal);
        Natural(TileId.PrismaticGrass, "Prismatic grass", 22, 1.1f, 1, null, traits: TileTraits.Crystal, drop: ItemId.SoilBlock);
        Natural(TileId.GlowshroomGrass, "Glowshroom grass", 22, 1.1f, 1, null, traits: TileTraits.Organic, drop: ItemId.MudBlock);
        Natural(TileId.ThinIce, "Thin ice", 6, 0.4f, 9, ItemId.ThinIceBlock, traits: TileTraits.Fragile);
        Natural(TileId.Meteorite, "Meteorite", 60, 2.2f, 2, null, minTool: 50, drop: ItemId.MeteoriteOre, tier: 3);
        list.Add(new(TileId.MiteSilk, "Mite silk", false, 4, 0.3f, 33, null, TileTraits.Fragile | TileTraits.Organic, Drop: ItemId.SilkThread));
        foreach (var (moss, name) in new[] { (TileId.MossBlue, "Blue moss"), (TileId.MossGold, "Gold moss"),
                     (TileId.MossRed, "Red moss"), (TileId.MossViolet, "Violet moss") })
            Natural(moss, name, 42, 1.5f, 2, null, traits: TileTraits.Organic, drop: ItemId.MossBlock);
        // Each hardmode ore needs the previous tier's drill (G4): lumenite a viridium drill, voidsteel a lumenite drill.
        Natural(TileId.LumeniteOre, "Lumenite ore", 70, 2.5f, 2, null, minTool: 85, drop: ItemId.LumeniteOre, tier: 5);
        Natural(TileId.VoidsteelOre, "Voidsteel ore", 80, 2.8f, 2, null, minTool: 100, drop: ItemId.VoidsteelOre, tier: 6);
        Natural(TileId.ShellBeach, "Shell beach", 16, 0.8f, 3, ItemId.ShellBeachBlock);
        Natural(TileId.MyceliumStone, "Mycelium stone", 44, 1.5f, 2, ItemId.MyceliumStoneBlock, traits: TileTraits.Organic);
        Natural(TileId.SporeBlock, "Spore block", 24, 1f, 1, ItemId.SporeBlockBlock, traits: TileTraits.Organic);
        Natural(TileId.MushroomCap, "Mushroom cap", 20, 0.8f, 1, ItemId.MushroomCapBlock, traits: TileTraits.Organic);
        Natural(TileId.CrystalSpire, "Crystal spire", 58, 1.9f, 2, ItemId.CrystalSpireBlock, traits: TileTraits.Crystal);
        Natural(TileId.PearlSand, "Pearl sand", 16, 0.8f, 3, ItemId.PearlSandBlock, traits: TileTraits.Crystal);
        Natural(TileId.CanopyGrass, "Canopy grass", 22, 1.1f, 1, null, traits: TileTraits.Organic, drop: ItemId.MudBlock);
        Natural(TileId.GeodeCyan, "Cyan geode", 40, 1.6f, 2, ItemId.GeodeCyanBlock, minTool: 35, drop: ItemId.SapphireShard);
        Natural(TileId.GeodeMagenta, "Magenta geode", 40, 1.6f, 2, ItemId.GeodeMagentaBlock, minTool: 35, drop: ItemId.AmethystShard);

        // ---- Crafted blocks ----
        Crafted(TileId.FerriteBrick, "Ferrite brick", 60, 2f, 17, ItemId.FerriteBrickBlock, 1);
        Crafted(TileId.CobaltBrick, "Cobalt brick", 64, 2.1f, 17, ItemId.CobaltBrickBlock, 2);
        Crafted(TileId.ViridiumBrick, "Viridium brick", 70, 2.2f, 17, ItemId.ViridiumBrickBlock, 4);
        Crafted(TileId.LumeniteBrick, "Lumenite brick", 74, 2.3f, 17, ItemId.LumeniteBrickBlock, 5);
        Crafted(TileId.VoidsteelBrick, "Voidsteel brick", 80, 2.4f, 17, ItemId.VoidsteelBrickBlock, 6);
        Crafted(TileId.AstralBrick, "Astral brick", 90, 2.6f, 17, ItemId.AstralBrickBlock, 7);
        Crafted(TileId.SlateBrick, "Slate brick", 56, 1.9f, 17, ItemId.SlateBrickBlock);
        Crafted(TileId.AlabasterBrick, "Alabaster brick", 58, 1.9f, 17, ItemId.AlabasterBrickBlock);
        Crafted(TileId.BasaltBrick, "Basalt brick", 60, 2f, 17, ItemId.BasaltBrickBlock);
        Crafted(TileId.PearlBrick, "Pearl brick", 62, 2f, 17, ItemId.PearlBrickBlock, 5);
        Crafted(TileId.HiveBrick, "Hive brick", 40, 1.3f, 17, ItemId.HiveBrickBlock, 0, TileTraits.Organic);
        Crafted(TileId.CoralBrick, "Coral brick", 44, 1.4f, 17, ItemId.CoralBrickBlock);
        Crafted(TileId.Glass, "Glass", 16, 0.6f, 34, ItemId.GlassBlock);
        Crafted(TileId.ReinforcedGlass, "Reinforced glass", 50, 1.8f, 34, ItemId.ReinforcedGlassBlock, 2);
        Crafted(TileId.HullPlatingGrey, "Grey hull plating", 52, 1.8f, 35, ItemId.HullPlatingGreyBlock, 1);
        Crafted(TileId.HullPlatingBlue, "Blue hull plating", 52, 1.8f, 35, ItemId.HullPlatingBlueBlock, 1);
        Crafted(TileId.HullPlatingGreen, "Green hull plating", 52, 1.8f, 35, ItemId.HullPlatingGreenBlock, 1);
        Crafted(TileId.HullPlatingRed, "Red hull plating", 52, 1.8f, 35, ItemId.HullPlatingRedBlock, 1);
        Crafted(TileId.HullPlatingHazard, "Hazard-striped plating", 52, 1.8f, 35, ItemId.HullPlatingHazardBlock, 1);
        Crafted(TileId.AstroTimberPlanks, "Astro-timber planks", 28, 0.7f, 4, ItemId.AstroTimberPlanksBlock, 0, TileTraits.Organic);
        Crafted(TileId.CharredPlanks, "Charred planks", 26, 0.7f, 4, ItemId.CharredPlanksBlock, 0, TileTraits.Organic);
        Crafted(TileId.CrystalBlock, "Crystal block", 40, 1.5f, 34, ItemId.CrystalBlockBlock, 2);
        Crafted(TileId.CircuitPanel, "Circuit panel", 44, 1.6f, 35, ItemId.CircuitPanelBlock, 2);
        Crafted(TileId.Conveyor, "Conveyor", 40, 1.4f, 36, ItemId.ConveyorBlock, 2, TileTraits.Conveyor);
        Crafted(TileId.Grate, "Grate", 40, 1.4f, 36, ItemId.GrateBlock, 1);
        Crafted(TileId.Plate, "Steel plate", 54, 1.9f, 35, ItemId.PlateBlock, 1);
        Crafted(TileId.Vent, "Vent block", 40, 1.4f, 36, ItemId.VentBlock, 1);
        // WT-1 (D-35): the precursor temple is sealed like Terraria's Lihzahrd brick (pickaxe power 210). The curve maps 210 to
        // 122, above every drill before the Vault Colossus (the void auger, 120) and below the astral drills (128).
        // The bricks also stay explosion-proof (GameSimulation.IsExplosionProof).
        list.Add(new(TileId.PrecursorBrick, "Precursor brick", true, 120, 3f, 17, ItemId.PrecursorBrickBlock,
            MinimumToolPower: PrecursorBrickToolPower, Value: 12, Tier: 5));
        list.Add(new(TileId.PrecursorGlyph, "Precursor glyph", true, 120, 3f, 17, ItemId.PrecursorGlyphBlock,
            MinimumToolPower: PrecursorBrickToolPower, Value: 12, Tier: 5));

        // ---- Stations ----
        void Station(TileId id, string name, CraftingStationId station, int tier) =>
            Prop(id, name, Enum.Parse<ItemId>(id.ToString()), TileTraits.None, 30 + tier * tier * 30, 36, station, tier);
        Station(TileId.FieldWorkbench, "Field workbench", CraftingStationId.FieldWorkbench, 1);
        Station(TileId.NanoFabricatorMk2, "Nano-Fabricator Mk2", CraftingStationId.NanoFabricatorMk2, 5);
        Station(TileId.OreRefineryMk2, "Ore Refinery Mk2", CraftingStationId.OreRefineryMk2, 4);
        Station(TileId.PowerForgeMk2, "Power Forge Mk2", CraftingStationId.PowerForgeMk2, 3);
        Station(TileId.AlloyAnvil, "Alloy anvil", CraftingStationId.AlloyAnvil, 4);
        Station(TileId.VoidForge, "Void forge", CraftingStationId.VoidForge, 6);
        Station(TileId.AstralAssembler, "Astral assembler", CraftingStationId.AstralAssembler, 7);
        Station(TileId.ChemLab, "Chem lab", CraftingStationId.ChemLab, 1);
        Station(TileId.BioLab, "Bio lab", CraftingStationId.BioLab, 2);
        Station(TileId.Loom, "Loom", CraftingStationId.Loom, 1);
        Station(TileId.Sawmill, "Sawmill", CraftingStationId.Sawmill, 1);
        Station(TileId.Kiln, "Kiln", CraftingStationId.Kiln, 1);
        Station(TileId.CookingStation, "Cooking station", CraftingStationId.CookingStation, 1);
        Station(TileId.TinkerBench, "Tinker bench", CraftingStationId.TinkerBench, 2);
        Station(TileId.DroneBay, "Drone bay", CraftingStationId.DroneBay, 2);
        Station(TileId.AmmoPress, "Ammo press", CraftingStationId.AmmoPress, 1);
        Station(TileId.ChargeReactor, "Charge reactor", CraftingStationId.ChargeReactor, 2);
        Station(TileId.ResearchTerminal, "Research terminal", CraftingStationId.ResearchTerminal, 2);
        Station(TileId.FuelRefiner, "Fuel refiner", CraftingStationId.FuelRefiner, 1); // 0.7.5

        // ---- Doors: closed (solid) and open (passable) share one item ----
        foreach (var (closed, open, name, item) in new[]
                 {
                     (TileId.PlankDoor, TileId.PlankDoorOpen, "Plank door", ItemId.PlankDoor),
                     (TileId.HullDoor, TileId.HullDoorOpen, "Hull door", ItemId.HullDoor),
                     (TileId.Trapdoor, TileId.TrapdoorOpen, "Trapdoor", ItemId.Trapdoor),
                     (TileId.VentDoor, TileId.VentDoorOpen, "Vent door", ItemId.VentDoor)
                 })
        {
            Prop(closed, name, item, TileTraits.Door, 12, 30, solid: true);
            Prop(open, $"{name} (open)", null, TileTraits.OpenDoor, 12, 30, drop: item);
        }

        // ---- Furniture ----
        Prop(TileId.PlankChair, "Plank chair", ItemId.PlankChair, TileTraits.Seat, 8);
        Prop(TileId.HullChair, "Hull chair", ItemId.HullChair, TileTraits.Seat, 14);
        Prop(TileId.Stool, "Stool", ItemId.Stool, TileTraits.Seat, 6);
        Prop(TileId.Armchair, "Armchair", ItemId.Armchair, TileTraits.Seat, 24);
        Prop(TileId.Sofa, "Sofa", ItemId.Sofa, TileTraits.Seat, 30);
        Prop(TileId.ObservationChair, "Observation chair", ItemId.ObservationChair, TileTraits.Seat, 28);
        Prop(TileId.PlankTable, "Plank table", ItemId.PlankTable, TileTraits.Table, 10);
        Prop(TileId.HullTable, "Hull table", ItemId.HullTable, TileTraits.Table, 16);
        Prop(TileId.DiningTable, "Dining table", ItemId.DiningTable, TileTraits.Table, 22);
        Prop(TileId.CoffeeTable, "Coffee table", ItemId.CoffeeTable, TileTraits.Table, 18);
        Prop(TileId.BunkBed, "Bunk bed", ItemId.BunkBed, TileTraits.Bed, 20);
        Prop(TileId.BunkBedMetal, "Metal bunk bed", ItemId.BunkBedMetal, TileTraits.Bed, 26);
        Prop(TileId.ModularBed, "Modular bed", ItemId.ModularBed, TileTraits.Bed, 32);
        Prop(TileId.CryoPodBed, "Cryo-pod bed", ItemId.CryoPodBed, TileTraits.Bed, 60, tier: 3);
        Prop(TileId.StorageCrate, "Storage crate", ItemId.StorageCrate, TileTraits.Container, 10);
        Prop(TileId.WardrobeLocker, "Wardrobe locker", ItemId.WardrobeLocker, TileTraits.Container, 18);
        Prop(TileId.Shelving, "Shelving", ItemId.Shelving, TileTraits.Container, 14);
        Prop(TileId.ShelfJars, "Jar shelf", ItemId.ShelfJars, TileTraits.Container, 14);
        Prop(TileId.StorageContainer, "Storage container", ItemId.StorageContainer, TileTraits.Container, 30);
        Prop(TileId.FoodLocker, "Food locker", ItemId.FoodLocker, TileTraits.Container, 22);
        Prop(TileId.LockedVaultChest, "Locked vault chest", null, TileTraits.Container | TileTraits.Locked, 200, 120);
        Prop(TileId.WallTorch, "Wall torch", ItemId.WallTorch, TileTraits.Light, 2, 8);
        Prop(TileId.DeskLamp, "Desk lamp", ItemId.DeskLamp, TileTraits.Light, 8);
        Prop(TileId.FloorLamp, "Floor lamp", ItemId.FloorLamp, TileTraits.Light, 10);
        Prop(TileId.CeilingLight, "Ceiling light", ItemId.CeilingLight, TileTraits.Light, 12);
        Prop(TileId.StandingFloodlight, "Standing floodlight", ItemId.StandingFloodlight, TileTraits.Light, 18);
        Prop(TileId.GlowLampAmber, "Amber glow-lamp", ItemId.GlowLampAmber, TileTraits.Light, 14);
        Prop(TileId.GlowLampCyan, "Cyan glow-lamp", ItemId.GlowLampCyan, TileTraits.Light, 14);
        Prop(TileId.GlowLampViolet, "Violet glow-lamp", ItemId.GlowLampViolet, TileTraits.Light, 14);
        Prop(TileId.ColonyBanner, "Colony banner", ItemId.ColonyBanner, TileTraits.None, 10);
        Prop(TileId.SignPost, "Sign post", ItemId.SignPost, TileTraits.None, 4);
        Prop(TileId.PaintingPlanet, "Planet painting", ItemId.PaintingPlanet, TileTraits.None, 25);
        Prop(TileId.PlantPot, "Plant pot", ItemId.PlantPot, TileTraits.Organic, 5);
        Prop(TileId.HerbPlanter, "Herb planter", ItemId.HerbPlanter, TileTraits.Organic, 12);
        Prop(TileId.DisplayPanel, "Display panel", ItemId.DisplayPanel, TileTraits.Light, 30, tier: 2);
        Prop(TileId.ControlConsole, "Control console", ItemId.ControlConsole, TileTraits.Table, 40, tier: 2);
        Prop(TileId.PowerHub, "Power hub", ItemId.PowerHub, TileTraits.None, 36, tier: 2);
        Prop(TileId.BatteryCharger, "Battery charger", ItemId.BatteryCharger, TileTraits.None, 30, tier: 2);
        Prop(TileId.ChemicalAnalyzer, "Chemical analyzer", ItemId.ChemicalAnalyzer, TileTraits.Table, 34, tier: 2);
        Prop(TileId.FoodSynthesizer, "Food synthesizer", ItemId.FoodSynthesizer, TileTraits.None, 40, tier: 2);
        Prop(TileId.WaterPurifier, "Water purifier", ItemId.WaterPurifier, TileTraits.None, 34, tier: 2);
        Prop(TileId.AdvancedFabricator, "Advanced fabricator", ItemId.AdvancedFabricator, TileTraits.Table, 44, tier: 3);
        Prop(TileId.CraftingBench, "Crafting bench", ItemId.CraftingBench, TileTraits.Table, 12);
        Prop(TileId.MetalFurnace, "Metal furnace", ItemId.MetalFurnace, TileTraits.Light, 20);
        Prop(TileId.ResearchLab, "Research lab", ItemId.ResearchLab, TileTraits.Table, 40, tier: 2);

        // ---- Biome structures and vault decor (generated, not crafted) ----
        Prop(TileId.BloomHive, "Bloom hive", null, TileTraits.Organic, 0, 50, drop: ItemId.RoyalNectar);
        Prop(TileId.PrecursorVaultDoor, "Precursor vault door", null, TileTraits.Locked, 0, 255, solid: true);
        Prop(TileId.CoastalHabPod, "Coastal hab pod", null, TileTraits.Container, 0, 40);
        Prop(TileId.BeaconTower, "Beacon tower", null, TileTraits.Light, 0, 50);
        Prop(TileId.FungalHabRuin, "Fungal hab ruin", null, TileTraits.Container, 0, 40);
        Prop(TileId.CrystalAltar, "Crystal altar", null, TileTraits.Crystal, 0, 60, drop: ItemId.PrismCatalyst);
        Prop(TileId.PrecursorStatue, "Precursor statue", null, TileTraits.None, 0, 80, drop: ItemId.PrecursorRelic);
        Prop(TileId.GlyphTablet, "Glyph tablet", null, TileTraits.None, 0, 60, drop: ItemId.PrecursorGlyphBlock);
        Prop(TileId.EnergyPylon, "Energy pylon", null, TileTraits.Light, 0, 60, drop: ItemId.AncientPowerCell);
        Prop(TileId.GeodeCluster, "Geode cluster", null, TileTraits.Light, 0, 30, drop: ItemId.DiamondShard);
        Prop(TileId.MiteSilkCocoon, "Silk cocoon", null, TileTraits.Organic, 0, 12, drop: ItemId.SilkThread);
        AddPhysicsTiles(list);
        AddWorldGenTiles(list);
        AddThemeTiles(list);
        AddPlayerSystemsTiles(list);
        list.AddRange(Rimtera.Systems.ContentFillFlora.TileDefinitions()); // content fill: biome trees, gem trees, wild plants
        list.AddRange(Rimtera.Systems.GrowthItems.TileDefinitions()); // character growth: lifebloom pod plant
        return list.ToArray();
    }
}
