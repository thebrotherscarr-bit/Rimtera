using System.Numerics;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// The station AI powers the base (0.3.8, docs/VISION.md §3.2). The AI holds a power reserve and a power tier. The
/// machines among the crafting stations need power from it: crafting at one costs its tier in power, and a station only
/// runs when the AI's tier reaches the station's. Feeding the AI fuel refills the reserve, and a better fuel raises the
/// tier, so the fuel climbs with progress: energy crystals, power cells, uranium, void essence, astral ore. Hand-built
/// stations need no power. Losing the AI no longer ends the run: the machines go dark and research stops until an AI
/// core rebuilds it.
/// </summary>
public sealed partial class GameSimulation
{
    public const string PowerFlag = "ai.power", PowerTierFlag = "ai.tier", AiDownFlag = "ai.down", PowerInitFlag = "ai.power.init";
    /// <summary>The reserve's ceiling, what a new world starts with, and what a rebuilt AI wakes with.</summary>
    public const int MaxPower = 1000, StartPower = 300, RebuiltPower = 60;
    /// <summary>Power a second while the AI studies (research costs power too).</summary>
    public const float StudyPowerPerSecond = 0.02f;

    /// <summary>The fuels, by tier: what each unit adds to the reserve, and the power tier it raises the AI to.</summary>
    public static IReadOnlyList<(ItemId Fuel, int Energy, int Tier)> PowerFuels { get; } =
    [
        (ItemId.EnergyCrystal, 20, 1),
        (ItemId.PowerCell, 60, 2),
        (ItemId.UraniumOre, 40, 3),
        (ItemId.VoidEssence, 120, 4),
        (ItemId.AstralOre, 200, 5)
    ];

    /// <summary>The power tier a station needs (0: hand-built, needs no power).</summary>
    public static int PowerTierOf(CraftingStationId station) => station switch
    {
        CraftingStationId.FieldWorkbench or CraftingStationId.Sawmill or CraftingStationId.Loom or CraftingStationId.Kiln or
            CraftingStationId.CookingStation or CraftingStationId.DyeVat or CraftingStationId.None => 0,
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

    /// <summary>The highest station tier the AI can power (1 to 5).</summary>
    public int StationPowerTier
    {
        get
        {
            InitPower();
            return (int)Math.Max(1, Flags.Get(PowerTierFlag));
        }
    }
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
        Flags.Clear(PowerTierFlag);
        Flags.Increment(PowerTierFlag, Math.Clamp(tier, 1, 5));
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
        var tier = 1;
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
    /// Why a recipe's stations are dark at <paramref name="at"/> (where the craft happens; the player by default), or null when
    /// they have power (the crafting gate).
    /// </summary>
    public string? PowerLock(RecipeDefinition recipe, Vector2? at = null)
    {
        var need = PowerCost(recipe);
        if (need == 0)
            return null;
        if (!StationAiOnline)
            return "The station AI is down, so the machines have no power. Build an AI core to bring it back.";
        if (StationPowerTier < need)
            return $"This station needs tier {need} power. Feed the station AI {Items[PowerFuels.First(fuel => fuel.Tier == need).Fuel].Name.ToLowerInvariant()}.";
        if (StationPower < need)
            return "The station AI is out of power. Feed it energy crystals or better fuel.";
        // 0.7.4: the power reaches the base and the staffed outposts, and an outpost only while the reserve holds.
        var reach = PowerReachAt(at ?? PlayerCenter);
        if (reach == PowerReach.None)
            return $"This machine is beyond the station AI's reach: it runs at the base or at a staffed outpost.";
        if (reach == PowerReach.Outpost && StationPower < OutpostMinimumPower)
            return $"The station AI's power is too low to reach the outposts (below {OutpostMinimumPower}). Feed it.";
        return null;
    }

    /// <summary>Where the station AI's power reaches (0.7.4).</summary>
    public enum PowerReach { Base, Outpost, None }

    /// <summary>The reserve below which the outposts go dark first (0.7.4).</summary>
    public const int OutpostMinimumPower = MaxPower / 10;
    /// <summary>The extra power a craft at an outpost draws (the long line out).</summary>
    public const int OutpostLinePower = 1;

    /// <summary>
    /// Whether the station AI's power reaches <paramref name="at"/> (0.7.4): the base (within the outpost distance of the
    /// station AI's home), a staffed outpost (within its spread), or nowhere. Until the AI has a home there is no grid yet,
    /// and machines run wherever they stand.
    /// </summary>
    public PowerReach PowerReachAt(Vector2 at)
    {
        if (!_townNpcs.Any(npc => npc.Kind == TownNpcKind.StationAi && !npc.IsBound && npc.HomeCell is not null))
            return PowerReach.Base;
        if (Vector2.Distance(at, BaseCenter) <= OutpostDistance)
            return PowerReach.Base;
        return Outposts.Any(outpost => outpost.People.Any(npc => !npc.IsPrisoner) && Vector2.Distance(outpost.Center, at) <= OutpostSpread)
            ? PowerReach.Outpost
            : PowerReach.None;
    }

    /// <summary>The power a craft of <paramref name="recipe"/> at <paramref name="at"/> draws.</summary>
    public int PowerCostAt(RecipeDefinition recipe, Vector2 at)
    {
        var cost = PowerCost(recipe);
        return cost > 0 && PowerReachAt(at) == PowerReach.Outpost ? cost + OutpostLinePower : cost;
    }

    /// <summary>Draws a finished craft's power from the reserve.</summary>
    private void SpendCraftPower(RecipeDefinition recipe, Vector2 at)
    {
        var cost = PowerCostAt(recipe, at);
        if (cost <= 0)
            return;
        var before = StationPower;
        SetPower(before - cost);
        if (before >= MaxPower / 10 && StationPower < MaxPower / 10)
            PostMessage($"The station AI's power is low ({StationPower}/{MaxPower}). Feed it fuel.");
        else if (before > 0 && StationPower == 0)
            PostMessage("The station AI is out of power. The machines are dark.");
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
            return StationPower >= MaxPower ? "The power reserve is full." : "You carry no fuel. Energy crystals, power cells, uranium, void essence and astral ore all work.";
        var raised = StationPowerTier > tierBefore ? $" Power tier {StationPowerTier}: more machines can run." : "";
        return $"Fed {string.Join(", ", fed)}. Power {StationPower}/{MaxPower}.{raised}";
    }

    /// <summary>The AI was destroyed (a setting where people die for good): the machines go dark until it is rebuilt.</summary>
    private void TakeStationAiDown(TownNpc npc)
    {
        Flags.Set(AiDownFlag);
        Record($"{npc.Name}, the station AI, was destroyed. The machines went dark.");
        PostMessage($"{npc.Name}, the station AI, was destroyed! The machines are dark and research has stopped. Build an AI core to bring it back.");
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
