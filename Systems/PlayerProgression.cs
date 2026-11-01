using Rimtera.Characters;

namespace Rimtera.Systems;

public enum EquipmentSlot : byte { Head, Body, Legs, Accessory, Accessory2, Accessory3, Accessory4, Accessory5,
    /// <summary>The sixth accessory slot: usable only once unlocked (auxiliary socket or an Annihilation world).</summary>
    Accessory6 }
public enum VanitySlot : byte { Head, Body, Legs }
public enum StatusId : byte
{
    Regeneration, Poison, Burning, Guarded, ManaFocus, FrostGuard, Haste, Armored, Spelunker, ChargeSurge,
    // Batch B (append-only): potion sickness blocks healing items; featherfall negates fall damage;
    // gills stop the breath meter draining; phase strain follows a phase-rod jump.
    PotionSickness, Featherfall, Gills, PhaseStrain,
    // Arsenal pass (append-only): class tonics, utility potions and well-fed tiers. Numeric effects ride on
    // "name:value" tags read by GameSimulation.Gear (damage, crit, speed, mining, light, dodge, thorns, ...).
    CloseQuartersBoost, BallisticsBoost, TechBoost, CommandBoost, Precision, Fortified, Vitality, Rejuvenation, Sprint,
    MiningBoost, Illuminated, Angler, WaterWalking, Thornskin, Endurance, AmmoReserve, Wrath, DroneLink, SentryLink,
    Overcharge, Builder, Magnetism, Fireproof, DangerSense, Lifeforce, Inertia, Evasion, NightSight, WellFed, Satiated, Gourmet,
    // World generation completion (append-only): bathing in the phase pool lets the body sink through floors.
    Phased,
    // Player & combat systems (append-only): creature afflictions applied by weapons and ammo (frostbite slows,
    // armour shred raises damage taken, confusion scrambles movement, marked draws focus) and the fortune tonic's luck.
    Frostbite, ArmorShred, Confused, Marked, Lucky,
    // Combat core (0.1.9, append-only): creature-inflicted debuffs on the player (IC-3) and the extra creature
    // afflictions weapons and ammo carry (EN-17): corroded strips defense, the salvage mark adds credits on the kill,
    // and wet puts burning out.
    Bleeding, Chilled, Slow, Darkness, Weak, BrokenArmor, Silenced, Venom, Frostburn, CursedFire, Corroded, SalvageMark, Wet,
    // Spawn controls (0.3.4, append-only): the battle stim draws more creatures, the calming stim fewer.
    Battle, Calm
}

public readonly record struct PlayerStats(
    float MaximumLife,
    float MaximumMana,
    int Defense,
    float LifeRegenerationPerHour,
    float ManaRegenerationPerHour,
    float DamageMultiplier,
    float KnockbackMultiplier,
    float MoveSpeedMultiplier,
    IReadOnlySet<string> ImmunityTags);

public sealed record EquipmentSnapshot(
    ItemId? Head,
    ItemId? Body,
    ItemId? Legs,
    ItemId? Accessory,
    ItemId? VanityHead,
    ItemId? VanityBody,
    ItemId? VanityLegs,
    ItemId? Accessory2 = null,
    ItemId? Accessory3 = null,
    ItemId? Accessory4 = null,
    ItemId? Accessory5 = null,
    SlotCalibration[]? Calibrations = null,
    ItemId? Accessory6 = null);

/// <summary>A calibration rolled onto the accessory worn in a slot (kept with saves, loadouts and character files).</summary>
public readonly record struct SlotCalibration(EquipmentSlot Slot, ItemId Item, Calibration Calibration);

/// <summary>Owns functional gear and cosmetic overrides independently.</summary>
public sealed class EquipmentLoadout
{
    /// <summary>The five accessory slots, in fill order.</summary>
    public static readonly IReadOnlyList<EquipmentSlot> AccessorySlots =
        [EquipmentSlot.Accessory, EquipmentSlot.Accessory2, EquipmentSlot.Accessory3, EquipmentSlot.Accessory4, EquipmentSlot.Accessory5];

