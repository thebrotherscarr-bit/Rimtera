namespace Rimtera.Systems;

/// <summary>
/// The per-tier gear curve every generated tool, weapon and armour piece reads (docs/GEAR_DIFF_ITEMS.md).
/// Rimtera's seven tiers stand in for Terraria's progression (numbers from Terraria.exe 1.4.5.8, used as evidence only):
/// 1 Ferrite = copper/tin, 2 Cobalt = iron/lead, 3 the biome metals = silver..platinum, meteor, demonite and (pyrocite)
/// hellstone, 4 Viridium = cobalt/palladium (first post-Fracture ore), 5 Lumenite = mythril/orichalcum,
/// 6 Voidsteel = adamantite/titanium and hallowed, 7 Astral = luminite (after the final boss).
/// Names and items stay Rimtera's own; only the numbers and the shape of the progression follow Terraria.
/// </summary>
public static class GearCurve
{
    // ------------------------------------------------------------------ tools

    /// <summary>
    /// Rimtera drill power for a Terraria pickaxe power. Pre-Fracture powers are 1:1 (meteorite needs 50 in both games);
    /// hardmode is compressed so the ore gates line up: Terraria 110 (mythril gate) = Rimtera 85 (lumenite gate),
    /// Terraria 150 (adamantite gate) = Rimtera 100 (voidsteel gate), Terraria 225 (luminite) = Rimtera 128.
    /// </summary>
    public static int DrillPowerFromTerraria(int terrariaPick) => terrariaPick switch
    {
        <= 55 => terrariaPick,
        <= 110 => (int)MathF.Round(55f + (terrariaPick - 55) * 30f / 55f),
        <= 150 => (int)MathF.Round(85f + (terrariaPick - 110) * 15f / 40f),
        _ => (int)MathF.Round(100f + (terrariaPick - 150) * 28f / 75f)
    };

    /// <summary>Standard drill power per tier (index = tier): copper 35, iron/lead 41, gold 55, cobalt/palladium 88, mythril/orichalcum 103, adamantite/titanium 114, luminite 128.</summary>
    private static readonly int[] DrillPowerByTier = [0, 35, 41, 55, 88, 103, 114, 128];

    /// <summary>Drill power for a tier and material. Tier 3 spreads like Terraria's band: glacial/uranium = gold (55), meteorite = the evil picks (60), pyrocite = molten (80).</summary>
    public static int DrillPower(int tier, string? material = null)
    {
        var t = Math.Clamp(tier, 1, 7);
        if (t == 3)
            return material switch
            {
                "Pyrocite" => DrillPowerFromTerraria(100),
                "Meteorite" => DrillPowerFromTerraria(65),
                _ => DrillPowerByTier[3]
            };
        return DrillPowerByTier[t];
    }

    /// <summary>
    /// Ticks between drill hits = Terraria's pickaxe useTime (both games run 60 ticks a second and a block takes 100
    /// damage, CombatTuning.DrillDamage): copper 15, iron 13, gold/platinum/evil 15, molten 18, cobalt 13, mythril 10,
    /// adamantite 8, luminite 6.
    /// </summary>
    public static int DrillCooldown(int tier, string? material = null) =>
        ToolTicks(tier == 3 && material == "Pyrocite" ? 18 : PickUseTime[Math.Clamp(tier, 1, 7)]);

    // Terraria useTime per tier: pickaxes, axes and hammers.
    private static readonly int[] PickUseTime = [0, 15, 13, 15, 13, 10, 8, 6];
    private static readonly int[] AxeUseTime = [0, 21, 19, 18, 13, 10, 8, 7];
    private static readonly int[] HammerUseTime = [0, 23, 20, 20, 14, 14, 12, 7];
    /// <summary>Rimtera ticks per tool use = Terraria useTime × this. 1: the tool formulas are Terraria's (100-damage blocks, axe × 1.2 per chop, hammer × 1.5 per wall hit).</summary>
    public const float ToolUseTimeScale = 1f;

    private static int ToolTicks(int terrariaUseTime) => Math.Max(1, (int)MathF.Round(terrariaUseTime * ToolUseTimeScale));

