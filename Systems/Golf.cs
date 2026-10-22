using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>The golf ball in play (renderer, HUD and tests).</summary>
public sealed record GolfBallView(Vector2 Position, Vector2 Velocity, bool Resting, int Strokes, float Charge, ItemId? ChargingClub);

/// <summary>
/// Golf: one ball in play at a time. A club charges while the use button is held and swings on release (putter, iron and driver
/// differ in strength and loft); the ball bounces, rolls with per-surface friction, resets out of liquid with a penalty stroke, and
/// sinks into a cup. Strokes per hole show on the HUD; the greenskeeper moves in after the first golf ball is found.
/// </summary>
public sealed partial class GameSimulation
{
    public const float GolfGravity = 22f;
    public const float GolfReach = 3f;
    public const float GolfChargePerTick = 1f / 50f;
    public const float GolfSinkSpeed = 7f;

    private GolfBallState? _golfBall;
    private float _golfCharge;
    private long _golfChargeTick = -10;
    private ItemId? _golfClub;
    private Vector2 _golfAim;

    private sealed class GolfBallState
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public bool Resting = true;
        public float RestTimer;
        public int Strokes;
        public Vector2 LastRest;
    }

    public GolfBallView? GolfBall => _golfBall is { } ball
        ? new GolfBallView(ball.Position, ball.Velocity, ball.Resting, ball.Strokes, _golfCharge, _golfClub)
        : null;

    /// <summary>Holes sunk in this world.</summary>
    public long GolfHolesSunk => Flags.Get("golf.holes");

    public static (float Power, float Loft) ClubStats(ItemId club) => club switch
    {
        ItemId.GolfPutter => (7f, 0.05f),
        ItemId.GolfIron => (15f, 0.75f),
        _ => (24f, 0.45f)
    };

    private void RestoreGolfFromFlags()
    {
        // A ball left in play when the world was saved comes back to the pack.
        if (_golfBall is null && Flags.IsSet("golf.ball.out"))
        {
            Flags.Clear("golf.ball.out");
            Player.Inventory.Add(ItemId.GolfBall, 1);
        }
    }

    private void PlaceGolfBall(GridPoint point)
    {
        if (!World.Contains(point) || !IsInReach(point))
        {
            PostMessage("That is out of reach.");
            return;
        }
        var onTee = World.GetCell(point).TileId == TileId.GolfTee;
        if (!onTee && SolidAt(point.X, point.Y))
            point = new GridPoint(point.X, point.Y - 1);
        if (SolidAt(point.X, point.Y))
        {
            PostMessage("There is no room for the ball there.");
            return;
        }
        if (!Player.Inventory.TryConsume(ItemId.GolfBall))
            return;
        if (_golfBall is not null)
            Player.Inventory.Add(ItemId.GolfBall, 1);
        var position = onTee ? new Vector2(point.X + 0.5f, point.Y + 0.55f) : new Vector2(point.X + 0.5f, point.Y + 0.5f);
        _golfBall = new GolfBallState { Position = position, LastRest = position, Resting = onTee };
        Flags.Set("golf.ball.out");
        Flags.Set("npc.greenskeeper.unlocked");
        PostMessage(onTee ? "Ball teed up. Hold a club to charge, release to swing." : "Ball dropped. Hold a club to charge, release to swing.");
        SetPlayerCooldown(10);
    }

    /// <summary>Places a ball directly (tests): on a tee cell or anywhere open.</summary>
    public void DropGolfBallForTests(Vector2 position)
    {
        _golfBall = new GolfBallState { Position = position, LastRest = position, Resting = false };
        Flags.Set("golf.ball.out");
    }

    private void ChargeGolfClub(ItemId club, GridPoint point)
    {
        if (_golfBall is not { } ball)
        {
            PostMessage("Drop a golf ball first.");
            SetPlayerCooldown(15);
            return;
        }
        if (!ball.Resting || Vector2.Distance(ball.Position, PlayerCenter) > GolfReach)
        {
            PostMessage(ball.Resting ? "Walk up to your ball." : "Wait for the ball to stop.");
            SetPlayerCooldown(15);
            return;
        }
        if (_golfClub != club)
            _golfCharge = 0f;
        _golfClub = club;
        _golfCharge = Math.Min(1f, _golfCharge + GolfChargePerTick);
        _golfChargeTick = TickNumber;
        _golfAim = new Vector2(point.X + 0.5f, point.Y + 0.5f) - ball.Position;
    }

    /// <summary>Strikes the ball (also the release of a charged swing): power 0–1 of the club's strength toward <paramref name="aim"/>.</summary>
    public bool StrikeGolfBall(ItemId club, Vector2 aim, float power)
    {
        if (_golfBall is not { } ball || !ball.Resting)
            return false;
        var (strength, loft) = ClubStats(club);
        var horizontal = aim.X >= 0f ? 1f : -1f;
        // Aim sets the direction; each club adds its own loft (a putter rolls, an iron pops up, a driver flies long).
        var direction = aim.LengthSquared() > 0.001f ? Vector2.Normalize(aim) : new Vector2(horizontal, 0f);
        direction = Vector2.Normalize(new Vector2(direction.X, Math.Min(direction.Y, 0f) - loft));
        if (club == ItemId.GolfPutter)
            direction = new Vector2(horizontal, -0.05f);
        ball.Velocity = direction * strength * Math.Clamp(power, 0.05f, 1f);
        ball.Resting = false;
        ball.RestTimer = 0f;
        ball.Strokes++;
        _golfCharge = 0f;
        _golfClub = null;
        return true;
    }

    private void UpdateGolf(float deltaSeconds)
    {
        if (TickNumber % 60 == 0 && Player.Inventory.Count(ItemId.GolfBall) > 0)
            Flags.Set("npc.greenskeeper.unlocked");
        // Release: the club stopped being used on the previous tick.
        if (_golfClub is { } club && _golfChargeTick < TickNumber - 1)
        {
            if (Player.Inventory.SelectedItem?.Id == club)
                StrikeGolfBall(club, _golfAim, _golfCharge);
            _golfCharge = 0f;
            _golfClub = null;
        }
        if (_golfBall is not { } ball || ball.Resting)
            return;

        var remaining = deltaSeconds;
        while (remaining > 0f && _golfBall is not null)
        {
            var step = Math.Min(remaining, 0.01f);
            remaining -= step;
            StepGolfBall(ball, step);
        }
    }

    private static float SurfaceFriction(TileId tile) => tile switch
    {
        TileId.Grass or TileId.CanopyGrass or TileId.JungleGrass or TileId.HallowGrass or TileId.PrismaticGrass => 3f,
        TileId.Sand or TileId.DesertSand => 12f,
        TileId.Snow or TileId.RadiatedIce or TileId.OvergrowthIce => 0.8f,
        _ => 5f
    };

    private static float SurfaceBounce(TileId tile) => tile switch
    {
        TileId.Sand or TileId.DesertSand => 0.1f,
        TileId.Grass or TileId.CanopyGrass or TileId.JungleGrass or TileId.HallowGrass or TileId.PrismaticGrass => 0.35f,
        _ => 0.5f
    };

    private void StepGolfBall(GolfBallState ball, float step)
    {
        ball.Velocity += new Vector2(0f, GolfGravity * step);
        // Horizontal move.
        var nextX = ball.Position.X + ball.Velocity.X * step;
        if (SolidAt((int)MathF.Floor(nextX), (int)MathF.Floor(ball.Position.Y)))
            ball.Velocity = ball.Velocity with { X = -ball.Velocity.X * 0.45f };
        else
            ball.Position = ball.Position with { X = nextX };
        // Vertical move.
        var nextY = ball.Position.Y + ball.Velocity.Y * step;
        var column = (int)MathF.Floor(ball.Position.X);
        if (SolidAt(column, (int)MathF.Floor(nextY)))
        {
            if (ball.Velocity.Y > 0f)
            {
                var ground = World.Contains(column, (int)MathF.Floor(nextY)) ? World.GetCell(column, (int)MathF.Floor(nextY)).TileId : TileId.Rock;
                ball.Position = ball.Position with { Y = MathF.Floor(nextY) - 0.001f };
                ball.Velocity = ball.Velocity with { Y = ball.Velocity.Y > 2f ? -ball.Velocity.Y * SurfaceBounce(ground) : 0f };
            }
            else
                ball.Velocity = ball.Velocity with { Y = 0f };
        }
        else
            ball.Position = ball.Position with { Y = nextY };
        // Rolling: on (or a hair above) the ground and not bouncing, friction of the surface below slows the ball.
        var below = (int)MathF.Floor(ball.Position.Y + 0.02f);
        var grounded = SolidAt(column, below) && ball.Velocity.Y >= 0f && ball.Velocity.Y < 1f;
        if (grounded)
        {
            var ground = World.Contains(column, below) ? World.GetCell(column, below).TileId : TileId.Rock;
            var friction = SurfaceFriction(ground) * step;
            var vx = ball.Velocity.X;
            ball.Velocity = ball.Velocity with { X = MathF.Abs(vx) <= friction ? 0f : vx - MathF.Sign(vx) * friction };
        }

        var cellPoint = new GridPoint((int)MathF.Floor(ball.Position.X), (int)MathF.Floor(ball.Position.Y));
        if (!World.Contains(cellPoint))
        {
            ResetGolfBall(ball, "The ball sailed out of bounds.");
            return;
        }
        var cell = World.GetCell(cellPoint);
        if (cell.LiquidDepth > 0.3f)
        {
            ResetGolfBall(ball, "Splash! The ball is back where it was, plus a penalty stroke.");
            return;
        }
        if (cell.TileId == TileId.GolfCup && ball.Velocity.Length() < GolfSinkSpeed)
        {
            SinkGolfBall(ball);
            return;
        }
        if (grounded && MathF.Abs(ball.Velocity.X) < 0.05f)
        {
            ball.RestTimer += step;
            if (ball.RestTimer >= 0.25f)
            {
                ball.Resting = true;
                ball.Velocity = Vector2.Zero;
                ball.LastRest = ball.Position;
            }
        }
        else
            ball.RestTimer = 0f;
    }

    private void ResetGolfBall(GolfBallState ball, string message)
    {
        ball.Position = ball.LastRest;
        ball.Velocity = Vector2.Zero;
        ball.Resting = true;
        ball.Strokes++;
        PostMessage(message);
    }

    private void SinkGolfBall(GolfBallState ball)
    {
        var strokes = ball.Strokes;
        _golfBall = null;
        Flags.Clear("golf.ball.out");
        Flags.Increment("golf.holes");
        Player.Inventory.Add(ItemId.GolfBall, 1);
        LastGolfScore = strokes;
        PostMessage(strokes switch
        {
            1 => "Hole in one! The cup rings like a bell.",
            2 => "Sunk in 2 strokes. Crisp.",
            <= 4 => $"Sunk in {strokes} strokes. Solid round.",
            _ => $"Sunk in {strokes} strokes. The cup is patient."
        });
    }

    /// <summary>Strokes taken on the last hole sunk (0 before any).</summary>
    public int LastGolfScore { get; private set; }
}
