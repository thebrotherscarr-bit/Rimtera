using System.Globalization;
using Rimtera.Entities;

namespace Rimtera.Systems;

/// <summary>How a tooltip line is coloured (the renderer picks the brush).</summary>
public enum TooltipTone : byte
{
    /// <summary>The item's name, in its rarity colour (<see cref="ItemTooltip.RarityColor"/>).</summary>
    Name,
    Normal,
    /// <summary>A comparison or calibration change for the better.</summary>
    Better,
    /// <summary>A comparison or calibration change for the worse.</summary>
    Worse,
    /// <summary>Same as what is worn or held, or a set bonus not yet active.</summary>
    Dim,
    /// <summary>The sell value at an open shop.</summary>
    Price
}

/// <summary>One tooltip line.</summary>
public readonly record struct TooltipLine(string Text, TooltipTone Tone = TooltipTone.Normal);

/// <summary>
/// What a tooltip is shown against: the running game (worn gear, held item, class bonuses, charge cost multipliers) and
/// the shop open right now (for the sell value). Both optional; without a game the item's own numbers are shown.
/// </summary>
public sealed record TooltipContext(GameSimulation? Game = null, TownNpc? Shop = null);

/// <summary>
/// IC-17: the truthful item tooltip. Built from the item's data and the game it is shown in, so it cannot drift from the
/// rules: class-correct damage ("42 tech damage", "15 drone damage"), the ammunition the weapon really fires, crit chance,
/// speed words from use ticks, knockback words, charge cost, the calibration and what it changes, defense, the armor set
/// and its bonus, accessory effects in plain words, consumable effects, the comparison with what is worn or held, and
/// the sell value at an open shop. The renderer (GraphicalSandbox) only colours and draws the lines.
/// </summary>
public static class ItemTooltip
{
    private static readonly Lazy<ItemCatalog> SharedCatalog = new(() => new ItemCatalog());
    private static readonly Lazy<Dictionary<WorldEventKind, string>> EventNames =
        new(() => new WorldEventSystem(1).Specs.ToDictionary(spec => spec.Kind, spec => spec.Name));

    public static IReadOnlyList<TooltipLine> Build(ItemStack stack, TooltipContext? context = null)
    {
        context ??= new TooltipContext();
        var game = context.Game;
        var items = game?.Items ?? SharedCatalog.Value;
        var lines = new List<TooltipLine>();
        if (stack.IsEmpty || !items.Contains(stack.ItemId))
            return lines;
        var item = items[stack.ItemId];
        var calibration = CalibrationCatalog.Get(stack.Calibration);
        var name = CalibrationCatalog.Describe(item, stack.Calibration);
        lines.Add(new TooltipLine(stack.Count > 1 ? $"{name} ({stack.Count})" : name, TooltipTone.Name));

        AddWeaponLines(lines, item, stack.Calibration, calibration, game, items);
        AddToolLines(lines, item);
        AddArmorLines(lines, item, calibration, game);
        AddBonusLines(lines, item);
        AddUseLines(lines, item, items);
        AddCalibrationLines(lines, item, stack.Calibration, calibration);

        if (game is not null && Comparison(game, item) is { } comparison)
            lines.Add(comparison);
        if (game is not null && context.Shop is { } shop)
        {
            var unit = game.GetSellPrice(shop, stack);
            lines.Add(unit > 0
                ? new TooltipLine(SellLine(unit * Math.Max(1, stack.Count)), TooltipTone.Price)
                : new TooltipLine("Cannot be sold", TooltipTone.Dim));
        }
        return lines;
    }

    /// <summary>"Sells for 12 credits" (one credit reads singular).</summary>
    public static string SellLine(int credits) => $"Sells for {credits} credit{(credits == 1 ? "" : "s")}";

    // ------------------------------------------------------------------ weapons

    /// <summary>"melee", "ranged", "tech"; Command damage reads "drone" (or "sentry" for turrets), as in "15 drone damage".</summary>
    public static string ClassWord(ItemDefinition item) => item.EffectiveClass switch
    {
        DamageClass.Melee => "melee",
        DamageClass.Ranged => "ranged",
        DamageClass.Tech => "tech",
        DamageClass.Command => item.Summons is { } minion && MinionCatalog.Get(minion).IsSentry ? "sentry" : "drone",
        _ => ""
    };

