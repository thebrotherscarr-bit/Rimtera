using System.Numerics;
using Rimtera.World;

namespace Rimtera.Entities;

public readonly record struct GrappleConstraint(Vector2 Anchor, float RopeLength);

public readonly record struct MovementInput(
    float Horizontal,
    bool JumpPressed = false,
    bool DashPressed = false,
    bool WingsHeld = false,
    GrappleConstraint? Grapple = null,
    float SpeedMultiplier = 1f,
    bool JumpHeld = false,
    float Vertical = 0f)
{
    /// <summary>Holding down drops through one-way platforms (and climbs down ropes).</summary>
    public bool DropThrough => Vertical > 0.5f;
}

/// <summary>
/// The body every person in the world shares (player, crew, town NPCs): Terraria's 20 × 42 pixel player at
/// 16-pixel tiles, so it needs a gap two tiles wide and three tall, exactly as Terraria's does.
/// </summary>
public static class HumanoidBody
{
    public static readonly Vector2 Size = new(1.25f, 2.625f);
}

/// <summary>Movement dimensions use grid cells as world units.</summary>
public sealed class PawnMotionState
{
    public PawnMotionState(Vector2 position, Vector2 size)
    {
        if (!IsFinite(position) || !IsFinite(size) || size.X <= 0f || size.Y <= 0f)
            throw new ArgumentOutOfRangeException(nameof(size), "Position and size must be finite; size must be positive.");

        Position = position;
        Size = size;
    }

    public Vector2 Position { get; set; }
    public Vector2 Velocity { get; set; }
    public Vector2 Size { get; }
    public int Facing { get; internal set; } = 1;
    public bool Grounded { get; internal set; }
    public float DashTimeRemaining { get; internal set; }
    public float WingEnergy { get; set; } = 1f;

    /// <summary>Seconds of jump lift left while the jump button stays held.</summary>
    public float JumpSustainRemaining { get; internal set; }

    /// <summary>Seconds since the pawn last stood on ground (for the late-jump grace window).</summary>
    public float AirborneSeconds { get; internal set; }

    /// <summary>Highest point (smallest Y) since leaving the ground; used to measure falls.</summary>
    public float FallPeakY { get; internal set; }

    /// <summary>Tiles fallen on the most recent landing (0 when the pawn did not just land).</summary>
    public float LastLandingFallDistance { get; internal set; }

    /// <summary>Hanging on a rope, ladder or vine: no gravity, up/down input climbs.</summary>
    public bool Climbing { get; internal set; }

    /// <summary>Seconds a jump pressed too early (in the air) stays queued for the landing.</summary>
    public float JumpBufferRemaining { get; internal set; }

    /// <summary>Seconds until another dash may start (counts the dash itself plus the cooldown).</summary>
    public float DashCooldownRemaining { get; internal set; }

    // Double-tap dash tracking: last direction pressed, seconds since that press, and the previous tick's direction.
    internal int LastTapDirection { get; set; }
    internal float TapAge { get; set; } = 99f;
    internal int PreviousInputDirection { get; set; }

    private static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
}

/// <summary>Per-game tuning, expressed in grid units and seconds.</summary>
public sealed record PawnMovementSettings
{
    public float GroundAcceleration { get; init; } = 24f;
    public float AirAcceleration { get; init; } = 12f;
    public float MaximumRunSpeed { get; init; } = 5f;
    public float GroundFriction { get; init; } = 40f;
    public float AirFriction { get; init; } = 4f;
    public float Gravity { get; init; } = 24f;
    public float TerminalFallSpeed { get; init; } = 18f;
    public float JumpImpulse { get; init; } = 9.5f;
    public float DashSpeed { get; init; } = 12f;
    public float DashDuration { get; init; } = 0.16f;
    public bool LockVerticalVelocityDuringDash { get; init; } = true;
    public float WingLiftAcceleration { get; init; } = 18f;
    public float MaximumWingRiseSpeed { get; init; } = 8f;
    public float WingTerminalFallSpeed { get; init; } = 3f;
    public float WingEnergyDrainPerSecond { get; init; } = 0.35f;
    public float WingEnergyRecoveryPerSecond { get; init; } = 0.2f;
    public float CollisionSubstep { get; init; } = 0.2f;

    /// <summary>How long holding jump keeps the launch speed, for taller jumps (0 disables).</summary>
    public float JumpSustainSeconds { get; init; } = 0.2f;