    /// <summary>Cutter power = Terraria axe power in percent (axe field × 5, so CombatTuning.CutterDamage reads power / 5 back as the axe value): copper 35, iron 45, gold 55, cobalt 70, mythril 85, titanium 105, lunar axes 135.</summary>
    private static readonly int[] CutterPowerByTier = [0, 35, 45, 55, 70, 85, 105, 135];
    public static int CutterPower(int tier) => CutterPowerByTier[Math.Clamp(tier, 1, 7)];
    public static int CutterCooldown(int tier) => ToolTicks(AxeUseTime[Math.Clamp(tier, 1, 7)]);

    /// <summary>Demolisher power = Terraria hammer power: copper 35, iron/lead 42, gold 55, pwnhammer 80, 85, chlorophyte 90, lunar 100.</summary>
    private static readonly int[] HammerPowerByTier = [0, 35, 42, 55, 80, 85, 90, 100];
    public static int DemolisherPower(int tier) => HammerPowerByTier[Math.Clamp(tier, 1, 7)];
    public static int DemolisherCooldown(int tier) => ToolTicks(HammerUseTime[Math.Clamp(tier, 1, 7)]);

    /// <summary>Boss and biome tools sit 40% of the way to the next tier (Terraria's reaver shark, bone and fossil picks sit above their tier the same way).</summary>
    public static int UniqueToolPower(ItemUseKind kind, int tier, string? material = null)
    {
        int Power(int t) => kind switch
        {
            ItemUseKind.Cutter => CutterPower(t),
            ItemUseKind.Demolisher => DemolisherPower(t),
            _ => DrillPower(t, material)
        };
        var t = Math.Clamp(tier, 1, 7);
        var next = t < 7 ? Power(t + 1) : Power(7) + 10;
        return Power(t) + (int)MathF.Round(0.4f * (next - Power(t)));
    }

    /// <summary>Power of a generated tool row (0 in the row = take it from the curve).</summary>
    public static int ToolPower(ItemUseKind kind, int tier, string? material, bool unique) => unique
        ? UniqueToolPower(kind, tier, material)
        : kind switch
        {
            ItemUseKind.Cutter => CutterPower(tier),
            ItemUseKind.Demolisher => DemolisherPower(tier),
            _ => DrillPower(tier, material)
        };

    public static int ToolCooldown(ItemUseKind kind, int tier, string? material) => kind switch
    {
        ItemUseKind.Cutter => CutterCooldown(tier),
        ItemUseKind.Demolisher => DemolisherCooldown(tier),
        _ => DrillCooldown(tier, material)
    };

    // ------------------------------------------------------------------ weapons

    /// <summary>
    /// Rimtera damage per Terraria damage at tier 1 (copper broadsword 9 → ferrite longblade 12). Later tiers keep
    /// Terraria's hits-to-kill ratio against each tier's creatures: Rimtera's creature hit points grow differently
    /// (see the doc), so each tier's base is Terraria's tier damage × this × (Rimtera tier HP / Terraria tier HP),
    /// normalised to tier 1.
    /// </summary>
    public const float DamageScale = 4f / 3f;

    // Per-hit base damage per tier (index = tier) and class, before the style factor.
    /// <summary>Broadsword base: Terraria copper 9, iron 12.5, gold/demonite 18, cobalt/palladium 45, mythril/orichalcum 55, adamantite/hallowed 70, lunar 185, ratio-matched to Rimtera creature HP.</summary>
    public static readonly int[] MeleeBase = [0, 12, 23, 40, 57, 78, 105, 173];
    /// <summary>Bow base (before ammo): Terraria bows/repeaters run at 0.67-0.8 of the tier's broadsword.</summary>
    public static readonly int[] RangedBase = [0, 8, 16, 27, 46, 57, 69, 95];
    /// <summary>Staff base: Terraria gem staves hit 1.2-1.6× a sword early (slow casts) and ~0.8× in hardmode.</summary>
    public static readonly int[] TechBase = [0, 20, 27, 40, 50, 62, 82, 120];
    /// <summary>Drone base: Terraria minions sit at 0.6-0.8 of the tier's broadsword.</summary>
    public static readonly int[] CommandBase = [0, 10, 15, 29, 38, 45, 70, 86];
    /// <summary>Charge per staff cast = Terraria mana per cast: gem staves 7-9, hardmode 10-14, lunar 15.</summary>
    public static readonly int[] ChargeBase = [0, 7, 8, 9, 10, 12, 14, 15];

