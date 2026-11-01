using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Original creature behaviour families layered over the shared enemy loop: segmented burrowing worms,
/// teleporting casters with slow homing bolts, hovering divers, container mimics, and three hopper styles.
/// </summary>
public sealed partial class GameSimulation
{
    public const float WormSegmentSpacing = 0.85f;
    private const float CasterTeleportSeconds = 3.8f;
    private const float DiverCycleSeconds = 2.6f;
    private float _familySpawnTimer;
    private Random? _familyRandomField;
    private Random FamilyRandom => _familyRandomField ??= new Random(Seed ^ 0x0FA417E5);

    /// <summary>Ambient spawns of the Batch B creatures (on their own random stream). Tools may switch them off.</summary>
    public bool FamilySpawnsEnabled { get; set; } = true;

    /// <summary>Moves a creature with its family's behaviour. False when the legacy movement should run instead.</summary>
    private bool StepAiFamily(EnemyAgent enemy, Vector2 playerCenter, float distance, float alertRadius, float deltaSeconds)
    {
        switch (enemy.Family)
        {
            case EnemyAiFamily.Worm:
                StepWorm(enemy, playerCenter, distance, alertRadius, deltaSeconds);
                return true;
            case EnemyAiFamily.Caster:
                StepCaster(enemy, playerCenter, distance, alertRadius, deltaSeconds);
                return true;
            case EnemyAiFamily.DivingFlyer:
                StepDiver(enemy, playerCenter, distance, alertRadius, deltaSeconds);
                return true;
            case EnemyAiFamily.Mimic:
                StepMimic(enemy, playerCenter, distance, deltaSeconds);
                return true;
            case EnemyAiFamily.Hopper or EnemyAiFamily.LeapingHopper or EnemyAiFamily.SkitteringHopper:
                StepHopper(enemy, playerCenter, distance, alertRadius, deltaSeconds);
                return true;
            default:
                return false;
        }
    }

    // ------------------------------------------------------------------ worm

    private void StepWorm(EnemyAgent enemy, Vector2 playerCenter, float distance, float alertRadius, float deltaSeconds)
    {
        var motion = enemy.Motion;
        var center = motion.Position + motion.Size / 2f;
        var segments = enemy.SegmentList;
        if (segments.Count == 0)
            for (var i = 1; i <= enemy.Archetype.SegmentCount; i++)
                segments.Add(center + new Vector2(0f, i * WormSegmentSpacing));

        var burrowed = SolidAt(center);
        var velocity = motion.Velocity;
        var speed = 7f * enemy.Archetype.MoveSpeedMultiplier;
        if (burrowed)
        {
            // Underground the head steers freely toward the target (or wanders).
            var goal = distance <= alertRadius
                ? playerCenter
                : center + new Vector2(MathF.Sin((TickNumber + enemy.Id * 17) * 0.02f) * 6f, MathF.Cos((TickNumber + enemy.Id * 11) * 0.015f) * 3f);
            var desired = goal - center;
            if (desired.LengthSquared() > 0.01f)
                desired = Vector2.Normalize(desired) * speed;
            velocity = Vector2.Lerp(velocity, desired, Math.Min(1f, deltaSeconds * 2.2f));
        }
        else
        {
            // In open air it arcs under gravity until it dives back into the ground.
            velocity.Y = Math.Min(velocity.Y + 18f * deltaSeconds, 14f);
            velocity.X = MoveToward(velocity.X, Math.Sign(playerCenter.X - center.X) * speed * 0.6f, 4f * deltaSeconds);
        }
        motion.Velocity = velocity;
        var next = motion.Position + velocity * deltaSeconds;
        motion.Position = Vector2.Clamp(next, Vector2.Zero, new Vector2(World.Width, World.Height) - motion.Size);
        if (MathF.Abs(velocity.X) > 0.05f)
            motion.Facing = Math.Sign(velocity.X);

        // Segments trail the head at a fixed spacing.
        var leader = motion.Position + motion.Size / 2f;
        for (var i = 0; i < segments.Count; i++)
        {
            var offset = segments[i] - leader;
            var length = offset.Length();
            if (length > WormSegmentSpacing)
                segments[i] = leader + offset / length * WormSegmentSpacing;
            leader = segments[i];
        }

        // Every segment bites on contact.
        if (enemy.AttackCooldown <= 0f && segments.Any(segment => SegmentTouchesPlayer(enemy, segment))) // segment box vs player box
        {
            var damage = HitPlayerFromEnemy(enemy, ScaleIncoming(enemy.AttackDamage * 0.75f), DamageKind.Bite);
            enemy.AttackCooldown = enemy.Archetype.ContactCooldown;
            if (damage > 0f)
                PostMessage($"The {enemy.DisplayName}'s body slams you for {damage:0}.");
        }
        enemy.State = distance <= alertRadius ? EnemyState.Pursuing : EnemyState.Roaming;
    }

