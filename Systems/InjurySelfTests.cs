using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Lasting injuries (0.5.3): grave hits can leave them; a lost arm limits work; a lost leg slows; saved; the console gives them.</summary>
public static class InjurySelfTests
{
    public static void Run(Action<bool, string> check)
    {
        var game = new GameSimulation(seed: 9895, width: 120, height: 56) { FamilySpawnsEnabled = false };
        var smith = game.AddTownNpc(TownNpcKind.ChiefEngineer, new Vector2(30f, 20f), bound: false);
        game.Player.Inventory.Add(ItemId.ScrapPistol, 1);
        game.EquipResident(smith, ItemId.ScrapPistol);
        var armed = smith.IsArmed;
        game.Injure(smith, LastingInjury.LostArm);
        check(armed && !smith.IsArmed && game.Player.Inventory.Count(ItemId.ScrapPistol) == 1 &&
              smith.Work.Priority(WorkType.Operate) == WorkCatalog.Off && smith.Work.Priority(WorkType.Guard) == WorkCatalog.Off &&
              smith.Work.Priority(WorkType.Farm) == WorkCatalog.Off && smith.Work.Priority(WorkType.Research) != WorkCatalog.Off &&
              game.EquipResident(smith, ItemId.ScrapPistol).Contains("will not fight") &&
              game.Inspect(new InspectTarget(InspectKind.Resident, smith.Id))!.Lines.Any(line => line.Contains("lost arm")),
            "a lost arm limits someone to one-handed work: no crafting at stations, farming, fishing or fighting (their weapon comes back), though they still study");

        var runner = game.AddTownNpc(TownNpcKind.Quartermaster, new Vector2(40f, 20f), bound: false);
        game.Injure(runner, LastingInjury.LostLeg);
        var eyes = game.AddTownNpc(TownNpcKind.Armorer, new Vector2(50f, 20f), bound: false);
        game.Player.Inventory.Add(ItemId.ScrapPistol, 1);
        game.EquipResident(eyes, ItemId.ScrapPistol);
        var aim = game.ResidentAttack(eyes).Damage;
        game.Injure(eyes, LastingInjury.LostEye);
        var thirdEye = game.Injure(eyes, LastingInjury.LostEye) && !game.Injure(eyes, LastingInjury.LostEye);
        check(GameSimulation.LegFactor(runner.Injuries) == 0.6f && runner.Work.Priority(WorkType.Guard) == WorkCatalog.Off &&
              game.ResidentAttack(eyes).Damage < aim && thirdEye,
            "a lost leg slows them (60%) and keeps them from guarding; a lost eye blunts their aim; nobody loses more than two eyes");

        // ---- grave hits leave them, sometimes; injuries are saved; the console gives them ----
        var hurt = 0;
        var trials = new GameSimulation(seed: 9896, width: 120, height: 56) { FamilySpawnsEnabled = false };
        for (var i = 0; i < 60; i++)
        {
            var victim = trials.AddTownNpc(TownNpcKind.Settler, new Vector2(30f + i, 20f), bound: false);
            trials.HurtTownNpc(victim, victim.MaximumHitPoints * 0.5f);
            if (victim.Injuries.Count > 0)
                hurt++;
            trials.HurtTownNpc(victim, 5f); // a scratch never does
        }
        var reloaded = GameSimulation.Restore(game.CaptureSnapshot());
        var smithAgain = reloaded.TownNpcs.Single(npc => npc.Kind == TownNpcKind.ChiefEngineer);
        var console = new DevConsole(game);
        game.Player.Motion.Position = runner.Motion.Position;
        var scarred = console.Execute("injure scar")[1];
        check(hurt is > 3 and < 30 && smithAgain.Injuries.Contains(LastingInjury.LostArm) && smithAgain.Work.Priority(WorkType.Operate) == WorkCatalog.Off &&
              scarred.Contains("scarred") && runner.Injuries.Contains(LastingInjury.Scar),
            $"a grave hit sometimes leaves a lasting injury ({hurt} of 60); injuries are saved; the console's injure gives one ('{scarred}')");
    }
}
