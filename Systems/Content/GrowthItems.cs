using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// One-use permanent upgrades (docs/PROGRESSION_DIFF.md G9), stored as a bit field in the character file.
/// Append-only: the numeric bits are saved.
/// </summary>
[Flags]
public enum PermanentUpgrade : ushort
{
    None = 0,
    /// <summary>+4 defense.</summary>
    NaniteMarrow = 1 << 0,
    /// <summary>Faster Charge regeneration.</summary>
    CoolantPearl = 1 << 1,
    /// <summary>A little permanent luck (+0.1, see <see cref="GameSimulation.Luck"/>).</summary>
    FortuneLattice = 1 << 2,
    /// <summary>+fishing power.</summary>
    BaitGland = 1 << 3,
    /// <summary>+mining speed.</summary>
    ServoGrease = 1 << 4,
    /// <summary>Crafting stations count from further away.</summary>
    FabricatorManual = 1 << 5,
    /// <summary>Biome-tinted personal light plus a slightly larger light radius (renderer reads the flag).</summary>
    LumenAccord = 1 << 6
}

/// <summary>Grappling hook data (G10): how far it flies, how fast it flies and reels, and how many hooks it carries.</summary>
public sealed record HookSpec(ItemId Item, string Name, float Range, float LaunchSpeed, float ReelSpeed, int HookCount, bool InstantRetract);

/// <summary>Bonuses the character carries regardless of gear (lifebloom pods and permanent upgrades).</summary>
public readonly record struct PermanentStatBonus(float MaximumLife, int Defense, float ManaRegenerationPerHour);

/// <summary>
/// Character growth content (docs/PROGRESSION_DIFF.md G3, G7-G11): lifebloom pods, the auxiliary socket, permanent
/// upgrades, the hook ladder and the salvaged thruster fins. All names and numbers are original.
/// </summary>
public static class GrowthItems
{
    public const int LifebloomBonus = 5;
    public const int MaximumLifeblooms = 20;
    /// <summary>Lifebloom growth chance per random update on a valid underground canopy ground (1 in N).</summary>
    public const int LifebloomGrowthOdds = 30;

    public static readonly IReadOnlyDictionary<PermanentUpgrade, ItemId> UpgradeItems = new Dictionary<PermanentUpgrade, ItemId>
    {
        [PermanentUpgrade.NaniteMarrow] = ItemId.NaniteMarrow,
        [PermanentUpgrade.CoolantPearl] = ItemId.CoolantPearl,
        [PermanentUpgrade.FortuneLattice] = ItemId.FortuneLattice,
        [PermanentUpgrade.BaitGland] = ItemId.BaitGland,
        [PermanentUpgrade.ServoGrease] = ItemId.ServoGrease,
        [PermanentUpgrade.FabricatorManual] = ItemId.FabricatorManual,
        [PermanentUpgrade.LumenAccord] = ItemId.LumenAccord
    };

    /// <summary>Late bosses and the upgrade each one sheds (always on Brutal and Annihilation, a third of the time otherwise).</summary>
    public static readonly IReadOnlyDictionary<EnemyKind, ItemId> UpgradeBosses = new Dictionary<EnemyKind, ItemId>
    {
        [EnemyKind.VoidStalker] = ItemId.NaniteMarrow,
        [EnemyKind.PrismEmpress] = ItemId.CoolantPearl,
        [EnemyKind.EclipseLord] = ItemId.FortuneLattice,
        [EnemyKind.PirateDreadnought] = ItemId.BaitGland,
        [EnemyKind.ProtocolDragon] = ItemId.ServoGrease,
        [EnemyKind.MothershipCore] = ItemId.FabricatorManual,
        [EnemyKind.SolarFlareEntity] = ItemId.LumenAccord
    };

    /// <summary>The hook ladder, weakest first. The Mag-grapple is the original grapple hook.</summary>
    public static readonly IReadOnlyList<HookSpec> HookLadder =
    [
        new(ItemId.ScrapTether, "Scrap tether", 10f, 20f, 11f, 1, false),
        new(ItemId.GrappleHook, "Mag-grapple", GameSimulation.GrappleRange, GameSimulation.GrappleSpeed, GameSimulation.GrappleReelSpeed, 1, false),
        new(ItemId.TwinLash, "Twin lash", 20f, 32f, 18f, 2, false),
        new(ItemId.PhaseTether, "Phase tether", 28f, 90f, 26f, 1, true)
    ];

