namespace Rimtera.Systems;

/// <summary>
/// Buff durations on Terraria's scale (0.3.2). Statuses run on vital hours (12.5 real seconds each), and the buff items were
/// authored at 3–10 vital hours: 40 seconds to two minutes, where Terraria's potions last two to fifteen minutes. Each buff
/// item now takes the duration of its Terraria counterpart, in real minutes; food keeps its spread, rescaled so a common
/// meal lasts Terraria's ten minutes of Well Fed. Run after registration, like <see cref="DefenseBalance.Apply"/>.
/// docs/GEAR_DIFF_ITEMS.md §8 lists every pairing.
/// </summary>
public static class BuffCurve
{
    /// <summary>Each buff item, its Terraria counterpart and that counterpart's duration in real minutes.</summary>
    public static readonly IReadOnlyList<(ItemId Item, string Terraria, float Minutes)> Potions =
    [
        (ItemId.SpeedStim, "Swiftness", 8), (ItemId.ArmorStim, "Ironskin", 8), (ItemId.ChargeStim, "Mana Regeneration", 7),
        (ItemId.RejuvenationGel, "Heartreach", 8), (ItemId.SprintStim, "Swiftness (lesser)", 4), (ItemId.MarathonStim, "Swiftness", 8),
        (ItemId.HunterLens, "Hunter", 5), (ItemId.GuardSerum, "Ironskin (lesser)", 5),
        (ItemId.FeatherfallPill, "Featherfall", 5), (ItemId.FeatherfallMist, "Featherfall (greater)", 8),
        (ItemId.WaterBreathCapsule, "Gills", 2), (ItemId.GillDraught, "Gills (greater)", 4),
        (ItemId.CloseQuartersTonic, "Titan", 4), (ItemId.BallisticsTonic, "Archery", 4), (ItemId.TechTonic, "Magic Power", 4),
        (ItemId.CommandTonic, "Summoning", 6),
        (ItemId.GreaterCloseQuartersTonic, "Titan (greater)", 8), (ItemId.GreaterBallisticsTonic, "Archery (greater)", 8),
        (ItemId.GreaterTechTonic, "Magic Power (greater)", 8), (ItemId.GreaterCommandTonic, "Summoning (greater)", 10),
        (ItemId.PrecisionDraught, "Wrath (crit)", 4), (ItemId.HuntersFocus, "Wrath (crit, greater)", 8),
        (ItemId.FortifyStim, "Endurance", 4), (ItemId.HeavyFortifyStim, "Endurance (greater)", 8),
        (ItemId.ThornskinSerum, "Thorns", 2), (ItemId.EnduranceStim, "Endurance", 4), (ItemId.AmmoReserveTonic, "Ammo Reservation", 4),
        (ItemId.WrathDraught, "Wrath", 4), (ItemId.DroneLinkStim, "Summoning", 6), (ItemId.SentryLinkStim, "Summoning", 6),
        (ItemId.OverchargeCell, "Magic Power", 4), (ItemId.FireproofSalve, "Obsidian Skin", 4), (ItemId.LifeforceSerum, "Lifeforce", 8),
        (ItemId.InertiaStim, "Gravitation", 3), (ItemId.EvasionSerum, "Invisibility", 2),
        (ItemId.RestorativeTonic, "Regeneration", 8), (ItemId.RegenPatch, "Regeneration (lesser)", 5), (ItemId.ManaTonic, "Mana Regeneration", 7),
        (ItemId.SpelunkerSpray, "Spelunker", 5), (ItemId.DeepScanVapor, "Spelunker (greater)", 8), (ItemId.NightOwlDrops, "Night Owl", 4),
        (ItemId.NightSightDrops, "Night Owl", 4), (ItemId.VitalitySerum, "Heartreach", 8), (ItemId.MinersDraught, "Mining", 8),
        (ItemId.SurfaceTensionSerum, "Water Walking", 5), (ItemId.DangerSenseDrops, "Dangersense", 5), (ItemId.GlowDraught, "Shine", 5),
        (ItemId.AnglersBrew, "Fishing", 8), (ItemId.MagnetismTonic, "Treasure Magnet", 5), (ItemId.BuildersBrew, "Builder", 15),
        (ItemId.FortuneTonic, "Luck", 10)
    ];

    /// <summary>Real minutes of Well Fed a food item gives per vital hour it was authored with (6 h → Terraria's 10 minutes).</summary>
    public const float FoodMinutesPerAuthoredHour = 10f / 6f;

    /// <summary>The longest any buff may last: Terraria's longest common potion buff (Builder, 15 minutes) or a feast, whichever is longer.</summary>
    public static readonly float CapHours = Hours(20);

    /// <summary>Vital hours for a duration in real minutes.</summary>
    public static float Hours(float minutes) => minutes * 60f * GameSimulation.VitalHoursPerRealSecond;

    public static void Apply(Dictionary<ItemId, ItemDefinition> items)
    {
        foreach (var (id, _, minutes) in Potions)
            if (items.TryGetValue(id, out var item) && item.GrantsStatus is not null)
                items[id] = item with { StatusDurationHours = Hours(minutes) };
        foreach (var (id, item) in items.ToArray())
            if (item.UseKind == ItemUseKind.Food && item.GrantsStatus is not null && item.StatusDurationHours > 0f)
                items[id] = item with { StatusDurationHours = Hours(item.StatusDurationHours * FoodMinutesPerAuthoredHour) };
    }
}
