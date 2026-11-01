using Rimtera.Characters;

namespace Rimtera.Systems;

/// <summary>
/// Random prefix rolled when a weapon is crafted; stored on the inventory stack
/// (<see cref="ItemStack.Calibration"/>). Append-only: the byte value is saved.
/// </summary>
public enum Calibration : byte
{
    None,
    Precise,
    Overclocked,
    Reinforced,
    Featherweight,
    Volatile,
    Cracked,
    Tuned,
    Shoddy,
    Sluggish,
    Hardened,
    Efficient,
    Masterwork,
    // Arsenal pass (append-only): class-specific and accessory calibrations.
    Keen, Massive, Hefty, Nimble, Brutal, Lengthened, Savage, Blunted,
    Rifled, Hypersonic, Sighted, Rapid, Deadeye, Powered, Misaligned, Jittery,
    Focused, Resonant, Conductive, Amplified, Harmonic, Leaky, Unstable,
    Synced, Commanding, Linked, Networked, Lagging,
    Armored, Plated, Bulwarked, Lucky, Sharp, Honed, Swift, Quick, Spirited, Fierce, Menacing
}

/// <summary>Which items a calibration may roll on.</summary>
public enum CalibrationGroup : byte
{
    /// <summary>Any weapon.</summary>
    Universal,
    Melee,
    Ranged,
    Tech,
    Command,
    /// <summary>Accessories (defense, crit, speed, damage).</summary>
    Accessory
}

/// <summary>Modifiers one calibration applies (multipliers are 1 when neutral).</summary>
public sealed record CalibrationDefinition(
    Calibration Id,
    string Name,
    float DamageMultiplier = 1f,
    int CritBonus = 0,
    float CooldownMultiplier = 1f,
    float KnockbackMultiplier = 1f,
    float ChargeCostMultiplier = 1f,
    float ValueMultiplier = 1f,
    int Weight = 10,
    CalibrationGroup Group = CalibrationGroup.Universal,
    float VelocityMultiplier = 1f,
    float SizeMultiplier = 1f,
    int AccessoryDefense = 0,
    float AccessoryMoveSpeed = 0f);

