using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Content-expansion combat: the four damage classes, Charge, drones and sentries, calibrations,
/// crits, pierce, lifesteal/thorns set bonuses, ammo saving and credit drops.
/// </summary>
public sealed partial class GameSimulation
{
    // Separate streams so new combat rolls never shift the legacy spawn/combat sequence.
    private Random? _combatRandomStream;
    private Random? _craftRandomStream;
    private Random CombatRandom => _combatRandomStream ??= new Random(Seed ^ 0x0C0B47);
    private Random CraftRandom => _craftRandomStream ??= new Random(Seed ^ 0x0CA11B);
    private readonly List<MinionState> _minions = new();
    private long _whipMarkedEnemy = -1;
    private float _whipBuffSeconds;
    private float _charge = -1f;

    /// <summary>Current Charge (the Tech resource).</summary>
    public float Charge
    {
        get
        {
            if (_charge < 0f)
                _charge = MaximumCharge;
            return _charge;
        }
        private set
        {
            var spent = value < Charge - 0.0001f;
            _charge = Math.Clamp(value, 0f, MaximumCharge);
            // Any spend restarts the regeneration delay, longer the emptier the pool is left.
            if (spent)
                _chargeRegenDelayTicks = CombatTuning.ChargeRegenDelayTicks(_charge, MaximumCharge, ChargeRegenBuffActive);
        }
    }

    private float _chargeRegenDelayTicks;
    private float _chargeSicknessSeconds;
    private float _secondsSinceHurt = 99f;
    private float _lastHitPoints = -1f;

    /// <summary>Ticks left before spent charge starts flowing back.</summary>
    public float ChargeRegenDelayTicks => _chargeRegenDelayTicks;

    /// <summary>The charge-regeneration buff (charge stim): shorter delay, doubled and unscaled regeneration.</summary>
    private bool ChargeRegenBuffActive => Player.Statuses.IsActive(StatusId.ChargeSurge);

    /// <summary>Seconds of charge sickness left (Tech damage reduced, fading out).</summary>
    public float ChargeSicknessSeconds => _chargeSicknessSeconds;

    /// <summary>Seconds since the player last lost hit points (drives natural regeneration).</summary>
    public float SecondsSinceHurt => _secondsSinceHurt;

    /// <summary>Charge from a restorative item: refills, then brings on a short charge sickness.</summary>
    private void RestoreChargeFromItem(float amount)
    {
        Charge += amount;
        RecordChargeGain(amount); // PE-14: a blue number (Systems/SoundEvents.cs)
        _chargeSicknessSeconds = CombatTuning.ChargeSicknessSeconds;
    }

    private bool PlayerStandingStill => Player.Motion.Grounded && MathF.Abs(Player.Motion.Velocity.X) < 0.05f;

    /// <summary>Natural regeneration counts the body as still whenever it has no sideways speed (mid-jump included).</summary>
    private bool PlayerHorizontallyStill => MathF.Abs(Player.Motion.Velocity.X) < 0.05f;

    /// <summary>Any well-fed tier is active (lifts the Brutal/Annihilation regeneration halving).</summary>
    private bool PlayerWellFed => Player.Statuses.IsActive(StatusId.WellFed) || Player.Statuses.IsActive(StatusId.Satiated) ||
                                  Player.Statuses.IsActive(StatusId.Gourmet);

    /// <summary>Brutal and Annihilation halve natural regeneration for players who are not well fed.</summary>
    private bool HarshRegenWorld => Difficulty is WorldDifficulty.Brutal or WorldDifficulty.Annihilation;

    /// <summary>Natural hit points per second right now (0 just after a hurt; see <see cref="CombatTuning.NaturalLifeRegenUnits"/>).</summary>
    public float NaturalLifeRegenPerSecond =>
        CombatTuning.NaturalLifeRegen(Math.Min(_secondsSinceHurt, CombatTuning.LifeRegenCapTicks / CombatTuning.TicksPerSecond),
            Player.MaximumHitPoints, PlayerHorizontallyStill, _sleeping, HarshRegenWorld, PlayerWellFed);