    /// <summary>Grace window after walking off a ledge in which a jump still works.</summary>
    public float LateJumpSeconds { get; init; } = 0.08f;

    /// <summary>Highest ledge (in tiles) walked onto automatically while grounded (0 disables).</summary>
    public float StepUpHeight { get; init; } = 1f;

    /// <summary>Climbing speed on ropes, ladders and vines (tiles per second).</summary>
    public float ClimbSpeed { get; init; } = 4.5f;

    /// <summary>Sideways drift while climbing, as a fraction of the run speed.</summary>
    public float ClimbSideFactor { get; init; } = 0.35f;

    /// <summary>How long a jump pressed just before landing stays queued (fires on landing while still held).</summary>
    public float JumpBufferSeconds { get; init; } = 0.1f;

    /// <summary>Pause after a dash ends before the next one may start.</summary>
    public float DashCooldownSeconds { get; init; } = 0.45f;

    /// <summary>Two presses of the same direction within this many seconds start a dash (0 disables; AI pawns leave it off).</summary>
    public float DoubleTapDashWindow { get; init; }

    /// <summary>Fastest fall (tiles per second) inside sticky webs.</summary>
    public float WebFallSpeed { get; init; } = 1.5f;
}

/// <summary>What one collision probe is doing, so one-way platforms know whether to block it.</summary>
internal readonly record struct CollisionProbe(bool Vertical, bool MovingDown, float PreviousBottom, bool DropThrough);

/// <summary>
/// A compact platformer controller: accelerate/friction, gravity, optional jump,
/// dash, lift, rope constraint, then axis-separated swept tile collision.
/// </summary>
public sealed class PawnMovementSystem
{
    private readonly PawnMovementSettings _settings;

    public PawnMovementSystem(PawnMovementSettings? settings = null)
    {
        _settings = settings ?? new PawnMovementSettings();
        ValidateSettings(_settings);
    }