/// <summary>Rimtera's calibration table and the seeded roll used at the crafting bench.</summary>
public static class CalibrationCatalog
{
    private static readonly Dictionary<Calibration, CalibrationDefinition> Table = new CalibrationDefinition[]
    {
        new(Calibration.None, ""),
        new(Calibration.Precise, "Precise", 1.05f, CritBonus: 6, ValueMultiplier: 1.25f, Weight: 12),
        new(Calibration.Overclocked, "Overclocked", 1.12f, CooldownMultiplier: 0.9f, ValueMultiplier: 1.5f, Weight: 7),
        new(Calibration.Reinforced, "Reinforced", 1.05f, KnockbackMultiplier: 1.15f, ValueMultiplier: 1.2f, Weight: 12),
        new(Calibration.Featherweight, "Featherweight", CooldownMultiplier: 0.85f, ValueMultiplier: 1.3f, Weight: 10),
        new(Calibration.Volatile, "Volatile", 1.18f, CritBonus: 4, CooldownMultiplier: 1.1f, ValueMultiplier: 1.25f, Weight: 8),
        new(Calibration.Cracked, "Cracked", 0.85f, ValueMultiplier: 0.7f, Weight: 10),
        new(Calibration.Tuned, "Tuned", 1.08f, CooldownMultiplier: 0.92f, ChargeCostMultiplier: 0.85f, ValueMultiplier: 1.4f, Weight: 8),
        new(Calibration.Shoddy, "Shoddy", 0.9f, KnockbackMultiplier: 0.9f, ValueMultiplier: 0.75f, Weight: 10),
        new(Calibration.Sluggish, "Sluggish", CooldownMultiplier: 1.15f, ValueMultiplier: 0.8f, Weight: 9),
        new(Calibration.Hardened, "Hardened", 1.1f, ValueMultiplier: 1.3f, Weight: 10),
        new(Calibration.Efficient, "Efficient", ChargeCostMultiplier: 0.75f, ValueMultiplier: 1.2f, Weight: 8),
        new(Calibration.Masterwork, "Masterwork", 1.15f, CritBonus: 5, CooldownMultiplier: 0.9f, KnockbackMultiplier: 1.1f,
            ValueMultiplier: 2f, Weight: 2),
        // Close quarters (SizeMultiplier scales melee reach/arc).
        new(Calibration.Keen, "Keen", 1.03f, CritBonus: 5, ValueMultiplier: 1.2f, Weight: 9, Group: CalibrationGroup.Melee),
        new(Calibration.Massive, "Massive", 1.06f, CooldownMultiplier: 1.05f, ValueMultiplier: 1.3f, Weight: 8, Group: CalibrationGroup.Melee, SizeMultiplier: 1.18f),
        new(Calibration.Hefty, "Hefty", 1.08f, KnockbackMultiplier: 1.25f, ValueMultiplier: 1.25f, Weight: 9, Group: CalibrationGroup.Melee),
        new(Calibration.Nimble, "Nimble", CooldownMultiplier: 0.88f, ValueMultiplier: 1.25f, Weight: 9, Group: CalibrationGroup.Melee),
        new(Calibration.Brutal, "Brutal", 1.15f, ValueMultiplier: 1.5f, Weight: 5, Group: CalibrationGroup.Melee, SizeMultiplier: 1.1f),
        new(Calibration.Lengthened, "Lengthened", ValueMultiplier: 1.15f, Weight: 9, Group: CalibrationGroup.Melee, SizeMultiplier: 1.12f),
        new(Calibration.Savage, "Savage", 1.12f, KnockbackMultiplier: 1.1f, ValueMultiplier: 1.4f, Weight: 6, Group: CalibrationGroup.Melee),
        new(Calibration.Blunted, "Blunted", 0.9f, ValueMultiplier: 0.75f, Weight: 8, Group: CalibrationGroup.Melee, SizeMultiplier: 0.9f),
        // Ballistics (VelocityMultiplier scales projectile speed).
        new(Calibration.Rifled, "Rifled", 1.05f, ValueMultiplier: 1.2f, Weight: 9, Group: CalibrationGroup.Ranged, VelocityMultiplier: 1.2f),
        new(Calibration.Hypersonic, "Hypersonic", CritBonus: 3, ValueMultiplier: 1.3f, Weight: 7, Group: CalibrationGroup.Ranged, VelocityMultiplier: 1.3f),
        new(Calibration.Sighted, "Sighted", CritBonus: 8, ValueMultiplier: 1.3f, Weight: 8, Group: CalibrationGroup.Ranged),
        new(Calibration.Rapid, "Rapid", CooldownMultiplier: 0.88f, ValueMultiplier: 1.3f, Weight: 8, Group: CalibrationGroup.Ranged),
        new(Calibration.Deadeye, "Deadeye", 1.12f, CritBonus: 5, ValueMultiplier: 1.7f, Weight: 3, Group: CalibrationGroup.Ranged, VelocityMultiplier: 1.1f),
        new(Calibration.Powered, "Powered", 1.1f, KnockbackMultiplier: 1.1f, ValueMultiplier: 1.3f, Weight: 8, Group: CalibrationGroup.Ranged),
        new(Calibration.Misaligned, "Misaligned", 0.92f, ValueMultiplier: 0.75f, Weight: 8, Group: CalibrationGroup.Ranged, VelocityMultiplier: 0.85f),
        new(Calibration.Jittery, "Jittery", CritBonus: -2, CooldownMultiplier: 1.1f, ValueMultiplier: 0.75f, Weight: 7, Group: CalibrationGroup.Ranged),
        // Tech.
        new(Calibration.Focused, "Focused", 1.05f, ChargeCostMultiplier: 0.85f, ValueMultiplier: 1.3f, Weight: 9, Group: CalibrationGroup.Tech),
        new(Calibration.Resonant, "Resonant", 1.12f, ValueMultiplier: 1.35f, Weight: 7, Group: CalibrationGroup.Tech),
        new(Calibration.Conductive, "Conductive", ChargeCostMultiplier: 0.8f, ValueMultiplier: 1.25f, Weight: 8, Group: CalibrationGroup.Tech),
        new(Calibration.Amplified, "Amplified", 1.15f, ChargeCostMultiplier: 1.1f, ValueMultiplier: 1.4f, Weight: 6, Group: CalibrationGroup.Tech),
        new(Calibration.Harmonic, "Harmonic", CooldownMultiplier: 0.9f, ChargeCostMultiplier: 0.9f, ValueMultiplier: 1.4f, Weight: 6, Group: CalibrationGroup.Tech),
        new(Calibration.Leaky, "Leaky", ChargeCostMultiplier: 1.2f, ValueMultiplier: 0.75f, Weight: 8, Group: CalibrationGroup.Tech),
        new(Calibration.Unstable, "Unstable", 1.2f, CritBonus: 3, ChargeCostMultiplier: 1.25f, ValueMultiplier: 1.2f, Weight: 6, Group: CalibrationGroup.Tech),
        // Command.
        new(Calibration.Synced, "Synced", 1.1f, ValueMultiplier: 1.3f, Weight: 9, Group: CalibrationGroup.Command),
        new(Calibration.Commanding, "Commanding", 1.15f, CooldownMultiplier: 0.9f, ValueMultiplier: 1.6f, Weight: 4, Group: CalibrationGroup.Command),
        new(Calibration.Linked, "Linked", 1.06f, KnockbackMultiplier: 1.1f, ValueMultiplier: 1.2f, Weight: 9, Group: CalibrationGroup.Command),
        new(Calibration.Networked, "Networked", 1.12f, ValueMultiplier: 1.4f, Weight: 6, Group: CalibrationGroup.Command),
        new(Calibration.Lagging, "Lagging", 0.88f, ValueMultiplier: 0.75f, Weight: 8, Group: CalibrationGroup.Command),
        // Accessories (DamageMultiplier/CritBonus apply to every class while worn).
        new(Calibration.Armored, "Armored", ValueMultiplier: 1.1f, Weight: 10, Group: CalibrationGroup.Accessory, AccessoryDefense: 1),
        new(Calibration.Plated, "Plated", ValueMultiplier: 1.2f, Weight: 7, Group: CalibrationGroup.Accessory, AccessoryDefense: 2),
        new(Calibration.Bulwarked, "Bulwarked", ValueMultiplier: 1.35f, Weight: 4, Group: CalibrationGroup.Accessory, AccessoryDefense: 3),
        new(Calibration.Lucky, "Lucky", CritBonus: 1, ValueMultiplier: 1.1f, Weight: 10, Group: CalibrationGroup.Accessory),
        new(Calibration.Sharp, "Sharp", CritBonus: 2, ValueMultiplier: 1.2f, Weight: 7, Group: CalibrationGroup.Accessory),
        new(Calibration.Honed, "Honed", CritBonus: 3, ValueMultiplier: 1.35f, Weight: 4, Group: CalibrationGroup.Accessory),
        new(Calibration.Swift, "Swift", ValueMultiplier: 1.15f, Weight: 9, Group: CalibrationGroup.Accessory, AccessoryMoveSpeed: 0.02f),
        new(Calibration.Quick, "Quick", ValueMultiplier: 1.3f, Weight: 5, Group: CalibrationGroup.Accessory, AccessoryMoveSpeed: 0.04f),
        new(Calibration.Spirited, "Spirited", 1.01f, ValueMultiplier: 1.1f, Weight: 10, Group: CalibrationGroup.Accessory),
        new(Calibration.Fierce, "Fierce", 1.02f, ValueMultiplier: 1.2f, Weight: 7, Group: CalibrationGroup.Accessory),
        new(Calibration.Menacing, "Menacing", 1.04f, ValueMultiplier: 1.4f, Weight: 3, Group: CalibrationGroup.Accessory)
    }.ToDictionary(definition => definition.Id);

