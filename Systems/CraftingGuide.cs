using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>One ingredient group of a recipe against what the player carries: the alternative shown, have and need.</summary>
public sealed record IngredientStatus(IngredientRequirement Requirement, ItemId Item, int Have, int Need)
{
    public bool Met => Have >= Need;
}

/// <summary>
/// A recipe read against the player's state, for the crafting panel and the station AI's recipe guide: each ingredient
/// as have/needed, the stations it needs and which are missing, any other condition (biome, liquid) not met, and
/// whether the station AI still has to teach it.
/// </summary>
public sealed record RecipeStatus(
    RecipeDefinition Recipe,
    IReadOnlyList<IngredientStatus> Ingredients,
    IReadOnlyList<CraftingStationId> Stations,
    IReadOnlyList<CraftingStationId> MissingStations,
    IReadOnlyList<string> MissingConditions,
    bool Locked)
{
    public bool HasAllIngredients => Ingredients.All(ingredient => ingredient.Met);
    public bool AtStations => MissingStations.Count == 0 && MissingConditions.Count == 0;
    public bool Craftable => !Locked && AtStations && HasAllIngredients;
    public int MissingIngredientCount => Ingredients.Count(ingredient => !ingredient.Met);
    /// <summary>0..1: how much of the recipe's material the player already carries.</summary>
    public float Progress => Ingredients.Count == 0 ? 1f
        : Ingredients.Average(ingredient => Math.Min(1f, ingredient.Have / (float)Math.Max(1, ingredient.Need)));
}

/// <summary>Which half of the station AI's recipe guide is showing.</summary>
public enum GuideView : byte { UsedIn, MadeFrom }

/// <summary>
/// Read-only recipe queries for the guide panel, the crafting panel's "almost craftable" list, tooltips and the
/// station AI's tips. Everything is asked of <see cref="CraftingSystem"/> and the item and tile catalogs, so it stays
/// right when recipe data changes; nothing here edits recipes.
/// </summary>
public static class CraftingGuide
{
    /// <summary>Every recipe that uses <paramref name="item"/> (Terraria's Guide: hand him an item), ordered by tier then name.</summary>
    public static IReadOnlyList<RecipeDefinition> UsesOf(CraftingSystem crafting, ItemCatalog items, ItemId item) =>
        Order(crafting.FindByIngredient(item), items);

    /// <summary>Every recipe that produces <paramref name="item"/>.</summary>
    public static IReadOnlyList<RecipeDefinition> MadeFrom(CraftingSystem crafting, ItemCatalog items, ItemId item) =>
        Order(crafting.FindByOutput(item), items);

