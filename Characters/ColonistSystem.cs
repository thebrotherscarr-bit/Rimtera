using Rimtera.AI;
using Rimtera.Entities;
using Rimtera.Systems;

namespace Rimtera.Characters;

public enum ColonistGender : byte { Female, Male, NonBinary }

public enum ColonistSkill : byte
{
    Shooting,
    Melee,
    Construction,
    Mining,
    Cooking,
    Plants,
    Animals,
    Crafting,
    Artistic,
    Medical,
    Social,
    Intellectual
}

public enum PassionLevel : byte { None, Interested, Burning }

public enum BackstoryId : byte
{
    CrashlandedEngineer,
    FrontierMedic,
    ColonyCook,
    DeepMiner,
    TrailScout,
    ColonySoldier,
    WorkshopArtisan,
    FieldResearcher,
    HomesteadFarmer,
    StreetOrphan,
    SalvageMechanic,
    JungleHunter
}

public enum ColonistTraitId : byte
{
    Optimist,
    Pessimist,
    IronWilled,
    Nervous,
    Industrious,
    Lazy,
    Tough,
    Frail,
    FastWalker,
    Slowpoke,
    TriggerHappy,
    Brawler,
    GreenThumb,
    Gourmet,
    Ascetic,
    QuickLearner,
    Psychopath,
    Kind,
    Neat,
    Sloppy,
    NightOwl,
    Abrasive
}

/// <summary>Immutable palette indices used by the renderer and character creator.</summary>
public sealed record ColonistAppearance(
    int SkinTone = 1,
    int HairStyle = 0,
    int HairColor = 1,
    int EyeColor = 0,
    int OutfitColor = 0)
{
    public const int SkinToneCount = 6;
    public const int HairStyleCount = 8;
    public const int HairColorCount = 8;
    public const int EyeColorCount = 6;
    public const int OutfitColorCount = 6;

    public static readonly string[] HairStyleNames =
    [
        "Short", "Fringe", "Braid", "Bun", "Mohawk", "Long", "Curly", "Bald"
    ];

    public static readonly string[] SkinToneNames =
    [
        "Porcelain", "Fair", "Warm", "Olive", "Bronze", "Deep"
    ];

    public static readonly string[] HairColorNames =
    [
        "Black", "Dark brown", "Chestnut", "Auburn", "Blond", "White", "Blue", "Green"
    ];

    public static readonly string[] EyeColorNames =
    [
        "Brown", "Hazel", "Green", "Blue", "Grey", "Amber"
    ];

    public static readonly string[] OutfitColorNames =
    [
        "Rust", "Moss", "Indigo", "Sand", "Plum", "Teal"
    ];

    public ColonistAppearance Normalized() => this with
    {
        SkinTone = Math.Clamp(SkinTone, 0, SkinToneCount - 1),
        HairStyle = Math.Clamp(HairStyle, 0, HairStyleCount - 1),
        HairColor = Math.Clamp(HairColor, 0, HairColorCount - 1),
        EyeColor = Math.Clamp(EyeColor, 0, EyeColorCount - 1),
        OutfitColor = Math.Clamp(OutfitColor, 0, OutfitColorCount - 1)
    };

    public string Summary =>
        $"{SkinToneNames[SkinTone]} · {HairStyleNames[HairStyle]} · {HairColorNames[HairColor]} hair";
}

public sealed record BackstoryDefinition(
    BackstoryId Id,
    string Name,
    string Description,
    int[] SkillBonuses,
    float WorkSpeed = 1f,
    float MoveSpeed = 1f,
    float MoodOffset = 0f);

public sealed record ColonistTraitDefinition(
    ColonistTraitId Id,
    string Name,
    string Description,
    int[] SkillBonuses,
    float WorkSpeed = 1f,
    float MoveSpeed = 1f,
    float MoodOffset = 0f,
    float FoodConsumption = 1f,
    float RestRate = 1f,
    float MeleeHitBonus = 0f,
    float ShootingAccuracyBonus = 0f,
    float MaximumHealthBonus = 0f,
    float MeleeDamageBonus = 0f,
    float ShootingDamageBonus = 0f,
    ColonistTraitId[]? Conflicts = null);