    /// <summary>
    /// Natural life regeneration: the unhurt timer runs from the last loss of hit points (damage over time included);
    /// every 5 s (later 10 s) unhurt adds a step, up to nine steps at 60 s. Sleeping runs the timer 4× as fast. Gear and
    /// status regeneration is applied separately on top.
    /// </summary>
    private void UpdateLifeRegen(float deltaSeconds)
    {
        if (_lastHitPoints >= 0f && Player.HitPoints < _lastHitPoints - 0.0001f)
            _secondsSinceHurt = 0f;
        else
            _secondsSinceHurt += deltaSeconds * (_sleeping ? 4f : 1f);
        if (!Player.IsDead && Player.HitPoints < Player.MaximumHitPoints && !PlayerBleeding) // bleeding: no regeneration
        {
            var regen = NaturalLifeRegenPerSecond;
            if (regen > 0f)
                Player.Heal(regen * deltaSeconds);
        }
        _lastHitPoints = Player.HitPoints;
    }

    /// <summary>
    /// Maximum Charge: the mana pool from <see cref="PlayerStats"/>, Tech set bonuses and charge crystals, capped at
    /// <see cref="CombatTuning.MaximumChargeCap"/>.
    /// </summary>
    public float MaximumCharge => Math.Min(CombatTuning.MaximumChargeCap, Player.Stats.MaximumMana + CombatStats.ChargeMaxBonus +
                                  Player.ChargeCrystalsUsed * PlayerItems.ChargeCrystalBonus);

    /// <summary>Gear and status charge regeneration (charge per second) in regen units (half a point per second each).</summary>
    private float ChargeRegenBonusUnits => (Player.Stats.ManaRegenerationPerHour * 2f + CombatStats.ChargeRegenBonus) * 2f;

    /// <summary>Charge regained per real second once the post-spend delay is over, at the current charge level.</summary>
    public float ChargeRegenPerSecond => CombatTuning.RegenUnitsPerSecond(CombatTuning.ChargeRegenUnits(Charge, MaximumCharge,
        ChargeRegenBonusUnits, PlayerStandingStill, ChargeRegenBuffActive));

    /// <summary>Multiplier on charge cost from worn gear and buffs ("chargecost:-8" = 8% cheaper).</summary>
    public float ChargeCostMultiplier => Math.Max(0f, 1f + GearTagValue("chargecost") / 100f);

    /// <summary>Charge one use of <paramref name="item"/> costs with its calibration and the wearer's cost multiplier.</summary>
    public int ChargeCostFor(ItemDefinition item, Calibration calibration) =>
        CombatTuning.ChargeCost(item.ChargeCost, CalibrationCatalog.Get(calibration).ChargeCostMultiplier, ChargeCostMultiplier);

    /// <summary>Class multipliers, crit, slots and set bonus from worn gear.</summary>
    public CombatStats CombatStats => GearAdjust(CombatStatsCalculator.Calculate(Player.Equipment, Items));

    /// <summary>Drones the player may keep out at once.</summary>
    public int DroneSlots => CombatStats.DroneSlots;
    /// <summary>Sentries the player may keep deployed at once.</summary>
    public int SentrySlots => CombatStats.SentrySlots;
    public int ActiveDrones => _minions.Count(minion => !minion.IsSentry);
    public int ActiveSentries => _minions.Count(minion => minion.IsSentry);
    /// <summary>Live drones and sentries (render with <see cref="MinionState.SpriteName"/>).</summary>
    public IReadOnlyList<MinionState> Minions => _minions;

    /// <summary>The selected stack's name with its calibration prefix ("Precise Ferrite shiv").</summary>
    public string DescribeStack(ItemStack stack) =>
        stack.IsEmpty ? string.Empty : CalibrationCatalog.Describe(Items[stack.ItemId], stack.Calibration);

    /// <summary>
    /// Damage one use of <paramref name="item"/> deals before the hit roll: the calibration is baked into the base
    /// damage, ammunition adds its damage, the class multiplier (1 + the additive class bonuses from gear, times the
    /// charge-sickness penalty for Tech) scales it and the result is truncated to whole points
    /// (<see cref="CombatTuning.WeaponDamage"/>). The colonist's combat skill then multiplies it (Rimtera's own rule).
    /// </summary>
    public float ComputeWeaponDamage(ItemDefinition item, Calibration calibration = Calibration.None, int ammoBonus = 0)
    {
        ArgumentNullException.ThrowIfNull(item);
        var damageClass = item.EffectiveClass;
        var skill = CombatStatsCalculator.SkillMultiplier(Player.ColonistStats, damageClass);
        var sickness = damageClass == DamageClass.Tech && _chargeSicknessSeconds > 0f
            ? 1f - CombatTuning.ChargeSicknessPenalty * (_chargeSicknessSeconds / CombatTuning.ChargeSicknessSeconds)
            : 1f;
        var classMultiplier = CombatStats.ClassMultiplier(damageClass) * sickness;
        var damage = CombatTuning.WeaponDamage(item.Damage, CalibrationCatalog.Get(calibration).DamageMultiplier, ammoBonus, classMultiplier);
        return damage * skill;
    }