    public static CalibrationDefinition Get(Calibration calibration) =>
        Table.TryGetValue(calibration, out var definition) ? definition : Table[Calibration.None];

    public static IEnumerable<CalibrationDefinition> All => Table.Values.Where(definition => definition.Id != Calibration.None);

    /// <summary>Rolls a calibration for a freshly crafted weapon from the given (seeded) stream.</summary>
    public static Calibration Roll(Random random, ItemDefinition item)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (!item.Calibratable && !IsAccessory(item))
            return Calibration.None;
        var pool = All.Where(definition => ValidFor(definition, item)).ToArray();
        var total = pool.Sum(definition => definition.Weight);
        var pick = random.Next(total);
        foreach (var definition in pool)
        {
            pick -= definition.Weight;
            if (pick < 0)
                return definition.Id;
        }
        return pool[^1].Id;
    }

    /// <summary>Accessories (wings included) may roll accessory calibrations when crafted.</summary>
    public static bool IsAccessory(ItemDefinition item) =>
        item.UseKind == ItemUseKind.Equipment && item.EquipmentSlot == EquipmentSlot.Accessory && !item.IsVanity;

    /// <summary>The group an item rolls from (Universal calibrations are valid for every weapon).</summary>
    public static CalibrationGroup GroupFor(ItemDefinition item) => IsAccessory(item) ? CalibrationGroup.Accessory : item.EffectiveClass switch
    {
        DamageClass.Melee => CalibrationGroup.Melee,
        DamageClass.Ranged => CalibrationGroup.Ranged,
        DamageClass.Tech => CalibrationGroup.Tech,
        DamageClass.Command => CalibrationGroup.Command,
        _ => CalibrationGroup.Universal
    };

    /// <summary>
    /// True when the calibration may roll on the item: accessories take only accessory calibrations; weapons take
    /// Universal ones plus their own class group; charge-cost changes only on Charge users.
    /// </summary>
    public static bool ValidFor(CalibrationDefinition definition, ItemDefinition item)
    {
        var group = GroupFor(item);
        if (group == CalibrationGroup.Accessory)
            return definition.Group == CalibrationGroup.Accessory;
        if (definition.Group != CalibrationGroup.Universal && definition.Group != group)
            return false;
        return definition.ChargeCostMultiplier >= 1f || item.ChargeCost > 0;
    }

    /// <summary>"Precise Ferrite shiv", or the plain name when uncalibrated.</summary>
    public static string Describe(ItemDefinition item, Calibration calibration) =>
        calibration == Calibration.None ? item.Name : $"{Get(calibration).Name} {item.Name}";
}

/// <summary>Drones (orbit the player) and sentries (stand where deployed). Append-only.</summary>
public enum MinionKind : byte
{
    ScoutDrone,
    HornetDrone,
    RepairDrone,
    MechCompanion,
    FrostDrone,
    EmberDrone,
    RadSwarm,
    CoralJelly,
    SporeFamiliar,
    HiveFamiliar,
    LumeniteSprite,
    RadSwarmling,
    VoidFamiliar,
    AstralGuardian,
    TurretAutogun,
    TurretFlame,
    TurretMortar,
    TurretTesla
}

/// <summary>Behaviour row for one drone or sentry.</summary>
public sealed record MinionSpec(
    MinionKind Kind,
    string DisplayName,
    string Sprite,
    bool IsSentry,
    float FireIntervalSeconds,
    float Range,
    float ProjectileSpeed,
    int Projectiles = 1,
    bool Heals = false,
    float LifetimeSeconds = 0f);

public static class MinionCatalog
{
    private static readonly Dictionary<MinionKind, MinionSpec> Table = new MinionSpec[]
    {
        new(MinionKind.ScoutDrone, "Scout drone", "minion_scout_drone", false, 1.1f, 10f, 13f),
        new(MinionKind.HornetDrone, "Hornet drone", "minion_hornet_drone", false, 0.8f, 11f, 15f),
        new(MinionKind.RepairDrone, "Repair drone", "minion_repair_drone", false, 1.5f, 0f, 0f, Heals: true),
        new(MinionKind.MechCompanion, "Mech companion", "minion_mech_companion", false, 1.2f, 9f, 12f, 2),
        new(MinionKind.FrostDrone, "Frost drone", "minion_frost_drone", false, 1f, 12f, 14f),
        new(MinionKind.EmberDrone, "Ember drone", "minion_ember_drone", false, 0.9f, 10f, 12f),
        new(MinionKind.RadSwarm, "Rad swarm", "minion_rad_swarm", false, 0.6f, 9f, 11f),
        new(MinionKind.CoralJelly, "Coral jelly", "minion_coral_jelly", false, 1.2f, 10f, 10f),
        new(MinionKind.SporeFamiliar, "Spore familiar", "minion_spore_familiar", false, 1.1f, 10f, 10f, 2),
        new(MinionKind.HiveFamiliar, "Hive familiar", "minion_hive_familiar", false, 0.7f, 11f, 14f),
        new(MinionKind.LumeniteSprite, "Lumenite sprite", "minion_lumenite_sprite", false, 0.8f, 13f, 18f),
        new(MinionKind.RadSwarmling, "Rad swarmling", "minion_rad_swarmling", false, 0.5f, 9f, 13f),
        new(MinionKind.VoidFamiliar, "Void familiar", "minion_void_familiar", false, 0.7f, 14f, 18f, 2),
        new(MinionKind.AstralGuardian, "Astral guardian", "minion_astral_guardian", false, 0.6f, 16f, 22f, 3),
        new(MinionKind.TurretAutogun, "Autogun sentry", "sentry_autogun", true, 0.5f, 14f, 18f),
        new(MinionKind.TurretFlame, "Flame sentry", "sentry_flame", true, 0.25f, 7f, 9f),
        new(MinionKind.TurretMortar, "Mortar sentry", "sentry_mortar", true, 1.6f, 20f, 12f, 1),
        new(MinionKind.TurretTesla, "Tesla sentry", "sentry_tesla", true, 0.7f, 12f, 24f, 2)
    }.ToDictionary(spec => spec.Kind);

