using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>A research project: its cost in study points, what it needs first, and what it gives.</summary>
public sealed record ResearchProject(string Id, string Name, float Cost, string? Requires, string Gives);

/// <summary>Saved research: the chosen project, points put into each, and the finished ones.</summary>
public sealed record ResearchSnapshot(string? Current, Dictionary<string, float>? Progress, string[]? Done);

/// <summary>
/// Research (0.2.7, docs/DESIGN_COLONY_ESCAPE.md §3.3 "Research"): the player picks a project in the Research tab and
/// Research crew (Intellectual skill) study it at a research terminal or lab near home. Projects make the colony's work
/// faster, teach recipes the station AI's guide would otherwise need a reveal material for, and start the ship's plans.
/// A study point is one second of study at research speed 1 (Intellectual 10).
/// </summary>
public sealed partial class GameSimulation
{
    public const string FieldManuals = "field-manuals", Herbalism = "herbalism", NetFishing = "net-fishing",
        Metallurgy = "metallurgy", CobaltStudies = "cobalt-studies", ViridiumStudies = "viridium-studies", ShipSchematics = "ship-schematics";

    /// <summary>The projects, in the Research tab's order.</summary>
    public static IReadOnlyList<ResearchProject> ResearchProjects { get; } =
    [
        new(FieldManuals, "Field manuals", 600f, null, "Bills are worked 15% faster."),
        new(Herbalism, "Herbalism", 800f, null, "Farm work is 25% faster."),
        new(NetFishing, "Net fishing", 800f, null, "Crew fish 25% faster."),
        new(Metallurgy, "Metallurgy notes", 1000f, null, "Teaches the recipes copper bars would reveal."),
        new(CobaltStudies, "Cobalt studies", 1600f, Metallurgy, "Teaches the recipes cobalt ore would reveal."),
        new(ViridiumStudies, "Viridium studies", 2400f, CobaltStudies, "Teaches the recipes viridium ore would reveal."),
        new(ShipSchematics, "Ship schematics I", 3000f, Metallurgy, "The first plans for the ship (its parts come with the escape).")
    ];

    private readonly Dictionary<string, float> _researchProgress = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _researchDone = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The project Research crew are studying; null when none is chosen.</summary>
    public string? CurrentResearch { get; private set; }

    public bool ResearchDone(string id) => _researchDone.Contains(id);

    public float ResearchProgress(string id) => _researchProgress.GetValueOrDefault(id);

    public static ResearchProject? FindProject(string id) => ResearchProjects.FirstOrDefault(project => string.Equals(project.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether a project can be studied: not done, and its prerequisite is.</summary>
    public bool CanResearch(string id) =>
        FindProject(id) is { } project && !ResearchDone(project.Id) && (project.Requires is null || ResearchDone(project.Requires));

    /// <summary>Chooses the project to study (the Research tab's button); false when it can't be studied yet.</summary>
    public bool ChooseResearch(string id)
    {
        if (!CanResearch(id))
            return false;
        CurrentResearch = FindProject(id)!.Id;
        return true;
    }

    /// <summary>Bills, farm work and fishing speed from finished research.</summary>
    public float ResearchBillSpeed => ResearchDone(FieldManuals) ? 1.15f : 1f;
    public float ResearchFarmSpeed => ResearchDone(Herbalism) ? 1.25f : 1f;
    public float ResearchFishSpeed => ResearchDone(NetFishing) ? 1.25f : 1f;

    /// <summary>Study points a colonist puts in per second: their research speed (Intellectual skill).</summary>
    public static float StudyRate(Rimtera.Characters.ColonistStats stats) => Math.Max(0.1f, stats.ResearchSpeed);

    /// <summary>Research benches: research terminals and labs.</summary>
    private static bool IsResearchBench(TileId tile) => tile is TileId.ResearchTerminal or TileId.ResearchLab;

    /// <summary>The Research job giver: study the chosen project at the nearest free bench near home.</summary>
    private bool TryResearch(ColonistAgent agent, Vector2 center, float deltaSeconds)
    {
        if (CurrentResearch is not { } current || !CanResearch(current))
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
        var project = FindProject(current)!;
        if (!Reaches(center, at))
        {
            agent.Activity = $"Going to study {project.Name.ToLowerInvariant()}";
            StepColonistToward(agent, center, new Vector2(at.X + 0.5f, at.Y + 0.5f), deltaSeconds, 1f);
            return true;
        }
        agent.Activity = $"Studying {project.Name.ToLowerInvariant()}";
        StepColonistToward(agent, center, center, deltaSeconds, 1f);
        _researchProgress[project.Id] = ResearchProgress(project.Id) + deltaSeconds * StudyRate(agent.ColonistStats);
        if (ResearchProgress(project.Id) >= project.Cost)
            FinishResearch(project);
        return true;
    }

    /// <summary>Completes a project: applies what it gives and clears the choice.</summary>
    public void FinishResearch(ResearchProject project)
    {
        _researchProgress[project.Id] = project.Cost;
        if (!_researchDone.Add(project.Id))
            return;
        ApplyResearch(project);
        if (string.Equals(CurrentResearch, project.Id, StringComparison.OrdinalIgnoreCase))
            CurrentResearch = null;
        Flags.Set($"research.{project.Id}");
        PostMessage($"Research complete: {project.Name}. {project.Gives}");
        Record($"The crew finished researching {project.Name}.");
    }

    /// <summary>What a finished project changes outside the speed bonuses (re-applied when a world loads).</summary>
    private void ApplyResearch(ResearchProject project)
    {
        var reveals = project.Id switch
        {
            Metallurgy => ItemId.CopperBar,
            CobaltStudies => ItemId.CobaltOre,
            ViridiumStudies => ItemId.ViridiumOre,
            _ => (ItemId?)null
        };
        if (reveals is { } material)
            foreach (var recipe in Crafting.Recipes.Where(recipe => recipe.RequiresGuideUnlock && recipe.GuideUnlockMaterials?.Contains(material) == true))
                Crafting.LearnGuideRecipe(recipe.Id);
    }

    private ResearchSnapshot CaptureColonyResearch() =>
        new(CurrentResearch, new Dictionary<string, float>(_researchProgress, StringComparer.OrdinalIgnoreCase), _researchDone.OrderBy(id => id).ToArray());

    private void RestoreColonyResearch(ResearchSnapshot? saved)
    {
        _researchProgress.Clear();
        _researchDone.Clear();
        CurrentResearch = null;
        if (saved is null)
            return;
        foreach (var (id, points) in saved.Progress ?? new())
            if (FindProject(id) is { } project && float.IsFinite(points))
                _researchProgress[project.Id] = Math.Clamp(points, 0f, project.Cost);
        foreach (var id in saved.Done ?? [])
            if (FindProject(id) is { } project && _researchDone.Add(project.Id))
                ApplyResearch(project);
        if (saved.Current is { } current && CanResearch(current))
            CurrentResearch = FindProject(current)!.Id;
    }
}