    public static HookSpec? HookFor(ItemId item) => HookLadder.FirstOrDefault(spec => spec.Item == item);

    private static HashSet<string> Tags(params string[] tags) => new(tags, StringComparer.OrdinalIgnoreCase);

    public static void RegisterAll(Dictionary<ItemId, ItemDefinition> items)
    {
        void Add(ItemDefinition definition) => items[definition.Id] = definition;
        ItemDefinition OneUse(ItemId id, string name, int tier, int value) =>
            new(id, name, ItemUseKind.Consumable, 99) { Tier = tier, Rarity = ContentItems.RarityFor(tier), Value = value };

        Add(OneUse(ItemId.LifebloomPod, "Lifebloom pod", 5, 200));
        Add(OneUse(ItemId.AuxiliarySocket, "Auxiliary socket", 5, 600));
        Add(OneUse(ItemId.NaniteMarrow, "Nanite marrow", 6, 500));
        Add(OneUse(ItemId.CoolantPearl, "Coolant pearl", 5, 500));
        Add(OneUse(ItemId.FortuneLattice, "Fortune lattice", 6, 500));
        Add(OneUse(ItemId.BaitGland, "Bait gland", 5, 400));
        Add(OneUse(ItemId.ServoGrease, "Servo grease", 6, 400));
        Add(OneUse(ItemId.FabricatorManual, "Fabricator manual", 6, 400));
        Add(OneUse(ItemId.LumenAccord, "Lumen accord", 5, 400));

        // The one rare early pair that really climbs (a chest find), weaker than any post-Fracture wings.
        Add(new ItemDefinition(ItemId.SalvagedThrusterFins, "Salvaged thruster fins", ItemUseKind.Equipment, 1)
        {
            EquipmentSlot = global::Rimtera.Systems.EquipmentSlot.Accessory, Tier = 2, Rarity = ContentItems.RarityFor(3), Value = 300,
            WingFlightSeconds = 0.7f, MoveSpeedMultiplier = 1.02f, ImmunityTags = Tags("wings", "glide", "featherfall", "wing-climb")
        });

        foreach (var spec in HookLadder.Where(spec => spec.Item != ItemId.GrappleHook))
        {
            var tier = spec.Item switch { ItemId.ScrapTether => 1, ItemId.TwinLash => 4, _ => 7 };
            Add(new ItemDefinition(spec.Item, spec.Name, ItemUseKind.Equipment, 1)
            {
                EquipmentSlot = global::Rimtera.Systems.EquipmentSlot.Accessory, Tier = tier, Rarity = ContentItems.RarityFor(tier),
                Value = 40 + tier * tier * 30, ImmunityTags = Tags("grapple")
            });
        }
    }

    public static void RegisterRecipes(CraftingSystem crafting)
    {
        void Recipe(string id, ItemId output, CraftingStationId station, params (ItemId Item, int Count)[] ingredients) =>
            crafting.Register(new RecipeDefinition("gr-" + id, output, 1,
                ingredients.Select(entry => IngredientRequirement.Exact(entry.Item, entry.Count)).ToArray(),
                new HashSet<CraftingStationId> { station }));
        Recipe("scrap-tether", ItemId.ScrapTether, CraftingStationId.Workbench, (ItemId.ScrapIronOre, 12), (ItemId.RopeCoil, 2));
        // Post-Fracture: viridium needs the Alloy Anvil, which needs the Fracture's ores.
        Recipe("twin-lash", ItemId.TwinLash, ContentRecipes.TierStations[4], (ItemId.ViridiumBar, 10), (ItemId.GrappleHook, 1), (ItemId.FlightEssence, 4));
        Recipe("phase-tether", ItemId.PhaseTether, ContentRecipes.TierStations[7], (ItemId.AstralBar, 8), (ItemId.TwinLash, 1), (ItemId.VoidEssence, 6));
    }

    /// <summary>The lifebloom pod plant tile (a harvestable plant that drops the pod).</summary>
    public static IEnumerable<ContentTileDefinition> TileDefinitions()
    {
        yield return new(TileId.LifebloomPod, "Lifebloom pod", false, 4, 0.4f, 46, null,
            TileTraits.Plant | TileTraits.Organic | TileTraits.Light, Furniture: true, Value: 1, Drop: ItemId.LifebloomPod);
    }