/// <summary>
/// Derived colonist sheet. Specific work speeds are exposed now so the world/settlement
/// systems can consume them without changing the character data later.
/// </summary>
public sealed record ColonistStats(
    int[] SkillLevels,
    PassionLevel[] Passions,
    float MaximumHealthBonus,
    float MoveSpeedMultiplier,
    float GlobalWorkSpeed,
    float MiningSpeed,
    float ConstructionSpeed,
    float PlantWorkSpeed,
    float CookingSpeed,
    float CraftingSpeed,
    float ResearchSpeed,
    float MedicalTendQuality,
    float SocialImpact,
    float MeleeHitChance,
    float MeleeDodgeChance,
    float ShootingAccuracy,
    float MeleeDamageMultiplier,
    float ShootingDamageMultiplier,
    float MoodOffset,
    float FoodConsumptionMultiplier,
    float RestRateMultiplier)
{
    public int Level(ColonistSkill skill) => SkillLevels[(int)skill];
    public PassionLevel Passion(ColonistSkill skill) => Passions[(int)skill];
    public float MaximumHealth(float baseMaximumHealth) => baseMaximumHealth + MaximumHealthBonus;
}

public static class ColonistCatalog
{
    public const int SkillCount = 12;
    public const int MaximumSkillLevel = 20;

    private static readonly string[] FirstNames =
    [
        "Ada", "Bram", "Cora", "Dain", "Elis", "Fenn", "Greta", "Hale", "Iris", "Jory",
        "Kara", "Lio", "Mira", "Nell", "Orin", "Pax", "Quinn", "Rhea", "Sable", "Tarn",
        "Uma", "Vale", "Wren", "Yara", "Zeke"
    ];

    private static readonly string[] LastNames =
    [
        "Ash", "Barrow", "Cinder", "Dawn", "Ember", "Flint", "Grove", "Hollow", "Iron", "Juniper",
        "Kestrel", "Lark", "Moss", "North", "Oak", "Pine", "Reed", "Stone", "Thorn", "Vale",
        "Willow", "Wren", "Yew", "Zephyr"
    ];

    public static readonly IReadOnlyList<BackstoryDefinition> Backstories =
    [
        new(BackstoryId.CrashlandedEngineer, "Crashlanded engineer",
            "Kept a broken colony running with salvaged parts and stubborn arithmetic.",
            Bonuses((ColonistSkill.Construction, 4), (ColonistSkill.Crafting, 2), (ColonistSkill.Intellectual, 1)),
            WorkSpeed: 1.05f),
        new(BackstoryId.FrontierMedic, "Frontier medic",
            "Treated wounds in the field, often with more courage than supplies.",
            Bonuses((ColonistSkill.Medical, 4), (ColonistSkill.Social, 1), (ColonistSkill.Intellectual, 1)),
            MoodOffset: 0.01f),
        new(BackstoryId.ColonyCook, "Colony cook",
            "Turned plain rations into something worth gathering around.",
            Bonuses((ColonistSkill.Cooking, 4), (ColonistSkill.Plants, 1), (ColonistSkill.Social, 1)),
            MoodOffset: 0.02f),
        new(BackstoryId.DeepMiner, "Deep miner",
            "Spent years reading stone and listening for the crack before a collapse.",
            Bonuses((ColonistSkill.Mining, 4), (ColonistSkill.Construction, 1), (ColonistSkill.Crafting, 1)),
            WorkSpeed: 1.04f),
        new(BackstoryId.TrailScout, "Trail scout",
            "Mapped safe paths through country that wanted them lost.",
            Bonuses((ColonistSkill.Shooting, 3), (ColonistSkill.Animals, 2), (ColonistSkill.Melee, 1)),
            MoveSpeed: 1.06f),
        new(BackstoryId.ColonySoldier, "Colony soldier",
            "Held a line long enough for everyone else to get home.",
            Bonuses((ColonistSkill.Shooting, 4), (ColonistSkill.Melee, 2)),
            MoodOffset: -0.01f),
        new(BackstoryId.WorkshopArtisan, "Workshop artisan",
            "Measured twice, cut once, and signed every finished piece.",
            Bonuses((ColonistSkill.Crafting, 4), (ColonistSkill.Artistic, 1), (ColonistSkill.Construction, 1)),
            WorkSpeed: 1.03f),
        new(BackstoryId.FieldResearcher, "Field researcher",
            "Catalogued plants, minerals, and the occasional questionable fungus.",
            Bonuses((ColonistSkill.Intellectual, 4), (ColonistSkill.Medical, 1), (ColonistSkill.Social, 1)),
            WorkSpeed: 1.02f),
        new(BackstoryId.HomesteadFarmer, "Homestead farmer",
            "Raised crops and animals through bad seasons and good neighbours.",
            Bonuses((ColonistSkill.Plants, 4), (ColonistSkill.Animals, 2), (ColonistSkill.Cooking, 1)),
            MoodOffset: 0.02f),
        new(BackstoryId.StreetOrphan, "Street orphan",
            "Learned to trade, run, and read a room before learning to read.",
            Bonuses((ColonistSkill.Social, 2), (ColonistSkill.Melee, 1), (ColonistSkill.Cooking, 1)),
            MoveSpeed: 1.04f),
        new(BackstoryId.SalvageMechanic, "Salvage mechanic",
            "Nothing stayed broken for long if it could be carried home.",
            Bonuses((ColonistSkill.Crafting, 3), (ColonistSkill.Construction, 2), (ColonistSkill.Mining, 1)),
            WorkSpeed: 1.04f),
        new(BackstoryId.JungleHunter, "Jungle hunter",
            "Followed game through wet green dark and never came back empty-handed.",
            Bonuses((ColonistSkill.Shooting, 3), (ColonistSkill.Animals, 3), (ColonistSkill.Cooking, 1)),
            MoveSpeed: 1.03f)
    ];