    /// <summary>The per-tier bases the hand-authored content-expansion weapons were written against (kept to rescale them onto the curve).</summary>
    private static readonly int[] LegacyMeleeBase = [0, 16, 25, 36, 50, 74, 105, 150];
    private static readonly int[] LegacyRangedBase = [0, 10, 16, 27, 36, 48, 68, 92];
    private static readonly int[] LegacyTechBase = [0, 12, 19, 28, 38, 50, 68, 100];
    private static readonly int[] LegacyCommandBase = [0, 8, 13, 20, 30, 40, 56, 85];

    public static int[] BaseFor(DamageClass damageClass) => damageClass switch
    {
        DamageClass.Ranged => RangedBase,
        DamageClass.Tech => TechBase,
        DamageClass.Command => CommandBase,
        _ => MeleeBase
    };

    /// <summary>Moves a hand-authored damage number from the old per-tier base onto the curve (keeps its place within the tier).</summary>
    public static int RescaleAuthoredDamage(DamageClass damageClass, int tier, int damage)
    {
        var t = Math.Clamp(tier, 1, 7);
        var (now, legacy) = damageClass switch
        {
            DamageClass.Ranged => (RangedBase, LegacyRangedBase),
            DamageClass.Tech => (TechBase, LegacyTechBase),
            DamageClass.Command => (CommandBase, LegacyCommandBase),
            _ => (MeleeBase, LegacyMeleeBase)
        };
        return Math.Max(1, (int)MathF.Round(damage * (float)now[t] / legacy[t]));
    }

    /// <summary>
    /// Moves a hand-authored use time onto the curve's style use times: the old content was written against a 16-tick
    /// sword, a 12-tick gun, a 16-tick staff, an 18-tick whip and a 30-tick drone remote; the curve uses Terraria's 20, 16,
    /// the tier's staff time, 30 and 36. Rapid-fire items (8 ticks or less: beams, flamers, chainsaws) keep their rate.
    /// </summary>
    public static int RescaleAuthoredUseTime(DamageClass damageClass, int tier, int ticks, bool whip = false)
    {
        if (ticks <= 8)
            return ticks;
        var t = Math.Clamp(tier, 1, 7);
        var factor = whip ? 30f / 18f : damageClass switch
        {
            DamageClass.Ranged => 16f / 12f,
            DamageClass.Tech => StaffUseTime[t] / 16f,
            DamageClass.Command => 36f / 30f,
            _ => 20f / 16f
        };
        return Math.Max(1, (int)MathF.Round(ticks * factor));
    }

    /// <summary>Per-hit damage of a style relative to its class base (Terraria's style ratios at the same tier).</summary>
    public static float StyleDamageFactor(WeaponStyle style, bool explosive = false) => style switch
    {
        WeaponStyle.Shortsword => 0.6f,    // copper shortsword 5 vs broadsword 9; gold 12 vs 15
        WeaponStyle.Spear => 0.88f,        // spear 8, trident 14, naginata/pike 44 vs swords 9-45
        WeaponStyle.Yoyo => 0.7f,          // Rimtera yo-yos re-hit every few ticks; kept below Terraria's 1.0 per hit
        WeaponStyle.Boomerang => 1f,       // wooden 10, enchanted 17, light disc 60
        WeaponStyle.Flail => 1.1f,         // ball o' hurt 15, sunfury 32, dao of pow 50
        WeaponStyle.Whip => 1.6f,          // whips hit ~1.6× the tier's minion (leather 14 vs finch 7, firecracker 37 vs spider 26)
        WeaponStyle.Gun => 0.85f,
        WeaponStyle.Shotgun => 0.55f,      // per pellet: boomstick 14, shotgun 24
        WeaponStyle.Launcher => 1.8f,      // rocket launcher 55 vs repeaters 42-53
        WeaponStyle.Thrown => explosive ? 3f : 1.4f, // grenade 60 vs bow 6; knives 12, shuriken 10
        WeaponStyle.Tome => 0.55f,         // three bolts a cast
        WeaponStyle.Beam => 0.3f,
        WeaponStyle.SentryStaff => 1.1f,
        _ => 1f
    };

