using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// The crafting guidance (Systems/CraftingGuide.cs, Systems/GameSimulation.Guide.cs): the recipe guide's lookups, the
/// "close to" list, tooltip lines, and the station AI's next steps. Checks are written against the recipe data, not
/// fixed ingredient names, so they keep holding when recipes change.
/// </summary>
public static class CraftingGuideSelfTests
{
    public static void Run(Action<bool, string> check)
    {
        var previous = CharacterFileStore.Active;
        CharacterFileStore.Active = null;
        try
        {
            Lookups(check);
            Statuses(check);
            Advice(check);
        }
        finally
        {
            CharacterFileStore.Active = previous;
        }
    }

    private static void Lookups(Action<bool, string> check)
    {
        var items = new ItemCatalog();
        var crafting = new CraftingSystem(items);
        var missedUse = crafting.Recipes.FirstOrDefault(recipe => recipe.Ingredients.SelectMany(group => group.Alternatives)
            .Any(item => !CraftingGuide.UsesOf(crafting, items, item).Contains(recipe)));
        var missedMake = crafting.Recipes.FirstOrDefault(recipe => !CraftingGuide.MadeFrom(crafting, items, recipe.Output).Contains(recipe));
        check(missedUse is null && missedMake is null,
            $"the recipe guide lists every recipe under each of its materials (Used in) and under its result (Made from) " +
            $"(missed use {missedUse?.Id ?? "none"}, missed make {missedMake?.Id ?? "none"})");

        var made = crafting.Recipes[0];
        var used = made.Ingredients[0].Alternatives[0];
        var madeLines = CraftingGuide.TooltipLines(crafting, items, made.Output);
        var usedLines = CraftingGuide.TooltipLines(crafting, items, used);
        check(madeLines.Any(line => line.StartsWith("Crafted ", StringComparison.Ordinal)) &&
              usedLines.Any(line => line.StartsWith("Used in ", StringComparison.Ordinal)) &&
              CraftingGuide.TooltipLines(crafting, items, ItemId.Credits).All(line => !line.StartsWith("Crafted", StringComparison.Ordinal) || crafting.FindByOutput(ItemId.Credits).Count > 0),
            $"item tooltips say where an item is crafted and what it is used in ({string.Join(" | ", madeLines.Concat(usedLines))})");

        var guide = new GuideLookupState();
        guide.LookUp(made.Output);
        guide.Scroll = 3;
        guide.LookUp(used, GuideView.MadeFrom);
        var deeper = guide.Item == used && guide.View == GuideView.MadeFrom && guide.Scroll == 0 && guide.CanGoBack;
        var back = guide.Back() && guide.Item == made.Output && !guide.CanGoBack && !guide.Back();
        guide.Clear();
        check(deeper && back && guide.Item is null,
            "clicking an ingredient in the recipe guide looks it up (back to the top of the list), and Back returns to the item before");

        // Sources: an item with no recipe still says where it comes from (ores are mined).
        var tiles = new TileCatalog();
        var ore = CraftingGuide.SourcesOf(items, tiles, ItemId.CopperOre);
        check(ore.Any(source => source.StartsWith("mine ", StringComparison.Ordinal) && source.Contains(tiles[TileId.CopperOre].Name, StringComparison.Ordinal)),
            $"the guide says where uncrafted materials come from, from the drop tables ({string.Join("; ", ore)})");
        var stationItems = CraftingGuide.StationItems(items, tiles, CraftingStationId.Workbench);
        check(stationItems.Count > 0 && stationItems.All(item => CraftingGuide.StationOf(items, tiles, item) == CraftingStationId.Workbench),
            "the guide finds the item that places each crafting station from the item and tile data");
    }

