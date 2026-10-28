using System.Numerics;
using Rimtera.World;

namespace Rimtera.Systems;

public sealed record IngredientRequirement(IReadOnlyList<ItemId> Alternatives, int Quantity)
{
    public static IngredientRequirement Exact(ItemId itemId, int quantity) =>
        new(new[] { itemId }, quantity);
}

public sealed record RecipeDefinition(
    string Id,
    ItemId Output,
    int OutputCount,
    IReadOnlyList<IngredientRequirement> Ingredients,
    IReadOnlySet<CraftingStationId>? RequiredStations = null,
    IReadOnlySet<string>? RequiredBiomeTags = null,
    IReadOnlySet<LiquidKind>? RequiredLiquids = null,
    bool RequiresGuideUnlock = false,
    IReadOnlySet<ItemId>? GuideUnlockMaterials = null);

public sealed record CraftingEnvironment(
    Vector2 PlayerPosition,
    TileGrid World,
    TileCatalog Tiles,
    float StationRadiusTiles = 6f,
    IReadOnlySet<string>? BiomeTags = null,
    IReadOnlySet<LiquidKind>? NearbyLiquids = null);

public sealed record CraftingResult(
    bool Success,
    string Message,
    ItemId? Output = null,
    int OutputCount = 0)
{
    /// <summary>Calibration rolled onto a crafted weapon (None otherwise).</summary>
    public Calibration Calibration { get; init; }
}

/// <summary>
/// Data-driven recipe lookup and atomic crafting. The recipe set and tuning are
/// original Rimtera content, not copied game definitions.
/// </summary>
public sealed class CraftingSystem
{
    private readonly ItemCatalog _items;
    private readonly List<RecipeDefinition> _recipes = new();
    private readonly Dictionary<ItemId, List<RecipeDefinition>> _byIngredient = new();
    private readonly Dictionary<ItemId, List<RecipeDefinition>> _byOutput = new();
    private readonly Dictionary<CraftingStationId, List<RecipeDefinition>> _byStation = new();
    private readonly HashSet<string> _knownGuideRecipes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RecipeDefinition> _byId = new(StringComparer.OrdinalIgnoreCase);

