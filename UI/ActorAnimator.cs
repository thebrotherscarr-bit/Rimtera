using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using Rimtera.Entities;
using Rimtera.Systems;
using Rimtera.World;

namespace Rimtera.UI;

/// <summary>
/// Presentation-only animation layer. Each frame it compares the simulation with what it saw last frame (hit points,
/// grounded, facing, use cooldown) and reads the simulation's feedback logs: hit records (PE-14: crits larger and orange,
/// damage over time smaller and dimmer, heals green, charge gains blue, rapid hits stacking into one number), death
/// records (PE-5: only kills burst and shed gore; despawns vanish quietly) and sound events (PE-7: material-coloured dust
/// on every dig and break, the double-jump puff). Nothing here feeds back into the simulation.
/// </summary>
internal sealed class ActorAnimator
{
    private const long PlayerId = -1;
    private readonly Dictionary<long, ActorTrack> _tracks = new();
    private readonly List<Particle> _particles = new();
    private readonly List<CombatText> _texts = new();
    private readonly List<Chunk> _chunks = new();
    private readonly Random _random = new(1234);
    private GameSimulation? _game;
    private long _soundCursor;
    private long _deathCursor;
    private long _hitCursor;
    private float _playerImmunity;
    private readonly List<SoundEvent> _sounds = new();
    private readonly List<DeathRecord> _deaths = new();
    private readonly List<HitRecord> _hits = new();
    private readonly HashSet<long> _struck = new();
    private readonly HashSet<long> _critical = new();

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
        public float VelocityY;
    }

    private sealed record Particle(Vector2 Position, Vector2 Velocity, Color Color, double Born, float Life, float Size, float Gravity = 14f)
    {
        public Vector2 Position { get; set; } = Position;
        public Vector2 Velocity { get; set; } = Velocity;
    }

    /// <summary>Most particles alive at once; gear trails stop adding when the pool is full so GDI+ keeps its frame rate.</summary>
    private const int ParticleCap = 700;

    /// <summary>One floating number. Rapid hits of the same kind on the same target add into it (PE-14 stacking).</summary>
    private sealed class CombatText(long target, Vector2 position, float amount, string sign, HitKind kind, bool critical, Color color, double born)
    {
        public long Target { get; } = target;
        public Vector2 Position { get; set; } = position;
        public float Amount { get; set; } = amount;
        public string Sign { get; } = sign;
        public HitKind Kind { get; } = kind;
        public bool Critical { get; } = critical;
        public Color Color { get; } = color;
        public double Born { get; set; } = born;
        public string Text => $"{Sign}{Amount:0}";
    }

    /// <summary>A piece of a dead creature's idle frame, falling and fading (PE-7 gore).</summary>
    private sealed class Chunk
    {
        public Bitmap? Source;
        public Rectangle Cut;
        public Color Fallback;
        public Vector2 Position;
        public Vector2 Velocity;
        public Vector2 Size;
        public double Born;
        public float Life;
        public bool Flip;
    }

    /// <summary>Most gore pieces alive at once (each is an image draw).</summary>
    private const int ChunkCap = 120;

    /// <summary>Average colour of a tile's art (dust colour); set by the renderer, which owns the sprites.</summary>
    public Func<TileId, Color>? TileColor { get; set; }

    /// <summary>Average colour of a background wall's art (wall dust).</summary>
    public Func<ushort, Color>? WallColor { get; set; }

    /// <summary>A creature's idle frame (unscaled source art) for its death chunks; null draws plain coloured chunks.</summary>
    public Func<EnemyKind, Bitmap?>? CreatureFrame { get; set; }

    /// <summary>Particles alive right now (the cap test and the F10 effects readout).</summary>
    public int ParticleCount => _particles.Count;

    /// <summary>Gore chunks alive right now.</summary>
    public int ChunkCount => _chunks.Count;

    /// <summary>Floating numbers alive right now, as (text, kind, critical) for tests.</summary>
    internal IEnumerable<(string Text, HitKind Kind, bool Critical)> Texts => _texts.Select(text => (text.Text, text.Kind, text.Critical));

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
            _chunks.Clear();
            // Start reading at the end of the logs: a loaded world never replays old dust, deaths or numbers.
            _soundCursor = game.SoundEvents.Next;
            _deathCursor = game.RecentDeaths.Next;
            _hitCursor = game.HitRecords.Next;
            _playerImmunity = game.Player.ImmunitySeconds;
        }

        // Hit records: which creatures took a direct hit (and a crit) since last frame; charge gains float up in blue.
        _hits.Clear();
        game.HitRecords.Drain(ref _hitCursor, _hits);
        _struck.Clear();
        _critical.Clear();
        foreach (var hit in _hits)
        {
            if (hit.Kind == HitKind.Charge)
                AddNumber(PlayerId, hit.Position - new Vector2(0, 1.6f), hit.Amount, "+", HitKind.Charge, false, now);
            else if (hit.Kind == HitKind.Direct && hit.TargetId >= 0)
            {
                _struck.Add(hit.TargetId);
                if (hit.Critical)
                    _critical.Add(hit.TargetId);
            }
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
                var (kind, critical) = CombatTextRules.ClassifyCreatureLoss(_struck.Contains(enemy.Id), _critical.Contains(enemy.Id),
                    enemy.Afflictions.Count > 0);
                AddNumber(enemy.Id, center - new Vector2(0, enemy.Motion.Size.Y / 2f), track.HitPoints - enemy.HitPoints, "", kind, critical, now);
                if (kind == HitKind.Direct)
                {
                    track.FlashUntil = now + 0.12;
                    Burst(center, track.Tint, critical ? 9 : 5, now, critical ? 3.5f : 2.5f);
                }
            }
            track.HitPoints = enemy.HitPoints;
            track.LastCenter = center;
            track.WalkPhase += Math.Abs(enemy.Motion.Velocity.X) * deltaSeconds * 3.2;
        }

        // PE-5: creatures that left are forgotten quietly; only the death records burst (and shed gore).
        foreach (var id in _tracks.Keys.Where(id => !seen.Contains(id)).ToArray())
            _tracks.Remove(id);
        _deaths.Clear();
        game.RecentDeaths.Drain(ref _deathCursor, _deaths);
        foreach (var death in _deaths)
        {
            if (death.Reason == DeathReason.Died)
            {
                var tint = EnemyArchetypes.Contains(death.Kind) && EnemyArchetypes.Get(death.Kind).IsBoss ? Color.FromArgb(200, 120, 255) : Color.FromArgb(230, 90, 90);
                Burst(death.Position, tint, 18, now, 5f);
                Burst(death.Position, Color.FromArgb(220, 220, 220), 8, now, 3f);
                Gore(death, tint, now);
            }
            else if (death.Reason == DeathReason.Caught)
                Burst(death.Position, Color.FromArgb(235, 240, 245), 4, now, 1.5f); // a soft puff in the net
        }

        // PE-7: material-coloured dust on every dig (2-4 motes) and break (8-12), from the simulation's sound events.
        _sounds.Clear();
        game.SoundEvents.Drain(ref _soundCursor, _sounds);
        foreach (var sound in _sounds)
        {
            switch (sound.Kind)
            {
                case SoundEventKind.Dig:
                    Dust(sound, 2 + _random.Next(3), 2.2f, now);
                    break;
                case SoundEventKind.Break:
                    Dust(sound, 8 + _random.Next(5), 4f, now);
                    break;
                case SoundEventKind.DoubleJump:
                    DoubleJumpAt = now;
                    var feet = game.Player.Motion.Position + new Vector2(game.Player.Motion.Size.X / 2f, game.Player.Motion.Size.Y);
                    for (var i = 0; i < 10; i++)
                    {
                        var angle = i / 10.0 * Math.PI * 2;
                        Emit(feet, new Vector2((float)Math.Cos(angle) * 3.2f, (float)Math.Sin(angle) * 1.1f + 1.5f), Color.FromArgb(235, 240, 245), now,
                            0.45f, 3f, 0f);
                    }
                    break;
            }
        }

        foreach (var particle in _particles)
        {
            particle.Velocity += new Vector2(0, particle.Gravity) * deltaSeconds;
            particle.Position += particle.Velocity * deltaSeconds;
        }
        foreach (var chunk in _chunks)
        {
            chunk.Velocity += new Vector2(0, 22f) * deltaSeconds;
            chunk.Position += chunk.Velocity * deltaSeconds;
        }
        _particles.RemoveAll(particle => now - particle.Born > particle.Life);
        _chunks.RemoveAll(chunk => now - chunk.Born > chunk.Life);
        _texts.RemoveAll(text => now - text.Born > 1.1);
    }

    /// <summary>Dust from a dig or break, coloured like the tile's (or wall's) art.</summary>
    private void Dust(SoundEvent sound, int count, float speed, double now)
    {
        var color = sound.Variant == "wall"
            ? WallColor?.Invoke((ushort)sound.Detail) ?? Color.FromArgb(110, 105, 100)
            : TileColor?.Invoke((TileId)sound.Detail) ?? MaterialColor(sound.Variant);
        count = Math.Max(1, count * ParticleScale / 2);
        if (ParticleScale <= 0)
            return;
        for (var i = 0; i < count; i++)
        {
            var angle = _random.NextDouble() * Math.PI * 2;
            var magnitude = speed * (0.3f + (float)_random.NextDouble() * 0.8f);
            var jitter = new Vector2((float)_random.NextDouble() - 0.5f, (float)_random.NextDouble() - 0.5f) * 0.8f;
            // Each mote is a shade lighter or darker than the tile so the cloud reads as grains, not a flat blob.
            var shade = 0.8f + (float)_random.NextDouble() * 0.4f;
            var mote = Color.FromArgb(Math.Clamp((int)(color.R * shade), 0, 255), Math.Clamp((int)(color.G * shade), 0, 255),
                Math.Clamp((int)(color.B * shade), 0, 255));
            Emit(sound.Position + jitter, new Vector2((float)Math.Cos(angle) * magnitude, (float)Math.Sin(angle) * magnitude - speed * 0.5f),
                mote, now, 0.35f + (float)_random.NextDouble() * 0.35f, 2f + (float)_random.NextDouble() * 2f, 12f);
        }
    }

    /// <summary>Dust colour by material when the renderer has no art colour.</summary>
    private static Color MaterialColor(string material) => material switch
    {
        "stone" => Color.FromArgb(128, 124, 120),
        "plant" => Color.FromArgb(90, 150, 70),
        "glass" => Color.FromArgb(170, 210, 230),
        "metal" => Color.FromArgb(150, 156, 166),
        _ => Color.FromArgb(140, 105, 70)
    };

    /// <summary>PE-7: 3-5 pieces cut from the creature's idle frame fly apart, fall and fade.</summary>
    private void Gore(DeathRecord death, Color tint, double now)
    {
        if (ParticleScale <= 0)
            return;
        var source = CreatureFrame?.Invoke(death.Kind);
        var pieces = 3 + _random.Next(3);
        const int columns = 2, rows = 3;
        var cells = Enumerable.Range(0, columns * rows).OrderBy(_ => _random.Next()).Take(pieces);
        var size = new Vector2(Math.Max(0.3f, death.Size.X / columns), Math.Max(0.3f, death.Size.Y / rows));
        foreach (var cell in cells)
        {
            if (_chunks.Count >= ChunkCap)
                _chunks.RemoveAt(0);
            var column = cell % columns;
            var row = cell / columns;
            var cut = source is null ? Rectangle.Empty
                : new Rectangle(column * source.Width / columns, row * source.Height / rows, Math.Max(1, source.Width / columns), Math.Max(1, source.Height / rows));
            var offset = new Vector2((column + 0.5f) * size.X - death.Size.X / 2f, (row + 0.5f) * size.Y - death.Size.Y / 2f);
            var away = offset.LengthSquared() > 1e-4f ? Vector2.Normalize(offset) : new Vector2(0f, -1f);
            _chunks.Add(new Chunk
            {
                Source = source, Cut = cut, Fallback = tint, Position = death.Position + offset, Size = size,
                Velocity = away * (2.5f + (float)_random.NextDouble() * 3f) + new Vector2(((float)_random.NextDouble() - 0.5f) * 3f, -4f - (float)_random.NextDouble() * 3f),
                Born = now, Life = 0.9f + (float)_random.NextDouble() * 0.6f, Flip = _random.Next(2) == 0
            });
        }
    }

    /// <summary>
    /// A floating number (PE-14). A hit of the same kind on the same target within <see cref="CombatTextRules.MergeSeconds"/>
    /// adds into the number already rising instead of stacking a new one.
    /// </summary>
    private void AddNumber(long target, Vector2 position, float amount, string sign, HitKind kind, bool critical, double now)
    {
        if (!ShowDamageNumbers || amount < 0.5f)
            return;
        for (var i = _texts.Count - 1; i >= 0; i--)
        {
            var text = _texts[i];
            if (text.Target == target && text.Kind == kind && text.Critical == critical && text.Sign == sign &&
                now - text.Born <= CombatTextRules.MergeSeconds)
            {
                text.Amount += amount;
                text.Born = now;
                text.Position = position;
                return;
            }
        }
        var color = kind switch
        {
            HitKind.Heal => Color.FromArgb(110, 230, 120),
            HitKind.Charge => Color.FromArgb(90, 160, 255),
            _ when critical => Color.FromArgb(255, 140, 30),
            _ when target == PlayerId => Color.FromArgb(255, 90, 90),
            _ => Color.FromArgb(255, 210, 90)
        };
        // A little sideways scatter so numbers from different sources don't sit on top of each other.
        var scatter = new Vector2(((float)_random.NextDouble() - 0.5f) * 0.6f, 0f);
        _texts.Add(new CombatText(target, position + scatter, amount, sign, kind, critical, color, now));
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

        var immunityStarted = player.ImmunitySeconds > _playerImmunity + 0.001f;
        _playerImmunity = player.ImmunitySeconds;
        if (player.HitPoints < track.HitPoints - 0.01f)
        {
            var kind = CombatTextRules.ClassifyPlayerLoss(immunityStarted, game.PlayerHasDamagingStatus, game.PlayerInHarmfulLiquid);
            if (kind == HitKind.Direct)
            {
                PlayerHitAt = now;
                track.FlashUntil = now + 0.15;
            }
            AddNumber(PlayerId, center - new Vector2(0, 1.2f), track.HitPoints - player.HitPoints, "-", kind, false, now);
        }
        else if (player.HitPoints > track.HitPoints + 0.5f)
        {
            AddNumber(PlayerId, center - new Vector2(0, 1.2f), player.HitPoints - track.HitPoints, "+", HitKind.Heal, false, now);
        }

        if (motion.Grounded && !track.Grounded)
        {
            track.LandedAt = now;
            Burst(new Vector2(center.X, motion.Position.Y + motion.Size.Y), Color.FromArgb(170, 150, 120), 4, now, 1.6f);
        }
        if (motion.Facing != track.Facing)
            track.TurnedAt = now;
        // Leaving the ground with a jump kicks up dust; the double-jump puff comes from the simulation's DoubleJump event.
        var feet = new Vector2(center.X, motion.Position.Y + motion.Size.Y);
        if (!motion.Grounded && track.Grounded && motion.Velocity.Y < -3f)
            Burst(feet, Color.FromArgb(170, 150, 120), 3, now, 1.4f);
        track.VelocityY = motion.Velocity.Y;
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
    /// landing frame, hurt and knocked-down. Frames face right. Tools are never part of the body: a drill swings as its
    /// own sprite (DrawHeldItem), so the body keeps one size whatever the hands are doing.
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

    /// <summary>Real time the player last lost hit points (shield and thorn glints key off it).</summary>
    public double PlayerHitAt { get; private set; } = -10;

    /// <summary>Real time of the last mid-air jump (double-jump puff).</summary>
    public double DoubleJumpAt { get; private set; } = -10;

    /// <summary>0→1 progress through the current item use, or null when not using.</summary>
    public float? UseProgress(double now)
    {
        // The simulation times every use (useAnimation, held vs pressed, channel): tools, weapons, blocks, food alike.
        // Items whose use time is a tick or two (their cooldown lives elsewhere) fall back to the cooldown timing below.
        if (_game is { } game && (ToolParity.IsTool(game.Player.Inventory.SelectedItem) ||
                                  game.ToolSwingProgress is not null && game.ToolSwingLength >= 6))
            return game.ToolSwingProgress;
        if (!_tracks.TryGetValue(PlayerId, out var track))
            return null;
        var duration = track.UseDurationTicks / 60.0;
        var t = (now - track.UseStartedAt) / duration;
        return t is >= 0 and < 1 ? (float)t : null;
    }

    /// <summary>One presentation particle in world space (gear trails, crumbs, sparks); dropped when the pool is full.</summary>
    public void Emit(Vector2 position, Vector2 velocity, Color color, double now, float life, float size, float gravity = 14f)
    {
        if (_particles.Count < ParticleCap)
            _particles.Add(new Particle(position, velocity, color, now, life, size, gravity));
    }

    /// <summary>Particle density setting (0 = off, 2 = normal); gear emitters scale by it.</summary>
    public float ParticleDensity => ParticleScale / 2f;

    private void Burst(Vector2 position, Color color, int count, double now, float speed)
    {
        count = count * ParticleScale / 2;
        for (var i = 0; i < count; i++)
        {
            var angle = _random.NextDouble() * Math.PI * 2;
            var magnitude = speed * (0.4f + (float)_random.NextDouble());
            var velocity = new Vector2((float)Math.Cos(angle) * magnitude, (float)Math.Sin(angle) * magnitude - speed * 0.6f);
            Emit(position, velocity, color, now, 0.35f + (float)_random.NextDouble() * 0.35f, 2f + (float)_random.NextDouble() * 2f);
        }
    }

    private Font? _baseFont;
    private Font? _critFont;
    private Font? _dotFont;
    // Made on first draw: GDI+ objects exist only where the game renders (headless tests never draw).
    private ImageAttributes? _fade;
    private ColorMatrix? _fadeMatrix;

    /// <summary>Particles and floating combat text, in world space.</summary>
    public void DrawEffects(Graphics g, int startX, int startY, int tileSize, double now, Font font)
    {
        using var dot = new SolidBrush(Color.White);
        foreach (var particle in _particles)
        {
            var age = (float)((now - particle.Born) / particle.Life);
            var alpha = (int)(particle.Color.A * Math.Clamp(1f - age, 0f, 1f));
            dot.Color = Color.FromArgb(alpha, particle.Color.R, particle.Color.G, particle.Color.B);
            g.FillRectangle(dot, (particle.Position.X - startX) * tileSize - particle.Size / 2f,
                (particle.Position.Y - startY) * tileSize - particle.Size / 2f, particle.Size, particle.Size);
        }
        foreach (var chunk in _chunks)
        {
            var age = (float)((now - chunk.Born) / chunk.Life);
            var alpha = Math.Clamp(1.4f - age * 1.4f, 0f, 1f);
            var rect = new RectangleF((chunk.Position.X - chunk.Size.X / 2f - startX) * tileSize, (chunk.Position.Y - chunk.Size.Y / 2f - startY) * tileSize,
                chunk.Size.X * tileSize, chunk.Size.Y * tileSize);
            if (chunk.Source is { } source)
            {
                _fade ??= new ImageAttributes();
                _fadeMatrix ??= new ColorMatrix();
                _fadeMatrix.Matrix33 = alpha;
                _fade.SetColorMatrix(_fadeMatrix);
                var destination = Rectangle.Round(rect);
                if (chunk.Flip)
                    destination = new Rectangle(destination.Right, destination.Y, -destination.Width, destination.Height);
                g.DrawImage(source, destination, chunk.Cut.X, chunk.Cut.Y, chunk.Cut.Width, chunk.Cut.Height, GraphicsUnit.Pixel, _fade);
            }
            else
            {
                dot.Color = Color.FromArgb((int)(255 * alpha), chunk.Fallback);
                g.FillRectangle(dot, rect);
            }
        }
        if (!ReferenceEquals(font, _baseFont))
        {
            _baseFont = font;
            _critFont?.Dispose();
            _dotFont?.Dispose();
            _critFont = new Font(font.FontFamily, font.Size * 1.4f, FontStyle.Bold, font.Unit);
            _dotFont = new Font(font.FontFamily, font.Size * 0.8f, font.Style, font.Unit);
        }
        foreach (var text in _texts)
        {
            var age = (float)(now - text.Born);
            var rise = age * (text.Kind == HitKind.Dot ? 0.9f : 1.4f);
            // Damage over time is smaller and dimmer; crits are larger, bold and orange.
            var alpha = (int)(255 * Math.Clamp(1.2f - age, 0f, 1f) * (text.Kind == HitKind.Dot ? 0.65f : 1f));
            var x = (text.Position.X - startX) * tileSize;
            var y = (text.Position.Y - rise - startY) * tileSize;
            var face = text.Critical ? _critFont! : text.Kind == HitKind.Dot ? _dotFont! : font;
            var color = text.Kind == HitKind.Dot ? Color.FromArgb((text.Color.R + 160) / 2, (text.Color.G + 150) / 2, (text.Color.B + 150) / 2) : text.Color;
            using var shadow = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
            using var brush = new SolidBrush(Color.FromArgb(alpha, color));
            g.DrawString(text.Text, face, shadow, x + 1, y + 1);
            g.DrawString(text.Text, face, brush, x, y);
        }
    }
}
