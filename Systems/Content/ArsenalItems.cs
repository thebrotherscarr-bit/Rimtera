using Rimtera.Entities;

namespace Rimtera.Systems;

/// <summary>The special effect an armor set adds on top of its stat bonus (resolved in GameSimulation.Gear).</summary>
public enum SetSpecial : byte
{
    None,
    /// <summary>Chance (magnitude) to ignore a creature hit entirely.</summary>
    Dodge,
    /// <summary>Fraction (magnitude) of contact damage reflected at the attacker.</summary>
    Thorns,
    /// <summary>Fraction (magnitude) of damage dealt returned as hit points.</summary>
    HealOnHit,
    /// <summary>Charge (magnitude) restored per hit landed.</summary>
    ChargeOnHit,
    /// <summary>When struck, a shock burst (magnitude scales it) hurts nearby hostiles.</summary>
    BurstWhenHit,
    /// <summary>Extra move speed (magnitude) below 40% hit points.</summary>
    SpeedWhenLow,
    /// <summary>Fraction (magnitude) of incoming hit damage removed.</summary>
    Endurance,
    /// <summary>Extra damage for every class (magnitude) while above 90% hit points.</summary>
    Frenzy
}

internal sealed record WeaponRow(ItemId Id, string Name, WeaponStyle Style, int Tier, string Material, float Power, float Speed,
    string? Flavour, string? Source, bool Unique);
internal sealed record ToolRow(ItemId Id, string Name, ItemUseKind Kind, int Tier, string Material, int Power, int Cooldown, string? Source);
internal sealed record AmmoRow(ItemId Id, string Name, AmmoClass Kind, int Tier, int Damage, int Pierce, string Flavour, string Recipe, int Yield);
internal sealed record ArmorRow(string SetId, string Name, int Tier, DamageClass Class, SetSpecial Special, float Magnitude,
    ItemId Head, string HeadName, ItemId Body, string BodyName, ItemId Legs, string LegsName, string? Source);
internal sealed record AccessoryRow(ItemId Id, string Name, int Tier, string Effects, string Recipe, string? Source);
internal sealed record WingRow(ItemId Id, string Name, int Tier, float Flight, float Speed, string Recipe, string? Source);
internal sealed record ConsumableRow(ItemId Id, string Name, int Tier, StatusId Status, float Hours, string Recipe, float Food);

/// <summary>
/// The arsenal pass: a tier ladder × weapon style generator (17 styles), tools, ammunition families, class armor
/// sets with special effects, accessories and wings, potions and food. Row tables live in ArsenalItems.Data.cs
/// (generated); this file turns rows into definitions with tier-scaled stats. All names and numbers are original.
/// </summary>
internal static partial class ArsenalItems
{
    public const int FirstId = 6000;
    public const int LastId = 6999;

    /// <summary>True for item ids added by the arsenal pass.</summary>
    public static bool Owns(ItemId id) => (int)id is >= FirstId and <= LastId;

    /// <summary>Material ladder: tier, bar and the secondary ingredient used by generated recipes.</summary>
    public static readonly IReadOnlyDictionary<string, (int Tier, ItemId Bar, ItemId Extra, int ExtraCount)> Materials =
        new Dictionary<string, (int, ItemId, ItemId, int)>
        {
            ["Ferrite"] = (1, ItemId.FerriteBar, ItemId.WoodBlock, 4),
            ["Cobalt"] = (2, ItemId.CobaltBar, ItemId.EnergyCrystal, 2),
            ["Glacial"] = (3, ItemId.GlacialBar, ItemId.FrostCrystal, 2),
            ["Pyrocite"] = (3, ItemId.PyrociteBar, ItemId.ObsidianShard, 2),
            ["Uranium"] = (3, ItemId.UraniumRod, ItemId.CausticShard, 2),
            ["Meteorite"] = (3, ItemId.MeteoriteBar, ItemId.EnergyCrystal, 3),
            ["Viridium"] = (4, ItemId.ViridiumBar, ItemId.EnergyCrystal, 4),
            ["Lumenite"] = (5, ItemId.LumeniteBar, ItemId.PrismShard, 3),
            ["Voidsteel"] = (6, ItemId.VoidsteelBar, ItemId.VoidEssence, 2),
            ["Astral"] = (7, ItemId.AstralBar, ItemId.AstralOre, 2)
        };