    // ------------------------------------------------------------------ caster

    private void StepCaster(EnemyAgent enemy, Vector2 playerCenter, float distance, float alertRadius, float deltaSeconds)
    {
        enemy.AiTimer += deltaSeconds;
        enemy.State = distance <= alertRadius ? EnemyState.Pursuing : EnemyState.Roaming;
        // Casters shuffle slowly; their threat is the teleport-and-bolt cycle.
        _enemyMovement.Step(enemy.Motion, new MovementInput(0f), deltaSeconds, World, Tiles);
        if (distance > alertRadius)
            return;
        if (enemy.AiStep == 0 && enemy.AiTimer >= CasterTeleportSeconds)
        {
            if (TryFindCasterSpot(enemy, playerCenter, out var spot))
            {
                enemy.Motion.Position = spot;
                enemy.Motion.Velocity = Vector2.Zero;
                enemy.Lunging = true;
            }
            enemy.AiStep = 1;
            enemy.AiTimer = 0f;
        }
        else if (enemy.AiStep == 1 && enemy.AiTimer >= 0.7f)
        {
            var origin = enemy.Motion.Position + enemy.Motion.Size * new Vector2(0.5f, 0.3f);
            var aim = playerCenter - origin;
            if (aim.LengthSquared() < 0.01f)
                aim = Vector2.UnitX;
            for (var shot = 0; shot < 3; shot++)
            {
                var angle = (shot - 1) * 0.35f;
                var direction = Vector2.Normalize(aim);
                direction = new Vector2(direction.X * MathF.Cos(angle) - direction.Y * MathF.Sin(angle),
                    direction.X * MathF.Sin(angle) + direction.Y * MathF.Cos(angle));
                _projectiles.Add(new ProjectileState(_nextEntityId++, origin, direction * 4.5f, enemy.AttackDamage * DefenseBalance.EnemyHitScale(enemy.Archetype) * 0.8f,
                    1.5f, 5f, ProjectileTeam.Enemy, enemy.DisplayName, DamageKind.Burn)
                { Homing = 1.6f, SpriteName = "bolt_homing", SourceKind = enemy.Kind });
            }
            enemy.Lunging = false;
            enemy.AiStep = 0;
            enemy.AiTimer = 0f;
        }
    }

    private bool TryFindCasterSpot(EnemyAgent enemy, Vector2 playerCenter, out Vector2 spot)
    {
        var size = enemy.Motion.Size;
        for (var attempt = 0; attempt < 24; attempt++)
        {
            var dx = (FamilyRandom.Next(2) == 0 ? -1 : 1) * FamilyRandom.Next(5, 12);
            var x = Math.Clamp((int)playerCenter.X + dx, 1, World.Width - 2);
            for (var dy = -6; dy <= 6; dy++)
            {
                var y = (int)playerCenter.Y + dy;
                if (y < 2 || y >= World.Height - 1)
                    continue;
                var candidate = new Vector2(x + 0.5f - size.X / 2f, y - size.Y);
                if (!IsSolidActorPosition(candidate, size) && Tiles[World.GetCell(new GridPoint(x, y)).TileId].IsSolid)
                {
                    spot = candidate;
                    return true;
                }
            }
        }
        spot = default;
        return false;
    }

    /// <summary>Homing bolts steer toward the player at their turn rate.</summary>
    private void UpdateHomingProjectiles(float deltaSeconds)
    {
        foreach (var projectile in _projectiles)
        {
            if (projectile.Homing <= 0f || projectile.Team != ProjectileTeam.Enemy)
                continue;
            var speed = projectile.Velocity.Length();
            var toPlayer = PlayerCenter - projectile.Position;
            if (speed < 0.01f || toPlayer.LengthSquared() < 0.01f)
                continue;
            var current = MathF.Atan2(projectile.Velocity.Y, projectile.Velocity.X);
            var wanted = MathF.Atan2(toPlayer.Y, toPlayer.X);
            var delta = MathF.IEEERemainder(wanted - current, MathF.Tau);
            var turn = Math.Clamp(delta, -projectile.Homing * deltaSeconds, projectile.Homing * deltaSeconds);
            projectile.Velocity = new Vector2(MathF.Cos(current + turn), MathF.Sin(current + turn)) * speed;
        }
    }

    // ------------------------------------------------------------------ diving flyer