    public static readonly IReadOnlyList<ColonistTraitDefinition> Traits =
    [
        new(ColonistTraitId.Optimist, "Optimist", "Finds a little more daylight in every setback.",
            Empty(), MoodOffset: 0.08f, Conflicts: [ColonistTraitId.Pessimist]),
        new(ColonistTraitId.Pessimist, "Pessimist", "Expects the worst, so the worst rarely surprises them.",
            Empty(), MoodOffset: -0.08f, Conflicts: [ColonistTraitId.Optimist]),
        new(ColonistTraitId.IronWilled, "Iron-willed", "Keeps moving when others would stop.",
            Empty(), MoodOffset: 0.10f, Conflicts: [ColonistTraitId.Nervous]),
        new(ColonistTraitId.Nervous, "Nervous", "Quick on their feet, quicker to worry.",
            Empty(), MoveSpeed: 1.05f, MoodOffset: -0.05f, Conflicts: [ColonistTraitId.IronWilled]),
        new(ColonistTraitId.Industrious, "Industrious", "Happiest with a task half-finished and another waiting.",
            Empty(), WorkSpeed: 1.15f, Conflicts: [ColonistTraitId.Lazy]),
        new(ColonistTraitId.Lazy, "Lazy", "Believes rest is a kind of work too.",
            Empty(), WorkSpeed: 0.88f, Conflicts: [ColonistTraitId.Industrious]),
        new(ColonistTraitId.Tough, "Tough", "Takes a hit and keeps standing.",
            Empty(), MaximumHealthBonus: 15f, Conflicts: [ColonistTraitId.Frail]),
        new(ColonistTraitId.Frail, "Frail", "Built for thought more than for impact.",
            Empty(), MoveSpeed: 0.95f, MaximumHealthBonus: -10f, Conflicts: [ColonistTraitId.Tough]),
        new(ColonistTraitId.FastWalker, "Fast walker", "Always first to the next ridge.",
            Empty(), MoveSpeed: 1.12f, Conflicts: [ColonistTraitId.Slowpoke]),
        new(ColonistTraitId.Slowpoke, "Slowpoke", "Arrives late, but arrives.",
            Empty(), MoveSpeed: 0.90f, Conflicts: [ColonistTraitId.FastWalker]),
        new(ColonistTraitId.TriggerHappy, "Trigger-happy", "Shoots first and aims on the way.",
            Bonuses((ColonistSkill.Shooting, 3), (ColonistSkill.Melee, -2)),
            ShootingAccuracyBonus: 0.05f, Conflicts: [ColonistTraitId.Brawler]),
        new(ColonistTraitId.Brawler, "Brawler", "Prefers a problem close enough to grab.",
            Bonuses((ColonistSkill.Melee, 3), (ColonistSkill.Shooting, -2)),
            MeleeHitBonus: 0.05f, MeleeDamageBonus: 0.08f, Conflicts: [ColonistTraitId.TriggerHappy]),
        new(ColonistTraitId.GreenThumb, "Green thumb", "Things grow better when they are nearby.",
            Bonuses((ColonistSkill.Plants, 3))),
        new(ColonistTraitId.Gourmet, "Gourmet", "Knows exactly what a meal is missing.",
            Bonuses((ColonistSkill.Cooking, 3)), FoodConsumption: 1.15f),
        new(ColonistTraitId.Ascetic, "Ascetic", "Needs little, and notices it less.",
            Empty(), MoodOffset: 0.05f, FoodConsumption: 0.90f),
        new(ColonistTraitId.QuickLearner, "Quick learner", "Picks up new work before the dust settles.",
            Bonuses((ColonistSkill.Intellectual, 2)), WorkSpeed: 1.05f),
        new(ColonistTraitId.Psychopath, "Psychopath", "Reads people as problems rather than company.",
            Bonuses((ColonistSkill.Social, -3)), MoodOffset: 0.04f, Conflicts: [ColonistTraitId.Kind]),
        new(ColonistTraitId.Kind, "Kind", "A steady word and an open hand.",
            Bonuses((ColonistSkill.Social, 2), (ColonistSkill.Medical, 1)),
            MoodOffset: 0.03f, Conflicts: [ColonistTraitId.Psychopath]),
        new(ColonistTraitId.Neat, "Neat", "A place for everything, and a better mood when it stays there.",
            Empty(), MoodOffset: 0.03f, Conflicts: [ColonistTraitId.Sloppy]),
        new(ColonistTraitId.Sloppy, "Sloppy", "Cleans up eventually. Usually.",
            Empty(), MoodOffset: -0.02f, Conflicts: [ColonistTraitId.Neat]),
        new(ColonistTraitId.NightOwl, "Night owl", "Comes alive when the lamps do.",
            Empty(), MoodOffset: 0.03f, RestRate: 0.95f),
        new(ColonistTraitId.Abrasive, "Abrasive", "Says exactly what they think, whether or not anyone asked.",
            Bonuses((ColonistSkill.Social, -1)), Conflicts: [ColonistTraitId.Kind])
    ];

