using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>One piece of the station AI's advice: a stable id (for dismissing), the line to show, and an item to look up.</summary>
public sealed record GuideTip(string Id, string Text, ItemId? Focus = null);

/// <summary>
/// The station AI's guidance, Terraria's Guide in Rimtera's terms: what the player can craft where they stand, which
/// stations are around their base, and the next step in the progression, read from the player's state and the
/// crafting data (never a fixed script, so it follows recipe changes).
/// </summary>
public sealed partial class GameSimulation
{
    /// <summary>How far around the player the guide looks for a base's stations and lights.</summary>
    public const int GuideBaseRadius = 30;

    /// <summary>The crafting conditions where the player stands (stations in reach, biome and liquids).</summary>
    public CraftingEnvironment CurrentCraftingEnvironment => CreateCraftingEnvironment();

    /// <summary>Stations within crafting reach, including those an upgraded station stands in for.</summary>
    public IReadOnlySet<CraftingStationId> NearbyCraftingStations() => Crafting.GetNearbyStations(CreateCraftingEnvironment());

    /// <summary>Recipes the player is close to at the stations in reach (the crafting panel's "almost" list).</summary>
    public IReadOnlyList<RecipeStatus> GetAlmostCraftableRecipes()
    {
        var environment = CreateCraftingEnvironment();
        return CraftingGuide.AlmostCraftable(Crafting, Player.Inventory, Crafting.GetNearbyStations(environment), environment);
    }

    /// <summary>A recipe read against the player's inventory and the stations in reach.</summary>
    public RecipeStatus RecipeStatusOf(RecipeDefinition recipe)
    {
        var environment = CreateCraftingEnvironment();
        return CraftingGuide.Describe(Crafting, recipe, Player.Inventory, Crafting.GetNearbyStations(environment), environment);
    }

    /// <summary>
    /// Stations placed around the player's base (a wider look than crafting reach) and whether a placed light is there.
    /// </summary>
    public (IReadOnlySet<CraftingStationId> Stations, bool Light) SurveyBase(int radius = GuideBaseRadius)
    {
        var center = PlayerCenter;
        var stations = new HashSet<CraftingStationId>();
        var light = false;
        var minX = Math.Max(0, (int)center.X - radius);
        var maxX = Math.Min(World.Width - 1, (int)center.X + radius);
        var minY = Math.Max(0, (int)center.Y - radius);
        var maxY = Math.Min(World.Height - 1, (int)center.Y + radius);
        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            var tile = World.TileAt(x, y);
            if (tile == TileId.Air || !Tiles.Contains(tile))
                continue;
            var station = Tiles[tile].Station;
            if (station != CraftingStationId.None && stations.Add(station))
                stations.UnionWith(station.ImpliedClosure());
            if (!light && TilePhysics.GetTileLight(tile) is not null)
                light = true;
        }
        return (stations, light);
    }

    /// <summary>Why a boss summon would refuse right now (location, an earlier boss, the Fracture), or null.</summary>
    public string? SummonRefusal(EnemyKind boss) => HardmodeSummonRefusal(boss);

    /// <summary>The station AI's next steps, most pressing first. Never empty.</summary>
    public IReadOnlyList<GuideTip> GuideTips() => GuideAdvisor.NextSteps(this);
}

/// <summary>
/// Builds the station AI's next steps in Terraria's early order (first station, light, refinery and forge, a better
/// drill, armour, life cores, the next boss and the Fracture), each worded from the crafting data: what the station
/// item's recipe needs, what the player has of it, and where the missing material comes from.
/// </summary>
public static class GuideAdvisor
{
    /// <summary>Stations in the order a Terraria player builds their analogs: work bench, furnace, anvil.</summary>
    private static readonly CraftingStationId[] StarterStations =
        [CraftingStationId.Workbench, CraftingStationId.Furnace, CraftingStationId.Anvil];

