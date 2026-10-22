using System.Numerics;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Movement and physics parity probes (docs/MECHANICS_PARITY.md section A). Each probe builds a small world,
/// shapes terrain cells directly, drives the player with <see cref="GameInput"/> ticks and checks the
/// observable behaviour (not exact numbers; tuning stays Rimtera's own).
/// </summary>
public static class ParityMovementTests
{
    private const int Floor = 40;

    private static GameSimulation NewGame(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 96, height: 56) { FamilySpawnsEnabled = false };
        ClearEnemies(game);
        return game;
    }

    private static void ClearEnemies(GameSimulation game, EnemyAgent? keep = null)
    {
        foreach (var enemy in game.Enemies.ToArray())
            if (enemy != keep)
                enemy.TakeDamage(1e6f);
    }

    private static void Tick(GameSimulation game, int ticks = 1, MovementInput movement = default, EnemyAgent? keep = null)
    {
        for (var i = 0; i < ticks; i++)
        {
            game.Tick(new GameInput(movement));
            ClearEnemies(game, keep);
        }
    }

    /// <summary>Air box from x=1..94, y=1..Floor-1 with a <paramref name="floor"/> floor; player stands at <paramref name="x"/>.</summary>
    private static void Arena(GameSimulation game, float x = 20f, TileId floor = TileId.Rock)
    {
        for (var y = 1; y < game.World.Height - 1; y++)
        for (var cx = 1; cx < game.World.Width - 1; cx++)
        {
            var cell = new GridPoint(cx, y);
            game.World.SetTile(cell, y >= Floor ? (y == Floor ? floor : TileId.Rock) : TileId.Air);
            var data = game.World.GetCell(cell);
            data.LiquidDepth = 0f;
            data.LiquidKind = LiquidKind.None;
            data.Shape = TileShape.Full;
            game.World.SetCell(cell, data);
        }
        Place(game, new Vector2(x, Floor - game.Player.Motion.Size.Y - 0.01f));
    }

    private static void Place(GameSimulation game, Vector2 position)
    {
        game.Player.Motion.Position = position;
        game.Player.Motion.Velocity = Vector2.Zero;
        Tick(game, 3);
    }

    private static void Set(GameSimulation game, int x, int y, TileId tile, TileShape shape = TileShape.Full)
    {
        var cell = new GridPoint(x, y);
        game.World.SetTile(cell, tile);
        var data = game.World.GetCell(cell);
        data.Shape = shape;
        game.World.SetCell(cell, data);
    }

    private static void Fill(GameSimulation game, int left, int top, int right, int bottom, LiquidKind kind)
    {
        for (var y = top; y <= bottom; y++)
        for (var x = left; x <= right; x++)
        {
            var cell = new GridPoint(x, y);
            var data = game.World.GetCell(cell);
            data.LiquidKind = kind;
            data.LiquidDepth = 1f;
            game.World.SetCell(cell, data);
        }
    }

    private static MovementInput Run(float dir, float speed = 1f) => new(dir, SpeedMultiplier: speed);
    private static float Vx(GameSimulation game) => game.Player.Motion.Velocity.X;
    private static float Feet(GameSimulation game) => game.Player.Motion.Position.Y + game.Player.Motion.Size.Y;

    /// <summary>Top running speed reached after two seconds of holding right.</summary>
    private static float TopSpeed(GameSimulation game, float speed = 1f)
    {
        Tick(game, 120, Run(1f, speed));
        return Vx(game);
    }

    private static int TicksUntil(GameSimulation game, MovementInput input, Func<bool> done, int limit = 600)
    {
        for (var i = 1; i <= limit; i++)
        {
            Tick(game, 1, input);
            if (done())
                return i;
        }
        return limit + 1;
    }

    /// <summary>Highest point (smallest feet Y) reached during a jump whose button is held for <paramref name="holdTicks"/>.</summary>
    private static float JumpApex(GameSimulation game, int holdTicks)
    {
        var apex = Feet(game);
        game.Tick(new GameInput(new MovementInput(0f, JumpPressed: true, JumpHeld: true)));
        for (var i = 1; i < 90; i++)
        {
            Tick(game, 1, new MovementInput(0f, JumpHeld: i < holdTicks));
            apex = Math.Min(apex, Feet(game));
        }
        return Floor - apex;
    }

    public static void Run(Action<bool, string> check)
    {
        // ---- A1: acceleration, deceleration, reversing ----
        var runner = NewGame(7101);
        Arena(runner);
        var top = TopSpeed(runner);
        var decelTicks = TicksUntil(runner, default, () => Math.Abs(Vx(runner)) < 0.05f);
        Tick(runner, 10);
        var accelTicks = TicksUntil(runner, Run(1f), () => Vx(runner) >= 0.98f * top);
        Tick(runner, 30, Run(-1f));
        Tick(runner, 120);
        TopSpeed(runner);
        var reverseTicks = TicksUntil(runner, Run(-1f), () => Vx(runner) <= 0f);
        Tick(runner, 120, Run(-1f));
        var reversed = Vx(runner) < -0.9f * top;
        check(top > 3f && accelTicks > 3 && decelTicks < accelTicks && reverseTicks < decelTicks && reversed,
            $"A1 run accelerates to a cap, release brakes faster than accelerating, reversing brakes harder (accel {accelTicks}, stop {decelTicks}, reverse {reverseTicks} ticks)");

        // ---- A2: surfaces and speed bonuses ----
        int StopTicks(TileId floor)
        {
            var game = NewGame(7102);
            Arena(game, floor: floor);
            TopSpeed(game);
            return TicksUntil(game, default, () => Math.Abs(Vx(game)) < 0.05f);
        }
        float TopOn(TileId floor, float speed = 1f)
        {
            var game = NewGame(7103);
            Arena(game, x: 10f, floor: floor);
            return TopSpeed(game, speed);
        }
        var rockStop = StopTicks(TileId.Rock);
        var iceStop = StopTicks(TileId.Snow);
        var rockTop = TopOn(TileId.Rock);
        var mudTop = TopOn(TileId.Mud);
        var nectarTop = TopOn(TileId.Nectar);
        var bonusTop = TopOn(TileId.Rock, 1.5f);
        var webbed = NewGame(7104);
        Arena(webbed, x: 10f);
        for (var y = Floor - 3; y < Floor; y++)
        for (var x = 8; x < 60; x++)
            Set(webbed, x, y, TileId.MiteSilk);
        var webTop = TopSpeed(webbed);
        check(iceStop > rockStop * 2, $"A2 ice keeps you sliding (stop: rock {rockStop}, ice {iceStop} ticks)");
        check(mudTop < rockTop * 0.9f && nectarTop < rockTop * 0.8f && webTop < rockTop * 0.6f,
            $"A2 mud, nectar blocks and webs slow running (rock {rockTop:0.00}, mud {mudTop:0.00}, nectar {nectarTop:0.00}, web {webTop:0.00})");
        check(bonusTop > rockTop * 1.4f, $"A2 run-speed bonuses multiply the top speed ({rockTop:0.00} -> {bonusTop:0.00})");

        // ---- A3: jumping ----
        var jumper = NewGame(7105);
        Arena(jumper);
        var tapApex = JumpApex(jumper, 1);
        Place(jumper, new Vector2(20f, Floor - jumper.Player.Motion.Size.Y - 0.01f));
        var holdApex = JumpApex(jumper, 60);
        Place(jumper, new Vector2(20f, Floor - jumper.Player.Motion.Size.Y - 0.01f));
        var midApex = JumpApex(jumper, 6);
        check(holdApex > tapApex + 0.8f && midApex > tapApex && midApex < holdApex && holdApex < 8f,
            $"A3 holding jump lifts higher up to a cap; releasing early is shorter (tap {tapApex:0.00}, mid {midApex:0.00}, hold {holdApex:0.00} tiles)");
        Place(jumper, new Vector2(20f, Floor - 12f));
        Tick(jumper, 15);
        Tick(jumper, 1, new MovementInput(0f, JumpPressed: true, JumpHeld: true));
        check(!jumper.Player.Motion.Grounded && jumper.Player.Motion.Velocity.Y > 0f,
            "A3 no jumping in mid-air without a double jump or wings");
        Place(jumper, new Vector2(20f, Floor - jumper.Player.Motion.Size.Y - 3f));
        var pressedAt = -1;
        var bufferedJump = false;
        for (var i = 0; i < 120; i++)
        {
            var vy = jumper.Player.Motion.Velocity.Y;
            var press = pressedAt < 0 && vy > 0f && Floor - Feet(jumper) < vy / 60f * 3f;
            if (press)
                pressedAt = i;
            Tick(jumper, 1, new MovementInput(0f, JumpPressed: press, JumpHeld: pressedAt >= 0));
            if (pressedAt >= 0 && Floor - Feet(jumper) > 1f)
                bufferedJump = true;
        }
        check(pressedAt >= 0 && bufferedJump, "A3 a jump pressed just before landing still fires on landing");

        // ---- A4: gravity, terminal speed, fall damage ----
        var faller = NewGame(7106);
        Arena(faller);
        Place(faller, new Vector2(20f, 2f));
        Tick(faller, 60);
        var v1 = faller.Player.Motion.Velocity.Y;
        Tick(faller, 10);
        var v2 = faller.Player.Motion.Velocity.Y;
        check(v1 > 5f && Math.Abs(v1 - v2) < 0.01f, $"A4 gravity accelerates to a terminal fall speed ({v2:0.0} tiles/s)");
        float FallDamage(float height, ItemId? gear = null, bool pool = false)
        {
            var game = NewGame(7107);
            Arena(game);
            if (gear is { } item)
            {
                game.Player.Inventory.Add(item, 1);
                game.Player.Equipment.Set(EquipmentSlot.Accessory, item);
            }
            if (pool)
            {
                for (var y = Floor - 4; y < Floor; y++)
                {
                    Set(game, 14, y, TileId.Rock);
                    Set(game, 26, y, TileId.Rock);
                }
                Fill(game, 15, Floor - 4, 25, Floor - 1, LiquidKind.Water);
            }
            game.Player.Heal(1e6f);
            var hp = game.Player.HitPoints;
            Place(game, new Vector2(20f, Floor - game.Player.Motion.Size.Y - height));
            Tick(game, 240);
            return hp - game.Player.HitPoints;
        }
        var shortFall = FallDamage(12f);
        var midFall = FallDamage(29f);
        var longFall = FallDamage(33f);
        var featherFall = FallDamage(33f, ItemId.GliderWings);
        var poolFall = FallDamage(33f, pool: true);
        check(shortFall < 0.01f && midFall > 0f && longFall > midFall,
            $"A4 fall damage starts beyond a safe height and grows with distance (12: {shortFall:0}, 29: {midFall:0}, 33: {longFall:0})");
        check(featherFall < 0.01f && poolFall < 0.01f, "A4 featherfall gear and landing in liquid negate fall damage");

        // ---- A5: steps, slopes, half blocks, ceilings ----
        var stepper = NewGame(7108);
        Arena(stepper, x: 10f);
        Set(stepper, 16, Floor - 1, TileId.Rock);
        Set(stepper, 17, Floor - 1, TileId.Rock);
        Set(stepper, 18, Floor - 1, TileId.Rock);
        Set(stepper, 18, Floor - 2, TileId.Rock);
        Set(stepper, 18, Floor - 3, TileId.Rock);
        Tick(stepper, 120, Run(1f));
        var stepped = stepper.Player.Motion.Position.X > 16f && Math.Abs(Feet(stepper) - (Floor - 1)) < 0.05f &&
                      stepper.Player.Motion.Position.X + stepper.Player.Motion.Size.X <= 18.01f;
        check(stepped, "A5 running into a 1-tile step climbs it; a taller wall still stops you");
        var ramp = NewGame(7109);
        Arena(ramp, x: 8f);
        for (var i = 0; i < 6; i++)
        {
            Set(ramp, 12 + i, Floor - 1 - i, TileId.Rock, TileShape.SlopeUpRight);
            for (var y = Floor - i; y < Floor; y++)
                Set(ramp, 12 + i, y, TileId.Rock);
        }
        for (var x = 18; x < 40; x++)
        for (var y = Floor - 6; y < Floor; y++)
            Set(ramp, x, y, TileId.Rock);
        Tick(ramp, 30, Run(1f));
        var minRampSpeed = float.MaxValue;
        for (var i = 0; i < 150; i++)
        {
            Tick(ramp, 1, Run(1f));
            if (ramp.Player.Motion.Position.X is > 12.5f and < 17f)
                minRampSpeed = Math.Min(minRampSpeed, Vx(ramp));
        }
        check(ramp.Player.Motion.Position.X > 18f && Math.Abs(Feet(ramp) - (Floor - 6)) < 0.1f && minRampSpeed > 2f,
            $"A5 slopes are walked smoothly (slowest on ramp {minRampSpeed:0.00}, x {ramp.Player.Motion.Position.X:0.00}, feet {Feet(ramp):0.00})");
        var halfStep = NewGame(7110);
        Arena(halfStep, x: 10f);
        for (var x = 15; x < 40; x++)
            Set(halfStep, x, Floor - 1, TileId.Rock, x == 15 ? TileShape.Half : TileShape.Full);
        Tick(halfStep, 30, Run(1f));
        var onHalf = false;
        for (var i = 0; i < 60 && !onHalf; i++)
        {
            Tick(halfStep, 1, Run(1f));
            var cx = halfStep.Player.Motion.Position.X + halfStep.Player.Motion.Size.X * 0.5f;
            onHalf = cx is > 15.2f and < 15.8f && Math.Abs(Feet(halfStep) - (Floor - 0.5f)) < 0.12f;
        }
        Tick(halfStep, 60, Run(1f));
        check(onHalf && Math.Abs(Feet(halfStep) - (Floor - 1)) < 0.05f, "A5 half blocks act as half steps");
        var roof = NewGame(7111);
        Arena(roof);
        for (var x = 10; x < 30; x++)
            Set(roof, x, Floor - 4, TileId.Rock);
        var bumped = false;
        var ceilingBottom = Floor - 3f;
        var lowestHead = float.MaxValue;
        for (var i = 0; i < 60; i++)
        {
            Tick(roof, 1, new MovementInput(0f, JumpPressed: i == 0, JumpHeld: true));
            lowestHead = Math.Min(lowestHead, roof.Player.Motion.Position.Y);
            if (roof.Player.Motion.Position.Y < ceilingBottom + 0.25f)
                bumped = true;
        }
        check(bumped && lowestHead >= ceilingBottom - 0.01f && roof.Player.Motion.Grounded && Math.Abs(Feet(roof) - Floor) < 0.05f,
            $"A5 ceilings stop jumps without sticking (falls back while jump is still held) bumped {bumped} head {lowestHead:0.00} feet {Feet(roof):0.00} g {roof.Player.Motion.Grounded}");

        // ---- A6: platforms ----
        var plat = NewGame(7112);
        Arena(plat);
        for (var x = 10; x < 30; x++)
            Set(plat, x, Floor - 3, TileId.PlankPlatform);
        Place(plat, new Vector2(20f, Floor - 3 - plat.Player.Motion.Size.Y - 0.01f));
        Tick(plat, 20);
        var stands = plat.Player.Motion.Grounded && Math.Abs(Feet(plat) - (Floor - 3)) < 0.05f;
        Tick(plat, 30, new MovementInput(0f, Vertical: 1f));
        var dropped = Feet(plat) > Floor - 2.5f;
        Tick(plat, 30);
        Jump(plat, 30);
        Tick(plat, 60);
        var upThrough = Math.Abs(Feet(plat) - (Floor - 3)) < 0.05f && plat.Player.Motion.Grounded;
        check(stands, "A6 you stand on platforms");
        check(dropped, "A6 holding down drops through platforms");
        check(upThrough, "A6 you jump up through platforms from below and land on top");

        // ---- A7: ropes, vines, ladders, chains ----
        var climber = NewGame(7113);
        Arena(climber);
        for (var y = Floor - 16; y < Floor; y++)
            Set(climber, 20, y, TileId.Rope);
        Place(climber, new Vector2(19.64f, Floor - climber.Player.Motion.Size.Y - 0.01f));
        var startY = climber.Player.Motion.Position.Y;
        Tick(climber, 40, new MovementInput(0f, Vertical: -1f));
        var climbedUp = climber.Player.Motion.Climbing && climber.Player.Motion.Position.Y < startY - 2f;
        var hangY = climber.Player.Motion.Position.Y;
        Tick(climber, 60);
        var hangs = climber.Player.Motion.Climbing && Math.Abs(climber.Player.Motion.Position.Y - hangY) < 0.05f;
        Tick(climber, 15, new MovementInput(0f, Vertical: 1f));
        var climbedDown = climber.Player.Motion.Position.Y > hangY + 0.5f;
        Tick(climber, 1, new MovementInput(1f, JumpPressed: true, JumpHeld: true));
        var leftRope = !climber.Player.Motion.Climbing && climber.Player.Motion.Velocity.Y < 0f;
        check(climbedUp && climbedDown, "A7 up and down climb ropes");
        check(hangs, "A7 you can hang on a rope without falling");
        check(leftRope, "A7 jumping leaves the rope");

        // ---- A8: liquids ----
        var wader = NewGame(7114);
        Arena(wader, x: 10f);
        var dryTop = TopSpeed(wader);
        Place(wader, new Vector2(10f, Floor - wader.Player.Motion.Size.Y - 0.01f));
        Fill(wader, 2, Floor - 2, 90, Floor - 1, LiquidKind.Water);
        Tick(wader, 1);
        var wetTop = TopSpeed(wader);
        check(wetTop < dryTop * 0.8f, $"A8 water slows movement ({dryTop:0.00} -> {wetTop:0.00})");
        var breather = NewGame(7115);
        Arena(breather);
        for (var y = Floor - 6; y < Floor; y++)
        {
            Set(breather, 16, y, TileId.Rock);
            Set(breather, 24, y, TileId.Rock);
        }
        Fill(breather, 17, Floor - 1, 23, Floor - 1, LiquidKind.Water);
        Place(breather, new Vector2(19.6f, Floor - breather.Player.Motion.Size.Y - 0.01f));
        Tick(breather, 120);
        var waistDeep = breather.IsSwimming || !breather.HeadSubmerged;
        var breathKept = !breather.HeadSubmerged && breather.Breath >= breather.MaximumBreath - 0.01f;
        Fill(breather, 17, Floor - 5, 23, Floor - 1, LiquidKind.Water);
        Tick(breather, 120);
        var breathDrained = breather.HeadSubmerged && breather.Breath < breather.MaximumBreath - 1.5f;
        check(waistDeep && breathKept && breathDrained, "A8 breath drains only while the head is submerged");
        var swim = NewGame(7116);
        Arena(swim);
        for (var y = Floor - 12; y < Floor; y++)
        {
            Set(swim, 16, y, TileId.Rock);
            Set(swim, 24, y, TileId.Rock);
        }
        Fill(swim, 17, Floor - 12, 23, Floor - 1, LiquidKind.Water);
        Place(swim, new Vector2(19.6f, Floor - swim.Player.Motion.Size.Y - 0.01f));
        var swimStart = swim.Player.Motion.Position.Y;
        Tick(swim, 40, new MovementInput(0f, JumpHeld: true));
        check(swim.Player.Motion.Position.Y < swimStart - 1.5f, "A8 holding jump swims upward");
        var exit = NewGame(7117);
        Arena(exit);
        for (var y = Floor - 9; y < Floor; y++)
        {
            Set(exit, 16, y, TileId.Rock);
            Set(exit, 24, y, TileId.Rock);
        }
        for (var x = 25; x < 40; x++)
        for (var y = Floor - 9; y < Floor; y++)
            Set(exit, x, y, TileId.Rock);
        Fill(exit, 17, Floor - 8, 23, Floor - 1, LiquidKind.Water);
        Place(exit, new Vector2(21.5f, Floor - exit.Player.Motion.Size.Y - 0.01f));
        var climbedOut = false;
        for (var i = 0; i < 300 && !climbedOut; i++)
        {
            Tick(exit, 1, new MovementInput(1f, JumpPressed: i % 30 == 0, JumpHeld: true));
            climbedOut = exit.Player.Motion.Position.X > 25f && exit.Player.Motion.Grounded;
        }
        check(climbedOut, "A8 leaving liquid gives an exit boost to climb out over a 1-tile lip");
        var lava = NewGame(7118);
        Arena(lava, x: 10f);
        lava.Player.Heal(1e6f);
        Fill(lava, 2, Floor - 2, 90, Floor - 1, LiquidKind.Lava);
        var lavaHp = lava.Player.HitPoints;
        Tick(lava, 1);
        var lavaTop = TopSpeed(lava);
        check(lava.Player.HitPoints < lavaHp && (lavaTop < dryTop * 0.8f || lava.Player.IsDead), "A8 lava burns and slows");

        // ---- A9: mobility gear ----
        var gear = NewGame(7119);
        Arena(gear);
        foreach (var (slot, item) in new[] { (EquipmentSlot.Accessory, ItemId.DoubleJumpCanister), (EquipmentSlot.Accessory2, ItemId.RocketPack) })
        {
            gear.Player.Inventory.Add(item, 1);
            gear.Player.Equipment.Set(slot, item);
        }
        Tick(gear, 3);
        var fuel = gear.RocketFuel;
        Jump(gear, 12);
        Tick(gear, 1, new MovementInput(0f, JumpPressed: true, JumpHeld: true));
        var usedDouble = !gear.DoubleJumpAvailable;
        Tick(gear, 30, new MovementInput(0f, JumpHeld: true));
        var burned = gear.RocketFuel < fuel;
        Tick(gear, 240);
        check(usedDouble && burned && gear.Player.Motion.Grounded && gear.DoubleJumpAvailable && gear.RocketFuel >= fuel - 0.001f,
            "A9 double jump and rocket fuel are spent in the air and refill on landing");
        var wings = NewGame(7120);
        Arena(wings);
        wings.Player.Inventory.Add(ItemId.GliderWings, 1);
        wings.Player.Equipment.Set(EquipmentSlot.Accessory, ItemId.GliderWings);
        Place(wings, new Vector2(20f, Floor - 14f));
        Tick(wings, 20, new MovementInput(0f, JumpHeld: true));
        var spent = wings.Player.Motion.WingEnergy < 1f;
        Tick(wings, 240);
        Tick(wings, 3);
        check(spent && wings.Player.Motion.Grounded && wings.Player.Motion.WingEnergy >= 0.999f, "A9 wing time refills on landing");
        var dasher = NewGame(7121);
        Arena(dasher, x: 10f);
        Tick(dasher, 1, new MovementInput(0f, DashPressed: true));
        bool Dashing() => dasher.Player.Motion.DashTimeRemaining > 0f;
        var dashed = Dashing() && Vx(dasher) > 8f;
        Tick(dasher, 12);
        Tick(dasher, 1, new MovementInput(0f, DashPressed: true));
        var chained = Dashing();
        Tick(dasher, 60);
        Tick(dasher, 1, new MovementInput(0f, DashPressed: true));
        var again = Dashing();
        Tick(dasher, 60);
        Tick(dasher, 3, Run(1f));
        Tick(dasher, 3);
        Tick(dasher, 1, Run(1f));
        var doubleTap = Dashing() && Vx(dasher) > 8f;
        check(dashed && !chained && again, "A9 the dash key dashes, then has a cooldown");
        check(doubleTap, "A9 double-tapping a direction dashes");
        var hooker = NewGame(7122);
        Arena(hooker);
        for (var x = 10; x < 30; x++)
            Set(hooker, x, Floor - 12, TileId.Rock);
        hooker.Player.Inventory.Add(ItemId.GrappleHook, 1);
        hooker.Player.Equipment.Set(EquipmentSlot.Accessory, ItemId.GrappleHook);
        Tick(hooker, 2);
        var fired = hooker.TryFireGrapple(new Vector2(22f, Floor - 11.5f));
        var hookStart = hooker.Player.Motion.Position.Y;
        Tick(hooker, 50);
        var pulled = hooker.Grapple is { Latched: true } && hooker.Player.Motion.Position.Y < hookStart - 3f;
        Tick(hooker, 1, new MovementInput(0f, JumpPressed: true, JumpHeld: true));
        var jumpedOff = hooker.Grapple is null && hooker.Player.Motion.Velocity.Y < 0f;
        check(fired && pulled && jumpedOff, "A9 a grappling hook pulls toward its anchor and jumping lets go");

        // ---- A10: knockback and immunity ----
        var hit = NewGame(7123);
        Arena(hit, x: 30f);
        hit.Player.Heal(1e6f);
        var biter = hit.SpawnCreature(EnemyKind.GelHopper, hit.Player.Motion.Position + new Vector2(0.9f, 0.6f));
        var hp0 = hit.Player.HitPoints;
        var knockedAway = false;
        var hits = 0;
        var lastHp = hp0;
        var ticksSinceHit = -1;
        var repeatInWindow = false;
        for (var i = 0; i < 120; i++)
        {
            biter.Motion.Position = hit.Player.Motion.Position + new Vector2(0.9f, 0.6f);
            biter.AttackCooldown = 0f;
            Tick(hit, 1, keep: biter);
            if (hit.Player.HitPoints < lastHp - 0.01f)
            {
                if (hits == 0)
                    knockedAway = Vx(hit) < 0f && hit.Player.IsImmune;
                else if (ticksSinceHit < (int)(PlayerCharacter.HitImmunitySeconds * 60f) - 2)
                    repeatInWindow = true;
                hits++;
                ticksSinceHit = 0;
            }
            else if (ticksSinceHit >= 0)
                ticksSinceHit++;
            lastHp = hit.Player.HitPoints;
            if (hit.Player.IsDead)
                break;
        }
        check(hits >= 1 && knockedAway, "A10 a hit knocks you back away from the source and starts immunity (flashing)");
        check(hits >= 2 && !repeatInWindow, $"A10 immunity frames block repeat hits for a short window ({hits} hits in 2 s)");

        // ---- A11: world edges ----
        var edge = NewGame(7124);
        Arena(edge, x: 4f);
        Tick(edge, 180, Run(-1f));
        var leftOk = edge.Player.Motion.Position.X >= 0f;
        Place(edge, new Vector2(edge.World.Width - 5f, Floor - edge.Player.Motion.Size.Y - 0.01f));
        Tick(edge, 180, Run(1f));
        var rightOk = edge.Player.Motion.Position.X + edge.Player.Motion.Size.X <= edge.World.Width;
        check(leftOk && rightOk, "A11 you cannot leave the world's edges");
    }

    private static void Jump(GameSimulation game, int hold = 40)
    {
        Tick(game, 1, new MovementInput(0f, JumpPressed: true, JumpHeld: true));
        Tick(game, hold - 1, new MovementInput(0f, JumpHeld: true));
    }
}