    public static MinionSpec Get(MinionKind kind) => Table[kind];
    public static IEnumerable<MinionSpec> All => Table.Values;
}

/// <summary>A live drone or sentry owned by the player.</summary>
public sealed class MinionState
{
    internal MinionState(long id, MinionSpec spec, System.Numerics.Vector2 position, float damage, ItemId source)
    {
        Id = id;
        Spec = spec;
        Position = position;
        Damage = damage;
        SourceItem = source;
    }

    public long Id { get; }
    public MinionSpec Spec { get; }
    public MinionKind Kind => Spec.Kind;
    public bool IsSentry => Spec.IsSentry;
    /// <summary>Sprite name for the renderer (minion_* / sentry_*).</summary>
    public string SpriteName => Spec.Sprite;
    public System.Numerics.Vector2 Position { get; internal set; }
    public float Damage { get; }
    public ItemId SourceItem { get; }
    public float Cooldown { get; internal set; }
    public long? TargetId { get; internal set; }
    public float AgeSeconds { get; internal set; }
}

/// <summary>A three-piece set bonus for one class.</summary>
public sealed record ArmorSetBonus(
    string SetId,
    string DisplayName,
    DamageClass Class,
    string Description,
    int Defense = 0,
    float ClassDamageBonus = 0f,
    int CritBonus = 0,
    float ChargeMaxBonus = 0f,
    float ChargeRegenBonus = 0f,
    int DroneSlots = 0,
    int SentrySlots = 0,
    float AmmoSaveChance = 0f,
    float Lifesteal = 0f,
    float Thorns = 0f);

/// <summary>Set bonuses keyed by <see cref="ItemDefinition.ArmorSetId"/>.</summary>
public static class ArmorSets
{
    private static readonly Dictionary<string, ArmorSetBonus> Table = new(StringComparer.OrdinalIgnoreCase);

    internal static void Register(ArmorSetBonus bonus) => Table[bonus.SetId] = bonus;

    public static ArmorSetBonus? Get(string? setId) =>
        setId is not null && Table.TryGetValue(setId, out var bonus) ? bonus : null;

    public static IEnumerable<ArmorSetBonus> All => Table.Values;
}

/// <summary>Class-specific combat numbers derived from worn gear (kept apart from <see cref="PlayerStats"/>).</summary>
public readonly record struct CombatStats(
    float MeleeMultiplier,
    float RangedMultiplier,
    float TechMultiplier,
    float CommandMultiplier,
    int CritBonus,
    int DroneSlots,
    int SentrySlots,
    float ChargeMaxBonus,
    float ChargeRegenBonus,
    float AmmoSaveChance,
    float Lifesteal,
    float Thorns,
    ArmorSetBonus? ActiveSet)
{
    public float ClassMultiplier(DamageClass damageClass) => damageClass switch
    {
        DamageClass.Melee => MeleeMultiplier,
        DamageClass.Ranged => RangedMultiplier,
        DamageClass.Tech => TechMultiplier,
        DamageClass.Command => CommandMultiplier,
        _ => 1f
    };
}

public static class CombatStatsCalculator
{
    public const int BaseDroneSlots = 1;
    public const int BaseSentrySlots = 1;

    public static CombatStats Calculate(EquipmentLoadout loadout, ItemCatalog catalog)
    {
        float melee = 1f, ranged = 1f, tech = 1f, command = 1f, chargeMax = 0f, chargeRegen = 0f;
        int crit = 0, drones = BaseDroneSlots, sentries = BaseSentrySlots;
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            if (loadout[slot] is not { } id)
                continue;
            var item = catalog[id];
            melee += item.MeleeDamageBonus;
            ranged += item.RangedDamageBonus;
            tech += item.TechDamageBonus;
            command += item.CommandDamageBonus;
            crit += item.CritBonus;
            drones += item.DroneSlotBonus;
            sentries += item.SentrySlotBonus;
            chargeRegen += item.ChargeRegenBonus;
        }