    private static readonly Random LifebloomRandom = new(0x11FEB1);

    /// <summary>
    /// After the Fracture, underground Verdant Canopy ground (mud, canopy or jungle grass below the surface band) rarely
    /// sprouts a lifebloom pod on its open top face, never within 10 columns of another pod. True when one grew.
    /// </summary>
    public static bool TryGrowLifebloom(TileGrid world, int x, int y, TileId tile, ICollection<GridPoint> changed)
    {
        if (tile is not (TileId.Mud or TileId.CanopyGrass or TileId.JungleGrass) || y < world.Height * 0.45f || y < 1)
            return false;
        if (world.TileAt(x, y - 1, TileId.Bedrock) != TileId.Air || world.GetCell(x, y - 1).LiquidUnits > 0)
            return false;
        if (LifebloomRandom.Next(LifebloomGrowthOdds) != 0)
            return false;
        for (var dx = -10; dx <= 10; dx++)
        for (var dy = -3; dy <= 3; dy++)
            if (world.TileAt(x + dx, y - 1 + dy, TileId.Air) == TileId.LifebloomPod)
                return false;
        var point = new GridPoint(x, y - 1);
        world.SetTile(point, TileId.LifebloomPod);
        changed.Add(point);
        return true;
    }
}

/// <summary>Character growth state and effects (lifebloom pods, the sixth slot, permanent upgrades, creative kit).</summary>
public sealed partial class GameSimulation
{
    private Random? _growthRandomStream;
    private Random GrowthRandom => _growthRandomStream ??= new Random(Seed ^ 0x6A0B10);
    private GrappleHookState? _grappleSpare;

    /// <summary>Lifebloom pods consumed (each +<see cref="GrowthItems.LifebloomBonus"/> maximum life, up to 20). Saved with the character.</summary>
    public int LifebloomPodsUsed { get; internal set; }
    /// <summary>The auxiliary socket was used: the sixth accessory slot is open in every world. Saved with the character.</summary>
    public bool AuxSocketUsed { get; internal set; }
    /// <summary>One-use upgrades the character has absorbed. Saved with the character.</summary>
    public PermanentUpgrade PermanentUpgrades { get; internal set; }
    /// <summary>The Creative colony supply drop has been handed out (once per character).</summary>
    public bool CreativeKitGranted { get; internal set; }

    /// <summary>True when <see cref="EquipmentSlot.Accessory6"/> may be used (auxiliary socket, or an Annihilation world).</summary>
    public bool AccessorySlot6Unlocked => AuxSocketUsed || Difficulty == WorldDifficulty.Annihilation;
    /// <summary>The accessory slots usable right now, in fill order (5, or 6 when unlocked).</summary>
    public IReadOnlyList<EquipmentSlot> ActiveAccessorySlots => Player.Equipment.ActiveAccessorySlots;

    public bool HasUpgrade(PermanentUpgrade upgrade) => (PermanentUpgrades & upgrade) == upgrade && upgrade != PermanentUpgrade.None;
    /// <summary>Lumen accord absorbed: the renderer tints the player's light by biome (<see cref="LumenAccordTint"/>).</summary>
    public bool LumenAccordActive => HasUpgrade(PermanentUpgrade.LumenAccord);
    /// <summary>Light colour for the Lumen accord in the current biome, or null without it.</summary>
    public (byte R, byte G, byte B)? LumenAccordTint => !LumenAccordActive ? null : CurrentBiome.Zone switch
    {
        BiomeZone.FrozenWasteland => (170, 220, 255),
        BiomeZone.JunkWasteland => (255, 200, 120),
        BiomeZone.RadiatedDeeps => (150, 255, 120),
        BiomeZone.MoltenChasm => (255, 140, 80),
        BiomeZone.Overgrowth => (200, 120, 255),
        BiomeZone.AcidCoast => (120, 255, 220),
        BiomeZone.FungalHollows => (110, 160, 255),
        BiomeZone.VerdantCanopy => (170, 255, 140),
        BiomeZone.BloomHives => (255, 220, 110),
        BiomeZone.CrystalSpires => (255, 170, 240),
        _ => (255, 240, 210)
    };
    /// <summary>Luck added by the fortune lattice (on the same −1..+1 scale as <see cref="Luck"/>).</summary>
    public float GrowthLuckBonus => HasUpgrade(PermanentUpgrade.FortuneLattice) ? 0.1f : 0f;
    /// <summary>Extra tiles at which crafting stations count (fabricator manual).</summary>
    public float CraftingStationReachBonus => HasUpgrade(PermanentUpgrade.FabricatorManual) ? 4f : 0f;
    /// <summary>The grappling hook data of the best equipped hook (null without one).</summary>
    public HookSpec? ActiveHook
    {
        get
        {
            // The hook slot comes first, as in Terraria; otherwise the best hook worn as an accessory.
            if (MiscEquipped(MiscSlot.Hook) is { } hooked && GrowthItems.HookFor(hooked) is { } equipped)
                return equipped;
            HookSpec? best = null;
            foreach (var slot in Player.Equipment.ActiveAccessorySlots)
                if (Player.Equipment[slot] is { } id && Items.Contains(id) && Items[id].ImmunityTags.Contains("grapple"))
                {
                    var spec = GrowthItems.HookFor(id) ?? GrowthItems.HookFor(ItemId.GrappleHook)!;
                    if (best is null || spec.Range > best.Range)
                        best = spec;
                }
            return best;
        }
    }
    /// <summary>A grappling hook is equipped: in the hook slot or worn as an accessory.</summary>
    public bool HasGrapple => MiscEquipped(MiscSlot.Hook) is not null || Player.Stats.ImmunityTags.Contains("grapple");