    /// <summary>All six accessory slots (the sixth is gated by <see cref="Slot6Gate"/>).</summary>
    public static readonly IReadOnlyList<EquipmentSlot> AllAccessorySlots =
        [EquipmentSlot.Accessory, EquipmentSlot.Accessory2, EquipmentSlot.Accessory3, EquipmentSlot.Accessory4, EquipmentSlot.Accessory5,
         EquipmentSlot.Accessory6];

    /// <summary>Says whether the sixth accessory slot is open (set by the simulation; closed when null).</summary>
    public Func<bool>? Slot6Gate { get; set; }
    public bool Slot6Open => Slot6Gate?.Invoke() == true;
    /// <summary>The accessory slots usable right now, in fill order.</summary>
    public IReadOnlyList<EquipmentSlot> ActiveAccessorySlots => Slot6Open ? AllAccessorySlots : AccessorySlots;
    /// <summary>Gear-independent bonuses the wearer carries (lifebloom pods, permanent upgrades); set by the simulation.</summary>
    public Func<PermanentStatBonus>? PermanentBonuses { get; set; }

    private readonly Dictionary<EquipmentSlot, ItemId?> _functional = Enum.GetValues<EquipmentSlot>()
        .ToDictionary(slot => slot, _ => (ItemId?)null);
    private readonly Dictionary<VanitySlot, ItemId?> _vanity = Enum.GetValues<VanitySlot>()
        .ToDictionary(slot => slot, _ => (ItemId?)null);

    public ItemId? this[EquipmentSlot slot] => _functional[slot];
    public ItemId? this[VanitySlot slot] => _vanity[slot];

    internal ItemId? Set(EquipmentSlot slot, ItemId? item) => Swap(_functional, slot, item);
    internal ItemId? Set(VanitySlot slot, ItemId? item) => Swap(_vanity, slot, item);

    public EquipmentSnapshot Capture() => new(
        this[EquipmentSlot.Head], this[EquipmentSlot.Body], this[EquipmentSlot.Legs], this[EquipmentSlot.Accessory],
        this[VanitySlot.Head], this[VanitySlot.Body], this[VanitySlot.Legs],
        this[EquipmentSlot.Accessory2], this[EquipmentSlot.Accessory3], this[EquipmentSlot.Accessory4], this[EquipmentSlot.Accessory5],
        Accessory6: this[EquipmentSlot.Accessory6]);

    public bool Restore(EquipmentSnapshot snapshot, ItemCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(catalog);
        if (!Valid(snapshot.Head, EquipmentSlot.Head, vanity: false) ||
            !Valid(snapshot.Body, EquipmentSlot.Body, vanity: false) ||
            !Valid(snapshot.Legs, EquipmentSlot.Legs, vanity: false) ||
            !Valid(snapshot.Accessory, EquipmentSlot.Accessory, vanity: false) ||
            !Valid(snapshot.VanityHead, EquipmentSlot.Head, vanity: true) ||
            !Valid(snapshot.VanityBody, EquipmentSlot.Body, vanity: true) ||
            !Valid(snapshot.VanityLegs, EquipmentSlot.Legs, vanity: true) ||
            !Valid(snapshot.Accessory2, EquipmentSlot.Accessory, vanity: false) ||
            !Valid(snapshot.Accessory3, EquipmentSlot.Accessory, vanity: false) ||
            !Valid(snapshot.Accessory4, EquipmentSlot.Accessory, vanity: false) ||
            !Valid(snapshot.Accessory5, EquipmentSlot.Accessory, vanity: false) ||
            !Valid(snapshot.Accessory6, EquipmentSlot.Accessory, vanity: false))
            return false;

        _functional[EquipmentSlot.Head] = snapshot.Head;
        _functional[EquipmentSlot.Body] = snapshot.Body;
        _functional[EquipmentSlot.Legs] = snapshot.Legs;
        _functional[EquipmentSlot.Accessory] = snapshot.Accessory;
        _functional[EquipmentSlot.Accessory2] = snapshot.Accessory2;
        _functional[EquipmentSlot.Accessory3] = snapshot.Accessory3;
        _functional[EquipmentSlot.Accessory4] = snapshot.Accessory4;
        _functional[EquipmentSlot.Accessory5] = snapshot.Accessory5;
        _functional[EquipmentSlot.Accessory6] = snapshot.Accessory6;
        _vanity[VanitySlot.Head] = snapshot.VanityHead;
        _vanity[VanitySlot.Body] = snapshot.VanityBody;
        _vanity[VanitySlot.Legs] = snapshot.VanityLegs;
        return true;

        bool Valid(ItemId? id, EquipmentSlot slot, bool vanity) => id is null ||
            (Enum.IsDefined(id.Value) && catalog.Contains(id.Value) &&
             catalog[id.Value].EquipmentSlot == slot && catalog[id.Value].IsVanity == vanity);
    }

