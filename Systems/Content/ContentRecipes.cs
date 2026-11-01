using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// The content-expansion crafting tree: stations, tier bars, generated tier gear recipes (one bar per
/// tier at that tier's forge), ammunition, accessories, consumables, blocks, furniture, walls and
/// summons. Quantities are original Rimtera tuning.
/// </summary>
internal static class ContentRecipes
{
    /// <summary>The forge each progression tier's gear is built at.</summary>
    public static readonly IReadOnlyDictionary<int, CraftingStationId> TierStations = new Dictionary<int, CraftingStationId>
    {
        [1] = CraftingStationId.Anvil, [2] = CraftingStationId.Anvil, [3] = CraftingStationId.PowerForgeMk2,
        [4] = CraftingStationId.AlloyAnvil, [5] = CraftingStationId.NanoFabricatorMk2, [6] = CraftingStationId.VoidForge,
        [7] = CraftingStationId.AstralAssembler
    };

    /// <summary>Gear that only drops (bosses, biomes, shops); everything else tiered gets a generated recipe.</summary>
    private static readonly HashSet<ItemId> LootOnly =
    [
        ItemId.BoneClub, ItemId.MushroomMace, ItemId.CoralTrident, ItemId.HarpoonGun, ItemId.CoralWand, ItemId.SporeCenser,
        ItemId.CoralJellyFamiliar, ItemId.SporeFamiliar, ItemId.MechCompanionKey, ItemId.HiveStingerRapier, ItemId.HiveFamiliar,
        ItemId.LaserScalpel, ItemId.CoreBreaker
    ];