    /// <summary>True when the equipped wings can climb (glide rigs only slow the fall).</summary>
    public bool WingClimbAvailable => GearWingFlightSeconds > 0f;

    /// <summary>Numeric gear-tag values the permanent upgrades add (read by <see cref="GearTagValue"/>).</summary>
    internal float GrowthTagValue(string key) => PermanentUpgrades == PermanentUpgrade.None ? 0f : key switch
    {
        "fish" => HasUpgrade(PermanentUpgrade.BaitGland) ? 10f : 0f,
        "mining" => HasUpgrade(PermanentUpgrade.ServoGrease) ? 15f : 0f,
        "light" => HasUpgrade(PermanentUpgrade.LumenAccord) ? 1.5f : 0f,
        _ => 0f
    };

    /// <summary>Wires the equipment loadout to the growth state (called once from the constructor).</summary>
    private void AttachGrowth()
    {
        Player.Equipment.Slot6Gate = () => AccessorySlot6Unlocked;
        Player.Equipment.PermanentBonuses = () => new PermanentStatBonus(
            LifebloomPodsUsed * GrowthItems.LifebloomBonus,
            HasUpgrade(PermanentUpgrade.NaniteMarrow) ? 4 : 0,
            HasUpgrade(PermanentUpgrade.CoolantPearl) ? 0.5f : 0f);
    }

    /// <summary>Uses a growth item (lifebloom pod, auxiliary socket, permanent upgrade). True when the item is one of them.</summary>
    private bool TryUseGrowthItem(ItemDefinition item)
    {
        bool used;
        if (item.Id == ItemId.LifebloomPod)
            used = UseLifebloomPod();
        else if (item.Id == ItemId.AuxiliarySocket)
            used = UseAuxiliarySocket();
        else if (GrowthItems.UpgradeItems.FirstOrDefault(pair => pair.Value == item.Id) is { Key: not PermanentUpgrade.None } entry)
            used = UsePermanentUpgrade(entry.Key);
        else
            return false;
        if (used)
            SetPlayerCooldown(6);
        return true;
    }

    /// <summary>Consumes a lifebloom pod for +5 maximum life (after all ten life cores, up to twenty pods).</summary>
    public bool UseLifebloomPod()
    {
        if (Player.LifeCoresUsed < PlayerCharacter.MaximumLifeCores)
        {
            PostMessage("The pod withers in your hand: your body needs every life core first.");
            return false;
        }
        if (LifebloomPodsUsed >= GrowthItems.MaximumLifeblooms)
        {
            PostMessage("Your body cannot take any more lifebloom.");
            return false;
        }
        if (!Player.Inventory.TryConsume(ItemId.LifebloomPod))
            return false;
        LifebloomPodsUsed++;
        Player.Heal(GrowthItems.LifebloomBonus);
        PostMessage($"The lifebloom takes root in you. Maximum health is now {Player.MaximumHitPoints:0}.");
        return true;
    }