    private static ItemId? Swap<TKey>(Dictionary<TKey, ItemId?> map, TKey key, ItemId? value) where TKey : notnull
    {
        var previous = map[key];
        map[key] = value;
        return previous;
    }
}

public readonly record struct StatusSnapshot(StatusId Id, float RemainingHours, float TickProgressHours);

public sealed record StatusDefinition(
    StatusId Id,
    float MaximumDurationHours,
    float TickIntervalHours = 0f,
    float DamagePerTick = 0f,
    float LifeRegenerationPerHour = 0f,
    float ManaRegenerationPerHour = 0f,
    int DefenseBonus = 0,
    float MaximumLifeBonus = 0f,
    float MaximumManaBonus = 0f,
    IReadOnlySet<string>? ImmunityTags = null,
    string? BlockedByImmunityTag = null);

/// <summary>Definitions and duration/tick state for project-authored buffs and debuffs.</summary>
public sealed class StatusEffectSystem
{
    private readonly Dictionary<StatusId, StatusDefinition> _definitions = new()
    {
        [StatusId.Regeneration] = new(StatusId.Regeneration, 8f, LifeRegenerationPerHour: 0.4f),
        [StatusId.Poison] = new(StatusId.Poison, 6f, TickIntervalHours: 0.5f, DamagePerTick: 1.25f, BlockedByImmunityTag: "poison"),
        [StatusId.Burning] = new(StatusId.Burning, 4f, TickIntervalHours: 0.25f, DamagePerTick: 1f, BlockedByImmunityTag: "heat"),
        [StatusId.Guarded] = new(StatusId.Guarded, 5f, DefenseBonus: 4),
        [StatusId.ManaFocus] = new(StatusId.ManaFocus, 8f, ManaRegenerationPerHour: 1.25f),
        [StatusId.FrostGuard] = new(StatusId.FrostGuard, 10f,
            ImmunityTags: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cold" }),
        // Stims (content expansion): haste is read by the movement code, charge surge by the charge regen.
        [StatusId.Haste] = new(StatusId.Haste, 4f),
        [StatusId.Armored] = new(StatusId.Armored, 4f, DefenseBonus: 8),
        [StatusId.Spelunker] = new(StatusId.Spelunker, 6f),
        [StatusId.ChargeSurge] = new(StatusId.ChargeSurge, 4f, ManaRegenerationPerHour: 2f),
        [StatusId.PotionSickness] = new(StatusId.PotionSickness, 8f),
        [StatusId.Featherfall] = new(StatusId.Featherfall, 6f,
            ImmunityTags: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "featherfall" }),
        [StatusId.Gills] = new(StatusId.Gills, 8f,
            ImmunityTags: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "gills" }),
        [StatusId.PhaseStrain] = new(StatusId.PhaseStrain, 2f),
        [StatusId.Phased] = new(StatusId.Phased, 1f),
        // Arsenal pass.
        [StatusId.CloseQuartersBoost] = new(StatusId.CloseQuartersBoost, 10f, ImmunityTags: Tags("dmg-melee:10")),
        [StatusId.BallisticsBoost] = new(StatusId.BallisticsBoost, 10f, ImmunityTags: Tags("dmg-ranged:10")),
        [StatusId.TechBoost] = new(StatusId.TechBoost, 10f, ImmunityTags: Tags("dmg-tech:10")),
        [StatusId.CommandBoost] = new(StatusId.CommandBoost, 10f, ImmunityTags: Tags("dmg-command:10")),
        [StatusId.Precision] = new(StatusId.Precision, 10f, ImmunityTags: Tags("crit:10")),
        [StatusId.Fortified] = new(StatusId.Fortified, 10f, DefenseBonus: 10),
        [StatusId.Vitality] = new(StatusId.Vitality, 10f, LifeRegenerationPerHour: 0.15f, MaximumLifeBonus: 30f),
        [StatusId.Rejuvenation] = new(StatusId.Rejuvenation, 6f, LifeRegenerationPerHour: 0.9f),
        [StatusId.Sprint] = new(StatusId.Sprint, 8f, ImmunityTags: Tags("speed:25")),
        [StatusId.MiningBoost] = new(StatusId.MiningBoost, 10f, ImmunityTags: Tags("mining:25")),
        [StatusId.Illuminated] = new(StatusId.Illuminated, 12f, ImmunityTags: Tags("light:8")),
        [StatusId.Angler] = new(StatusId.Angler, 12f, ImmunityTags: Tags("fish:15")),
        [StatusId.WaterWalking] = new(StatusId.WaterWalking, 10f, ImmunityTags: Tags("water-walk")),
        [StatusId.Thornskin] = new(StatusId.Thornskin, 10f, ImmunityTags: Tags("thorns:30")),
        [StatusId.Endurance] = new(StatusId.Endurance, 10f, ImmunityTags: Tags("endurance:10")),
        [StatusId.AmmoReserve] = new(StatusId.AmmoReserve, 10f, ImmunityTags: Tags("ammosave:20")),
        [StatusId.Wrath] = new(StatusId.Wrath, 10f, ImmunityTags: Tags("dmg:10")),
        [StatusId.DroneLink] = new(StatusId.DroneLink, 10f, ImmunityTags: Tags("drone:1")),
        [StatusId.SentryLink] = new(StatusId.SentryLink, 10f, ImmunityTags: Tags("sentry:1")),
        [StatusId.Overcharge] = new(StatusId.Overcharge, 10f, ManaRegenerationPerHour: 1.5f, MaximumManaBonus: 40f),
        [StatusId.Builder] = new(StatusId.Builder, 12f, ImmunityTags: Tags("reach:3")),
        [StatusId.Magnetism] = new(StatusId.Magnetism, 12f, ImmunityTags: Tags("magnet")),
        [StatusId.Fireproof] = new(StatusId.Fireproof, 10f, ImmunityTags: Tags("lava", "heat", "fire-block")),
        [StatusId.DangerSense] = new(StatusId.DangerSense, 10f, ImmunityTags: Tags("danger-sense")),
        [StatusId.Lifeforce] = new(StatusId.Lifeforce, 8f, MaximumLifeBonus: 80f),
        [StatusId.Inertia] = new(StatusId.Inertia, 10f, ImmunityTags: Tags("knockback-immune")),
        [StatusId.Evasion] = new(StatusId.Evasion, 8f, ImmunityTags: Tags("dodge:10")),
        [StatusId.NightSight] = new(StatusId.NightSight, 12f, ImmunityTags: Tags("light:5", "night-vision", "dark")),
        [StatusId.WellFed] = new(StatusId.WellFed, 12f, LifeRegenerationPerHour: 0.1f, DefenseBonus: 1, ImmunityTags: Tags("dmg:3", "crit:2", "speed:5")),
        [StatusId.Satiated] = new(StatusId.Satiated, 12f, LifeRegenerationPerHour: 0.2f, DefenseBonus: 2, ImmunityTags: Tags("dmg:5", "crit:3", "speed:8")),
        [StatusId.Gourmet] = new(StatusId.Gourmet, 12f, LifeRegenerationPerHour: 0.3f, DefenseBonus: 4, ImmunityTags: Tags("dmg:8", "crit:4", "speed:10")),
        // Player & combat systems: creature afflictions (ticked on creatures by GameSimulation.PlayerSystems) and luck.
        [StatusId.Frostbite] = new(StatusId.Frostbite, 2f), [StatusId.ArmorShred] = new(StatusId.ArmorShred, 2f),
        [StatusId.Confused] = new(StatusId.Confused, DebuffCapHours, BlockedByImmunityTag: "confused"), [StatusId.Marked] = new(StatusId.Marked, 2f),
        [StatusId.Lucky] = new(StatusId.Lucky, 12f),
        // Combat core (IC-3): creature-inflicted debuffs. The movement, light, damage and use effects are read by
        // GameSimulation.Debuffs; damage over time is Terraria's lifeRegen units (-N units = N/2 hit points a second)
        // paid every half second. Each is blocked by the immunity tag of its matching accessory.
        [StatusId.Bleeding] = new(StatusId.Bleeding, DebuffCapHours, BlockedByImmunityTag: "bleeding"),
        [StatusId.Chilled] = new(StatusId.Chilled, DebuffCapHours, BlockedByImmunityTag: "cold"),
        [StatusId.Slow] = new(StatusId.Slow, DebuffCapHours, BlockedByImmunityTag: "slow"),
        [StatusId.Darkness] = new(StatusId.Darkness, DebuffCapHours, BlockedByImmunityTag: "darkness"),
        [StatusId.Weak] = new(StatusId.Weak, DebuffCapHours, DefenseBonus: -4, BlockedByImmunityTag: "weak"),
        [StatusId.BrokenArmor] = new(StatusId.BrokenArmor, DebuffCapHours, BlockedByImmunityTag: "broken-armor"),
        [StatusId.Silenced] = new(StatusId.Silenced, DebuffCapHours, BlockedByImmunityTag: "silenced"),
        [StatusId.Venom] = DamageOverTime(StatusId.Venom, 30, "venom"),
        [StatusId.Frostburn] = DamageOverTime(StatusId.Frostburn, 16, "frostburn"),
        [StatusId.CursedFire] = DamageOverTime(StatusId.CursedFire, 24, "cursed"),
        // Creature-only afflictions (EN-17); listed so the status system knows them.
        [StatusId.Corroded] = new(StatusId.Corroded, DebuffCapHours),
        [StatusId.SalvageMark] = new(StatusId.SalvageMark, DebuffCapHours),
        [StatusId.Wet] = new(StatusId.Wet, DebuffCapHours),
        [StatusId.Battle] = new(StatusId.Battle, 10f),
        [StatusId.Calm] = new(StatusId.Calm, 10f)
    };

    /// <summary>Longest a creature-inflicted debuff may last on the player: 4 vital hours (50 real seconds).</summary>
    public const float DebuffCapHours = 4f;

    /// <summary>Real seconds between damage-over-time pulses of the Terraria-unit debuffs.</summary>
    public const float DotPulseSeconds = 0.5f;

    /// <summary>
    /// A damage-over-time debuff in Terraria's lifeRegen units: -<paramref name="units"/> units lose units/2 hit points a
    /// second, paid every <see cref="DotPulseSeconds"/> (1 vital hour = 12.5 real seconds).
    /// </summary>
    private static StatusDefinition DamageOverTime(StatusId id, int units, string immunityTag) =>
        new(id, DebuffCapHours, TickIntervalHours: DotPulseSeconds * GameSimulation.VitalHoursPerRealSecond, DamagePerTick: units / 2f * DotPulseSeconds,
            BlockedByImmunityTag: immunityTag);

    /// <summary>Hit points a second a Terraria-unit damage-over-time status costs (0 for the rest).</summary>
    public float DamagePerSecond(StatusId id) =>
        _definitions[id] is { TickIntervalHours: > 0f, DamagePerTick: > 0f } definition
            ? definition.DamagePerTick / (definition.TickIntervalHours / GameSimulation.VitalHoursPerRealSecond)
            : 0f;

    private static HashSet<string> Tags(params string[] tags) => new(tags, StringComparer.OrdinalIgnoreCase);

    /// <summary>Debuffs (shown red in the buff bar; quick-buff never applies them).</summary>
    public static bool IsDebuff(StatusId id) => id is StatusId.Poison or StatusId.Burning or StatusId.PotionSickness or StatusId.PhaseStrain
        or StatusId.Frostbite or StatusId.ArmorShred or StatusId.Confused or StatusId.Marked
        or StatusId.Bleeding or StatusId.Chilled or StatusId.Slow or StatusId.Darkness or StatusId.Weak or StatusId.BrokenArmor
        or StatusId.Silenced or StatusId.Venom or StatusId.Frostburn or StatusId.CursedFire or StatusId.Corroded
        or StatusId.SalvageMark or StatusId.Wet;

    /// <summary>
    /// Buffs (anything not a debuff) may last up to <see cref="BuffCurve.CapHours"/>, so the Terraria-length durations of
    /// 0.3.2 are not cut short by the shorter caps they were authored with.
    /// </summary>
    public StatusEffectSystem()
    {
        foreach (var (id, definition) in _definitions.ToArray())
            if (!IsDebuff(id) && id != StatusId.Phased && definition.MaximumDurationHours < BuffCurve.CapHours)
                _definitions[id] = definition with { MaximumDurationHours = BuffCurve.CapHours };
    }

    public StatusDefinition this[StatusId id] => _definitions[id];
    public IEnumerable<StatusDefinition> All => _definitions.Values.OrderBy(definition => definition.Id);
}

