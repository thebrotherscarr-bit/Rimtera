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
    Operate,
    Farm,
    Brew,
    Cook,
    Fish,
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
        new(WorkType.Operate, "Operate", ColonistSkill.Crafting, true, "Work station bills: fetch ingredients, craft, store the product."),
        new(WorkType.Farm, "Farm", ColonistSkill.Plants, true, "Plant and harvest grow zones; store the herbs, keep seeds to replant."),
        new(WorkType.Brew, "Brew", ColonistSkill.Medical, true, "Work bills at chem labs: brew potions from herbs."),
        new(WorkType.Cook, "Cook", ColonistSkill.Cooking, true, "Work bills at cooking stations: meals for the crew."),
        new(WorkType.Fish, "Fish", ColonistSkill.Animals, true, "Fish open water near home; store the catch."),
        new(WorkType.Research, "Research", ColonistSkill.Intellectual, true, "Study the chosen project at a research terminal or lab."),
        new(WorkType.Trade, "Trade", ColonistSkill.Social, true, "Better shop prices: the best Social on Trade bargains for you.")
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
/// order decides. Since 0.3.9 there is no Work tab: each person orients toward their preferred work by themselves, from
/// their background (which sets their skills and passions). Their two best skills' work comes first, then any work they
/// are good at (skill 6+) or have a passion for, then the rest.
/// </summary>
public sealed class ColonyWorkSettings
{
    private readonly int[] _priorities = new int[Enum.GetValues<WorkType>().Length];

    /// <summary>Skill level from which a person counts as good at a kind of work.</summary>
    public const int GoodSkill = 6;

    public static ColonyWorkSettings For(ColonistStats stats)
    {
        var settings = new ColonyWorkSettings();
        // The two best skills (a passion breaks ties): the work they are drawn to first.
        var best = Enum.GetValues<ColonistSkill>()
            .OrderByDescending(skill => stats.Level(skill) + (stats.Passion(skill) == PassionLevel.None ? 0f : 0.5f))
            .Take(2).ToHashSet();
        foreach (var info in WorkCatalog.All)
            settings._priorities[(int)info.Type] =
                best.Contains(info.Skill) ? WorkCatalog.Highest
                : stats.Level(info.Skill) >= GoodSkill || stats.Passion(info.Skill) != PassionLevel.None ? WorkCatalog.Passionate
                : WorkCatalog.Default;
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

    /// <summary>Work types saves made before 0.2.9 held (Haul at 2 and Build at 8 were removed then).</summary>
    private const int LegacyWorkTypes = 11;

    public void Restore(int[]? saved)
    {
        if (saved is null)
            return;
        if (saved.Length == LegacyWorkTypes)
            saved = saved.Where((_, index) => index is not (2 or 8)).ToArray();
        for (var i = 0; i < Math.Min(saved.Length, _priorities.Length); i++)
            _priorities[i] = Math.Clamp(saved[i], WorkCatalog.Off, WorkCatalog.Lowest);
    }
}
