using System.Numerics;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Skills grow by doing (0.5.2): work practises its skill, passion speeds it, levels get dearer, practice is saved.</summary>
public static class SkillGrowthSelfTests
{
    public static void Run(Action<bool, string> check)
    {
        var game = new GameSimulation(seed: 9893, width: 120, height: 56) { FamilySpawnsEnabled = false };
        var cook = game.AddTownNpc(TownNpcKind.Cook, new Vector2(30f, 20f), bound: false);
        var before = cook.Stats.Level(ColonistSkill.Cooking);
        var passion = cook.Stats.Passion(ColonistSkill.Cooking);
        var seconds = GameSimulation.PracticeToNext(before) / GameSimulation.PassionFactor(passion);
        for (var t = 0f; t < seconds + 1f; t += 1f / 60f)
            game.Practise(cook, WorkType.Cook, 1f / 60f);
        var grew = cook.Stats.Level(ColonistSkill.Cooking) == before + 1 && game.LastMessage.Contains("Cooking");
        check(grew && GameSimulation.PracticeToNext(15) > GameSimulation.PracticeToNext(5) &&
              GameSimulation.PassionFactor(PassionLevel.Burning) > GameSimulation.PassionFactor(PassionLevel.Interested) &&
              GameSimulation.PassionFactor(PassionLevel.Interested) > GameSimulation.PassionFactor(PassionLevel.None),
            $"a cook who cooks gets better at it (Cooking {before} → {cook.Stats.Level(ColonistSkill.Cooking)} after {seconds:0} s with a {passion} passion); " +
            $"passion speeds it, and each level takes longer than the last");

        // ---- a guard with a gun practises Shooting; the station AI does not learn; practice and levels are saved ----
        var guard = game.AddTownNpc(TownNpcKind.Armorer, new Vector2(40f, 20f), bound: false);
        game.Player.Inventory.Add(ItemId.ScrapPistol, 1);
        game.EquipResident(guard, ItemId.ScrapPistol);
        game.Practise(guard, WorkType.Guard, 5f);
        var ai = game.AddTownNpc(TownNpcKind.StationAi, new Vector2(50f, 20f), bound: false);
        game.Practise(ai, WorkType.Research, 500f);
        var reloaded = GameSimulation.Restore(game.CaptureSnapshot());
        var cookAgain = reloaded.TownNpcs.Single(npc => npc.Kind == TownNpcKind.Cook);
        var guardAgain = reloaded.TownNpcs.Single(npc => npc.Kind == TownNpcKind.Armorer);
        check(game.SkillPractised(guard, WorkType.Guard) == ColonistSkill.Shooting && guard.PracticeIn(ColonistSkill.Shooting) > 0f &&
              ai.PracticeIn(ColonistSkill.Intellectual) == 0f && cookAgain.Stats.Level(ColonistSkill.Cooking) == before + 1 &&
              MathF.Abs(guardAgain.PracticeIn(ColonistSkill.Shooting) - guard.PracticeIn(ColonistSkill.Shooting)) < 0.001f,
            "a guard with a gun practises Shooting; the station AI does not learn; skills and practice are saved");

        // ---- in the world: someone tending the hurt practises Medical as the world runs ----
        var settlers = new GameSimulation(seed: 9894, width: 120, height: 56) { FamilySpawnsEnabled = false };
        settlers.SetTimeOfDay(10f);
        var worker = settlers.AddStartingCrewForChecks()[0];
        worker.HomeCell = new GridPoint((int)worker.Motion.Position.X, (int)worker.Motion.Position.Y);
        foreach (var type in Enum.GetValues<WorkType>())
            worker.Work.Set(type, WorkCatalog.Off);
        worker.Work.Set(WorkType.Tend, WorkCatalog.Highest);
        var patient = settlers.AddTownNpc(TownNpcKind.Quartermaster, worker.Motion.Position + new Vector2(2f, 0f), bound: false);
        patient.HomeCell = worker.HomeCell;
        for (var i = 0; i < 600; i++)
        {
            patient.HitPoints = Math.Min(patient.HitPoints, 60f);
            settlers.Tick(new GameInput(new MovementInput(0f)));
        }
        check(worker.PracticeIn(ColonistSkill.Medical) > 0f,
            $"someone tending the hurt practises Medical as the world runs (practice {worker.PracticeIn(ColonistSkill.Medical):0.0}, {worker.Activity})");
    }
}