    // Per-tier damage, use times, knockback and charge come from GearCurve (Terraria-shaped tier curve, docs/GEAR_DIFF_ITEMS.md).
    private static int[] MeleeBase => GearCurve.MeleeBase;
    private static int[] RangedBase => GearCurve.RangedBase;
    private static int[] TechBase => GearCurve.TechBase;
    private static int[] CommandBase => GearCurve.CommandBase;
    private static int[] ChargeBase => GearCurve.ChargeBase;

    /// <summary>Armor set special effects, keyed by set id (new sets and the content-expansion sets).</summary>
    public static readonly Dictionary<string, (SetSpecial Special, float Magnitude)> SetSpecials = new(StringComparer.OrdinalIgnoreCase)
    {
        ["trail"] = (SetSpecial.SpeedWhenLow, 0.1f),
        ["scav"] = (SetSpecial.SpeedWhenLow, 0.15f),
        ["cobalt-trooper"] = (SetSpecial.Endurance, 0.04f),
        ["pyrocite-bulwark"] = (SetSpecial.BurstWhenHit, 1f),
        ["glacial-ranger"] = (SetSpecial.Dodge, 0.05f),
        ["uranium-hazmat"] = (SetSpecial.ChargeOnHit, 1.5f),
        ["glacial-handler"] = (SetSpecial.Endurance, 0.06f),
        ["viridium-warden"] = (SetSpecial.HealOnHit, 0.03f),
        ["viridium-stalker"] = (SetSpecial.Frenzy, 0.1f),
        ["viridium-sage"] = (SetSpecial.BurstWhenHit, 1.3f),
        ["viridium-handler"] = (SetSpecial.Dodge, 0.06f),
        ["lumenite-paladin"] = (SetSpecial.Endurance, 0.1f),
        ["lumenite-marksman"] = (SetSpecial.Dodge, 0.08f),
        ["lumenite-sage"] = (SetSpecial.ChargeOnHit, 2.5f),
        ["lumenite-handler"] = (SetSpecial.SpeedWhenLow, 0.3f),
        ["voidsteel-reaver"] = (SetSpecial.HealOnHit, 0.05f),
        ["voidsteel-marksman"] = (SetSpecial.Frenzy, 0.12f),
        ["voidsteel-wraith"] = (SetSpecial.Dodge, 0.1f),
        ["voidsteel-handler"] = (SetSpecial.Thorns, 0.4f),
        ["astral-warden"] = (SetSpecial.Endurance, 0.12f),
        ["astral-marksman"] = (SetSpecial.Dodge, 0.1f),
        ["astral-sage"] = (SetSpecial.ChargeOnHit, 4f),
        ["astral-handler"] = (SetSpecial.BurstWhenHit, 2.2f)
    };

    /// <summary>Non-recipe sources: "boss:Kind", "mob:Kind:percent", "cache", "shop:Npc".</summary>
    public static readonly Dictionary<ItemId, string> LootSources = new();
    /// <summary>Recipes given as ingredient strings ("A*2+B*1@Station") or derived ("craft:Material"), per item.</summary>
    internal static readonly Dictionary<ItemId, string> RecipeHints = new();
    /// <summary>Thrown consumables yield a stack per craft.</summary>
    internal static readonly Dictionary<ItemId, int> RecipeYield = new();

    /// <summary>Boss → items it may drop (one is rolled per kill).</summary>
    public static IReadOnlyDictionary<EnemyKind, ItemId[]> BossPools => _bossPools ??= LootSources
        .Where(pair => pair.Value.StartsWith("boss:", StringComparison.Ordinal))
        .GroupBy(pair => Enum.Parse<EnemyKind>(pair.Value[5..]))
        .ToDictionary(group => group.Key, group => group.Select(pair => pair.Key).OrderBy(id => id).ToArray());
    private static IReadOnlyDictionary<EnemyKind, ItemId[]>? _bossPools;

