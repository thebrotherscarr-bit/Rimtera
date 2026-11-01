using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Self-tests for Batch A world physics (slopes, platforms, climbing, plants, falling blocks, liquids, wiring, explosives, worldgen).</summary>
public static class BatchASelfTests
{
    /// <summary>Runs only these checks (<c>dotnet run -- --batch-a-test</c>).</summary>
    public static int RunStandalone()
    {
        GameSimulation.FastClock = true;
        var passed = 0;
        Run((condition, name) =>
        {
            if (!condition)
                throw new InvalidOperationException($"Self-test failed: {name}");
            passed++;
            Console.WriteLine($"PASS {name}");
        });
        Console.WriteLine($"{passed} Batch A checks passed.");
        return 0;
    }

    public static void Run(Action<bool, string> check)
    {
        var catalog = new TileCatalog();
        var mover = new PawnMovementSystem();
        const float dt = 1f / 60f;

        TileGrid Arena(int width = 40, int height = 20, int floor = 15)
        {
            var grid = new TileGrid(width, height);
            for (var x = 0; x < width; x++)
            for (var y = floor; y < height; y++)
                grid.SetTile(new GridPoint(x, y), TileId.Rock);
            return grid;
        }
        void Shape(TileGrid grid, int x, int y, TileId tile, TileShape shape)
        {
            grid.SetTile(new GridPoint(x, y), tile);
            var cell = grid.GetCell(x, y);
            cell.Shape = shape;
            grid.SetCell(new GridPoint(x, y), cell);
        }

        // ---- Slopes: walking up a ramp lifts the pawn smoothly (no one-tile hop) ----
        var ramp = Arena();
        Shape(ramp, 10, 14, TileId.Rock, TileShape.SlopeUpRight);
        ramp.SetTile(new GridPoint(11, 14), TileId.Rock);
        Shape(ramp, 11, 13, TileId.Rock, TileShape.SlopeUpRight);
        for (var x = 12; x < 40; x++)
        {
            ramp.SetTile(new GridPoint(x, 14), TileId.Rock);
            ramp.SetTile(new GridPoint(x, 13), TileId.Rock);
        }
        var walker = new PawnMotionState(new Vector2(5f, 15f - 1.8f), new Vector2(0.72f, 1.8f));
        mover.Step(walker, new MovementInput(0f), dt, ramp, catalog);
        var maxRise = 0f;
        for (var i = 0; i < 150; i++)
        {
            var before = walker.Position.Y;
            mover.Step(walker, new MovementInput(1f), dt, ramp, catalog);
            maxRise = Math.Max(maxRise, before - walker.Position.Y);
        }
        check(walker.Position.X > 14f && walker.Position.Y + walker.Size.Y <= 13.05f && walker.Grounded && maxRise < 0.35f,
            "pawns walk smoothly up a hammered slope onto the higher ground");

        // Half blocks: a pawn rests half a tile up.
        var half = Arena();
        Shape(half, 5, 14, TileId.Rock, TileShape.Half);
        var sitter = new PawnMotionState(new Vector2(5.1f, 10f), new Vector2(0.72f, 1.8f));
        for (var i = 0; i < 90; i++)
            mover.Step(sitter, new MovementInput(0f), dt, half, catalog);
        check(sitter.Grounded && Math.Abs(sitter.Position.Y + sitter.Size.Y - 14.5f) < 0.06f,
            "half blocks hold a pawn half a tile above the floor");

        // ---- Platforms: land on top, drop through holding down, pass up from below ----
        var deck = Arena();
        for (var x = 3; x <= 12; x++)
            deck.SetTile(new GridPoint(x, 12), TileId.PlankPlatform);
        var jumper = new PawnMotionState(new Vector2(6f, 9f), new Vector2(0.72f, 1.8f));
        for (var i = 0; i < 60; i++)
            mover.Step(jumper, new MovementInput(0f), dt, deck, catalog);
        var landed = jumper.Grounded && Math.Abs(jumper.Position.Y + jumper.Size.Y - 12f) < 0.05f;
        for (var i = 0; i < 40; i++)
            mover.Step(jumper, new MovementInput(0f, Vertical: 1f), dt, deck, catalog);
        var dropped = jumper.Position.Y + jumper.Size.Y > 14.9f;
        for (var i = 0; i < 90; i++)
            mover.Step(jumper, new MovementInput(0f, JumpPressed: i == 0, JumpHeld: i < 20), dt, deck, catalog);
        check(landed && dropped && jumper.Grounded && Math.Abs(jumper.Position.Y + jumper.Size.Y - 12f) < 0.05f,
            "one-way platforms hold from above, drop through on down, and pass a jump from below");

        // Sloped platforms make stairs.
        var stairs = Arena();
        Shape(stairs, 10, 14, TileId.PlankPlatform, TileShape.SlopeUpRight);
        Shape(stairs, 11, 13, TileId.PlankPlatform, TileShape.SlopeUpRight);
        Shape(stairs, 12, 12, TileId.PlankPlatform, TileShape.SlopeUpRight);
        for (var x = 13; x < 20; x++)
            stairs.SetTile(new GridPoint(x, 12), TileId.PlankPlatform);
        var climber = new PawnMotionState(new Vector2(5f, 15f - 1.8f), new Vector2(0.72f, 1.8f));
        for (var i = 0; i < 160; i++)
            mover.Step(climber, new MovementInput(1f), dt, stairs, catalog);
        check(climber.Position.X > 13.5f && climber.Position.Y + climber.Size.Y <= 12.05f,
            "sloped platforms work as stairs");

        // ---- Ropes: climb with up, hang without gravity, let go with jump ----
        var shaft = Arena();
        for (var y = 5; y <= 14; y++)
            shaft.SetTile(new GridPoint(10, y), TileId.Rope);
        var roper = new PawnMotionState(new Vector2(9.7f, 15f - 1.8f), new Vector2(0.72f, 1.8f));
        mover.Step(roper, new MovementInput(0f), dt, shaft, catalog);
        for (var i = 0; i < 150; i++)
            mover.Step(roper, new MovementInput(0f, Vertical: -1f), dt, shaft, catalog);
        var climbedTo = roper.Position.Y + roper.Size.Y;
        for (var i = 0; i < 30; i++)
            mover.Step(roper, new MovementInput(0f), dt, shaft, catalog);
        var held = roper.Climbing && Math.Abs(roper.Position.Y + roper.Size.Y - climbedTo) < 0.01f;
        mover.Step(roper, new MovementInput(0f, JumpPressed: true), dt, shaft, catalog);
        check(climbedTo < 5.2f && climbedTo >= 5.05f && held && !roper.Climbing && roper.Velocity.Y < 0f,
            "ropes are climbed with up/down, hold the pawn without gravity and release on jump");

        // ---- Liquids: water + liquid uranium harden into vitrified core glass; nectar + water into hive comb ----
        var basin = new TileGrid(12, 8);
        for (var x = 0; x < 12; x++)
            basin.SetTile(new GridPoint(x, 7), TileId.Rock);
        var liquids = new ActiveLiquidSimulator(basin, catalog);
        basin.TryAddLiquid(new GridPoint(3, 6), LiquidKind.Water, 1f, catalog);
        basin.TryAddLiquid(new GridPoint(4, 6), LiquidKind.Lava, 1f, catalog);
        basin.TryAddLiquid(new GridPoint(8, 6), LiquidKind.Nectar, 1f, catalog);
        basin.TryAddLiquid(new GridPoint(9, 6), LiquidKind.Water, 0.5f, catalog);
        liquids.RunUntilSettled(80);
        check(basin.CountTiles(TileId.VitrifiedGlass) >= 1 && basin.CountTiles(TileId.HiveComb) >= 1 && liquids.Merges >= 2 &&
              ActiveLiquidSimulator.MergeTile(LiquidKind.Acid, LiquidKind.Lava) == TileId.CoreCrust,
            "touching liquids merge: water and liquid uranium make vitrified glass, nectar and water make hive comb");

        // ---- The integrated simulation: plants, falling sand, wiring, explosives ----
        var game = new GameSimulation(seed: 4242, width: 80, height: 48);
        var feet = game.Player.Motion.Position + new Vector2(game.Player.Motion.Size.X / 2f, game.Player.Motion.Size.Y + 0.05f);
        var px = (int)MathF.Floor(feet.X);
        var groundRow = (int)MathF.Floor(feet.Y);
        void Clear(int left, int top, int right, int bottom)
        {
            for (var y = top; y <= bottom; y++)
            for (var x = left; x <= right; x++)
            {
                var cell = game.World.GetCell(x, y);
                cell.TileId = TileId.Air;
                cell.LiquidUnits = 0;
                cell.LiquidKind = LiquidKind.None;
                cell.Flags = 0;
                game.World.SetCell(new GridPoint(x, y), cell);
            }
        }
        void Ground(int left, int right)
        {
            for (var x = left; x <= right; x++)
                game.World.SetTile(new GridPoint(x, groundRow), TileId.Grass);
        }
        void Idle(int ticks)
        {
            for (var i = 0; i < ticks; i++)
                game.Tick(new GameInput(new MovementInput(0f)));
        }
        void Use(ItemId item, GridPoint target, int ticks = 1)
        {
            if (game.Player.Inventory.Count(item) == 0)
                game.Player.Inventory.Add(item, 1);
            game.TrySelectInventorySlot(game.Player.Inventory.FindSlot(item));
            for (var i = 0; i < ticks; i++)
                game.Tick(new GameInput(new MovementInput(0f), PlayerAction.UseSelected, target));
        }
        Clear(px - 8, groundRow - 16, px + 8, groundRow - 1);
        Ground(px - 8, px + 8);
        Idle(5);

        // Saplings grow into trees; cutting the trunk fells the whole tree for timber and saplings.
        var treeX = px + 3;
        game.World.SetTile(new GridPoint(treeX, groundRow - 1), TileId.Sapling);
        // Flora parity: a sapling grows on 1 random update in 20 (and only with room), so allow many more updates than before.
        for (var i = 0; i < 1200 && game.World.TileAt(treeX, groundRow - 1) == TileId.Sapling; i++)
            game.Ticker.UpdateCell(treeX, groundRow - 1);
        var grew = TilePhysics.IsTrunk(game.World.TileAt(treeX, groundRow - 1)) && TilePhysics.IsTreePart(game.World.TileAt(treeX, groundRow - 4));
        var timberBefore = game.Player.Inventory.Count(ItemId.Timber);
        game.Player.Inventory.Add(ItemId.FerriteCutter, 1);
        // A copper-tier cutter needs about 13 chops at 21 ticks each (100 damage per segment, axe × 1.2 per chop).
        Use(ItemId.FerriteCutter, new GridPoint(treeX, groundRow - 1), 320);
        var treeLeft = 0;
        for (var y = groundRow - 16; y < groundRow; y++)
        for (var x = treeX - 3; x <= treeX + 3; x++)
            if (TilePhysics.IsTreePart(game.World.TileAt(x, y)))
                treeLeft++;
        // Parity B4: felled timber and saplings pop out as ground items; count what lies there plus what the magnet grabbed.
        int Owned(ItemId id) => game.Player.Inventory.Count(id) + game.GroundItems.Where(drop => drop.ItemId == id).Sum(drop => drop.Count);
        // Flora parity: saplings now drop on a 1-in-2 roll per leafy top and branch (and on shakes), so a single fell may drop
        // none; FloraParitySelfTests checks the sapling rate over many fells.
        check(grew && treeLeft == 0 && Owned(ItemId.Timber) > timberBefore + 4,
            "saplings grow into trees, and chopping the trunk fells the whole tree for timber and saplings");

        // Herbs mature and drop seeds when harvested in bloom (sunpetal blooms by day).
        var herbCell = new GridPoint(px - 3, groundRow - 1);
        game.World.SetTile(herbCell, TileId.HerbSunpetal);
        // Flora parity: a young herb matures on 1 update in 50 (plus 1 in 50 in its own conditions), so allow more updates.
        for (var i = 0; i < 1500 && (game.World.GetCell(herbCell).State & 1) == 0; i++)
            game.Ticker.UpdateCell(herbCell.X, herbCell.Y);
        var blooming = game.IsHerbBlooming(herbCell);
        game.Interact(herbCell);
        check(blooming && Owned(ItemId.SunpetalHerb) >= 1 && Owned(ItemId.SunpetalSeeds) >= 1 &&
              game.World.TileAt(herbCell.X, herbCell.Y) == TileId.Air,
            "herbs mature over time and yield herbs and seeds when picked in bloom");

        // Falling sand: knock out the support and the column falls and re-settles on the floor.
        var sx = px - 6;
        Clear(sx, groundRow - 15, sx, groundRow - 1);
        game.World.SetTile(new GridPoint(sx, groundRow - 9), TileId.Rock);
        for (var y = groundRow - 12; y <= groundRow - 10; y++)
            game.World.SetTile(new GridPoint(sx, y), TileId.Sand);
        Idle(2);
        game.World.SetTile(new GridPoint(sx, groundRow - 9), TileId.Air);
        var sawFalling = false;
        for (var i = 0; i < 150; i++)
        {
            Idle(1);
            sawFalling |= game.FallingBlocks.Count > 0;
        }
        check(sawFalling && game.FallingBlocks.Count == 0 && game.World.TileAt(sx, groundRow - 1) == TileId.Sand &&
              game.World.TileAt(sx, groundRow - 2) == TileId.Sand && game.World.TileAt(sx, groundRow - 3) == TileId.Sand &&
              game.World.TileAt(sx, groundRow - 10) == TileId.Air,
            "unsupported sand falls as moving blocks, cascades and re-settles on the ground");

        // Torches: placeable on walls or floors and emit light the renderer can query.
        game.Player.Inventory.Add(ItemId.WallTorch, 2);
        Use(ItemId.WallTorch, new GridPoint(px - 1, groundRow - 1));
        check(game.World.TileAt(px - 1, groundRow - 1) == TileId.WallTorch && game.GetCellLight(px - 1, groundRow - 1) is { R: > 0.5f } &&
              GameSimulation.GetTileLight(TileId.StandingTorch) is not null,
            "torches place on the floor and register as light emitters");

        // Wiring: a switch toggles a wired door and lamp.
        var row = groundRow - 1;
        var switchCell = new GridPoint(px + 1, row);
        var lampCell = new GridPoint(px - 2, row);
        var doorX = px + 5;
        game.World.SetTile(switchCell, TileId.Switch);
        game.World.SetTile(lampCell, TileId.DeskLamp);
        for (var dy = 0; dy < 3; dy++)
            game.World.SetTile(new GridPoint(doorX, row - dy), TileId.PlankDoor);
        for (var x = lampCell.X; x <= doorX; x++)
            game.PlaceWire(new GridPoint(x, row));
        foreach (var agent in game.Colonists)
            agent.Motion.Position = new Vector2(3f, agent.Motion.Position.Y);
        game.Interact(switchCell);
        var opened = game.World.TileAt(doorX, row) == TileId.PlankDoorOpen && game.World.TileAt(doorX, row - 2) == TileId.PlankDoorOpen &&
                     game.GetCellLight(lampCell.X, lampCell.Y) is null;
        game.Interact(switchCell);
        check(opened && game.World.TileAt(doorX, row) == TileId.PlankDoor && game.GetCellLight(lampCell.X, lampCell.Y) is not null,
            "a wired switch toggles a door and a lamp on its network");

        // Actuators: a lever pulse makes a block passable, and again solid.
        var blockCell = new GridPoint(px + 2, row - 4);
        var leverCell = new GridPoint(px + 2, row - 3);
        game.World.SetTile(blockCell, TileId.Rock);
        game.World.SetTile(leverCell, TileId.Lever);
        game.PlaceActuator(blockCell);
        game.PlaceWire(blockCell, 2);
        game.PlaceWire(leverCell, 2);
        game.Interact(leverCell);
        var probe = new Vector2(blockCell.X + 0.1f, blockCell.Y + 0.1f);
        var passable = game.World.GetCell(blockCell).IsInactive &&
                       !PawnMovementSystem.OverlapsSolid(probe, new Vector2(0.8f, 0.8f), game.World, game.Tiles);
        game.Interact(leverCell);
        check(passable && PawnMovementSystem.OverlapsSolid(probe, new Vector2(0.8f, 0.8f), game.World, game.Tiles) &&
              game.World.TileAt(blockCell.X, blockCell.Y) == TileId.Rock,
            "actuators toggle a block between solid and passable on a wire pulse");

        // Timers pulse their network on a schedule; logic gates combine inputs.
        var timerCell = new GridPoint(px - 3, row - 3);
        var timedLamp = new GridPoint(px - 1, row - 3);
        game.World.SetTile(timerCell, TileId.Timer1s);
        game.World.SetTile(timedLamp, TileId.FloorLamp);
        for (var x = timerCell.X; x <= timedLamp.X; x++)
            game.PlaceWire(new GridPoint(x, row - 3), 3);
        var pulsesBefore = game.WirePulses;
        game.Interact(timerCell);
        Idle(62);
        var offAfterOne = game.GetCellLight(timedLamp.X, timedLamp.Y) is null;
        Idle(60);
        check(offAfterOne && game.GetCellLight(timedLamp.X, timedLamp.Y) is not null && game.WirePulses - pulsesBefore >= 2,
            "a one-second timer pulses its network every second");
        game.Interact(timerCell);

        var gateCell = new GridPoint(px - 6, row - 7);
        var gateLamp = new GridPoint(px - 4, row - 7);
        var inputA = new GridPoint(px - 6, row - 8);
        var inputB = new GridPoint(px - 7, row - 7);
        game.World.SetTile(gateCell, TileId.GateAnd);
        game.World.SetTile(gateLamp, TileId.DeskLamp);
        game.World.SetTile(inputA, TileId.Switch);
        game.World.SetTile(inputB, TileId.Switch);
        game.PlaceWire(inputA, 1);
        game.PlaceWire(gateCell, 1);
        game.PlaceWire(inputB, 2);
        game.PlaceWire(gateCell, 2);
        for (var x = gateCell.X; x <= gateLamp.X; x++)
            game.PlaceWire(new GridPoint(x, gateLamp.Y), 3);
        game.PulseWires(inputA);
        var oneInput = game.GetCellLight(gateLamp.X, gateLamp.Y) is not null;
        game.PulseWires(inputB);
        check(oneInput && game.GetCellLight(gateLamp.X, gateLamp.Y) is null,
            "an AND gate fires its output only when both inputs are on");

        // Hammer cycles block shapes.
        var shapeCell = new GridPoint(px + 1, groundRow);
        game.World.SetTile(shapeCell, TileId.Rock);
        Idle(10);
        Use(ItemId.ShapingHammer, shapeCell);
        var halved = game.World.GetCell(shapeCell).Shape == TileShape.Half;
        Idle(12);
        Use(ItemId.ShapingHammer, shapeCell);
        // Tool parity (docs/TOOLS_PARITY.md): with ground on both sides the first slope after half is the left-high one.
        check(halved && game.World.GetCell(shapeCell).Shape == TileShape.SlopeUpLeft,
            "the shaping hammer cycles a block through half and sloped shapes");

        // Ropes placed on a rope extend downward.
        var ropeTop = new GridPoint(px + 3, groundRow - 4);
        game.World.SetTile(new GridPoint(ropeTop.X, ropeTop.Y - 1), TileId.Rock);
        Idle(12);
        game.Player.Inventory.Add(ItemId.ClimbingRope, 5);
        Use(ItemId.ClimbingRope, ropeTop);
        Idle(8);
        Use(ItemId.ClimbingRope, ropeTop);
        check(game.World.TileAt(ropeTop.X, ropeTop.Y) == TileId.Rope && game.World.TileAt(ropeTop.X, ropeTop.Y + 1) == TileId.Rope,
            "using a rope on a rope extends it downward");

        // Wires survive a save round-trip.
        var json = new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        var wireCount = game.Wires.Count;
        var reloaded = GameSimulation.Restore(System.Text.Json.JsonSerializer.Deserialize<GameSnapshot>(
            System.Text.Json.JsonSerializer.Serialize(game.CaptureSnapshot(), json), json)!);
        check(wireCount > 10 && reloaded.Wires.Count == wireCount && reloaded.World.GetCell(blockCell).HasActuator &&
              reloaded.World.GetCell(shapeCell).Shape == TileShape.SlopeUpLeft,
            "wires, actuators and block shapes persist through a save");

        // Bombs: clear a crater but never bedrock or containers.
        var bx = 20;
        var by = game.World.Height - 3;
        for (var y = by - 3; y <= by + 1; y++)
        for (var x = bx - 4; x <= bx + 4; x++)
            game.World.SetTile(new GridPoint(x, y), TileId.Rock);
        game.World.SetTile(new GridPoint(bx + 2, by), TileId.CabinChest);
        game.World.SetTile(new GridPoint(bx, game.World.Height - 1), TileId.Bedrock);
        var broken = game.Explode(new Vector2(bx + 0.5f, by + 0.5f), 3.5f, 55, 0f);
        check(broken > 15 && game.World.TileAt(bx, by) == TileId.Air && game.World.TileAt(bx, game.World.Height - 1) == TileId.Bedrock &&
              game.World.TileAt(bx + 2, by) == TileId.CabinChest,
            "explosions clear tiles in a radius but spare bedrock and containers");
        game.Player.Inventory.Add(ItemId.Bomb, 1);
        Idle(25);
        Use(ItemId.Bomb, new GridPoint(px + 4, groundRow - 2));
        var thrown = game.Explosives.Count == 1;
        Idle(200);
        check(thrown && game.Explosives.Count == 0, "a thrown bomb flies, lands and detonates when its fuse runs out");

        // ---- Generation on a tiny detailed world: dungeon, cabins, giant trees, pots, flora ----
        var tiny = new GameSimulation(seed: 2718, width: 640, height: 300, blight: WorldBlight.Radiated);
        var features = tiny.Features!;
        int Count(TileId tile) => tiny.World.CountTiles(tile);
        check(features.Dungeon is not null && Count(TileId.DungeonBrickPrecursor) > 100 && Count(TileId.DungeonChest) >= 2 &&
              features.Cabins.Count >= 1 && Count(TileId.CabinChest) >= 1 && Count(TileId.PotDungeon) >= 1 &&
              Enumerable.Range((int)TileId.PotSurface, 7).Sum(id => Count((TileId)id)) >= 10 &&
              features.Trees >= 3 && features.Herbs >= 1 && Count(TileId.WeaponShrine) >= 1,
            "generation builds the precursor dungeon, buried cabins, pots, shrines, trees and herbs on a tiny world");

        // The Dungeon Guardian wakes when the dungeon is entered before the gate boss falls, and withdraws after.
        var dungeon = features.Dungeon!.Value;
        GridPoint? inside = null;
        for (var y = dungeon.Top; y <= dungeon.Bottom && inside is null; y++)
        for (var x = dungeon.Left; x <= dungeon.Right && inside is null; x++)
            if (tiny.World.GetCell(x, y).WallId == WallIds.Dungeon && tiny.World.TileAt(x, y) == TileId.Air &&
                tiny.World.TileAt(x, y - 1) == TileId.Air)
                inside = new GridPoint(x, y);
        var home = tiny.Player.Motion.Position;
        tiny.Player.Motion.Position = new Vector2(inside!.Value.X + 0.1f, inside.Value.Y - 0.8f);
        for (var i = 0; i < 31; i++)
            tiny.Tick(new GameInput(new MovementInput(0f)));
        var woke = tiny.Enemies.Any(enemy => enemy.Kind == EnemyKind.DungeonGuardian && enemy.IsAlive);
        tiny.Player.Motion.Position = home;
        for (var i = 0; i < 300; i++)
            tiny.Tick(new GameInput(new MovementInput(0f)));
        check(woke && tiny.Enemies.All(enemy => enemy.Kind != EnemyKind.DungeonGuardian),
            "the Dungeon Guardian guards the dungeon before the gate boss falls and withdraws when the player leaves");
    }
}