    public CraftingSystem(ItemCatalog items)
    {
        _items = items ?? throw new ArgumentNullException(nameof(items));
        // The opening (docs/CRAFTING_PROGRESSION.md): a felled tree's timber builds the first station by hand, as
        // Terraria's 10 wood make a work bench, and timber presses into hull panels, the building material most
        // early recipes use (Terraria's wood). Salvaged hull panels still work for the fabricator.
        Register(new RecipeDefinition(
            "workbench", ItemId.WorkbenchBlock, 1,
            new[] { new IngredientRequirement(new[] { ItemId.Timber, ItemId.WoodBlock }, 10) }));
        Register(new RecipeDefinition(
            "hull-panel", ItemId.WoodBlock, 1,
            new[] { IngredientRequirement.Exact(ItemId.Timber, 1) }));
        Register(new RecipeDefinition(
            "furnace", ItemId.FurnaceBlock, 1,
            new[]
            {
                IngredientRequirement.Exact(ItemId.RockBlock, 20),
                IngredientRequirement.Exact(ItemId.WoodBlock, 5)
            },
            new HashSet<CraftingStationId> { CraftingStationId.Workbench }));
        Register(new RecipeDefinition(
            "anvil", ItemId.AnvilBlock, 1,
            new[]
            {
                IngredientRequirement.Exact(ItemId.RockBlock, 12),
                IngredientRequirement.Exact(ItemId.CopperOre, 8)
            },
            new HashSet<CraftingStationId> { CraftingStationId.Workbench }));
        Register(new RecipeDefinition(
            "copper-bar", ItemId.CopperBar, 1,
            new[] { IngredientRequirement.Exact(ItemId.CopperOre, 3) },
            new HashSet<CraftingStationId> { CraftingStationId.Furnace }));
        Register(new RecipeDefinition(
            "copper-pickaxe", ItemId.CopperPickaxe, 1,
            new[]
            {
                IngredientRequirement.Exact(ItemId.CopperBar, 8),
                IngredientRequirement.Exact(ItemId.WoodBlock, 10)
            },
            new HashSet<CraftingStationId> { CraftingStationId.Anvil },
            RequiresGuideUnlock: true,
            GuideUnlockMaterials: new HashSet<ItemId> { ItemId.CopperBar }));
        Register(new RecipeDefinition(
            "copper-sword", ItemId.CopperSword, 1,
            new[]
            {
                IngredientRequirement.Exact(ItemId.CopperBar, 6),
                IngredientRequirement.Exact(ItemId.WoodBlock, 4)
            },
            new HashSet<CraftingStationId> { CraftingStationId.Anvil },
            RequiresGuideUnlock: true,
            GuideUnlockMaterials: new HashSet<ItemId> { ItemId.CopperBar }));
        Register(new RecipeDefinition(
            "trailcap", ItemId.Trailcap, 1,
            new[]
            {
                IngredientRequirement.Exact(ItemId.WoodBlock, 8),
                IngredientRequirement.Exact(ItemId.CopperBar, 3)
            },
            new HashSet<CraftingStationId> { CraftingStationId.Anvil }));
        Register(new RecipeDefinition(
            "trailcoat", ItemId.Trailcoat, 1,
            new[]
            {
                IngredientRequirement.Exact(ItemId.WoodBlock, 12),
                IngredientRequirement.Exact(ItemId.CopperBar, 5)
            },
            new HashSet<CraftingStationId> { CraftingStationId.Anvil }));
        Register(new RecipeDefinition(
            "trailboots", ItemId.Trailboots, 1,
            new[]
            {
                IngredientRequirement.Exact(ItemId.WoodBlock, 10),
                IngredientRequirement.Exact(ItemId.CopperBar, 4)
            },
            new HashSet<CraftingStationId> { CraftingStationId.Anvil }));
        Register(new RecipeDefinition(
            "hearth-charm", ItemId.HearthCharm, 1,
            new[]
            {
                IngredientRequirement.Exact(ItemId.CopperBar, 6),
                IngredientRequirement.Exact(ItemId.Berry, 4)
            },
            new HashSet<CraftingStationId> { CraftingStationId.Anvil }));
        Register(new RecipeDefinition(
            "restorative-tonic", ItemId.RestorativeTonic, 2,
            new[]
            {
                IngredientRequirement.Exact(ItemId.Berry, 3),
                IngredientRequirement.Exact(ItemId.CopperOre, 1)
            },
            new HashSet<CraftingStationId> { CraftingStationId.Workbench }));
        Register(new RecipeDefinition(
            "mana-tonic", ItemId.ManaTonic, 2,
            new[]
            {
                IngredientRequirement.Exact(ItemId.CopperOre, 2),
                IngredientRequirement.Exact(ItemId.Berry, 1)
            },
            new HashSet<CraftingStationId> { CraftingStationId.Workbench }));
        RegisterNeoTerraRecipes();
        ContentRecipes.RegisterAll(this, _items);
        BatchAItems.RegisterRecipes(this);
        PlayerItems.RegisterRecipes(this);
        PlayerSystemsContent.RegisterRecipes(this);
        TravelContent.RegisterRecipes(this);
        ContentFillItems.RegisterRecipes(this);
        CreatureCoverage.RegisterRecipes(this); // creature coverage: summon and starter recipes
        ArsenalRecipes.RegisterAll(this, _items);
    }