    private void StepDiver(EnemyAgent enemy, Vector2 playerCenter, float distance, float alertRadius, float deltaSeconds)
    {
        enemy.AiTimer += deltaSeconds;
        enemy.State = distance <= alertRadius ? EnemyState.Pursuing : EnemyState.Roaming;
        if (distance > alertRadius)
        {
            MoveFlyingEnemy(enemy, playerCenter, deltaSeconds, pursuing: false, enemy.Archetype.MoveSpeedMultiplier);
            return;
        }
        var center = enemy.Motion.Position + enemy.Motion.Size / 2f;
        if (!enemy.Lunging)
        {
            var hover = playerCenter + new Vector2(MathF.Sin((TickNumber + enemy.Id * 13) * 0.03f) * 4f, -6f);
            MoveFlyingEnemy(enemy, hover, deltaSeconds, pursuing: true, enemy.Archetype.MoveSpeedMultiplier * 1.4f);
            if (enemy.AiTimer >= DiverCycleSeconds && Vector2.Distance(center, hover) < 3f)
            {
                enemy.Lunging = true;
                enemy.AiTimer = 0f;
                enemy.Motion.Velocity = Vector2.Normalize(playerCenter - center) * 10f;
            }
            return;
        }
        // Dive in a straight line, then pull up.
        var step = enemy.Motion.Velocity * deltaSeconds;
        var candidate = enemy.Motion.Position + step;
        if (enemy.AiTimer > 0.8f || IsSolidActorPosition(candidate, enemy.Motion.Size))
        {
            enemy.Lunging = false;
            enemy.AiTimer = 0f;
            enemy.Motion.Velocity = new Vector2(enemy.Motion.Velocity.X * 0.3f, -4f);
            return;
        }
        enemy.Motion.Position = candidate;
    }

    // ------------------------------------------------------------------ mimic

    private void StepMimic(EnemyAgent enemy, Vector2 playerCenter, float distance, float deltaSeconds)
    {
        if (enemy.AiStep == 0)
        {
            // Fresh mimics start disguised.
            enemy.Disguised = true;
            enemy.AiStep = 1;
        }
        if (enemy.Disguised)
        {
            enemy.State = EnemyState.Roaming;
            enemy.AttackCooldown = Math.Max(enemy.AttackCooldown, 0.5f);
            _enemyMovement.Step(enemy.Motion, new MovementInput(0f), deltaSeconds, World, Tiles);
            if (distance < 2.6f || enemy.HitPoints < enemy.MaximumHitPoints)
            {
                enemy.Disguised = false;
                enemy.AttackCooldown = 0.4f;
                PostMessage($"The container was a {enemy.DisplayName}!");
            }
            return;
        }
        StepHopper(enemy, playerCenter, distance, 40f, deltaSeconds, leapScale: 1.25f, interval: 0.7f);
    }

    // ------------------------------------------------------------------ hoppers

    private void StepHopper(EnemyAgent enemy, Vector2 playerCenter, float distance, float alertRadius, float deltaSeconds,
        float leapScale = 1f, float interval = 0f)
    {
        var family = enemy.Family;
        var (hopInterval, rise, run) = family switch
        {
            EnemyAiFamily.LeapingHopper => (2f, 12f, 5.5f),
            EnemyAiFamily.SkitteringHopper => (0.45f, 4.5f, 4f),
            _ => (1.1f, 8.5f, 3.2f)
        };
        if (interval > 0f)
            hopInterval = interval;
        enemy.AiTimer += deltaSeconds;
        enemy.State = distance <= alertRadius ? EnemyState.Pursuing : EnemyState.Roaming;
        var dir = distance <= alertRadius ? Math.Sign(playerCenter.X - (enemy.Motion.Position.X + enemy.Motion.Size.X / 2f)) : 0;
        if (dir == 0 && distance > alertRadius)
            dir = ((enemy.Id + TickNumber / 240) % 2 == 0) ? 1 : -1;
        var hop = enemy.Motion.Grounded && enemy.AiTimer >= hopInterval;
        // Hoppers only move while airborne: they sit, then spring.
        var movement = new MovementInput(enemy.Motion.Grounded ? 0f : dir, SpeedMultiplier: enemy.Archetype.MoveSpeedMultiplier);
        _enemyMovement.Step(enemy.Motion, movement, deltaSeconds, World, Tiles);
        if (hop)
        {
            enemy.AiTimer = 0f;
            enemy.Motion.Velocity = new Vector2(dir * run * leapScale, -rise * MathF.Sqrt(leapScale));
            enemy.Motion.Facing = dir == 0 ? enemy.Motion.Facing : dir;
        }
    }