    public static void RegisterAll(CraftingSystem crafting, ItemCatalog items)
    {
        var used = new HashSet<string>(crafting.Recipes.Select(recipe => recipe.Id), StringComparer.OrdinalIgnoreCase);
        void Add(string id, ItemId output, int count, CraftingStationId[] stations, params (ItemId Item, int Count)[] ingredients)
        {
            var unique = id;
            for (var n = 2; !used.Add(unique); n++)
                unique = $"{id}-{n}";
            crafting.Register(new RecipeDefinition(unique, output, count,
                ingredients.Select(entry => IngredientRequirement.Exact(entry.Item, entry.Count)).ToArray(),
                stations.Length == 0 ? null : new HashSet<CraftingStationId>(stations)));
        }
        void AddAlt(string id, ItemId output, int count, CraftingStationId station, ItemId[] alternatives, int quantity,
            params (ItemId Item, int Count)[] rest)
        {
            used.Add(id);
            var groups = new List<IngredientRequirement> { new(alternatives, quantity) };
            groups.AddRange(rest.Select(entry => IngredientRequirement.Exact(entry.Item, entry.Count)));
            crafting.Register(new RecipeDefinition(id, output, count, groups,
                station == CraftingStationId.None ? null : new HashSet<CraftingStationId> { station }));
        }
        string Key(ItemId id) => "x-" + id.ToString().ToLowerInvariant();
        CraftingStationId[] At(params CraftingStationId[] stations) => stations;
        var none = Array.Empty<CraftingStationId>();
        const CraftingStationId field = CraftingStationId.FieldWorkbench, fab = CraftingStationId.Workbench,
            furnace = CraftingStationId.Furnace, anvil = CraftingStationId.Anvil, forge2 = CraftingStationId.PowerForgeMk2,
            alloy = CraftingStationId.AlloyAnvil, refinery2 = CraftingStationId.OreRefineryMk2, fab2 = CraftingStationId.NanoFabricatorMk2,
            voidForge = CraftingStationId.VoidForge, astral = CraftingStationId.AstralAssembler, chem = CraftingStationId.ChemLab,
            bio = CraftingStationId.BioLab, loom = CraftingStationId.Loom, saw = CraftingStationId.Sawmill, kiln = CraftingStationId.Kiln,
            cook = CraftingStationId.CookingStation, tinker = CraftingStationId.TinkerBench, droneBay = CraftingStationId.DroneBay,
            ammo = CraftingStationId.AmmoPress, reactor = CraftingStationId.ChargeReactor, research = CraftingStationId.ResearchTerminal;

        // ---- Stations ----
        Add(Key(ItemId.FieldWorkbench), ItemId.FieldWorkbench, 1, none, (ItemId.WoodBlock, 6));
        Add(Key(ItemId.Sawmill), ItemId.Sawmill, 1, At(field), (ItemId.WoodBlock, 12), (ItemId.FerriteBar, 2));
        Add(Key(ItemId.Kiln), ItemId.Kiln, 1, At(field), (ItemId.RockBlock, 25), (ItemId.WoodBlock, 5));
        Add(Key(ItemId.Loom), ItemId.Loom, 1, At(field), (ItemId.WoodBlock, 10), (ItemId.RopeCoil, 2));
        Add(Key(ItemId.CookingStation), ItemId.CookingStation, 1, At(fab), (ItemId.RockBlock, 10), (ItemId.FerriteBar, 3));
        Add(Key(ItemId.ChemLab), ItemId.ChemLab, 1, At(fab), (ItemId.GlassBlock, 6), (ItemId.FerriteBar, 4));
        Add(Key(ItemId.AmmoPress), ItemId.AmmoPress, 1, At(anvil), (ItemId.FerriteBar, 8), (ItemId.RockBlock, 10));
        Add(Key(ItemId.TinkerBench), ItemId.TinkerBench, 1, At(fab), (ItemId.FerriteBar, 6), (ItemId.WoodBlock, 10));
        Add(Key(ItemId.BioLab), ItemId.BioLab, 1, At(fab), (ItemId.GlassBlock, 8), (ItemId.CobaltBar, 4), (ItemId.BioGel, 5));
        Add(Key(ItemId.DroneBay), ItemId.DroneBay, 1, At(anvil), (ItemId.CobaltBar, 10), (ItemId.EnergyCrystal, 4), (ItemId.PowerCell, 2));
        Add(Key(ItemId.ChargeReactor), ItemId.ChargeReactor, 1, At(anvil), (ItemId.CobaltBar, 8), (ItemId.EnergyCrystal, 8));
        Add(Key(ItemId.ResearchTerminal), ItemId.ResearchTerminal, 1, At(fab), (ItemId.CobaltBar, 6), (ItemId.CircuitBoard, 3), (ItemId.GlassBlock, 4));
        Add(Key(ItemId.PowerForgeMk2), ItemId.PowerForgeMk2, 1, At(anvil), (ItemId.CobaltBar, 12), (ItemId.OverseerCircuit, 1), (ItemId.AnvilBlock, 1));
        Add(Key(ItemId.OreRefineryMk2), ItemId.OreRefineryMk2, 1, At(forge2), (ItemId.FurnaceBlock, 1), (ItemId.HeartFragment, 2), (ItemId.ObsidianShard, 10));
        // Fission slag (Fission Tyrant drop) moves the tier-4 anvil past the Fracture (G4).
        Add(Key(ItemId.AlloyAnvil), ItemId.AlloyAnvil, 1, At(forge2), (ItemId.ViridiumBar, 10), (ItemId.HeartFragment, 1), (ItemId.FissionSlag, 3));
        Add(Key(ItemId.NanoFabricatorMk2), ItemId.NanoFabricatorMk2, 1, At(alloy), (ItemId.LumeniteBar, 12), (ItemId.WorkbenchBlock, 1), (ItemId.PrismShard, 6));
        Add(Key(ItemId.VoidForge), ItemId.VoidForge, 1, At(fab2), (ItemId.LumeniteBar, 10), (ItemId.VoidEssence, 6), (ItemId.ObsidianShard, 20));
        Add(Key(ItemId.AstralAssembler), ItemId.AstralAssembler, 1, At(voidForge), (ItemId.VoidsteelBar, 12), (ItemId.AstralOre, 6));

        // ---- Bars and refined materials ----
        Add(Key(ItemId.FerriteBar), ItemId.FerriteBar, 1, At(furnace), (ItemId.CopperOre, 3));
        Add("x-ferritebar-scrap", ItemId.FerriteBar, 1, At(furnace), (ItemId.ScrapIronOre, 4));
        Add(Key(ItemId.CobaltBar), ItemId.CobaltBar, 1, At(furnace), (ItemId.CobaltOre, 3));
        Add(Key(ItemId.GlacialBar), ItemId.GlacialBar, 1, At(furnace), (ItemId.GlacialOre, 3), (ItemId.FrostCrystal, 1));
        Add(Key(ItemId.PyrociteBar), ItemId.PyrociteBar, 1, At(furnace), (ItemId.PyrociteOre, 3), (ItemId.ObsidianShard, 1));
        Add(Key(ItemId.UraniumRod), ItemId.UraniumRod, 1, At(furnace), (ItemId.UraniumOre, 3), (ItemId.CausticShard, 1));
        Add(Key(ItemId.MeteoriteBar), ItemId.MeteoriteBar, 1, At(furnace), (ItemId.MeteoriteOre, 3));
        Add(Key(ItemId.ViridiumBar), ItemId.ViridiumBar, 1, At(refinery2), (ItemId.ViridiumOre, 3));
        Add(Key(ItemId.LumeniteBar), ItemId.LumeniteBar, 1, At(refinery2), (ItemId.LumeniteOre, 4));
        Add(Key(ItemId.VoidsteelBar), ItemId.VoidsteelBar, 1, At(voidForge), (ItemId.VoidsteelOre, 4), (ItemId.VoidEssence, 1));
        Add(Key(ItemId.AstralBar), ItemId.AstralBar, 1, At(astral), (ItemId.AstralOre, 2), (ItemId.LumeniteBar, 1), (ItemId.VoidsteelBar, 1));
        Add(Key(ItemId.CircuitBoard), ItemId.CircuitBoard, 2, At(fab), (ItemId.CopperBar, 2), (ItemId.EnergyCrystal, 1));
        Add(Key(ItemId.PowerCell), ItemId.PowerCell, 1, At(fab), (ItemId.EnergyCrystal, 2), (ItemId.FerriteBar, 1));
        Add(Key(ItemId.BioGel), ItemId.BioGel, 2, At(chem), (ItemId.Berry, 1), (ItemId.SoilBlock, 2));
        AddAlt(Key(ItemId.RopeCoil), ItemId.RopeCoil, 1, field, [ItemId.SilkThread, ItemId.JungleRootBlock], 4);
        AddAlt(Key(ItemId.ClothRoll), ItemId.ClothRoll, 1, loom, [ItemId.SilkThread, ItemId.JungleRootBlock], 6);
        Add(Key(ItemId.PrismShard), ItemId.PrismShard, 1, At(kiln), (ItemId.PearlStoneBlock, 3), (ItemId.EnergyCrystal, 1));
        Add(Key(ItemId.DroneSpareParts), ItemId.DroneSpareParts, 2, At(fab), (ItemId.CobaltBar, 1), (ItemId.CircuitBoard, 1));

        // ---- Generated tier gear: weapons, tools and armor ----
        foreach (var item in items.All)
        {
            if (item.Tier is < 1 or > 7 || LootOnly.Contains(item.Id) || ArsenalItems.Owns(item.Id))
                continue;
            var isGear = item.UseKind is ItemUseKind.Melee or ItemUseKind.Ranged or ItemUseKind.Tech or ItemUseKind.Command
                or ItemUseKind.Pickaxe or ItemUseKind.Cutter or ItemUseKind.Demolisher ||
                (item.UseKind == ItemUseKind.Equipment && item.ArmorSetId is not null && item.Id >= ItemId.ScavHelmet);
            if (!isGear || (int)item.Id < (int)ItemId.Credits)
                continue;
            var tier = item.Tier;
            var bar = BarFor(item);
            var extra = ExtraFor(item, tier);
            var station = TierStations[tier];
            var stations = new List<CraftingStationId> { station };
            int bars;
            switch (item.UseKind)
            {
                case ItemUseKind.Melee: bars = item.SignalWhip ? 6 : 8; break;
                case ItemUseKind.Ranged: bars = 10; break;
                case ItemUseKind.Tech: bars = 7; break;
                case ItemUseKind.Command: bars = 8; break;
                case ItemUseKind.Pickaxe: bars = 10; break;
                case ItemUseKind.Cutter: bars = 8; break;
                case ItemUseKind.Demolisher: bars = 9; break;
                default:
                    bars = item.EquipmentSlot switch
                    {
                        EquipmentSlot.Head => 10,
                        EquipmentSlot.Body => 14,
                        _ => 12
                    };
                    break;
            }
            var ingredients = new List<(ItemId, int)> { (bar, bars), extra };
            if (item.UseKind == ItemUseKind.Tech)
                ingredients.Add((ItemId.EnergyCrystal, 3));
            if (item.UseKind == ItemUseKind.Command && tier >= 2)
            {
                ingredients.Add((ItemId.DroneSpareParts, 2));
                stations.Add(droneBay);
            }
            if (item.SignalWhip)
                ingredients.Add((ItemId.RopeCoil, 2));
            if (item.Name.StartsWith("Datapad", StringComparison.Ordinal))
                stations.Add(research);
            if (item.UseKind == ItemUseKind.Equipment && tier >= 3)
                ingredients.Add((ItemId.ClothRoll, 2));
            // Top hardmode gear also needs the post-Fracture essences.
            if (tier == 6)
            {
                ingredients.Add((ItemId.PrismEssence, 3));
                ingredients.Add((ItemId.BlightEssence, 3));
            }
            Add(Key(item.Id), item.Id, 1, stations.Distinct().ToArray(), ingredients.ToArray());
        }

        // ---- Ammunition ----
        Add(Key(ItemId.SlugRounds), ItemId.SlugRounds, 50, At(ammo), (ItemId.FerriteBar, 1));
        Add("x-slugrounds-anvil", ItemId.SlugRounds, 25, At(anvil), (ItemId.FerriteBar, 1));
        Add(Key(ItemId.ShotgunShells), ItemId.ShotgunShells, 30, At(ammo), (ItemId.FerriteBar, 1), (ItemId.SandBlock, 2));
        Add(Key(ItemId.ToxicDarts), ItemId.ToxicDarts, 40, At(ammo), (ItemId.BioGel, 1), (ItemId.WoodBlock, 1));
        Add(Key(ItemId.CryoRounds), ItemId.CryoRounds, 50, At(ammo), (ItemId.GlacialBar, 1), (ItemId.FrostCrystal, 1));
        Add(Key(ItemId.FuelCanister), ItemId.FuelCanister, 40, At(ammo), (ItemId.PyrociteOre, 2));
        Add(Key(ItemId.ExplosiveCharge), ItemId.ExplosiveCharge, 20, At(ammo), (ItemId.PyrociteBar, 1), (ItemId.ObsidianShard, 2));
        Add(Key(ItemId.MiniRocket), ItemId.MiniRocket, 20, At(ammo), (ItemId.ViridiumBar, 1), (ItemId.ExplosiveCharge, 2));

        // ---- Accessories ----
        (ItemId Out, (ItemId, int)[] In, CraftingStationId Station)[] accessories =
        [
            (ItemId.FocusLensCrystal, [(ItemId.CrystalBlockBlock, 4), (ItemId.GlacialBar, 4)], tinker),
            (ItemId.OverchargeCapacitor, [(ItemId.PowerCell, 4), (ItemId.UraniumRod, 4)], tinker),
            (ItemId.CommandVisor, [(ItemId.CircuitBoard, 3), (ItemId.GlacialBar, 4)], tinker),
            (ItemId.DroneBattery, [(ItemId.PowerCell, 4), (ItemId.PyrociteBar, 3)], tinker),
            (ItemId.DroneController, [(ItemId.ViridiumBar, 6), (ItemId.CircuitBoard, 4)], tinker),
            (ItemId.ReinforcedArmorSuit, [(ItemId.ViridiumBar, 10), (ItemId.PlateBlock, 10)], tinker),
            (ItemId.JetBoots, [(ItemId.PyrociteBar, 6), (ItemId.PowerCell, 2)], tinker),
            (ItemId.EnergyShieldEmitter, [(ItemId.ViridiumBar, 6), (ItemId.PowerCell, 4)], tinker),
            (ItemId.NightVisionGoggles, [(ItemId.GlassBlock, 4), (ItemId.CobaltBar, 3)], tinker),
            (ItemId.MagnetRing, [(ItemId.FerriteBar, 6), (ItemId.CobaltBar, 2)], tinker),
            (ItemId.MeleeModule, [(ItemId.ViridiumBar, 6), (ItemId.CircuitBoard, 3), (ItemId.RubyShard, 2)], tinker),
            (ItemId.BallisticsModule, [(ItemId.ViridiumBar, 6), (ItemId.CircuitBoard, 3), (ItemId.TopazShard, 2)], tinker),
            (ItemId.TechModule, [(ItemId.ViridiumBar, 6), (ItemId.CircuitBoard, 3), (ItemId.SapphireShard, 2)], tinker),
            (ItemId.CommandModule, [(ItemId.ViridiumBar, 6), (ItemId.CircuitBoard, 3), (ItemId.EmeraldShard, 2)], tinker),
            (ItemId.RocketPack, [(ItemId.ViridiumBar, 8), (ItemId.PowerCell, 6)], tinker),
            (ItemId.GliderWings, [(ItemId.ClothRoll, 8), (ItemId.CobaltBar, 4)], tinker),
            (ItemId.GrappleHook, [(ItemId.CobaltBar, 5), (ItemId.RopeCoil, 3)], tinker),
            (ItemId.DoubleJumpCanister, [(ItemId.CobaltBar, 4), (ItemId.PowerCell, 2)], tinker),
            (ItemId.ClimbingClaws, [(ItemId.FerriteBar, 6)], tinker),
            (ItemId.LifeMonitor, [(ItemId.CircuitBoard, 1), (ItemId.GlassBlock, 2)], tinker),
            (ItemId.OreScanner, [(ItemId.CircuitBoard, 2), (ItemId.CobaltBar, 2)], tinker),
            (ItemId.DepthMeter, [(ItemId.FerriteBar, 3), (ItemId.GlassBlock, 1)], tinker),
            (ItemId.CompassChip, [(ItemId.CircuitBoard, 1), (ItemId.FerriteBar, 2)], tinker),
            (ItemId.RegenImplant, [(ItemId.BioGel, 10), (ItemId.GlacialBar, 4)], tinker),
            (ItemId.RebreatherMask, [(ItemId.GlassBlock, 4), (ItemId.ClothRoll, 2)], tinker),
            (ItemId.LavaWaderBoots, [(ItemId.ObsidianShard, 12), (ItemId.PyrociteBar, 6)], tinker),
            (ItemId.FrostGripGloves, [(ItemId.GlacialBar, 5), (ItemId.ClothRoll, 2)], tinker),
            (ItemId.LuckyCharm, [(ItemId.TopazShard, 3), (ItemId.FerriteBar, 2)], tinker),
            (ItemId.ThornPlating, [(ItemId.CobaltBar, 6), (ItemId.PlateBlock, 6)], tinker),
            (ItemId.AstralHalo, [(ItemId.AstralBar, 8), (ItemId.DiamondShard, 4)], astral)
        ];
        foreach (var (output, inputs, station) in accessories)
            Add(Key(output), output, 1, At(station), inputs);

        // ---- Consumables and food ----
        Add(Key(ItemId.ChargeCellSmall), ItemId.ChargeCellSmall, 2, At(reactor), (ItemId.EnergyCrystal, 1));
        Add(Key(ItemId.ChargeStim), ItemId.ChargeStim, 1, At(chem), (ItemId.EnergyCrystal, 2), (ItemId.BioGel, 1));
        Add(Key(ItemId.MedStim), ItemId.MedStim, 1, At(chem), (ItemId.BioGel, 2), (ItemId.RestorativeTonic, 1));
        // The healing and charge ladders (0.1.9 C1). Gates: Chem Lab -> Bio Lab after the first boss (its overseer circuit)
        // -> Bio Lab beside the Alloy Anvil with post-Fracture prism or flight essence -> Bio Lab beside the Void Forge.
        Add(Key(ItemId.TraumaStim), ItemId.TraumaStim, 5, At(bio), (ItemId.MedStim, 5), (ItemId.BioGel, 5), (ItemId.OverseerCircuit, 1));
        Add(Key(ItemId.RestorationStim), ItemId.RestorationStim, 2, At(bio), (ItemId.MedStim, 2), (ItemId.GlowSpore, 2), (ItemId.RestorativeTonic, 1));
        Add(Key(ItemId.NaniteStim), ItemId.NaniteStim, 3, At(bio, alloy), (ItemId.TraumaStim, 3), (ItemId.PrismEssence, 1), (ItemId.BioGel, 1));
        Add(Key(ItemId.PhoenixStim), ItemId.PhoenixStim, 3, At(bio, voidForge), (ItemId.NaniteStim, 3), (ItemId.VoidsteelBar, 1), (ItemId.VoidEssence, 1));
        Add(Key(ItemId.ChargeCellMedium), ItemId.ChargeCellMedium, 5, At(bio), (ItemId.ChargeCellSmall, 5), (ItemId.EnergyCrystal, 2), (ItemId.OverseerCircuit, 1));
        Add(Key(ItemId.ChargeCellLarge), ItemId.ChargeCellLarge, 3, At(bio, alloy), (ItemId.ChargeCellMedium, 3), (ItemId.FlightEssence, 1), (ItemId.EnergyCrystal, 1));
        Add(Key(ItemId.ChargeCellPeak), ItemId.ChargeCellPeak, 3, At(bio, voidForge), (ItemId.ChargeCellLarge, 3), (ItemId.VoidsteelBar, 1), (ItemId.PowerCell, 1));
        Add(Key(ItemId.SpeedStim), ItemId.SpeedStim, 1, At(chem), (ItemId.BioGel, 1), (ItemId.Berry, 2));
        Add(Key(ItemId.ArmorStim), ItemId.ArmorStim, 1, At(chem), (ItemId.BioGel, 1), (ItemId.RockBlock, 5));
        Add(Key(ItemId.RegenPatch), ItemId.RegenPatch, 1, At(chem), (ItemId.BioGel, 2), (ItemId.ClothRoll, 1));
        Add(Key(ItemId.SpelunkerSpray), ItemId.SpelunkerSpray, 1, At(chem), (ItemId.BioGel, 1), (ItemId.GlowSpore, 2));
        Add(Key(ItemId.NightOwlDrops), ItemId.NightOwlDrops, 1, At(chem), (ItemId.GlowSpore, 2), (ItemId.BioGel, 1));
        Add(Key(ItemId.HunterLens), ItemId.HunterLens, 1, At(chem), (ItemId.GlassBlock, 1), (ItemId.BioGel, 1));
        Add(Key(ItemId.FeatherfallPill), ItemId.FeatherfallPill, 1, At(chem), (ItemId.BioGel, 1), (ItemId.SilkThread, 2));
        Add(Key(ItemId.WaterBreathCapsule), ItemId.WaterBreathCapsule, 1, At(chem), (ItemId.BioGel, 1), (ItemId.CoralShard, 1));
        Add(Key(ItemId.RationBar), ItemId.RationBar, 2, At(cook), (ItemId.Berry, 3));
        Add(Key(ItemId.SnowWolfSteak), ItemId.SnowWolfSteak, 1, At(cook), (ItemId.Berry, 2), (ItemId.FrostCrystal, 1));
        Add(Key(ItemId.MushroomStew), ItemId.MushroomStew, 1, At(cook), (ItemId.GlowSpore, 3), (ItemId.MushroomCapBlock, 1));
        Add(Key(ItemId.NectarTea), ItemId.NectarTea, 1, At(cook), (ItemId.NectarBlock, 2));
        Add(Key(ItemId.CoralSushi), ItemId.CoralSushi, 1, At(cook), (ItemId.TideFish, 1), (ItemId.CoralShard, 1));
        Add(Key(ItemId.NanoBananaSplit), ItemId.NanoBananaSplit, 1, At(cook), (ItemId.Berry, 4), (ItemId.NectarBlock, 1));

        // ---- Tools and utilities ----
        Add(Key(ItemId.BugNet), ItemId.BugNet, 1, At(field), (ItemId.WoodBlock, 4), (ItemId.RopeCoil, 1));
        Add(Key(ItemId.FishingRod), ItemId.FishingRod, 1, At(field), (ItemId.WoodBlock, 6), (ItemId.RopeCoil, 1));
        Add(Key(ItemId.EmptyBucket), ItemId.EmptyBucket, 1, At(anvil), (ItemId.FerriteBar, 3));
        Add(Key(ItemId.PaintSprayer), ItemId.PaintSprayer, 1, At(tinker), (ItemId.FerriteBar, 4), (ItemId.RubyShard, 1), (ItemId.SapphireShard, 1));
        Add(Key(ItemId.WireTool), ItemId.WireTool, 1, At(tinker), (ItemId.FerriteBar, 4), (ItemId.CircuitBoard, 1));
        Add(Key(ItemId.GlowSticks), ItemId.GlowSticks, 10, At(chem), (ItemId.BioGel, 1), (ItemId.GlowSpore, 1));
        Add(Key(ItemId.TeleportBeacon), ItemId.TeleportBeacon, 1, At(research), (ItemId.AncientPowerCell, 1), (ItemId.LumeniteBar, 5));

        // ---- Blocks ----
        foreach (var (output, input) in new[]
                 {
                     (ItemId.FerriteBrickBlock, ItemId.FerriteBar), (ItemId.CobaltBrickBlock, ItemId.CobaltBar),
                     (ItemId.ViridiumBrickBlock, ItemId.ViridiumBar), (ItemId.LumeniteBrickBlock, ItemId.LumeniteBar),
                     (ItemId.VoidsteelBrickBlock, ItemId.VoidsteelBar), (ItemId.AstralBrickBlock, ItemId.AstralBar)
                 })
            Add(Key(output), output, 10, At(kiln), (ItemId.RockBlock, 10), (input, 1));
        foreach (var (output, input) in new[]
                 {
                     (ItemId.SlateBrickBlock, ItemId.SlateBlock), (ItemId.AlabasterBrickBlock, ItemId.AlabasterBlock),
                     (ItemId.BasaltBrickBlock, ItemId.BasaltColumnBlock), (ItemId.PearlBrickBlock, ItemId.PearlStoneBlock),
                     (ItemId.HiveBrickBlock, ItemId.HiveCombBlock), (ItemId.CoralBrickBlock, ItemId.CoralReefBlock)
                 })
            Add(Key(output), output, 1, At(kiln), (input, 2));
        AddAlt(Key(ItemId.GlassBlock), ItemId.GlassBlock, 1, kiln, [ItemId.SandBlock, ItemId.ShellSandBlock, ItemId.PearlSandBlock, ItemId.SiltBlock], 2);
        Add(Key(ItemId.ReinforcedGlassBlock), ItemId.ReinforcedGlassBlock, 2, At(kiln), (ItemId.GlassBlock, 2), (ItemId.CobaltBar, 1));
        Add(Key(ItemId.CrystalBlockBlock), ItemId.CrystalBlockBlock, 2, At(kiln), (ItemId.EnergyCrystal, 2), (ItemId.GlassBlock, 2));
        Add(Key(ItemId.HullPlatingGreyBlock), ItemId.HullPlatingGreyBlock, 10, At(anvil), (ItemId.FerriteBar, 1), (ItemId.RockBlock, 4));
        foreach (var (output, gem) in new[]
                 {
                     (ItemId.HullPlatingBlueBlock, ItemId.SapphireShard), (ItemId.HullPlatingGreenBlock, ItemId.EmeraldShard),
                     (ItemId.HullPlatingRedBlock, ItemId.RubyShard), (ItemId.HullPlatingHazardBlock, ItemId.TopazShard)
                 })
            Add(Key(output), output, 10, At(fab), (ItemId.HullPlatingGreyBlock, 10), (gem, 1));
        Add(Key(ItemId.AstroTimberPlanksBlock), ItemId.AstroTimberPlanksBlock, 2, At(saw), (ItemId.WoodBlock, 1));
        Add(Key(ItemId.CharredPlanksBlock), ItemId.CharredPlanksBlock, 4, At(saw), (ItemId.AstroTimberPlanksBlock, 4), (ItemId.AshBlock, 1));
        Add(Key(ItemId.PlateBlock), ItemId.PlateBlock, 4, At(anvil), (ItemId.FerriteBar, 1));
        Add(Key(ItemId.GrateBlock), ItemId.GrateBlock, 4, At(anvil), (ItemId.FerriteBar, 1));
        Add(Key(ItemId.VentBlock), ItemId.VentBlock, 4, At(anvil), (ItemId.FerriteBar, 1), (ItemId.GrateBlock, 2));
        Add(Key(ItemId.CircuitPanelBlock), ItemId.CircuitPanelBlock, 4, At(fab), (ItemId.CircuitBoard, 1), (ItemId.PlateBlock, 4));
        Add(Key(ItemId.ConveyorBlock), ItemId.ConveyorBlock, 10, At(anvil), (ItemId.FerriteBar, 2), (ItemId.PowerCell, 1));

        // ---- Furniture (wood at the field workbench / sawmill, metal at the fabricator) ----
        (ItemId Out, int Count, CraftingStationId Station, (ItemId, int)[] In)[] furniture =
        [
            (ItemId.PlankDoor, 1, field, [(ItemId.WoodBlock, 6)]),
            (ItemId.PlankChair, 1, field, [(ItemId.WoodBlock, 4)]),
            (ItemId.PlankTable, 1, field, [(ItemId.WoodBlock, 8)]),
            (ItemId.Stool, 1, field, [(ItemId.WoodBlock, 3)]),
            (ItemId.Trapdoor, 1, field, [(ItemId.WoodBlock, 4)]),
            (ItemId.SignPost, 1, field, [(ItemId.WoodBlock, 3)]),
            (ItemId.StorageCrate, 1, field, [(ItemId.WoodBlock, 8), (ItemId.FerriteBar, 1)]),
            (ItemId.CraftingBench, 1, field, [(ItemId.WoodBlock, 10)]),
            (ItemId.PlantPot, 1, field, [(ItemId.ClayBlock, 3)]),
            (ItemId.HerbPlanter, 1, field, [(ItemId.WoodBlock, 4), (ItemId.SoilBlock, 4)]),
            (ItemId.WallTorch, 4, CraftingStationId.None, [(ItemId.WoodBlock, 1), (ItemId.CopperOre, 1)]),
            (ItemId.BunkBed, 1, saw, [(ItemId.AstroTimberPlanksBlock, 10), (ItemId.ClothRoll, 2)]),
            (ItemId.Armchair, 1, saw, [(ItemId.AstroTimberPlanksBlock, 6), (ItemId.ClothRoll, 2)]),
            (ItemId.Sofa, 1, saw, [(ItemId.AstroTimberPlanksBlock, 8), (ItemId.ClothRoll, 3)]),
            (ItemId.DiningTable, 1, saw, [(ItemId.AstroTimberPlanksBlock, 10)]),
            (ItemId.CoffeeTable, 1, saw, [(ItemId.AstroTimberPlanksBlock, 6)]),
            (ItemId.Shelving, 1, saw, [(ItemId.AstroTimberPlanksBlock, 8)]),
            (ItemId.ShelfJars, 1, saw, [(ItemId.AstroTimberPlanksBlock, 6), (ItemId.GlassBlock, 3)]),
            (ItemId.WardrobeLocker, 1, saw, [(ItemId.AstroTimberPlanksBlock, 12)]),
            (ItemId.ColonyBanner, 1, loom, [(ItemId.ClothRoll, 3), (ItemId.WoodBlock, 2)]),
            (ItemId.PaintingPlanet, 1, loom, [(ItemId.ClothRoll, 2), (ItemId.WoodBlock, 2), (ItemId.SapphireShard, 1)]),
            (ItemId.HullDoor, 1, fab, [(ItemId.FerriteBar, 4), (ItemId.PlateBlock, 2)]),
            (ItemId.VentDoor, 1, fab, [(ItemId.FerriteBar, 3), (ItemId.VentBlock, 2)]),
            (ItemId.HullChair, 1, fab, [(ItemId.FerriteBar, 3)]),
            (ItemId.HullTable, 1, fab, [(ItemId.FerriteBar, 5)]),
            (ItemId.ObservationChair, 1, fab, [(ItemId.CobaltBar, 4), (ItemId.ClothRoll, 1)]),
            (ItemId.BunkBedMetal, 1, fab, [(ItemId.FerriteBar, 8), (ItemId.ClothRoll, 2)]),
            (ItemId.ModularBed, 1, fab, [(ItemId.CobaltBar, 6), (ItemId.ClothRoll, 3)]),
            (ItemId.CryoPodBed, 1, forge2, [(ItemId.GlacialBar, 8), (ItemId.GlassBlock, 4), (ItemId.PowerCell, 2)]),
            (ItemId.StorageContainer, 1, fab, [(ItemId.FerriteBar, 10)]),
            (ItemId.FoodLocker, 1, fab, [(ItemId.FerriteBar, 6), (ItemId.GlacialBar, 1)]),
            (ItemId.DeskLamp, 1, fab, [(ItemId.FerriteBar, 2), (ItemId.EnergyCrystal, 1)]),
            (ItemId.FloorLamp, 1, fab, [(ItemId.FerriteBar, 3), (ItemId.EnergyCrystal, 1)]),
            (ItemId.CeilingLight, 1, fab, [(ItemId.FerriteBar, 2), (ItemId.GlassBlock, 1), (ItemId.EnergyCrystal, 1)]),
            (ItemId.StandingFloodlight, 1, fab, [(ItemId.CobaltBar, 3), (ItemId.PowerCell, 1)]),
            (ItemId.GlowLampAmber, 1, fab, [(ItemId.GlassBlock, 2), (ItemId.TopazShard, 1)]),
            (ItemId.GlowLampCyan, 1, fab, [(ItemId.GlassBlock, 2), (ItemId.SapphireShard, 1)]),
            (ItemId.GlowLampViolet, 1, fab, [(ItemId.GlassBlock, 2), (ItemId.AmethystShard, 1)]),
            (ItemId.DisplayPanel, 1, fab, [(ItemId.CircuitBoard, 2), (ItemId.GlassBlock, 2)]),
            (ItemId.ControlConsole, 1, fab, [(ItemId.CircuitBoard, 3), (ItemId.PlateBlock, 4)]),
            (ItemId.PowerHub, 1, fab, [(ItemId.PowerCell, 3), (ItemId.PlateBlock, 4)]),
            (ItemId.BatteryCharger, 1, fab, [(ItemId.PowerCell, 2), (ItemId.FerriteBar, 3)]),
            (ItemId.ChemicalAnalyzer, 1, chem, [(ItemId.GlassBlock, 4), (ItemId.CircuitBoard, 2)]),
            (ItemId.FoodSynthesizer, 1, cook, [(ItemId.CircuitBoard, 2), (ItemId.CobaltBar, 4)]),
            (ItemId.WaterPurifier, 1, fab, [(ItemId.GlassBlock, 4), (ItemId.CobaltBar, 3)]),
            (ItemId.AdvancedFabricator, 1, forge2, [(ItemId.CircuitBoard, 4), (ItemId.PyrociteBar, 4)]),
            (ItemId.MetalFurnace, 1, fab, [(ItemId.FerriteBar, 6), (ItemId.RockBlock, 10)]),
            (ItemId.ResearchLab, 1, research, [(ItemId.CircuitBoard, 4), (ItemId.GlassBlock, 6)])
        ];
        foreach (var (output, count, station, inputs) in furniture)
            Add(Key(output), output, count, station == CraftingStationId.None ? none : At(station), inputs);

        // ---- Walls: four walls per block at the field workbench ----
        (ItemId Wall, ItemId Block)[] walls =
        [
            (ItemId.PlankWall, ItemId.WoodBlock), (ItemId.PlatingGreyWall, ItemId.HullPlatingGreyBlock),
            (ItemId.PlatingBlueWall, ItemId.HullPlatingBlueBlock), (ItemId.GlassWall, ItemId.GlassBlock),
            (ItemId.FerriteBrickWall, ItemId.FerriteBrickBlock), (ItemId.AlabasterBrickWall, ItemId.AlabasterBrickBlock),
            (ItemId.LatticeWall, ItemId.GrateBlock), (ItemId.CircuitWall, ItemId.CircuitPanelBlock),
            (ItemId.PearlWall, ItemId.PearlBrickBlock), (ItemId.HiveWall, ItemId.HiveBrickBlock),
            (ItemId.CoralWall, ItemId.CoralBrickBlock), (ItemId.BasaltWall, ItemId.BasaltBrickBlock),
            (ItemId.MudWall, ItemId.MudBlock), (ItemId.PanelWall, ItemId.PlateBlock),
            (ItemId.WindowWall, ItemId.ReinforcedGlassBlock), (ItemId.BlastWall, ItemId.PlateBlock),
            (ItemId.LeadWall, ItemId.UraniumRod)
        ];
        foreach (var (wall, block) in walls)
            Add(Key(wall), wall, 4, At(field), (block, 1));

        // ---- Boss and event summons ----
        (ItemId Out, CraftingStationId Station, (ItemId, int)[] In)[] summons =
        [
            (ItemId.SummonReactorKey, fab, [(ItemId.FerriteBar, 5), (ItemId.EnergyCrystal, 3), (ItemId.HeartFragment, 1)]),
            (ItemId.SummonSwarmPheromone, chem, [(ItemId.BioGel, 5), (ItemId.Berry, 5)]),
            (ItemId.SummonFaultyMiningChip, fab, [(ItemId.CircuitBoard, 2), (ItemId.CobaltOre, 10)]),
            (ItemId.SummonRottingSeed, bio, [(ItemId.JungleRootBlock, 10), (ItemId.BioGel, 3)]),
            (ItemId.SummonScoutBeacon, anvil, [(ItemId.CobaltBar, 6), (ItemId.PowerCell, 2)]),
            (ItemId.FalloutHiveCell, chem, [(ItemId.CausticShard, 8), (ItemId.UraniumOre, 10), (ItemId.BioGel, 4)]),
            // The three hard-tier summons: hardmode bars plus essences, at the hardmode anvil.
            (ItemId.SwarmResonator, alloy, [(ItemId.ViridiumBar, 6), (ItemId.BlightEssence, 6), (ItemId.PowerCell, 3)]),
            (ItemId.RenegadeCredentials, alloy, [(ItemId.LumeniteBar, 6), (ItemId.PrismEssence, 6), (ItemId.CircuitBoard, 4)]),
            (ItemId.MutagenBloom, bio, [(ItemId.ViridiumOre, 10), (ItemId.BioGel, 6)]),
            (ItemId.TaintedTissue, bio, [(ItemId.HeartFragment, 2), (ItemId.BioGel, 8)]),
            (ItemId.WarshipTransponder, alloy, [(ItemId.ViridiumBar, 8), (ItemId.FlightEssence, 6), (ItemId.CircuitBoard, 4)]),
            (ItemId.SunspotLens, kiln, [(ItemId.GlassBlock, 10), (ItemId.PyrociteBar, 4)]),
            (ItemId.VoidLure, chem, [(ItemId.VoidEssence, 3), (ItemId.ObsidianShard, 6)]),
            (ItemId.ClaimMarker, anvil, [(ItemId.CobaltBar, 10), (ItemId.MeteoriteBar, 4)]),
            (ItemId.BountyNotice, fab, [(ItemId.ClothRoll, 2), (ItemId.Credits, 300)]),
            (ItemId.SummonAstralSigil, voidForge, [(ItemId.LumeniteBar, 10), (ItemId.VoidsteelBar, 10), (ItemId.PrismShard, 10)]),
            (ItemId.SporeCrown, bio, [(ItemId.GlowSpore, 15), (ItemId.MushroomCapBlock, 10)]),
            (ItemId.RoyalNectar, bio, [(ItemId.NectarBlock, 15), (ItemId.HiveCombBlock, 10)]),
            (ItemId.PrismCatalyst, fab2, [(ItemId.PrismShard, 12), (ItemId.LumeniteBar, 6)]),
            // Tier 6: the lumenite (post-Fracture) keeps the core out of a pre-Fracture bag; the boss still waits for the vault.
            (ItemId.VaultCore, research, [(ItemId.AncientPowerCell, 2), (ItemId.PrecursorRelic, 1), (ItemId.LumeniteBar, 5)]),
            (ItemId.MoonEaterLure, chem, [(ItemId.ObsidianShard, 8), (ItemId.BioGel, 6)]),
            (ItemId.EclipseOrb, fab2, [(ItemId.VoidEssence, 4), (ItemId.LumeniteBar, 4)]),
            (ItemId.WarlordBanner, loom, [(ItemId.ClothRoll, 4), (ItemId.ScrapIronOre, 20)]),
            (ItemId.DreadnoughtFlag, loom, [(ItemId.ClothRoll, 6), (ItemId.ViridiumBar, 6)]),
            (ItemId.ProtocolKey, research, [(ItemId.CircuitBoard, 6), (ItemId.AncientPowerCell, 1)]),
            (ItemId.MothershipBeacon, voidForge, [(ItemId.VoidsteelBar, 8), (ItemId.AncientPowerCell, 2)]),
            (ItemId.ScrapBeacon, fab, [(ItemId.ScrapIronOre, 15), (ItemId.PowerCell, 1)]),
            (ItemId.SummonPirateTransmitter, alloy, [(ItemId.CircuitBoard, 4), (ItemId.ViridiumBar, 4)]),
            (ItemId.SummonSignalCore, research, [(ItemId.CircuitBoard, 3), (ItemId.PowerCell, 3), (ItemId.CobaltBar, 4)]),
            (ItemId.FestivalFlare, fab, [(ItemId.Berry, 5), (ItemId.EnergyCrystal, 1)])
        ];
        foreach (var (output, station, inputs) in summons)
            Add(Key(output), output, 1, At(station), inputs);
        HardmodeContent.RegisterRecipes((id, output, count, stations, ingredients) => Add(id, output, count, stations, ingredients));
        LeisureContent.RegisterRecipes((id, output, count, stations, ingredients) => Add(id, output, count, stations, ingredients));

        // ---- Fishing ----
        Add(Key(ItemId.FishingRod), ItemId.FishingRod, 1, At(field), (ItemId.WoodBlock, 6), (ItemId.RopeCoil, 1));
        (ItemId Rod, int Tier)[] rods =
        [
            (ItemId.FerriteFishingRod, 1), (ItemId.CobaltFishingRod, 2), (ItemId.GlacialFishingRod, 3), (ItemId.ViridiumFishingRod, 4),
            (ItemId.LumeniteFishingRod, 5), (ItemId.VoidsteelFishingRod, 6), (ItemId.AstralFishingRod, 7)
        ];
        foreach (var (rod, tier) in rods)
            Add(Key(rod), rod, 1, At(TierStations[tier]), (BarFor(items[rod] with { Tier = tier }), 8), (ItemId.RopeCoil, 2));
        Add(Key(ItemId.AcidFishingRod), ItemId.AcidFishingRod, 1, At(forge2), (ItemId.UraniumRod, 8), (ItemId.CoralShard, 6));
        Add(Key(ItemId.MagmaFishingRod), ItemId.MagmaFishingRod, 1, At(forge2), (ItemId.PyrociteBar, 10), (ItemId.ObsidianShard, 8));
        Add(Key(ItemId.FishingLine), ItemId.FishingLine, 1, At(tinker), (ItemId.ClothRoll, 4), (ItemId.SilkThread, 10));
        Add(Key(ItemId.FishingLure), ItemId.FishingLure, 1, At(tinker), (ItemId.GlassBlock, 2), (ItemId.GlowSpore, 4), (ItemId.CobaltBar, 2));
        Add(Key(ItemId.AcidWader), ItemId.AcidWader, 1, At(tinker), (ItemId.UraniumRod, 6), (ItemId.ClothRoll, 4));
        Add(Key(ItemId.LavaLure), ItemId.LavaLure, 1, At(tinker), (ItemId.PyrociteBar, 6), (ItemId.ObsidianShard, 6));
        Add(Key(ItemId.CatchScanner), ItemId.CatchScanner, 1, At(tinker), (ItemId.CircuitBoard, 2), (ItemId.GlassBlock, 2));
        // PC-3: flight essence (after the Fracture) instead of void essence, which coastal hab pods hold before it.
        Add(Key(ItemId.AbyssBait), ItemId.AbyssBait, 1, At(bio), (ItemId.AcidEel, 3), (ItemId.FlightEssence, 1));
        AddAlt(Key(ItemId.CookedFish), ItemId.CookedFish, 1, cook,
            [ItemId.TideFish, ItemId.FishSilver, ItemId.FrostCod, ItemId.ReefSnapper, ItemId.MudSkipper, ItemId.CaveBlindfish,
             ItemId.SporeGuppy, ItemId.BlightCarp, ItemId.NectarMinnow, ItemId.PrismTrout, ItemId.FishGlow], 1);
        AddAlt(Key(ItemId.CookedEel), ItemId.CookedEel, 1, cook, [ItemId.AcidEel], 1);
        AddAlt(Key(ItemId.GrilledKoi), ItemId.GrilledKoi, 1, cook, [ItemId.EmberKoi], 1);
        AddAlt(Key(ItemId.FishFillet), ItemId.FishFillet, 2, cook, [ItemId.TideFish, ItemId.FishSilver, ItemId.ReefSnapper, ItemId.FrostCod], 1);
    }

    /// <summary>The bar an item is forged from: the tier bar, except tier 3 picks by material.</summary>
    public static ItemId BarFor(ItemDefinition item)
    {
        if (item.Tier != 3)
            return ContentItems.TierBars[item.Tier];
        var name = item.Id.ToString();
        if (name.Contains("Glacial") || name.Contains("Frost"))
            return ItemId.GlacialBar;
        if (name.Contains("Uranium") || name.Contains("Rad"))
            return ItemId.UraniumRod;
        return ItemId.PyrociteBar;
    }

    private static (ItemId, int) ExtraFor(ItemDefinition item, int tier) => tier switch
    {
        1 => (ItemId.WoodBlock, 4),
        2 => (ItemId.EnergyCrystal, 2),
        3 => (BarFor(item) switch
        {
            ItemId.GlacialBar => ItemId.FrostCrystal,
            ItemId.UraniumRod => ItemId.CausticShard,
            _ => ItemId.ObsidianShard
        }, 2),
        4 => (ItemId.EnergyCrystal, 4),
        5 => (ItemId.PrismShard, 3),
        6 => (ItemId.VoidEssence, 2),
        _ => (ItemId.AstralOre, 2)
    };
}