    public static BackstoryDefinition GetBackstory(BackstoryId id) =>
        Backstories.FirstOrDefault(definition => definition.Id == id) ?? Backstories[0];

    public static ColonistTraitDefinition GetTrait(ColonistTraitId id) =>
        Traits.First(definition => definition.Id == id);

    public static ColonistTraitId[] NormalizeTraits(IEnumerable<ColonistTraitId>? traits)
    {
        if (traits is null)
            return [];

        var result = new List<ColonistTraitId>();
        foreach (var id in traits)
        {
            if (!Enum.IsDefined(id) || result.Contains(id) || result.Count >= 3)
                continue;
            var definition = GetTrait(id);
            if (definition.Conflicts is not null && definition.Conflicts.Any(result.Contains))
                continue;
            if (result.Any(existing => GetTrait(existing).Conflicts?.Contains(id) == true))
                continue;
            result.Add(id);
        }
        return result.ToArray();
    }

    public static ColonistStats Compute(
        BackstoryId backstoryId,
        IEnumerable<ColonistTraitId>? traitIds,
        int age,
        int[]? skillLevels,
        PassionLevel[]? passions)
    {
        var levels = NormalizeLevels(skillLevels);
        var passionValues = NormalizePassions(passions);
        var backstory = GetBackstory(backstoryId);
        var traits = NormalizeTraits(traitIds).Select(GetTrait).ToArray();

        var work = backstory.WorkSpeed;
        var move = backstory.MoveSpeed;
        var mood = backstory.MoodOffset;
        var food = 1f;
        var rest = 1f;
        var meleeHit = 0f;
        var shootingAccuracy = 0f;
        var maximumHealth = 0f;
        var meleeDamage = 0f;
        var shootingDamage = 0f;

        foreach (var trait in traits)
        {
            work *= trait.WorkSpeed;
            move *= trait.MoveSpeed;
            mood += trait.MoodOffset;
            food *= trait.FoodConsumption;
            rest *= trait.RestRate;
            meleeHit += trait.MeleeHitBonus;
            shootingAccuracy += trait.ShootingAccuracyBonus;
            maximumHealth += trait.MaximumHealthBonus;
            meleeDamage += trait.MeleeDamageBonus;
            shootingDamage += trait.ShootingDamageBonus;
        }

        var workSkills = new[]
        {
            levels[(int)ColonistSkill.Construction], levels[(int)ColonistSkill.Mining],
            levels[(int)ColonistSkill.Cooking], levels[(int)ColonistSkill.Plants],
            levels[(int)ColonistSkill.Animals], levels[(int)ColonistSkill.Crafting],
            levels[(int)ColonistSkill.Artistic], levels[(int)ColonistSkill.Medical],
            levels[(int)ColonistSkill.Social], levels[(int)ColonistSkill.Intellectual]
        };
        var averageWork = (float)workSkills.Average() / MaximumSkillLevel;
        var globalWork = (0.82f + averageWork * 0.28f) * work;

        var ageMove = age switch
        {
            <= 24 => 1.03f,
            <= 40 => 1f,
            <= 55 => 0.97f,
            <= 70 => 0.92f,
            _ => 0.85f
        };

        float SkillSpeed(ColonistSkill skill, float baseValue, float perLevel) =>
            (baseValue + levels[(int)skill] * perLevel) * work;

        return new ColonistStats(
            levels,
            passionValues,
            maximumHealth,
            (move * ageMove),
            globalWork,
            SkillSpeed(ColonistSkill.Mining, 0.65f, 0.035f),
            SkillSpeed(ColonistSkill.Construction, 0.65f, 0.035f),
            SkillSpeed(ColonistSkill.Plants, 0.60f, 0.040f),
            SkillSpeed(ColonistSkill.Cooking, 0.60f, 0.040f),
            SkillSpeed(ColonistSkill.Crafting, 0.65f, 0.035f),
            0.50f + levels[(int)ColonistSkill.Intellectual] * 0.05f,
            0.40f + levels[(int)ColonistSkill.Medical] * 0.03f,
            0.50f + levels[(int)ColonistSkill.Social] * 0.025f,
            Math.Clamp(0.50f + levels[(int)ColonistSkill.Melee] * 0.02f + meleeHit, 0.25f, 0.95f),
            Math.Clamp(0.05f + levels[(int)ColonistSkill.Melee] * 0.01f, 0.05f, 0.45f),
            Math.Clamp(0.55f + levels[(int)ColonistSkill.Shooting] * 0.02f + shootingAccuracy, 0.30f, 0.95f),
            1f + levels[(int)ColonistSkill.Melee] * 0.025f + meleeDamage,
            1f + levels[(int)ColonistSkill.Shooting] * 0.025f + shootingDamage,
            mood,
            food,
            rest);
    }

