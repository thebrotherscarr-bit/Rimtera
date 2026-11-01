using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Renderer view of the player's current melee swing, stab or thrust.</summary>
/// <param name="AngleRadians">Aim direction for stabs and spears; the blade's current angle for broadswords.</param>
/// <param name="Progress">0..1 through the swing or thrust.</param>
public readonly record struct MeleeSwingView(WeaponStyle Style, ItemId Item, Vector2 Origin, float AngleRadians, float Reach, float Progress);

/// <summary>Renderer view of a held beam weapon.</summary>
public readonly record struct BeamView(Vector2 Start, Vector2 End, float Width);

/// <summary>Renderer view of a whip lash: a polyline from the hand to the tip.</summary>
public readonly record struct WhipView(IReadOnlyList<Vector2> Points, float Progress);

/// <summary>Renderer view of a recent launcher blast.</summary>
public readonly record struct ExplosionView(Vector2 Center, float Radius, float AgeSeconds);

/// <summary>
/// Weapon styles (ITEMS_DIFF.md "Missing mechanics" 1–2): the shape of each melee attack, tethered and
/// returning projectiles, ammo families, explosive and thrown shots, held beams and marking whips.
/// Items with <see cref="WeaponStyle.None"/> never reach this file and keep the generic behaviour.
/// All tuning numbers here are original.
/// </summary>
public sealed partial class GameSimulation
{
    private const float BroadswordArc = 2.3f;
    private const float WhipMarkSeconds = 4f;
    private const float YoyoHitInterval = 0.22f;
    private const float ExplosionShowSeconds = 0.5f;
    private const float ThrownGravity = 16f;
    private const int WhipSegments = 14;

    private sealed class ActiveSwing
    {
        public required WeaponStyle Style;
        public required ItemDefinition Item;
        public long StartTick;
        public int Duration;
        public float Aim;
        public float Sign;
        public float Reach;
        public float Damage;
        public bool Critical;
        public float Knockback;
        public float Progress;
        public float BladeAngle;
        public readonly HashSet<long> Hit = new();
        public Vector2[]? WhipPoints;
    }

    private sealed class ActiveBeam
    {
        public required ItemDefinition Item;
        public Calibration Calibration;
        public int Timer;
        public Vector2 Start;
        public Vector2 End;
    }

    private long _styleUseTick = -1;
    private Vector2 _styleAim;
    private ActiveSwing? _swing;
    private ActiveBeam? _beam;
    private readonly List<ExplosionView> _explosions = new();

    /// <summary>The player's current broadsword/shortsword/spear attack, or null.</summary>
    public MeleeSwingView? PlayerSwing => _swing is { Style: not WeaponStyle.Whip } swing
        ? new MeleeSwingView(swing.Style, swing.Item.Id, PlayerCenter,
            swing.Style == WeaponStyle.Broadsword ? swing.BladeAngle : swing.Aim, swing.Reach, swing.Progress)
        : null;

    /// <summary>The held beam, or null.</summary>
    public BeamView? PlayerBeam => _beam is { } beam ? new BeamView(beam.Start, beam.End, 0.25f + 0.03f * beam.Item.Tier) : null;

    /// <summary>The current whip lash, or null.</summary>
    public WhipView? PlayerWhip => _swing is { Style: WeaponStyle.Whip, WhipPoints: { } points } swing ? new WhipView(points, swing.Progress) : null;

    /// <summary>Launcher blasts from roughly the last half second.</summary>
    public IReadOnlyList<ExplosionView> RecentExplosions => _explosions;

    /// <summary>True while a whip's mark is on this creature (drones and sentries focus it and hit harder).</summary>
    public bool IsMarkedByWhip(EnemyAgent enemy) => enemy is not null && _whipBuffSeconds > 0f && enemy.Id == _whipMarkedEnemy;

    // ------------------------------------------------------------------ use dispatch

