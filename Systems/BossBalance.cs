using Rimtera.Entities;

namespace Rimtera.Systems;

/// <summary>
/// The <c>--boss-balance</c> report (0.2.0 A1, PC-15, BO-23): every boss and mini-boss against a reference player kitted
/// for the point in progression where it becomes fightable.
/// <para>The kit comes from the crafting audit (<see cref="CraftingAudit"/>): the stage at which the boss first becomes
/// reachable (its gate open and a summon in hand, or its event available), and everything obtainable by then — the best
/// weapon of each class (melee, ranged, tech), the best armour (a full set or the best three pieces), the five
/// highest-defense accessories, the best healing stim of the ladder and the life cores and lifebloom pods in reach.</para>
/// <para>Damage out: each weapon's damage per use against the boss's defense (half of it, at least 1), with the crit
/// chance's expected doubling, the projectile count of spread guns and casters, and its use time; ammunition is the best
/// reachable round of its class. The reference weapon is the median of the three class bests at their top tier (the
/// "tier weapon set").</para>
/// <para>Damage in: one landed hit per attack cycle (a volley lands at most once through the player's immunity frames;
/// contact shares the same cooldown), the cycle shortening in phase 2. Mini-bosses hit by contact every contact cooldown.
/// Each hit is the authored damage × the toughness scale (<see cref="DefenseBalance.EnemyHitScale"/>) × the difficulty,
/// under the boss ceiling, less the worn defense share.</para>
/// <para>Bands (BO-23): time to kill 60-180 s; survival without healing at least 15 s; and with half the shots dodged and
/// a stim on every potion-sickness cooldown, the player outlasts the fight (or heals faster than they bleed: ∞).</para>
/// </summary>
public static class BossBalance
{
    /// <summary>Time-to-kill band in seconds.</summary>
    public const float MinimumSeconds = 60f, MaximumSeconds = 180f;
    /// <summary>Survival without healing must reach this many seconds.</summary>
    public const float MinimumUnhealedSurvival = 15f;
    /// <summary>Share of hits the potion-survival check assumes the player avoids.</summary>
    public const float DodgedShare = 0.5f;

    /// <summary>One weapon's measured output against a given defense.</summary>
    public sealed record WeaponOutput(ItemDefinition Weapon, ItemDefinition? Ammo, float DamagePerUse, float UsesPerSecond)
    {
        public float Dps => DamagePerUse * UsesPerSecond;
        public string Label => Ammo is null ? Weapon.Name : $"{Weapon.Name} + {Ammo.Name}";
    }

    /// <summary>The reference player for one boss.</summary>
    public sealed record Kit(int Stage, string StageName, IReadOnlyList<ItemDefinition> Weapons, IReadOnlyList<ItemDefinition> Ammo,
        int ArmorDefense, string ArmorLabel, int AccessoryDefense, float Life, float PotionHeal, string PotionName)
    {
        public int Defense => ArmorDefense + AccessoryDefense;
    }

    /// <summary>One boss row of the report.</summary>
    public sealed record Row(EnemyArchetype Archetype, bool MiniBoss, int ProgressionIndex, WorldDifficulty Difficulty, Kit Kit,
        WeaponOutput Reference, float HitPoints, int Hits, float Seconds, float RawHit, float HitTaken, float HitsPerSecond,
        float SurvivalUnhealed, float SurvivalWithPotions)
    {
        public EnemyKind Kind => Archetype.Kind;
        public string Name => Archetype.DisplayName;
        public float IncomingDps => HitTaken * HitsPerSecond;
        public bool TimeInBand => Seconds >= MinimumSeconds && Seconds <= MaximumSeconds;
        public bool Survivable => SurvivalUnhealed >= MinimumUnhealedSurvival && SurvivalWithPotions >= Seconds;
        public bool InBand => TimeInBand && Survivable;
    }

    /// <summary>The nine event and roster mini-bosses (the coverage pass's <see cref="CoverageRole.MiniBoss"/> rows).</summary>
    public static IReadOnlyList<EnemyKind> MiniBosses { get; } =
        CreatureCoverageData.Creatures.Where(row => row.Role == CoverageRole.MiniBoss).Select(row => row.Kind).ToArray();