    private static void Statuses(Action<bool, string> check)
    {
        var items = new ItemCatalog();
        var crafting = new CraftingSystem(items);
        var none = new HashSet<CraftingStationId>();
        var handRecipe = crafting.Recipes.FirstOrDefault(recipe => recipe.RequiredStations is not { Count: > 0 } && !recipe.RequiresGuideUnlock &&
            recipe.RequiredBiomeTags is not { Count: > 0 } && recipe.RequiredLiquids is not { Count: > 0 } &&
            recipe.Ingredients.Sum(group => group.Quantity) > 1 &&
            recipe.Ingredients.SelectMany(group => group.Alternatives).Distinct().Count() == recipe.Ingredients.Sum(group => group.Alternatives.Count));
        if (handRecipe is null)
        {
            check(false, "a hand-craftable recipe exists to test the crafting statuses with");
            return;
        }
        var inventory = new Inventory(items, 40);
        var empty = CraftingGuide.Describe(crafting, handRecipe, inventory, none);
        foreach (var group in handRecipe.Ingredients)
            inventory.Add(group.Alternatives[0], group.Quantity);
        var full = CraftingGuide.Describe(crafting, handRecipe, inventory, none);
        var short1 = (handRecipe.Ingredients.FirstOrDefault(group => group.Quantity > 1) ?? handRecipe.Ingredients[0]).Alternatives[0];
        inventory.TryConsume(short1);
        var partial = CraftingGuide.Describe(crafting, handRecipe, inventory, none);
        var almost = CraftingGuide.AlmostCraftable(crafting, inventory, none);
        check(!empty.HasAllIngredients && empty.Ingredients.All(ingredient => ingredient.Have == 0) &&
              full.Craftable && full.Ingredients.All(ingredient => ingredient.Met) &&
              !partial.Craftable && partial.Ingredients.Count(ingredient => !ingredient.Met) == 1 &&
              partial.Ingredients.First(ingredient => !ingredient.Met).Have == partial.Ingredients.First(ingredient => !ingredient.Met).Need - 1 &&
              almost.Any(status => status.Recipe == handRecipe),
            $"each recipe reads as have/needed per material; one short shows the missing material and lands in the \"close to\" list ({handRecipe.Id})");

        var stationRecipe = crafting.Recipes.FirstOrDefault(recipe => recipe.RequiredStations is { Count: 1 } && !recipe.RequiresGuideUnlock &&
            recipe.RequiredBiomeTags is not { Count: > 0 } && recipe.RequiredLiquids is not { Count: > 0 });
        if (stationRecipe is not null)
        {
            var station = stationRecipe.RequiredStations!.First();
            var stocked = new Inventory(items, 40);
            foreach (var group in stationRecipe.Ingredients)
                stocked.Add(group.Alternatives[0], group.Quantity);
            var away = CraftingGuide.Describe(crafting, stationRecipe, stocked, none);
            var at = CraftingGuide.Describe(crafting, stationRecipe, stocked, new HashSet<CraftingStationId> { station });
            var awayAlmost = CraftingGuide.AlmostCraftable(crafting, stocked, none);
            var atAlmost = CraftingGuide.AlmostCraftable(crafting, stocked, new HashSet<CraftingStationId> { station });
            check(away.HasAllIngredients && !away.Craftable && away.MissingStations.SequenceEqual([station]) && at.Craftable &&
                  awayAlmost.Any(status => status.Recipe == stationRecipe) && atAlmost.All(status => status.Recipe != stationRecipe) &&
                  CraftingGuide.WhereCrafted(stationRecipe).Contains(station.DisplayName(), StringComparison.Ordinal),
                $"away from its station a recipe names the station it needs and shows as close; at the station it is simply craftable ({stationRecipe.Id})");
        }
    }

    private static GameSimulation FreshGame(int seed)
    {
        var profile = CharacterDraft.CreateRandom(seed, "Newcomer").ToProfile(Guid.NewGuid(), DateTimeOffset.UtcNow) with
            { Difficulty = CharacterDifficulty.Classic };
        var game = new GameSimulation(seed: seed, width: 120, height: 64, colonist: profile);
        // A bare landing site: no stations or lights around the player, whatever the generator put there.
        var center = game.Player.Motion.Position + game.Player.Motion.Size / 2f;
        for (var y = Math.Max(0, (int)center.Y - 40); y < Math.Min(game.World.Height, (int)center.Y + 40); y++)
        for (var x = Math.Max(0, (int)center.X - 40); x < Math.Min(game.World.Width, (int)center.X + 40); x++)
        {
            var tile = game.World.TileAt(x, y);
            if (tile != TileId.Air && game.Tiles.Contains(tile) &&
                (game.Tiles[tile].Station != CraftingStationId.None || TilePhysics.GetTileLight(tile) is not null))
                game.World.SetTile(new GridPoint(x, y), TileId.Air);
        }
        return game;
    }