    /// <summary>Handles the selected item when it has a weapon style. Returns false to fall through to the generic use.</summary>
    private bool TryUseWeaponStyle(GridPoint point)
    {
        var item = Player.Inventory.SelectedItem;
        if (item is null || item.Style == WeaponStyle.None)
            return false;
        _styleUseTick = TickNumber;
        _styleAim = new Vector2(point.X + 0.5f, point.Y + 0.5f);
        switch (item.Style)
        {
            case WeaponStyle.SummonStaff or WeaponStyle.SentryStaff:
                return false; // existing drone/sentry command logic
            case WeaponStyle.Yoyo when FindStyled(item.Id, WeaponStyle.Yoyo) is not null:
                return true; // held: the yo-yo keeps following the aim
            case WeaponStyle.Beam when _beam is not null && _beam.Item.Id == item.Id:
                return true;
        }
        if (Player.ActionCooldownTicks > 0)
            return true;

        switch (item.Style)
        {
            case WeaponStyle.Broadsword or WeaponStyle.Shortsword or WeaponStyle.Spear or WeaponStyle.Whip:
                StartSwing(item);
                break;
            case WeaponStyle.Yoyo:
                ThrowYoyo(item);
                break;
            case WeaponStyle.Boomerang or WeaponStyle.Flail:
                ThrowReturning(item);
                break;
            case WeaponStyle.Bow or WeaponStyle.Gun or WeaponStyle.Shotgun or WeaponStyle.Launcher:
                if (item.UsesAmmo is AmmoClass.None or AmmoClass.Cell && item.AmmoType is not null)
                {
                    // Energy-cell and dedicated-ammo weapons keep the original firing code.
                    var before = _projectiles.Count;
                    AttackRanged(item, point);
                    TagStyled(before, item, null);
                }
                else
                    FireStyledRanged(item, point);
                break;
            case WeaponStyle.Thrown:
                ThrowConsumable(item, point);
                break;
            case WeaponStyle.Staff or WeaponStyle.Tome:
            {
                var before = _projectiles.Count;
                AttackTech(item, point);
                TagStyled(before, item, null);
                break;
            }
            case WeaponStyle.Beam:
                StartBeam(item);
                break;
            default:
                return false;
        }
        return true;
    }

    private ProjectileState? FindStyled(ItemId item, WeaponStyle style) =>
        _projectiles.FirstOrDefault(projectile => projectile.Style == style && projectile.SourceItem == item && projectile.Team == ProjectileTeam.Player);

    private float AimAngle()
    {
        var aim = _styleAim - PlayerCenter;
        if (aim.LengthSquared() < 0.001f)
            aim = new Vector2(Player.Motion.Facing, 0f);
        return MathF.Atan2(aim.Y, aim.X);
    }

    private static Vector2 Direction(float angle) => new(MathF.Cos(angle), MathF.Sin(angle));

    private void TagStyled(int firstIndex, ItemDefinition item, ItemDefinition? ammo)
    {
        for (var i = firstIndex; i < _projectiles.Count; i++)
        {
            var projectile = _projectiles[i];
            projectile.Style = item.Style;
            projectile.SourceItem = item.Id;
            projectile.Homing = Math.Max(projectile.Homing, Math.Max(item.AmmoHoming, ammo?.AmmoHoming ?? 0f));
            var explodes = item.Style == WeaponStyle.Launcher || item.ExplosionRadius > 0f ||
                           ammo is not null && (ammo.AmmoKind == AmmoClass.Rocket || ammo.ExplosionRadius > 0f);
            if (explodes)
                projectile.ExplosionRadius = Math.Max(Math.Max(item.ExplosionRadius, ammo?.ExplosionRadius ?? 0f), 2f + 0.25f * item.Tier);
        }
    }

    // ------------------------------------------------------------------ melee shapes

