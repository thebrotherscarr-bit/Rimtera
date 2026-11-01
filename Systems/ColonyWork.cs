using Rimtera.Characters;

namespace Rimtera.Systems;

/// <summary>
/// The colony's work types (docs/DESIGN_COLONY_ESCAPE.md §3.3). Each uses one skill; the skill sets speed and quality,
/// the passion sets mood from doing it. Crew and town NPCs make things faster and better, never fully hands-off.
/// </summary>
public enum WorkType : byte
{
    Guard,
    Tend,
    Haul,
    Operate,
    Farm,
    Brew,
    Cook,
    Fish,
    Build,
    Research,
    Trade
}

/// <summary>One work type: its label, the skill it uses, and whether the colony has a job giver for it yet.</summary>
public sealed record WorkTypeInfo(WorkType Type, string Label, ColonistSkill Skill, bool HasJobs, string Description);

public static class WorkCatalog
{
    /// <summary>RimWorld's numbers: 1 is done first, 4 last, 0 never.</summary>
    public const int Off = 0, Highest = 1, Lowest = 4, Default = 3, Passionate = 2;

    public static IReadOnlyList<WorkTypeInfo> All { get; } =
    [
        new(WorkType.Guard, "Guard", ColonistSkill.Melee, true, "Fight creatures that come near home."),
        new(WorkType.Tend, "Tend", ColonistSkill.Medical, true, "Treat hurt and downed crew and residents."),
        new(WorkType.Haul, "Haul", ColonistSkill.Mining, true, "Carry loose items near home into chests."),
        new(WorkType.Operate, "Operate", ColonistSkill.Crafting, true, "Work station bills: fetch ingredients, craft, store the product."),
        new(WorkType.Farm, "Farm", ColonistSkill.Plants, true, "Plant and harvest grow zones; store the herbs, keep seeds to replant."),
        new(WorkType.Brew, "Brew", ColonistSkill.Medical, true, "Work bills at chem labs: brew potions from herbs."),
        new(WorkType.Cook, "Cook", ColonistSkill.Cooking, true, "Work bills at cooking stations: meals for the crew."),
        new(WorkType.Fish, "Fish", ColonistSkill.Animals, true, "Fish open water near home; store the catch."),
        new(WorkType.Build, "Build", ColonistSkill.Construction, true, "Build the blueprints the player places, with blocks from storage."),
        new(WorkType.Research, "Research", ColonistSkill.Intellectual, true, "Study the chosen project at a research terminal or lab."),
        new(WorkType.Trade, "Trade", ColonistSkill.Social, false, "Better prices with traders (coming).")
    ];

    public static WorkTypeInfo Get(WorkType type) => All[(int)type];

    /// <summary>
    /// A colonist's speed at a work type: the crafting speed's work factor applied to the work's own skill
    /// (0.65 + 0.035 a level, so 1 at skill 10). Crafting itself uses the stat as computed.
    /// </summary>
    public static float SkillSpeed(ColonistStats stats, WorkType type)
    {
        if (type == WorkType.Operate)
            return stats.CraftingSpeed;
        if (type == WorkType.Farm)
            return stats.PlantWorkSpeed;
        var factor = stats.CraftingSpeed / (0.65f + stats.Level(ColonistSkill.Crafting) * 0.035f);
        return factor * (0.65f + stats.Level(Get(type).Skill) * 0.035f);
    }
}

/// <summary>
/// A colonist's work priorities, RimWorld's way: 1 (first) to 4 (last), 0 (never). Among equal numbers the work type's
/// order decides. New crew start at 3, or 2 for work whose skill they have a passion for.
/// </summary>
public sealed class ColonyWorkSettings
{
    private readonly int[] _priorities = new int[Enum.GetValues<WorkType>().Length];

    public static ColonyWorkSettings For(ColonistStats stats)
    {
        var settings = new ColonyWorkSettings();
        foreach (var info in WorkCatalog.All)
            settings._priorities[(int)info.Type] = stats.Passion(info.Skill) == PassionLevel.None ? WorkCatalog.Default : WorkCatalog.Passionate;
        return settings;
    }

    public int Priority(WorkType type) => _priorities[(int)type];

    public void Set(WorkType type, int priority) => _priorities[(int)type] = Math.Clamp(priority, WorkCatalog.Off, WorkCatalog.Lowest);

    /// <summary>The Work tab's click: 1 → 2 → 3 → 4 → off → 1.</summary>
    public void Cycle(WorkType type) => Set(type, Priority(type) is WorkCatalog.Off ? WorkCatalog.Highest
        : Priority(type) >= WorkCatalog.Lowest ? WorkCatalog.Off : Priority(type) + 1);

    /// <summary>Enabled work in the order a colonist considers it: by priority number, then by work type order.</summary>
    public IEnumerable<WorkType> Order() => Enum.GetValues<WorkType>()
        .Where(type => Priority(type) != WorkCatalog.Off)
        .OrderBy(Priority).ThenBy(type => (int)type);

    public int[] Capture() => _priorities.ToArray();

    public void Restore(int[]? saved)
    {
        if (saved is null)
            return;
        for (var i = 0; i < Math.Min(saved.Length, _priorities.Length); i++)
            _priorities[i] = Math.Clamp(saved[i], WorkCatalog.Off, WorkCatalog.Lowest);
    }
}