    public static ColonistStats Compute(CharacterProfile profile) =>
        Compute(profile.Backstory, profile.Traits, profile.Age, profile.SkillLevels, profile.Passions);

    public static int[] GenerateSkillLevels(
        int seed,
        BackstoryId backstoryId,
        IEnumerable<ColonistTraitId>? traits,
        ColonistSkill focus)
    {
        var random = new Random(seed);
        var levels = new int[SkillCount];
        for (var i = 0; i < levels.Length; i++)
            levels[i] = random.Next(0, 4);

        var backstory = GetBackstory(backstoryId);
        AddBonuses(levels, backstory.SkillBonuses);
        foreach (var trait in NormalizeTraits(traits))
            AddBonuses(levels, GetTrait(trait).SkillBonuses);
        levels[(int)focus] += 2;
        return NormalizeLevels(levels);
    }

    public static PassionLevel[] GeneratePassions(
        BackstoryId backstoryId,
        IEnumerable<ColonistTraitId>? traits,
        ColonistSkill focus)
    {
        var passions = new PassionLevel[SkillCount];
        var backstory = GetBackstory(backstoryId);
        for (var i = 0; i < passions.Length; i++)
        {
            if (backstory.SkillBonuses[i] >= 3)
                passions[i] = PassionLevel.Interested;
        }
        foreach (var trait in NormalizeTraits(traits))
        {
            var bonuses = GetTrait(trait).SkillBonuses;
            for (var i = 0; i < bonuses.Length; i++)
            {
                if (bonuses[i] >= 3)
                    passions[i] = PassionLevel.Interested;
            }
        }
        passions[(int)focus] = PassionLevel.Burning;
        return passions;
    }

    public static string SkillName(ColonistSkill skill) => skill switch
    {
        ColonistSkill.Shooting => "Shooting",
        ColonistSkill.Melee => "Melee",
        ColonistSkill.Construction => "Construction",
        ColonistSkill.Mining => "Mining",
        ColonistSkill.Cooking => "Cooking",
        ColonistSkill.Plants => "Plants",
        ColonistSkill.Animals => "Animals",
        ColonistSkill.Crafting => "Crafting",
        ColonistSkill.Artistic => "Artistic",
        ColonistSkill.Medical => "Medical",
        ColonistSkill.Social => "Social",
        ColonistSkill.Intellectual => "Intellectual",
        _ => skill.ToString()
    };

    public static string PassionMarker(PassionLevel passion) => passion switch
    {
        PassionLevel.Burning => "▲▲",
        PassionLevel.Interested => "▲",
        _ => "·"
    };

    public static string RandomName(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return $"{FirstNames[random.Next(FirstNames.Length)]} {LastNames[random.Next(LastNames.Length)]}";
    }