    private void StartSwing(ItemDefinition item)
    {
        var calibration = SelectedCalibration;
        var cooldown = Math.Max(1, ScaledCooldown(item, calibration));
        var aim = AimAngle();
        var (rolled, critical) = RollUse(ComputeWeaponDamage(item, calibration), CritChanceFor(item, calibration));
        var swing = new ActiveSwing
        {
            Style = item.Style, Item = item, StartTick = TickNumber, Aim = aim, Sign = MathF.Cos(aim) >= 0f ? 1f : -1f,
            Damage = rolled, Critical = critical,
            Knockback = item.Knockback * CalibrationCatalog.Get(calibration).KnockbackMultiplier
        };
        switch (item.Style)
        {
            case WeaponStyle.Broadsword:
                swing.Reach = item.Reach > 0f ? item.Reach : 2.4f + 0.15f * item.Tier;
                swing.Duration = Math.Clamp(cooldown, 6, 18);
                break;
            case WeaponStyle.Shortsword:
                swing.Reach = item.Reach > 0f ? item.Reach : 1.9f;
                swing.Duration = Math.Clamp(cooldown, 4, 8);
                break;
            case WeaponStyle.Spear:
                swing.Reach = item.Reach > 0f ? item.Reach : 5f;
                swing.Duration = Math.Clamp(cooldown, 8, 20);
                break;
            default:
                swing.Reach = item.Reach > 0f ? item.Reach : 6f;
                swing.Duration = Math.Clamp(cooldown, 10, 22);
                break;
        }
        // Size calibrations (Massive, Lengthened…) scale melee reach.
        swing.Reach *= CalibratedSize(calibration);
        swing.BladeAngle = aim - swing.Sign * BroadswordArc / 2f;
        _swing = swing;
        SetPlayerCooldown(cooldown);
    }

    private void UpdateSwing()
    {
        if (_swing is not { } swing)
            return;
        var elapsed = TickNumber - swing.StartTick + 1;
        swing.Progress = Math.Clamp(elapsed / (float)swing.Duration, 0f, 1f);
        var origin = PlayerCenter;
        var p = swing.Progress;
        var direction = Direction(swing.Aim);
        CutPlantsAlong(origin, Direction(swing.BladeAngle), swing.Reach); // flora parity: the blade cuts plants it passes
        switch (swing.Style)
        {
            case WeaponStyle.Broadsword:
            {
                var start = swing.Aim - swing.Sign * BroadswordArc / 2f;
                swing.BladeAngle = start + swing.Sign * BroadswordArc * p;
                foreach (var enemy in LiveTargets(swing.Hit))
                {
                    var center = enemy.Motion.Position + enemy.Motion.Size / 2f;
                    var offset = center - origin;
                    var radius = MathF.Max(enemy.Motion.Size.X, enemy.Motion.Size.Y) * 0.5f;
                    if (offset.Length() > swing.Reach + radius)
                        continue;
                    var swept = MathF.IEEERemainder(MathF.Atan2(offset.Y, offset.X) - start, MathF.Tau) * swing.Sign;
                    if (offset.Length() < 0.9f || swept >= -0.3f && swept <= BroadswordArc * p + 0.3f)
                        SwingHit(swing, enemy, origin);
                }
                break;
            }
            case WeaponStyle.Shortsword or WeaponStyle.Spear:
            {
                float extension;
                if (swing.Style == WeaponStyle.Shortsword)
                    extension = MathF.Sin(MathF.PI * p);
                else
                    extension = p < 0.45f ? p / 0.45f : p < 0.6f ? 1f : (1f - p) / 0.4f;
                var tip = origin + direction * (0.5f + (swing.Reach - 0.5f) * extension);
                foreach (var enemy in LiveTargets(swing.Hit))
                    if (SegmentDistance(enemy, origin, tip) <= 0.3f)
                        SwingHit(swing, enemy, origin);
                break;
            }
            case WeaponStyle.Whip:
            {
                var points = swing.WhipPoints ??= new Vector2[WhipSegments + 1];
                var normal = new Vector2(-direction.Y, direction.X) * swing.Sign;
                var reach = swing.Reach * MathF.Sin(MathF.PI * MathF.Min(1f, p * 1.15f));
                for (var k = 0; k <= WhipSegments; k++)
                {
                    var t = k / (float)WhipSegments;
                    var curl = MathF.Sin(t * MathF.PI * 1.5f - p * MathF.Tau) * 0.7f * t * (1f - 0.6f * p);
                    points[k] = origin + direction * (t * reach) + normal * curl;
                }
                foreach (var enemy in LiveTargets(swing.Hit))
                    for (var k = 0; k < WhipSegments; k++)
                        if (SegmentDistance(enemy, points[k], points[k + 1]) <= 0.3f)
                        {
                            SwingHit(swing, enemy, origin);
                            break;
                        }
                break;
            }
        }
        if (elapsed >= swing.Duration)
            _swing = null;
    }

