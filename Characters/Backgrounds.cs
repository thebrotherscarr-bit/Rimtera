using Rimtera.Systems;

namespace Rimtera.Characters;

/// <summary>
/// One part of a person's story (0.5.1, docs/VISION.md §3.3): a childhood or an adulthood. It adds to their skills, and it
/// can leave them unwilling or unable to do some kinds of work (a pacifist will not fight, a noble will not haul crops).
/// </summary>
public sealed record BackgroundDefinition(
    string Id,
    string Title,
    bool Childhood,
    string Description,
    (ColonistSkill Skill, int Amount)[] Skills,
    WorkType[] Incapable,
    TownNpcKind[]? Trades = null)
{
    public bool Allows(WorkType work) => Array.IndexOf(Incapable, work) < 0;
}

/// <summary>The childhoods and adulthoods people are drawn from (Rimtera's own frontier stories).</summary>
public static class Backgrounds
{
    public static readonly IReadOnlyList<BackgroundDefinition> Childhoods =
    [
        new("vault-child", "Vault child", true, "Grew up in a sealed habitat, raised by tutors and terminals.",
            [(ColonistSkill.Intellectual, 3), (ColonistSkill.Crafting, 1)], [WorkType.Farm]),
        new("scrapyard-runner", "Scrapyard runner", true, "Ran salvage through the junk wastes for a fence who paid in rations.",
            [(ColonistSkill.Crafting, 2), (ColonistSkill.Mining, 2), (ColonistSkill.Melee, 1)], []),
        new("hydro-farm-kid", "Hydro-farm kid", true, "Tended grow racks aboard a colony ship from the age of six.",
            [(ColonistSkill.Plants, 4), (ColonistSkill.Cooking, 1)], []),
        new("frontier-orphan", "Frontier orphan", true, "Survived alone at the edge of a dying settlement.",
            [(ColonistSkill.Melee, 2), (ColonistSkill.Shooting, 2), (ColonistSkill.Animals, 1)], [WorkType.Trade]),
        new("medic-s-child", "Medic's child", true, "Followed a field medic from camp to camp, handing over bandages.",
            [(ColonistSkill.Medical, 3), (ColonistSkill.Social, 1)], []),
        new("noble-heir", "Noble heir", true, "Raised on an orbital estate where others did the work.",
            [(ColonistSkill.Social, 4), (ColonistSkill.Artistic, 2)], [WorkType.Farm, WorkType.Fish]),
        new("cave-born", "Cave-born", true, "Born in the deep tunnels of a mining world; the dark is home.",
            [(ColonistSkill.Mining, 4), (ColonistSkill.Construction, 1)], []),
        new("kitchen-hand", "Galley hand", true, "Peeled, chopped and scrubbed in a freighter's galley.",
            [(ColonistSkill.Cooking, 4)], []),
        new("cult-raised", "Cult-raised", true, "Raised by a sect that preached peace above everything.",
            [(ColonistSkill.Social, 2), (ColonistSkill.Medical, 2), (ColonistSkill.Artistic, 1)], [WorkType.Guard]),
        new("drone-tinkerer", "Drone tinkerer", true, "Took apart every machine they found, and rebuilt most of them.",
            [(ColonistSkill.Crafting, 3), (ColonistSkill.Intellectual, 1)], []),
        new("ranch-kid", "Ranch kid", true, "Herded grazers across open plains under two moons.",
            [(ColonistSkill.Animals, 4), (ColonistSkill.Plants, 1)], []),
        new("street-gang", "Street gang", true, "Learned early that whoever hits first eats.",
            [(ColonistSkill.Melee, 3), (ColonistSkill.Shooting, 1)], [WorkType.Tend])
    ];