    public static IReadOnlyList<CharacterProfile> GenerateCrew(int seed, int count, string? excludedName = null)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        var random = new Random(unchecked(seed ^ 0x6A09E667));
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(excludedName))
            used.Add(excludedName);
        var crew = new List<CharacterProfile>(count);
        for (var index = 0; index < count; index++)
        {
            var name = RandomName(random);
            for (var attempt = 0; used.Contains(name) && attempt < 64; attempt++)
                name = RandomName(random);
            used.Add(name);
            var draft = CharacterDraft.CreateRandom(random.Next(), name);
            crew.Add(draft.ToProfile(DeterministicGuid(seed, index), DateTimeOffset.UtcNow));
        }
        return crew;
    }

    private static int[] NormalizeLevels(int[]? levels)
    {
        var result = new int[SkillCount];
        if (levels is not null)
        {
            for (var i = 0; i < Math.Min(levels.Length, result.Length); i++)
                result[i] = Math.Clamp(levels[i], 0, MaximumSkillLevel);
        }
        return result;
    }

    private static PassionLevel[] NormalizePassions(PassionLevel[]? passions)
    {
        var result = new PassionLevel[SkillCount];
        if (passions is not null)
        {
            for (var i = 0; i < Math.Min(passions.Length, result.Length); i++)
                result[i] = Enum.IsDefined(passions[i]) ? passions[i] : PassionLevel.None;
        }
        return result;
    }

    private static void AddBonuses(int[] levels, int[] bonuses)
    {
        for (var i = 0; i < Math.Min(levels.Length, bonuses.Length); i++)
            levels[i] += bonuses[i];
    }

    private static int[] Empty() => new int[SkillCount];

    private static int[] Bonuses(params (ColonistSkill Skill, int Amount)[] values)
    {
        var result = new int[SkillCount];
        foreach (var (skill, amount) in values)
            result[(int)skill] += amount;
        return result;
    }

    private static Guid DeterministicGuid(int seed, int index)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(seed).CopyTo(bytes, 0);
        BitConverter.GetBytes(index).CopyTo(bytes, 4);
        BitConverter.GetBytes(unchecked(seed * 397) ^ index).CopyTo(bytes, 8);
        BitConverter.GetBytes(unchecked(seed + 7919 * (index + 1))).CopyTo(bytes, 12);
        return new Guid(bytes);
    }
}

public static class ColonistMoodCalculator
{
    public static float Calculate(SurvivalNeeds needs, ColonistStats stats, HealthComponent? health = null)
    {
        ArgumentNullException.ThrowIfNull(needs);
        ArgumentNullException.ThrowIfNull(stats);
        var mood = 0.62f + stats.MoodOffset;
        mood += (needs.FoodReserve - 0.55f) * 0.22f;
        mood += (needs.RestReserve - 0.55f) * 0.18f;
        mood += (needs.JoyReserve - 0.55f) * 0.12f;
        mood += (needs.ComfortReserve - 0.55f) * 0.10f;
        if (health is not null)
            mood -= health.PainLevel * 0.30f;
        return Math.Clamp(mood, 0f, 1f);
    }

    public static string Label(float mood) => mood switch
    {
        >= 0.80f => "Radiant",
        >= 0.65f => "Content",
        >= 0.50f => "Stable",
        >= 0.35f => "Stressed",
        >= 0.20f => "Miserable",
        _ => "Break risk"
    };
}