    /// <summary>Regular creatures → (item, percent chance) rare drops.</summary>
    public static IReadOnlyDictionary<EnemyKind, (ItemId Item, int Percent)[]> MobDrops => _mobDrops ??= LootSources
        .Where(pair => pair.Value.StartsWith("mob:", StringComparison.Ordinal))
        .Select(pair => (pair.Key, Parts: pair.Value.Split(':')))
        .GroupBy(entry => Enum.Parse<EnemyKind>(entry.Parts[1]))
        .ToDictionary(group => group.Key, group => group.Select(entry => (entry.Key, int.Parse(entry.Parts[2]))).OrderBy(e => e.Key).ToArray());
    private static IReadOnlyDictionary<EnemyKind, (ItemId Item, int Percent)[]>? _mobDrops;

    /// <summary>Items found in supply caches.</summary>
    public static IEnumerable<ItemId> CacheItems => LootSources.Where(pair => pair.Value == "cache").Select(pair => pair.Key).OrderBy(id => id);

    /// <summary>Items sold by a town NPC kind.</summary>
    public static IEnumerable<ItemId> ShopItems(TownNpcKind kind) =>
        LootSources.Where(pair => pair.Value == "shop:" + kind).Select(pair => pair.Key).OrderBy(id => id);

    /// <summary>Counts by area, for the census and the self-tests.</summary>
    internal static int WeaponCount => WeaponRows.Length;
    internal static IEnumerable<ItemId> AllIds => WeaponRows.Select(r => r.Id).Concat(ToolRows.Select(r => r.Id)).Concat(AmmoRows.Select(r => r.Id))
        .Concat(ArmorRows.SelectMany(r => new[] { r.Head, r.Body, r.Legs })).Concat(AccessoryRows.Select(r => r.Id))
        .Concat(WingRows.Select(r => r.Id)).Concat(ConsumableRows.Select(r => r.Id));
    internal static IEnumerable<string> NewSetIds => ArmorRows.Select(r => r.SetId);

    /// <summary>True when a wing row gives climbing flight (crafted with flight essence, or a tier 5+ drop).</summary>
    internal static bool WingClimbs(WingRow row) => row.Recipe.Contains("FlightEssence", StringComparison.Ordinal) || row.Tier >= 5;