    /// <summary>True for anything the tooltip treats as a weapon (it deals damage in a class).</summary>
    public static bool IsWeapon(ItemDefinition item) => item.Damage > 0 && item.EffectiveClass != DamageClass.None;

    private static void AddWeaponLines(List<TooltipLine> lines, ItemDefinition item, Calibration calibrationId, CalibrationDefinition calibration,
        GameSimulation? game, ItemCatalog items)
    {
        if (!IsWeapon(item))
            return;
        var damage = game is not null
            ? (int)game.ComputeWeaponDamage(item, calibrationId)
            : (int)CombatTuning.WeaponDamage(item.Damage, calibration.DamageMultiplier, 0, 1f);
        lines.Add(new TooltipLine($"{Math.Max(1, damage)} {ClassWord(item)} damage"));
        if (item.EffectiveClass != DamageClass.Command)
        {
            var crit = game is not null ? game.ProbeCritChance(item, calibrationId)
                : CombatTuning.WeaponCritChance(item.EffectiveClass, item.CritChance, calibration.CritBonus);
            lines.Add(new TooltipLine($"{crit}% critical strike chance"));
        }
        var ticks = CombatTuning.CalibratedUseTicks(item.UseCooldownTicks, calibration.CooldownMultiplier);
        if (ticks > 0)
            lines.Add(new TooltipLine(SpeedWord(ticks)));
        lines.Add(new TooltipLine(KnockbackWord(item.Knockback * calibration.KnockbackMultiplier)));
        if (AmmoLine(item, items) is { } ammo)
            lines.Add(new TooltipLine(ammo));
        if (item.ChargeCost > 0)
        {
            var cost = game?.ChargeCostFor(item, calibrationId) ?? CombatTuning.ChargeCost(item.ChargeCost, calibration.ChargeCostMultiplier, 1f);
            lines.Add(new TooltipLine($"Uses {cost} charge"));
        }
        if (item.Summons is { } kind)
        {
            var spec = MinionCatalog.Get(kind);
            lines.Add(new TooltipLine(spec.IsSentry ? $"Deploys a {spec.DisplayName} (uses a sentry slot)" : $"Launches a {spec.DisplayName} (uses a drone slot)"));
        }
        if (item.SignalWhip)
            lines.Add(new TooltipLine("Struck enemies are marked: your drones focus them and hit harder"));
    }

    /// <summary>
    /// The ammunition a ranged weapon really fires: its ammo family (arrows draw Energy cells only when their old ammo was
    /// cells), its dedicated ammunition, or itself when thrown. Null for weapons that use none.
    /// </summary>
    public static string? AmmoLine(ItemDefinition item, ItemCatalog items)
    {
        if (item.UseKind != ItemUseKind.Ranged && item.UsesAmmo == AmmoClass.None && item.AmmoType is null)
            return null;
        if (item.Style == WeaponStyle.Thrown && item.UsesAmmo == AmmoClass.None && item.AmmoType is null)
            return "Consumed when thrown";
        var family = item.UsesAmmo != AmmoClass.None ? AmmoFamilyName(item.UsesAmmo) : null;
        var dedicated = item.AmmoType is { } ammo && items.Contains(ammo) ? items[ammo] : null;
        // The weapon falls back to its dedicated ammunition when it has none of its family (WeaponStyles.FindAmmo).
        var fallback = dedicated is not null && dedicated.AmmoKind != item.UsesAmmo
            ? dedicated.Id == ItemId.Arrow ? "Energy cells" : dedicated.Name
            : null;
        return (family, fallback) switch
        {
            ({ } f, { } b) => $"Uses {f} (or {b})",
            ({ } f, null) => $"Uses {f}",
            (null, { } b) => $"Uses {b}",
            _ => dedicated is not null ? $"Uses {dedicated.Name}" : null
        };
    }

    /// <summary>A plural name for an ammunition family.</summary>
    public static string AmmoFamilyName(AmmoClass family) => family switch
    {
        AmmoClass.Arrow => "arrows and bolts",
        AmmoClass.Bullet => "rounds",
        AmmoClass.Rocket => "rockets",
        AmmoClass.Dart => "darts",
        AmmoClass.Fuel => "fuel",
        AmmoClass.Cell => "Energy cells",
        _ => "ammunition"
    };

