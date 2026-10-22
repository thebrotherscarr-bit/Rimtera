using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Arsenal-pass gear effects: armor set specials, accessory tags ("knockback-immune", "water-walk", "night-vision",
/// "ore-detector", "danger-sense", "auto-swing", "fire-block" and numeric "key:value" tags such as dodge, thorns,
/// lifesteal, chargehit, endurance, light, reach, mining, attackspeed, ammosave, dmg, crit, speed, fish, drone,
/// sentry), potion statuses that carry the same tags, accessory calibrations, wing flight time and arsenal loot.
/// Every entry point is a one-line hook from the simulation (combat stats, hits, movement, mining, lights).
/// </summary>
public sealed partial class GameSimulation
{
    private static readonly StatusEffectSystem GearStatusDefinitions = new();
    private static readonly StatusId[] WellFedTiers = [StatusId.WellFed, StatusId.Satiated, StatusId.Gourmet];
    private Random? _gearRandomStream;
    private Random GearRandom => _gearRandomStream ??= new Random(Seed ^ 0x6EA2B1);
    private readonly Dictionary<EquipmentSlot, (ItemId Item, Calibration Calibration)> _accessoryCalibrations = new();
    private float _gearBurstCooldown;

    /// <summary>Hits avoided by dodge effects (set specials, accessories, the evasion buff).</summary>
    public int GearDodges { get; private set; }

    // ------------------------------------------------------------------ tag reading

    /// <summary>
    /// Sum of a numeric gear tag ("dodge:5" → 5) over worn equipment (each item counts, so two pieces stack) and
    /// active statuses.
    /// </summary>
    public float GearTagValue(string key)
    {
        var prefix = key + ":";
        var total = 0f;
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
            if (Player.Equipment[slot] is { } id && Items.Contains(id) && (slot != EquipmentSlot.Accessory6 || AccessorySlot6Unlocked))
                total += SumTags(Items[id].ImmunityTags, prefix);
        total += GrowthTagValue(key); // character growth: permanent upgrades
        foreach (var status in Player.Statuses.Active)
            if (GearStatusDefinitions[status.Id].ImmunityTags is { } tags)
                total += SumTags(tags, prefix);
        return total;
    }

    private static float SumTags(IReadOnlySet<string> tags, string prefix)
    {
        var total = 0f;
        foreach (var tag in tags)
            if (tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                float.TryParse(tag.AsSpan(prefix.Length), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
                total += value;
        return total;
    }

    /// <summary>True when worn gear or an active status grants the flag tag.</summary>
    public bool GearHasTag(string tag) => Player.Stats.ImmunityTags.Contains(tag);

    /// <summary>The worn armor set's special effect (None without a full set).</summary>
    public (SetSpecial Special, float Magnitude) ActiveSetSpecial =>
        CombatStatsCalculator.Calculate(Player.Equipment, Items).ActiveSet is { } set &&
        ArsenalItems.SetSpecials.TryGetValue(set.SetId, out var special) ? special : (SetSpecial.None, 0f);

    private float SpecialMagnitude(SetSpecial wanted) => ActiveSetSpecial is var (special, magnitude) && special == wanted ? magnitude : 0f;

    private float HealthFraction => Player.MaximumHitPoints > 0f ? Player.HitPoints / Player.MaximumHitPoints : 1f;

    // ------------------------------------------------------------------ renderer / UI views

    /// <summary>Extra light radius (tiles) around the player from headlamps, visors, night vision and glow buffs.</summary>
    public float PlayerLightBonus =>
        GearTagValue("light") + (GearHasTag("night-vision") ? 3f : 0f) + (GearHasTag("dark") ? 2f : 0f);

    /// <summary>Ore detector (accessory) or the spelunker buff: ores should be highlighted.</summary>
    public bool OreDetectorActive => GearHasTag("ore-detector") || Player.Statuses.IsActive(StatusId.Spelunker);
    /// <summary>Danger sense: hostiles and traps should be outlined.</summary>
    public bool DangerSenseActive => GearHasTag("danger-sense");
    public bool WaterWalkingActive => GearHasTag("water-walk");
    public bool KnockbackImmune => GearHasTag("knockback-immune");
    /// <summary>Auto-swing gear: holding the use button keeps attacking (and close-quarters swings are faster).</summary>
    public bool AutoSwingActive => GearHasTag("auto-swing");
    /// <summary>Seconds of wing flight from the best equipped wings (0 without wings).</summary>
    public float GearWingFlightSeconds
    {
        get
        {
            var best = 0f;
            foreach (var slot in Player.Equipment.ActiveAccessorySlots)
                if (Player.Equipment[slot] is { } id && Items.Contains(id) && !Items[id].ImmunityTags.Contains("glide-rig")) // glide rigs never climb
                    best = Math.Max(best, Items[id].WingFlightSeconds);
            return best;
        }
    }

    /// <summary>Extra reach in tiles for mining, placing and melee.</summary>
    public float GearReachBonus => GearTagValue("reach");
    /// <summary>Mining speed multiplier from gear and buffs.</summary>
    public float GearMiningFactor => 1f + GearTagValue("mining") / 100f;
    /// <summary>Fishing power from buffs and tagged gear (rod/accessory FishingPower is counted separately).</summary>
    public float GearFishingBonus => GearTagValue("fish");

    /// <summary>Movement multiplier from buffs, low-health set specials and accessory calibrations.</summary>
    public float GearMoveFactor
    {
        get
        {
            var factor = 1f + GearTagValue("speed") / 100f + AccessoryCalibrations().Sum(c => c.AccessoryMoveSpeed);
            if (HealthFraction < 0.4f)
                factor += SpecialMagnitude(SetSpecial.SpeedWhenLow);
            return factor;
        }
    }

    private IReadOnlyList<PlayerLightSource> GearLights()
    {
        var radius = PlayerLightBonus;
        return radius > 0f ? [new PlayerLightSource(PlayerCenter, radius, 0.85f, "white")] : [];
    }

    // ------------------------------------------------------------------ combat hooks

    /// <summary>Adds buff/accessory tags, accessory calibrations and set specials to the gear-derived combat stats.</summary>
    private CombatStats GearAdjust(CombatStats stats)
    {
        var calibrations = AccessoryCalibrations().ToArray();
        var all = GearTagValue("dmg") / 100f + calibrations.Sum(c => c.DamageMultiplier - 1f);
        if (HealthFraction > 0.9f)
            all += SpecialMagnitude(SetSpecial.Frenzy);
        return stats with
        {
            MeleeMultiplier = stats.MeleeMultiplier + all + GearTagValue("dmg-melee") / 100f,
            RangedMultiplier = stats.RangedMultiplier + all + GearTagValue("dmg-ranged") / 100f,
            TechMultiplier = stats.TechMultiplier + all + GearTagValue("dmg-tech") / 100f,
            CommandMultiplier = stats.CommandMultiplier + all + GearTagValue("dmg-command") / 100f,
            CritBonus = stats.CritBonus + (int)GearTagValue("crit") + calibrations.Sum(c => c.CritBonus),
            DroneSlots = stats.DroneSlots + (int)GearTagValue("drone"),
            SentrySlots = stats.SentrySlots + (int)GearTagValue("sentry"),
            AmmoSaveChance = Math.Clamp(stats.AmmoSaveChance + GearTagValue("ammosave") / 100f, 0f, 0.9f),
            Thorns = stats.Thorns + GearTagValue("thorns") / 100f + SpecialMagnitude(SetSpecial.Thorns)
        };
    }

    /// <summary>
    /// Melee use-time multiplier: close-quarters attack-speed gear (auto-swing gloves) raises attack speed additively,
    /// which becomes 1 / min(speed, 3) (<see cref="CombatTuning.UseTimeMultiplier"/>). 1 for other classes.
    /// </summary>
    private float GearAttackSpeedFactor(ItemDefinition item) =>
        item.EffectiveClass == DamageClass.Melee ? CombatTuning.UseTimeMultiplier(1f + GearTagValue("attackspeed") / 100f) : 1f;

    /// <summary>Velocity calibrations (Rifled, Hypersonic...) scale the selected weapon's projectile speed, whatever its class.</summary>
    private float GearProjectileSpeedFactor(DamageClass damageClass) => CalibrationCatalog.Get(SelectedCalibration).VelocityMultiplier;

    /// <summary>Melee size calibrations (Massive, Lengthened...) for the style code: reach × this.</summary>
    public float CalibratedSize(Calibration calibration) => CalibrationCatalog.Get(calibration).SizeMultiplier;

    /// <summary>Knockback taken by the player, cancelled by knockback immunity.</summary>
    private Vector2 GearKnockback(Vector2 push) => KnockbackImmune ? Vector2.Zero : push;

    /// <summary>
    /// A creature or projectile hit on the player: dodge, endurance and accessory-calibration defense first, then the
    /// hit, then the burst-when-hit set special. Returns the damage applied.
    /// </summary>
    private float GearHitPlayer(EnemyAgent? source, float amount, DamageKind kind)
    {
        if (Player.IsImmune || Player.IsDead || !float.IsFinite(amount) || amount <= 0f)
            return Player.TakeHit(amount, kind);
        var dodge = GearTagValue("dodge") / 100f + SpecialMagnitude(SetSpecial.Dodge);
        if (dodge > 0f && GearRandom.NextDouble() < Math.Min(0.6f, dodge))
        {
            Player.ImmunitySeconds = Math.Max(Player.ImmunitySeconds, PlayerCharacter.HitImmunitySeconds);
            GearDodges++;
            PostMessage(source is null ? "Dodged the shot!" : $"Dodged the {source.DisplayName}!");
            return 0f;
        }
        // Defense first (worn gear, statuses and accessory calibrations), then endurance multiplies what is left.
        amount = DefenseBalance.ApplyDefense(amount, TotalDefense, Difficulty);
        var endurance = GearTagValue("endurance") / 100f + SpecialMagnitude(SetSpecial.Endurance);
        amount *= 1f - Math.Clamp(endurance, 0f, 0.6f);
        var applied = Player.TakeHit(amount, kind);
        var burst = SpecialMagnitude(SetSpecial.BurstWhenHit);
        if (applied > 0f && burst > 0f && _gearBurstCooldown <= 0f)
        {
            var damage = 10f * burst + applied;
            foreach (var enemy in _enemies.Where(enemy => enemy.IsAlive && !enemy.IsPassive &&
                                                          EnemyHitDistance(enemy, PlayerCenter) <= 3.5f).ToArray())
                enemy.TakeDamage(ScaleOutgoing(damage));
            _gearBurstCooldown = 1f;
        }
        return applied;
    }

    /// <summary>Defense that reduces creature and projectile hits: the player's stats plus accessory calibrations.</summary>
    public int TotalDefense => Player.Stats.Defense + AccessoryCalibrations().Sum(c => c.AccessoryDefense);

    /// <summary>On-hit effects for any class: lifesteal tags, heal-on-hit and charge-on-hit set specials.</summary>
    private void GearOnHitEnemy(EnemyAgent enemy, float damage, DamageClass damageClass)
    {
        if (damage <= 0f || enemy.IsPassive)
            return;
        var heal = GearTagValue("lifesteal") / 100f + SpecialMagnitude(SetSpecial.HealOnHit);
        if (heal > 0f && Player.HitPoints < Player.MaximumHitPoints)
            Player.Heal(Math.Min(damage * heal, Player.MaximumHitPoints * 0.05f));
        var charge = GearTagValue("chargehit") + SpecialMagnitude(SetSpecial.ChargeOnHit);
        if (charge > 0f)
            Charge += charge;
    }

    /// <summary>Per-tick gear upkeep: burst cooldown, fire-block clears burning.</summary>
    private void GearTick(float deltaSeconds)
    {
        _gearBurstCooldown = Math.Max(0f, _gearBurstCooldown - deltaSeconds);
        if (Player.Statuses.IsActive(StatusId.Burning) && GearHasTag("fire-block"))
            Player.Statuses.Remove(StatusId.Burning);
    }

    // ------------------------------------------------------------------ movement: water walking

    /// <summary>Water-walking gear stands the player on liquid surfaces (lava only with lava immunity); jump works from it.</summary>
    private void GearWaterWalk(ref Vector2 velocity, MovementInput movement, ref bool jumpUsed)
    {
        if (_bodyLiquid != LiquidKind.None || velocity.Y < 0f || !WaterWalkingActive)
            return;
        var motion = Player.Motion;
        var feet = motion.Position.Y + motion.Size.Y;
        var cell = new GridPoint((int)MathF.Floor(motion.Position.X + motion.Size.X / 2f), (int)MathF.Floor(feet + 0.05f));
        if (!World.Contains(cell))
            return;
        var data = World.GetCell(cell);
        if (data.LiquidKind == LiquidKind.None || data.LiquidDepth < 0.35f || Tiles[data.TileId].IsSolid ||
            (data.LiquidKind == LiquidKind.Lava && !GearHasTag("lava")))
            return;
        var surface = cell.Y + (1f - Math.Min(1f, data.LiquidDepth));
        if (feet < surface - 0.2f || feet > surface + 0.7f)
            return;
        motion.Position = new Vector2(motion.Position.X, surface - motion.Size.Y);
        velocity.Y = 0f;
        motion.FallPeakY = motion.Position.Y;
        _doubleJumpUsed = false;
        if (movement.JumpPressed && !jumpUsed)
        {
            velocity.Y = -8.6f;
            jumpUsed = true;
        }
    }

    // ------------------------------------------------------------------ food, equipment calibrations

    /// <summary>Food with a well-fed tier replaces the lower/other tiers, then applies its own.</summary>
    private void GearOnAte(ItemDefinition food)
    {
        if (food.GrantsStatus is not { } status)
            return;
        if (WellFedTiers.Contains(status))
            foreach (var tier in WellFedTiers.Where(tier => tier != status))
                Player.Statuses.Remove(tier);
        Player.Statuses.Apply(status, food.StatusDurationHours, Player.Stats.ImmunityTags);
    }

    private IEnumerable<CalibrationDefinition> AccessoryCalibrations()
    {
        foreach (var (slot, entry) in _accessoryCalibrations)
            if (Player.Equipment[slot] == entry.Item)
                yield return CalibrationCatalog.Get(entry.Calibration);
    }

    /// <summary>Calibration remembered for a worn accessory (None when uncalibrated).</summary>
    public Calibration EquippedCalibration(EquipmentSlot slot) =>
        _accessoryCalibrations.TryGetValue(slot, out var entry) && Player.Equipment[slot] == entry.Item ? entry.Calibration : Calibration.None;

    /// <summary>
    /// The worn equipment plus the calibrations of worn accessories. The calibration list is null when nothing
    /// worn is calibrated, so plain snapshots stay equal to <see cref="EquipmentLoadout.Capture"/>.
    /// </summary>
    public EquipmentSnapshot CaptureEquipmentWithCalibrations()
    {
        var calibrations = _accessoryCalibrations
            .Where(pair => pair.Value.Calibration != Calibration.None && Player.Equipment[pair.Key] == pair.Value.Item)
            .Select(pair => new SlotCalibration(pair.Key, pair.Value.Item, pair.Value.Calibration))
            .OrderBy(entry => entry.Slot).ToArray();
        return Player.Equipment.Capture() with { Calibrations = calibrations.Length == 0 ? null : calibrations };
    }

    /// <summary>
    /// Replaces the remembered accessory calibrations with a snapshot's. Entries are kept only when that
    /// accessory is actually worn in that slot and the calibration is a known one.
    /// </summary>
    private void RestoreAccessoryCalibrations(EquipmentSnapshot? snapshot)
    {
        _accessoryCalibrations.Clear();
        foreach (var entry in snapshot?.Calibrations ?? [])
            if (EquipmentLoadout.AllAccessorySlots.Contains(entry.Slot) && Enum.IsDefined(entry.Calibration) &&
                entry.Calibration != Calibration.None && Player.Equipment[entry.Slot] == entry.Item)
                _accessoryCalibrations[entry.Slot] = (entry.Item, entry.Calibration);
    }

    /// <summary>Equips the selected stack, remembering an accessory's calibration (and returning a replaced one's).</summary>
    private bool GearTryEquip(EquipmentSlot slot, bool vanity)
    {
        var stack = Player.Inventory.SelectedStack;
        var slots = Player.Equipment.ActiveAccessorySlots;
        var before = slots.Select(candidate => Player.Equipment[candidate]).ToArray();
        var beforeCalibrations = slots.Select(EquippedCalibration).ToArray();
        if (!EquipmentSystem.TryEquip(Player.Inventory, Player.Equipment, Items, slot, vanity))
            return false;
        if (vanity || stack.IsEmpty)
            return true;
        for (var i = 0; i < slots.Count; i++)
        {
            if (Player.Equipment[slots[i]] == before[i])
                continue;
            // A replaced calibrated accessory goes back to the inventory with its calibration.
            if (before[i] is { } replaced && beforeCalibrations[i] != Calibration.None)
                RecalibrateLoose(replaced, beforeCalibrations[i]);
            _accessoryCalibrations.Remove(slots[i]);
            if (stack.Calibration != Calibration.None && Player.Equipment[slots[i]] == stack.ItemId)
                _accessoryCalibrations[slots[i]] = (stack.ItemId, stack.Calibration);
        }
        return true;
    }

    private void RecalibrateLoose(ItemId item, Calibration calibration)
    {
        var stacks = Player.Inventory.Slots.ToArray();
        for (var i = 0; i < stacks.Length; i++)
        {
            if (stacks[i].ItemId != item || stacks[i].Calibration != Calibration.None || stacks[i].Count != 1)
                continue;
            stacks[i] = stacks[i] with { Calibration = calibration };
            Player.Inventory.RestoreState(stacks, Player.Inventory.SelectedSlot);
            return;
        }
    }

    /// <summary>Unequips a slot, returning a calibrated accessory with its calibration.</summary>
    private ItemId? GearUnequip(EquipmentSlot slot, bool vanity)
    {
        if (!vanity && _accessoryCalibrations.TryGetValue(slot, out var entry) && Player.Equipment[slot] == entry.Item)
        {
            if (Player.Inventory.Add(entry.Item, 1, entry.Calibration) != 1)
                return null;
            Player.Equipment.Set(slot, null);
            _accessoryCalibrations.Remove(slot);
            return entry.Item;
        }
        if (!vanity)
            _accessoryCalibrations.Remove(slot);
        return EquipmentSystem.Unequip(Player.Inventory, Player.Equipment, slot, vanity);
    }

    // ------------------------------------------------------------------ self-test probes

    /// <summary>Self-test probe: one creature-style hit on the player through the gear pipeline (immunity cleared first).</summary>
    internal float GearProbeHit(float amount)
    {
        Player.ImmunitySeconds = 0f;
        return GearHitPlayer(null, amount, DamageKind.Blunt);
    }

    /// <summary>Self-test probe: knockback the player would take from a push.</summary>
    internal Vector2 GearProbeKnockback(Vector2 push) => GearKnockback(push);

    // ------------------------------------------------------------------ loot

    /// <summary>Bosses roll one item (sometimes two) from their arsenal pool; creatures roll their rare drops.</summary>
    private void OnCreatureDefeatedArsenal(EnemyAgent enemy)
    {
        if (enemy.IsBoss && ArsenalItems.BossPools.TryGetValue(enemy.Kind, out var pool) && pool.Length > 0)
        {
            // Boss gear pops out where the boss fell (not straight into a full pack).
            var center = enemy.Motion.Position + enemy.Motion.Size / 2f;
            var first = GearRandom.Next(pool.Length);
            SpawnGroundItem(pool[first], 1, center, pickupDelay: 1.5f);
            Bestiary.RecordDrop(enemy.Kind, pool[first]);
            if (pool.Length >= 4 && GearRandom.NextDouble() < 0.35)
            {
                var second = pool[(first + 1 + GearRandom.Next(pool.Length - 1)) % pool.Length];
                SpawnGroundItem(second, 1, center + new Vector2(0.6f, 0f), pickupDelay: 1.5f);
                Bestiary.RecordDrop(enemy.Kind, second);
            }
            return;
        }
        if (ArsenalItems.MobDrops.TryGetValue(enemy.Kind, out var drops))
            foreach (var (item, percent) in drops)
                if (GearRandom.Next(100) < percent)
                    DropLoot(enemy, item, 1);
    }
}