    public static void RegisterAll(Dictionary<ItemId, ItemDefinition> items)
    {
        LootSources.Clear();
        RecipeHints.Clear();
        RecipeYield.Clear();
        _bossPools = null;
        _mobDrops = null;
        void Add(ItemDefinition definition) => items[definition.Id] = definition;
        void Source(ItemId id, string? source, string? recipe = null)
        {
            if (!string.IsNullOrEmpty(recipe))
                RecipeHints[id] = recipe;
            if (source is null)
                return;
            if (source.StartsWith("craft:", StringComparison.Ordinal))
                RecipeHints[id] = source;
            else
                LootSources[id] = source;
        }

        foreach (var row in WeaponRows)
        {
            Add(BuildWeapon(row));
            Source(row.Id, row.Source);
            if (row.Style == WeaponStyle.Thrown)
                RecipeYield[row.Id] = row.Unique ? 20 : row.Name.Contains("javelin") ? 15 : 30;
        }
        foreach (var row in ToolRows)
        {
            Add(new ItemDefinition(row.Id, row.Name, row.Kind, 1)
            {
                // A 0 in the row takes power and use time from the Terraria-shaped tool curve (GearCurve).
                Tier = row.Tier, Rarity = ContentItems.RarityFor(row.Tier),
                ToolPower = row.Power > 0 ? row.Power : GearCurve.ToolPower(row.Kind, row.Tier, row.Material, row.Material == "Unique"),
                UseCooldownTicks = row.Cooldown > 0 ? row.Cooldown : GearCurve.ToolCooldown(row.Kind, row.Tier, row.Material),
                Value = 20 + row.Tier * row.Tier * 20, Damage = row.Kind == ItemUseKind.Pickaxe ? 0 : 4 + row.Tier * 3
            });
            Source(row.Id, row.Source);
        }
        foreach (var row in AmmoRows)
        {
            var flavour = row.Flavour.Split(';', StringSplitOptions.RemoveEmptyEntries);
            bool Has(string key) => flavour.Contains(key);
            float Num(string key, float fallback) =>
                flavour.FirstOrDefault(part => part.StartsWith(key + "=", StringComparison.Ordinal)) is { } part
                    ? float.Parse(part[(key.Length + 1)..], System.Globalization.CultureInfo.InvariantCulture) : fallback;
            Add(new ItemDefinition(row.Id, row.Name, ItemUseKind.Ammunition, 999)
            {
                Class = DamageClass.Ranged, Tier = row.Tier, Rarity = ContentItems.RarityFor(row.Tier), Value = 1 + row.Tier / 2,
                AmmoKind = row.Kind, AmmoDamage = row.Damage, Pierce = row.Pierce,
                AmmoHoming = Has("homing") ? 2.5f + row.Tier * 0.25f : 0f,
                ExplosionRadius = Has("explode") ? 1.5f + row.Tier * 0.25f : 0f,
                PurifyRadius = Has("purify") ? 3f : 0f,
                Knockback = Num("kb", 0f),
                // Extra muzzle velocity (tiles per second) this ammo adds.
                ProjectileSpeed = (Num("speed", 1f) - 1f) * 20f,
                // On-hit status for the target (burn/poison flavour); ammo is never quick-buffed.
                GrantsStatus = Has("burn") ? StatusId.Burning : Has("poison") ? StatusId.Poison : Has("venom") ? StatusId.Venom : null,
                StatusDurationHours = Has("burn") || Has("poison") || Has("venom") ? 1f : 0f
            });
            RecipeHints[row.Id] = row.Recipe;
            RecipeYield[row.Id] = row.Yield;
        }
        foreach (var row in ArmorRows)
            RegisterArmor(row, Add, Source);
        foreach (var row in AccessoryRows)
        {
            Add(BuildAccessory(row));
            Source(row.Id, row.Source, row.Recipe);
        }
        foreach (var row in WingRows)
        {
            Add(new ItemDefinition(row.Id, row.Name, ItemUseKind.Equipment, 1)
            {
                EquipmentSlot = global::Rimtera.Systems.EquipmentSlot.Accessory, Tier = row.Tier, Rarity = ContentItems.RarityFor(row.Tier),
                Value = 80 + row.Tier * row.Tier * 40, WingFlightSeconds = row.Flight, MoveSpeedMultiplier = row.Speed,
                // G3: only flight-essence wings and post-Fracture drops (tier 5+) climb; earlier pairs are glide rigs.
                ImmunityTags = WingClimbs(row) ? Tags("wings", "glide", "featherfall", "wing-climb") : Tags("wings", "glide", "featherfall", "glide-rig")
            });
            Source(row.Id, row.Source, row.Recipe);
        }
        GrowthItems.RegisterAll(items); // character growth items (Systems/Content/GrowthItems.cs)
        foreach (var row in ConsumableRows)
        {
            var food = row.Food > 0f;
            Add(new ItemDefinition(row.Id, row.Name, food ? ItemUseKind.Food : ItemUseKind.Consumable, food ? 99 : 30)
            {
                Tier = row.Tier, Rarity = ContentItems.RarityFor(row.Tier), Value = 8 + row.Tier * 6, FoodRestore = row.Food,
                GrantsStatus = row.Status, StatusDurationHours = row.Hours
            });
            RecipeHints[row.Id] = row.Recipe;
        }
    }

    private static HashSet<string> Tags(params string[] tags) => new(tags, StringComparer.OrdinalIgnoreCase);