    private IEnumerable<EnemyAgent> LiveTargets(HashSet<long> exclude) =>
        _enemies.Where(enemy => enemy.IsAlive && !exclude.Contains(enemy.Id)).ToArray();

    private float SegmentDistance(EnemyAgent enemy, Vector2 a, Vector2 b)
    {
        var center = enemy.Motion.Position + enemy.Motion.Size / 2f;
        var ab = b - a;
        var t = ab.LengthSquared() < 1e-5f ? 0f : Math.Clamp(Vector2.Dot(center - a, ab) / ab.LengthSquared(), 0f, 1f);
        return EnemyProjectileDistance(enemy, a + ab * t);
    }

    private void SwingHit(ActiveSwing swing, EnemyAgent enemy, Vector2 origin)
    {
        swing.Hit.Add(enemy.Id);
        var dealt = StyleHit(swing.Item, enemy, swing.Damage, swing.Knockback, origin, swing.Critical);
        PostMessage(enemy.IsAlive ? $"Hit {enemy.DisplayName} for {dealt:0} damage." : $"Defeated {enemy.DisplayName}.");
    }

    /// <summary>One styled hit with an already rolled use, defended in <see cref="StrikeEnemy"/>. Returns the damage dealt.</summary>
    private float StyleHit(ItemDefinition? item, EnemyAgent enemy, float damage, float knockback, Vector2 from, bool critical)
    {
        damage = StrikeEnemy(enemy, damage, critical, item?.EffectiveClass ?? DamageClass.None);
        ApplyWeaponAffliction(item, enemy);
        var center = enemy.Motion.Position + enemy.Motion.Size / 2f;
        var away = center.X >= from.X ? 1f : -1f;
        KnockEnemy(enemy, new Vector2(away * knockback, -knockback * 0.2f), LastHitCritical);
        if (item is null)
            return damage;
        if (item.Style == WeaponStyle.Whip)
        {
            _whipMarkedEnemy = enemy.Id;
            _whipBuffSeconds = WhipMarkSeconds;
        }
        OnMeleeHit(item, enemy, damage);
        return damage;
    }

    // ------------------------------------------------------------------ tethered and returning projectiles

    /// <summary>A thrown styled weapon: its spread and crit are rolled once per throw.</summary>
    private ProjectileState NewStyledProjectile(ItemDefinition item, Vector2 velocity, float lifetime, float damage, float knockback, int critChance)
    {
        var (rolled, critical) = RollUse(damage, critChance);
        return new(_nextEntityId++, PlayerCenter, velocity, rolled, knockback, lifetime, ProjectileTeam.Player)
        {
            Class = item.EffectiveClass, CritChance = critChance, Critical = critical, SpriteName = SnakeName(item.Id), Style = item.Style,
            SourceItem = item.Id
        };
    }

    private void ThrowYoyo(ItemDefinition item)
    {
        var calibration = SelectedCalibration;
        var crit = CritChanceFor(item, calibration);
        var projectile = NewStyledProjectile(item, Vector2.Zero, 3f + 0.75f * item.Tier,
            ComputeWeaponDamage(item, calibration), item.Knockback * CalibrationCatalog.Get(calibration).KnockbackMultiplier, crit);
        projectile.Tethered = true;
        projectile.MaxRange = item.Reach > 0f ? item.Reach : 4f + 0.6f * Math.Max(1, item.Tier);
        _projectiles.Add(projectile);
        SetPlayerCooldown(ScaledCooldown(item, calibration));
    }

    private void ThrowReturning(ItemDefinition item)
    {
        var flail = item.Style == WeaponStyle.Flail;
        if (_projectiles.Count(projectile => projectile.SourceItem == item.Id && projectile.Style == item.Style) >= 1)
            return; // one out at a time
        var calibration = SelectedCalibration;
        var crit = CritChanceFor(item, calibration);
        var speed = flail ? MathF.Max(14f, item.ProjectileSpeed) : MathF.Max(10f, item.ProjectileSpeed);
        var projectile = NewStyledProjectile(item, Direction(AimAngle()) * speed, 6f,
            ComputeWeaponDamage(item, calibration), item.Knockback * CalibrationCatalog.Get(calibration).KnockbackMultiplier, crit);
        projectile.Tethered = flail;
        projectile.MaxRange = item.Reach > 0f ? item.Reach : flail ? 5f : 6f + 0.75f * item.Tier;
        _projectiles.Add(projectile);
        SetPlayerCooldown(ScaledCooldown(item, calibration));
        PostMessage(flail ? $"Swung the {DescribeStack(Player.Inventory.SelectedStack)}." : $"Threw the {DescribeStack(Player.Inventory.SelectedStack)}.");
    }

