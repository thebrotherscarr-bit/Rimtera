using System.Numerics;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// The station AI powers the base (0.3.8, docs/VISION.md §3.2; the power grid, 0.7.5). The AI holds a power reserve and a
/// power tier. Every station crafts unpowered (0.7.5): power adds bonuses rather than permission. A machine runs powered
/// when the AI's tier reaches the machine's and the reserve holds its tier in power; powered, the people who work it work
/// faster, and each craft draws its tier. The AI is charged with fuel crystals: crude ones from junk at the Nano-Fabricator,
/// refined ones of each tier from the fuel refiner, which turns tiered junk and ore into crystals of the ore's tier (more
/// from better input). Feeding the AI a tier's crystal raises its tier to that. Hand-built stations need no power. Losing
/// the AI no longer ends the run: the machines run unpowered and research stops until an AI core rebuilds it.
/// </summary>
public sealed partial class GameSimulation
{
    public const string PowerFlag = "ai.power", PowerTierFlag = "ai.tier", AiDownFlag = "ai.down", PowerInitFlag = "ai.power.init";
    /// <summary>The reserve's ceiling, what a new world starts with, and what a rebuilt AI wakes with.</summary>
    public const int MaxPower = 1000, StartPower = 300, RebuiltPower = 60;
    /// <summary>Power a second while the AI studies (research costs power too).</summary>
    public const float StudyPowerPerSecond = 0.02f;

    /// <summary>
    /// The fuels (0.7.5: fuel crystals), by tier: what each unit adds to the reserve, and the power tier it raises the AI to
    /// (crude crystals add power but raise no tier).
    /// </summary>
    public static IReadOnlyList<(ItemId Fuel, int Energy, int Tier)> PowerFuels { get; } =
    [
        (ItemId.CrudeFuelCrystal, 10, 0),
        (ItemId.FuelCrystalT1, 25, 1),
        (ItemId.FuelCrystalT2, 40, 2),
        (ItemId.FuelCrystalT3, 60, 3),
        (ItemId.FuelCrystalT4, 90, 4),
        (ItemId.FuelCrystalT5, 140, 5)
    ];

    /// <summary>
    /// What the fuel refiner eats (0.7.5): tiered junk and ore, matching the ore ladder. Each makes crystals of its tier (0 is
    /// crude); better input makes more: ore, then bars and rods, then the tier's crystals and essences.
    /// </summary>
    public static IReadOnlyList<(ItemId Input, int Count, int Tier, int Made)> RefinerInputs { get; } =
    [
        (ItemId.WoodBlock, 3, 0, 1), (ItemId.BioGel, 1, 0, 1), (ItemId.RockBlock, 12, 0, 1),
        (ItemId.CopperOre, 3, 1, 1), (ItemId.ScrapIronOre, 3, 1, 1), (ItemId.FerriteBar, 1, 1, 2), (ItemId.EnergyCrystal, 1, 1, 3),
        (ItemId.CobaltOre, 3, 2, 1), (ItemId.CobaltBar, 1, 2, 2), (ItemId.PowerCell, 1, 2, 3),
        (ItemId.GlacialOre, 3, 3, 1), (ItemId.PyrociteOre, 3, 3, 1), (ItemId.MeteoriteOre, 3, 3, 1), (ItemId.UraniumOre, 2, 3, 1),
        (ItemId.GlacialBar, 1, 3, 2), (ItemId.PyrociteBar, 1, 3, 2), (ItemId.UraniumRod, 1, 3, 3),
        (ItemId.ViridiumOre, 3, 4, 1), (ItemId.LumeniteOre, 3, 4, 1), (ItemId.ViridiumBar, 1, 4, 2), (ItemId.LumeniteBar, 1, 4, 2),
        (ItemId.VoidEssence, 1, 4, 3),
        (ItemId.VoidsteelOre, 3, 5, 1), (ItemId.AstralOre, 2, 5, 1), (ItemId.VoidsteelBar, 1, 5, 2), (ItemId.AstralBar, 1, 5, 3)
    ];

    /// <summary>How much faster people work a powered machine (0.7.5; skill affinity comes in 0.7.7).</summary>
    public const float PoweredWorkSpeed = 1.25f;

