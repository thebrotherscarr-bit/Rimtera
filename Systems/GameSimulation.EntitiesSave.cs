using System.Numerics;
using Rimtera.Entities;

namespace Rimtera.Systems;

public sealed partial class GameSimulation
{
    private const int MaximumSavedEntities = 20_000;

    /// <summary>
    /// Restores live creatures and projectiles after the world, content and town roster are in place.
    /// Saved IDs are kept when free. If a rebuilt colonist/town NPC already owns an ID, the entity gets a
    /// fresh one and projectile/set references are remapped through the same table.
    /// </summary>
    private void RestoreEntities(GameSnapshot snapshot)
    {
        _enemies.Clear();
        _projectiles.Clear();
        _raidEnemyIds.Clear();
        _nightBossIds.Clear();
        _aggroTargets.Clear();
        _farSeconds.Clear();
        _whipMarkedEnemy = -1;
        _whipBuffSeconds = 0f;

        if (snapshot.Enemies is null && snapshot.Projectiles is null)
            return;
        if (snapshot.Enemies is { Length: > MaximumSavedEntities } ||
            snapshot.Projectiles is { Length: > MaximumSavedEntities })
            throw new InvalidDataException("The saved entity list is too large.");

        var used = new HashSet<long>();
        foreach (var colonist in _colonists)
            used.Add(colonist.Id);
        foreach (var npc in _townNpcs)
            used.Add(npc.Id);

        var remap = new Dictionary<long, long>();
        foreach (var saved in snapshot.Enemies ?? [])
        {
            ValidateEnemy(saved, remap);
            // BO-17 (D-13): living bosses never survive a save and quit (old saves still carry them): the fight is dropped,
            // nothing is given back, and running events re-arm their finale (below).
            if (EnemyArchetypes.Get(saved.Kind).IsBoss)
                continue;
            var id = AllocateEntityId(saved.Id, used);
            remap.Add(saved.Id, id);
            var enemy = new EnemyAgent(id, saved.Kind, new Vector2(saved.X, saved.Y));
            enemy.RestoreState(saved);
            _enemies.Add(enemy);
        }

        foreach (var saved in snapshot.Projectiles ?? [])
        {
            ValidateProjectile(saved);
            var id = AllocateEntityId(saved.Id, used);
            var projectile = new ProjectileState(
                id,
                new Vector2(saved.X, saved.Y),
                new Vector2(saved.VelocityX, saved.VelocityY),
                saved.Damage,
                saved.Knockback,
                saved.RemainingSeconds,
                saved.Team,
                saved.SourceName,
                saved.Kind,
                saved.PurifyRadius);
            var remapped = saved with
            {
                HitEnemies = RemapEnemyIds(saved.HitEnemies, remap),
                HitCooldowns = RemapHitCooldowns(saved.HitCooldowns, remap)
            };
            projectile.RestoreState(remapped);
            _projectiles.Add(projectile);
        }

        foreach (var oldId in snapshot.RaidEnemyIds ?? [])
            if (remap.TryGetValue(oldId, out var id))
                _raidEnemyIds.Add(id);
        foreach (var oldId in snapshot.NightBossIds ?? [])
            if (remap.TryGetValue(oldId, out var id))
                _nightBossIds.Add(id);
        _worldEvents.RearmFinales();

        if (snapshot.WhipMarkedEnemy is { } marked && remap.TryGetValue(marked, out var mapped))
            _whipMarkedEnemy = mapped;
        if (snapshot.WhipBuffSeconds is { } buff && float.IsFinite(buff) && buff > 0f)
            _whipBuffSeconds = buff;
    }