/// <summary>Ticks status durations in game-hours and damage over time at authored intervals.</summary>
public sealed class StatusEffectController
{
    private sealed class ActiveStatus(float remainingHours, float tickProgressHours)
    {
        public float RemainingHours = remainingHours;
        public float TickProgressHours = tickProgressHours;
    }

    private readonly StatusEffectSystem _system;
    private readonly Dictionary<StatusId, ActiveStatus> _active = new();

    public StatusEffectController(StatusEffectSystem? system = null) => _system = system ?? new StatusEffectSystem();
    public IReadOnlyList<StatusSnapshot> Active => _active.OrderBy(pair => pair.Key)
        .Select(pair => new StatusSnapshot(pair.Key, pair.Value.RemainingHours, pair.Value.TickProgressHours)).ToArray();

    public bool Apply(StatusId id, float durationHours, IReadOnlySet<string>? immunityTags = null)
    {
        if (!Enum.IsDefined(id) || !float.IsFinite(durationHours) || durationHours <= 0f)
            return false;
        var definition = _system[id];
        if (definition.BlockedByImmunityTag is { } blocked && immunityTags?.Contains(blocked) == true)
            return false;
        var duration = Math.Min(definition.MaximumDurationHours, durationHours);
        if (_active.TryGetValue(id, out var current))
        {
            current.RemainingHours = Math.Max(current.RemainingHours, duration);
            return true;
        }
        _active.Add(id, new ActiveStatus(duration, 0f));
        return true;
    }