public sealed record CharacterProfile(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    float HitPoints = 100f,
    int MaximumHealth = 100,
    int MaximumMana = 20,
    ColonistGender Gender = ColonistGender.Female,
    int Age = 30,
    BackstoryId Backstory = BackstoryId.CrashlandedEngineer,
    ColonistTraitId[]? Traits = null,
    int[]? SkillLevels = null,
    PassionLevel[]? Passions = null,
    ColonistAppearance? Appearance = null,
    CharacterDifficulty Difficulty = CharacterDifficulty.Classic,
    bool Favorite = false)
{
    public ColonistStats Stats => ColonistCatalog.Compute(this);
    public float EffectiveMaximumHealth => Stats.MaximumHealth(MaximumHealth);
    public string BackstoryName => ColonistCatalog.GetBackstory(Backstory).Name;
    public string TraitSummary => Traits is { Length: > 0 }
        ? string.Join(", ", Traits.Select(id => ColonistCatalog.GetTrait(id).Name))
        : "No traits";
    public ColonistAppearance AppearanceValue => Appearance ?? new ColonistAppearance();

    public CharacterProfile Normalized()
    {
        var name = string.IsNullOrWhiteSpace(Name) ? "Adventurer" : Name.Trim();
        if (name.Length > 32)
            name = name[..32];
        var gender = Enum.IsDefined(Gender) ? Gender : ColonistGender.Female;
        var backstory = Enum.IsDefined(Backstory) ? Backstory : BackstoryId.CrashlandedEngineer;
        var age = Math.Clamp(Age, 16, 90);
        var traits = ColonistCatalog.NormalizeTraits(Traits);
        var maximumHealth = Math.Clamp(MaximumHealth, 1, 400);
        var maximumMana = Math.Clamp(MaximumMana, 0, 400);
        var levels = SkillLevels is { Length: ColonistCatalog.SkillCount }
            ? SkillLevels.Select(level => Math.Clamp(level, 0, ColonistCatalog.MaximumSkillLevel)).ToArray()
            : ColonistCatalog.GenerateSkillLevels(unchecked(Id.GetHashCode() ^ age), backstory, traits, MostLikelyFocus(backstory));
        var passions = Passions is { Length: ColonistCatalog.SkillCount }
            ? Passions.Select(passion => Enum.IsDefined(passion) ? passion : PassionLevel.None).ToArray()
            : ColonistCatalog.GeneratePassions(backstory, traits, MostLikelyFocus(backstory));
        var appearance = (Appearance ?? new ColonistAppearance()).Normalized();
        var profile = this with
        {
            Name = name,
            Gender = gender,
            Backstory = backstory,
            Age = age,
            Traits = traits,
            SkillLevels = levels,
            Passions = passions,
            Appearance = appearance,
            MaximumHealth = maximumHealth,
            MaximumMana = maximumMana,
            Difficulty = Enum.IsDefined(Difficulty) ? Difficulty : CharacterDifficulty.Classic
        };
        return profile with { HitPoints = Math.Clamp(HitPoints, 0f, profile.EffectiveMaximumHealth) };
    }

    private static ColonistSkill MostLikelyFocus(BackstoryId backstory)
    {
        var bonuses = ColonistCatalog.GetBackstory(backstory).SkillBonuses;
        var best = 0;
        for (var i = 1; i < bonuses.Length; i++)
        {
            if (bonuses[i] > bonuses[best])
                best = i;
        }
        return (ColonistSkill)best;
    }
}

/// <summary>Mutable character-creation draft that deterministically rebuilds skills from base rolls.</summary>
public sealed class CharacterDraft
{
    private readonly int[] _baseSkillLevels = new int[ColonistCatalog.SkillCount];
    private readonly bool[] _passionOverridden = new bool[ColonistCatalog.SkillCount];

    public string Name { get; set; } = "Adventurer";
    public ColonistGender Gender { get; set; } = ColonistGender.Female;
    public int Age { get; set; } = 30;
    public BackstoryId Backstory { get; set; } = BackstoryId.CrashlandedEngineer;
    public ColonistAppearance Appearance { get; set; } = new();
    public CharacterDifficulty Difficulty { get; set; } = CharacterDifficulty.Classic;
    public ColonistSkill Focus { get; private set; } = ColonistSkill.Mining;
    public int Seed { get; private set; }
    public List<ColonistTraitId> Traits { get; } = new();
    public int[] SkillLevels { get; } = new int[ColonistCatalog.SkillCount];
    public PassionLevel[] Passions { get; } = new PassionLevel[ColonistCatalog.SkillCount];

    public ColonistStats Stats => ColonistCatalog.Compute(Backstory, Traits, Age, SkillLevels, Passions);
    public string BackstoryName => ColonistCatalog.GetBackstory(Backstory).Name;
    public string FocusName => ColonistCatalog.SkillName(Focus);
    public string TraitSummary => Traits.Count == 0
        ? "None"
        : string.Join(", ", Traits.Select(id => ColonistCatalog.GetTrait(id).Name));

    public static CharacterDraft CreateRandom(int seed, string? name = null)
    {
        var draft = new CharacterDraft();
        draft.Randomize(seed, name);
        return draft;
    }

    public void Randomize(int seed, string? name = null)
    {
        var random = new Random(seed);
        Seed = seed;
        if (!string.IsNullOrWhiteSpace(name))
            Name = name.Trim();
        else if (string.IsNullOrWhiteSpace(Name))
            Name = ColonistCatalog.RandomName(random);

        Gender = (ColonistGender)random.Next(3);
        Age = random.Next(18, 61);
        Backstory = ColonistCatalog.Backstories[random.Next(ColonistCatalog.Backstories.Count)].Id;
        Focus = (ColonistSkill)random.Next(ColonistCatalog.SkillCount);
        Appearance = new ColonistAppearance(
            random.Next(ColonistAppearance.SkinToneCount),
            random.Next(ColonistAppearance.HairStyleCount),
            random.Next(ColonistAppearance.HairColorCount),
            random.Next(ColonistAppearance.EyeColorCount),
            random.Next(ColonistAppearance.OutfitColorCount));

        Traits.Clear();
        var traitCount = random.Next(1, 4);
        var candidates = ColonistCatalog.Traits.Select(definition => definition.Id).OrderBy(_ => random.Next()).ToArray();
        foreach (var candidate in candidates)
        {
            if (Traits.Count >= traitCount)
                break;
            if (CanSelectTrait(candidate))
                Traits.Add(candidate);
        }

        for (var i = 0; i < _baseSkillLevels.Length; i++)
            _baseSkillLevels[i] = random.Next(0, 4);
        Array.Clear(_passionOverridden);
        Rebuild();
    }