    /// <summary>
    /// Energy weapons and colony furniture. Gun costs climb with ore depth so the
    /// Plasma pistol is reachable from ferrite and the Rocket launcher needs viridium.
    /// </summary>
    private void RegisterNeoTerraRecipes()
    {
        var fabricator = new HashSet<CraftingStationId> { CraftingStationId.Workbench };
        var refinery = new HashSet<CraftingStationId> { CraftingStationId.Furnace };
        var forge = new HashSet<CraftingStationId> { CraftingStationId.Anvil };

        Register(new RecipeDefinition(
            "energy-cell", ItemId.Arrow, 25,
            new[] { IngredientRequirement.Exact(ItemId.EnergyCrystal, 1) },
            fabricator));
        Register(new RecipeDefinition(
            "metal-alloy-cobalt", ItemId.CopperBar, 2,
            new[] { IngredientRequirement.Exact(ItemId.CobaltOre, 3) },
            refinery));
        Register(new RecipeDefinition(
            "metal-alloy-viridium", ItemId.CopperBar, 3,
            new[] { IngredientRequirement.Exact(ItemId.ViridiumOre, 2) },
            refinery));

        Register(new RecipeDefinition(
            "plasma-pistol", ItemId.PlasmaPistol, 1,
            new[]
            {
                IngredientRequirement.Exact(ItemId.CopperBar, 6),
                IngredientRequirement.Exact(ItemId.EnergyCrystal, 2),
                IngredientRequirement.Exact(ItemId.CopperOre, 4)
            },
            forge));
        Register(new RecipeDefinition(
            "sniper-rifle", ItemId.SniperRifle, 1,
            new[]
            {
                IngredientRequirement.Exact(ItemId.CopperBar, 10),
                IngredientRequirement.Exact(ItemId.EnergyCrystal, 4),
                IngredientRequirement.Exact(ItemId.CobaltOre, 6)
            },
            forge,
            RequiresGuideUnlock: true,
            GuideUnlockMaterials: new HashSet<ItemId> { ItemId.CobaltOre }));
        Register(new RecipeDefinition(
            "grenade-launcher", ItemId.GrenadeLauncher, 1,
            new[]
            {
                IngredientRequirement.Exact(ItemId.CopperBar, 14),
                IngredientRequirement.Exact(ItemId.EnergyCrystal, 6),
                IngredientRequirement.Exact(ItemId.CobaltOre, 12)
            },
            forge,
            RequiresGuideUnlock: true,
            GuideUnlockMaterials: new HashSet<ItemId> { ItemId.CobaltOre }));
        Register(new RecipeDefinition(
            "rocket-launcher", ItemId.RocketLauncher, 1,
            new[]
            {
                IngredientRequirement.Exact(ItemId.CopperBar, 20),
                IngredientRequirement.Exact(ItemId.EnergyCrystal, 10),
                IngredientRequirement.Exact(ItemId.ViridiumOre, 12)
            },
            forge,
            RequiresGuideUnlock: true,
            GuideUnlockMaterials: new HashSet<ItemId> { ItemId.ViridiumOre }));

        Register(new RecipeDefinition(
            "hydroponics-tray", ItemId.HydroponicsTray, 1,
            new[]
            {
                IngredientRequirement.Exact(ItemId.WoodBlock, 6),
                IngredientRequirement.Exact(ItemId.SoilBlock, 4),
                IngredientRequirement.Exact(ItemId.Berry, 2)
            },
            fabricator));
        Register(new RecipeDefinition(
            "oxygen-unit", ItemId.OxygenUnit, 1,
            new[]
            {
                IngredientRequirement.Exact(ItemId.CopperBar, 4),
                IngredientRequirement.Exact(ItemId.RockBlock, 8)
            },
            fabricator));
        Register(new RecipeDefinition(
            "solar-panel", ItemId.SolarPanel, 1,
            new[]
            {
                IngredientRequirement.Exact(ItemId.CopperBar, 3),
                IngredientRequirement.Exact(ItemId.EnergyCrystal, 2),
                IngredientRequirement.Exact(ItemId.SandBlock, 4)
            },
            fabricator));
        Register(new RecipeDefinition(
            "mini-turret", ItemId.MiniTurret, 1,
            new[]
            {
                IngredientRequirement.Exact(ItemId.CopperBar, 5),
                IngredientRequirement.Exact(ItemId.EnergyCrystal, 3),
                IngredientRequirement.Exact(ItemId.CobaltOre, 2)
            },
            fabricator));

        RegisterBiomeRecipes(fabricator, refinery, forge);
    }