    public bool IsActive(StatusId id) => _active.ContainsKey(id);

    /// <summary>Remaining game-hours of a status (0 when inactive).</summary>
    public float RemainingHours(StatusId id) => _active.TryGetValue(id, out var status) ? status.RemainingHours : 0f;

    public bool Remove(StatusId id) => _active.Remove(id);

    public float Advance(float elapsedHours)
    {
        if (!float.IsFinite(elapsedHours) || elapsedHours < 0f)
            throw new ArgumentOutOfRangeException(nameof(elapsedHours));
        var damage = 0f;
        foreach (var (id, active) in _active.ToArray())
        {
            var lived = Math.Min(elapsedHours, active.RemainingHours);
            var definition = _system[id];
            if (definition.TickIntervalHours > 0f && definition.DamagePerTick > 0f && lived > 0f)
            {
                active.TickProgressHours += lived;
                var ticks = (int)MathF.Floor((active.TickProgressHours + 1e-6f) / definition.TickIntervalHours);
                if (ticks > 0)
                {
                    active.TickProgressHours -= ticks * definition.TickIntervalHours;
                    damage += ticks * definition.DamagePerTick;
                }
            }
            active.RemainingHours -= elapsedHours;
            if (active.RemainingHours <= 1e-6f)
                _active.Remove(id);
        }
        return damage;
    }