        float ammoSave = 0f, lifesteal = 0f, thorns = 0f;
        ArmorSetBonus? active = null;
        var armor = new[] { loadout[EquipmentSlot.Head], loadout[EquipmentSlot.Body], loadout[EquipmentSlot.Legs] };
        if (armor.All(id => id.HasValue))
        {
            var sets = armor.Select(id => catalog[id!.Value].ArmorSetId).Distinct().ToArray();
            if (sets.Length == 1 && ArmorSets.Get(sets[0]) is { } bonus)
            {
                active = bonus;
                switch (bonus.Class)
                {
                    case DamageClass.Melee: melee += bonus.ClassDamageBonus; break;
                    case DamageClass.Ranged: ranged += bonus.ClassDamageBonus; break;
                    case DamageClass.Tech: tech += bonus.ClassDamageBonus; break;
                    case DamageClass.Command: command += bonus.ClassDamageBonus; break;
                }
                crit += bonus.CritBonus;
                drones += bonus.DroneSlots;
                sentries += bonus.SentrySlots;
                chargeMax += bonus.ChargeMaxBonus;
                chargeRegen += bonus.ChargeRegenBonus;
                ammoSave += bonus.AmmoSaveChance;
                lifesteal += bonus.Lifesteal;
                thorns += bonus.Thorns;
            }
        }
        return new CombatStats(melee, ranged, tech, command, crit, drones, sentries, chargeMax, chargeRegen,
            Math.Clamp(ammoSave, 0f, 0.9f), lifesteal, thorns, active);
    }

    /// <summary>Skill multiplier for a class: melee/shooting from the existing sheet, Tech from Intellectual, Command from Social.</summary>
    public static float SkillMultiplier(ColonistStats stats, DamageClass damageClass) => damageClass switch
    {
        DamageClass.Melee => stats.MeleeDamageMultiplier,
        DamageClass.Ranged => stats.ShootingDamageMultiplier,
        DamageClass.Tech => 1f + stats.Level(ColonistSkill.Intellectual) * 0.025f,
        DamageClass.Command => 1f + stats.Level(ColonistSkill.Social) * 0.025f,
        _ => 1f
    };
}

/// <summary>
/// Combat, health and spawning feel (MECHANICS_PARITY.md section C). Every number here is Rimtera's own tuning;
/// the shapes (small damage spread, doubling crits, resisted knockback, regeneration that builds while unhurt,
/// a charge delay after spending, safe spawn radius, busier nights and quieter towns) are the contract.
/// </summary>
public static class CombatTuning
{
    // ================================================================== stat formulas (docs/GEAR_DIFF_MECHANICS.md)
    // Each rule below follows the behaviour measured in the reference game's IL (see the doc for citations).
    // The code and the names are Rimtera's own.

    // ---- outgoing damage ----
    /// <summary>Weapon damage varies by up to this many whole percent either way on every hit.</summary>
    public const int DamageSpreadPercent = 15;
    /// <summary>Weapon damage varies by up to this fraction either way on every hit.</summary>
    public const float DamageSpread = DamageSpreadPercent / 100f;
    public const float CritMultiplier = 2f;
    /// <summary>Every Melee, Ranged and Tech weapon starts with this crit chance (percent); Command weapons never crit.</summary>
    public const int BaseCritChance = 4;

    /// <summary>
    /// Damage of one use before the hit roll: the calibration's damage factor is baked into the weapon's base damage
    /// (rounded to whole points), ammunition adds its damage, then the class multiplier (1 + every additive class
    /// bonus, times the few multiplicative penalties) scales the sum. Rimtera keeps the fraction here (the reference
    /// truncates); the hit roll rounds to whole points, so the difference is under one point before the spread.
    /// </summary>
    public static float WeaponDamage(int baseDamage, float calibrationMultiplier, int ammoDamage, float classMultiplier)
    {
        if (baseDamage <= 0)
            return baseDamage;
        var calibrated = (int)MathF.Round(baseDamage * calibrationMultiplier, MidpointRounding.ToEven);
        return (calibrated + ammoDamage) * classMultiplier;
    }

    /// <summary>
    /// One hit's spread: a whole-percent roll in [−15, +15], rounded to whole points. Positive luck gives a second
    /// roll (with chance = luck) and keeps the higher; negative luck a second roll (chance = −luck) keeping the lower.
    /// </summary>
    public static float RollVariance(float damage, Random random, float luck = 0f)
    {
        ArgumentNullException.ThrowIfNull(random);
        float Roll() => damage * (1f + random.Next(-DamageSpreadPercent, DamageSpreadPercent + 1) * 0.01f);
        var result = Roll();
        if (luck > 0f && random.NextSingle() < luck)
            result = Math.Max(result, Roll());
        else if (luck < 0f && random.NextSingle() < -luck)
            result = Math.Min(result, Roll());
        return MathF.Round(result, MidpointRounding.ToEven);
    }

    /// <summary>Defense ignored by armor penetration: half of the smaller of penetration and defense (whole points).</summary>
    public static int ArmorPenetrationGiveBack(int armorPenetration, int defense) =>
        armorPenetration <= 0 || defense <= 0 ? 0 : Math.Min(armorPenetration, defense) / 2;

    /// <summary>
    /// Damage a creature takes from one player hit: the penetration give-back is added, half the creature's defense
    /// comes off (never below 1), then a crit doubles it; at least 1 point always lands.
    /// </summary>
    public static float CreatureDamageTaken(float damage, int defense, int armorPenetration, bool critical)
    {
        if (damage <= 0f)
            return 0f;
        var raw = damage + ArmorPenetrationGiveBack(armorPenetration, defense);
        var afterDefense = Math.Max(1f, raw - Math.Max(0, defense) * 0.5f);
        return (int)Math.Max(1f, afterDefense * (critical ? CritMultiplier : 1f));
    }

    /// <summary>Crit chance of a weapon: Command weapons never crit; every other class starts at <see cref="BaseCritChance"/>.</summary>
    public static int WeaponCritChance(DamageClass damageClass, int weaponCrit, int bonuses) =>
        damageClass is DamageClass.Command ? 0 : Math.Max(0, BaseCritChance + weaponCrit + bonuses);