    /// <summary>Biome-ore refining and environmental gear (original Rimtera content).</summary>
    private void RegisterBiomeRecipes(
        IReadOnlySet<CraftingStationId> fabricator,
        IReadOnlySet<CraftingStationId> refinery,
        IReadOnlySet<CraftingStationId> forge)
    {
        Register(new RecipeDefinition(
            "metal-alloy-scrap-iron", ItemId.CopperBar, 2,
            new[] { IngredientRequirement.Exact(ItemId.ScrapIronOre, 4) },
            refinery));
        Register(new RecipeDefinition(
            "metal-alloy-glacial", ItemId.CopperBar, 2,
            new[] { IngredientRequirement.Exact(ItemId.GlacialOre, 3) },
            refinery));
        Register(new RecipeDefinition(
            "metal-alloy-pyrocite", ItemId.CopperBar, 4,
            new[] { IngredientRequirement.Exact(ItemId.PyrociteOre, 3) },
            refinery));
        Register(new RecipeDefinition(
            "energy-cell-uranium", ItemId.Arrow, 40,
            new[]
            {
                IngredientRequirement.Exact(ItemId.UraniumOre, 1),
                IngredientRequirement.Exact(ItemId.CausticShard, 1)
            },
            fabricator));
        Register(new RecipeDefinition(
            "insulated-suit", ItemId.InsulatedSuit, 1,
            new[]
            {
                IngredientRequirement.Exact(ItemId.GlacialOre, 8),
                IngredientRequirement.Exact(ItemId.FrostCrystal, 4),
                IngredientRequirement.Exact(ItemId.CopperBar, 4)
            },
            forge));
        Register(new RecipeDefinition(
            "obsidian-heat-ward", ItemId.HeatWard, 1,
            new[]
            {
                IngredientRequirement.Exact(ItemId.ObsidianShard, 6),
                IngredientRequirement.Exact(ItemId.PyrociteOre, 4),
                IngredientRequirement.Exact(ItemId.CopperBar, 3)
            },
            forge));
        Register(new RecipeDefinition(
            "rad-filter-mask", ItemId.RadFilterMask, 1,
            new[]
            {
                IngredientRequirement.Exact(ItemId.CausticShard, 5),
                IngredientRequirement.Exact(ItemId.OxidizedShard, 4),
                IngredientRequirement.Exact(ItemId.CopperBar, 2)
            },
            forge));
        // Blight purification.
        Register(new RecipeDefinition(
            "purifier-sprayer", ItemId.PurifierSprayer, 1,
            new[]
            {
                IngredientRequirement.Exact(ItemId.CopperBar, 8),
                IngredientRequirement.Exact(ItemId.EnergyCrystal, 3)
            },
            forge));
        Register(new RecipeDefinition(
            "purifier-gel", ItemId.PurifierGel, 25,
            new[]
            {
                IngredientRequirement.Exact(ItemId.EnergyCrystal, 1),
                new IngredientRequirement(new[] { ItemId.SandBlock, ItemId.SnowBlock }, 3)
            },
            fabricator));
        Register(new RecipeDefinition(
            "purifier-beacon", ItemId.PurifierBeacon, 1,
            new[]
            {
                IngredientRequirement.Exact(ItemId.CopperBar, 6),
                IngredientRequirement.Exact(ItemId.EnergyCrystal, 4),
                IngredientRequirement.Exact(ItemId.WoodBlock, 4)
            },
            forge));
    }

    public IReadOnlyList<RecipeDefinition> Recipes => _recipes;
    public IReadOnlySet<string> KnownGuideRecipes => _knownGuideRecipes;