    public PlayerStats AddBonuses(PlayerStats source)
    {
        var maxLife = source.MaximumLife;
        var maxMana = source.MaximumMana;
        var defense = source.Defense;
        var lifeRegen = source.LifeRegenerationPerHour;
        var manaRegen = source.ManaRegenerationPerHour;
        var immunities = new HashSet<string>(source.ImmunityTags, StringComparer.OrdinalIgnoreCase);
        foreach (var id in _active.Keys)
        {
            var definition = _system[id];
            maxLife += definition.MaximumLifeBonus;
            maxMana += definition.MaximumManaBonus;
            defense += definition.DefenseBonus;
            lifeRegen += definition.LifeRegenerationPerHour;
            manaRegen += definition.ManaRegenerationPerHour;
            if (definition.ImmunityTags is not null)
                immunities.UnionWith(definition.ImmunityTags);
        }
        return source with
        {
            MaximumLife = maxLife,
            MaximumMana = maxMana,
            Defense = defense,
            LifeRegenerationPerHour = lifeRegen,
            ManaRegenerationPerHour = manaRegen,
            ImmunityTags = immunities
        };
    }

    public bool Restore(IEnumerable<StatusSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        var pending = new Dictionary<StatusId, ActiveStatus>();
        foreach (var snapshot in snapshots)
        {
            if (!Enum.IsDefined(snapshot.Id) || !float.IsFinite(snapshot.RemainingHours) ||
                snapshot.RemainingHours <= 0f || snapshot.RemainingHours > _system[snapshot.Id].MaximumDurationHours ||
                !float.IsFinite(snapshot.TickProgressHours) || snapshot.TickProgressHours < 0f ||
                snapshot.TickProgressHours >= Math.Max(_system[snapshot.Id].TickIntervalHours, 0.0001f) ||
                pending.ContainsKey(snapshot.Id))
                return false;
            pending.Add(snapshot.Id, new ActiveStatus(snapshot.RemainingHours, snapshot.TickProgressHours));
        }
        _active.Clear();
        foreach (var pair in pending) _active.Add(pair.Key, pair.Value);
        return true;
    }
}

