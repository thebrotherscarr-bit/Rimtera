using System.Drawing;
using System.Numerics;
using Rimtera.Entities;
using Rimtera.Systems;

namespace Rimtera.UI;

/// <summary>
/// Presentation-only animation layer. Each frame it compares the simulation with what it saw
/// last frame (hit points, grounded, facing, use cooldown, enemies that vanished) and turns the
/// differences into poses, hit flashes, floating damage numbers and particle bursts. Nothing
/// here feeds back into the simulation.
/// </summary>
internal sealed class ActorAnimator
{
    private const long PlayerId = -1;
    private readonly Dictionary<long, ActorTrack> _tracks = new();
    private readonly List<Particle> _particles = new();
    private readonly List<CombatText> _texts = new();
    private readonly Random _random = new(1234);
    private GameSimulation? _game;

    private sealed class ActorTrack
    {
        public float HitPoints;
        public bool Grounded = true;
        public int Facing = 1;
        public double LandedAt = -10;
        public double TurnedAt = -10;
        public double FlashUntil = -10;
        public double UseStartedAt = -10;
        public int UseDurationTicks = 1;
        public int LastCooldown;
        public Vector2 LastCenter;
        public Color Tint = Color.White;
        public double WalkPhase;
    }

    private sealed record Particle(Vector2 Position, Vector2 Velocity, Color Color, double Born, float Life, float Size)
    {
        public Vector2 Position { get; set; } = Position;
        public Vector2 Velocity { get; set; } = Velocity;
    }

    private sealed record CombatText(Vector2 Position, string Text, Color Color, double Born);

    public bool ShowDamageNumbers { get; set; } = true;
    public int ParticleScale { get; set; } = 2;

    public readonly record struct Pose(string Frame, bool Flip, float ScaleX, float ScaleY, float Bob, bool Flash, bool ShowsTool = false);

    /// <summary>True when the generated animation frames (anim_*) are available.</summary>
    public bool HasAnimationFrames { get; set; }

    /// <summary>Call once per rendered frame before drawing actors.</summary>
    public void Update(GameSimulation game, double now, float deltaSeconds)
    {
        if (!ReferenceEquals(game, _game))
        {
            _game = game;
            _tracks.Clear();
            _particles.Clear();
            _texts.Clear();
        }

        TrackPlayer(game, now);
        var seen = new HashSet<long> { PlayerId };
        foreach (var enemy in game.Enemies)
        {
            seen.Add(enemy.Id);
            var center = enemy.Motion.Position + enemy.Motion.Size / 2f;
            if (!_tracks.TryGetValue(enemy.Id, out var track))
            {
                _tracks[enemy.Id] = new ActorTrack { HitPoints = enemy.HitPoints, LastCenter = center, Tint = enemy.IsBoss ? Color.FromArgb(200, 120, 255) : Color.FromArgb(230, 90, 90) };
                continue;
            }
            if (enemy.HitPoints < track.HitPoints - 0.01f)
            {
                track.FlashUntil = now + 0.12;
                AddText(center - new Vector2(0, enemy.Motion.Size.Y / 2f), $"{track.HitPoints - enemy.HitPoints:0}", Color.FromArgb(255, 210, 90), now);
                Burst(center, track.Tint, 5, now, 2.5f);
            }
            track.HitPoints = enemy.HitPoints;
            track.LastCenter = center;
            track.WalkPhase += Math.Abs(enemy.Motion.Velocity.X) * deltaSeconds * 3.2;
        }

        // Enemies that disappeared since last frame died (or despawned): burst where they were.
        foreach (var id in _tracks.Keys.Where(id => !seen.Contains(id)).ToArray())
        {
            var track = _tracks[id];
            Burst(track.LastCenter, track.Tint, 18, now, 5f);
            Burst(track.LastCenter, Color.FromArgb(220, 220, 220), 8, now, 3f);
            _tracks.Remove(id);
        }

        var gravity = new Vector2(0, 14f);
        foreach (var particle in _particles)
        {
            particle.Velocity += gravity * deltaSeconds;
            particle.Position += particle.Velocity * deltaSeconds;
        }
        _particles.RemoveAll(particle => now - particle.Born > particle.Life);
        _texts.RemoveAll(text => now - text.Born > 1.1);
    }