    private static void ValidateEnemy(EnemySnapshot saved, Dictionary<long, long> seen)
    {
        if (saved.Id <= 0 || saved.Id > 1_000_000_000 || seen.ContainsKey(saved.Id))
            throw new InvalidDataException("The saved creature id is invalid.");
        if (!Enum.IsDefined(saved.Kind) || !EnemyArchetypes.Contains(saved.Kind) ||
            !Enum.IsDefined(saved.State) ||
            (saved.EventTag is { } tag && !Enum.IsDefined(tag)) ||
            !Finite(saved.X) || !Finite(saved.Y) ||
            !Finite(saved.VelocityX) || !Finite(saved.VelocityY) ||
            !Finite(saved.MaximumHitPoints) || saved.MaximumHitPoints <= 0f ||
            !Finite(saved.HitPoints) || saved.HitPoints < 0f ||
            !Finite(saved.AttackDamage) || saved.AttackDamage < 0f ||
            !Finite(saved.StateTime) || !Finite(saved.AttackCooldown) ||
            !Finite(saved.FleeSeconds) || !Finite(saved.AiTimer) ||
            saved.Segments is { Length: > 2_000 } ||
            saved.Segments is { Length: var length } && length % 2 != 0)
            throw new InvalidDataException("The saved creature state is invalid.");
        if (saved.Segments is { } segments)
            foreach (var value in segments)
                if (!Finite(value))
                    throw new InvalidDataException("The saved creature segment is invalid.");
        foreach (var affliction in saved.Afflictions ?? [])
            if (!Enum.IsDefined(affliction.Status) || !Finite(affliction.Seconds) || affliction.Seconds <= 0f)
                throw new InvalidDataException("The saved creature affliction is invalid.");
        foreach (var immunity in saved.HitImmunity ?? [])
            if (!Finite(immunity.Seconds) || immunity.Seconds <= 0f)
                throw new InvalidDataException("The saved creature immunity is invalid.");
    }

    private void ValidateProjectile(ProjectileSnapshot saved)
    {
        if (saved.Id <= 0 || saved.Id > 1_000_000_000 ||
            !Enum.IsDefined(saved.Team) || !Enum.IsDefined(saved.Kind) ||
            !Enum.IsDefined(saved.Class) || !Enum.IsDefined(saved.Style) ||
            (saved.InflictStatus is { } status && !Enum.IsDefined(status)) ||
            (saved.SourceItem is { } item && !Items.Contains(item)) ||
            !Finite(saved.X) || !Finite(saved.Y) ||
            !Finite(saved.VelocityX) || !Finite(saved.VelocityY) ||
            !Finite(saved.Damage) || saved.Damage < 0f ||
            !Finite(saved.Knockback) || !Finite(saved.RemainingSeconds) || saved.RemainingSeconds < 0f ||
            !Finite(saved.PurifyRadius) || !Finite(saved.Homing) ||
            !Finite(saved.Gravity) || !Finite(saved.ExplosionRadius) ||
            !Finite(saved.MaxRange) || !Finite(saved.Traveled) ||
            saved.HitEnemies is { Length: > MaximumSavedEntities })
            throw new InvalidDataException("The saved projectile state is invalid.");
        foreach (var cooldown in saved.HitCooldowns ?? [])
            if (!Finite(cooldown.Seconds) || cooldown.Seconds <= 0f)
                throw new InvalidDataException("The saved projectile cooldown is invalid.");
    }

    private long AllocateEntityId(long preferred, HashSet<long> used)
    {
        if (preferred > 0 && preferred <= 1_000_000_000 && used.Add(preferred))
        {
            if (preferred >= _nextEntityId)
                _nextEntityId = preferred + 1;
            return preferred;
        }

        while (_nextEntityId <= 0 || used.Contains(_nextEntityId))
            _nextEntityId++;
        var id = _nextEntityId++;
        used.Add(id);
        return id;
    }

    private static long[]? RemapEnemyIds(long[]? ids, Dictionary<long, long> remap)
    {
        if (ids is null || ids.Length == 0)
            return null;
        var mapped = ids.Where(id => remap.ContainsKey(id)).Select(id => remap[id]).Distinct().ToArray();
        return mapped.Length == 0 ? null : mapped;
    }

    private static HitCooldownSnapshot[]? RemapHitCooldowns(HitCooldownSnapshot[]? cooldowns, Dictionary<long, long> remap)
    {
        if (cooldowns is null || cooldowns.Length == 0)
            return null;
        var mapped = cooldowns
            .Where(entry => remap.ContainsKey(entry.EnemyId))
            .Select(entry => new HitCooldownSnapshot(remap[entry.EnemyId], entry.Seconds))
            .GroupBy(entry => entry.EnemyId)
            .Select(group => new HitCooldownSnapshot(group.Key, group.Max(entry => entry.Seconds)))
            .ToArray();
        return mapped.Length == 0 ? null : mapped;
    }

    private static bool Finite(float value) => float.IsFinite(value);
}
