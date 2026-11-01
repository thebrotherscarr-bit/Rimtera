using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// One research tier (0.3.7): opened by defeating any of <paramref name="Bosses"/>, it takes <paramref name="Minutes"/>
/// of study at the base rate, and until it is done the recipes for <paramref name="Unlocks"/> and every recipe that uses
/// one of <paramref name="Materials"/> stay locked.
/// </summary>
public sealed record ResearchTier(string Id, string Name, EnemyKind[] Bosses, float Minutes, ItemId[] Unlocks, ItemId[] Materials, string Gives)
{
    /// <summary>Study points to finish: one point is one second at the base rate.</summary>
    public float Cost => Minutes * 60f;
}

/// <summary>Saved research: the points put into each tier and the finished ones (Current is kept for old saves only).</summary>
public sealed record ResearchSnapshot(string? Current, Dictionary<string, float>? Progress, string[]? Done);

/// <summary>
/// Research (0.3.7, docs/VISION.md §3.1): Terraria's ladder is the base, and research adds time on top. Defeating a boss
/// opens its research tier; the station AI studies the open tiers in order on its own, and research crew at a research
/// terminal and housed residents whose trade is research make it faster. A finished tier unlocks the station upgrade it
/// names and every recipe that uses the boss's material. The 0.2.7 study-point projects are folded in: research no
/// longer has work-speed bonuses or recipe reveals of its own.
/// </summary>
public sealed partial class GameSimulation
{
    /// <summary>The tiers, in progression order. Each is opened by its boss and unlocks that step's station upgrade.</summary>
    public static IReadOnlyList<ResearchTier> ResearchTiers { get; } =
    [
        new("overseer", "Overseer salvage", [EnemyKind.StoneSentinel], 3f, [ItemId.PowerForgeMk2], [ItemId.OverseerCircuit],
            "The Power Forge Mk2, and recipes using the Overseer circuit."),
        new("scout", "Scout mech telemetry", [EnemyKind.ScoutMech], 4f, [ItemId.OreRefineryMk2], [],
            "The Ore Refinery Mk2."),
        new("fission", "Fission studies", [EnemyKind.FissionTyrant], 5f, [ItemId.AlloyAnvil], [ItemId.FissionSlag],
            "The Alloy Anvil, and recipes using fission slag."),
        new("essences", "Essence studies", [.. HardTrio], 6f, [ItemId.NanoFabricatorMk2],
            [ItemId.PrismEssence, ItemId.BlightEssence, ItemId.FlightEssence],
            "The Nano-Fabricator Mk2, and recipes using the prism, blight and flight essences."),
        new("flora", "Flora core studies", [EnemyKind.MutatedFlora], 7f, [ItemId.VoidForge], [],
            "The Void Forge."),
        new("astral", "Astral studies", [EnemyKind.AstroGod], 8f, [ItemId.AstralAssembler], [],
            "The Astral Assembler.")
    ];

    /// <summary>Study points a second the station AI puts in on its own.</summary>
    public const float BaseStudyRate = 1f;

    private readonly Dictionary<string, float> _researchProgress = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _researchDone = new(StringComparer.OrdinalIgnoreCase);

    public static ResearchTier? FindTier(string id) =>
        ResearchTiers.FirstOrDefault(tier => string.Equals(tier.Id, id, StringComparison.OrdinalIgnoreCase));

    public bool ResearchDone(string id) => _researchDone.Contains(id);

    public float ResearchProgress(string id) => _researchProgress.GetValueOrDefault(id);

    /// <summary>A tier is open once any of its bosses has been defeated.</summary>
    public bool ResearchOpen(ResearchTier tier) => tier.Bosses.Any(_defeatedBosses.Contains);

    /// <summary>The tier being studied: the first open one not yet done, or null.</summary>
    public ResearchTier? CurrentResearch => ResearchTiers.FirstOrDefault(tier => ResearchOpen(tier) && !ResearchDone(tier.Id));

    /// <summary>Study points a second each housed resident whose trade is research adds, with a bench near home.</summary>
    public const float ResidentStudyRate = 0.6f;

    /// <summary>Study points a second right now: the station AI's base rate plus the research residents at home.</summary>
    public float StudyRate => BaseStudyRate + ResidentStudyRate * _townNpcs.Count(npc => !npc.IsBound && npc.IsAlive && npc.HomeCell is not null &&
        TownTrades.TradeOf(npc.Kind).Contains(CraftingStationId.ResearchTerminal) && ResearchBenchNear(npc.HomePosition) is not null);

    /// <summary>Why a recipe is still locked by research, or null when it is free (the crafting gate).</summary>
    public string? ResearchLock(RecipeDefinition recipe)
    {
        foreach (var tier in ResearchTiers)
        {
            if (ResearchDone(tier.Id))
                continue;
            var gated = tier.Unlocks.Contains(recipe.Output) ||
                        tier.Materials.Length > 0 && recipe.Ingredients.Any(group => group.Alternatives.Any(tier.Materials.Contains));
            if (!gated)
                continue;
            return ResearchOpen(tier)
                ? $"Research {tier.Name} first ({(int)(100f * ResearchProgress(tier.Id) / tier.Cost)}% done)."
                : $"Needs research: {tier.Name}, opened by defeating the {EnemyArchetypes.Get(tier.Bosses[0]).DisplayName}.";
        }
        return null;
    }

    /// <summary>Each tick: the current tier gains study points; a finished tier unlocks its recipes.</summary>
    private void UpdateResearch(float deltaSeconds)
    {
        Flags.Set("research.tiers"); // this world keeps tiers (RestoreColonyResearch tells it from a pre-0.3.7 save)
        if (CurrentResearch is not { } tier)
            return;
        _researchProgress[tier.Id] = ResearchProgress(tier.Id) + deltaSeconds * StudyRate;
        if (ResearchProgress(tier.Id) >= tier.Cost)
            FinishResearch(tier);
    }