    /// <summary>One-line hook from <c>UpdateProjectiles</c>: returns true when this file fully stepped (or removed) the projectile.</summary>
    private bool StepStyledProjectile(int index, float deltaSeconds)
    {
        var projectile = _projectiles[index];
        if (projectile.Team != ProjectileTeam.Player)
            return false;
        bool remove;
        switch (projectile.Style)
        {
            case WeaponStyle.Yoyo:
                remove = StepYoyo(projectile, deltaSeconds);
                break;
            case WeaponStyle.Boomerang or WeaponStyle.Flail:
                remove = StepReturning(projectile, deltaSeconds);
                break;
            default:
                if (projectile.Gravity > 0f)
                    projectile.Velocity += new Vector2(0f, projectile.Gravity * deltaSeconds);
                if (projectile.ExplosionRadius <= 0f)
                    return false;
                remove = StepExplosive(projectile, deltaSeconds);
                break;
        }
        if (remove)
            _projectiles.RemoveAt(index);
        return true;
    }

    /// <summary>Moves in small steps; returns true when a solid tile stopped it.</summary>
    private bool MoveStyled(ProjectileState projectile, Vector2 delta, bool solidStops, Func<bool>? afterStep = null)
    {
        var steps = Math.Max(1, (int)MathF.Ceiling(delta.Length() / 0.2f));
        var increment = delta / steps;
        // Shaped melee weapons cut plants they pass, like the reference's Projectile.CutTilesAt.
        var cutsPlants = projectile.Team == ProjectileTeam.Player &&
                         projectile.Style is WeaponStyle.Yoyo or WeaponStyle.Boomerang or WeaponStyle.Flail;
        for (var step = 0; step < steps; step++)
        {
            var next = projectile.Position + increment;
            if (solidStops)
            {
                var tile = new GridPoint((int)MathF.Floor(next.X), (int)MathF.Floor(next.Y));
                if (!World.Contains(tile) || Tiles[World.GetCell(tile).TileId].IsSolid)
                    return true;
            }
            projectile.Position = next;
            projectile.Traveled += increment.Length();
            if (cutsPlants)
                CutPlantAt(new GridPoint((int)MathF.Floor(next.X), (int)MathF.Floor(next.Y)), FloraCut.Projectile);
            if (afterStep?.Invoke() == true)
                return false;
        }
        return false;
    }

    private void HitAlong(ProjectileState projectile, bool useCooldowns, float deltaSeconds)
    {
        if (useCooldowns && projectile.HitCooldowns is { } cooldowns)
            foreach (var key in cooldowns.Keys.ToArray())
                cooldowns[key] -= deltaSeconds;
        var item = projectile.SourceItem is { } id ? Items[id] : null;
        foreach (var enemy in _enemies)
        {
            if (!enemy.IsAlive || EnemyProjectileDistance(enemy, projectile.Position) > 0.35f)
                continue;
            if (useCooldowns)
            {
                var cooldowns2 = projectile.HitCooldowns ??= new Dictionary<long, float>();
                if (cooldowns2.TryGetValue(enemy.Id, out var left) && left > 0f)
                    continue;
                cooldowns2[enemy.Id] = YoyoHitInterval;
            }
            else
            {
                var hit = projectile.HitEnemies ??= new HashSet<long>();
                if (!hit.Add(enemy.Id))
                    continue;
            }
            StyleHit(item, enemy, projectile.Damage, projectile.Knockback, projectile.Position - projectile.Velocity * 0.01f, projectile.Critical);
        }
    }