    // Use times in ticks: Rimtera and Terraria both run 60 ticks a second, so weapon use times map 1:1.
    private static readonly int[] BowUseTime = [0, 28, 27, 25, 22, 19, 17, 14];   // copper bow 29 → repeaters 23-16 → phantasm 12
    private static readonly int[] StaffUseTime = [0, 36, 33, 27, 20, 18, 18, 14]; // amethyst 37, sapphire 34, ruby/diamond 26-28, hardmode 12-25

    /// <summary>Base use time (ticks) of a style at a tier, before the row's speed factor.</summary>
    public static int StyleUseTime(WeaponStyle style, int tier, bool explosive = false)
    {
        var t = Math.Clamp(tier, 1, 7);
        return style switch
        {
            WeaponStyle.Broadsword => 20,  // copper..platinum 20-17, hardmode 19-22
            WeaponStyle.Shortsword => 12,  // 13-10
            WeaponStyle.Spear => 28,       // 31 pre-hardmode, 22-28 hardmode
            WeaponStyle.Yoyo => 12,        // Rimtera re-hit interval (Terraria yo-yos are channelled)
            WeaponStyle.Boomerang => 20,   // 20, chakrams 15
            WeaponStyle.Flail => 24,       // Rimtera flails hit once per launch; Terraria's 45 spins hit many times
            WeaponStyle.Whip => 30,        // every whip 27-35
            WeaponStyle.Bow => BowUseTime[t],
            WeaponStyle.Gun => 16,         // flintlock 16, handgun 15, phoenix blaster 14
            WeaponStyle.Shotgun => 42,     // boomstick 40, shotgun 45
            WeaponStyle.Launcher => 30,    // rocket launcher 30
            WeaponStyle.Thrown => explosive ? 45 : 15, // grenade 45; knives and shuriken 15
            WeaponStyle.Staff => StaffUseTime[t],
            WeaponStyle.Tome => (int)MathF.Round(StaffUseTime[t] * 0.75f),
            WeaponStyle.Beam => 4,
            WeaponStyle.SummonStaff or WeaponStyle.SentryStaff => 36,
            _ => 20
        };
    }

    /// <summary>Rimtera knockback per Terraria knockBack (Rimtera's push model; the broadsword's 3.5 came from Terraria's ~6).</summary>
    public const float KnockbackScale = 0.6f;

    // ---- Tool damage: every pick, axe and hammer is also a melee weapon, as in Terraria ----
    // Terraria tool damage per tier (tools.csv: copper/iron/gold picks 4/5/6, axes 3/5/7, hammers 4/7/9; hardmode
    // cobalt/mythril/adamantite picks 10/15/20, war-axes 33/39/43, Pwnhammer 26 and hallowed-era hammers ~30-35; luminite
    // picks ~60, Solar Flare axe 100 and hammer 110), scaled per tier by Rimtera's melee base over Terraria's matching sword
    // (copper 8, iron 10, gold 15, cobalt 39, mythril 47, adamantite 50, luminite ~100) so tools keep Terraria's share of a
    // sword's hit.
    private static readonly int[] PickDamage = [0, 6, 12, 16, 15, 25, 42, 102];
    private static readonly int[] AxeDamage = [0, 5, 12, 19, 48, 65, 90, 170];
    private static readonly int[] HammerDamage = [0, 6, 16, 24, 38, 50, 74, 187];

    /// <summary>Melee damage of a tool of <paramref name="kind"/> at <paramref name="tier"/> (0 for non-tools).</summary>
    public static int ToolDamage(ItemUseKind kind, int tier)
    {
        var t = Math.Clamp(tier, 1, 7);
        return kind switch
        {
            ItemUseKind.Pickaxe => PickDamage[t],
            ItemUseKind.Cutter => AxeDamage[t],
            ItemUseKind.Demolisher => HammerDamage[t],
            _ => 0
        };
    }