    public void RerollSkills(int seed)
    {
        Seed = seed;
        var random = new Random(seed);
        for (var i = 0; i < _baseSkillLevels.Length; i++)
            _baseSkillLevels[i] = random.Next(0, 4);
        Array.Clear(_passionOverridden);
        Rebuild();
    }

    public void SetBackstory(BackstoryId backstory)
    {
        if (!Enum.IsDefined(backstory))
            return;
        Backstory = backstory;
        Rebuild();
    }

    public void SetFocus(ColonistSkill focus)
    {
        if (!Enum.IsDefined(focus))
            return;
        Focus = focus;
        _passionOverridden[(int)focus] = false;
        Rebuild();
    }

    public void AdjustSkill(ColonistSkill skill, int amount)
    {
        if (!Enum.IsDefined(skill) || amount == 0)
            return;
        var index = (int)skill;
        _baseSkillLevels[index] = Math.Clamp(_baseSkillLevels[index] + Math.Sign(amount), 0,
            ColonistCatalog.MaximumSkillLevel);
        Rebuild();
    }

    public void CyclePassion(ColonistSkill skill)
    {
        if (!Enum.IsDefined(skill))
            return;
        var index = (int)skill;
        Passions[index] = (PassionLevel)(((int)Passions[index] + 1) % 3);
        _passionOverridden[index] = true;
    }

    public bool ToggleTrait(ColonistTraitId trait)
    {
        if (!Enum.IsDefined(trait))
            return false;
        if (Traits.Remove(trait))
        {
            Rebuild();
            return true;
        }
        if (!CanSelectTrait(trait))
            return false;
        Traits.Add(trait);
        Rebuild();
        return true;
    }

    public bool CanSelectTrait(ColonistTraitId trait)
    {
        if (Traits.Contains(trait))
            return true;
        if (Traits.Count >= 3)
            return false;
        var definition = ColonistCatalog.GetTrait(trait);
        if (definition.Conflicts?.Any(Traits.Contains) == true)
            return false;
        return !Traits.Any(existing => ColonistCatalog.GetTrait(existing).Conflicts?.Contains(trait) == true);
    }

    public void ClearTraits()
    {
        Traits.Clear();
        Rebuild();
    }

    public void RandomizeTraits(int seed)
    {
        var random = new Random(seed);
        Traits.Clear();
        var count = random.Next(1, 4);
        var candidates = ColonistCatalog.Traits.Select(definition => definition.Id).OrderBy(_ => random.Next()).ToArray();
        foreach (var candidate in candidates)
        {
            if (Traits.Count >= count)
                break;
            if (CanSelectTrait(candidate))
                Traits.Add(candidate);
        }
        Rebuild();
    }

    public CharacterProfile ToProfile(Guid id, DateTimeOffset createdAt)
    {
        var profile = new CharacterProfile(
            id,
            Name,
            createdAt,
            100f,
            100,
            20,
            Gender,
            Age,
            Backstory,
            Traits.ToArray(),
            SkillLevels.ToArray(),
            Passions.ToArray(),
            Appearance,
            Difficulty).Normalized();
        return profile with { HitPoints = profile.EffectiveMaximumHealth };
    }

    private void Rebuild()
    {
        var backstory = ColonistCatalog.GetBackstory(Backstory);
        var traitBonuses = new int[ColonistCatalog.SkillCount];
        foreach (var trait in Traits)
        {
            var bonuses = ColonistCatalog.GetTrait(trait).SkillBonuses;
            for (var i = 0; i < traitBonuses.Length; i++)
                traitBonuses[i] += bonuses[i];
        }

        for (var i = 0; i < SkillLevels.Length; i++)
        {
            var focusBonus = i == (int)Focus ? 2 : 0;
            SkillLevels[i] = Math.Clamp(
                _baseSkillLevels[i] + backstory.SkillBonuses[i] + traitBonuses[i] + focusBonus,
                0,
                ColonistCatalog.MaximumSkillLevel);
            if (_passionOverridden[i])
                continue;
            Passions[i] = i == (int)Focus
                ? PassionLevel.Burning
                : backstory.SkillBonuses[i] + traitBonuses[i] >= 3
                    ? PassionLevel.Interested
                    : PassionLevel.None;
        }
    }
}