    private static IReadOnlyList<RecipeDefinition> Order(IEnumerable<RecipeDefinition> recipes, ItemCatalog items) => recipes
        .OrderBy(recipe => items[recipe.Output].Tier)
        .ThenBy(recipe => recipe.RequiredStations?.Count ?? 0)
        .ThenBy(recipe => items[recipe.Output].Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(recipe => recipe.Id, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>Reads <paramref name="recipe"/> against the inventory and the stations (and biome / liquid conditions) in reach.</summary>
    public static RecipeStatus Describe(CraftingSystem crafting, RecipeDefinition recipe, Inventory inventory,
        IReadOnlySet<CraftingStationId> nearbyStations, CraftingEnvironment? environment = null)
    {
        var ingredients = recipe.Ingredients.Select(group =>
        {
            var best = group.Alternatives[0];
            var have = inventory.Count(best);
            foreach (var alternative in group.Alternatives.Skip(1))
            {
                var count = inventory.Count(alternative);
                if (count > have)
                {
                    best = alternative;
                    have = count;
                }
            }
            return new IngredientStatus(group, best, have, group.Quantity);
        }).ToArray();
        var stations = (recipe.RequiredStations ?? new HashSet<CraftingStationId>()).OrderBy(station => (int)station).ToArray();
        var missing = stations.Where(station => !nearbyStations.Contains(station)).ToArray();
        var conditions = new List<string>();
        foreach (var biome in recipe.RequiredBiomeTags ?? new HashSet<string>())
            if (environment?.BiomeTags?.Contains(biome) != true)
                conditions.Add($"in a {biome} area");
        foreach (var liquid in recipe.RequiredLiquids ?? new HashSet<LiquidKind>())
            if (environment?.NearbyLiquids?.Contains(liquid) != true)
                conditions.Add($"near {liquid.ToString().ToLowerInvariant()}");
        var locked = recipe.RequiresGuideUnlock && !crafting.KnownGuideRecipes.Contains(recipe.Id);
        return new RecipeStatus(recipe, ingredients, stations, missing, conditions, locked);
    }

    /// <summary>
    /// Recipes the player is close to: at the stations in reach with some of the material but not all of it, or with
    /// every material but away from the station. Already-craftable recipes are left out. Closest first.
    /// </summary>
    public static IReadOnlyList<RecipeStatus> AlmostCraftable(CraftingSystem crafting, Inventory inventory,
        IReadOnlySet<CraftingStationId> nearbyStations, CraftingEnvironment? environment = null, int limit = 120)
    {
        var carried = inventory.Slots.Where(stack => !stack.IsEmpty).Select(stack => stack.ItemId).ToHashSet();
        var candidates = new HashSet<RecipeDefinition>();
        foreach (var item in carried)
            candidates.UnionWith(crafting.FindByIngredient(item));
        var result = new List<RecipeStatus>();
        foreach (var recipe in candidates)
        {
            var status = Describe(crafting, recipe, inventory, nearbyStations, environment);
            if (status.Craftable || status.Locked || status.MissingConditions.Count > 0)
                continue;
            var atStations = status.MissingStations.Count == 0;
            var someMaterial = status.Ingredients.Any(ingredient => ingredient.Have > 0);
            if (atStations && someMaterial && !status.HasAllIngredients || !atStations && status.HasAllIngredients)
                result.Add(status);
        }
        return result
            .OrderBy(status => status.MissingStations.Count + status.MissingIngredientCount)
            .ThenByDescending(status => status.Progress)
            .ThenBy(status => status.Recipe.Id, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();
    }

    /// <summary>"by hand", "at a Nano-Fabricator", "at a Power Forge and a Chem Lab".</summary>
    public static string WhereCrafted(RecipeDefinition recipe) =>
        recipe.RequiredStations is not { Count: > 0 } stations
            ? "by hand"
            : "at " + string.Join(" and ", stations.OrderBy(station => (int)station).Select(station => $"{Article(station.DisplayName())} {station.DisplayName()}"));

    /// <summary>Two short tooltip lines: where the item is crafted and what it is used in (nothing for unrelated items).</summary>
    public static IReadOnlyList<string> TooltipLines(CraftingSystem crafting, ItemCatalog items, ItemId item)
    {
        var lines = new List<string>(2);
        var made = crafting.FindByOutput(item);
        if (made.Count > 0)
        {
            var where = made.Select(WhereCrafted).Distinct().ToArray();
            lines.Add($"Crafted {where[0]}{(where.Length > 1 ? $" (or {where.Length - 1} other way{(where.Length > 2 ? "s" : "")})" : string.Empty)}");
        }
        var uses = crafting.FindByIngredient(item);
        if (uses.Count > 0)
        {
            var names = uses.Select(recipe => items[recipe.Output].Name).Distinct().ToArray();
            lines.Add(names.Length <= 2
                ? $"Used in {string.Join(", ", names)}"
                : $"Used in {names[0]}, {names[1]} and {names.Length - 2} more");
        }
        return lines;
    }

    private static readonly Dictionary<ItemId, IReadOnlyList<string>> SourceCache = new();
    private static readonly Dictionary<CraftingStationId, IReadOnlyList<ItemId>> StationItemCache = new();
    private static readonly object CacheLock = new();

    /// <summary>
    /// Where an item comes from when it isn't crafted: the item's own block dug up, trees that yield it when felled,
    /// natural tiles that drop it when mined, creatures that drop it. Read from the drop tables, so new content shows
    /// up without new code. Player-built tiles (doors, statues, furniture) don't count as sources.
    /// </summary>
    public static IReadOnlyList<string> SourcesOf(ItemCatalog items, TileCatalog tiles, ItemId item)
    {
        lock (CacheLock)
            if (SourceCache.TryGetValue(item, out var cached))
                return cached;
        var placeable = PlaceableTiles(items);
        var own = items.Contains(item) ? items[item].PlacedTile : TileId.Air;
        var mined = new List<string>();
        var trees = new List<string>();
        foreach (var tile in Enum.GetValues<TileId>().Distinct())
        {
            if (tile == TileId.Air || !tiles.Contains(tile))
                continue;
            try
            {
                if (TilePhysics.IsTrunk(tile))
                {
                    if (ContentFillFlora.TimberFor(tile) == item)
                        trees.Add(tiles[tile].Name);
                    continue;
                }
                if (tiles[tile].Station != CraftingStationId.None || TileDropTable.GetDrop(tile) != item || tile != own && placeable.Contains(tile))
                    continue;
                var power = tiles[tile].MinimumToolPower;
                var line = tile == own ? $"mine {tiles[tile].Name} blocks" : $"mine {tiles[tile].Name}";
                line = power > 0 ? $"{line} (drill power {power})" : line;
                if (tile == own)
                    mined.Insert(0, line);
                else
                    mined.Add(line);
            }
            catch (Exception exception) when (exception is KeyNotFoundException or ArgumentException or InvalidOperationException)
            {
                // A tile without full data: not a source.
            }
        }
        var sources = new List<string>();
        if (trees.Count > 0)
            sources.Add(trees.Count <= 2 ? $"chop trees ({string.Join(", ", trees.Distinct())})" : "chop trees");
        sources.AddRange(mined.Take(3));
        var droppers = EnemyArchetypes.All
            .Where(archetype => NpcDropRegistry.GetDrops(archetype.Kind).Any(drop => drop.Item == item))
            .Select(archetype => archetype.DisplayName)
            .Distinct()
            .ToArray();
        if (droppers.Length > 0)
            sources.Add(droppers.Length <= 2 ? $"dropped by {string.Join(" and ", droppers)}" : $"dropped by {droppers[0]}, {droppers[1]} and others");
        var result = sources.Distinct().Take(4).ToArray();
        lock (CacheLock)
            SourceCache[item] = result;
        return result;
    }

    private static HashSet<TileId>? _placeable;

    /// <summary>Tiles some item places: built things, which only drop back what was placed.</summary>
    private static HashSet<TileId> PlaceableTiles(ItemCatalog items)
    {
        lock (CacheLock)
            return _placeable ??= items.All.Select(definition => definition.PlacedTile).Where(tile => tile != TileId.Air).ToHashSet();
    }

    /// <summary>The items that place <paramref name="station"/> (the Nano-Fabricator item for the Nano-Fabricator).</summary>
    public static IReadOnlyList<ItemId> StationItems(ItemCatalog items, TileCatalog tiles, CraftingStationId station)
    {
        lock (CacheLock)
            if (StationItemCache.TryGetValue(station, out var cached))
                return cached;
        var found = items.All
            .Where(item => item.PlacedTile != TileId.Air && tiles.Contains(item.PlacedTile) && tiles[item.PlacedTile].Station == station)
            .Select(item => item.Id)
            .ToArray();
        lock (CacheLock)
            StationItemCache[station] = found;
        return found;
    }

    /// <summary>The station a placeable item sets down, or None.</summary>
    public static CraftingStationId StationOf(ItemCatalog items, TileCatalog tiles, ItemId item) =>
        items.Contains(item) && items[item].PlacedTile is var tile && tile != TileId.Air && tiles.Contains(tile)
            ? tiles[tile].Station
            : CraftingStationId.None;

    /// <summary>True for items that place a light (torches, lamps).</summary>
    public static bool IsLightSource(ItemCatalog items, ItemId item) =>
        items.Contains(item) && items[item].PlacedTile is var tile && tile != TileId.Air && TilePhysics.GetTileLight(tile) is not null;

    /// <summary>"a" or "an" for a name.</summary>
    public static string Article(string name) =>
        name.Length > 0 && "AEIOUaeiou".Contains(name[0]) ? "an" : "a";
}

/// <summary>
/// The guide panel's lookup state, renderer-independent: the item in the slot, which view shows (recipes that use it
/// or recipes that make it), the scroll row, and a history so a click on an ingredient can be undone with Back.
/// </summary>
public sealed class GuideLookupState
{
    private readonly List<ItemId> _history = new();

    public ItemId? Item { get; private set; }
    public GuideView View { get; set; } = GuideView.UsedIn;
    public int Scroll { get; set; }
    public IReadOnlyList<ItemId> History => _history;
    public bool CanGoBack => _history.Count > 0;

    /// <summary>Looks an item up (from the slot or an ingredient click). The previous item goes on the history.</summary>
    public void LookUp(ItemId item, GuideView? view = null)
    {
        if (Item is { } previous && previous != item)
            _history.Add(previous);
        if (_history.Count > 32)
            _history.RemoveAt(0);
        Item = item;
        if (view is { } chosen)
            View = chosen;
        Scroll = 0;
    }

    public bool Back()
    {
        if (_history.Count == 0)
            return false;
        Item = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        Scroll = 0;
        return true;
    }

    public void Clear()
    {
        Item = null;
        _history.Clear();
        Scroll = 0;
    }
}