    private Calibration SelectedCalibration => Player.Inventory.SelectedStack.Calibration;

    /// <summary>
    /// Use time of a weapon: the calibration's speed factor is baked in (rounded), then close-quarters weapons are
    /// scaled by the melee use-time multiplier (attack speed capped at 3×), never below 1 tick. Other classes ignore
    /// melee speed.
    /// </summary>
    private int ScaledCooldown(ItemDefinition item, Calibration calibration)
    {
        var ticks = CombatTuning.CalibratedUseTicks(item.UseCooldownTicks, CalibrationCatalog.Get(calibration).CooldownMultiplier);
        return item.EffectiveClass == DamageClass.Melee ? CombatTuning.MeleeUseTicks(ticks, GearAttackSpeedFactor(item)) : ticks;
    }

    /// <summary>
    /// Crit chance of a use: 4% base + the weapon's own chance + its calibration + every worn crit bonus + luck.
    /// Command weapons (drones, sentries, signal whips) never crit.
    /// </summary>
    private int CritChanceFor(ItemDefinition item, Calibration calibration) =>
        CombatTuning.WeaponCritChance(item.EffectiveClass, item.CritChance,
            CalibrationCatalog.Get(calibration).CritBonus + CombatStats.CritBonus + LuckCritBonus);

    /// <summary>Armor penetration of the player's hits: "ap:N" gear, plus "ap-melee:N" for close-quarters hits.</summary>
    public int PlayerArmorPenetration(DamageClass damageClass) =>
        (int)(GearTagValue("ap") + (damageClass == DamageClass.Melee ? GearTagValue("ap-melee") : 0f));

    /// <summary>A creature's defense against player hits.</summary>
    private static int CreatureDefense(EnemyAgent enemy) => CorrodedDefense(enemy, Math.Max(0, enemy.Archetype.Defense));

    /// <summary>
    /// The roll for one use (a swing, a shot, a beam pulse): the ±15% spread rounded to whole points (luck rerolls) and
    /// the crit roll. Every creature that use hits shares the roll. Sets <see cref="LastHitCritical"/>.
    /// </summary>
    private (float Damage, bool Critical) RollUse(float damage, int critChance)
    {
        var varied = CombatTuning.RollVariance(damage, VarianceRandom, Math.Clamp(Luck, -1f, 1f));
        LastHitCritical = CombatTuning.RollCrit(critChance, CombatRandom);
        return (varied, LastHitCritical);
    }

    /// <summary>
    /// One rolled player hit landing on a creature: the trophy-banner bonus, armor penetration and half the creature's
    /// defense (at least 1), ×2 on a crit, then the world's creature-toughness scaling and damage-taken afflictions.
    /// Returns the damage before toughness scaling (what the hit message shows).
    /// </summary>
    private float StrikeEnemy(EnemyAgent enemy, float rolledDamage, bool critical, DamageClass damageClass) =>
        StrikeEnemy(enemy, rolledDamage, critical, damageClass, CreatureDefense(enemy));

    private float StrikeEnemy(EnemyAgent enemy, float rolledDamage, bool critical, DamageClass damageClass, int defense)
    {
        var dealt = CombatTuning.CreatureDamageTaken(rolledDamage * BannerDamageFactor(enemy.Kind), defense,
            PlayerArmorPenetration(damageClass), critical);
        LastHitCritical = critical;
        RecordHit(enemy, ScaleOutgoing(dealt) * EnemyDamageTakenFactor(enemy), critical); // PE-14 hit record (Systems/SoundEvents.cs)
        enemy.TakeDamage(ScaleOutgoing(dealt) * EnemyDamageTakenFactor(enemy));
        return dealt;
    }

    /// <summary>Spread, crit roll, defense and crit doubling for one hit (no creature needed).</summary>
    private float RollHit(float damage, int critChance, int defense, int armorPenetration)
    {
        var (varied, critical) = RollUse(damage, critChance);
        return CombatTuning.CreatureDamageTaken(varied, defense, armorPenetration, critical);
    }