    /// <summary>Terraria's speed words for a use time in ticks.</summary>
    public static string SpeedWord(int ticks) => ticks switch
    {
        <= 8 => "Insanely fast speed",
        <= 20 => "Very fast speed",
        <= 25 => "Fast speed",
        <= 30 => "Average speed",
        <= 35 => "Slow speed",
        <= 45 => "Very slow speed",
        <= 55 => "Extremely slow speed",
        _ => "Snail speed"
    };

    /// <summary>Terraria's knockback words.</summary>
    public static string KnockbackWord(float knockback) => knockback switch
    {
        <= 0f => "No knockback",
        <= 1.5f => "Extremely weak knockback",
        <= 3f => "Very weak knockback",
        <= 4f => "Weak knockback",
        <= 6f => "Average knockback",
        <= 7f => "Strong knockback",
        <= 9f => "Very strong knockback",
        <= 11f => "Extremely strong knockback",
        _ => "Insane knockback"
    };

    // ------------------------------------------------------------------ tools, armor, bonuses

    private static void AddToolLines(List<TooltipLine> lines, ItemDefinition item)
    {
        var word = item.UseKind switch
        {
            ItemUseKind.Pickaxe => "drill",
            ItemUseKind.Cutter => "cutter",
            ItemUseKind.Demolisher => "demolisher",
            _ => null
        };
        if (word is not null && item.ToolPower > 0)
            lines.Add(new TooltipLine($"{item.ToolPower}% {word} power"));
        if (item.FishingPower > 0)
            lines.Add(new TooltipLine($"{item.FishingPower}% fishing power"));
    }

    private static void AddArmorLines(List<TooltipLine> lines, ItemDefinition item, CalibrationDefinition calibration, GameSimulation? game)
    {
        if (item.EquipmentSlot is { } slot && item.UseKind is ItemUseKind.Equipment or ItemUseKind.Vanity)
        {
            if (item.IsVanity || item.UseKind == ItemUseKind.Vanity)
                lines.Add(new TooltipLine("Vanity item", TooltipTone.Dim));
            else if (slot >= EquipmentSlot.Accessory)
                lines.Add(new TooltipLine("Accessory", TooltipTone.Dim));
        }
        var defense = item.Defense + (item.UseKind == ItemUseKind.Equipment ? calibration.AccessoryDefense : 0);
        if (defense > 0 && item.UseKind == ItemUseKind.Equipment)
            lines.Add(new TooltipLine($"{defense} defense"));
        if (item.ArmorSetId is null)
            return;
        var active = game is not null && game.CombatStats.ActiveSet?.SetId is { } worn &&
                     string.Equals(worn, item.ArmorSetId, StringComparison.OrdinalIgnoreCase);
        if (ArmorSets.Get(item.ArmorSetId) is { } set)
        {
            lines.Add(new TooltipLine($"Set: {set.DisplayName}", active ? TooltipTone.Better : TooltipTone.Dim));
            lines.Add(new TooltipLine($"Set bonus: {set.Description}", active ? TooltipTone.Better : TooltipTone.Dim));
        }
        else if (item.SetBonusDefense > 0)
            lines.Add(new TooltipLine($"Set bonus: +{item.SetBonusDefense} defense", active ? TooltipTone.Better : TooltipTone.Dim));
    }