    /// <summary>The power tier a station needs (0: hand-built, needs no power).</summary>
    public static int PowerTierOf(CraftingStationId station) => station switch
    {
        CraftingStationId.FieldWorkbench or CraftingStationId.Sawmill or CraftingStationId.Loom or CraftingStationId.Kiln or
            CraftingStationId.CookingStation or CraftingStationId.DyeVat or CraftingStationId.FuelRefiner or CraftingStationId.None => 0,
        CraftingStationId.PowerForgeMk2 or CraftingStationId.OreRefineryMk2 => 2,
        CraftingStationId.AlloyAnvil => 3,
        CraftingStationId.NanoFabricatorMk2 or CraftingStationId.VoidForge => 4,
        CraftingStationId.AstralAssembler => 5,
        _ => 1
    };

    /// <summary>The AI's reserve (0 to <see cref="MaxPower"/>). Set up on first use (<see cref="InitPower"/>).</summary>
    public int StationPower
    {
        get
        {
            InitPower();
            return (int)Flags.Get(PowerFlag);
        }
    }

    /// <summary>The highest station tier the AI can power (0 to 5; 0 until it is fed a refined crystal).</summary>
    public int StationPowerTier
    {
        get
        {
            InitPower();
            return (int)Math.Max(0, Flags.Get(PowerTierFlag) - TierBase);
        }
    }

    // 0.7.5: the tier flag is stored one up (a tier of 0 must still read as set); worlds from before keep their tier.
    private const string TierBaseFlag = "ai.tier.base";
    private int TierBase => Flags.IsSet(TierBaseFlag) ? 1 : 0;
    /// <summary>False while the AI is destroyed and not yet rebuilt.</summary>
    public bool StationAiOnline => !Flags.IsSet(AiDownFlag);

    private void SetPower(int value)
    {
        Flags.Clear(PowerFlag);
        var clamped = Math.Clamp(value, 0, MaxPower);
        if (clamped > 0)
            Flags.Increment(PowerFlag, clamped);
    }

    private void SetPowerTier(int tier)
    {
        Flags.Set(TierBaseFlag);
        Flags.Clear(PowerTierFlag);
        Flags.Increment(PowerTierFlag, Math.Clamp(tier, 0, 5) + 1);
    }

    /// <summary>
    /// A world's first tick: a new world starts with <see cref="StartPower"/> at tier 1. A world saved before 0.3.8 starts
    /// full, at the tier its progress already uses, so nothing it could craft goes dark.
    /// </summary>
    private void InitPower()
    {
        if (Flags.IsSet(PowerInitFlag))
            return;
        Flags.Set(PowerInitFlag); // first, so the accessors below do not recurse
        var progressed = _defeatedBosses.Count > 0 || FractureActive;
        SetPower(progressed ? MaxPower : StartPower);
        // 0.7.5: a new world's AI runs on its landing charge at tier 0 until it is fed a refined crystal.
        var tier = progressed ? 1 : 0;
        foreach (var research in ResearchTiers)
            if (ResearchOpen(research))
                tier = research.Id switch { "overseer" or "scout" => Math.Max(tier, 2), "fission" => Math.Max(tier, 3), "essences" or "flora" => Math.Max(tier, 4), "astral" => 5, _ => tier };
        if (FractureActive)
            tier = Math.Max(tier, 3);
        SetPowerTier(tier);
    }

    /// <summary>The power a recipe draws: the highest tier among its powered stations (0 when none need power).</summary>
    public static int PowerCost(RecipeDefinition recipe) =>
        (recipe.RequiredStations ?? new HashSet<CraftingStationId>()).Select(PowerTierOf).DefaultIfEmpty(0).Max();

    /// <summary>
    /// 0.7.5: why a recipe's machines run unpowered, or null when they run powered or need no power. It no longer gates
    /// crafting (every station crafts unpowered); it explains what powering them would take.
    /// </summary>
    public string? PowerLock(RecipeDefinition recipe)
    {
        var need = PowerCost(recipe);
        if (need == 0)
            return null;
        if (!StationAiOnline)
            return "The station AI is down, so the machines run unpowered. Build an AI core to bring it back.";
        if (StationPowerTier < need)
            return $"This machine runs powered on tier {need} power. Feed the station AI {Items[PowerFuels.First(fuel => fuel.Tier == need).Fuel].Name.ToLowerInvariant()}s.";
        if (StationPower < need)
            return "The station AI is out of power, so the machines run unpowered. Feed it fuel crystals.";
        return null;
    }