    private Random? _varianceRandomStream;
    private Random VarianceRandom => _varianceRandomStream ??= new Random(Seed ^ 0x5D1FF);

    /// <summary>True when the most recent weapon roll was a critical hit.</summary>
    public bool LastHitCritical { get; private set; }

    /// <summary>Test probe: rolls one hit exactly as a weapon use does, against the given defense and penetration.</summary>
    internal float ProbeRollHit(float damage, int critChance, int defense = 0, int armorPenetration = 0) =>
        RollHit(damage, critChance, defense, armorPenetration);

    /// <summary>
    /// Pushes a creature away from a hit: its knockback resistance soaks part (bosses most or all) of it, the rest is
    /// soft-capped above 8 and hard-capped at 16, and a crit pushes 40% harder (<see cref="CombatTuning.KnockbackTaken"/>).
    /// </summary>
    private void KnockEnemy(EnemyAgent enemy, Vector2 push, bool critical = false)
    {
        var strength = push.Length();
        if (strength <= 1e-5f)
            return;
        var taken = CombatTuning.KnockbackTaken(strength, CombatTuning.KnockbackResistance(enemy.Archetype), critical);
        enemy.Motion.Velocity += push * (taken / strength);
    }

    /// <summary>Test probe for <see cref="KnockEnemy"/>.</summary>
    internal void ProbeKnock(EnemyAgent enemy, Vector2 push, bool critical = false) => KnockEnemy(enemy, push, critical);

    /// <summary>Test probe: the crit chance a use of <paramref name="item"/> would roll.</summary>
    internal int ProbeCritChance(ItemDefinition item) => CritChanceFor(item, Calibration.None);

    /// <summary>Test probe: the crit chance with a calibration.</summary>
    internal int ProbeCritChance(ItemDefinition item, Calibration calibration) => CritChanceFor(item, calibration);

    /// <summary>Test probe: the use time (ticks) of a weapon with a calibration, melee speed included.</summary>
    internal int ProbeUseTicks(ItemDefinition item, Calibration calibration) => ScaledCooldown(item, calibration);

    /// <summary>Test probe: a drill's swing time with the current mining-speed bonuses and skill.</summary>
    internal int ProbeDrillTicks(ItemDefinition item) => DrillCooldownTicks(item);

    /// <summary>Test probe: lands an already rolled hit on a creature as if it had the given defense.</summary>
    internal float ProbeStrike(EnemyAgent enemy, float rolledDamage, bool critical, int defense) =>
        StrikeEnemy(enemy, rolledDamage, critical, DamageClass.Melee, defense);

    /// <summary>Test probe: spends charge as a weapon use would.</summary>
    internal void ProbeSpendCharge(float amount) => Charge -= amount;

    /// <summary>Test probe: fires one plain player projectile with the given pierce.</summary>
    internal void ProbeFireProjectile(Vector2 origin, Vector2 velocity, float damage, int pierce) =>
        _projectiles.Add(new ProjectileState(_nextEntityId++, origin, velocity, damage, 0f, 3f, ProjectileTeam.Player)
        {
            Class = DamageClass.Ranged, PierceRemaining = pierce
        });

    private Calibration RollCalibration(ItemDefinition item) => RollCalibrationWithLuck(item);

    // ------------------------------------------------------------------ class attacks

    /// <summary>Melee extension: thrown weapons (discs, boomerangs, yo-yos) fly as projectiles.</summary>
    private bool TryThrowMelee(ItemDefinition item, GridPoint target)
    {
        if (!item.Thrown)
            return false;
        var calibration = SelectedCalibration;
        var damage = ComputeWeaponDamage(item, calibration);
        FireProjectiles(item, target, damage, item.Knockback * CalibrationCatalog.Get(calibration).KnockbackMultiplier,
            item.ProjectileSpeed, 1, 0f, item.ProjectileLifetime, item.Pierce, CritChanceFor(item, calibration), DamageClass.Melee);
        SetPlayerCooldown(ScaledCooldown(item, calibration));
        PostMessage($"Threw the {DescribeStack(Player.Inventory.SelectedStack)}.");
        return true;
    }