    /// <summary>Adds study points to the current tier (research crew at a bench).</summary>
    private void AddStudy(float points)
    {
        if (CurrentResearch is not { } tier)
            return;
        _researchProgress[tier.Id] = ResearchProgress(tier.Id) + points;
        if (ResearchProgress(tier.Id) >= tier.Cost)
            FinishResearch(tier);
    }

    /// <summary>Completes a tier: its recipes unlock.</summary>
    public void FinishResearch(ResearchTier tier)
    {
        _researchProgress[tier.Id] = tier.Cost;
        if (!_researchDone.Add(tier.Id))
            return;
        Flags.Set($"research.{tier.Id}");
        PostMessage($"Research complete: {tier.Name}. {tier.Gives}");
        Record($"The colony finished researching {tier.Name}.");
    }

    /// <summary>Puts a finished tier back to unstudied (the dev console).</summary>
    internal void ResetResearch(ResearchTier tier)
    {
        _researchDone.Remove(tier.Id);
        _researchProgress.Remove(tier.Id);
        Flags.Clear($"research.{tier.Id}");
    }

    /// <summary>Study points a colonist puts in per second: their research speed (Intellectual skill).</summary>
    public static float CrewStudyRate(Rimtera.Characters.ColonistStats stats) => Math.Max(0.1f, stats.ResearchSpeed);

    /// <summary>Research benches: research terminals and labs.</summary>
    private static bool IsResearchBench(TileId tile) => tile is TileId.ResearchTerminal or TileId.ResearchLab;

    private GridPoint? ResearchBenchNear(Vector2 home)
    {
        var reach = (int)OperateRadius;
        for (var y = (int)home.Y - reach; y <= (int)home.Y + reach; y++)
        for (var x = (int)home.X - reach; x <= (int)home.X + reach; x++)
        {
            var cell = new GridPoint(x, y);
            if (World.Contains(cell) && IsResearchBench(World.TileAt(x, y)) &&
                Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), home) <= OperateRadius)
                return cell;
        }
        return null;
    }

    /// <summary>The Research job giver: study the current tier at the nearest free bench near home.</summary>
    private bool TryResearch(ColonistAgent agent, Vector2 center, float deltaSeconds)
    {
        if (CurrentResearch is not { } tier)
        {
            agent.FarmTarget = null;
            return false;
        }
        var claimed = _colonists.Where(other => !ReferenceEquals(other, agent) && other.IsAlive && other.FarmTarget is not null)
            .Select(other => other.FarmTarget!.Value).ToHashSet();
        GridPoint? bench = null;
        var nearest = float.MaxValue;
        var reach = (int)OperateRadius;
        var home = agent.HomePosition;
        for (var y = (int)home.Y - reach; y <= (int)home.Y + reach; y++)
        for (var x = (int)home.X - reach; x <= (int)home.X + reach; x++)
        {
            var cell = new GridPoint(x, y);
            if (!World.Contains(cell) || !IsResearchBench(World.TileAt(x, y)) || claimed.Contains(cell))
                continue;
            var distance = Vector2.DistanceSquared(new Vector2(x + 0.5f, y + 0.5f), center);
            if (Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), home) <= OperateRadius && distance < nearest)
            {
                nearest = distance;
                bench = cell;
            }
        }
        if (bench is not { } at)
        {
            agent.FarmTarget = null;
            return false;
        }
        agent.FarmTarget = at;
        if (!Reaches(center, at))
        {
            agent.Activity = $"Going to study {tier.Name.ToLowerInvariant()}";
            StepColonistToward(agent, center, new Vector2(at.X + 0.5f, at.Y + 0.5f), deltaSeconds, 1f);
            return true;
        }
        agent.Activity = $"Studying {tier.Name.ToLowerInvariant()}";
        StepColonistToward(agent, center, center, deltaSeconds, 1f);
        AddStudy(deltaSeconds * CrewStudyRate(agent.ColonistStats));
        return true;
    }

    private ResearchSnapshot CaptureColonyResearch() =>
        new(null, new Dictionary<string, float>(_researchProgress, StringComparer.OrdinalIgnoreCase), _researchDone.OrderBy(id => id).ToArray());

    /// <summary>
    /// Loads research. A save from before the tiers (0.3.6 and earlier) has none of them: every tier its defeated bosses
    /// opened counts as done, so a world in progress keeps every recipe it could already make.
    /// </summary>
    private void RestoreColonyResearch(ResearchSnapshot? saved)
    {
        _researchProgress.Clear();
        _researchDone.Clear();
        var known = (saved?.Done ?? []).Concat(saved?.Progress?.Keys ?? Enumerable.Empty<string>()).Any(id => FindTier(id) is not null) ||
                    Flags.IsSet("research.tiers");
        if (!known)
        {
            foreach (var tier in ResearchTiers.Where(ResearchOpen))
            {
                _researchDone.Add(tier.Id);
                _researchProgress[tier.Id] = tier.Cost;
            }
            Flags.Set("research.tiers");
            return;
        }
        foreach (var (id, points) in saved?.Progress ?? new())
            if (FindTier(id) is { } tier && float.IsFinite(points))
                _researchProgress[tier.Id] = Math.Clamp(points, 0f, tier.Cost);
        foreach (var id in saved?.Done ?? [])
            if (FindTier(id) is { } tier)
                _researchDone.Add(tier.Id);
    }
}