    public static readonly IReadOnlyList<BackgroundDefinition> Adulthoods =
    [
        new("field-medic", "Field medic", false, "Patched up soldiers under fire for a frontier militia.",
            [(ColonistSkill.Medical, 5), (ColonistSkill.Shooting, 1)], [], [TownNpcKind.Medic]),
        new("chemist", "Pharma chemist", false, "Brewed stims and tonics in a corporate lab.",
            [(ColonistSkill.Medical, 3), (ColonistSkill.Intellectual, 2)], [], [TownNpcKind.AstroBotanist, TownNpcKind.FieldZoologist]),
        new("ship-engineer", "Ship engineer", false, "Kept a hauler's reactor alive with spit and wire.",
            [(ColonistSkill.Crafting, 4), (ColonistSkill.Construction, 2)], [], [TownNpcKind.ChiefEngineer, TownNpcKind.Mechanic, TownNpcKind.Pilot]),
        new("smith", "Forge smith", false, "Hammered ferrite into tools and blades for a mining guild.",
            [(ColonistSkill.Crafting, 4), (ColonistSkill.Mining, 1)], [], [TownNpcKind.Armorer, TownNpcKind.Isotopist, TownNpcKind.RelicWarden]),
        new("researcher", "Station researcher", false, "Ran experiments nobody else understood on a deep-space station.",
            [(ColonistSkill.Intellectual, 5)], [WorkType.Fish], [TownNpcKind.HeadResearcher, TownNpcKind.Archivist, TownNpcKind.Cartographer]),
        new("cook", "Mess-hall cook", false, "Fed three hundred mouths a shift from whatever the hold had.",
            [(ColonistSkill.Cooking, 5), (ColonistSkill.Social, 1)], [], [TownNpcKind.Cook, TownNpcKind.PartyPlanner, TownNpcKind.TideWarden]),
        new("merchant", "Trade broker", false, "Bought low and sold high across a dozen settlements.",
            [(ColonistSkill.Social, 5)], [WorkType.Farm], [TownNpcKind.Quartermaster, TownNpcKind.TradeConsul, TownNpcKind.TravellingMerchant, TownNpcKind.PigmentTrader]),
        new("soldier", "Colonial marine", false, "Served two tours keeping the frontier's lanes open.",
            [(ColonistSkill.Shooting, 4), (ColonistSkill.Melee, 2)], [], [TownNpcKind.Demolitionist, TownNpcKind.Aeronaut]),
        new("farmer", "Homestead farmer", false, "Worked a homestead's fields until the drought took them.",
            [(ColonistSkill.Plants, 5), (ColonistSkill.Animals, 1)], [], [TownNpcKind.Arborist, TownNpcKind.Greenskeeper]),
        new("artisan", "Artisan weaver", false, "Made cloth and colour by hand for collectors on the core worlds.",
            [(ColonistSkill.Artistic, 4), (ColonistSkill.Crafting, 2)], [], [TownNpcKind.Tailor]),
        new("pacifist-monk", "Pacifist monk", false, "Spent years in a quiet order that forbids violence.",
            [(ColonistSkill.Medical, 2), (ColonistSkill.Plants, 2), (ColonistSkill.Social, 2)], [WorkType.Guard]),
        new("miner", "Deep miner", false, "Cut ore in the hot dark for a company that kept the profits.",
            [(ColonistSkill.Mining, 5), (ColonistSkill.Construction, 1)], []),
        new("hunter", "Wilds hunter", false, "Tracked and trapped game beyond the colony walls.",
            [(ColonistSkill.Shooting, 3), (ColonistSkill.Animals, 3)], []),
        new("ai-caretaker", "AI caretaker", false, "Kept a station AI's cores clean and its mind sane.",
            [(ColonistSkill.Intellectual, 3), (ColonistSkill.Crafting, 2)], [], [TownNpcKind.StationAi])
    ];

    public static BackgroundDefinition? Find(string? id) =>
        id is null ? null : Childhoods.Concat(Adulthoods).FirstOrDefault(background => background.Id == id);

    /// <summary>
    /// A person's story, the same every time for the same seed: a random childhood, and an adulthood that fits their trade
    /// when one does (a medic was a field medic), else a random one.
    /// </summary>
    public static (BackgroundDefinition Childhood, BackgroundDefinition Adulthood) For(TownNpcKind kind, int seed)
    {
        var random = new Random(unchecked(seed * 2654435 + 97));
        var childhood = Childhoods[random.Next(Childhoods.Count)];
        var fitting = Adulthoods.Where(adulthood => adulthood.Trades?.Contains(kind) == true).ToArray();
        var adulthood = fitting.Length > 0 ? fitting[random.Next(fitting.Length)] : Adulthoods[random.Next(Adulthoods.Count)];
        // A trade's own work is never one they refuse: a childhood that would forbid it is swapped for one that does not.
        var main = TownPeople.SkillsOf(kind).FirstOrDefault();
        if (kind != TownNpcKind.Settler && TownPeople.SkillsOf(kind).Count > 0 &&
            childhood.Incapable.Any(work => WorkCatalog.Get(work).Skill == main))
            childhood = Childhoods.First(candidate => candidate.Incapable.All(work => WorkCatalog.Get(work).Skill != main));
        return (childhood, adulthood);
    }

    /// <summary>The work a person refuses: their childhood's and adulthood's together.</summary>
    public static IEnumerable<WorkType> Refused(BackgroundDefinition? childhood, BackgroundDefinition? adulthood) =>
        (childhood?.Incapable ?? []).Concat(adulthood?.Incapable ?? []).Distinct();

    /// <summary>Adds a background's skills to a profile's skill levels (capped at the maximum).</summary>
    public static CharacterProfile Apply(CharacterProfile profile, params BackgroundDefinition[] parts)
    {
        var levels = (profile.SkillLevels ?? new int[ColonistCatalog.SkillCount]).ToArray();
        foreach (var part in parts)
            foreach (var (skill, amount) in part.Skills)
                levels[(int)skill] = Math.Clamp(levels[(int)skill] + amount, 0, ColonistCatalog.MaximumSkillLevel);
        return profile with { SkillLevels = levels };
    }
}