    /// <summary>A crit roll: a uniform whole number 1-100 at or under the chance.</summary>
    public static bool RollCrit(int critChance, Random random) => critChance > 0 && random.Next(1, 101) <= critChance;

    // ---- knockback ----
    /// <summary>
    /// Knockback a creature takes: the share its resistance lets through, then soft caps (the part above 8, 10, 12 and
    /// 14 is kept at 90%, 80%, 70% and 60%), a hard cap of 16, and ×1.4 on a crit (after the caps).
    /// </summary>
    public static float KnockbackTaken(float knockback, float resistance, bool critical)
    {
        var k = knockback * (1f - Math.Clamp(resistance, 0f, 1f));
        if (k <= 0f)
            return 0f;
        if (k > 8f) k = 8f + (k - 8f) * 0.9f;
        if (k > 10f) k = 10f + (k - 10f) * 0.8f;
        if (k > 12f) k = 12f + (k - 12f) * 0.7f;
        if (k > 14f) k = 14f + (k - 14f) * 0.6f;
        if (k > 16f) k = 16f;
        return critical ? k * 1.4f : k;
    }

    // ---- speeds ----
    /// <summary>Attack speed is capped at 3×.</summary>
    public const float MaximumAttackSpeed = 3f;

    /// <summary>Turns an attack-speed stat (1 = normal, 1.25 = 25% faster) into a use-time multiplier: 1 / min(speed, 3).</summary>
    public static float UseTimeMultiplier(float attackSpeed)
    {
        var speed = Math.Min(attackSpeed, MaximumAttackSpeed);
        return speed <= 0f ? 0f : 1f / speed;
    }

    /// <summary>The calibration's use-time factor is baked in once, rounded to whole ticks.</summary>
    public static int CalibratedUseTicks(int ticks, float calibrationMultiplier) =>
        (int)MathF.Round(ticks * calibrationMultiplier, MidpointRounding.ToEven);

    /// <summary>Melee swing time: max(1, (int)(calibrated ticks × melee use-time multiplier)).</summary>
    public static int MeleeUseTicks(int calibratedTicks, float meleeUseTimeMultiplier) =>
        Math.Max(1, (int)(calibratedTicks * meleeUseTimeMultiplier));

    /// <summary>Lowest mining-speed multiplier (70% faster swings at most).</summary>
    public const float MinimumPickSpeed = 0.3f;

    /// <summary>Mining-speed multiplier from "+N% mining speed" bonuses: 1 − N/100, floored at 0.3.</summary>
    public static float PickSpeed(float miningBonusPercent) => Math.Max(MinimumPickSpeed, 1f - miningBonusPercent / 100f);

    /// <summary>A drill's swing time: (int)(use ticks × pick speed), at least 1.</summary>
    public static int DrillUseTicks(int useTicks, float pickSpeed) => Math.Max(1, (int)(useTicks * pickSpeed));

    /// <summary>Block or wall placement time: (int)(ticks / min(1 + bonus, 3)), at least 1.</summary>
    public static int PlacementTicks(int ticks, float buildBonusPercent) =>
        Math.Max(1, (int)(ticks * UseTimeMultiplier(1f + buildBonusPercent / 100f)));

    // ---- mining ----
    /// <summary>Accumulated hit damage that breaks a block, a tree segment or a wall.</summary>
    public const int TileBreakDamage = 100;
    /// <summary>Drill hardness classes: power ×2 on soft ground, ×1 on stone, ÷2..÷5 on the toughest blocks.</summary>
    public const int HardnessSoft = 0;
    public const int HardnessFragile = -1;

    /// <summary>Blocks whose mining resistance is at most this count as soft ground (drill power counts twice).</summary>
    public const float SoftGroundResistance = 1f;

    /// <summary>
    /// Drill hardness of a block: −1 fragile (one hit), 0 soft ground (power ×2), 1 stone (power ×1), 2..5 (power ÷ N).
    /// Named blocks follow their role in the progression (evil stone, derelict plating and the hellstone-like ore at ÷2,
    /// the three hardmode ores at ÷2/÷3/÷4, precursor brick at ÷4); everything else is soft when its resistance is
    /// at most <see cref="SoftGroundResistance"/>, otherwise stone.
    /// </summary>
    public static int DrillHardness(Rimtera.World.TileId tile, float resistance, bool fragile)
    {
        if (fragile)
            return HardnessFragile;
        switch (tile)
        {
            case Rimtera.World.TileId.CorruptRock:
            case Rimtera.World.TileId.PearlStone:
            case Rimtera.World.TileId.DungeonBrick:
            case Rimtera.World.TileId.PyrociteOre:
            case Rimtera.World.TileId.ViridiumOre:
                return 2;
            case Rimtera.World.TileId.LumeniteOre:
                return 3;
            case Rimtera.World.TileId.VoidsteelOre:
            case Rimtera.World.TileId.PrecursorBrick:
            case Rimtera.World.TileId.PrecursorGlyph:
                return 4;
        }
        return resistance <= SoftGroundResistance ? HardnessSoft : 1;
    }

    /// <summary>
    /// Damage one drill hit adds to a block (out of <see cref="TileBreakDamage"/>): power ×2 on soft ground, ×1 on stone,
    /// power ÷ N (whole-number division) on harder blocks, a full break on fragile ones. Below the block's minimum
    /// drill power nothing happens.
    /// </summary>
    public static int DrillDamage(int toolPower, int hardness, int minimumToolPower)
    {
        if (toolPower <= 0 || toolPower < minimumToolPower)
            return 0;
        return hardness switch
        {
            HardnessFragile => TileBreakDamage,
            HardnessSoft => toolPower * 2,
            _ => toolPower / Math.Max(1, hardness)
        };
    }