    public void Register(RecipeDefinition recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (string.IsNullOrWhiteSpace(recipe.Id) || recipe.OutputCount <= 0 ||
            recipe.Ingredients is null || recipe.Ingredients.Count == 0)
            throw new ArgumentException("Recipe ID, output quantity, and at least one ingredient group are required.", nameof(recipe));
        _ = _items[recipe.Output];
        if (_byId.ContainsKey(recipe.Id))
            throw new InvalidOperationException($"Recipe '{recipe.Id}' is already registered.");
        foreach (var group in recipe.Ingredients)
        {
            if (group is null || group.Alternatives is null || group.Quantity <= 0 || group.Alternatives.Count == 0)
                throw new ArgumentException($"Recipe '{recipe.Id}' has an empty or invalid ingredient group.", nameof(recipe));
            foreach (var item in group.Alternatives)
                _ = _items[item];
        }
        foreach (var station in recipe.RequiredStations ?? new HashSet<CraftingStationId>())
        {
            if (station == CraftingStationId.None || !Enum.IsDefined(station))
                throw new ArgumentException($"'{station}' is not a valid required crafting station.", nameof(recipe));
        }
        foreach (var liquid in recipe.RequiredLiquids ?? new HashSet<LiquidKind>())
        {
            if (!Enum.IsDefined(liquid) || liquid == LiquidKind.None)
                throw new ArgumentException($"'{liquid}' is not a valid required liquid.", nameof(recipe));
        }

        if (recipe.RequiredBiomeTags?.Any(string.IsNullOrWhiteSpace) == true)
            throw new ArgumentException("Biome tags must be non-empty strings.", nameof(recipe));
        if (recipe.RequiresGuideUnlock && recipe.GuideUnlockMaterials is not { Count: > 0 })
            throw new ArgumentException("Guide-gated recipes need at least one reveal material.", nameof(recipe));
        foreach (var material in recipe.GuideUnlockMaterials ?? new HashSet<ItemId>())
        {
            if (!_items[material].IsMaterial)
                throw new ArgumentException($"Guide reveal item '{material}' is not marked as a material.", nameof(recipe));
        }

        // Freeze caller-owned arrays and sets so recipe indexes cannot become stale after registration.
        recipe = recipe with
        {
            Ingredients = recipe.Ingredients
                .Select(group => new IngredientRequirement(group.Alternatives.Distinct().ToArray(), group.Quantity))
                .ToArray(),
            RequiredStations = recipe.RequiredStations is null ? null : new HashSet<CraftingStationId>(recipe.RequiredStations),
            RequiredBiomeTags = recipe.RequiredBiomeTags is null
                ? null
                : new HashSet<string>(recipe.RequiredBiomeTags, StringComparer.OrdinalIgnoreCase),
            RequiredLiquids = recipe.RequiredLiquids is null ? null : new HashSet<LiquidKind>(recipe.RequiredLiquids),
            GuideUnlockMaterials = recipe.GuideUnlockMaterials is null ? null : new HashSet<ItemId>(recipe.GuideUnlockMaterials)
        };

        _recipes.Add(recipe);
        _byId[recipe.Id] = recipe;
        AddIndex(_byOutput, recipe.Output, recipe);
        foreach (var item in recipe.Ingredients.SelectMany(group => group.Alternatives).Distinct())
            AddIndex(_byIngredient, item, recipe);
        foreach (var station in recipe.RequiredStations ?? new HashSet<CraftingStationId>())
            AddIndex(_byStation, station, recipe);
    }

    public IReadOnlyList<RecipeDefinition> FindByIngredient(ItemId itemId) =>
        _byIngredient.TryGetValue(itemId, out var recipes) ? recipes.ToArray() : Array.Empty<RecipeDefinition>();

    public IReadOnlyList<RecipeDefinition> FindByOutput(ItemId itemId) =>
        _byOutput.TryGetValue(itemId, out var recipes) ? recipes.ToArray() : Array.Empty<RecipeDefinition>();