    public void Step(
        PawnMotionState pawn,
        in MovementInput input,
        float deltaSeconds,
        TileGrid world,
        TileCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(catalog);
        if (!float.IsFinite(deltaSeconds) || deltaSeconds <= 0f)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        if (!float.IsFinite(input.Horizontal) || !float.IsFinite(input.SpeedMultiplier))
            throw new ArgumentOutOfRangeException(nameof(input), "Horizontal input and speed multiplier must be finite.");
        if (!IsFinite(pawn.Position) || !IsFinite(pawn.Velocity) || !float.IsFinite(pawn.WingEnergy))
            throw new ArgumentOutOfRangeException(nameof(pawn), "Pawn position, velocity, and wing energy must be finite.");

        var speedMultiplier = Math.Clamp(input.SpeedMultiplier, 0.25f, 3f);
        pawn.WingEnergy = Math.Clamp(pawn.WingEnergy, 0f, 1f);
        var horizontal = Math.Clamp(input.Horizontal, -1f, 1f);
        if (horizontal != 0f)
            pawn.Facing = Math.Sign(horizontal);

        // Dash: the dash key, or a quick double tap of one direction; then a cooldown before the next.
        var inputDirection = Math.Abs(horizontal) > 0.5f ? Math.Sign(horizontal) : 0;
        var doubleTap = false;
        if (inputDirection != 0 && pawn.PreviousInputDirection == 0)
        {
            if (_settings.DoubleTapDashWindow > 0f && inputDirection == pawn.LastTapDirection && pawn.TapAge <= _settings.DoubleTapDashWindow)
            {
                doubleTap = true;
                pawn.LastTapDirection = 0;
            }
            else
            {
                pawn.LastTapDirection = inputDirection;
                pawn.TapAge = 0f;
            }
        }
        pawn.TapAge += deltaSeconds;
        pawn.PreviousInputDirection = inputDirection;
        pawn.DashCooldownRemaining = Math.Max(0f, pawn.DashCooldownRemaining - deltaSeconds);
        var dashStarted = (input.DashPressed || doubleTap) && pawn.DashTimeRemaining <= 0f && pawn.DashCooldownRemaining <= 0f;
        if (dashStarted)
        {
            pawn.DashTimeRemaining = _settings.DashDuration;
            pawn.DashCooldownRemaining = _settings.DashDuration + _settings.DashCooldownSeconds;
        }

        var dashActive = pawn.DashTimeRemaining > 0f;
        var wasGrounded = pawn.Grounded;
        var velocity = pawn.Velocity;
        var wingsActive = false;
        var vertical = float.IsFinite(input.Vertical) ? Math.Clamp(input.Vertical, -1f, 1f) : 0f;
        var dropThrough = input.DropThrough;

        // Ropes, ladders and vines: grab with up/down, let go with jump or by leaving the climbable.
        var onClimbable = OverlapsClimbable(pawn.Position, pawn.Size, world);
        if (pawn.Climbing && (!onClimbable || input.JumpPressed || dashActive))
        {
            pawn.Climbing = false;
            if (input.JumpPressed && onClimbable)
            {
                velocity.Y = -_settings.JumpImpulse * 0.8f;
                pawn.JumpSustainRemaining = 0f;
            }
        }
        else if (!pawn.Climbing && onClimbable && !dashActive && Math.Abs(vertical) > 0.5f &&
                 !(vertical > 0f && wasGrounded && !ClimbableBelowFeet(pawn.Position, pawn.Size, world)))
        {
            pawn.Climbing = true;
        }
        if (pawn.Climbing)
        {
            StepClimbing(pawn, horizontal, vertical, deltaSeconds, world, catalog, speedMultiplier);
            return;
        }

        // Surfaces: ice keeps momentum (low grip), mud and nectar blocks drag, webs smother all movement.
        var floorTile = wasGrounded ? FloorTileUnder(pawn.Position, pawn.Size, world) : TileId.Air;
        var grip = TilePhysics.SurfaceGrip(floorTile);
        var inWeb = OverlapsWeb(pawn.Position, pawn.Size, world);
        var runMultiplier = speedMultiplier * TilePhysics.SurfaceSpeedFactor(floorTile) * (inWeb ? TilePhysics.WebSpeedFactor : 1f);

        if (dashActive)
        {
            velocity.X = pawn.Facing * _settings.DashSpeed * speedMultiplier;
            if (_settings.LockVerticalVelocityDuringDash)
                velocity.Y = 0f;
            else
                velocity.Y = Math.Min(velocity.Y + _settings.Gravity * deltaSeconds, _settings.TerminalFallSpeed);
        }
        else
        {
            var friction = wasGrounded ? _settings.GroundFriction * grip : _settings.AirFriction;
            if (horizontal != 0f)
            {
                var targetSpeed = horizontal * _settings.MaximumRunSpeed * runMultiplier;
                var acceleration = (wasGrounded ? _settings.GroundAcceleration : _settings.AirAcceleration) * speedMultiplier;
                if (velocity.X != 0f && Math.Sign(velocity.X) != Math.Sign(horizontal))
                    acceleration += friction; // reversing: brake first, then accelerate the other way
                else if (Math.Abs(velocity.X) > Math.Abs(targetSpeed))
                    acceleration = Math.Max(friction, _settings.AirFriction); // over the cap (after a dash, onto slow ground): ease down
                velocity.X = MoveTowards(velocity.X, targetSpeed, acceleration * deltaSeconds);
            }
            else
            {
                velocity.X = MoveTowards(velocity.X, 0f, friction * deltaSeconds);
            }

            // A jump works on the ground or just after walking off a ledge; holding the button
            // keeps the launch speed for a short while, so a tap is a hop and a hold is a full jump.
            // A press just before landing is queued and fires on landing while the button is still held.
            var canJump = wasGrounded || pawn.AirborneSeconds <= _settings.LateJumpSeconds;
            var buffered = !input.JumpPressed && input.JumpHeld && pawn.JumpBufferRemaining > 0f;
            pawn.JumpBufferRemaining = Math.Max(0f, pawn.JumpBufferRemaining - deltaSeconds);
            if ((input.JumpPressed || buffered) && canJump && velocity.Y >= 0f)
            {
                velocity.Y = -_settings.JumpImpulse * (inWeb ? 0.35f : 1f);
                pawn.Grounded = false;
                pawn.AirborneSeconds = _settings.LateJumpSeconds + 1f;
                pawn.JumpSustainRemaining = inWeb ? 0f : _settings.JumpSustainSeconds;
                pawn.JumpBufferRemaining = 0f;
            }
            else if (input.JumpPressed)
            {
                pawn.JumpBufferRemaining = _settings.JumpBufferSeconds;
            }
            else if (input.JumpHeld && pawn.JumpSustainRemaining > 0f && velocity.Y < 0f)
            {
                velocity.Y = Math.Min(velocity.Y, -_settings.JumpImpulse);
                pawn.JumpSustainRemaining = Math.Max(0f, pawn.JumpSustainRemaining - deltaSeconds);
            }
            else if (!input.JumpHeld)
            {
                pawn.JumpSustainRemaining = 0f;
            }

            wingsActive = input.WingsHeld && pawn.WingEnergy > 0f && !wasGrounded;
            if (wingsActive)
            {
                velocity.Y = Math.Max(
                    velocity.Y - _settings.WingLiftAcceleration * deltaSeconds,
                    -_settings.MaximumWingRiseSpeed);
                velocity.Y = Math.Min(velocity.Y, _settings.WingTerminalFallSpeed);
            }
            else
            {
                velocity.Y = Math.Min(
                    velocity.Y + _settings.Gravity * deltaSeconds,
                    _settings.TerminalFallSpeed);
            }
            if (inWeb)
                velocity.Y = Math.Clamp(velocity.Y, -_settings.WebFallSpeed * 2f, _settings.WebFallSpeed);
        }

        var desiredPosition = pawn.Position + velocity * deltaSeconds;
        if (input.Grapple is { } grapple)
        {
            if (!IsFinite(grapple.Anchor) || !float.IsFinite(grapple.RopeLength) || grapple.RopeLength < 0f)
                throw new ArgumentOutOfRangeException(nameof(input), "Grapple anchor and rope length must be finite and non-negative.");

            var center = desiredPosition + pawn.Size * 0.5f;
            var offset = center - grapple.Anchor;
            var distance = offset.Length();
            if (distance > grapple.RopeLength && distance > 0f)
            {
                var radial = offset / distance;
                desiredPosition = grapple.Anchor + radial * grapple.RopeLength - pawn.Size * 0.5f;
                var outwardSpeed = Vector2.Dot(velocity, radial);
                if (outwardSpeed > 0f)
                    velocity -= radial * outwardSpeed;
            }
        }

        var (afterHorizontal, hitHorizontal) = MoveAxis(
            desiredPosition: pawn.Position,
            amount: desiredPosition.X - pawn.Position.X,
            horizontalAxis: true,
            pawn.Size,
            world,
            catalog,
            dropThrough,
            climbSlopes: wasGrounded);
        var slopeLift = afterHorizontal.Y - pawn.Position.Y;
        if (slopeLift < 0f)
            desiredPosition.Y = Math.Min(desiredPosition.Y, afterHorizontal.Y);
        pawn.Position = afterHorizontal;
        if (hitHorizontal && wasGrounded && !dashActive && _settings.StepUpHeight > 0f && horizontal != 0f)
        {
            // Walk up a low ledge: try the same move lifted by up to StepUpHeight tiles.
            var remaining = desiredPosition.X - afterHorizontal.X;
            var lift = new Vector2(0f, -_settings.StepUpHeight - 0.01f);
            if (!IsBlocked(afterHorizontal + lift, pawn.Size, world, catalog, Horizontal(afterHorizontal + lift, pawn.Size, dropThrough)))
            {
                var (stepped, stillBlocked) = MoveAxis(afterHorizontal + lift, remaining, horizontalAxis: true, pawn.Size, world, catalog,
                    dropThrough, climbSlopes: false);
                if (!stillBlocked || Math.Abs(stepped.X - afterHorizontal.X) > 0.05f)
                {
                    // Settle onto the step's surface, so a half block is a half step and a full block a full one.
                    var (settled, landed) = MoveAxis(stepped, _settings.StepUpHeight + 0.02f, horizontalAxis: false, pawn.Size, world, catalog,
                        dropThrough);
                    pawn.Position = landed ? settled : stepped;
                    desiredPosition.Y = pawn.Position.Y;
                    if (landed)
                        velocity.Y = 0f;
                    hitHorizontal = false;
                }
            }
        }
        if (hitHorizontal)
            velocity.X = 0f;

        var (afterVertical, hitVertical) = MoveAxis(
            desiredPosition: pawn.Position,
            amount: desiredPosition.Y - pawn.Position.Y,
            horizontalAxis: false,
            pawn.Size,
            world,
            catalog,
            dropThrough,
            climbSlopes: false);
        pawn.Position = afterVertical;
        if (hitVertical)
        {
            if (desiredPosition.Y > afterVertical.Y)
                pawn.Grounded = true;
            velocity.Y = 0f;
        }
        else
        {
            pawn.Grounded = false;
        }

        if (!pawn.Grounded && HasSupport(pawn.Position, pawn.Size, world, catalog, dropThrough))
            pawn.Grounded = true;

        // Walking down a slope: stay glued to the floor instead of skipping off it.
        if (!pawn.Grounded && wasGrounded && velocity.Y >= 0f && !dashActive && !input.JumpPressed && !dropThrough)
        {
            var snap = Math.Abs(velocity.X) * deltaSeconds + 0.06f;
            var probe = pawn.Position + new Vector2(0f, snap);
            if (snap < 0.6f && IsBlocked(probe, pawn.Size, world, catalog, new CollisionProbe(true, true, pawn.Position.Y + pawn.Size.Y, false)))
            {
                var (snapped, landed) = MoveAxis(pawn.Position, snap, horizontalAxis: false, pawn.Size, world, catalog);
                if (landed)
                {
                    pawn.Position = snapped;
                    pawn.Grounded = true;
                    velocity.Y = 0f;
                }
            }
        }

        // Late-jump timer and fall measurement for landings.
        pawn.LastLandingFallDistance = 0f;
        if (pawn.Grounded)
        {
            if (!wasGrounded)
                pawn.LastLandingFallDistance = Math.Max(0f, pawn.Position.Y - pawn.FallPeakY);
            pawn.AirborneSeconds = 0f;
            pawn.FallPeakY = pawn.Position.Y;
        }
        else
        {
            if (wasGrounded)
                pawn.FallPeakY = pawn.Position.Y;
            pawn.AirborneSeconds += deltaSeconds;
            pawn.FallPeakY = Math.Min(pawn.FallPeakY, pawn.Position.Y);
        }

        if (wingsActive)
            pawn.WingEnergy = Math.Max(0f, pawn.WingEnergy - _settings.WingEnergyDrainPerSecond * deltaSeconds);
        else if (pawn.Grounded)
            pawn.WingEnergy = 1f; // wing time refills on landing

        pawn.Velocity = velocity;
        if (dashActive)
            pawn.DashTimeRemaining = hitHorizontal ? 0f : Math.Max(0f, pawn.DashTimeRemaining - deltaSeconds);
    }