    /// <summary>
    /// Tool knockback (Terraria × <see cref="KnockbackScale"/>): picks 2, drills 0.5, axes 4.5 (war-axes 6 from hardmode),
    /// hammers 5.5 (7.5 from hardmode).
    /// </summary>
    public static float ToolKnockback(ItemUseKind kind, int tier, bool channel) => KnockbackScale * kind switch
    {
        ItemUseKind.Pickaxe => channel ? 0.5f : 2f,
        ItemUseKind.Cutter => tier >= 4 ? 6f : 4.5f,
        ItemUseKind.Demolisher => tier >= 4 ? 7.5f : 5.5f,
        _ => 0f
    };

    /// <summary>Style knockback = Terraria style median × <see cref="KnockbackScale"/>.</summary>
    public static float StyleKnockback(WeaponStyle style, bool explosive = false) => KnockbackScale * style switch
    {
        WeaponStyle.Broadsword => 6f,
        WeaponStyle.Shortsword => 4f,
        WeaponStyle.Spear => 5.5f,
        WeaponStyle.Yoyo => 3.5f,
        WeaponStyle.Boomerang => 7f,
        WeaponStyle.Flail => 6f,
        WeaponStyle.Whip => 1.5f,
        WeaponStyle.Bow => 2f,
        WeaponStyle.Gun => 2f,
        WeaponStyle.Shotgun => 6f,
        WeaponStyle.Launcher => 4f,
        WeaponStyle.Thrown => explosive ? 8f : 2f,
        WeaponStyle.Staff or WeaponStyle.Tome => 5f,
        WeaponStyle.Beam => 0.5f,
        _ => 2f
    };

    // ------------------------------------------------------------------ armour

    /// <summary>
    /// Full-set defense (three pieces plus the set bonus) of the heaviest set per tier: Terraria's tier sets × 0.75 from
    /// tier 4 on (copper 6, iron 9-11, gold/platinum 16-20, molten 25, cobalt 32, mythril/orichalcum 37-42,
    /// adamantite/titanium/hallowed 50, solar 78). Tiers 1-3 follow Rimtera's softer early creatures (their hits are
    /// 0.5-0.67 of Terraria's), so the share of a hit that armour removes matches Terraria's (25-35%).
    /// </summary>
    public static readonly int[] HeavySetDefense = [0, 5, 7, 11, 24, 30, 38, 58];

    /// <summary>
    /// Class share of the heavy set. Hardmode follows Terraria's split (ranged sets 0.72, magic 0.65, summoner 0.55 of
    /// the melee set); before it, Terraria's class sets (jungle, necro, bee) sit near the ore armour, about 0.85.
    /// </summary>
    public static float ClassDefenseFactor(DamageClass damageClass, int tier) => damageClass switch
    {
        DamageClass.Melee => 1f,
        DamageClass.None => tier <= 3 ? 1f : 0.85f, // classless sets are the ore armour early, a generalist set later
        _ when tier <= 3 => 0.85f,
        DamageClass.Ranged => 0.72f,
        DamageClass.Tech => 0.65f,
        _ => 0.55f
    };

    /// <summary>Head, body and legs defense plus the set-bonus defense for a tier and class.</summary>
    public static (int Head, int Body, int Legs, int SetBonus) ArmorPieces(int tier, DamageClass damageClass)
    {
        var t = Math.Clamp(tier, 1, 7);
        var total = Math.Max(4, (int)MathF.Round(HeavySetDefense[t] * ClassDefenseFactor(damageClass, t)));
        // Terraria's ore sets add +2..+4 as the set bonus (about a quarter early); hardmode melee sets add little.
        var setBonus = damageClass is DamageClass.Melee or DamageClass.None
            ? Math.Max(1, (int)MathF.Round(total * (t <= 3 ? 0.25f : 0.08f)))
            : 1;
        var pieces = Math.Max(3, total - setBonus);
        var head = Math.Max(1, (int)MathF.Round(pieces * 0.3f));
        var body = Math.Max(head, (int)MathF.Round(pieces * 0.4f));
        var legs = Math.Max(1, pieces - head - body);
        return (head, body, legs, setBonus);
    }
}