    public IReadOnlyList<RecipeDefinition> FindByStation(CraftingStationId station) =>
        _byStation.TryGetValue(station, out var recipes) ? recipes.ToArray() : Array.Empty<RecipeDefinition>();

    /// <summary>Unlocks guide-gated recipes when the player carries a configured reveal material.</summary>
    public IReadOnlyList<string> ConsultGuide(Inventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var learned = new List<string>();
        foreach (var recipe in _recipes.Where(recipe => recipe.RequiresGuideUnlock))
        {
            var materials = recipe.GuideUnlockMaterials;
            if (_knownGuideRecipes.Contains(recipe.Id) || materials is null || materials.Count == 0)
                continue;
            if (!materials.Any(itemId => inventory.Count(itemId) > 0))
                continue;
            _knownGuideRecipes.Add(recipe.Id);
            learned.Add(recipe.Id);
        }
        return learned;
    }

    /// <summary>Uses ingredient and nearby-station indices to find candidate definitions.</summary>
    public IReadOnlyList<RecipeDefinition> GetCandidates(Inventory inventory, CraftingEnvironment environment) =>
        GetCandidates(inventory, environment, GetNearbyStations(environment));

    private IReadOnlyList<RecipeDefinition> GetCandidates(Inventory inventory, CraftingEnvironment environment,
        IReadOnlySet<CraftingStationId> nearbyStations)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var candidates = new HashSet<RecipeDefinition>();
        foreach (var stack in inventory.Slots)
        {
            if (!stack.IsEmpty && _byIngredient.TryGetValue(stack.ItemId, out var byItem))
                candidates.UnionWith(byItem);
        }
        foreach (var station in nearbyStations)
        {
            if (_byStation.TryGetValue(station, out var byStation))
                candidates.UnionWith(byStation);
        }
        candidates.UnionWith(_recipes.Where(recipe => recipe.RequiredStations is not { Count: > 0 }));
        return candidates.OrderBy(recipe => recipe.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<RecipeDefinition> GetCraftableRecipes(Inventory inventory, CraftingEnvironment environment)
    {
        var nearby = GetNearbyStations(environment);
        return GetCandidates(inventory, environment, nearby)
            .Where(recipe => Evaluate(recipe, inventory, environment, nearby, out _).Success)
            .ToArray();
    }

    /// <summary>Looks a recipe up by id (case-insensitive).</summary>
    public RecipeDefinition? FindById(string recipeId) => _byId.GetValueOrDefault(recipeId);

    public CraftingResult TryCraft(string recipeId, Inventory inventory, CraftingEnvironment environment) =>
        TryCraft(recipeId, inventory, environment, null);

    /// <summary>Crafts a recipe; <paramref name="calibrate"/> may roll a calibration for the crafted stack.</summary>
    public CraftingResult TryCraft(string recipeId, Inventory inventory, CraftingEnvironment environment,
        Func<ItemDefinition, Calibration>? calibrate)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var recipe = FindById(recipeId);
        if (recipe is null)
            return new CraftingResult(false, $"Unknown recipe '{recipeId}'.");

        var evaluation = Evaluate(recipe, inventory, environment, GetNearbyStations(environment), out var plan);
        if (!evaluation.Success)
            return evaluation;

        var output = _items[recipe.Output];
        var calibration = calibrate is not null && (output.Calibratable || CalibrationCatalog.IsAccessory(output)) ? calibrate(output) : Calibration.None;
        if (!inventory.TryApplyTransaction(plan, recipe.Output, recipe.OutputCount, calibration))
            return new CraftingResult(false, "Inventory changed or has no room for the crafted result.");
        return new CraftingResult(true, $"Crafted {recipe.OutputCount} {CalibrationCatalog.Describe(output, calibration)}.",
            recipe.Output, recipe.OutputCount) { Calibration = calibration };
    }