    public static IReadOnlyList<GuideTip> NextSteps(GameSimulation game)
    {
        var context = new Context(game);
        var tips = new List<GuideTip>();
        void Add(GuideTip? tip)
        {
            if (tip is not null && tips.All(existing => existing.Id != tip.Id))
                tips.Add(tip);
        }

        Add(context.StationTip(StarterStations[0]));
        Add(context.LightTip());
        foreach (var station in StarterStations.Skip(1))
            Add(context.StationTip(station));
        Add(context.RevealTip());
        Add(context.DrillTip());
        Add(context.ArmourTip());
        Add(context.LifeCoreTip());
        Add(context.BossTip());
        if (tips.Count == 0)
            tips.Add(new GuideTip("done", "Every challenge I know of is behind you. Build, explore and perfect your gear."));
        return tips;
    }

    private sealed class Context
    {
        private readonly GameSimulation _game;
        private readonly IReadOnlySet<CraftingStationId> _nearby;
        private readonly IReadOnlySet<CraftingStationId> _base;
        private readonly bool _lightAtBase;

        public Context(GameSimulation game)
        {
            _game = game;
            _nearby = game.NearbyCraftingStations();
            var survey = game.SurveyBase();
            var stations = new HashSet<CraftingStationId>(survey.Stations);
            stations.UnionWith(_nearby);
            _base = stations;
            _lightAtBase = survey.Light;
        }

        private ItemCatalog Items => _game.Items;
        private Inventory Inventory => _game.Player.Inventory;
        private CraftingSystem Crafting => _game.Crafting;
        private string Name(ItemId item) => Items[item].Name;

        /// <summary>True when the player has the station at their base, or carries an item that places it.</summary>
        private bool HasStation(CraftingStationId station) =>
            _base.Contains(station) || CarriedStationItem(station) is not null;

        private ItemId? CarriedStationItem(CraftingStationId station) =>
            CraftingGuide.StationItems(Items, _game.Tiles, station).Cast<ItemId?>().FirstOrDefault(item => Inventory.Count(item!.Value) > 0);

        /// <summary>The recipe for <paramref name="item"/> the player is best placed to use: taught, fewest missing stations, then material.</summary>
        private RecipeStatus? BestRecipe(ItemId item) => Crafting.FindByOutput(item)
            .Select(recipe => CraftingGuide.Describe(Crafting, recipe, Inventory, _base))
            .OrderBy(status => status.Locked)
            .ThenBy(status => status.MissingStations.Count)
            .ThenBy(status => status.MissingIngredientCount)
            .ThenByDescending(status => status.Progress)
            .FirstOrDefault();

        private string IngredientList(RecipeStatus status) =>
            string.Join(", ", status.Ingredients.Select(ingredient => $"{Name(ingredient.Item)} {ingredient.Have}/{ingredient.Need}"));

        /// <summary>"craft it by hand from Timber" or "chop trees" for the first material the player lacks, a few links deep.</summary>
        private string MissingChain(RecipeStatus status, int depth = 0)
        {
            var missing = status.Ingredients.FirstOrDefault(ingredient => !ingredient.Met);
            if (missing is null || depth > 2)
                return string.Empty;
            return $" {Name(missing.Item)}: {HowToGet(missing.Item, depth)}.";
        }

        /// <summary>Where an item comes from: its recipe (and that recipe's own missing link) or its drop sources.</summary>
        private string HowToGet(ItemId item, int depth)
        {
            if (BestRecipe(item) is { } recipe)
            {
                var inputs = string.Join(", ", recipe.Ingredients.Select(ingredient => $"{Name(ingredient.Item)} ×{ingredient.Need}"));
                var line = $"craft {CraftingGuide.WhereCrafted(recipe.Recipe)} from {inputs}";
                var next = recipe.Ingredients.FirstOrDefault(ingredient => !ingredient.Met);
                return next is null || depth >= 2 ? line : $"{line}; {Name(next.Item)}: {HowToGet(next.Item, depth + 1)}";
            }
            var sources = CraftingGuide.SourcesOf(Items, _game.Tiles, item);
            if (sources.Count == 0)
                return "found out in the world (crates, caches and traders)";
            var first = sources[0];
            if (first.StartsWith("chop trees", StringComparison.Ordinal) && BestTool(ItemUseKind.Cutter) is { } hatchet)
                first += $" with your {Name(hatchet)}";
            return first;
        }