    /// <summary>Whether a station runs powered now (0.7.5): a machine, with the AI up, at its tier, with power to spare.</summary>
    public bool StationPowered(CraftingStationId station)
    {
        var need = PowerTierOf(station);
        return need > 0 && StationAiOnline && StationPowerTier >= need && StationPower >= need;
    }

    /// <summary>Draws a finished craft's power from the reserve when its machines ran powered.</summary>
    private void SpendCraftPower(RecipeDefinition recipe)
    {
        var cost = PowerCost(recipe);
        if (cost <= 0 || PowerLock(recipe) is not null)
            return;
        var before = StationPower;
        SetPower(before - cost);
        if (before >= MaxPower / 10 && StationPower < MaxPower / 10)
            PostMessage($"The station AI's power is low ({StationPower}/{MaxPower}). Feed it fuel.");
        else if (before > 0 && StationPower == 0)
            PostMessage("The station AI is out of power. The machines run unpowered.");
    }

    /// <summary>
    /// Feeds the AI from the player's inventory, best fuel first, until the reserve is full. Returns what it took, for the
    /// talk menu ("" when nothing was fed).
    /// </summary>
    public string FeedStationAi()
    {
        if (!StationAiOnline)
            return "The station AI is down. Build an AI core to bring it back first.";
        var fed = new List<string>();
        var tierBefore = StationPowerTier;
        foreach (var (fuel, energy, tier) in PowerFuels.OrderByDescending(entry => entry.Tier))
        {
            var room = (MaxPower - StationPower) / energy;
            var take = Math.Min(room, Player.Inventory.Count(fuel));
            // The best fuel always raises the tier, even into a full reserve: one unit is enough.
            if (take == 0 && tier > StationPowerTier && Player.Inventory.Count(fuel) > 0)
                take = 1;
            if (take <= 0 || !Player.Inventory.TryConsume(fuel, take))
                continue;
            SetPower(StationPower + take * energy);
            if (tier > StationPowerTier)
                SetPowerTier(tier);
            fed.Add($"{take} {Items[fuel].Name.ToLowerInvariant()}");
        }
        if (fed.Count == 0)
            return StationPower >= MaxPower ? "The power reserve is full." : "You carry no fuel. Make crude fuel crystals at the Nano-Fabricator, or refined ones at a fuel refiner.";
        var raised = StationPowerTier > tierBefore ? $" Power tier {StationPowerTier}: more machines run powered." : "";
        return $"Fed {string.Join(", ", fed)}. Power {StationPower}/{MaxPower}.{raised}";
    }

    /// <summary>The AI was destroyed (a setting where people die for good): the machines run unpowered until it is rebuilt.</summary>
    private void TakeStationAiDown(TownNpc npc)
    {
        Flags.Set(AiDownFlag);
        Record($"{npc.Name}, the station AI, was destroyed. The machines lost their power.");
        PostMessage($"{npc.Name}, the station AI, was destroyed! The machines run unpowered and research has stopped. Build an AI core to bring it back.");
    }

    /// <summary>Uses an AI core: the station AI comes back online with a little power, into a free home if there is one.</summary>
    public bool RebuildStationAi()
    {
        if (StationAiOnline)
            return false;
        Flags.Clear(AiDownFlag);
        Flags.Clear(LostFlag(TownNpcKind.StationAi));
        SetPower(Math.Max(StationPower, RebuiltPower));
        if (!_townNpcs.Any(npc => npc.Kind == TownNpcKind.StationAi && !npc.IsBound))
        {
            var ai = AddTownNpc(TownNpcKind.StationAi, Player.Motion.Position + new Vector2(2f, 0f), bound: false);
            ai.HomeCell = FindFreeHouse();
        }
        Record("The station AI was rebuilt from a new core.");
        PostMessage("The station AI boots from its new core. The machines hum back to life.");
        return true;
    }

    /// <summary>Sets the reserve and tier directly (the dev console).</summary>
    internal void SetStationPower(int? power = null, int? tier = null)
    {
        Flags.Set(PowerInitFlag);
        if (power is { } value)
            SetPower(value);
        if (tier is { } level)
            SetPowerTier(level);
    }
}
