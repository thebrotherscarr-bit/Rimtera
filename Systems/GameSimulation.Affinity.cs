using Rimtera.Characters;

namespace Rimtera.Systems;

/// <summary>What a worker's skills give at a powered station (0.7.7): more output, or better rolls.</summary>
public enum StationAffinity : byte { Output, Quality }

/// <summary>
/// Station affinity (0.7.7, the power grid; like RimWorld's): at a powered station a worker's skills sometimes give more:
/// hands-on skills (Crafting, Cooking, Plants, Mining, Construction) an extra batch, head skills (Intellectual, Artistic,
/// Medical) a better calibration roll (the better of two). The chance grows with the skill: 2.5% a level, 50% at most. You
/// get a small powered bonus of your own: a 10% chance of an extra batch, and a 15% chance at a better roll.
/// </summary>
public sealed partial class GameSimulation
{
    public const float AffinityPerLevel = 0.025f, MaximumAffinity = 0.5f, PlayerPoweredExtra = 0.1f, PlayerPoweredLuck = 0.15f;

    private static readonly ColonistSkill[] HandsSkills =
        [ColonistSkill.Crafting, ColonistSkill.Cooking, ColonistSkill.Plants, ColonistSkill.Mining, ColonistSkill.Construction];
    private static readonly ColonistSkill[] HeadSkills = [ColonistSkill.Intellectual, ColonistSkill.Artistic, ColonistSkill.Medical];

    /// <summary>A worker's affinity: the kind their best hands or head skill gives, its skill, and its chance.</summary>
    public static (StationAffinity Kind, ColonistSkill Skill, float Chance) AffinityOf(ColonistStats stats)
    {
        var hands = HandsSkills.OrderByDescending(stats.Level).First();
        var head = HeadSkills.OrderByDescending(stats.Level).First();
        var (kind, skill) = stats.Level(head) > stats.Level(hands) ? (StationAffinity.Quality, head) : (StationAffinity.Output, hands);
        return (kind, skill, Math.Clamp(stats.Level(skill) * AffinityPerLevel, 0f, MaximumAffinity));
    }

    /// <summary>
    /// A powered craft's affinity: an extra batch (stackable output) or the better of two calibration rolls, by chance.
    /// Returns the amount made and the calibration.
    /// </summary>
    internal (int Made, Calibration Calibration) ApplyAffinity(ColonistStats stats, ItemDefinition output, int batch, int made, Calibration calibration)
    {
        var (kind, _, chance) = AffinityOf(stats);
        if (kind == StationAffinity.Output && output.MaxStack > 1 && CraftRandom.NextDouble() < chance)
            return (made + batch, calibration);
        if (kind == StationAffinity.Quality && (output.Calibratable || CalibrationCatalog.IsAccessory(output)))
            return (made, LuckyCalibration(CraftRandom, CraftRandom, output, chance));
        return (made, calibration);
    }

    /// <summary>A line for the inspect card.</summary>
    public static string AffinityLine(ColonistStats stats)
    {
        var (kind, skill, chance) = AffinityOf(stats);
        return $"Affinity: {(kind == StationAffinity.Output ? "more output" : "better rolls")} at powered stations ({skill}, {chance:P0})";
    }
}
