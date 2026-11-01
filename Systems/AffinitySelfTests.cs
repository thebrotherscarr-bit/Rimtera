using System.Numerics;
using Rimtera.Characters;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Station affinity (0.7.7): at a powered station a worker's best hands skill gives extra batches, their best head skill
/// better calibration rolls, more often the better they are; you get a small powered bonus too; the card shows it.
/// </summary>
public static class AffinitySelfTests
{
    private const int Floor = 40;

    public static void Run(Action<bool, string> check)
    {
        var game = new GameSimulation(seed: 9811, width: 120, height: 56) { FamilySpawnsEnabled = false };
        for (var y = 1; y < game.World.Height - 1; y++)
        for (var x = 1; x < game.World.Width - 1; x++)
            game.World.SetTile(new GridPoint(x, y), y >= Floor ? TileId.Rock : TileId.Air);
        game.Player.Motion.Position = new Vector2(20f, Floor - game.Player.Motion.Size.Y - 0.01f);

        // ---- which affinity, and how strong ----
        var smith = game.AddTownNpc(TownNpcKind.Settler, new Vector2(30f, Floor - 3f), bound: false);
        var levels = smith.Stats.SkillLevels;
        Array.Clear(levels);
        levels[(int)ColonistSkill.Crafting] = 12;
        var hands = GameSimulation.AffinityOf(smith.Stats);
        levels[(int)ColonistSkill.Intellectual] = 16;
        var head = GameSimulation.AffinityOf(smith.Stats);
        levels[(int)ColonistSkill.Intellectual] = 40;
        var capped = GameSimulation.AffinityOf(smith.Stats);
        check(hands is { Kind: StationAffinity.Output, Skill: ColonistSkill.Crafting } && MathF.Abs(hands.Chance - 0.3f) < 0.001f &&
              head is { Kind: StationAffinity.Quality, Skill: ColonistSkill.Intellectual } && MathF.Abs(head.Chance - 0.4f) < 0.001f &&
              capped.Chance == GameSimulation.MaximumAffinity &&
              game.Inspect(new InspectTarget(InspectKind.Resident, smith.Id))!.Lines.Any(line => line.StartsWith("Affinity: better rolls")),
            $"a worker's best hands skill gives more output, their best head skill better rolls ({hands.Chance:P0}, {head.Chance:P0}, at most {GameSimulation.MaximumAffinity:P0}); the card says which");

        // ---- an output affinity makes extra batches about as often as its chance; a quality one rolls better ----
        Array.Clear(levels);
        levels[(int)ColonistSkill.Crafting] = 12;
        var stackable = game.Items[ItemId.CircuitBoard];
        var extra = Enumerable.Range(0, 2000).Count(_ => game.ApplyAffinity(smith.Stats, stackable, 1, 1, Calibration.None).Made == 2);
        Array.Clear(levels);
        levels[(int)ColonistSkill.Intellectual] = 20;
        var blade = game.Items.All.First(item => item.Calibratable && item.MaxStack == 1);
        var dice = new Random(7);
        double MeanValue(Func<Calibration> roll) => Enumerable.Range(0, 3000).Average(_ => CalibrationCatalog.Get(roll()).ValueMultiplier);
        var plain = MeanValue(() => CalibrationCatalog.Roll(dice, blade));
        var better = MeanValue(() => game.ApplyAffinity(smith.Stats, blade, 1, 1, CalibrationCatalog.Roll(dice, blade)).Calibration);
        check(extra is > 500 and < 700 && better > plain,
            $"an output affinity (30%) makes extra batches about that often ({extra} of 2000); a quality affinity rolls better on average ({plain:0.000} → {better:0.000})");

        // ---- you get a small powered bonus: now and then an extra batch ----
        game.World.SetTile(new GridPoint(22, Floor - 1), TileId.Workbench);
        var crude = game.Crafting.FindByOutput(ItemId.CrudeFuelCrystal).First(recipe => recipe.Ingredients.Any(i => i.Alternatives.Contains(ItemId.WoodBlock)));
        int Crafted(int power)
        {
            new DevConsole(game).Execute($"power set {power}");
            new DevConsole(game).Execute("power tier 1");
            var before = game.Player.Inventory.Count(ItemId.CrudeFuelCrystal);
            for (var i = 0; i < 300; i++)
            {
                game.Player.Inventory.Add(ItemId.WoodBlock, 4);
                game.TryCraft(crude.Id);
                if (game.Player.Inventory.Count(ItemId.CrudeFuelCrystal) > 900)
                    game.Player.Inventory.TryConsume(ItemId.CrudeFuelCrystal, 900);
            }
            return (game.Player.Inventory.Count(ItemId.CrudeFuelCrystal) - before + 900) % 900;
        }
        var unpowered = Crafted(0);
        var powered = Crafted(1000);
        check(unpowered == 300 && powered > 310,
            $"crafting at a powered machine gives you an extra batch now and then ({unpowered} unpowered, {powered} powered, of 300 crafts)");
    }
}