    private static void AddBonusLines(List<TooltipLine> lines, ItemDefinition item)
    {
        void Percent(float fraction, string what)
        {
            if (MathF.Abs(fraction) >= 0.005f)
                lines.Add(new TooltipLine($"{fraction * 100f:+0;-0}% {what}"));
        }
        if (item.MaximumLifeBonus != 0f)
            lines.Add(new TooltipLine($"{item.MaximumLifeBonus:+0;-0} maximum health"));
        if (item.MaximumManaBonus != 0f)
            lines.Add(new TooltipLine($"{item.MaximumManaBonus:+0;-0} maximum charge"));
        if (item.LifeRegenerationPerHour > 0f)
            lines.Add(new TooltipLine("Faster health regeneration"));
        if (item.ManaRegenerationPerHour > 0f || item.ChargeRegenBonus > 0f)
            lines.Add(new TooltipLine("Faster charge regeneration"));
        Percent(item.MoveSpeedMultiplier - 1f, "movement speed");
        if (item.UseKind is ItemUseKind.Equipment)
        {
            Percent(item.DamageMultiplier - 1f, "damage");
            Percent(item.KnockbackMultiplier - 1f, "knockback");
            Percent(item.MeleeDamageBonus, "melee damage");
            Percent(item.RangedDamageBonus, "ranged damage");
            Percent(item.TechDamageBonus, "tech damage");
            Percent(item.CommandDamageBonus, "drone damage");
            if (item.CritBonus != 0)
                lines.Add(new TooltipLine($"{item.CritBonus:+0;-0}% critical strike chance"));
        }
        if (item.DroneSlotBonus != 0)
            lines.Add(new TooltipLine($"{item.DroneSlotBonus:+0;-0} drone slot{(Math.Abs(item.DroneSlotBonus) == 1 ? "" : "s")}"));
        if (item.SentrySlotBonus != 0)
            lines.Add(new TooltipLine($"{item.SentrySlotBonus:+0;-0} sentry slot{(Math.Abs(item.SentrySlotBonus) == 1 ? "" : "s")}"));
        if (item.WingFlightSeconds > 0f)
            lines.Add(new TooltipLine($"{item.WingFlightSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s of flight"));
        // Consumables carry their effect as a status; their tags describe the status, not the item.
        if (item.UseKind is ItemUseKind.Equipment or ItemUseKind.Vanity)
            foreach (var tag in item.ImmunityTags.OrderBy(tag => tag, StringComparer.Ordinal))
                if (TagWords(tag) is { } words)
                    lines.Add(new TooltipLine(words));
    }