public static class PlayerStatsCalculator
{
    public static PlayerStats Calculate(
        EquipmentLoadout loadout,
        ItemCatalog catalog,
        StatusEffectController? statuses = null,
        ColonistStats? colonist = null)
    {
        ArgumentNullException.ThrowIfNull(loadout);
        ArgumentNullException.ThrowIfNull(catalog);
        var life = 100f;
        var mana = 20f;
        var defense = 0;
        // No base regeneration: the player heals by Terraria's natural regen formula (GameSimulation.UpdateLifeRegen) plus
        // gear and buffs only. The old free 0.035 per hour was a colonist-style recovery Terraria's player never gets.
        var lifeRegen = 0f;
        var manaRegen = 0.05f;
        var damage = 1f;
        var knockback = 1f;
        var move = 1f;
        var immunityTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pieces = new[] { loadout[EquipmentSlot.Head], loadout[EquipmentSlot.Body], loadout[EquipmentSlot.Legs], loadout[EquipmentSlot.Accessory],
            loadout[EquipmentSlot.Accessory2], loadout[EquipmentSlot.Accessory3], loadout[EquipmentSlot.Accessory4], loadout[EquipmentSlot.Accessory5],
            loadout.Slot6Open ? loadout[EquipmentSlot.Accessory6] : null };
        foreach (var id in pieces)
        {
            if (id is not { } itemId) continue;
            var item = catalog[itemId];
            defense += item.Defense;
            life += item.MaximumLifeBonus;
            mana += item.MaximumManaBonus;
            lifeRegen += item.LifeRegenerationPerHour;
            manaRegen += item.ManaRegenerationPerHour;
            damage *= item.DamageMultiplier;
            knockback *= item.KnockbackMultiplier;
            move *= item.MoveSpeedMultiplier;
            immunityTags.UnionWith(item.ImmunityTags);
        }

        var armor = new[] { loadout[EquipmentSlot.Head], loadout[EquipmentSlot.Body], loadout[EquipmentSlot.Legs] };
        var setIds = armor.Where(id => id.HasValue).Select(id => catalog[id!.Value].ArmorSetId).ToArray();
        if (setIds.Length == 3 && setIds[0] is { } setId && setIds.All(candidate => candidate == setId))
        {
            var setPieces = armor.Select(id => catalog[id!.Value]).ToArray();
            defense += setPieces.Max(item => item.SetBonusDefense);
            life += setPieces.Sum(item => item.MaximumLifeBonus * 0.25f);
        }

        if (loadout.PermanentBonuses?.Invoke() is { } permanent)
        {
            life += permanent.MaximumLife;
            defense += permanent.Defense;
            manaRegen += permanent.ManaRegenerationPerHour;
        }

        if (colonist is not null)
        {
            life += colonist.MaximumHealthBonus;
            move *= colonist.MoveSpeedMultiplier;
        }

        var stats = new PlayerStats(life, mana, defense, lifeRegen, manaRegen, damage, knockback, move, immunityTags);
        return statuses?.AddBonuses(stats) ?? stats;
    }
}