    /// <summary>
    /// Damage one cutter hit adds to a tree segment or other organic block: (int)(power / 5 × 1.2), tripled on cactus.
    /// Cutter power reads like an axe percentage, so the stored axe value is power / 5.
    /// </summary>
    public static int CutterDamage(int toolPower, bool cactus = false)
    {
        var axe = toolPower / 5;
        return cactus ? (int)(axe * 3 * 1.2f) : (int)(axe * 1.2f);
    }

    /// <summary>Damage one demolisher hit adds to a background wall: (int)(power × 1.5).</summary>
    public static int WallHammerDamage(int toolPower) => (int)(Math.Max(0, toolPower) * 1.5f);

    /// <summary>Horizontal tile reach past the edge of the player's body, before bonuses.</summary>
    public const int TileReachX = 5;
    /// <summary>Vertical tile reach past the edge of the player's body, before bonuses.</summary>
    public const int TileReachY = 3;

    /// <summary>
    /// Tile reach is a box: the target cell must lie within <see cref="TileReachX"/> + bonus cells of the leftmost/rightmost
    /// cell the body touches, and <see cref="TileReachY"/> + bonus cells of the top/bottom cell.
    /// </summary>
    public static bool InTileReach(float bodyLeft, float bodyTop, float bodyWidth, float bodyHeight, int targetX, int targetY, int bonus)
    {
        var left = (int)MathF.Floor(bodyLeft);
        var right = (int)MathF.Floor(bodyLeft + bodyWidth - 0.001f);
        var top = (int)MathF.Floor(bodyTop);
        var bottom = (int)MathF.Floor(bodyTop + bodyHeight - 0.001f);
        var rx = TileReachX + bonus;
        var ry = TileReachY + bonus;
        return targetX >= left - rx && targetX <= right + rx && targetY >= top - ry && targetY <= bottom + ry;
    }

    /// <summary>
    /// Fraction of incoming knockback a creature ignores: the archetype's own <c>KnockbackResist</c> when set,
    /// otherwise wildlife none, heavier and bigger creatures more, rooted ones and late bosses all of it, early bosses
    /// nearly all.
    /// </summary>
    public static float KnockbackResistance(Rimtera.Entities.EnemyArchetype archetype)
    {
        ArgumentNullException.ThrowIfNull(archetype);
        if (archetype.KnockbackResist is { } resist)
            return Math.Clamp(resist, 0f, 1f);
        if (archetype.Stationary)
            return 1f;
        if (archetype.IsBoss)
            return archetype.Tier switch
            {
                Rimtera.Entities.BossTier.Gate => 0.85f,
                Rimtera.Entities.BossTier.Easy or Rimtera.Entities.BossTier.Event => 0.92f,
                _ => 1f
            };
        if (archetype.Passive)
            return 0f;
        var bulk = Math.Clamp((archetype.HitPoints - 40f) / 500f, 0f, 0.5f);
        var size = archetype.Size.X * archetype.Size.Y > 2.4f ? 0.15f : 0f;
        var worm = archetype.Family == Rimtera.Entities.EnemyAiFamily.Worm ? 0.35f : 0f;
        return Math.Clamp(bulk + size + worm, 0f, 0.9f);
    }

    // ---- life regeneration ----
    /// <summary>Simulation ticks per second (regeneration timers count in ticks).</summary>
    public const float TicksPerSecond = 60f;
    /// <summary>Ticks unhurt at which each step of natural regeneration switches on (5 s, 10 s ... 50 s).</summary>
    public static readonly int[] LifeRegenThresholdTicks = [300, 600, 900, 1200, 1500, 1800, 2400, 3000];
    /// <summary>The ninth and last step, after which the unhurt timer stops growing.</summary>
    public const int LifeRegenCapTicks = 3600;

    /// <summary>
    /// Natural regeneration in regen units (1 unit = half a hit point per second). The number of steps reached by
    /// <paramref name="ticksUnhurt"/> (0-9), ×1.3 sleeping, ×1.25 standing still (×0.5 moving), halved on Brutal and
    /// Annihilation unless well fed, then scaled by maxLife/400 × 0.85 + 0.15 and rounded.
    /// </summary>
    public static int NaturalLifeRegenUnits(float ticksUnhurt, float maximumLife, bool standingStill, bool sleeping, bool harshWorld, bool wellFed)
    {
        float r = 0f;
        foreach (var threshold in LifeRegenThresholdTicks)
            if (ticksUnhurt >= threshold)
                r += 1f;
        if (ticksUnhurt >= LifeRegenCapTicks)
            r += 1f;
        if (sleeping)
            r *= 1.3f;
        r *= standingStill ? 1.25f : 0.5f;
        if (harshWorld && !wellFed)
            r /= 2f;
        r *= maximumLife / 400f * 0.85f + 0.15f;
        return (int)MathF.Round(r, MidpointRounding.ToEven);
    }

    /// <summary>Hit points per second for a number of regen units (each unit is half a point per second).</summary>
    public static float RegenUnitsPerSecond(float units) => units * 0.5f;

    /// <summary>Natural hit points per second (see <see cref="NaturalLifeRegenUnits"/>).</summary>
    public static float NaturalLifeRegen(float secondsUnhurt, float maximumLife, bool standingStill, bool sleeping = false,
        bool harshWorld = false, bool wellFed = false) =>
        RegenUnitsPerSecond(NaturalLifeRegenUnits(secondsUnhurt * TicksPerSecond, maximumLife, standingStill, sleeping, harshWorld, wellFed));