    /// <summary>Every boss (any tier) and mini-boss the report covers, progression bosses first.</summary>
    public static IReadOnlyList<EnemyKind> Subjects { get; } =
        EnemyArchetypes.BossProgression
            .Concat(EnemyArchetypes.All.Where(archetype => archetype.IsBoss && !EnemyArchetypes.BossProgression.Contains(archetype.Kind))
                .OrderBy(archetype => archetype.Tier).ThenBy(archetype => archetype.HitPoints).Select(archetype => archetype.Kind))
            .Concat(MiniBosses).ToArray();

    private static readonly Dictionary<WorldDifficulty, IReadOnlyList<Row>> Cache = new();
    private static readonly object CacheLock = new();

    /// <summary>Every subject's row on <paramref name="difficulty"/> (cached; the crafting audit is the slow part).</summary>
    public static IReadOnlyList<Row> Rows(WorldDifficulty difficulty = WorldDifficulty.Classic)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(difficulty, out var cached))
                return cached;
            var audit = CraftingAudit.Run();
            var kits = new Dictionary<int, Kit>();
            var rows = Subjects.Select(kind =>
            {
                var stage = KitStage(ReachStage(kind, audit), audit);
                if (!kits.TryGetValue(stage, out var kit))
                    kits[stage] = kit = BuildKit(stage, audit);
                var archetype = EnemyArchetypes.Get(kind);
                return Evaluate(archetype, WithoutOwnDrops(kit, archetype, audit), difficulty);
            }).ToArray();
            Cache[difficulty] = rows;
            return rows;
        }
    }

    /// <summary>Row for one subject.</summary>
    public static Row RowFor(EnemyKind kind, WorldDifficulty difficulty = WorldDifficulty.Classic) =>
        Rows(difficulty).First(row => row.Kind == kind);

    // ------------------------------------------------------------------ kit

    /// <summary>
    /// Audit stage at which a boss becomes fightable. Mini-bosses follow their slot: an event's first availability (after
    /// the Fracture for post-Fracture rows) or, for the late dungeon, the bulb boss's fall.
    /// </summary>
    public static int ReachStage(EnemyKind kind, CraftingAudit.Result audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        if (audit.BossReach.TryGetValue(kind, out var reach))
            return Math.Max(reach, LifecycleFloor(kind, audit));
        var fracture = audit.BossReach.GetValueOrDefault(GameSimulation.CoreGateBoss) + 1; // the stage the Tyrant's fall opens
        var row = CreatureCoverageData.Creatures.FirstOrDefault(entry => entry.Kind == kind);
        if (row is null)
            return audit.Stages.Count - 1;
        var floor = row.Hard ? fracture : 0;
        if (row.Slot == "dungeon-hard")
            return Math.Max(floor, DefeatStage(GameSimulation.BulbBoss, audit));
        if (row.Slot.StartsWith("event:", StringComparison.Ordinal) &&
            Enum.TryParse<WorldEventKind>(row.Slot["event:".Length..], out var eventKind) &&
            audit.EventReach.TryGetValue(eventKind, out var eventStage))
            return Math.Max(floor, eventStage);
        return floor;
    }

    /// <summary>
    /// 0.2.2 stream C: the game's own lifecycle gates that put a boss later than its first summon. PC-1: the Moon Eater (its
    /// lure refuses and the Rust Moon brings no boss) only after the Fracture; PC-2: the Protocol Dragon only ends a Defense
    /// Protocol once the Vault Colossus has fallen. A no-op once the crafting audit models the same gates.
    /// </summary>
    public static int LifecycleFloor(EnemyKind kind, CraftingAudit.Result audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        return kind switch
        {
            EnemyKind.MoonEater => DefeatStage(GameSimulation.CoreGateBoss, audit),
            EnemyKind.ProtocolDragon => DefeatStage(EnemyKind.VaultColossus, audit),
            _ => 0
        };
    }

    /// <summary>
    /// The kit's stage: from the reach stage, everything the run gathers before it fights its next boss. The audit takes
    /// every station, drill gate and event (milestone orders below 3000) before any boss, so this is the full gear closure
    /// of that point of progression rather than the first instant the summon exists.
    /// </summary>
    public static int KitStage(int reach, CraftingAudit.Result audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        for (var stage = Math.Max(1, reach + 1); stage < audit.Stages.Count; stage++)
            if (audit.Stages[stage].StartsWith("Defeated ", StringComparison.Ordinal))
                return stage - 1;
        return audit.Stages.Count - 1;
    }

    /// <summary>
    /// Highest gear tier the run crafts by <paramref name="stage"/> (at least 1). Kit items above it are drop windfalls
    /// (the audit counts any creature that can drop an item as soon as it can be met) and stay out of the reference kit.
    /// </summary>
    public static int TierCeiling(int stage, CraftingAudit.Result audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        return Math.Max(1, audit.Items.Where(entry => entry.Value.Stage <= stage && entry.Value.Via.StartsWith("craft", StringComparison.Ordinal))
            .Select(entry => audit.Catalog[entry.Key])
            .Where(item => item.Damage > 0 && item.UseKind is ItemUseKind.Melee or ItemUseKind.Ranged or ItemUseKind.Tech ||
                           item.EquipmentSlot == EquipmentSlot.Body && item.Defense > 0)
            .Select(item => item.Tier).DefaultIfEmpty(1).Max());
    }

    private static int DefeatStage(EnemyKind kind, CraftingAudit.Result audit)
    {
        var name = "Defeated " + EnemyArchetypes.Get(kind).DisplayName;
        var index = audit.Stages.ToList().IndexOf(name);
        return index >= 0 ? index : audit.Stages.Count - 1;
    }

    /// <summary>The reference kit from everything the audit reaches by <paramref name="stage"/>, up to the crafted tier.</summary>
    public static Kit BuildKit(int stage, CraftingAudit.Result audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        var catalog = audit.Catalog;
        var ceiling = TierCeiling(stage, audit);
        var reached = audit.Items.Where(entry => entry.Value.Stage <= stage).Select(entry => catalog[entry.Key])
            .Where(item => item.Tier <= ceiling).ToArray();
        bool Has(ItemId id) => audit.Items.TryGetValue(id, out var reach) && reach.Stage <= stage;

        var weapons = reached.Where(IsReferenceWeapon).ToArray();
        var ammo = reached.Where(item => item.AmmoKind != AmmoClass.None && item.UseKind == ItemUseKind.Ammunition).ToArray();

        // Armour: the best complete set (pieces plus the set bonus), or the best loose head, body and legs if that beats it.
        var armour = reached.Where(item => item.EquipmentSlot is EquipmentSlot.Head or EquipmentSlot.Body or EquipmentSlot.Legs &&
                                           !item.IsVanity && item.UseKind == ItemUseKind.Equipment).ToArray();
        var bestSet = armour.Where(item => item.ArmorSetId is not null)
            .GroupBy(item => item.ArmorSetId!, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var pieces = new[] { EquipmentSlot.Head, EquipmentSlot.Body, EquipmentSlot.Legs }
                    .Select(slot => group.Where(item => item.EquipmentSlot == slot).MaxBy(item => item.Defense)).ToArray();
                return pieces.Any(piece => piece is null)
                    ? (Label: group.Key, Defense: -1)
                    : (Label: pieces[1]!.Name.Replace(" chestpiece", "", StringComparison.OrdinalIgnoreCase),
                        Defense: pieces.Sum(piece => piece!.Defense) + pieces.Max(piece => piece!.SetBonusDefense));
            })
            .Where(set => set.Defense >= 0).OrderByDescending(set => set.Defense).FirstOrDefault();
        var loose = new[] { EquipmentSlot.Head, EquipmentSlot.Body, EquipmentSlot.Legs }
            .Sum(slot => armour.Where(item => item.EquipmentSlot == slot).Select(item => item.Defense).DefaultIfEmpty(0).Max());
        var (armorLabel, armorDefense) = bestSet.Label is not null && bestSet.Defense >= loose
            ? (bestSet.Label + " set", bestSet.Defense)
            : ("loose pieces", loose);

        var accessories = reached.Where(item => item.EquipmentSlot is >= EquipmentSlot.Accessory && item.UseKind == ItemUseKind.Equipment &&
                                                !item.IsVanity)
            .Select(item => item.Defense).OrderByDescending(defense => defense).Take(5).Sum();

        var potion = ContentItems.HealingLadder.Where(rung => Has(rung.Item)).Select(rung => (rung.Name, rung.Heal))
            .DefaultIfEmpty(("none", 0f)).MaxBy(rung => rung.Item2);
        var life = 100f + (Has(ItemId.LifeCore) ? PlayerCharacter.MaximumLifeCores * PlayerCharacter.LifeCoreBonus : 0f) +
                   (Has(ItemId.LifebloomPod) ? GrowthItems.MaximumLifeblooms * GrowthItems.LifebloomBonus : 0f);
        return new Kit(stage, audit.Stages[stage], weapons, ammo, armorDefense, armorLabel, accessories, life, potion.Item2, potion.Item1);
    }

    /// <summary>The kit without the weapons only this creature drops (nobody fights a boss with its own loot).</summary>
    public static Kit WithoutOwnDrops(Kit kit, EnemyArchetype archetype, CraftingAudit.Result audit)
    {
        ArgumentNullException.ThrowIfNull(kit);
        ArgumentNullException.ThrowIfNull(archetype);
        ArgumentNullException.ThrowIfNull(audit);
        bool Own(ItemDefinition item) => audit.Items.TryGetValue(item.Id, out var reach) &&
                                         (reach.Via.StartsWith("kill " + archetype.DisplayName + " ", StringComparison.Ordinal) ||
                                          reach.Via == "defeat " + archetype.DisplayName);
        return kit.Weapons.Any(Own) ? kit with { Weapons = kit.Weapons.Where(item => !Own(item)).ToArray() } : kit;
    }

    /// <summary>Reusable melee, ranged and tech weapons (thrown consumables, tools and drone remotes are not the reference).</summary>
    private static bool IsReferenceWeapon(ItemDefinition item) =>
        item.Damage > 0 && item.UseCooldownTicks > 0 && !item.Thrown && item.Style != WeaponStyle.Thrown &&
        item.UseKind is ItemUseKind.Melee or ItemUseKind.Ranged or ItemUseKind.Tech &&
        item.EffectiveClass is DamageClass.Melee or DamageClass.Ranged or DamageClass.Tech;

    // ------------------------------------------------------------------ damage out

    /// <summary>Expected damage of one hit after the creature's defense, crits doubling.</summary>
    public static float ExpectedHit(float damage, int defense, int critChance)
    {
        var crit = Math.Clamp(critChance, 0, 100) / 100f;
        return (1f - crit) * CombatTuning.CreatureDamageTaken(damage, defense, 0, false) +
               crit * CombatTuning.CreatureDamageTaken(damage, defense, 0, true);
    }

    /// <summary>A weapon's output against <paramref name="defense"/>, or null when it has no ammunition in the kit.</summary>
    public static WeaponOutput? Measure(ItemDefinition weapon, IReadOnlyList<ItemDefinition> ammo, int defense)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        ItemDefinition? round = null;
        if (weapon.UsesAmmo != AmmoClass.None)
        {
            round = ammo.Where(item => item.AmmoKind == weapon.UsesAmmo).MaxBy(item => item.AmmoDamage);
            if (round is null)
                return null;
        }
        var damage = CombatTuning.WeaponDamage(weapon.Damage, 1f, round?.AmmoDamage ?? 0, 1f);
        var crit = CombatTuning.WeaponCritChance(weapon.EffectiveClass, weapon.CritChance, 0);
        var projectiles = weapon.EffectiveClass == DamageClass.Melee ? 1
            : weapon.Style == WeaponStyle.Shotgun && weapon.ProjectileCount < 2 ? 4
            : Math.Max(1, weapon.ProjectileCount);
        return new WeaponOutput(weapon, round, ExpectedHit(damage, defense, crit) * projectiles, 60f / weapon.UseCooldownTicks);
    }

    /// <summary>The best weapon of each class (melee, ranged, tech) among that class's highest tier in the kit.</summary>
    public static IReadOnlyList<WeaponOutput> ClassBests(Kit kit, int defense)
    {
        ArgumentNullException.ThrowIfNull(kit);
        return kit.Weapons.GroupBy(weapon => weapon.EffectiveClass)
            .Select(group =>
            {
                var measured = group.Select(weapon => Measure(weapon, kit.Ammo, defense)).Where(output => output is not null)
                    .Select(output => output!).ToArray();
                if (measured.Length == 0)
                    return null;
                var tier = measured.Max(output => output.Weapon.Tier);
                return measured.Where(output => output.Weapon.Tier == tier).MaxBy(output => output.Dps);
            })
            .Where(output => output is not null).Select(output => output!).OrderBy(output => output.Dps).ToArray();
    }

    /// <summary>
    /// The reference weapon (the "tier weapon set"): each class's best weapon of its highest tier in the kit against this
    /// defense, then the median of the three. Lower-tier oddities that outpace their tier (a rapid tier-5 power fist in a
    /// tier-6 kit) and the charge-hungry tech tomes at the top do not set the curve.
    /// </summary>
    public static WeaponOutput Reference(Kit kit, int defense)
    {
        var best = ClassBests(kit, defense);
        return best[best.Count / 2];
    }

    // ------------------------------------------------------------------ damage in and the row

    /// <summary>One boss on one difficulty against its kit.</summary>
    public static Row Evaluate(EnemyArchetype archetype, Kit kit, WorldDifficulty difficulty)
    {
        ArgumentNullException.ThrowIfNull(archetype);
        ArgumentNullException.ThrowIfNull(kit);
        var mini = !archetype.IsBoss;
        var reference = Reference(kit, archetype.Defense);
        var hitPoints = archetype.HitPoints * WorldSettings.EnemyHealthScale(difficulty);
        var hits = (int)MathF.Ceiling(hitPoints / Math.Max(0.01f, reference.DamagePerUse));
        var seconds = hitPoints / Math.Max(0.01f, reference.Dps);

        var raw = archetype.AttackDamage * DefenseBalance.EnemyHitScale(archetype) * WorldSettings.EnemyDamageScale(difficulty);
        if (archetype.IsBoss)
            raw = Math.Min(raw, GameSimulation.BossDamageCapFor(archetype, difficulty));
        var taken = DefenseBalance.ApplyDefense(raw, kit.Defense, difficulty);
        float hitsPerSecond;
        if (mini)
            hitsPerSecond = 1f / archetype.ContactCooldown;
        else
        {
            // Hit-point share spent in each phase at a steady damage rate; phase 2 attacks 2.1/1.45 times as often.
            var enraged = Math.Clamp(archetype.PhaseThreshold, 0f, 1f);
            var calm = archetype.AttackCooldown;
            hitsPerSecond = (1f - enraged) / calm + enraged / (calm * (1.45f / 2.1f));
        }
        var incoming = taken * hitsPerSecond;
        var unhealed = kit.Life / Math.Max(0.01f, incoming);
        var drain = incoming * (1f - DodgedShare) - kit.PotionHeal / GameSimulation.PotionSicknessSeconds;
        var withPotions = drain <= 0f ? float.PositiveInfinity : (kit.Life + kit.PotionHeal) / drain;
        return new Row(archetype, mini, IndexOf(EnemyArchetypes.BossProgression, archetype.Kind), difficulty, kit, reference, hitPoints,
            hits, seconds, raw, taken, hitsPerSecond, unhealed, withPotions);
    }

    private static int IndexOf(IReadOnlyList<EnemyKind> list, EnemyKind kind)
    {
        for (var i = 0; i < list.Count; i++)
            if (list[i] == kind)
                return i;
        return -1;
    }

    /// <summary>Spearman rank correlation of two equal-length series (average ranks for ties).</summary>
    public static float Spearman(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var ra = Ranks(a);
        var rb = Ranks(b);
        var ma = ra.Average();
        var mb = rb.Average();
        var cov = ra.Zip(rb, (x, y) => (x - ma) * (y - mb)).Sum();
        var va = ra.Sum(x => (x - ma) * (x - ma));
        var vb = rb.Sum(y => (y - mb) * (y - mb));
        return va <= 0 || vb <= 0 ? 0f : (float)(cov / Math.Sqrt(va * vb));
    }

    private static double[] Ranks(IReadOnlyList<float> values)
    {
        var order = values.Select((value, index) => (value, index)).OrderBy(pair => pair.value).ToArray();
        var ranks = new double[values.Count];
        for (var i = 0; i < order.Length;)
        {
            var j = i;
            while (j + 1 < order.Length && order[j + 1].value == order[i].value)
                j++;
            for (var k = i; k <= j; k++)
                ranks[order[k].index] = (i + j) / 2.0 + 1;
            i = j + 1;
        }
        return ranks;
    }

    /// <summary>Spearman correlation of hits to kill against progression index over the progression bosses.</summary>
    public static float ProgressionCorrelation(WorldDifficulty difficulty = WorldDifficulty.Classic)
    {
        var rows = Rows(difficulty).Where(row => row.ProgressionIndex >= 0).OrderBy(row => row.ProgressionIndex).ToArray();
        return Spearman(rows.Select(row => (float)row.ProgressionIndex).ToArray(), rows.Select(row => (float)row.Hits).ToArray());
    }

    // ------------------------------------------------------------------ design targets

    /// <summary>
    /// The design time to kill (Classic, seconds) each subject was tuned to (PC-15, BO-23). The progression bosses rise
    /// linearly from 62 s (the Overseer mech) to 175 s (the Astro-God, 2.8×; Terraria's ratio is about 2.6); the rest sit
    /// where their place in progression puts them, mini-bosses near the bottom of the band.
    /// </summary>
    public static float TargetSeconds(EnemyKind kind)
    {
        var index = IndexOf(EnemyArchetypes.BossProgression, kind);
        if (index >= 0)
            return 62f + (175f - 62f) * index / (EnemyArchetypes.BossProgression.Count - 1);
        return kind switch
        {
            EnemyKind.FalloutHivemind => 80f, EnemyKind.SporeMonarch => 85f, EnemyKind.HoarfrostBehemoth => 90f,
            EnemyKind.ScavengerWarlord => 65f, EnemyKind.VoidStalker => 68f, EnemyKind.CyberPirateCaptain => 70f,
            EnemyKind.SolarFlareEntity => 72f, EnemyKind.AsteroidMinerMech => 75f, EnemyKind.MoonEater => 78f,
            EnemyKind.PirateDreadnought => 85f, EnemyKind.ProtocolDragon => 88f, EnemyKind.AbyssLeviathan => 90f,
            EnemyKind.GelSovereign => 110f, EnemyKind.BloomMatriarch => 120f, EnemyKind.EclipseLord => 120f,
            EnemyKind.MothershipCore => 135f, EnemyKind.PrismEmpress => 140f, EnemyKind.HollowRegent => 140f,
            EnemyKind.RimeSeraph => 145f, EnemyKind.VaultColossus => 155f, EnemyKind.VoidCantor => 160f,
            EnemyKind.ScavengerHexer => 62f, EnemyKind.BlizzardColossus => 65f, EnemyKind.GritElemental => 65f,
            EnemyKind.CryptPaladin => 70f, EnemyKind.HollowLancer => 70f, EnemyKind.CorrosionNautilus => 75f,
            EnemyKind.EmberRootwalker => 75f, EnemyKind.FrostspirePine => 75f, EnemyKind.CryoTank => 80f,
            _ => 90f
        };
    }

    /// <summary>Share of the survivable incoming damage (the tighter of the two survival checks) a boss is tuned to deal.</summary>
    public const float TargetPressure = 0.85f;

    /// <summary>
    /// Prints the hit points and attack damage that put each subject on its <see cref="TargetSeconds"/> with
    /// <see cref="TargetPressure"/> of the survivable damage (Classic). The tuning tool behind the boss rows.
    /// </summary>
    public static int Suggest()
    {
        Console.WriteLine($"  {"boss",-22}{"target s",9}{"HP now",8}{"HP",8}{"dmg now",8}{"dmg",6}");
        foreach (var row in Rows(WorldDifficulty.Classic))
        {
            var target = TargetSeconds(row.Kind);
            var hp = row.Reference.Dps * target;
            hp = hp >= 20000f ? MathF.Round(hp / 1000f) * 1000f : hp >= 2000f ? MathF.Round(hp / 100f) * 100f : MathF.Round(hp / 50f) * 50f;
            var budget = Math.Min(row.Kit.Life / MinimumUnhealedSurvival,
                2f * ((row.Kit.Life + row.Kit.PotionHeal) / target + row.Kit.PotionHeal / GameSimulation.PotionSicknessSeconds));
            var taken = TargetPressure * budget / row.HitsPerSecond;
            var raw = taken + row.Kit.Defense * DefenseBalance.DefenseFactor(WorldDifficulty.Classic);
            var scale = DefenseBalance.EnemyHitScale(row.Archetype with { HitPoints = hp });
            var damage = MathF.Round(raw / scale);
            Console.WriteLine($"  {Trim(row.Name, 21),-22}{target,9:0}{row.Archetype.HitPoints,8:0}{hp,8:0}{row.Archetype.AttackDamage,8:0}{damage,6:0}");
        }
        return 0;
    }

    /// <summary>The Classic table as markdown, with the Brutal and Annihilation times beside it (docs/CREATURE_COVERAGE.md).</summary>
    public static int Markdown()
    {
        var brutal = Rows(WorldDifficulty.Brutal).ToDictionary(row => row.Kind);
        var master = Rows(WorldDifficulty.Annihilation).ToDictionary(row => row.Kind);
        Console.WriteLine("| Boss | Tier | # | Kit stage · reference weapon | Life · def · stim | HP | Def | Dmg | Hits | TTK s | Hit taken | Unhealed s | With stims s | Brutal TTK · unhealed | Annihilation TTK · unhealed |");
        Console.WriteLine("|---|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---|");
        foreach (var row in Rows(WorldDifficulty.Classic))
        {
            static string Survival(float seconds) => float.IsPositiveInfinity(seconds) ? "∞" : seconds.ToString("0");
            var b = brutal[row.Kind];
            var a = master[row.Kind];
            Console.WriteLine($"| {row.Name} | {(row.MiniBoss ? "mini" : row.Archetype.Tier.ToString().ToLowerInvariant())} | " +
                              $"{(row.ProgressionIndex >= 0 ? (row.ProgressionIndex + 1).ToString() : "")} | {row.Kit.StageName} · {row.Reference.Label} | " +
                              $"{row.Kit.Life:0} · {row.Kit.Defense} · {row.Kit.PotionHeal:0} | {row.Archetype.HitPoints:0} | {row.Archetype.Defense} | " +
                              $"{row.Archetype.AttackDamage:0} | {row.Hits} | {row.Seconds:0} | {row.HitTaken:0} | {Survival(row.SurvivalUnhealed)} | " +
                              $"{Survival(row.SurvivalWithPotions)} | {b.Seconds:0} · {Survival(b.SurvivalUnhealed)} | {a.Seconds:0} · {Survival(a.SurvivalUnhealed)} |");
        }
        return 0;
    }

    // ------------------------------------------------------------------ report

    /// <summary>
    /// Prints the table on Classic, Brutal and Annihilation (or the difficulties named on the command line) and returns 1
    /// when any Classic row misses its band.
    /// </summary>
    public static int Report(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Any(arg => string.Equals(arg, "suggest", StringComparison.OrdinalIgnoreCase)))
            return Suggest();
        if (args.Any(arg => string.Equals(arg, "markdown", StringComparison.OrdinalIgnoreCase)))
            return Markdown();
        var named = Enum.GetValues<WorldDifficulty>()
            .Where(difficulty => args.Any(arg => string.Equals(arg, difficulty.ToString(), StringComparison.OrdinalIgnoreCase))).ToArray();
        var difficulties = named.Length > 0 ? named : [WorldDifficulty.Classic, WorldDifficulty.Brutal, WorldDifficulty.Annihilation];
        var started = DateTime.UtcNow;
        var failures = 0;
        foreach (var difficulty in difficulties)
        {
            var rows = Rows(difficulty);
            Console.WriteLine($"Boss balance on {WorldSettings.Name(difficulty)} (life x{WorldSettings.EnemyHealthScale(difficulty):0.##}, " +
                              $"damage x{WorldSettings.EnemyDamageScale(difficulty):0.##}, defense counts {DefenseBalance.DefenseFactor(difficulty):P0})");
            Console.WriteLine($"  {"boss",-22}{"tier",-6}{"#",3} {"kit stage",-30}{"life",5}{"def",4}{"stim",5} {"weapon",-34}" +
                              $"{"HP",7}{"bdef",5}{"dmg/use",8}{"DPS",7}{"hits",6}{"TTK s",7}{"hit",6}{"taken",6}{"in/s",6}{"surv s",7}{"w/stim",8}  band");
            foreach (var row in rows)
            {
                var band = row.InBand ? "ok" : string.Join(",", new[]
                {
                    row.Seconds < MinimumSeconds ? "fast" : null, row.Seconds > MaximumSeconds ? "slow" : null,
                    row.SurvivalUnhealed < MinimumUnhealedSurvival ? "burst" : null, row.SurvivalWithPotions < row.Seconds ? "attrition" : null
                }.Where(flag => flag is not null));
                if (!row.InBand && difficulty == WorldDifficulty.Classic)
                    failures++;
                var tier = row.MiniBoss ? "Mini" : row.Archetype.Tier.ToString();
                var stage = row.Kit.StageName.Length > 29 ? row.Kit.StageName[..29] : row.Kit.StageName;
                var weapon = row.Reference.Label.Length > 33 ? row.Reference.Label[..33] : row.Reference.Label;
                var potions = float.IsPositiveInfinity(row.SurvivalWithPotions) ? "inf" : row.SurvivalWithPotions.ToString("0");
                Console.WriteLine($"  {Trim(row.Name, 21),-22}{tier,-6}{(row.ProgressionIndex >= 0 ? (row.ProgressionIndex + 1).ToString() : ""),3} {stage,-30}" +
                                  $"{row.Kit.Life,5:0}{row.Kit.Defense,4}{row.Kit.PotionHeal,5:0} {weapon,-34}{row.HitPoints,7:0}{row.Archetype.Defense,5}" +
                                  $"{row.Reference.DamagePerUse,8:0.0}{row.Reference.Dps,7:0}{row.Hits,6}{row.Seconds,7:0}{row.RawHit,6:0}{row.HitTaken,6:0}" +
                                  $"{row.IncomingDps,6:0.0}{row.SurvivalUnhealed,7:0}{potions,8}  {band}");
            }
            var inBand = rows.Count(row => row.InBand);
            var progression = rows.Where(row => row.ProgressionIndex >= 0).ToArray();
            var first = progression.First(row => row.ProgressionIndex == 0);
            var final = progression.First(row => row.Kind == EnemyKind.AstroGod);
            Console.WriteLine($"  {inBand}/{rows.Count} in band; hits-to-kill vs progression Spearman {ProgressionCorrelation(difficulty):0.00}; " +
                              $"{final.Name} {final.Hits} hits = {(float)final.Hits / first.Hits:0.00}x the {first.Name} ({first.Hits}); " +
                              $"TTK {final.Seconds:0} s vs {first.Seconds:0} s ({final.Seconds / first.Seconds:0.00}x)");
            Console.WriteLine();
        }
        Console.WriteLine($"Boss balance done in {(DateTime.UtcNow - started).TotalSeconds:0.0}s; Classic rows out of band: {failures}.");
        return failures == 0 ? 0 : 1;
    }

    private static string Trim(string text, int length) => text.Length > length ? text[..length] : text;
}
