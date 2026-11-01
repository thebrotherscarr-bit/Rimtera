using System.Numerics;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Fuel and the refiner (0.7.5, the power grid): crude fuel crystals from junk at the Nano-Fabricator; a fuel refiner made
/// there turns tiered junk and ore into crystals of the ore's tier, more from better input; a tier's crystal raises the
/// station AI to it; machines craft unpowered and run powered (worked faster) at the AI's tier; the AI works the refiner.
/// </summary>
public static class FuelSelfTests
{
    private const int Floor = 40;

    private static GameSimulation Site(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 120, height: 56) { FamilySpawnsEnabled = false };
        for (var y = 1; y < game.World.Height - 1; y++)
        for (var x = 1; x < game.World.Width - 1; x++)
        {
            var cell = new GridPoint(x, y);
            game.World.SetTile(cell, y >= Floor ? TileId.Rock : TileId.Air);
            var data = game.World.GetCell(cell);
            data.LiquidDepth = 0f;
            data.LiquidKind = LiquidKind.None;
            game.World.SetCell(cell, data);
        }
        game.Player.Motion.Position = new Vector2(20f, Floor - game.Player.Motion.Size.Y - 0.01f);
        game.Player.Motion.Velocity = Vector2.Zero;
        return game;
    }

    public static void Run(Action<bool, string> check)
    {
        // ---- the items, the refiner and its recipes ----
        var game = Site(9701);
        var crude = game.Crafting.FindByOutput(ItemId.CrudeFuelCrystal);
        var refiner = game.Crafting.FindByOutput(ItemId.FuelRefiner).SingleOrDefault();
        var tiers = Enumerable.Range(1, 5).Select(tier => game.Items[(ItemId)((int)ItemId.FuelCrystalT1 + tier - 1)].Tier).ToArray();
        var betterInput = GameSimulation.RefinerInputs.GroupBy(input => input.Tier).All(group =>
            group.Max(input => input.Made / (float)input.Count) > group.Min(input => input.Made / (float)input.Count));
        var everyTier = Enumerable.Range(0, 6).All(tier => GameSimulation.RefinerInputs.Any(input => input.Tier == tier));
        check(crude.Any(recipe => recipe.RequiredStations!.Contains(CraftingStationId.Workbench)) &&
              refiner?.RequiredStations?.Contains(CraftingStationId.Workbench) == true &&
              tiers.SequenceEqual([1, 2, 3, 4, 5]) && betterInput && everyTier &&
              game.Tiles[TileId.FuelRefiner].Station == CraftingStationId.FuelRefiner &&
              GameSimulation.PowerTierOf(CraftingStationId.FuelRefiner) == 0,
            "crude fuel crystals and the fuel refiner are made at the Nano-Fabricator; the refiner makes crystals of every tier, more from better input, and needs no power");

        // ---- refining ore at the refiner, unpowered, into the ore's tier ----
        game.World.SetTile(new GridPoint(22, Floor - 1), TileId.FuelRefiner);
        game.Player.Inventory.Add(ItemId.CobaltOre, 3);
        game.Player.Inventory.Add(ItemId.CobaltBar, 1);
        var oreRecipe = game.Crafting.FindByOutput(ItemId.FuelCrystalT2).First(recipe => recipe.Ingredients.Any(i => i.Alternatives.Contains(ItemId.CobaltOre)));
        var barRecipe = game.Crafting.FindByOutput(ItemId.FuelCrystalT2).First(recipe => recipe.Ingredients.Any(i => i.Alternatives.Contains(ItemId.CobaltBar)));
        var fromOre = game.TryCraft(oreRecipe.Id).Success;
        var fromBar = game.TryCraft(barRecipe.Id).Success;
        check(fromOre && fromBar && game.Player.Inventory.Count(ItemId.FuelCrystalT2) == 3,
            $"the refiner turns cobalt ore and a cobalt bar into tier 2 fuel crystals, more from the bar ({game.Player.Inventory.Count(ItemId.FuelCrystalT2)})");

        // ---- powered machines: at the AI's tier, with power; fed a tier's crystal, it rises; down, nothing runs powered ----
        var dark = !game.StationPowered(CraftingStationId.PowerForgeMk2) && game.StationPowerTier == 0;
        game.FeedStationAi();
        var lit = game.StationPowered(CraftingStationId.PowerForgeMk2) && game.StationPowered(CraftingStationId.Anvil) &&
                  !game.StationPowered(CraftingStationId.AlloyAnvil) && !game.StationPowered(CraftingStationId.Sawmill);
        new DevConsole(game).Execute("ai down");
        var down = !game.StationPowered(CraftingStationId.PowerForgeMk2);
        check(dark && lit && down && GameSimulation.PoweredWorkSpeed > 1f,
            $"machines run powered (worked {GameSimulation.PoweredWorkSpeed:0.##}× faster) only at the AI's tier: tier 2 crystals light the Mk2 forge, not the alloy anvil; hand-built stations and a downed AI have none");

        // ---- the station AI works the refiner; crafting is never gated by power ----
        var astral = game.Crafting.Recipes.First(recipe => recipe.RequiredStations?.Contains(CraftingStationId.AstralAssembler) == true);
        check(TownTrades.TradeOf(TownNpcKind.StationAi).Contains(CraftingStationId.FuelRefiner) && game.ResearchLock(astral)?.Contains("power") != true,
            "the station AI works the fuel refiner, and no recipe is locked by power, only by research");
    }
}