    private bool StepYoyo(ProjectileState projectile, float deltaSeconds)
    {
        var item = projectile.SourceItem is { } id ? Items[id] : null;
        var held = _styleUseTick == TickNumber && Player.Inventory.SelectedItem?.Id == projectile.SourceItem && !Player.IsDowned;
        projectile.RemainingSeconds -= deltaSeconds;
        if (!held || projectile.RemainingSeconds <= 0f)
            projectile.Returning = true;
        var speed = MathF.Max(8f, (item?.ProjectileSpeed ?? 10f) * 1.2f);
        var before = projectile.Position;
        if (projectile.Returning)
        {
            var home = PlayerCenter - projectile.Position;
            if (home.Length() <= speed * 1.5f * deltaSeconds + 0.4f)
                return true;
            MoveStyled(projectile, Vector2.Normalize(home) * speed * 1.5f * deltaSeconds, false);
        }
        else
        {
            var goal = _styleAim - PlayerCenter;
            if (goal.Length() > projectile.MaxRange)
                goal = Vector2.Normalize(goal) * projectile.MaxRange;
            var toGoal = PlayerCenter + goal - projectile.Position;
            var stepLength = MathF.Min(toGoal.Length(), speed * deltaSeconds);
            if (stepLength > 0.001f)
                MoveStyled(projectile, Vector2.Normalize(toGoal) * stepLength, true);
            // The tether never stretches past its range, even when the player walks away.
            var tether = projectile.Position - PlayerCenter;
            if (tether.Length() > projectile.MaxRange)
                projectile.Position = PlayerCenter + Vector2.Normalize(tether) * projectile.MaxRange;
        }
        projectile.Velocity = (projectile.Position - before) / deltaSeconds;
        HitAlong(projectile, true, deltaSeconds);
        return false;
    }

    private bool StepReturning(ProjectileState projectile, float deltaSeconds)
    {
        projectile.RemainingSeconds -= deltaSeconds;
        if (projectile.RemainingSeconds <= 0f)
            return true;
        if (!projectile.Returning)
        {
            var blocked = MoveStyled(projectile, projectile.Velocity * deltaSeconds, true,
                () => { HitAlong(projectile, false, deltaSeconds); return false; });
            if (blocked || projectile.Traveled >= projectile.MaxRange)
            {
                projectile.Returning = true;
                projectile.HitEnemies?.Clear(); // a fresh set of hits on the way back
            }
            return false;
        }
        var flail = projectile.Style == WeaponStyle.Flail;
        var baseSpeed = MathF.Max(flail ? 14f : 10f, projectile.SourceItem is { } id ? Items[id].ProjectileSpeed : 0f);
        var returnSpeed = baseSpeed * (flail ? 1.3f : 1.1f);
        var home = PlayerCenter - projectile.Position;
        if (home.Length() <= returnSpeed * deltaSeconds + 0.6f)
            return true;
        projectile.Velocity = Vector2.Normalize(home) * returnSpeed;
        MoveStyled(projectile, projectile.Velocity * deltaSeconds, false, () => { HitAlong(projectile, false, deltaSeconds); return false; });
        return false;
    }

    // ------------------------------------------------------------------ ranged: ammo families, launchers, thrown

    private ItemId? FindAmmo(ItemDefinition item)
    {
        // The ammo slots are searched first, as Terraria fires from them before the bag.
        if (item.UsesAmmo != AmmoClass.None)
            foreach (var slot in Player.Inventory.UseOrder())
                if (Player.Inventory.Slots[slot] is { IsEmpty: false } stack && Items[stack.ItemId].AmmoKind == item.UsesAmmo)
                    return stack.ItemId;
        if (item.AmmoType is { } fallback && Player.Inventory.Count(fallback) > 0)
            return fallback;
        return null;
    }

