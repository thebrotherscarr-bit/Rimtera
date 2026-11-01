using Rimtera.Characters;

namespace Rimtera.Systems;

/// <summary>
/// Skills grow by doing (0.5.2, docs/VISION.md §4.2): every second a person spends on a kind of work is practice in the
/// skill behind it (a chemist brewing practises Medical, a guard with a gun Shooting). Passion sets how fast it sticks, as in
/// RimWorld (none ×0.35, interested ×1, burning ×1.5); each level takes longer than the last; skills top out at 20.
/// </summary>
public sealed partial class GameSimulation
{
    /// <summary>Seconds of passionate (interested) practice to go from <paramref name="level"/> to the next.</summary>
    public static float PracticeToNext(int level) => 40f + 20f * Math.Max(0, level);

    /// <summary>How much of a second's practice sticks, by passion.</summary>
    public static float PassionFactor(PassionLevel passion) => passion switch
    {
        PassionLevel.Burning => 1.5f,
        PassionLevel.Interested => 1f,
        _ => 0.35f
    };

    /// <summary>The skill a kind of work practises for this person (guarding: Shooting with a ranged weapon, else Melee).</summary>
    public ColonistSkill SkillPractised(TownNpc npc, WorkType work) =>
        work == WorkType.Guard && npc.IsArmed && ResidentAttack(npc).Reach > UnarmedReach + 2f ? ColonistSkill.Shooting : WorkCatalog.Get(work).Skill;

    /// <summary>Practice for <paramref name="seconds"/> at a kind of work; a level gained is announced.</summary>
    internal void Practise(TownNpc npc, WorkType work, float seconds)
    {
        if (seconds <= 0f || npc.Kind == TownNpcKind.StationAi)
            return;
        var skill = SkillPractised(npc, work);
        var level = npc.Stats.Level(skill);
        if (level >= ColonistCatalog.MaximumSkillLevel)
            return;
        var gained = npc.AddPractice(skill, seconds * PassionFactor(npc.Stats.Passion(skill)));
        if (gained < PracticeToNext(level))
            return;
        npc.AddPractice(skill, -PracticeToNext(level));
        var levels = npc.Profile.SkillLevels!.ToArray();
        levels[(int)skill] = Math.Min(ColonistCatalog.MaximumSkillLevel, level + 1);
        npc.GrowSkills(npc.Profile with { SkillLevels = levels });
        PostMessage($"{npc.Name} has grown more skilled: {skill} {level + 1}.");
    }
}