    private static void Advice(Action<bool, string> check)
    {
        var game = FreshGame(7301);
        var items = game.Items;
        var workbenchItems = CraftingGuide.StationItems(items, game.Tiles, CraftingStationId.Workbench);
        foreach (var item in workbenchItems)
            while (game.Player.Inventory.Count(item) > 0)
                game.Player.Inventory.TryConsume(item);
        var tips = game.GuideTips();
        var first = tips[0];
        var recipeNames = workbenchItems.SelectMany(item => game.Crafting.FindByOutput(item))
            .SelectMany(recipe => recipe.Ingredients.SelectMany(group => group.Alternatives)).Select(item => items[item].Name).ToArray();
        check(first.Id == $"station:{CraftingStationId.Workbench}" && first.Text.Contains(CraftingStationId.Workbench.DisplayName(), StringComparison.Ordinal) &&
              (recipeNames.Length == 0 || recipeNames.Any(name => first.Text.Contains(name, StringComparison.Ordinal))) &&
              first.Focus is { } focus && workbenchItems.Contains(focus),
            $"a new player with no station is told to make the first one, with what its recipe needs ({first.Text})");
        check(tips.All(tip => !string.IsNullOrWhiteSpace(tip.Text) && !tip.Text.Contains('{') && tips.Count(other => other.Id == tip.Id) == 1),
            $"every station AI tip is a finished sentence with a unique id ({string.Join(" | ", tips.Select(tip => tip.Id))})");
        var boss = tips.FirstOrDefault(tip => tip.Id.StartsWith("boss:", StringComparison.Ordinal));
        check(boss is not null && boss.Id == $"boss:{game.NextBoss}" && boss.Text.Contains(EnemyArchetypes.Get(game.NextBoss).DisplayName, StringComparison.Ordinal),
            $"the station AI names the next boss in the challenge order and how to call it ({boss?.Text ?? "no boss tip"})");

        // Carrying the station's item: place it.
        if (workbenchItems.Count > 0)
        {
            game.Player.Inventory.Add(workbenchItems[0], 1);
            var carried = game.GuideTips()[0];
            check(carried.Id == $"place:{CraftingStationId.Workbench}" && carried.Text.Contains(items[workbenchItems[0]].Name, StringComparison.Ordinal),
                $"with the station's item in the bag, the first tip is to place it ({carried.Text})");
            game.Player.Inventory.TryConsume(workbenchItems[0]);
        }

        // A placed station moves the advice on: light, then the refinery and forge (each read from its recipe).
        var center = game.Player.Motion.Position + game.Player.Motion.Size / 2f;
        game.World.SetTile(new GridPoint((int)center.X + 2, (int)center.Y), TileId.Workbench);
        var after = game.GuideTips();
        var lightIndex = after.ToList().FindIndex(tip => tip.Id == "light");
        var furnaceIndex = after.ToList().FindIndex(tip => tip.Id == $"station:{CraftingStationId.Furnace}" || tip.Id == $"place:{CraftingStationId.Furnace}");
        var hasLight = game.Player.Inventory.Slots.Any(stack => !stack.IsEmpty && CraftingGuide.IsLightSource(items, stack.ItemId));
        var lightRecipeExists = game.Crafting.Recipes.Any(recipe => CraftingGuide.IsLightSource(items, recipe.Output));
        check(after.All(tip => tip.Id != $"station:{CraftingStationId.Workbench}") &&
              (hasLight || !lightRecipeExists || lightIndex == 0 && (furnaceIndex < 0 || furnaceIndex > lightIndex)),
            $"once the first station stands, the next step is light (then the refinery and forge) ({string.Join(" | ", after.Take(3).Select(tip => tip.Id))})");
        var furnace = after.FirstOrDefault(tip => tip.Id == $"station:{CraftingStationId.Furnace}");
        var furnaceRecipe = CraftingGuide.StationItems(items, game.Tiles, CraftingStationId.Furnace).SelectMany(item => game.Crafting.FindByOutput(item)).FirstOrDefault();
        check(furnace is null || furnaceRecipe is null ||
              furnaceRecipe.Ingredients.Any(group => group.Alternatives.Any(item => furnace.Text.Contains(items[item].Name, StringComparison.Ordinal))),
            $"the refinery tip quotes the refinery's recipe from the crafting data ({furnace?.Text ?? "no refinery tip"})");

        // The drill tip names an ore the current drill can't break and a craftable drill strong enough for it.
        var drill = after.FirstOrDefault(tip => tip.Id.StartsWith("drill:", StringComparison.Ordinal));
        var best = game.Player.Inventory.Slots.Where(stack => !stack.IsEmpty && items[stack.ItemId].UseKind == ItemUseKind.Pickaxe)
            .Select(stack => items[stack.ItemId].ToolPower).DefaultIfEmpty(0).Max();
        check(drill is null || int.Parse(drill.Id["drill:".Length..]) > best &&
              (drill.Focus is null || items[drill.Focus.Value].ToolPower >= int.Parse(drill.Id["drill:".Length..])),
            $"the drill tip points at the next drill-power gate above the best drill carried ({drill?.Text ?? "no drill tip"})");

        // Learning: a guide-taught recipe's material in the bag brings the "talk to me" tip.
        var taught = game.Crafting.Recipes.FirstOrDefault(recipe => recipe.RequiresGuideUnlock && !game.Crafting.KnownGuideRecipes.Contains(recipe.Id));
        if (taught is not null)
        {
            game.Player.Inventory.Add(taught.GuideUnlockMaterials!.First(), 1);
            check(game.GuideTips().Any(tip => tip.Id == "reveal"),
                $"carrying a material the station AI can teach from brings a tip to talk to it ({taught.Id})");
        }
    }
}
