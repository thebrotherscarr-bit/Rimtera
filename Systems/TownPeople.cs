using Rimtera.Characters;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// One kind of character (0.3.10, docs/VISION.md §4.1): the crew merged into the town NPCs. Every resident is a person
/// with skills and passions; a trade's skills are always strong in the people who practise it (a medic is good at
/// medicine, a cook at cooking). Settlers are the people who join without a trade: as the crew did, they work any
/// station, by the work their skills draw them to, and sell nothing.
/// </summary>
public static class TownPeople
{
    /// <summary>The skills each trade is practised with (the first is the trade's main skill).</summary>
    public static readonly IReadOnlyDictionary<TownNpcKind, ColonistSkill[]> TradeSkills = new Dictionary<TownNpcKind, ColonistSkill[]>
    {
        [TownNpcKind.StationAi] = [ColonistSkill.Intellectual, ColonistSkill.Crafting],
        [TownNpcKind.Quartermaster] = [ColonistSkill.Crafting, ColonistSkill.Social],
        [TownNpcKind.Medic] = [ColonistSkill.Medical, ColonistSkill.Intellectual],
        [TownNpcKind.Armorer] = [ColonistSkill.Crafting, ColonistSkill.Shooting],
        [TownNpcKind.ChiefEngineer] = [ColonistSkill.Crafting, ColonistSkill.Construction],
        [TownNpcKind.AstroBotanist] = [ColonistSkill.Plants, ColonistSkill.Medical],
        [TownNpcKind.HeadResearcher] = [ColonistSkill.Intellectual, ColonistSkill.Crafting],
        [TownNpcKind.TradeConsul] = [ColonistSkill.Social],
        [TownNpcKind.TideWarden] = [ColonistSkill.Animals, ColonistSkill.Cooking],
        [TownNpcKind.Demolitionist] = [ColonistSkill.Crafting, ColonistSkill.Mining],
        [TownNpcKind.Tailor] = [ColonistSkill.Crafting, ColonistSkill.Artistic],
        [TownNpcKind.Cartographer] = [ColonistSkill.Intellectual, ColonistSkill.Artistic],
        [TownNpcKind.Mechanic] = [ColonistSkill.Crafting, ColonistSkill.Construction],
        [TownNpcKind.Pilot] = [ColonistSkill.Crafting, ColonistSkill.Shooting],
        [TownNpcKind.Cook] = [ColonistSkill.Cooking, ColonistSkill.Plants],
        [TownNpcKind.Arborist] = [ColonistSkill.Plants, ColonistSkill.Crafting],
        [TownNpcKind.Archivist] = [ColonistSkill.Intellectual, ColonistSkill.Social],
        [TownNpcKind.Isotopist] = [ColonistSkill.Crafting, ColonistSkill.Intellectual],
        [TownNpcKind.Aeronaut] = [ColonistSkill.Crafting, ColonistSkill.Shooting],
        [TownNpcKind.RelicWarden] = [ColonistSkill.Crafting, ColonistSkill.Melee],
        [TownNpcKind.Greenskeeper] = [ColonistSkill.Plants, ColonistSkill.Construction],
        [TownNpcKind.TravellingMerchant] = [ColonistSkill.Social],
        [TownNpcKind.FieldZoologist] = [ColonistSkill.Animals, ColonistSkill.Medical],
        [TownNpcKind.PigmentTrader] = [ColonistSkill.Artistic, ColonistSkill.Crafting],
        [TownNpcKind.PartyPlanner] = [ColonistSkill.Cooking, ColonistSkill.Social]
    };

    /// <summary>A trade's main skill is at least this, its second skill at least <see cref="SecondSkill"/>.</summary>
    public const int MainSkill = 10, SecondSkill = 7;

    public static IReadOnlyList<ColonistSkill> SkillsOf(TownNpcKind kind) => TradeSkills.TryGetValue(kind, out var skills) ? skills : [];