        private ItemId? BestTool(ItemUseKind kind) => Inventory.Slots
            .Where(stack => !stack.IsEmpty && Items[stack.ItemId].UseKind == kind)
            .OrderByDescending(stack => Items[stack.ItemId].ToolPower)
            .Select(stack => (ItemId?)stack.ItemId)
            .FirstOrDefault();

        /// <summary>Build (or place) a station; a station its recipe needs comes first.</summary>
        public GuideTip? StationTip(CraftingStationId station, int depth = 0)
        {
            if (_base.Contains(station) || depth > 3)
                return null;
            var name = station.DisplayName();
            if (CarriedStationItem(station) is { } carried)
                return new GuideTip($"place:{station}",
                    $"Place your {Name(carried)}: select it on the hotbar and click the ground near your camp. Stand close to it to craft with it.", carried);
            var items = CraftingGuide.StationItems(Items, _game.Tiles, station);
            var best = items.Select(item => (Item: item, Recipe: BestRecipe(item))).Where(pair => pair.Recipe is not null).FirstOrDefault();
            if (best.Recipe is not { } recipe)
                return items.Count == 0 ? null
                    : new GuideTip($"station:{station}", $"You'll need {CraftingGuide.Article(name)} {name}: {HowToGet(items[0], 0)}.", items[0]);
            foreach (var needed in recipe.MissingStations)
                if (needed != station && StationTip(needed, depth + 1) is { } first)
                    return first;
            var lead = station == StarterStations[0] && !_base.Any()
                ? $"First, a station: craft {CraftingGuide.Article(name)} {name} {CraftingGuide.WhereCrafted(recipe.Recipe)}"
                : $"Build {CraftingGuide.Article(name)} {name} {CraftingGuide.WhereCrafted(recipe.Recipe)}";
            var text = recipe.HasAllIngredients
                ? $"{lead}. You have everything ({IngredientList(recipe)}): open the inventory and craft it."
                : $"{lead}: {IngredientList(recipe)}.{MissingChain(recipe)}";
            return new GuideTip($"station:{station}", text, best.Item);
        }

        /// <summary>Terraria's torches: with nothing that gives light carried or placed, make the cheapest light there is.</summary>
        public GuideTip? LightTip()
        {
            if (_lightAtBase || Inventory.Slots.Any(stack => !stack.IsEmpty && CraftingGuide.IsLightSource(Items, stack.ItemId)))
                return null;
            var best = Crafting.Recipes
                .Where(recipe => CraftingGuide.IsLightSource(Items, recipe.Output))
                .Select(recipe => CraftingGuide.Describe(Crafting, recipe, Inventory, _base))
                .Where(status => !status.Locked && status.MissingConditions.Count == 0)
                .OrderBy(status => status.MissingStations.Count)
                .ThenBy(status => status.Ingredients.Sum(ingredient => Math.Max(0, ingredient.Need - ingredient.Have)))
                .ThenByDescending(status => status.Recipe.OutputCount)
                .FirstOrDefault();
            if (best is null)
                return null;
            var name = Name(best.Recipe.Output);
            var text = best.HasAllIngredients && best.AtStations
                ? $"Caves are dark. Craft {name} {CraftingGuide.WhereCrafted(best.Recipe)} now ({IngredientList(best)}) and light your way."
                : $"Caves are dark: craft {name} {CraftingGuide.WhereCrafted(best.Recipe)} ({IngredientList(best)}).{MissingChain(best)}";
            return new GuideTip("light", text, best.Recipe.Output);
        }

