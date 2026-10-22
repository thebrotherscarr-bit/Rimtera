using Rimtera.Entities;
using Rimtera.UI;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// `--item-audit`: checks every registered item for (a) a working function — what using, equipping, consuming,
/// placing, firing or crafting with it does — and (b) a sprite of its own: a png in Assets/Sprites that
/// <see cref="SpriteCatalog.ForItem(ItemId)"/> resolves to and no other item shares (the tier-tinted
/// <see cref="ItemArt"/> fallback does not count). Utility, consumable and critter items are probed in a live
/// simulation: a use that reaches <see cref="GameSimulation.UnhandledUses"/> has no handler.
/// `--item-audit --kitbash` also prints a starter kitbash recipe for every item without its own sprite.
/// </summary>
public static class ItemAudit
{
    public sealed record Entry(ItemDefinition Item, string Kind, string Function, bool HasFunction, string? Sprite, bool SpriteFile, string? SharedWith)
    {
        public bool OwnSprite => SpriteFile && SharedWith is null;
    }

    /// <summary>Equipment tags some system reads (GameSimulation.Gear.cs and friends, status immunities, gear hooks).</summary>
    public static readonly IReadOnlySet<string> KnownFlagTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Gear flags (GameSimulation.Gear.cs and the movement/vision hooks).
        "knockback-immune", "water-walk", "night-vision", "ore-detector", "danger-sense", "auto-swing", "fire-block", "dark",
        "glide-rig", "glide", "grapple", "rocket-boots", "flight", "featherfall", "double-jump", "climb", "magnet", "gills",
        "catch-scanner", "breath", "lava", "acid", "cold", "heat", "toxic", "fishing-line", "acid-fishing", "lava-fishing",
        // Information gear (GameSimulation.InfoGear.cs).
        "position-readout", "depth-readout", "ore-readout", "life-readout",
        // Descriptive markers on items whose function lives in their stats (wing flight time, lure fishing power).
        "wings", "wing-climb", "fishing-lure",
    };

    /// <summary>Numeric "key:value" tag keys read through <see cref="GameSimulation.GearTagValue"/>.</summary>
    public static readonly IReadOnlySet<string> KnownNumericTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ammosave", "ap", "ap-melee", "attackspeed", "build", "chargecost", "chargehit", "crit", "dmg", "dmg-command", "dmg-melee",
        "dmg-ranged", "dmg-tech", "dodge", "drone", "endurance", "fish", "lifesteal", "light", "mining", "reach", "sentry", "speed", "thorns",
    };

    private static ItemCatalog? _catalog;
    private static CraftingSystem? _crafting;

    public static string SpriteDirectory(string? root = null) =>
        Path.Combine(root ?? AppContext.BaseDirectory, "Assets", "Sprites");

    /// <summary>Audits every item. <paramref name="probe"/> runs the live use probe for utility, consumable and critter items.</summary>
    public static IReadOnlyList<Entry> Run(string? spriteDirectory = null, bool probe = true)
    {
        var catalog = _catalog ??= new ItemCatalog();
        var crafting = _crafting ??= new CraftingSystem(catalog);
        spriteDirectory ??= SpriteDirectory();
        var files = Directory.Exists(spriteDirectory)
            ? Directory.EnumerateFiles(spriteDirectory, "*.png").Select(Path.GetFileNameWithoutExtension).OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool Exists(string name) => files.Contains(SpriteAtlas.ResolveName(name, files.Contains));

        var ingredients = crafting.Recipes.SelectMany(recipe => recipe.Ingredients).SelectMany(ingredient => ingredient.Alternatives).ToHashSet();
        var sold = TownNpcCatalog.All.SelectMany(npc => npc.Shop).Select(entry => entry.Item).ToHashSet();
        var ammoFamilies = catalog.All.Where(item => item.UseKind == ItemUseKind.Ammunition).Select(item => item.AmmoKind).ToHashSet();
        var ammoUsed = catalog.All.Where(item => item.AmmoType is not null).Select(item => item.AmmoType!.Value).ToHashSet();
        var weaponFamilies = catalog.All.Where(item => item.UsesAmmo != AmmoClass.None).Select(item => item.UsesAmmo).ToHashSet();
        var probed = probe ? Probe(catalog) : new Dictionary<ItemId, bool>();

        var entries = new List<Entry>();
        foreach (var item in catalog.All)
        {
            var (kind, function, ok) = Classify(item, ingredients, sold, ammoFamilies, ammoUsed, weaponFamilies, probed);
            var sprite = SpriteCatalog.ForItem(item.Id, Exists);
            var resolved = sprite is null ? null : SpriteAtlas.ResolveName(sprite, files.Contains);
            entries.Add(new Entry(item, kind, function, ok, resolved, resolved is not null && files.Contains(resolved), null));
        }
        // Several items on one picture: every one of them is flagged.
        var shared = entries.Where(entry => entry.SpriteFile).GroupBy(entry => entry.Sprite!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).ToDictionary(group => group.Key, group => group.Select(entry => entry.Item.Id).ToArray(), StringComparer.OrdinalIgnoreCase);
        return entries.Select(entry => entry.SpriteFile && shared.TryGetValue(entry.Sprite!, out var others)
            ? entry with { SharedWith = string.Join(", ", others.Where(id => id != entry.Item.Id)) }
            : entry).ToArray();
    }

    private static (string Kind, string Function, bool Ok) Classify(ItemDefinition item, HashSet<ItemId> ingredients, HashSet<ItemId> sold,
        HashSet<AmmoClass> ammoFamilies, HashSet<ItemId> ammoUsed, HashSet<AmmoClass> weaponFamilies, Dictionary<ItemId, bool> probed)
    {
        bool Probed() => probed.TryGetValue(item.Id, out var handled) && handled;
        var craftedWith = ingredients.Contains(item.Id);
        switch (item.UseKind)
        {
            case ItemUseKind.Melee or ItemUseKind.Ranged or ItemUseKind.Tech:
            {
                var ammoOk = item.UseKind != ItemUseKind.Ranged || item.UsesAmmo == AmmoClass.None && item.AmmoType is null ||
                             item.UsesAmmo != AmmoClass.None && ammoFamilies.Contains(item.UsesAmmo) || item.AmmoType is not null;
                var ok = (item.Damage > 0 || item.Style != WeaponStyle.None) && ammoOk;
                return ("weapon", ok ? $"{item.Damage} dmg {item.Style}" : ammoOk ? "no damage" : $"no ammo of family {item.UsesAmmo}", ok);
            }
            case ItemUseKind.Command:
                return ("weapon", item.Summons is { } minion ? $"summons {minion}" : "summons nothing", item.Summons is not null);
            case ItemUseKind.Pickaxe or ItemUseKind.Cutter or ItemUseKind.Demolisher:
                return ("tool", $"power {item.ToolPower}", item.ToolPower > 0);
            case ItemUseKind.BugNet:
                return ("tool", "catches critters", true);
            case ItemUseKind.PlaceTile:
                return ("placeable", item.PlacedTile == TileId.Air ? "places nothing" : $"places {item.PlacedTile}", item.PlacedTile != TileId.Air);
            case ItemUseKind.PlaceWall:
                return ("placeable", $"places wall {item.PlacedWall}", item.PlacedWall != 0);
            case ItemUseKind.Ammunition:
            {
                var ok = item.AmmoKind != AmmoClass.None && weaponFamilies.Contains(item.AmmoKind) || ammoUsed.Contains(item.Id);
                return ("ammo", ok ? $"ammo {item.AmmoKind}" : "no weapon fires it", ok);
            }
            case ItemUseKind.Food:
                return ("consumable", item.FoodRestore > 0f ? $"food {item.FoodRestore:0.##}" : "restores nothing", item.FoodRestore > 0f || Probed());
            case ItemUseKind.Consumable:
                return ("consumable", Probed() ? EffectOf(item) : "no effect", Probed());
            case ItemUseKind.Critter:
                return ("critter", item.ReleasesCritter is { } critter ? $"releases {critter}" : item.BaitPower > 0 ? "bait" : "nothing",
                    item.ReleasesCritter is not null || item.BaitPower > 0);
            case ItemUseKind.Equipment:
            {
                var stats = EquipmentStats(item);
                var unread = item.ImmunityTags.Where(tag => !IsKnownTag(tag)).ToArray();
                var kind = item.EquipmentSlot is EquipmentSlot.Head or EquipmentSlot.Body or EquipmentSlot.Legs ? "armor" : "accessory";
                if (unread.Length > 0)
                    return (kind, "tag no system reads: " + string.Join(", ", unread), false);
                return (kind, stats.Count > 0 ? string.Join(" ", stats) : "no stats", stats.Count > 0);
            }
            case ItemUseKind.Vanity:
                return ("vanity", item.EquipmentSlot is not null ? "cosmetic" : "no slot", item.EquipmentSlot is not null);
            case ItemUseKind.Utility:
                return ("utility", Probed() ? "handled" : "no handler", Probed());
            case ItemUseKind.Material:
            {
                var uses = new List<string>();
                if (craftedWith) uses.Add("recipe");
                if (sold.Contains(item.Id)) uses.Add("sold");
                if (item.Value > 0 || item.ShopPrice > 0) uses.Add($"value {item.CreditValue}");
                if (item.BaitPower > 0) uses.Add("bait");
                if (item.IsQuestFish) uses.Add("quest");
                if (PlayerSystemsContent.TrophyBoss(item.Id) is not null || PlayerSystemsContent.RelicBoss(item.Id) is not null) uses.Add("display");
                return ("material", uses.Count > 0 ? string.Join(" ", uses) : "unused", uses.Count > 0);
            }
            default:
                return (item.UseKind.ToString().ToLowerInvariant(), "unknown use kind", false);
        }
    }

    private static string EffectOf(ItemDefinition item) =>
        item.SummonsBoss is { } boss ? $"summons {boss}"
        : item.StartsEvent is { } started ? $"starts {started}"
        : item.GrantsStatus is { } status ? $"grants {status}"
        : item.IsCrate ? "opens"
        : item.HealAmount > 0f ? $"heals {item.HealAmount:0}"
        : "handled";

    /// <summary>What wearing the item changes (empty when nothing).</summary>
    public static List<string> EquipmentStats(ItemDefinition item)
    {
        var stats = new List<string>();
        void Add(bool when, string label) { if (when) stats.Add(label); }
        Add(item.Defense > 0, $"def {item.Defense}");
        Add(item.SetBonusDefense > 0 && item.ArmorSetId is not null, "set");
        Add(item.MaximumLifeBonus != 0f, "life");
        Add(item.MaximumManaBonus != 0f, "charge");
        Add(item.LifeRegenerationPerHour != 0f, "regen");
        Add(item.ManaRegenerationPerHour != 0f, "charge-regen");
        Add(item.DamageMultiplier != 1f, "dmg");
        Add(item.KnockbackMultiplier != 1f, "kb");
        Add(item.MoveSpeedMultiplier != 1f, "speed");
        Add(item.MeleeDamageBonus + item.RangedDamageBonus + item.TechDamageBonus + item.CommandDamageBonus != 0f, "class-dmg");
        Add(item.CritBonus != 0, "crit");
        Add(item.DroneSlotBonus + item.SentrySlotBonus != 0, "slots");
        Add(item.ChargeRegenBonus != 0f, "charge-regen");
        Add(item.WingFlightSeconds > 0f, "flight");
        Add(item.FishingPower > 0, "fishing");
        Add(item.ArmorSetId is not null && ArsenalItems.SetSpecials.ContainsKey(item.ArmorSetId), "set-special");
        Add(PlayerSystemsContent.GearLuck(item.Id) != 0f, "luck");
        Add(PlayerSystemsContent.GearAggro(item.Id) != 0, "aggro");
        Add(PlayerSystemsContent.IsDye(item.Id), "dye");
        foreach (var tag in item.ImmunityTags)
            Add(IsKnownTag(tag), tag);
        return stats;
    }

    /// <summary>Numeric "key:value" tags and the flag tags some system reads; status immunity tags count too.</summary>
    public static bool IsKnownTag(string tag) =>
        tag.IndexOf(':') is var colon and > 0 ? KnownNumericTags.Contains(tag[..colon]) : KnownFlagTags.Contains(tag) || StatusImmunityTags.Value.Contains(tag);

    /// <summary>Tags that block a status effect (a status's immunity tag), read by the status system.</summary>
    private static readonly Lazy<HashSet<string>> StatusImmunityTags = new(() =>
    {
        var system = new StatusEffectSystem();
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var status in Enum.GetValues<StatusId>())
            try
            {
                if (system[status].BlockedByImmunityTag is { } blocked)
                    tags.Add(blocked);
            }
            catch (KeyNotFoundException) { }
        return tags;
    });

    /// <summary>Tags on wearable items that no system reads.</summary>
    public static IEnumerable<(ItemDefinition Item, string Tag)> UnknownTags(IEnumerable<ItemDefinition> items) =>
        items.Where(item => item.UseKind == ItemUseKind.Equipment)
            .SelectMany(item => item.ImmunityTags.Where(tag => !IsKnownTag(tag)).Select(tag => (item, tag)));

    // ------------------------------------------------------------------ live probe

    /// <summary>
    /// Uses every utility, consumable, food and critter item once in a live simulation and records whether the
    /// use reached a handler (true) or the "no direct use" fallthrough (false).
    /// </summary>
    public static Dictionary<ItemId, bool> Probe(ItemCatalog catalog)
    {
        var results = new Dictionary<ItemId, bool>();
        GameSimulation? game = null;
        foreach (var item in catalog.All.Where(item => item.UseKind is ItemUseKind.Utility or ItemUseKind.Consumable or ItemUseKind.Food or ItemUseKind.Critter))
        {
            game ??= NewProbeGame();
            try
            {
                var before = game.UnhandledUses;
                game.Player.Inventory.TakeFromSlot(0);
                game.Player.Inventory.PlaceIntoSlot(0, new ItemStack(item.Id, Math.Min(5, item.MaxStack)));
                game.TrySelectInventorySlot(0);
                game.Player.ActionCooldownTicks = 0;
                var target = new GridPoint((int)game.Player.Motion.Position.X + 2, (int)game.Player.Motion.Position.Y);
                game.Tick(new GameInput(new MovementInput(0f), PlayerAction.UseSelected, target));
                var handled = game.UnhandledUses == before;
                // Control: a plain material must still reach the fallthrough, or the game stopped taking uses.
                if (handled && !ProbeReachesFallthrough(game))
                {
                    game = NewProbeGame();
                    results[item.Id] = ProbeOnce(game, item.Id);
                    continue;
                }
                results[item.Id] = handled;
                foreach (var enemy in game.Enemies.ToArray())
                    enemy.TakeDamage(1e7f);
                game.Player.Heal(1e6f);
                if (game.Player.IsDead)
                    game = null;
            }
            catch (Exception)
            {
                results[item.Id] = false;
                game = null;
            }
        }
        return results;
    }

    private static bool ProbeOnce(GameSimulation game, ItemId id)
    {
        var before = game.UnhandledUses;
        game.Player.Inventory.TakeFromSlot(0);
        game.Player.Inventory.PlaceIntoSlot(0, new ItemStack(id, 1));
        game.TrySelectInventorySlot(0);
        game.Player.ActionCooldownTicks = 0;
        var target = new GridPoint((int)game.Player.Motion.Position.X + 2, (int)game.Player.Motion.Position.Y);
        game.Tick(new GameInput(new MovementInput(0f), PlayerAction.UseSelected, target));
        return game.UnhandledUses == before;
    }

    private static bool ProbeReachesFallthrough(GameSimulation game) => !ProbeOnce(game, ItemId.CopperOre);

    private static GameSimulation NewProbeGame()
    {
        var game = new GameSimulation(seed: 7171, width: 96, height: 56) { FamilySpawnsEnabled = false };
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e7f);
        return game;
    }

    /// <summary>Prints the creature sprite behind every kill banner and boss item (pasted into tools/sprites/kitbash.py).</summary>
    private static void PrintDynamicKitbash(string? root)
    {
        var files = Directory.EnumerateFiles(SpriteDirectory(root), "*.png").Select(Path.GetFileNameWithoutExtension).OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? Creature(EnemyKind kind)
        {
            foreach (var candidate in new[] { SpriteCatalog.ForEnemy(kind), "creature_" + SpriteCatalog.SnakeCase(kind.ToString()), "boss_" + SpriteCatalog.SnakeCase(kind.ToString()) })
                if (SpriteAtlas.ResolveName(candidate, files.Contains) is var resolved && files.Contains(resolved))
                    return resolved;
            return null;
        }
        var catalog = _catalog ??= new ItemCatalog();
        Console.WriteLine("_BANNER_CREATURES = {");
        foreach (var item in catalog.All)
            if (PlayerItems.BannerKindOf(item.Id) is { } kind)
                Console.WriteLine($"    \"{SpriteCatalog.SnakeCase(kind.ToString())}\": \"{Creature(kind)}\",");
        Console.WriteLine("}");
        Console.WriteLine("_BOSS_SPRITES = {");
        foreach (var boss in PlayerSystemsContent.Bosses)
            Console.WriteLine($"    \"{SpriteCatalog.SnakeCase(boss.ToString())}\": \"{Creature(boss)}\",");
        Console.WriteLine("}");
    }

    // ------------------------------------------------------------------ report

    public static int Report(string[] args)
    {
        var root = Directory.Exists(Path.Combine(Environment.CurrentDirectory, "Assets", "Sprites")) ? Environment.CurrentDirectory : null;
        var entries = Run(SpriteDirectory(root));
        var noFunction = entries.Where(entry => !entry.HasFunction).ToArray();
        var noSprite = entries.Where(entry => !entry.OwnSprite).ToArray();
        Console.WriteLine($"Items: {entries.Count}");
        foreach (var group in entries.GroupBy(entry => entry.Kind).OrderBy(group => group.Key))
            Console.WriteLine($"  {group.Key,-12}{group.Count(),5}  no function {group.Count(entry => !entry.HasFunction),4}  no own sprite {group.Count(entry => !entry.OwnSprite),4}");
        Console.WriteLine($"Without a function: {noFunction.Length}");
        foreach (var entry in noFunction)
            Console.WriteLine($"  FUNC {entry.Item.Id,-28} {entry.Kind,-10} {entry.Item.UseKind,-12} {entry.Function}  \"{entry.Item.Name}\"");
        Console.WriteLine($"Without their own sprite: {noSprite.Length} (missing {noSprite.Count(entry => !entry.SpriteFile)}, shared {noSprite.Count(entry => entry.SpriteFile)})");
        foreach (var entry in noSprite)
            Console.WriteLine(entry.SpriteFile
                ? $"  SHARED {entry.Item.Id,-28} {entry.Sprite} (with {entry.SharedWith})"
                : $"  MISSING {entry.Item.Id,-28} {entry.Sprite}");
        var unknownTags = UnknownTags(entries.Select(entry => entry.Item)).ToArray();
        Console.WriteLine($"Equipment tags no system reads: {unknownTags.Length}");
        foreach (var (item, tag) in unknownTags)
            Console.WriteLine($"  TAG {item.Id,-28} {tag}");
        if (args.Contains("--kitbash", StringComparer.OrdinalIgnoreCase))
            PrintDynamicKitbash(root);
        if (args.Contains("--tsv", StringComparer.OrdinalIgnoreCase))
            foreach (var entry in entries)
                Console.WriteLine($"TSV\t{(int)entry.Item.Id}\t{entry.Item.Id}\t{entry.Item.Name}\t{entry.Kind}\t{entry.Item.UseKind}\t{entry.Item.Style}\t{entry.Item.EquipmentSlot}\t{entry.Item.Tier}\t{entry.HasFunction}\t{entry.Function}\t{entry.Sprite}\t{entry.SpriteFile}\t{entry.SharedWith}");
        return 0;
    }
}
