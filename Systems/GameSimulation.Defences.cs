using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>A sentry handed to the colony, as saved (0.7.3).</summary>
public sealed record ColonySentrySnapshot(MinionKind Kind, float X, float Y, float Damage, ItemId Item);

/// <summary>
/// Outpost defences (0.7.3, docs/VISION.md §4.4): Terraria's sentries and traps, manned by your people. A sentry you deploy
/// within <see cref="MannedRadius"/> tiles of an armed resident's home or post is handed to the colony: it no longer takes
/// one of your sentry slots, it stays when you die or leave, it is saved with the world, and it fires only while an armed
/// resident is near to man it (one sentry per armed resident there; a new one replaces the oldest). A dart trap within
/// <see cref="MannedRadius"/> tiles of an armed resident is fired at hostiles in its line, and its darts spare your people.
/// </summary>
public sealed partial class GameSimulation
{
    /// <summary>How near an armed resident (their home, post or self) must be to man a sentry or a trap (tiles).</summary>
    public const float MannedRadius = 12f;
    /// <summary>How far down its line a manned dart trap fires (tiles), and how often (seconds).</summary>
    public const float TrapReach = 24f, TrapInterval = 1.5f;

    private readonly List<MinionState> _colonySentries = new();
    private readonly Dictionary<GridPoint, float> _trapCooldowns = new();
    private float _trapScanSeconds;

    /// <summary>Sentries the colony mans.</summary>
    public IReadOnlyList<MinionState> ColonySentries => _colonySentries;

    private IEnumerable<TownNpc> ArmedResidents => _townNpcs.Where(npc => npc.IsArmed && !npc.IsBound && !npc.IsPrisoner && npc.IsAlive && npc.HomeCell is not null);

    /// <summary>Armed residents whose home or post lies within <see cref="MannedRadius"/> of <paramref name="at"/>.</summary>
    private TownNpc[] StationedNear(Vector2 at) => ArmedResidents.Where(npc =>
        Vector2.Distance(npc.HomePosition, at) <= MannedRadius ||
        npc.Post is { } post && Vector2.Distance(new Vector2(post.X + 0.5f, post.Y), at) <= MannedRadius).ToArray();

    /// <summary>Whether an armed resident is near enough to man something at <paramref name="at"/>.</summary>
    public bool IsManned(Vector2 at) => ArmedResidents.Any(npc =>
        !npc.FallingBack && Vector2.Distance(npc.Motion.Position + npc.Motion.Size / 2f, at) <= MannedRadius);

    /// <summary>
    /// A sentry just deployed near armed residents goes to the colony. Returns the resident who will man it, or null when
    /// none is stationed there (it stays yours).
    /// </summary>
    private TownNpc? HandSentryToColony(MinionState sentry)
    {
        var stationed = StationedNear(sentry.Position);
        if (stationed.Length == 0)
            return null;
        _minions.Remove(sentry);
        var here = _colonySentries.Where(other => Vector2.Distance(other.Position, sentry.Position) <= MannedRadius * 2f).ToList();
        while (here.Count >= stationed.Length)
        {
            _colonySentries.Remove(here[0]);
            here.RemoveAt(0);
        }
        sentry.Damage = LiveMinionDamage(sentry); // fixed at hand-over: it no longer follows your gear
        _colonySentries.Add(sentry);
        return stationed.OrderBy(npc => Vector2.Distance(npc.HomePosition, sentry.Position)).First();
    }