    public IReadOnlySet<CraftingStationId> GetNearbyStations(CraftingEnvironment environment)
    {
        ValidateEnvironment(environment);
        var player = environment.PlayerPosition;
        var radius = environment.StationRadiusTiles;
        var radiusSquared = radius * radius;
        var minX = (int)Math.Clamp(Math.Floor((double)player.X - radius), 0d, environment.World.Width - 1d);
        var maxX = (int)Math.Clamp(Math.Ceiling((double)player.X + radius), 0d, environment.World.Width - 1d);
        var minY = (int)Math.Clamp(Math.Floor((double)player.Y - radius), 0d, environment.World.Height - 1d);
        var maxY = (int)Math.Clamp(Math.Ceiling((double)player.Y + radius), 0d, environment.World.Height - 1d);
        var found = new HashSet<CraftingStationId>();
        var implied = new List<CraftingStationId>();

        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            var station = environment.Tiles[environment.World.GetCell(new GridPoint(x, y)).TileId].Station;
            if (station == CraftingStationId.None)
                continue;
            var dx = player.X - (x + 0.5f);
            var dy = player.Y - (y + 0.5f);
            if (dx * dx + dy * dy <= radiusSquared && found.Add(station))
                implied.AddRange(station.Implied());
        }
        found.UnionWith(implied);
        return found;
    }

    private CraftingResult Evaluate(
        RecipeDefinition recipe,
        Inventory inventory,
        CraftingEnvironment environment,
        IReadOnlySet<CraftingStationId> nearbyStations,
        out Dictionary<ItemId, int> ingredientPlan)
    {
        ingredientPlan = new Dictionary<ItemId, int>();
        ValidateEnvironment(environment);
        if (recipe.RequiresGuideUnlock && !_knownGuideRecipes.Contains(recipe.Id))
            return new CraftingResult(false, "Consult the station AI guide with a reveal material to learn this recipe.");

        foreach (var station in recipe.RequiredStations ?? new HashSet<CraftingStationId>())
        {
            if (!nearbyStations.Contains(station))
                return new CraftingResult(false, $"A nearby {station.DisplayName()} is required.");
        }

        foreach (var biome in recipe.RequiredBiomeTags ?? new HashSet<string>())
        {
            if (!(environment.BiomeTags?.Contains(biome) ?? false))
                return new CraftingResult(false, $"Biome condition '{biome}' is not met.");
        }
        foreach (var liquid in recipe.RequiredLiquids ?? new HashSet<LiquidKind>())
        {
            if (!(environment.NearbyLiquids?.Contains(liquid) ?? false))
                return new CraftingResult(false, $"Nearby {liquid} is required.");
        }

        foreach (var group in recipe.Ingredients)
        {
            ItemId? selected = null;
            foreach (var itemId in group.Alternatives)
            {
                if (inventory.Count(itemId) - ingredientPlan.GetValueOrDefault(itemId) < group.Quantity)
                    continue;
                selected = itemId;
                break;
            }
            if (selected is not { } selectedItem)
                return new CraftingResult(false, "Missing one or more required ingredients.");
            ingredientPlan[selectedItem] = ingredientPlan.GetValueOrDefault(selectedItem) + group.Quantity;
        }
        return new CraftingResult(true, "Ready to craft.");
    }

    private static void ValidateEnvironment(CraftingEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(environment.World);
        ArgumentNullException.ThrowIfNull(environment.Tiles);
        if (!float.IsFinite(environment.PlayerPosition.X) || !float.IsFinite(environment.PlayerPosition.Y) ||
            !float.IsFinite(environment.StationRadiusTiles) || environment.StationRadiusTiles < 0f)
            throw new ArgumentOutOfRangeException(nameof(environment), "Crafting position and station radius must be finite.");
    }

    private static void AddIndex<TKey>(Dictionary<TKey, List<RecipeDefinition>> index, TKey key, RecipeDefinition recipe)
        where TKey : notnull
    {
        if (!index.TryGetValue(key, out var recipes))
        {
            recipes = new List<RecipeDefinition>();
            index.Add(key, recipes);
        }
        recipes.Add(recipe);
    }
}