    private static float Flavour(string? flavour, string key, float fallback)
    {
        if (flavour is null)
            return fallback;
        foreach (var part in flavour.Split(';'))
            if (part.StartsWith(key + "=", StringComparison.Ordinal) &&
                float.TryParse(part[(key.Length + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
                return value;
        return fallback;
    }

    private static string? FlavourText(string? flavour, string key) =>
        flavour?.Split(';').FirstOrDefault(part => part.StartsWith(key + "=", StringComparison.Ordinal))?[(key.Length + 1)..];

    private static ItemDefinition BuildWeapon(WeaponRow row)
    {
        var t = Math.Clamp(row.Tier, 1, 7);
        var power = row.Power * (row.Unique ? 1.08f : 1f);
        var speed = row.Speed * Flavour(row.Flavour, "speed", 1f);
        int Cooldown(int baseTicks) => Math.Max(3, (int)MathF.Round(baseTicks / speed));
        int Dmg(int[] table, float factor) => Math.Max(1, (int)MathF.Round(table[t] * factor * power));
        var value = (int)((25 + 30 * t * t) * (row.Unique ? 1.3f : 1f));
        var rarity = ContentItems.RarityFor(row.Unique ? Math.Min(7, t + 1) : t);
        var crit = 4 + (int)Flavour(row.Flavour, "crit", 0f);
        var kb = Flavour(row.Flavour, "kb", 1f);
        var pierce = (int)Flavour(row.Flavour, "pierce", 0f);
        var reachBonus = Flavour(row.Flavour, "reach", 0f);
        var count = (int)Flavour(row.Flavour, "count", 0f);
        var explosive = row.Name.Contains("grenade", StringComparison.OrdinalIgnoreCase) || row.Name.Contains("mine", StringComparison.OrdinalIgnoreCase) ||
                        row.Name.Contains("bomb", StringComparison.OrdinalIgnoreCase) || row.Name.Contains("EMP", StringComparison.Ordinal) ||
                        row.Name.Contains("flask", StringComparison.OrdinalIgnoreCase);
        ItemDefinition Base(ItemUseKind kind, DamageClass cls, int stack = 1) => new(row.Id, row.Name, kind, stack)
        {
            Class = cls, Tier = t, Rarity = rarity, Value = value, Style = row.Style, CritChance = crit
        };
        ItemDefinition Ranged(AmmoClass ammo, ItemId fallback, float dmg, int cd, float projectileSpeed, float knockback) =>
            Base(ItemUseKind.Ranged, DamageClass.Ranged) with
            {
                Damage = Dmg(RangedBase, dmg), UseCooldownTicks = Cooldown(cd), UsesAmmo = ammo, AmmoType = fallback,
                ProjectileSpeed = projectileSpeed, Knockback = knockback * kb, Pierce = pierce,
                ProjectileCount = Math.Max(1, count), ProjectileSpread = count > 1 ? 0.1f : 0.04f
            };

        int Use(WeaponStyle style) => Cooldown(GearCurve.StyleUseTime(style, t, explosive));
        float Kb(WeaponStyle style) => GearCurve.StyleKnockback(style, explosive) * kb;
        float F(WeaponStyle style) => GearCurve.StyleDamageFactor(style, explosive);
        return row.Style switch
        {
            WeaponStyle.Broadsword => Base(ItemUseKind.Melee, DamageClass.Melee) with
                { Damage = Dmg(MeleeBase, F(row.Style)), UseCooldownTicks = Use(row.Style), Knockback = Kb(row.Style), Pierce = pierce },
            // Terraria shortswords carry no bonus crit; uniques keep their crit flavour.
            WeaponStyle.Shortsword => Base(ItemUseKind.Melee, DamageClass.Melee) with
                { Damage = Dmg(MeleeBase, F(row.Style)), UseCooldownTicks = Use(row.Style), Knockback = Kb(row.Style) },
            WeaponStyle.Spear => Base(ItemUseKind.Melee, DamageClass.Melee) with
                { Damage = Dmg(MeleeBase, F(row.Style)), UseCooldownTicks = Use(row.Style), Knockback = Kb(row.Style), Reach = 6.5f + 0.2f * t + reachBonus, Pierce = 2 },
            WeaponStyle.Yoyo => Base(ItemUseKind.Melee, DamageClass.Melee) with
            {
                Damage = Dmg(MeleeBase, F(row.Style)), UseCooldownTicks = Use(row.Style), Knockback = Kb(row.Style), Reach = 6f + 0.4f * t,
                ProjectileSpeed = 10f + t * 0.5f, ProjectileLifetime = 4f + t * 0.4f, Pierce = 2 + pierce
            },
            WeaponStyle.Boomerang => Base(ItemUseKind.Melee, DamageClass.Melee) with
            {
                Damage = Dmg(MeleeBase, F(row.Style)), UseCooldownTicks = Use(row.Style), Knockback = Kb(row.Style), Thrown = true,
                ProjectileSpeed = 12f + t * 0.6f, ProjectileLifetime = 1.4f, Pierce = 1 + pierce
            },
            WeaponStyle.Flail => Base(ItemUseKind.Melee, DamageClass.Melee) with
            {
                Damage = Dmg(MeleeBase, F(row.Style)), UseCooldownTicks = Use(row.Style), Knockback = Kb(row.Style), Reach = 6.5f + 0.2f * t,
                ProjectileSpeed = 12f, Pierce = 1 + pierce
            },
            WeaponStyle.Whip => Base(ItemUseKind.Melee, DamageClass.Command) with
            {
                Damage = Dmg(CommandBase, F(row.Style)), UseCooldownTicks = Use(row.Style), Knockback = Kb(row.Style), Reach = 8f + 0.3f * t,
                SignalWhip = true, CritChance = 0
            },
            WeaponStyle.Bow => Ranged(AmmoClass.Arrow, ItemId.FerriteBolts, F(row.Style), GearCurve.StyleUseTime(row.Style, t), 20f + 2f * t, GearCurve.StyleKnockback(row.Style)),
            WeaponStyle.Gun => Ranged(AmmoClass.Bullet, ItemId.SlugRounds, F(row.Style), GearCurve.StyleUseTime(row.Style, t), 22f + 2f * t, GearCurve.StyleKnockback(row.Style)),
            WeaponStyle.Shotgun => Ranged(AmmoClass.Bullet, ItemId.SlugRounds, F(row.Style), GearCurve.StyleUseTime(row.Style, t), 18f + t, GearCurve.StyleKnockback(row.Style)) with
                { ProjectileCount = Math.Max(5, count), ProjectileSpread = 0.09f, ProjectileLifetime = 0.9f },
            WeaponStyle.Launcher => Ranged(AmmoClass.Rocket, ItemId.MiniRocket, F(row.Style), GearCurve.StyleUseTime(row.Style, t), 12f + 0.5f * t, GearCurve.StyleKnockback(row.Style)) with
                { ExplosionRadius = 2f + 0.25f * t },
            WeaponStyle.Thrown => Base(ItemUseKind.Ranged, DamageClass.Ranged, 999) with
            {
                Damage = Dmg(RangedBase, F(row.Style)), UseCooldownTicks = Use(row.Style), Thrown = true,
                ProjectileSpeed = explosive ? 11f : 15f + t, ProjectileLifetime = 1.4f, Knockback = Kb(row.Style),
                Pierce = pierce, ExplosionRadius = explosive ? 2.5f + 0.2f * t : 0f, Value = Math.Max(1, value / 60)
            },
            WeaponStyle.Staff => Base(ItemUseKind.Tech, DamageClass.Tech) with
            {
                Damage = Dmg(TechBase, F(row.Style)), UseCooldownTicks = Use(row.Style), ChargeCost = Math.Max(2, (int)MathF.Round(ChargeBase[t] * (speed > 1.2f ? 0.7f : 1f))),
                ProjectileSpeed = 14f + t, ProjectileCount = Math.Max(1, count), ProjectileSpread = 0.15f, ProjectileLifetime = 2.5f,
                Pierce = pierce, Knockback = Kb(row.Style)
            },
            WeaponStyle.Tome => Base(ItemUseKind.Tech, DamageClass.Tech) with
            {
                Damage = Dmg(TechBase, F(row.Style)), UseCooldownTicks = Use(row.Style), ChargeCost = (int)MathF.Round(ChargeBase[t] * 1.5f),
                ProjectileSpeed = 13f + t, ProjectileCount = Math.Max(3, count), ProjectileSpread = 0.22f, ProjectileLifetime = 2f,
                Pierce = pierce, Knockback = Kb(row.Style)
            },
            WeaponStyle.Beam => Base(ItemUseKind.Tech, DamageClass.Tech) with
            {
                Damage = Dmg(TechBase, F(row.Style)), UseCooldownTicks = GearCurve.StyleUseTime(row.Style, t), ChargeCost = Math.Max(1, (int)MathF.Round(ChargeBase[t] * 0.3f)),
                ProjectileSpeed = 30f, ProjectileLifetime = 0.5f, Pierce = 3 + pierce, Knockback = Kb(row.Style)
            },
            WeaponStyle.SummonStaff or WeaponStyle.SentryStaff => Base(ItemUseKind.Command, DamageClass.Command) with
            {
                Damage = Dmg(CommandBase, F(row.Style)), UseCooldownTicks = GearCurve.StyleUseTime(row.Style, t), CritChance = 0,
                Summons = Enum.Parse<MinionKind>(FlavourText(row.Flavour, "minion") ?? "ScoutDrone")
            },
            _ => throw new InvalidOperationException($"No arsenal profile for {row.Style}.")
        };
    }

    private static void RegisterArmor(ArmorRow row, Action<ItemDefinition> add, Action<ItemId, string?, string?> source)
    {
        var t = row.Tier;
        var special = SpecialText(row.Special, row.Magnitude);
        // Piece and set-bonus defense follow the Terraria-shaped armour curve (GearCurve.ArmorPieces).
        var (headDefense, bodyDefense, legsDefense, setDefense) = GearCurve.ArmorPieces(t, row.Class);
        var bonus = row.Class switch
        {
            DamageClass.Melee => new ArmorSetBonus(row.SetId, row.Name, row.Class,
                $"+{setDefense} defense, +{6 + t}% close-quarters damage, +{2 + t / 2}% crit; {special}",
                Defense: setDefense, ClassDamageBonus: 0.06f + 0.01f * t, CritBonus: 2 + t / 2),
            DamageClass.Ranged => new ArmorSetBonus(row.SetId, row.Name, row.Class,
                $"+{8 + 2 * t}% ballistics damage, +{4 + t}% crit, {15 + 3 * t}% ammo saving; {special}",
                ClassDamageBonus: 0.08f + 0.02f * t, CritBonus: 4 + t, AmmoSaveChance: 0.15f + 0.03f * t),
            DamageClass.Tech => new ArmorSetBonus(row.SetId, row.Name, row.Class,
                $"+{20 + 10 * t} max charge, +{2 + t} charge regen, +{8 + 2 * t}% tech damage; {special}",
                ClassDamageBonus: 0.08f + 0.02f * t, ChargeMaxBonus: 20 + 10 * t, ChargeRegenBonus: 2 + t),
            DamageClass.Command => new ArmorSetBonus(row.SetId, row.Name, row.Class,
                $"+{(t >= 4 ? 2 : 1)} drone slots{(t >= 3 ? ", +1 sentry slot" : "")}, +{8 + 2 * t}% drone damage; {special}",
                ClassDamageBonus: 0.08f + 0.02f * t, DroneSlots: t >= 4 ? 2 : 1, SentrySlots: t >= 3 ? 1 : 0),
            _ => new ArmorSetBonus(row.SetId, row.Name, row.Class, $"+{setDefense} defense, +{t}% crit; {special}", Defense: setDefense, CritBonus: t)
        };
        ArmorSets.Register(bonus);
        SetSpecials[row.SetId] = (row.Special, row.Magnitude);

        var allClass = row.Class == DamageClass.None && t >= 7 ? 0.03f : 0f;
        var tags = row.SetId switch
        {
            "coral-diver" => Tags("gills"),
            "scrap-salvager" => Tags("mining:10"),
            "coral-tidecaller" => Tags("water-walk"),
            _ => Tags()
        };
        ItemDefinition Piece(ItemId id, string name, EquipmentSlot slot, int defense, int value) =>
            new(id, name, ItemUseKind.Equipment, 1)
            {
                EquipmentSlot = slot, Defense = defense, ArmorSetId = row.SetId, SetBonusDefense = setDefense,
                Tier = t, Rarity = ContentItems.RarityFor(t), Value = value, Class = row.Class,
                MeleeDamageBonus = row.Class == DamageClass.Melee ? 0.03f : allClass,
                RangedDamageBonus = row.Class == DamageClass.Ranged ? 0.03f : allClass,
                TechDamageBonus = row.Class == DamageClass.Tech ? 0.03f : allClass,
                CommandDamageBonus = row.Class == DamageClass.Command ? 0.03f : allClass,
                MaximumManaBonus = row.Class == DamageClass.Tech ? 10f : 0f,
                CritBonus = row.Class == DamageClass.Ranged ? 1 : 0,
                ImmunityTags = tags
            };
        var baseValue = 30 + 25 * t * t;
        add(Piece(row.Head, row.HeadName, global::Rimtera.Systems.EquipmentSlot.Head, headDefense, baseValue));
        add(Piece(row.Body, row.BodyName, global::Rimtera.Systems.EquipmentSlot.Body, bodyDefense, baseValue * 3 / 2));
        add(Piece(row.Legs, row.LegsName, global::Rimtera.Systems.EquipmentSlot.Legs, legsDefense, baseValue * 5 / 4));
        foreach (var id in new[] { row.Head, row.Body, row.Legs })
            source(id, row.Source, null);
    }

    public static string SpecialText(SetSpecial special, float magnitude) => special switch
    {
        SetSpecial.Dodge => $"{magnitude * 100:0}% chance to dodge hits",
        SetSpecial.Thorns => $"attackers take {magnitude * 100:0}% of their damage back",
        SetSpecial.HealOnHit => $"heal {magnitude * 100:0}% of damage dealt",
        SetSpecial.ChargeOnHit => $"+{magnitude:0.#} charge per hit",
        SetSpecial.BurstWhenHit => "a shock burst answers every hit taken",
        SetSpecial.SpeedWhenLow => $"+{magnitude * 100:0}% speed below 40% health",
        SetSpecial.Endurance => $"{magnitude * 100:0}% less damage from hits",
        SetSpecial.Frenzy => $"+{magnitude * 100:0}% damage above 90% health",
        _ => "no special effect"
    };

    private static ItemDefinition BuildAccessory(AccessoryRow row)
    {
        var item = new ItemDefinition(row.Id, row.Name, ItemUseKind.Equipment, 1)
        {
            EquipmentSlot = global::Rimtera.Systems.EquipmentSlot.Accessory, Tier = row.Tier, Rarity = ContentItems.RarityFor(row.Tier),
            Value = 40 + row.Tier * row.Tier * 25
        };
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in row.Effects.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = part.IndexOf(':');
            var key = part[..colon];
            var text = part[(colon + 1)..];
            if (key == "tags")
            {
                foreach (var tag in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    tags.Add(tag);
                continue;
            }
            var value = float.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            item = key switch
            {
                "melee" => item with { MeleeDamageBonus = value / 100f },
                "ranged" => item with { RangedDamageBonus = value / 100f },
                "tech" => item with { TechDamageBonus = value / 100f },
                "command" => item with { CommandDamageBonus = value / 100f },
                "crit" => item with { CritBonus = (int)value },
                "def" => item with { Defense = (int)value },
                "move" => item with { MoveSpeedMultiplier = 1f + value / 100f },
                "life" => item with { MaximumLifeBonus = value },
                "charge" => item with { MaximumManaBonus = value },
                "chargeregen" => item with { ChargeRegenBonus = value },
                "regen" => item with { LifeRegenerationPerHour = value },
                "drone" => item with { DroneSlotBonus = (int)value },
                "sentry" => item with { SentrySlotBonus = (int)value },
                "fish" => item with { FishingPower = (int)value },
                _ => throw new InvalidOperationException($"Unknown accessory effect '{key}' on {row.Id}.")
            };
        }
        return item with { ImmunityTags = tags };
    }
}