    /// <summary>Each tick: manned colony sentries fire; manned dart traps fire down their line.</summary>
    private void UpdateDefences(float deltaSeconds)
    {
        foreach (var sentry in _colonySentries)
        {
            sentry.AgeSeconds += deltaSeconds;
            sentry.Cooldown = Math.Max(0f, sentry.Cooldown - deltaSeconds);
            if (sentry.Cooldown > 0f || !IsManned(sentry.Position))
                continue;
            var target = _enemies
                .Where(enemy => enemy.IsAlive && !enemy.IsPassive &&
                                Vector2.Distance(enemy.Motion.Position + enemy.Motion.Size / 2f, sentry.Position) <= sentry.Spec.Range)
                .OrderBy(enemy => Vector2.Distance(enemy.Motion.Position + enemy.Motion.Size / 2f, sentry.Position))
                .FirstOrDefault();
            sentry.TargetId = target?.Id;
            if (target is null || sentry.Spec.Heals)
                continue;
            var aim = target.Motion.Position + target.Motion.Size / 2f - sentry.Position;
            if (aim.LengthSquared() < 0.0001f)
                aim = Vector2.UnitX;
            for (var shot = 0; shot < sentry.Spec.Projectiles; shot++)
            {
                var angle = MathF.Atan2(aim.Y, aim.X) + (shot - (sentry.Spec.Projectiles - 1) / 2f) * 0.12f;
                _projectiles.Add(new ProjectileState(_nextEntityId++, sentry.Position,
                    new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * sentry.Spec.ProjectileSpeed, sentry.Damage, 1f, 2f, ProjectileTeam.Player)
                {
                    Class = DamageClass.Command, SpriteName = sentry.SpriteName
                });
            }
            sentry.Cooldown = sentry.Spec.FireIntervalSeconds;
        }
        UpdateMannedTraps(deltaSeconds);
    }

    private void UpdateMannedTraps(float deltaSeconds)
    {
        foreach (var key in _trapCooldowns.Keys.ToArray())
            _trapCooldowns[key] = Math.Max(0f, _trapCooldowns[key] - deltaSeconds);
        _trapScanSeconds -= deltaSeconds;
        if (_trapScanSeconds > 0f)
            return;
        _trapScanSeconds = 0.25f;
        if (!_enemies.Any(enemy => enemy.IsAlive && !enemy.IsPassive))
            return;
        var seen = new HashSet<GridPoint>();
        foreach (var npc in ArmedResidents.Where(npc => !npc.FallingBack))
        {
            var center = npc.Motion.Position + npc.Motion.Size / 2f;
            var radius = (int)MannedRadius;
            for (var y = (int)center.Y - radius; y <= (int)center.Y + radius; y++)
            for (var x = (int)center.X - radius; x <= (int)center.X + radius; x++)
            {
                var cell = new GridPoint(x, y);
                if (!World.Contains(cell) || World.GetCell(cell).TileId != TileId.DartTrap || !seen.Add(cell) ||
                    _trapCooldowns.GetValueOrDefault(cell) > 0f)
                    continue;
                var direction = (World.GetCell(cell).State & 1) != 0 ? -1 : 1;
                var line = new Vector2(x + 0.5f, y + 0.5f);
                var inLine = _enemies.Any(enemy => enemy.IsAlive && !enemy.IsPassive &&
                    MathF.Abs(enemy.Motion.Position.Y + enemy.Motion.Size.Y / 2f - line.Y) <= enemy.Motion.Size.Y / 2f + 0.5f &&
                    (enemy.Motion.Position.X + enemy.Motion.Size.X / 2f - line.X) * direction is > 0f and <= TrapReach);
                if (!inLine)
                    continue;
                var origin = new Vector2(line.X + direction * 0.7f, line.Y);
                _projectiles.Add(new ProjectileState(_nextEntityId++, origin, new Vector2(direction * 16f, 0f), 18f, 1.5f, 1.6f,
                    ProjectileTeam.Player, "dart trap", DamageKind.Shot) { SpriteName = "proj_dart" });
                _trapCooldowns[cell] = TrapInterval;
            }
        }
    }

    private ColonySentrySnapshot[]? CaptureColonySentries() => _colonySentries.Count == 0
        ? null
        : _colonySentries.Select(sentry => new ColonySentrySnapshot(sentry.Kind, sentry.Position.X, sentry.Position.Y, sentry.Damage, sentry.SourceItem)).ToArray();

    private void RestoreColonySentries(ColonySentrySnapshot[]? saved)
    {
        _colonySentries.Clear();
        foreach (var sentry in saved ?? [])
        {
            if (sentry is null || !Enum.IsDefined(sentry.Kind) || !float.IsFinite(sentry.X) || !float.IsFinite(sentry.Y) || !float.IsFinite(sentry.Damage))
                continue;
            var spec = MinionCatalog.Get(sentry.Kind);
            if (!spec.IsSentry)
                continue;
            _colonySentries.Add(new MinionState(_nextEntityId++, spec, new Vector2(sentry.X, sentry.Y), Math.Max(0f, sentry.Damage), sentry.Item));
        }
    }
}