public static class EquipmentSystem
{
    public static bool TryEquip(Inventory inventory, EquipmentLoadout loadout, ItemCatalog catalog,
        EquipmentSlot slot, bool vanity = false)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(loadout);
        ArgumentNullException.ThrowIfNull(catalog);
        var stack = inventory.SelectedStack;
        if (stack.IsEmpty) return false;
        var item = catalog[stack.ItemId];
        // Accessories fill the first free accessory slot (or replace the first one when all are full).
        if (!vanity && item.EquipmentSlot == EquipmentSlot.Accessory && slot == EquipmentSlot.Accessory &&
            item.UseKind == ItemUseKind.Equipment)
        {
            var accessorySlots = loadout.ActiveAccessorySlots;
            if (accessorySlots.Any(candidate => loadout[candidate] == stack.ItemId))
                return false;
            var target = accessorySlots.FirstOrDefault(candidate => loadout[candidate] is null, EquipmentSlot.Accessory);
            if (!inventory.TryConsumeSelected()) return false;
            if (loadout.Set(target, stack.ItemId) is { } removed && inventory.Add(removed, 1) != 1)
            {
                loadout.Set(target, removed);
                inventory.Add(stack.ItemId, 1);
                return false;
            }
            return true;
        }
        if (item.EquipmentSlot != slot || item.IsVanity != vanity || item.UseKind is not (ItemUseKind.Equipment or ItemUseKind.Vanity))
            return false;
        var before = inventory.Slots.ToArray();
        var selected = inventory.SelectedSlot;
        if (!inventory.TryConsumeSelected()) return false;
        var previous = vanity
            ? loadout.Set((VanitySlot)(byte)slot, stack.ItemId)
            : loadout.Set(slot, stack.ItemId);
        if (previous is { } old && inventory.Add(old, 1) != 1)
        {
            if (!loadout.Set(slot, previous).HasValue && !vanity) { }
            if (vanity) loadout.Set((VanitySlot)(byte)slot, previous);
            inventory.RestoreState(before, selected);
            return false;
        }
        return true;
    }

    public static ItemId? Unequip(Inventory inventory, EquipmentLoadout loadout, EquipmentSlot slot, bool vanity = false)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(loadout);
        var current = vanity ? loadout[ (VanitySlot)(byte)slot ] : loadout[slot];
        if (current is not { } item || inventory.Add(item, 1) != 1) return null;
        if (vanity) loadout.Set((VanitySlot)(byte)slot, null);
        else loadout.Set(slot, null);
        return item;
    }
}