    private (Vector2 Position, bool Hit) MoveAxis(
        Vector2 desiredPosition,
        float amount,
        bool horizontalAxis,
        Vector2 size,
        TileGrid world,
        TileCatalog catalog,
        bool dropThrough = false,
        bool climbSlopes = false)
    {
        if (amount == 0f)
            return (desiredPosition, false);

        var steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(amount) / _settings.CollisionSubstep));
        var increment = amount / steps;
        var position = desiredPosition;
        for (var i = 0; i < steps; i++)
        {
            var candidate = position;
            if (horizontalAxis)
                candidate.X += increment;
            else
                candidate.Y += increment;

            var probe = horizontalAxis
                ? Horizontal(position, size, dropThrough)
                : new CollisionProbe(true, increment > 0f, position.Y + size.Y, dropThrough);
            if (IsBlocked(candidate, size, world, catalog, probe))
            {
                // Slopes: a grounded pawn walks up a 45-degree rise by lifting as far as it moved sideways.
                if (horizontalAxis && climbSlopes)
                {
                    var rise = Math.Abs(increment) + 0.02f;
                    var lifted = candidate with { Y = candidate.Y - rise };
                    var raised = position with { Y = position.Y - rise };
                    if (!IsBlocked(raised, size, world, catalog, new CollisionProbe(true, false, position.Y + size.Y, dropThrough)) &&
                        !IsBlocked(lifted, size, world, catalog, Horizontal(position, size, dropThrough)))
                    {
                        position = lifted;
                        continue;
                    }
                }
                return (position, true);
            }

            position = candidate;
        }

        return (position, false);
    }

    private static CollisionProbe Horizontal(Vector2 position, Vector2 size, bool dropThrough) =>
        new(false, false, position.Y + size.Y, dropThrough);

    private static bool IsBlocked(Vector2 position, Vector2 size, TileGrid world, TileCatalog catalog, in CollisionProbe probe)
    {
        const float edgeEpsilon = 0.00001f;
        var leftEdge = position.X;
        var rightEdge = position.X + size.X;
        var topEdge = position.Y;
        var bottomEdge = position.Y + size.Y;
        var left = (int)MathF.Floor(leftEdge + edgeEpsilon);
        var right = (int)MathF.Floor(rightEdge - edgeEpsilon);
        var top = (int)MathF.Floor(topEdge + edgeEpsilon);
        var bottom = (int)MathF.Floor(bottomEdge - edgeEpsilon);

        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                if (!world.Contains(x, y))
                    return true;
                var cell = world.GetCell(x, y);
                var tile = cell.TileId;
                if (tile == TileId.Air)
                    continue;
                if (TilePhysics.IsPlatform(tile))
                {
                    if (PlatformBlocks(cell, x, y, leftEdge, rightEdge, bottomEdge, probe))
                        return true;
                    continue;
                }
                if (TilePhysics.BoxHitsCell(cell, catalog, x, y, leftEdge, topEdge, rightEdge, bottomEdge))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// One-way platforms (flat, or sloped into stairs) only stop a pawn whose feet were at or above the walking
    /// surface before this probe; never while it rises or holds down to drop through.
    /// </summary>
    private static bool PlatformBlocks(in WorldCell cell, int x, int y, float leftEdge, float rightEdge, float bottomEdge, in CollisionProbe probe)
    {
        if (probe.DropThrough || cell.IsInactive)
            return false;
        var shape = cell.Shape is TileShape.SlopeUpRight or TileShape.SlopeUpLeft ? cell.Shape : TileShape.Full;
        var lx0 = Math.Max(0f, leftEdge - x);
        var lx1 = Math.Min(1f, rightEdge - x);
        if (lx1 - lx0 <= 0.001f)
            return false;
        var surface = y + TilePhysics.FloorTop(shape, lx0, lx1);
        if (bottomEdge <= surface + 0.001f)
            return false;
        if (probe.Vertical)
            return probe.MovingDown && probe.PreviousBottom <= surface + 0.02f;
        // Sideways: only stairs block, and only a pawn already walking on them (so it climbs instead of sinking).
        return shape != TileShape.Full && probe.PreviousBottom <= surface + 0.3f;
    }

    private static bool HasSupport(Vector2 position, Vector2 size, TileGrid world, TileCatalog catalog, bool dropThrough = false) =>
        IsBlocked(position + new Vector2(0f, 0.01f), size, world, catalog,
            new CollisionProbe(true, true, position.Y + size.Y, dropThrough));

    /// <summary>True when an actor box at <paramref name="position"/> overlaps solid geometry (shapes, actuation, platforms ignored).</summary>
    public static bool OverlapsSolid(Vector2 position, Vector2 size, TileGrid world, TileCatalog catalog) =>
        IsBlocked(position, size, world, catalog, new CollisionProbe(false, false, position.Y + size.Y, true));

    /// <summary>The tile the pawn stands on (centre of the feet first, then either foot).</summary>
    private static TileId FloorTileUnder(Vector2 position, Vector2 size, TileGrid world)
    {
        var row = (int)MathF.Floor(position.Y + size.Y + 0.05f);
        foreach (var fx in new[] { position.X + size.X * 0.5f, position.X + 0.05f, position.X + size.X - 0.05f })
        {
            var column = (int)MathF.Floor(fx);
            if (world.Contains(column, row) && world.GetCell(column, row).TileId is var tile && tile != TileId.Air)
                return tile;
        }
        return TileId.Air;
    }

    private static bool OverlapsWeb(Vector2 position, Vector2 size, TileGrid world)
    {
        var left = (int)MathF.Floor(position.X + 0.05f);
        var right = (int)MathF.Floor(position.X + size.X - 0.05f);
        var top = (int)MathF.Floor(position.Y + 0.05f);
        var bottom = (int)MathF.Floor(position.Y + size.Y - 0.05f);
        for (var y = top; y <= bottom; y++)
        for (var x = left; x <= right; x++)
            if (world.Contains(x, y) && TilePhysics.IsWeb(world.GetCell(x, y).TileId))
                return true;
        return false;
    }

    // ------------------------------------------------------------------ climbing

    private static bool OverlapsClimbable(Vector2 position, Vector2 size, TileGrid world)
    {
        var column = (int)MathF.Floor(position.X + size.X * 0.5f);
        var top = (int)MathF.Floor(position.Y + 0.05f);
        var bottom = (int)MathF.Floor(position.Y + size.Y - 0.05f);
        for (var y = top; y <= bottom; y++)
            if (world.Contains(column, y) && TilePhysics.IsClimbable(world.GetCell(column, y).TileId))
                return true;
        return false;
    }

    private static bool IsClimbableAt(TileGrid world, int x, int y) =>
        world.Contains(x, y) && TilePhysics.IsClimbable(world.GetCell(x, y).TileId);

    private static bool ClimbableBelowFeet(Vector2 position, Vector2 size, TileGrid world)
    {
        var column = (int)MathF.Floor(position.X + size.X * 0.5f);
        var row = (int)MathF.Floor(position.Y + size.Y + 0.05f);
        return world.Contains(column, row) && TilePhysics.IsClimbable(world.GetCell(column, row).TileId);
    }

    private void StepClimbing(PawnMotionState pawn, float horizontal, float vertical, float deltaSeconds, TileGrid world, TileCatalog catalog,
        float speedMultiplier)
    {
        var size = pawn.Size;
        var column = (int)MathF.Floor(pawn.Position.X + size.X * 0.5f);
        var velocity = new Vector2(horizontal * _settings.MaximumRunSpeed * _settings.ClimbSideFactor * speedMultiplier,
            vertical * _settings.ClimbSpeed * speedMultiplier);
        // Without sideways input, settle onto the middle of the rope.
        if (horizontal == 0f)
        {
            var centered = column + 0.5f - size.X * 0.5f;
            velocity.X = Math.Clamp((centered - pawn.Position.X) / deltaSeconds, -6f, 6f);
        }
        var moveY = velocity.Y * deltaSeconds;
        if (moveY < 0f)
        {
            // Climbing up stops with the feet just inside the top cell of the climbable.
            var feet = pawn.Position.Y + size.Y;
            var topRow = (int)MathF.Floor(pawn.Position.Y);
            var row = (int)MathF.Floor(feet - 0.001f);
            while (row >= topRow && !IsClimbableAt(world, column, row))
                row--;
            if (row < topRow)
                moveY = 0f;
            else
            {
                while (IsClimbableAt(world, column, row - 1))
                    row--;
                moveY = Math.Max(moveY, Math.Min(0f, row + 0.1f - feet));
            }
        }

        var (afterHorizontal, hitHorizontal) = MoveAxis(pawn.Position, velocity.X * deltaSeconds, horizontalAxis: true, size, world, catalog,
            dropThrough: true);
        var (afterVertical, hitVertical) = MoveAxis(afterHorizontal, moveY, horizontalAxis: false, size, world, catalog, dropThrough: true);
        pawn.Position = afterVertical;
        if (hitHorizontal)
            velocity.X = 0f;
        pawn.Velocity = new Vector2(velocity.X, hitVertical ? 0f : moveY / deltaSeconds);
        pawn.Grounded = hitVertical && moveY > 0f;
        if (pawn.Grounded)
            pawn.Climbing = false;
        pawn.AirborneSeconds = 0f;
        pawn.FallPeakY = pawn.Position.Y;
        pawn.LastLandingFallDistance = 0f;
        pawn.JumpSustainRemaining = 0f;
        pawn.WingEnergy = Math.Min(1f, pawn.WingEnergy + _settings.WingEnergyRecoveryPerSecond * deltaSeconds);
    }

    private static float MoveTowards(float current, float target, float maxDelta)
    {
        if (Math.Abs(target - current) <= maxDelta)
            return target;
        return current + Math.Sign(target - current) * maxDelta;
    }

    private static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static void ValidateSettings(PawnMovementSettings settings)
    {
        var positive = new[]
        {
            settings.GroundAcceleration, settings.AirAcceleration, settings.MaximumRunSpeed,
            settings.GroundFriction, settings.AirFriction, settings.Gravity,
            settings.TerminalFallSpeed, settings.JumpImpulse, settings.DashSpeed,
            settings.DashDuration, settings.WingLiftAcceleration, settings.MaximumWingRiseSpeed,
            settings.WingTerminalFallSpeed, settings.CollisionSubstep
        };
        if (positive.Any(value => !float.IsFinite(value) || value <= 0f))
            throw new ArgumentOutOfRangeException(nameof(settings), "Movement speeds, accelerations, and durations must be finite and positive.");

        if (!float.IsFinite(settings.WingEnergyDrainPerSecond) || settings.WingEnergyDrainPerSecond < 0f ||
            !float.IsFinite(settings.WingEnergyRecoveryPerSecond) || settings.WingEnergyRecoveryPerSecond < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Wing energy rates must be finite and non-negative.");
        }
    }
}
