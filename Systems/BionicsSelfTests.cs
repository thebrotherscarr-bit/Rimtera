using System.Numerics;
using Rimtera.Characters;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Bionics (0.5.4): crafted parts replace lost ones, fitted by someone with medical skill; the coprocessor adds Intellectual; saved.</summary>
public static class BionicsSelfTests
{
    public static void Run(Action<bool, string> check)
    {
        var crafting = new CraftingSystem(new ItemCatalog());
        var recipes = new[] { ItemId.BionicArm, ItemId.BionicLeg, ItemId.BionicEye, ItemId.NeuralCoprocessor }
            .All(item => crafting.Recipes.Any(recipe => recipe.Output == item && recipe.RequiredStations?.Contains(CraftingStationId.Workbench) == true));
        check(recipes, "bionic arms, legs and eyes and the neural coprocessor are crafted at the Nano-Fabricator");

        var game = new GameSimulation(seed: 9897, width: 120, height: 56) { FamilySpawnsEnabled = false };
        var smith = game.AddTownNpc(TownNpcKind.ChiefEngineer, new Vector2(30f, 20f), bound: false);
        smith.HomeCell = new GridPoint(30, 21);
        game.Injure(smith, LastingInjury.LostArm);
        game.Player.Inventory.Add(ItemId.BionicArm, 1);
        var noMedic = game.EquipResident(smith, ItemId.BionicArm);
        var medic = game.AddTownNpc(TownNpcKind.Medic, new Vector2(40f, 20f), bound: false);
        medic.HomeCell = new GridPoint(40, 21);
        var fitted = game.EquipResident(smith, ItemId.BionicArm);
        game.Player.Inventory.Add(ItemId.BionicLeg, 1);
        var noLeg = game.EquipResident(smith, ItemId.BionicLeg);
        check(noMedic.Contains("Nobody here can fit") && fitted.Contains(medic.Name) && !smith.Injuries.Contains(LastingInjury.LostArm) &&
              smith.Bionics.Contains(BionicPart.Arm) && smith.Work.Priority(WorkType.Operate) != WorkCatalog.Off &&
              game.Player.Inventory.Count(ItemId.BionicArm) == 0 && noLeg.Contains("no lost leg") && game.Player.Inventory.Count(ItemId.BionicLeg) == 1,
            $"a bionic arm replaces a lost one (their station work comes back); it takes a medic to fit it, and a part only replaces one that was lost ('{fitted}')");

        // ---- legs and eyes work better than before; the coprocessor once ----
        var runner = game.AddTownNpc(TownNpcKind.Quartermaster, new Vector2(50f, 20f), bound: false);
        game.Injure(runner, LastingInjury.LostLeg);
        game.EquipResident(runner, ItemId.BionicLeg);
        var scholar = game.AddTownNpc(TownNpcKind.Archivist, new Vector2(60f, 20f), bound: false);
        var mind = scholar.Stats.Level(ColonistSkill.Intellectual);
        game.Player.Inventory.Add(ItemId.NeuralCoprocessor, 2);
        game.EquipResident(scholar, ItemId.NeuralCoprocessor);
        var twice = game.EquipResident(scholar, ItemId.NeuralCoprocessor);
        check(GameSimulation.LegFactor(runner.Injuries) == 1f && runner.Bionics.Contains(BionicPart.Leg) &&
              scholar.Stats.Level(ColonistSkill.Intellectual) == Math.Min(20, mind + 3) && twice.Contains("already") &&
              game.Player.Inventory.Count(ItemId.NeuralCoprocessor) == 1,
            "a bionic leg gives back their pace; a neural coprocessor adds 3 Intellectual, once");

        var reloaded = GameSimulation.Restore(game.CaptureSnapshot());
        var smithAgain = reloaded.TownNpcs.Single(npc => npc.Kind == TownNpcKind.ChiefEngineer);
        check(smithAgain.Bionics.Contains(BionicPart.Arm) && !smithAgain.Injuries.Contains(LastingInjury.LostArm) &&
              reloaded.Inspect(new InspectTarget(InspectKind.Resident, smithAgain.Id))!.Lines.Any(line => line.Contains("bionic arm")),
            "bionics are saved and shown on the inspect card");
    }
}