    private void AttackTech(ItemDefinition item, GridPoint target)
    {
        var calibration = SelectedCalibration;
        var cost = ChargeCostFor(item, calibration);
        if (Charge < cost)
        {
            PostMessage($"Not enough charge ({Charge:0}/{cost:0}).");
            return;
        }
        Charge -= cost;
        SoundEvents.Emit(SoundEventKind.Use, "tech", PlayerCenter, (int)item.Id);
        var damage = ComputeWeaponDamage(item, calibration);
        FireProjectiles(item, target, damage, item.Knockback * CalibrationCatalog.Get(calibration).KnockbackMultiplier,
            item.ProjectileSpeed, item.ProjectileCount, item.ProjectileSpread, item.ProjectileLifetime, item.Pierce,
            CritChanceFor(item, calibration), DamageClass.Tech);
        SetPlayerCooldown(ScaledCooldown(item, calibration));
        PostMessage($"Discharged the {DescribeStack(Player.Inventory.SelectedStack)} ({Charge:0} charge left).");
    }

    /// <summary>Spawns <paramref name="count"/> fanned player projectiles toward a target cell.</summary>
    private void FireProjectiles(ItemDefinition item, GridPoint target, float damage, float knockback, float speed, int count,
        float spread, float lifetime, int pierce, int critChance, DamageClass damageClass, float purifyRadius = 0f)
    {
        var origin = Player.Motion.Position + new Vector2(Player.Motion.Size.X / 2f, Player.Motion.Size.Y * 0.35f);
        var aim = new Vector2(target.X + 0.5f, target.Y + 0.5f) - origin;
        if (aim.LengthSquared() < 0.001f)
            aim = new Vector2(Player.Motion.Facing, 0f);
        var baseAngle = MathF.Atan2(aim.Y, aim.X) - spread * (count - 1) / 2f;
        for (var shot = 0; shot < Math.Max(1, count); shot++)
        {
            var angle = baseAngle + spread * shot;
            var velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * Math.Max(1f, speed * GearProjectileSpeedFactor(damageClass)) + WindDrift;
            // Each shot rolls its spread and crit once; defense and the crit doubling apply on every creature it hits.
            var (rolled, critical) = RollUse(damage, critChance);
            _projectiles.Add(new ProjectileState(_nextEntityId++, origin, velocity, rolled, knockback,
                lifetime, ProjectileTeam.Player, purifyRadius: purifyRadius)
            {
                Class = damageClass, PierceRemaining = pierce, CritChance = critChance, Critical = critical, SpriteName = SnakeName(item.Id)
            });
            StampAffliction(_projectiles[^1], item);
        }
    }

    private static string SnakeName(ItemId id) =>
        System.Text.RegularExpressions.Regex.Replace(id.ToString(), "(?<=[a-z0-9])([A-Z])", "_$1").ToLowerInvariant();

    /// <summary>Deploys a drone (orbits the player) or a sentry (at the target cell), replacing the oldest past the slot limit.</summary>
    private void UseCommand(ItemDefinition item, GridPoint target)
    {
        if (item.Summons is not { } kind)
        {
            PostMessage("That remote has nothing to command.");
            return;
        }
        if (SummonMinion(item, kind, target) is { } minion)
        {
            SoundEvents.Emit(SoundEventKind.Use, "command", minion.Position, (int)item.Id);
            SetPlayerCooldown(item.UseCooldownTicks);
            PostMessage(minion.IsSentry
                ? $"Deployed a {minion.Spec.DisplayName} ({ActiveSentries}/{SentrySlots} sentries)."
                : $"Launched a {minion.Spec.DisplayName} ({ActiveDrones}/{DroneSlots} drones).");
        }
    }

    /// <summary>Creates a minion for a command item; returns null when the target is invalid.</summary>
    public MinionState? SummonMinion(ItemDefinition item, MinionKind kind, GridPoint target)
    {
        var spec = MinionCatalog.Get(kind);
        Vector2 position;
        if (spec.IsSentry)
        {
            if (!World.Contains(target) || Vector2.Distance(PlayerCenter, new Vector2(target.X + 0.5f, target.Y + 0.5f)) > 12f ||
                Tiles[World.GetCell(target).TileId].IsSolid)
            {
                PostMessage("A sentry needs open ground within 12 tiles.");
                return null;
            }
            position = new Vector2(target.X + 0.5f, target.Y + 0.5f);
        }
        else
        {
            position = PlayerCenter + new Vector2(0f, -1.5f);
        }
        var limit = spec.IsSentry ? SentrySlots : DroneSlots;
        while (_minions.Count(minion => minion.IsSentry == spec.IsSentry) >= Math.Max(1, limit))
            _minions.Remove(_minions.First(minion => minion.IsSentry == spec.IsSentry));
        var damage = ComputeWeaponDamage(item, Player.Inventory.SelectedStack.ItemId == item.Id ? SelectedCalibration : Calibration.None);
        var minion = new MinionState(_nextEntityId++, spec, position, damage, item.Id);
        _minions.Add(minion);
        return minion;
    }