    private void FireStyledRanged(ItemDefinition item, GridPoint target)
    {
        if (_storyteller.SolarFlareActive)
        {
            PostMessage($"The solar flare has knocked the {item.Name} offline.");
            SetPlayerCooldown(item.UseCooldownTicks);
            return;
        }
        if (FindAmmo(item) is not { } ammoId)
        {
            PostMessage(item.UsesAmmo != AmmoClass.None ? $"Out of {item.UsesAmmo.ToString().ToLowerInvariant()} ammunition." : "No compatible ammunition.");
            return;
        }
        var ammo = Items[ammoId];
        var ammoSave = CombatStats.AmmoSaveChance;
        if (ammoSave <= 0f || CombatRandom.NextDouble() >= ammoSave)
            Player.Inventory.TryConsume(ammoId);
        var calibration = SelectedCalibration;
        var damage = ComputeWeaponDamage(item, calibration, ammo.AmmoDamage);
        var count = item.Style == WeaponStyle.Shotgun && item.ProjectileCount < 2 ? 4 : item.ProjectileCount;
        var spread = item.Style == WeaponStyle.Shotgun ? MathF.Max(0.06f, item.ProjectileSpread) : item.ProjectileSpread;
        var before = _projectiles.Count;
        FireProjectiles(item, target, damage, item.Knockback * CalibrationCatalog.Get(calibration).KnockbackMultiplier,
            item.ProjectileSpeed, count, spread, item.ProjectileLifetime, item.Pierce + ammo.Pierce,
            CritChanceFor(item, calibration), item.EffectiveClass, item.PurifyRadius);
        TagStyled(before, item, ammo);
        SetPlayerCooldown(ScaledCooldown(item, calibration));
        PostMessage($"Fired the {DescribeStack(Player.Inventory.SelectedStack)} ({ammo.Name}).");
    }

    private void ThrowConsumable(ItemDefinition item, GridPoint target)
    {
        var calibration = SelectedCalibration;
        var label = DescribeStack(Player.Inventory.SelectedStack);
        if (!Player.Inventory.TryConsume(item.Id))
        {
            PostMessage($"No {item.Name} left to throw.");
            return;
        }
        var before = _projectiles.Count;
        FireProjectiles(item, target, ComputeWeaponDamage(item, calibration), item.Knockback * CalibrationCatalog.Get(calibration).KnockbackMultiplier,
            MathF.Max(8f, item.ProjectileSpeed), 1, 0f, MathF.Max(1.5f, item.ProjectileLifetime), item.Pierce,
            CritChanceFor(item, calibration), item.EffectiveClass);
        TagStyled(before, item, null);
        for (var i = before; i < _projectiles.Count; i++)
            _projectiles[i].Gravity = ThrownGravity;
        SetPlayerCooldown(ScaledCooldown(item, calibration));
        PostMessage($"Threw a {label}.");
    }

    private bool StepExplosive(ProjectileState projectile, float deltaSeconds)
    {
        projectile.RemainingSeconds -= deltaSeconds;
        var struck = false;
        var blocked = MoveStyled(projectile, projectile.Velocity * deltaSeconds, true, () =>
        {
            struck = _enemies.Any(enemy => enemy.IsAlive && EnemyProjectileDistance(enemy, projectile.Position) <= 0.35f);
            return struck;
        });
        if (!blocked && !struck && projectile.RemainingSeconds > 0f)
            return false;
        Explode(projectile.Position, projectile.ExplosionRadius, projectile.Damage, projectile.Knockback,
            projectile.SourceItem is { } id ? Items[id] : null, projectile.Critical);
        return true;
    }

    /// <summary>Area damage to creatures only: blasts never break tiles and never hurt the player.</summary>
    private void Explode(Vector2 center, float radius, float damage, float knockback, ItemDefinition? source, bool critical = false)
    {
        SoundEvents.Emit(SoundEventKind.Explosion, string.Empty, center, (int)MathF.Round(radius));
        foreach (var enemy in _enemies.Where(enemy => enemy.IsAlive && EnemyProjectileDistance(enemy, center) <= radius).ToArray())
        {
            var dealt = StrikeEnemy(enemy, damage, critical, source?.EffectiveClass ?? DamageClass.None);
            ApplyWeaponAffliction(source, enemy);
            var push = enemy.Motion.Position + enemy.Motion.Size / 2f - center;
            push = push.LengthSquared() < 1e-4f ? -Vector2.UnitY : Vector2.Normalize(push);
            KnockEnemy(enemy, push * knockback, LastHitCritical);
            if (source is not null && source.EffectiveClass == DamageClass.Melee && CombatStats.Lifesteal > 0f)
                Player.Heal(dealt * CombatStats.Lifesteal);
        }
        _explosions.Add(new ExplosionView(center, radius, 0f));
    }