    // ---- charge (the mana analog) ----
    /// <summary>Maximum Charge is capped here, whatever the crystals, gear and buffs add.</summary>
    public const float MaximumChargeCap = 400f;
    /// <summary>Seconds of charge sickness after drinking a charge restorative.</summary>
    public const float ChargeSicknessSeconds = 6f;
    /// <summary>Tech damage lost at the start of charge sickness (fades out with it).</summary>
    public const float ChargeSicknessPenalty = 0.25f;

    /// <summary>
    /// Ticks of no regeneration after spending charge: (int)(((1 − charge/max) × 240 + 45) × 0.7), at most 20 with a
    /// charge-regeneration buff. Spending from nearly full waits about half a second, from empty about 3.3 s.
    /// </summary>
    public static float ChargeRegenDelayTicks(float charge, float maximum, bool regenBuff)
    {
        var fraction = maximum > 0f ? Math.Clamp(charge / maximum, 0f, 1f) : 1f;
        float delay = (int)(((1f - fraction) * TicksPerSecond * 4f + 45f) * 0.7f);
        return regenBuff ? Math.Min(delay, 20f) : delay;
    }

    /// <summary>The delay counts down 1 per tick, plus 1 more while standing still or with the regeneration buff.</summary>
    public static float ChargeDelayCountdownPerTick(bool standingStill, bool regenBuff) => standingStill || regenBuff ? 2f : 1f;

    /// <summary>
    /// Charge regeneration in regen units (1 unit = half a point per second) once the delay is over:
    /// (int)(max/3) + bonus + 1, doubled standing still or with the buff, then × (0.5 + 0.5 × charge/max) (× 1 with the
    /// buff), truncated, at least 2. <paramref name="bonusUnits"/> is gear and status regeneration in the same units.
    /// </summary>
    public static int ChargeRegenUnits(float charge, float maximum, float bonusUnits, bool standingStill, bool regenBuff)
    {
        var baseRate = (int)(maximum / 3f) + bonusUnits + 1f;
        var rate = standingStill || regenBuff ? baseRate * 2f : baseRate;
        var fraction = maximum > 0f ? Math.Clamp(charge / maximum, 0f, 1f) : 1f;
        var scale = regenBuff ? 1f : 0.5f + 0.5f * fraction;
        return Math.Max(2, (int)(rate * scale));
    }

    /// <summary>
    /// Charge one use costs: the calibration's cost factor is baked in (rounded), then the wearer's charge-cost
    /// multiplier scales it and the result is truncated.
    /// </summary>
    public static int ChargeCost(int baseCost, float calibrationMultiplier, float wearerCostMultiplier)
    {
        if (baseCost <= 0)
            return 0;
        var calibrated = (int)MathF.Round(baseCost * calibrationMultiplier, MidpointRounding.ToEven);
        return (int)(calibrated * Math.Max(0f, wearerCostMultiplier));
    }

    // ---- spawning ----
    /// <summary>Town NPCs within this many tiles make the player's area calmer.</summary>
    public const float TownCalmRadius = 45f;
    // Despawn numbers for EN-9 (0.1.10), D-21 / EN-18. Terraria 1.4.4, NPC..cctor and NPC.CheckActive (from memory;
    // not re-measured from the IL):
    //   NPC.sWidth = 1920, NPC.sHeight = 1080 (pixels; 16 per tile)
    //   NPC.activeTime = 750 ticks (12.5 s): timeLeft is reset to it while any player is within the active range, and
    //     counts down one per tick outside it; at 0 the NPC despawns silently.
    //   NPC.activeRangeX = (int)(sWidth * 2.1) = 4032 px = 252 tiles, activeRangeY = (int)(sHeight * 2.1) = 2268 px
    //     = 141.75 tiles (a box around the player, half-extents).
    //   NPC.spawnRangeX = (int)(sWidth / 16 * 0.7) = 84 tiles, spawnRangeY = (int)(sHeight / 16 * 0.7) = 47 tiles.
    //   NPC.safeRangeX = (int)(sWidth / 16 * 0.52) = 62 tiles, safeRangeY = (int)(sHeight / 16 * 0.52) = 35 tiles.
    // Rimtera's own rule below is unchanged until EN-9 adopts these.
    /// <summary>Creatures this far (tiles) from the player for <see cref="DespawnSeconds"/> wander off.</summary>
    public const float DespawnDistance = 48f;
    public const float DespawnSeconds = 3f;

    /// <summary>
    /// Spawn rate and population-cap multipliers for the player's surroundings: nights, the underground and the
    /// blight are busier; a populated town and a living boss calm things down.
    /// </summary>
    public static (float Rate, float Cap) SpawnFactors(bool night, bool underground, bool blight, int townNpcsNearby, bool bossAlive)
    {
        float rate = 1f, cap = 1f;
        if (night && !underground)
        {
            rate *= 1.7f;
            cap *= 1.5f;
        }
        if (underground)
        {
            rate *= 1.45f;
            cap *= 1.35f;
        }
        if (blight)
        {
            rate *= 1.3f;
            cap *= 1.25f;
        }
        if (townNpcsNearby > 0)
        {
            var calm = townNpcsNearby >= 3 ? 0.12f : 1f / (1f + 0.6f * townNpcsNearby);
            rate *= calm;
            cap *= Math.Max(0.3f, calm);
        }
        if (bossAlive)
        {
            rate *= 0.35f;
            cap *= 0.6f;
        }
        return (rate, cap);
    }
}
