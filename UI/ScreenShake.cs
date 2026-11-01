using System.Numerics;
using Rimtera.Entities;
using Rimtera.Systems;

namespace Rimtera.UI;

/// <summary>
/// PE-16: a decaying screen shake. The simulation's feedback logs (0.1.15 <see cref="GameSimulation.SoundEvents"/>,
/// <see cref="GameSimulation.RecentDeaths"/>) and the player's own fall supply the impulses: a boss arriving (its roar) or
/// dying, an explosion near the player, and a heavy landing. Each impulse adds "trauma" (0-1); the offset is trauma² times
/// <see cref="MaxOffsetPixels"/>, so small knocks barely move the view and big ones jolt it, and trauma falls off linearly
/// at <see cref="DecayPerSecond"/>. The <see cref="GamePreferences.ScreenShake"/> preference turns it off.
/// </summary>
public sealed class ScreenShake
{
    public const float MaxOffsetPixels = 14f;
    /// <summary>Trauma lost per second: a full-strength shake is over in 0.6 s.</summary>
    public const float DecayPerSecond = 1.6f;
    public const float BossRoar = 0.75f;
    public const float BossDeath = 0.7f;
    public const float PlayerDeath = 0.45f;
    /// <summary>Explosions closer than this many tiles shake the view, harder the closer and bigger they are.</summary>
    public const float ExplosionReach = 40f;
    /// <summary>A landing from at least this high shakes the view (25 tiles is where fall damage starts).</summary>
    public const float HeavyLandingTiles = 25f;

    private readonly List<SoundEvent> _sounds = new();
    private readonly List<DeathRecord> _deaths = new();
    private readonly HashSet<long> _bosses = new();
    private GameSimulation? _game;
    private long _soundCursor;
    private long _deathCursor;
    private bool _airborne;
    private float _peakY;
    private double _time;

    public float Trauma { get; private set; }

    public void AddImpulse(float amount)
    {
        if (float.IsFinite(amount) && amount > 0f)
            Trauma = Math.Min(1f, Trauma + amount);
    }

    /// <summary>Reads what happened since the last call and turns it into impulses. Call once a frame.</summary>
    public void Observe(GameSimulation game)
    {
        var player = game.Player.Motion;
        var center = player.Position + player.Size / 2f;
        if (!ReferenceEquals(game, _game))
        {
            // A new or loaded world starts calm: nothing that happened before counts.
            _game = game;
            _soundCursor = game.SoundEvents.Next;
            _deathCursor = game.RecentDeaths.Next;
            _bosses.Clear();
            foreach (var enemy in game.Enemies)
                if (enemy.IsAlive && enemy.IsBoss)
                    _bosses.Add(enemy.Id);
            _airborne = !player.Grounded;
            _peakY = player.Position.Y;
            Trauma = 0f;
            return;
        }

        _sounds.Clear();
        game.SoundEvents.Drain(ref _soundCursor, _sounds);
        foreach (var sound in _sounds)
        {
            if (sound.Kind == SoundEventKind.Explosion)
                AddImpulse(ExplosionImpulse(Vector2.Distance(sound.Position, center), sound.Detail));
            else if (sound.Kind == SoundEventKind.PlayerDeath)
                AddImpulse(PlayerDeath);
        }

        // Bosses: a new one roars; one that dies (not one that leaves) collapses.
        _deaths.Clear();
        game.RecentDeaths.Drain(ref _deathCursor, _deaths);
        foreach (var death in _deaths)
            if (_bosses.Remove(death.Id) && death.Reason == DeathReason.Died)
                AddImpulse(BossDeath);
        foreach (var enemy in game.Enemies)
            if (enemy.IsAlive && enemy.IsBoss && _bosses.Add(enemy.Id))
                AddImpulse(BossRoar);

        // Heavy landing: the drop from the highest point of the jump or fall to where the player touched down.
        if (!player.Grounded)
        {
            _peakY = _airborne ? Math.Min(_peakY, player.Position.Y) : player.Position.Y;
            _airborne = true;
        }
        else
        {
            if (_airborne)
                AddImpulse(LandingImpulse(player.Position.Y - _peakY));
            _airborne = false;
        }
    }

    /// <summary>An explosion's impulse: its radius (tiles) scales it, distance fades it to nothing at <see cref="ExplosionReach"/>.</summary>
    public static float ExplosionImpulse(float distance, int radius)
    {
        if (!(distance < ExplosionReach))
            return 0f;
        var size = Math.Clamp(0.25f + radius / 8f, 0.25f, 0.9f);
        return size * (1f - distance / ExplosionReach);
    }

    /// <summary>A landing's impulse: none below <see cref="HeavyLandingTiles"/>, then growing to a full jolt at twice that.</summary>
    public static float LandingImpulse(float fallTiles) =>
        fallTiles < HeavyLandingTiles ? 0f : Math.Clamp(0.3f + (fallTiles - HeavyLandingTiles) / HeavyLandingTiles * 0.5f, 0.3f, 0.8f);

    /// <summary>
    /// Advances the shake by <paramref name="deltaSeconds"/> and returns this frame's camera offset in pixels. With the
    /// preference off it returns zero and drops any pending shake.
    /// </summary>
    public Vector2 Offset(float deltaSeconds, bool enabled)
    {
        if (!enabled)
        {
            Trauma = 0f;
            return Vector2.Zero;
        }
        deltaSeconds = float.IsFinite(deltaSeconds) ? Math.Clamp(deltaSeconds, 0f, 0.25f) : 0f;
        _time += deltaSeconds;
        var strength = Trauma * Trauma * MaxOffsetPixels;
        Trauma = Math.Max(0f, Trauma - DecayPerSecond * deltaSeconds);
        if (strength <= 0.01f)
            return Vector2.Zero;
        // Two incommensurate wobbles per axis: irregular, but smooth from frame to frame.
        var x = (float)(Math.Sin(_time * 47.0) * 0.6 + Math.Sin(_time * 83.0 + 1.3) * 0.4);
        var y = (float)(Math.Sin(_time * 53.0 + 0.7) * 0.6 + Math.Sin(_time * 71.0 + 2.1) * 0.4);
        return new Vector2(x, y) * strength;
    }
}