    /// <summary>Drones orbit and shoot, repair drones heal, sentries hold position and fire at the nearest hostile.</summary>
    private void UpdateMinions(float deltaSeconds)
    {
        _whipBuffSeconds = Math.Max(0f, _whipBuffSeconds - deltaSeconds);
        if (_minions.Count == 0)
            return;
        var droneIndex = 0;
        var drones = Math.Max(1, ActiveDrones);
        foreach (var minion in _minions.ToArray())
        {
            minion.AgeSeconds += deltaSeconds;
            minion.Cooldown = Math.Max(0f, minion.Cooldown - deltaSeconds);
            if (!minion.IsSentry)
            {
                var angle = minion.AgeSeconds * 1.6f + droneIndex * MathF.Tau / drones;
                var goal = PlayerCenter + new Vector2(MathF.Cos(angle) * 2.2f, -1.6f + MathF.Sin(angle) * 0.8f);
                minion.Position = Vector2.Lerp(minion.Position, goal, Math.Min(1f, deltaSeconds * 6f));
                droneIndex++;
            }
            if (minion.Cooldown > 0f)
                continue;
            if (minion.Spec.Heals)
            {
                if (Player.HitPoints < Player.MaximumHitPoints)
                    Player.Heal(Math.Max(1f, minion.Damage * 0.5f));
                minion.Cooldown = minion.Spec.FireIntervalSeconds;
                continue;
            }
            var target = _enemies
                .Where(enemy => enemy.IsAlive && !enemy.IsPassive &&
                                Vector2.Distance(enemy.Motion.Position + enemy.Motion.Size / 2f, minion.Position) <= minion.Spec.Range)
                .OrderByDescending(enemy => enemy.Id == _whipMarkedEnemy)
                .ThenBy(enemy => Vector2.Distance(enemy.Motion.Position + enemy.Motion.Size / 2f, minion.Position))
                .FirstOrDefault();
            if (target is null)
            {
                minion.TargetId = null;
                continue;
            }
            minion.TargetId = target.Id;
            var aim = target.Motion.Position + target.Motion.Size / 2f - minion.Position;
            if (aim.LengthSquared() < 0.0001f)
                aim = Vector2.UnitX;
            var damage = minion.Damage * (_whipBuffSeconds > 0f && target.Id == _whipMarkedEnemy ? 1.3f : 1f);
            for (var shot = 0; shot < minion.Spec.Projectiles; shot++)
            {
                var spread = (shot - (minion.Spec.Projectiles - 1) / 2f) * 0.12f;
                var angle = MathF.Atan2(aim.Y, aim.X) + spread;
                // Drone and sentry shots get the same ±15% spread as weapons, but Command hits never crit.
                _projectiles.Add(new ProjectileState(_nextEntityId++, minion.Position,
                    new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * minion.Spec.ProjectileSpeed, RollUse(damage, 0).Damage, 1f, 2f, ProjectileTeam.Player)
                {
                    Class = DamageClass.Command, SpriteName = minion.SpriteName
                });
            }
            minion.Cooldown = minion.Spec.FireIntervalSeconds;
        }
    }

    /// <summary>Signal whips mark the struck enemy so drones focus it and hit harder for a few seconds.</summary>
    private void OnMeleeHit(ItemDefinition item, EnemyAgent enemy, float damage)
    {
        if (item.SignalWhip)
        {
            _whipMarkedEnemy = enemy.Id;
            _whipBuffSeconds = 4f;
        }
        if (item.EffectiveClass == DamageClass.Melee && CombatStats.Lifesteal > 0f)
            Player.Heal(damage * CombatStats.Lifesteal);
        GearOnHitEnemy(enemy, damage, item.EffectiveClass);
        ApplyWeaponAffliction(item, enemy);
    }