        /// <summary>Guide-taught recipes the player can learn now by showing the station AI a material they carry.</summary>
        public GuideTip? RevealTip()
        {
            var teachable = Crafting.Recipes
                .Where(recipe => recipe.RequiresGuideUnlock && !Crafting.KnownGuideRecipes.Contains(recipe.Id) &&
                                 recipe.GuideUnlockMaterials?.Any(material => Inventory.Count(material) > 0) == true)
                .ToArray();
            if (teachable.Length == 0)
                return null;
            var material = teachable[0].GuideUnlockMaterials!.First(item => Inventory.Count(item) > 0);
            var names = teachable.Select(recipe => Name(recipe.Output)).Distinct().Take(3);
            return new GuideTip("reveal",
                $"Talk to me while carrying your {Name(material)} and I'll teach you {teachable.Length} recipe{(teachable.Length == 1 ? "" : "s")}: {string.Join(", ", names)}.",
                material);
        }

        /// <summary>The next ore the player's best drill can't break, and the cheapest drill that can.</summary>
        public GuideTip? DrillTip()
        {
            var drill = BestTool(ItemUseKind.Pickaxe);
            var power = drill is { } held ? Items[held].ToolPower : 0;
            var gates = Enum.GetValues<TileId>().Distinct()
                .Where(tile => _game.Tiles.Contains(tile) && _game.Tiles[tile].MinimumToolPower > power &&
                               _game.Tiles[tile].Station == CraftingStationId.None)
                .Select(tile => (Tile: tile, Drop: SafeDrop(tile)))
                .Where(pair => pair.Drop is { } drop && Items.Contains(drop) && Items[drop].IsMaterial && Crafting.FindByIngredient(drop).Count > 0)
                .GroupBy(pair => _game.Tiles[pair.Tile].MinimumToolPower)
                .OrderBy(group => group.Key)
                .FirstOrDefault();
            if (gates is null)
                return null;
            var threshold = gates.Key;
            var next = Items.All
                .Where(item => item.UseKind == ItemUseKind.Pickaxe && item.ToolPower >= threshold && Crafting.FindByOutput(item.Id).Count > 0)
                .OrderBy(item => item.ToolPower)
                .ThenBy(item => item.Tier)
                .FirstOrDefault();
            var oreNames = gates.Select(pair => _game.Tiles[pair.Tile].Name).Distinct().Take(3).ToArray();
            var ores = oreNames.Length == 1 ? $"{oreNames[0]} needs" : $"{string.Join(", ", oreNames)} need";
            var have = drill is { } current ? $"your {Name(current)} has {power}" : "you have no drill";
            if (next is null || BestRecipe(next.Id) is not { } recipe)
                return new GuideTip($"drill:{threshold}", $"{ores} drill power {threshold}; {have}.");
            var where = CraftingGuide.WhereCrafted(recipe.Recipe);
            var text = recipe.Craftable
                ? $"{ores} drill power {threshold} ({have}). Craft {CraftingGuide.Article(next.Name)} {next.Name} ({next.ToolPower}) {where} now."
                : $"{ores} drill power {threshold} ({have}). Craft {CraftingGuide.Article(next.Name)} {next.Name} ({next.ToolPower}) {where}: {IngredientList(recipe)}.{MissingChain(recipe)}";
            return new GuideTip($"drill:{threshold}", text, next.Id);
        }

        private static ItemId? SafeDrop(TileId tile)
        {
            try { return TileDropTable.GetDrop(tile); }
            catch (Exception exception) when (exception is KeyNotFoundException or ArgumentException) { return null; }
        }