    private void TrackPlayer(GameSimulation game, double now)
    {
        var player = game.Player;
        var motion = player.Motion;
        var center = motion.Position + motion.Size / 2f;
        if (!_tracks.TryGetValue(PlayerId, out var track))
        {
            _tracks[PlayerId] = new ActorTrack { HitPoints = player.HitPoints, Grounded = motion.Grounded, Facing = motion.Facing, LastCenter = center };
            return;
        }

        if (player.HitPoints < track.HitPoints - 0.01f)
        {
            track.FlashUntil = now + 0.15;
            AddText(center - new Vector2(0, 1.2f), $"-{track.HitPoints - player.HitPoints:0}", Color.FromArgb(255, 90, 90), now);
        }
        else if (player.HitPoints > track.HitPoints + 0.5f)
        {
            AddText(center - new Vector2(0, 1.2f), $"+{player.HitPoints - track.HitPoints:0}", Color.FromArgb(110, 230, 120), now);
        }

        if (motion.Grounded && !track.Grounded)
        {
            track.LandedAt = now;
            Burst(new Vector2(center.X, motion.Position.Y + motion.Size.Y), Color.FromArgb(170, 150, 120), 4, now, 1.6f);
        }
        if (motion.Facing != track.Facing)
            track.TurnedAt = now;
        // A jump in the cooldown counter means an item was just used.
        if (player.ActionCooldownTicks > track.LastCooldown)
        {
            track.UseStartedAt = now;
            track.UseDurationTicks = Math.Max(6, player.ActionCooldownTicks);
        }

        track.LastCooldown = player.ActionCooldownTicks;
        track.HitPoints = player.HitPoints;
        track.Grounded = motion.Grounded;
        track.Facing = motion.Facing;
        track.LastCenter = center;
    }

    /// <summary>Frame, facing, squash and bob for the player this frame.</summary>
    public Pose PlayerPose(GameSimulation game, double now)
    {
        if (HasAnimationFrames)
            return AnimatedPose(game, now);
        var motion = game.Player.Motion;
        var track = _tracks.GetValueOrDefault(PlayerId) ?? new ActorTrack();
        var speed = MathF.Abs(motion.Velocity.X);
        string frame;
        var bob = 0f;
        if (now - track.TurnedAt < 0.08)
            frame = "colonist_front";
        else if (!motion.Grounded)
            frame = motion.Velocity.Y < 0 ? "colonist_side_b" : "colonist_side_a";
        else if (speed > 0.4f)
        {
            // Step rate follows run speed; each step dips the body slightly.
            var phase = now * (4.0 + speed * 1.1);
            frame = ((int)phase & 1) == 0 ? "colonist_side_a" : "colonist_side_b";
            bob = -MathF.Abs(MathF.Sin((float)(phase * Math.PI))) * 2.5f;
        }
        else
        {
            frame = "colonist_front";
            bob = MathF.Sin((float)(now * 2.2)) * 1.1f;
        }

        // Landing squash eases back over a tenth of a second.
        var sinceLanding = (float)(now - track.LandedAt);
        var squash = sinceLanding < 0.12f ? 1f - 0.12f * (1f - sinceLanding / 0.12f) : 1f;
        var flip = frame != "colonist_front" && motion.Facing > 0;
        return new Pose(frame, flip, 2f - squash, squash, bob, now < track.FlashUntil);
    }