    /// <summary>Charge regeneration, minions and environmental hazards (acid, conveyors) each tick.</summary>
    private void UpdateContentPerTick(float deltaSeconds)
    {
        _chargeSicknessSeconds = Math.Max(0f, _chargeSicknessSeconds - deltaSeconds);
        UpdateChargeRegen(deltaSeconds);
        UpdateLifeRegen(deltaSeconds);
        UpdateMinions(deltaSeconds);
        UpdateWeaponStyles(deltaSeconds);
        UpdateHazards(deltaSeconds);
        GearTick(deltaSeconds);
    }

    /// <summary>
    /// Charge regeneration: after spending, a delay (longer the emptier the pool) counts down, twice as fast standing
    /// still or with the charge stim; then charge flows back at <see cref="CombatTuning.ChargeRegenUnits"/>, faster
    /// when standing still and when the pool is fuller.
    /// </summary>
    private void UpdateChargeRegen(float deltaSeconds)
    {
        var ticks = deltaSeconds * CombatTuning.TicksPerSecond;
        if (_chargeRegenDelayTicks > 0f)
        {
            _chargeRegenDelayTicks = Math.Max(0f, _chargeRegenDelayTicks -
                ticks * CombatTuning.ChargeDelayCountdownPerTick(PlayerStandingStill, ChargeRegenBuffActive));
            return;
        }
        var maximum = MaximumCharge;
        if (_charge >= 0f && _charge >= maximum)
            return;
        var units = CombatTuning.ChargeRegenUnits(Charge, maximum, ChargeRegenBonusUnits, PlayerStandingStill, ChargeRegenBuffActive);
        var gained = CombatTuning.RegenUnitsPerSecond(units) * deltaSeconds;
        _charge = Math.Min(maximum, Charge + gained); // regeneration never restarts the delay
    }

    private float _acidTimer;

    /// <summary>Acid harms whoever stands in it; conveyors carry actors standing on them.</summary>
    private void UpdateHazards(float deltaSeconds)
    {
        _acidTimer += deltaSeconds;
        var cell = ClampCell(RegionMap.CellAt(PlayerCenter));
        var feet = ClampCell(RegionMap.CellAt(Player.Motion.Position + new Vector2(Player.Motion.Size.X / 2f, Player.Motion.Size.Y - 0.1f)));
        if (_acidTimer >= 0.5f)
        {
            _acidTimer -= 0.5f;
            var acid = Math.Max(AcidDepth(cell), AcidDepth(feet));
            if (acid > 0.2f && !Player.Stats.ImmunityTags.Contains("acid"))
            {
                Player.TakeDamage(3f * acid, DamageKind.Burn);
                if (!_acidWarned)
                    PostMessage("The acid burns! Get out of the sea.");
                _acidWarned = true;
            }
            else
                _acidWarned = false;
            foreach (var agent in _colonists.Where(agent => agent.IsAlive))
            {
                var center = ClampCell(RegionMap.CellAt(agent.Motion.Position + agent.Motion.Size / 2f));
                if (AcidDepth(center) > 0.2f)
                    agent.TakeDamage(3f, DamageKind.Burn);
            }
        }
        var below = new GridPoint(feet.X, Math.Min(World.Height - 1, feet.Y + 1));
        if (Player.Motion.Grounded && ContentTiles.Has(World.GetCell(below).TileId, TileTraits.Conveyor))
        {
            var push = new Vector2(2.5f * deltaSeconds, 0f);
            if (!IsSolidActorPosition(Player.Motion.Position + push, Player.Motion.Size))
                Player.Motion.Position += push;
        }
    }

    private bool _acidWarned;

    /// <summary>Speed stims (+25%) and blizzards outdoors (−15%); 1 otherwise.</summary>
    private float ContentMoveFactor
    {
        get
        {
            var factor = 1f;
            if (Player.Statuses.Active.Any(status => status.Id == StatusId.Haste))
                factor *= 1.25f;
            if (_worldEventsField is not null && Weather.Snow > 0.5f && PlayerRoom is null)
                factor *= 0.85f;
            factor *= GearMoveFactor;
            return factor;
        }
    }

    private float AcidDepth(GridPoint cell)
    {
        var data = World.GetCell(cell);
        return data.LiquidKind == LiquidKind.Acid ? data.LiquidDepth : 0f;
    }