        /// <summary>With no armour worn, the cheapest piece the player can make at their stations.</summary>
        public GuideTip? ArmourTip()
        {
            var equipment = _game.Player.Equipment;
            if (equipment[EquipmentSlot.Head] is not null || equipment[EquipmentSlot.Body] is not null || equipment[EquipmentSlot.Legs] is not null)
                return null;
            if (Inventory.Slots.Any(stack => !stack.IsEmpty && Items[stack.ItemId].EquipmentSlot is EquipmentSlot.Head or EquipmentSlot.Body or EquipmentSlot.Legs &&
                                             Items[stack.ItemId].Defense > 0))
                return new GuideTip("armour:wear", "You're carrying armour: right-click it in the inventory to wear it.");
            var best = Crafting.Recipes
                .Where(recipe => Items[recipe.Output].EquipmentSlot is EquipmentSlot.Head or EquipmentSlot.Body or EquipmentSlot.Legs &&
                                 Items[recipe.Output].Defense > 0 && !Items[recipe.Output].IsVanity)
                .Select(recipe => CraftingGuide.Describe(Crafting, recipe, Inventory, _base))
                .Where(status => !status.Locked && status.MissingStations.Count == 0 && status.MissingConditions.Count == 0)
                .OrderBy(status => status.MissingIngredientCount)
                .ThenBy(status => Items[status.Recipe.Output].Tier)
                .ThenBy(status => status.Ingredients.Sum(ingredient => ingredient.Need))
                .FirstOrDefault();
            if (best is null)
                return null;
            var name = Name(best.Recipe.Output);
            return new GuideTip("armour",
                $"Armour keeps you alive: {name} {CraftingGuide.WhereCrafted(best.Recipe)} ({IngredientList(best)}).{MissingChain(best)}", best.Recipe.Output);
        }

        /// <summary>Life cores until the player has used them all.</summary>
        public GuideTip? LifeCoreTip()
        {
            var player = _game.Player;
            if (player.LifeCoresUsed >= PlayerCharacter.MaximumLifeCores || !Items.Contains(ItemId.LifeCore))
                return null;
            var name = Name(ItemId.LifeCore);
            if (Inventory.Count(ItemId.LifeCore) > 0)
                return new GuideTip("lifecore:use", $"Use your {name}: each adds {PlayerCharacter.LifeCoreBonus} maximum life ({player.LifeCoresUsed}/{PlayerCharacter.MaximumLifeCores} used).", ItemId.LifeCore);
            return new GuideTip("lifecore",
                $"Grow tougher: find {name}s in caves and use them, +{PlayerCharacter.LifeCoreBonus} maximum life each ({player.LifeCoresUsed}/{PlayerCharacter.MaximumLifeCores} used).", ItemId.LifeCore);
        }

        /// <summary>The next boss in the challenge order: its summon, where that is made, and what holds it back.</summary>
        public GuideTip? BossTip()
        {
            if (EnemyArchetypes.BossProgression.All(_game.DefeatedBosses.Contains))
                return null;
            var boss = _game.NextBoss;
            var bossName = EnemyArchetypes.Get(boss).DisplayName;
            var fracture = boss == GameSimulation.CoreGateBoss && !_game.FractureActive
                ? " Beating it fractures the planet's crust: harder creatures, new ores and new bosses."
                : string.Empty;
            var summons = Items.All.Where(item => item.SummonsBoss == boss).Select(item => item.Id).ToArray();
            var refusal = _game.SummonRefusal(boss);
            var gate = refusal is null ? string.Empty : $" ({refusal})";
            if (summons.Length == 0)
                return new GuideTip($"boss:{boss}", $"Next challenge: the {bossName}.{fracture}{gate}");
            if (summons.Where(item => Inventory.Count(item) > 0).Cast<ItemId?>().FirstOrDefault() is { } carried)
                return new GuideTip($"boss:{boss}", $"Next challenge: the {bossName}. Use your {Name(carried)} when you're ready.{fracture}{gate}", carried);
            var made = summons.Select(item => (Item: item, Recipe: BestRecipe(item))).Where(pair => pair.Recipe is not null).FirstOrDefault();
            if (made.Recipe is not { } recipe)
                return new GuideTip($"boss:{boss}", $"Next challenge: the {bossName}, called with {CraftingGuide.Article(Name(summons[0]))} {Name(summons[0])} ({HowToGet(summons[0], 0)}).{fracture}{gate}", summons[0]);
            return new GuideTip($"boss:{boss}",
                $"Next challenge: the {bossName}. Craft {CraftingGuide.Article(Name(made.Item))} {Name(made.Item)} {CraftingGuide.WhereCrafted(recipe.Recipe)}: {IngredientList(recipe)}.{MissingChain(recipe)}{fracture}{gate}",
                made.Item);
        }
    }
}