    /// <summary>
    /// Pose from the generated frame set: an 8-frame walk timed to speed, idle, jump, fall, a
    /// landing frame, the mining swing while drilling, hurt and knocked-down. Frames face right.
    /// </summary>
    private Pose AnimatedPose(GameSimulation game, double now)
    {
        var player = game.Player;
        var motion = player.Motion;
        var track = _tracks.GetValueOrDefault(PlayerId) ?? new ActorTrack();
        var speed = MathF.Abs(motion.Velocity.X);
        var flip = motion.Facing < 0;
        var flash = now < track.FlashUntil || (player.IsImmune && (int)(now * 14) % 2 == 0); // immunity frames blink
        var sinceLanding = (float)(now - track.LandedAt);

        if (player.IsDowned)
            return new Pose("anim_down", flip, 1f, 1f, 0f, flash);
        if (flash && now - (track.FlashUntil - 0.15) < 0.25)
            return new Pose("anim_hurt", flip, 1f, 1f, 0f, true);
        if (UseProgress(now) is { } progress && player.Inventory.SelectedItem?.UseKind == ItemUseKind.Pickaxe)
            return new Pose(progress < 0.5f ? "anim_mine_start" : "anim_mine_hit", flip, 1f, 1f, 0f, flash, ShowsTool: true);
        if (!motion.Grounded)
            return new Pose(motion.Velocity.Y < 0 ? "anim_jump" : "anim_fall", flip, 1f, 1f, 0f, flash);
        if (sinceLanding < 0.1f)
            return new Pose("anim_land", flip, 1f, 1f, 0f, flash);
        if (speed > 0.4f)
        {
            // Eight frames per stride; stride rate follows run speed.
            var frame = (int)(now * (7.0 + speed * 1.6)) % 8 + 1;
            return new Pose($"anim_walk_{frame}", flip, 1f, 1f, 0f, flash);
        }
        return new Pose("anim_idle", flip, 1f, 1f, MathF.Sin((float)(now * 2.2)) * 1.1f, flash);
    }

    public bool IsFlashing(long enemyId, double now) =>
        _tracks.TryGetValue(enemyId, out var track) && now < track.FlashUntil;

    /// <summary>Walking bob for ground creatures, hover sway for flyers.</summary>
    public float EnemyBob(EnemyAgent enemy, double now)
    {
        if (enemy.Archetype.Flying)
            return MathF.Sin((float)(now * 3.0 + enemy.Id)) * 3f;
        var phase = _tracks.TryGetValue(enemy.Id, out var track) ? track.WalkPhase : 0;
        return -MathF.Abs(MathF.Sin((float)(phase * Math.PI))) * 2f;
    }

    /// <summary>0→1 progress through the current item use, or null when not using.</summary>
    public float? UseProgress(double now)
    {
        // Drills, cutters and demolishers swing for their own swing time, which the simulation tracks.
        if (_game is { } game && ToolParity.IsTool(game.Player.Inventory.SelectedItem))
            return game.ToolSwingProgress;
        if (!_tracks.TryGetValue(PlayerId, out var track))
            return null;
        var duration = track.UseDurationTicks / 60.0;
        var t = (now - track.UseStartedAt) / duration;
        return t is >= 0 and < 1 ? (float)t : null;
    }

    private void AddText(Vector2 position, string text, Color color, double now)
    {
        if (ShowDamageNumbers)
            _texts.Add(new CombatText(position, text, color, now));
    }

    private void Burst(Vector2 position, Color color, int count, double now, float speed)
    {
        count = count * ParticleScale / 2;
        for (var i = 0; i < count; i++)
        {
            var angle = _random.NextDouble() * Math.PI * 2;
            var magnitude = speed * (0.4f + (float)_random.NextDouble());
            var velocity = new Vector2((float)Math.Cos(angle) * magnitude, (float)Math.Sin(angle) * magnitude - speed * 0.6f);
            _particles.Add(new Particle(position, velocity, color, now, 0.35f + (float)_random.NextDouble() * 0.35f, 2f + (float)_random.NextDouble() * 2f));
        }
    }

    /// <summary>Particles and floating combat text, in world space.</summary>
    public void DrawEffects(Graphics g, int startX, int startY, int tileSize, double now, Font font)
    {
        foreach (var particle in _particles)
        {
            var age = (float)((now - particle.Born) / particle.Life);
            var alpha = (int)(255 * Math.Clamp(1f - age, 0f, 1f));
            using var brush = new SolidBrush(Color.FromArgb(alpha, particle.Color));
            g.FillRectangle(brush, (particle.Position.X - startX) * tileSize, (particle.Position.Y - startY) * tileSize, particle.Size, particle.Size);
        }
        foreach (var text in _texts)
        {
            var age = (float)(now - text.Born);
            var rise = age * 1.4f;
            var alpha = (int)(255 * Math.Clamp(1.2f - age, 0f, 1f));
            var x = (text.Position.X - startX) * tileSize;
            var y = (text.Position.Y - rise - startY) * tileSize;
            using var shadow = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
            using var brush = new SolidBrush(Color.FromArgb(alpha, text.Color));
            g.DrawString(text.Text, font, shadow, x + 1, y + 1);
            g.DrawString(text.Text, font, brush, x, y);
        }
    }
}