    /// <summary>Credits and event bookkeeping when a creature dies.</summary>
    private void OnCreatureDefeated(EnemyAgent enemy)
    {
        RecordDeath(enemy, DeathReason.Died); // PE-5: only real kills burst and sound (Systems/SoundEvents.cs)
        var credits = SalvageMarkCredits(enemy, enemy.Archetype.CreditDrop); // the salvage mark adds credits
        if (credits > 0)
            DropLoot(enemy, ItemId.Credits, credits);
        OnCreatureDefeatedForEvents(enemy);
        OnCreatureDefeatedBatchB(enemy);
        if (!BagReplacesBossLoot(enemy))
            OnCreatureDefeatedArsenal(enemy);
        OnCreatureDefeatedPlayerSystems(enemy);
    }

    /// <summary>Share of carried credits dropped where a Classic character died (Ironman drops all, Explorer none).</summary>
    public const float ClassicDeathCreditShare = 0.4f;

    /// <summary>On respawn: leaves the death-credit pile at the death site. Returns the credits dropped.</summary>
    private int DropDeathCredits(CharacterDifficulty difficulty)
    {
        var share = difficulty switch
        {
            CharacterDifficulty.Explorer or CharacterDifficulty.Creative => 0f,
            // Easy and Medium drop a share of the money; Medium's items are dropped below.
            CharacterDifficulty.Classic or CharacterDifficulty.Salvager => ClassicDeathCreditShare,
            _ => 1f
        };
        if (difficulty == CharacterDifficulty.Salvager)
            DropSalvagerStash(PlayerCenter);
        else if (difficulty is CharacterDifficulty.Hard or CharacterDifficulty.Ironman)
            DropSalvagerStash(PlayerCenter, keepTools: true);
        var carried = Player.Inventory.Count(ItemId.Credits);
        var drop = (int)MathF.Ceiling(carried * share);
        if (drop <= 0)
            return 0;
        var site = PlayerCenter;
        var left = drop;
        for (var slot = 0; slot < Player.Inventory.Slots.Count && left > 0; slot++)
        {
            if (Player.Inventory.Slots[slot].ItemId != ItemId.Credits || Player.Inventory.Slots[slot].IsEmpty)
                continue;
            left -= Player.Inventory.TakeFromSlot(slot, Math.Min(left, Player.Inventory.Slots[slot].Count)).Count;
        }
        var dropped = drop - left;
        if (dropped > 0)
            SpawnGroundItem(ItemId.Credits, dropped, site);
        return dropped;
    }

    /// <summary>Tools a Hard death leaves in the bag: drills, cutters, wall hammers, bug nets and utility tools.</summary>
    public static bool IsTool(ItemDefinition item) =>
        item.UseKind is ItemUseKind.Pickaxe or ItemUseKind.Cutter or ItemUseKind.Demolisher or ItemUseKind.BugNet or ItemUseKind.Utility;

    /// <summary>Extra seconds a Salvager death pile lasts beyond the normal ground-item lifetime.</summary>
    public const float SalvagerStashExtraSeconds = 1200f;

    /// <summary>
    /// Medium (Salvager) and Hard deaths: every carried stack except credits (those are dropped by their own share) drops as
    /// ground items at the death site, with a longer lifetime so the run back can recover it. Worn gear stays equipped.
    /// With <paramref name="keepTools"/> (Hard), mining and building tools stay in the bag. Returns stacks dropped.
    /// </summary>
    private int DropSalvagerStash(Vector2 site, bool keepTools = false)
    {
        var stacks = 0;
        for (var slot = 0; slot < Player.Inventory.Slots.Count; slot++)
        {
            var stack = Player.Inventory.Slots[slot];
            if (stack.IsEmpty || stack.ItemId == ItemId.Credits || keepTools && IsTool(Items[stack.ItemId]))
                continue;
            var taken = Player.Inventory.TakeFromSlot(slot, stack.Count);
            if (taken.IsEmpty)
                continue;
            var offset = new Vector2(((stacks % 7) - 3) * 0.35f, -0.2f);
            if (SpawnGroundItem(taken.ItemId, taken.Count, site + offset, taken.Calibration, pickupDelay: 1.5f) is { } pile)
                pile.AgeSeconds = -SalvagerStashExtraSeconds;
            stacks++;
        }
        if (stacks > 0)
            Flags.Increment("salvager.stashes");
        return stacks;
    }
}