    /// <summary>An accessory or armor tag in plain words ("dodge:4" → "4% chance to dodge an attack"); null for tags with no player-facing effect.</summary>
    public static string? TagWords(string tag)
    {
        var colon = tag.IndexOf(':');
        var key = colon < 0 ? tag : tag[..colon];
        var number = colon < 0 ? 0f
            : float.TryParse(tag.AsSpan(colon + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0f;
        var n = number.ToString("0.##", CultureInfo.InvariantCulture);
        return key.ToLowerInvariant() switch
        {
            "cold" => "Protects against freezing cold",
            "heat" => "Protects against scorching heat",
            "poison" => "Immune to poison",
            "venom" => "Immune to venom",
            "bleeding" => "Immune to bleeding",
            "weak" => "Immune to weakness",
            "broken-armor" => "Immune to broken armor",
            "slow" => "Immune to slowing",
            "confused" => "Immune to confusion",
            "darkness" => "Immune to darkness",
            "cursed" => "Immune to cursed fire",
            "silenced" => "Immune to silence",
            "frostburn" => "Immune to frostburn",
            "potion-sickness" => $"Healing items cause {(1f - number) * 100f:0}% less sickness",
            "toxic" => "Immune to toxic fallout",
            "acid" => "Protects against acid",
            "lava" => "Wade through molten pools unharmed for a while",
            "fire-block" => "Immune to burning",
            "breath" => "Hold your breath far longer",
            "gills" => "Breathe underwater",
            "swim" => "Swim freely",
            "water-walk" => "Walk on water",
            "featherfall" => "Fall slowly",
            "glide" => "Glide by holding jump",
            "glide-rig" => "Hold jump to glide after flight runs out",
            "wings" => "Flight",
            "wing-climb" => "Wings climb faster",
            "flight" => "Rocket-powered flight",
            "rocket-boots" => "Rocket boots: hold jump to fly briefly",
            "double-jump" => "Jump again in mid-air",
            "climb" => "Cling to and jump off walls",
            "grapple" => "Fires a grappling hook",
            "magnet" => "Larger item pickup range",
            "dark" => "See in the dark",
            "night-vision" => "Improved night vision",
            "ore-detector" => "Shows nearby ores",
            "danger-sense" => "Shows nearby dangers",
            "knockback-immune" => "Immune to knockback",
            "auto-swing" => "Melee weapons swing automatically",
            "life-readout" => "Shows your health regeneration rate",
            "ore-readout" => "Shows the most valuable ore nearby",
            "depth-readout" => "Shows depth",
            "position-readout" => "Shows horizontal position",
            "catch-scanner" => "Shows what is biting",
            "fishing-line" => "Fishing line never breaks",
            "fishing-lure" => "Faster bites",
            "acid-fishing" => "Fish in acid",
            "lava-fishing" => "Fish in molten pools",
            "light" => $"+{n} light radius",
            "reach" => $"+{n} tile reach",
            "mining" => $"{n}% faster mining",
            "attackspeed" => $"{n}% faster melee swings",
            "dodge" => $"{n}% chance to dodge an attack",
            "endurance" => $"{n}% less damage taken",
            "thorns" => $"Attackers take {n}% of the damage they deal",
            "lifesteal" => $"Heals {n}% of melee damage dealt",
            "chargehit" => $"Restores {n} charge on hit",
            "ammosave" => $"{n}% chance not to use ammunition",
            "dmg" => $"+{n}% damage",
            "crit" => $"+{n}% critical strike chance",
            "speed" => $"+{n}% movement speed",
            "fish" => $"+{n} fishing power",
            "drone" => $"+{n} drone slots",
            "sentry" => $"+{n} sentry slots",
            "ap" => $"{n} armor penetration",
            "ap-melee" => $"{n} melee armor penetration",
            "chargecost" => $"{(number <= 0f ? "" : "+")}{n}% charge cost",
            "build" => $"{n}% faster building",
            _ => null
        };
    }

    // ------------------------------------------------------------------ consumables and everything else

    private static void AddUseLines(List<TooltipLine> lines, ItemDefinition item, ItemCatalog items)
    {
        if (item.HealAmount > 0f)
            lines.Add(new TooltipLine($"Restores {item.HealAmount:0} health"));
        if (item.ChargeRestore > 0f)
            lines.Add(new TooltipLine($"Restores {item.ChargeRestore:0} charge"));
        if (item.FoodRestore > 0f)
            lines.Add(new TooltipLine($"Restores {item.FoodRestore:P0} food"));
        if (item.GrantsStatus is { } status && item.StatusDurationHours > 0f)
        {
            var seconds = item.StatusDurationHours / GameSimulation.VitalHoursPerRealSecond;
            lines.Add(new TooltipLine($"{Spaced(status.ToString())} ({Duration(seconds)})"));
        }
        if (item.SummonsBoss is { } boss && EnemyArchetypes.Contains(boss))
            lines.Add(new TooltipLine($"Summons the {EnemyArchetypes.Get(boss).DisplayName}"));
        if (item.StartsEvent is { } worldEvent)
            lines.Add(new TooltipLine($"Starts the {EventNames.Value.GetValueOrDefault(worldEvent, Spaced(worldEvent.ToString()))}"));
        if (item.IsCrate)
            lines.Add(new TooltipLine("Right click to open"));
        if (item.ReleasesCritter is not null)
            lines.Add(new TooltipLine("Can be released"));
        if (item.BaitPower > 0)
            lines.Add(new TooltipLine($"{item.BaitPower}% bait power"));
        if (item.UseKind == ItemUseKind.Ammunition)
        {
            var family = item.AmmoKind != AmmoClass.None ? AmmoFamilyName(item.AmmoKind) : null;
            lines.Add(new TooltipLine(family is null ? "Ammunition" : $"Ammunition ({family})"));
            if (item.AmmoDamage > 0)
                lines.Add(new TooltipLine($"+{item.AmmoDamage} damage"));
        }
        if (item.UseKind is ItemUseKind.PlaceTile or ItemUseKind.PlaceWall)
            lines.Add(new TooltipLine("Can be placed"));
        if (item.UseKind is ItemUseKind.Consumable or ItemUseKind.Food && item.MaxStack > 1)
            lines.Add(new TooltipLine("Consumable", TooltipTone.Dim));
        if (item.IsMaterial)
            lines.Add(new TooltipLine("Material", TooltipTone.Dim));
        if (item.IsQuestFish)
            lines.Add(new TooltipLine("Quest fish", TooltipTone.Dim));
    }

    /// <summary>The calibration's name and each thing it changes, green when better and red when worse.</summary>
    private static void AddCalibrationLines(List<TooltipLine> lines, ItemDefinition item, Calibration id, CalibrationDefinition calibration)
    {
        if (id == Calibration.None)
            return;
        void Delta(float change, string what, bool lowerIsBetter = false)
        {
            if (MathF.Abs(change) < 0.005f)
                return;
            var better = lowerIsBetter ? change < 0f : change > 0f;
            lines.Add(new TooltipLine($"{change * 100f:+0;-0}% {what}", better ? TooltipTone.Better : TooltipTone.Worse));
        }
        var value = calibration.ValueMultiplier;
        lines.Add(new TooltipLine($"Calibration: {calibration.Name}", value >= 1f ? TooltipTone.Better : TooltipTone.Worse));
        Delta(calibration.DamageMultiplier - 1f, "damage");
        if (calibration.CritBonus != 0)
            lines.Add(new TooltipLine($"{calibration.CritBonus:+0;-0}% critical strike chance", calibration.CritBonus > 0 ? TooltipTone.Better : TooltipTone.Worse));
        // Speed reads as Terraria's: a 10% shorter use time is "+10% speed".
        Delta(1f - calibration.CooldownMultiplier, "speed");
        Delta(calibration.KnockbackMultiplier - 1f, "knockback");
        if (item.ChargeCost > 0)
            Delta(calibration.ChargeCostMultiplier - 1f, "charge cost", lowerIsBetter: true);
        Delta(calibration.VelocityMultiplier - 1f, "projectile speed");
        Delta(calibration.SizeMultiplier - 1f, "size");
        if (calibration.AccessoryDefense != 0)
            lines.Add(new TooltipLine($"{calibration.AccessoryDefense:+0;-0} defense", calibration.AccessoryDefense > 0 ? TooltipTone.Better : TooltipTone.Worse));
        Delta(calibration.AccessoryMoveSpeed, "movement speed");
    }

    /// <summary>
    /// Terraria's comparison line: armor against the piece worn in its slot (defense), weapons and drills against the
    /// item in hand (damage, drill power). Null when there is nothing to compare.
    /// </summary>
    public static TooltipLine? Comparison(GameSimulation game, ItemDefinition item)
    {
        TooltipLine Line(int difference, string what, string against) => difference == 0
            ? new TooltipLine($"Same {what} as {against}", TooltipTone.Dim)
            : new TooltipLine($"{difference:+0;-0} {what} compared with {against}", difference > 0 ? TooltipTone.Better : TooltipTone.Worse);

        if (item.UseKind == ItemUseKind.Equipment && item.EquipmentSlot is EquipmentSlot.Head or EquipmentSlot.Body or EquipmentSlot.Legs)
        {
            var worn = game.Player.Equipment[item.EquipmentSlot.Value];
            if (worn is null)
                return new TooltipLine($"+{item.Defense} defense (nothing worn there)", item.Defense > 0 ? TooltipTone.Better : TooltipTone.Dim);
            if (worn == item.Id)
                return null;
            var wornItem = game.Items[worn.Value];
            return Line(item.Defense - wornItem.Defense, "defense", wornItem.Name);
        }
        if (game.Player.Inventory.SelectedItem is not { } held || held.Id == item.Id)
            return null;
        if (item.UseKind is ItemUseKind.Melee or ItemUseKind.Ranged or ItemUseKind.Tech &&
            held.UseKind is ItemUseKind.Melee or ItemUseKind.Ranged or ItemUseKind.Tech)
            return Line((int)MathF.Round(item.Damage - held.Damage), "damage", held.Name);
        if (item.UseKind == ItemUseKind.Pickaxe && held.UseKind == ItemUseKind.Pickaxe)
            return Line(item.ToolPower - held.ToolPower, "% drill power", held.Name);
        return null;
    }

    /// <summary>The name colour for a rarity (Terraria's ladder: white, blue, green, orange, light red, pink, purple).</summary>
    public static (byte R, byte G, byte B) RarityColor(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Uncommon => (150, 150, 255),
        ItemRarity.Rare => (150, 255, 150),
        ItemRarity.Epic => (255, 200, 150),
        ItemRarity.Legendary => (255, 150, 150),
        ItemRarity.Mythic => (255, 150, 255),
        ItemRarity.Astral => (210, 160, 255),
        _ => (255, 255, 255)
    };

    private static string Spaced(string identifier) =>
        System.Text.RegularExpressions.Regex.Replace(identifier, "(?<=[a-z0-9])([A-Z])", " $1");

    private static string Duration(float seconds)
    {
        var whole = Math.Max(1, (int)MathF.Round(seconds)); // 38.4 h / 0.08 is 479.99… in floats: show 8 min, not 7 min 59 s
        return whole >= 60 ? $"{whole / 60} min{(whole % 60 > 0 ? $" {whole % 60} s" : "")}" : $"{whole} s";
    }
}