    /// <summary>Installs the auxiliary socket: the sixth accessory slot opens for this character.</summary>
    public bool UseAuxiliarySocket()
    {
        if (AuxSocketUsed)
        {
            PostMessage("An auxiliary socket is already installed.");
            return false;
        }
        if (!Player.Inventory.TryConsume(ItemId.AuxiliarySocket))
            return false;
        AuxSocketUsed = true;
        PostMessage("The auxiliary socket locks in. A sixth accessory slot opens.");
        return true;
    }

    /// <summary>Absorbs a one-use upgrade item (false when already absorbed or not carried).</summary>
    public bool UsePermanentUpgrade(PermanentUpgrade upgrade)
    {
        if (!GrowthItems.UpgradeItems.TryGetValue(upgrade, out var item))
            return false;
        if (HasUpgrade(upgrade))
        {
            PostMessage($"You have already absorbed a {Items[item].Name.ToLowerInvariant()}.");
            return false;
        }
        if (!Player.Inventory.TryConsume(item))
            return false;
        PermanentUpgrades |= upgrade;
        PostMessage($"The {Items[item].Name.ToLowerInvariant()} becomes part of you.");
        return true;
    }

    /// <summary>Hands a Creative character the colony supply drop once, on first world entry.</summary>
    private void GrantCreativeKitOnce()
    {
        if (CreativeKitGranted || PlayerProfile.Difficulty != CharacterDifficulty.Creative)
            return;
        CreativeKitGranted = true;
        var kitRandom = new Random(Seed ^ 0x57A27F);
        foreach (var (item, count, calibrated) in StarterKits.Creative)
            if (Items.Contains(item))
                CollectOrDrop(item, count, PlayerCenter, calibrated ? StarterKits.Roll(kitRandom, Items[item]) : Calibration.None);
        // Like the Journey kit, the wings start worn in the first accessory slot.
        if (Player.Inventory.FindSlot(StarterKits.CreativeWornWings) is var wings and >= 0)
            TryEquipFromInventory(wings);
        if (Player.Inventory.FindSlot(ItemId.CobaltStiletto) is var sword and >= 0)
            Player.Inventory.Select(sword);
        // PC-14: the drone uplink's drone is already out on first entry.
        foreach (var (item, _, _) in StarterKits.Creative)
            if (Items.Contains(item) && Items[item].Summons is { } minion && !MinionCatalog.Get(minion).IsSentry &&
                Player.Inventory.Count(item) > 0 && ActiveDrones == 0)
                SummonMinion(Items[item], minion, RegionMap.CellAt(PlayerCenter));
        PostMessage("A colony supply drop lands beside you.");
    }

    /// <summary>Growth drops: the auxiliary socket (Fission Tyrant, Brutal and up) and upgrades from late bosses.</summary>
    private void OnCreatureDefeatedGrowth(EnemyAgent enemy)
    {
        var center = enemy.Motion.Position + enemy.Motion.Size / 2f;
        var hard = Difficulty >= WorldDifficulty.Brutal;
        if (!enemy.IsBoss)
            return;
        if (enemy.Kind == EnemyKind.FissionTyrant && hard)
        {
            SpawnGroundItem(ItemId.AuxiliarySocket, 1, center + new Vector2(0.4f, 0f), pickupDelay: 1f);
            PostMessage("Something glints in the Tyrant's wreckage: an auxiliary socket.");
        }
        if (GrowthItems.UpgradeBosses.TryGetValue(enemy.Kind, out var upgrade) && (hard || GrowthRandom.Next(3) == 0))
            SpawnGroundItem(upgrade, 1, center + new Vector2(-0.4f, 0f), pickupDelay: 1f);
    }

    /// <summary>Generated underground chests rarely hold the salvaged thruster fins (seeded per chest).</summary>
    private void GrowthChestLoot(GridPoint cell, Inventory inventory)
    {
        if ((Features?.ChestTier(cell) ?? -1) is < 1 or > 3)
            return;
        var roll = new Random(unchecked(Seed * 31 + cell.X * 7919 + cell.Y * 104729)).Next(18);
        if (roll == 0)
            inventory.Add(ItemId.SalvagedThrusterFins, 1);
    }

    /// <summary>Equipped hook data for the grapple code (the Mag-grapple defaults when nothing better is worn).</summary>
    private HookSpec CurrentHook => ActiveHook ?? GrowthItems.HookFor(ItemId.GrappleHook)!;
}