    private static float MoveToward(float current, float target, float maxDelta) =>
        Math.Abs(target - current) <= maxDelta ? target : current + Math.Sign(target - current) * maxDelta;

    // ------------------------------------------------------------------ ambient spawns

    /// <summary>Places a creature exactly where asked (no lifting out of terrain): burrowing worms start buried.</summary>
    public EnemyAgent SpawnCreatureAt(EnemyKind kind, Vector2 position)
    {
        var enemy = new EnemyAgent(_nextEntityId++, kind, position);
        _enemies.Add(enemy);
        return enemy;
    }

    /// <summary>Test hook: one ambient-family spawn attempt now (skips the 45-second timer).</summary>
    internal void ProbeFamilySpawn() => UpdateFamilySpawns(45f);

    private void UpdateFamilySpawns(float deltaSeconds)
    {
        if (!FamilySpawnsEnabled || Player.IsDead)
            return;
        _familySpawnTimer += deltaSeconds;
        if (_familySpawnTimer < 45f)
            return;
        _familySpawnTimer = 0f;
        // EN-7: the family stream obeys the main spawner's off switches: the creative spawn slider (0 = none), events that
        // stop hostile spawns, a populated town or a living boss, and the dungeon's own roster.
        var rate = CreativeSpawnRate;
        if (rate <= 0f || _worldEvents.Modifiers.NoHostiles)
            return;
        if (_enemies.Count(enemy => enemy.Family != EnemyAiFamily.Default && enemy.IsAlive) >= 2 || FamilyRandom.NextDouble() > 0.5 * rate)
            return;
        if (TownNpcsCalmingSpawns >= 3 || _enemies.Any(enemy => enemy.IsBoss && enemy.IsAlive))
            return;
        if (World.GetCell(PlayerCell()).WallId == WallIds.Dungeon)
            return;
        var underground = CurrentBiome.Underground;
        var night = ClockHour >= 19f || ClockHour < 5f;
        // Biome-themed pools: the Irradiated Core gets its burrowers and casters, the ocean only diving sea kites.
        EnemyKind[] pool = CurrentBiome.Zone switch
        {
            BiomeZone.MoltenChasm => [EnemyKind.TunnelWyrm, EnemyKind.RiftCaster],
            BiomeZone.AcidCoast when !underground => [EnemyKind.DiveKite],
            _ => underground
                ? [EnemyKind.TunnelWyrm, EnemyKind.RiftCaster, EnemyKind.BoulderHopper, EnemyKind.FleaHopper, EnemyKind.CacheMimic]
                : night
                    ? [EnemyKind.DiveKite, EnemyKind.GelHopper, EnemyKind.DuneBorer, EnemyKind.FleaHopper]
                    : [EnemyKind.GelHopper, EnemyKind.FleaHopper, EnemyKind.DiveKite]
        };
        // The biome tables' Fracture gate: hard-roster families (the 260-HP tunnel wyrm) wait for the Fracture.
        if (!FractureActive)
            pool = pool.Where(candidate => !BiomeSpawnTables.FamilyNeedsFracture(candidate)).ToArray();
        if (pool.Length == 0)
            return;
        var kind = pool[FamilyRandom.Next(pool.Length)];
        // Underground mimics only appear once the world has fractured.
        if (kind == EnemyKind.CacheMimic && (FamilyRandom.NextDouble() > 0.25 || !FractureActive))
            kind = EnemyKind.BoulderHopper;
        var archetype = EnemyArchetypes.Get(kind);
        var side = FamilyRandom.Next(2) == 0 ? -1 : 1;
        var x = Math.Clamp((int)PlayerCenter.X + side * ((int)ViewHalfWidth + 4), 2, World.Width - 3);
        // Never inside the safe radius (clamping at a world edge could pull it closer).
        if (MathF.Abs(x - PlayerCenter.X) < SafeSpawnHalfWidth)
            return;
        Vector2 position;
        if (archetype.Family == EnemyAiFamily.Worm)
        {
            // Worms start buried below the player's level.
            var y = Math.Clamp((int)PlayerCenter.Y + 6, 2, World.Height - 3);
            position = new Vector2(x, y);
            if (!SolidAt(position + archetype.Size / 2f))
                return;
            SpawnCreatureAt(kind, position);
            return;
        }
        if (!TryFindSpawnGround(x, underground, out var groundY))
            return;
        var lift = archetype.Flying ? 4f : 0f;
        SpawnCreature(kind, new Vector2(x, groundY - archetype.Size.Y - lift));
    }
}