    // ------------------------------------------------------------------ beam

    private void StartBeam(ItemDefinition item)
    {
        var calibration = SelectedCalibration;
        var cost = ChargeCostFor(item, calibration);
        if (Charge < cost)
        {
            PostMessage($"Not enough charge ({Charge:0}/{cost:0}).");
            return;
        }
        _beam = new ActiveBeam { Item = item, Calibration = calibration, Timer = 0, Start = PlayerCenter, End = PlayerCenter };
    }

    private void UpdateBeam()
    {
        if (_beam is not { } beam)
            return;
        var item = beam.Item;
        if (_styleUseTick != TickNumber || Player.Inventory.SelectedItem?.Id != item.Id || Player.IsDowned)
        {
            _beam = null;
            return;
        }
        var start = PlayerCenter;
        var direction = Direction(AimAngle());
        var range = Math.Clamp(item.ProjectileSpeed * item.ProjectileLifetime, 6f, 22f);
        var end = start;
        for (var travelled = 0f; travelled < range; travelled += 0.25f)
        {
            var next = start + direction * (travelled + 0.25f);
            var tile = new GridPoint((int)MathF.Floor(next.X), (int)MathF.Floor(next.Y));
            if (!World.Contains(tile) || Tiles[World.GetCell(tile).TileId].IsSolid)
                break;
            end = next;
        }
        beam.Start = start;
        beam.End = end;
        if (beam.Timer-- > 0)
            return;
        var cost = ChargeCostFor(item, beam.Calibration);
        if (Charge < cost)
        {
            PostMessage($"The {item.Name} sputters out ({Charge:0}/{cost:0} charge).");
            _beam = null;
            return;
        }
        Charge -= cost;
        var (damage, crit) = RollUse(ComputeWeaponDamage(item, beam.Calibration), CritChanceFor(item, beam.Calibration));
        foreach (var enemy in _enemies.Where(enemy => enemy.IsAlive).ToArray())
            if (SegmentDistance(enemy, start, end) <= 0.3f)
                StyleHit(item, enemy, damage, item.Knockback * 0.3f, start, crit);
        beam.Timer = Math.Max(6, ScaledCooldown(item, beam.Calibration)) - 1;
    }

    // ------------------------------------------------------------------ per tick

    /// <summary>One-line hook from <c>UpdateContentPerTick</c>: swings, beams, homing shots and blast fade-out.</summary>
    private void UpdateWeaponStyles(float deltaSeconds)
    {
        UpdateSwing();
        UpdateBeam();
        for (var i = _explosions.Count - 1; i >= 0; i--)
        {
            var aged = _explosions[i] with { AgeSeconds = _explosions[i].AgeSeconds + deltaSeconds };
            if (aged.AgeSeconds > ExplosionShowSeconds)
                _explosions.RemoveAt(i);
            else
                _explosions[i] = aged;
        }
        foreach (var projectile in _projectiles)
        {
            if (projectile.Team != ProjectileTeam.Player || projectile.Homing <= 0f || projectile.Style is WeaponStyle.Yoyo or
                    WeaponStyle.Boomerang or WeaponStyle.Flail)
                continue;
            var target = _enemies
                .Where(enemy => enemy.IsAlive && !enemy.IsPassive &&
                                Vector2.Distance(enemy.Motion.Position + enemy.Motion.Size / 2f, projectile.Position) <= 12f)
                .OrderBy(enemy => Vector2.Distance(enemy.Motion.Position + enemy.Motion.Size / 2f, projectile.Position))
                .FirstOrDefault();
            var speed = projectile.Velocity.Length();
            if (target is null || speed < 0.01f)
                continue;
            var to = target.Motion.Position + target.Motion.Size / 2f - projectile.Position;
            var current = MathF.Atan2(projectile.Velocity.Y, projectile.Velocity.X);
            var delta = MathF.IEEERemainder(MathF.Atan2(to.Y, to.X) - current, MathF.Tau);
            var turn = Math.Clamp(delta, -projectile.Homing * deltaSeconds, projectile.Homing * deltaSeconds);
            projectile.Velocity = Direction(current + turn) * speed;
        }
    }
}