    /// <summary>
    /// A resident's person, the same every time for the same kind, name and seed: a random background, then the trade's
    /// skills raised (main 10–14 with a burning passion, second 7–10 with an interest).
    /// </summary>
    public static CharacterProfile ProfileFor(TownNpcKind kind, string name, int seed)
    {
        var random = new Random(unchecked(seed * 31 + StableHash(name)));
        var draft = CharacterDraft.CreateRandom(random.Next(), name);
        var profile = draft.ToProfile(new Guid(Enumerable.Range(0, 16).Select(_ => (byte)random.Next(256)).ToArray()), DateTimeOffset.UnixEpoch);
        var levels = profile.SkillLevels!.ToArray();
        var passions = profile.Passions!.ToArray();
        var skills = SkillsOf(kind);
        for (var index = 0; index < skills.Count; index++)
        {
            var skill = (int)skills[index];
            var floor = index == 0 ? MainSkill + random.Next(5) : SecondSkill + random.Next(4);
            levels[skill] = Math.Clamp(Math.Max(levels[skill], floor), 0, ColonistCatalog.MaximumSkillLevel);
            passions[skill] = (PassionLevel)Math.Max((int)passions[skill], index == 0 ? (int)PassionLevel.Burning : (int)PassionLevel.Interested);
        }
        return profile with { SkillLevels = levels, Passions = passions };
    }

    /// <summary>A settler's person: a generated crew member (the same generator the crew always used).</summary>
    public static CharacterProfile SettlerProfile(int seed, IEnumerable<string> taken)
    {
        var used = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var people = ColonistCatalog.GenerateCrew(seed, 4);
        var chosen = people.FirstOrDefault(candidate => !used.Contains(candidate.Name)) ?? people[0];
        return chosen with { CreatedAt = DateTimeOffset.UnixEpoch };
    }

    /// <summary>
    /// The work a resident turns to, by skill (0.3.9). The station AI only runs machines and studies; traders and town gels
    /// work nothing (the Trade Consul still bargains).
    /// </summary>
    public static ColonyWorkSettings WorkFor(TownNpcKind kind, ColonistStats stats, bool armed = false)
    {
        var work = ColonyWorkSettings.For(stats);
        if (kind == TownNpcKind.StationAi)
            foreach (var type in Enum.GetValues<WorkType>())
                if (type is not (WorkType.Operate or WorkType.Research))
                    work.Set(type, WorkCatalog.Off);
        if (TownTrades.Idle(kind))
            foreach (var type in Enum.GetValues<WorkType>())
                if (!(kind == TownNpcKind.TradeConsul && type == WorkType.Trade))
                    work.Set(type, WorkCatalog.Off);
        // Only those good at fighting (Melee a best or good skill, or a passion) guard their area; the rest keep working
        // and go indoors (0.4.0 brings guards and their gear properly).
        if (work.Priority(WorkType.Guard) > WorkCatalog.Passionate)
            work.Set(WorkType.Guard, WorkCatalog.Off);
        // 0.4.0: handing someone a weapon makes them a guard of their area, whatever their skills.
        if (armed && kind != TownNpcKind.StationAi && !TownTrades.Idle(kind))
            work.Set(WorkType.Guard, WorkCatalog.Highest);
        // Bargaining at shops is the Trade Consul's calling, and a settler's whose Social is a best skill; a shopkeeper
        // does not haggle down another's prices.
        if (kind != TownNpcKind.TradeConsul && !(kind == TownNpcKind.Settler && work.Priority(WorkType.Trade) == WorkCatalog.Highest))
            work.Set(WorkType.Trade, WorkCatalog.Off);
        return work;
    }

    /// <summary>The crew sprites settlers wear.</summary>
    public static readonly string[] SettlerSprites =
        ["head_researcher", "security_commander", "astro_botanist", "chief_engineer", "trade_consul"];

    public static string SettlerSprite(int look) => SettlerSprites[(int)((uint)look % (uint)SettlerSprites.Length)];

    private static int StableHash(string text)
    {
        unchecked
        {
            var hash = 17;
            foreach (var character in text)
                hash = hash * 31 + character;
            return hash;
        }
    }
}
