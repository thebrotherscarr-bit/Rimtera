using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Mechanics-parity probes for section B (mining, building and tiles) of docs/MECHANICS_PARITY.md.
/// Each probe builds a small deterministic arena, drives ticks and inputs, and checks the behaviour.
/// </summary>
public static class ParityBuildTests
{
    private const int Left = 10, Top = 4, Right = 60, Floor = 30;

    private static GameSimulation NewArena(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 80, height: 48) { FamilySpawnsEnabled = false };
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e6f);
        for (var y = Top; y <= Floor + 1; y++)
        for (var x = Left; x <= Right; x++)
        {
            var point = new GridPoint(x, y);
            game.World.SetTile(point, y >= Floor ? TileId.Rock : TileId.Air);
            var cell = game.World.GetCell(point);
            cell.LiquidDepth = 0f;
            cell.LiquidKind = LiquidKind.None;
            cell.WallId = WallIds.None;
            cell.Flags = 0;
            game.World.SetCell(point, cell);
        }
        PutPlayer(game, 30f);
        return game;
    }

    /// <summary>Stands the player on the floor with their left edge at <paramref name="x"/>.</summary>
    private static void PutPlayer(GameSimulation game, float x)
    {
        game.Player.Motion.Position = new Vector2(x, Floor - game.Player.Motion.Size.Y - 0.01f);
        game.Player.Motion.Velocity = Vector2.Zero;
        Idle(game, 2);
    }

    private static int PlayerColumn(GameSimulation game) => (int)MathF.Floor(game.Player.Motion.Position.X + game.Player.Motion.Size.X / 2f);

    private static void Idle(GameSimulation game, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            game.Tick(new GameInput(new MovementInput(0f)));
    }

    private static void Select(GameSimulation game, ItemId item)
    {
        if (game.Player.Inventory.Count(item) == 0)
            game.Player.Inventory.Add(item, 1);
        game.TrySelectInventorySlot(game.Player.Inventory.FindSlot(item));
    }

    /// <summary>Holds the action on a target for <paramref name="ticks"/> ticks (auto-swing: the input repeats every tick).</summary>
    private static void Hold(GameSimulation game, PlayerAction action, GridPoint target, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            game.Tick(new GameInput(new MovementInput(0f), action, target));
    }

    private static void Once(GameSimulation game, PlayerAction action, GridPoint target)
    {
        while (game.Player.ActionCooldownTicks > 0)
            Idle(game, 1);
        game.Tick(new GameInput(new MovementInput(0f), action, target));
    }

    private static void MineUntilBroken(GameSimulation game, GridPoint target, int maxTicks = 1200)
    {
        for (var i = 0; i < maxTicks && game.World.TileAt(target.X, target.Y) != TileId.Air; i++)
            game.Tick(new GameInput(new MovementInput(0f), PlayerAction.Mine, target));
    }

    private static void SetWall(GameSimulation game, int x, int y, ushort wall) => game.World.SetWall(new GridPoint(x, y), wall);

    public static void Run(Action<bool, string> check)
    {
        B1PickPower(check);
        B2MiningDamage(check);
        B3ToolUse(check);
        B4GroundDrops(check);
        B5HammersAndAxes(check);
        B6PlacingBlocks(check);
        B7PlacingWalls(check);
        B8Furniture(check);
        B9Containers(check);
        B10Buckets(check);
        B11LooseBlocks(check);
        B12Torches(check);
    }

    // B1: blocks have a pick-power requirement; a weaker drill does nothing and says so.
    private static void B1PickPower(Action<bool, string> check)
    {
        var game = NewArena(7101);
        var target = new GridPoint(PlayerColumn(game) + 2, Floor - 1);
        game.World.SetTile(target, TileId.CobaltOre);
        Select(game, ItemId.WoodenPickaxe);
        Once(game, PlayerAction.Mine, target);
        var refused = game.World.GetCell(target) is { TileId: TileId.CobaltOre, Damage: 0 } &&
                      game.LastMessage.Contains("stronger drill", StringComparison.Ordinal);
        Select(game, ItemId.CopperPickaxe);
        Once(game, PlayerAction.Mine, target);
        check(refused && game.Tiles[TileId.CobaltOre].MinimumToolPower > game.Items[ItemId.WoodenPickaxe].ToolPower &&
              game.World.GetCell(target).Damage > 0,
            "parity B1: a drill below a block's pick power does no damage and asks for a stronger drill");
    }

    // B2: hits accumulate, stronger tools/softer blocks take more per hit, and partial damage stays until later hits age it.
    private static void B2MiningDamage(Action<bool, string> check)
    {
        var game = NewArena(7102);
        var column = PlayerColumn(game);
        var rock = new GridPoint(column + 2, Floor - 1);
        var soil = new GridPoint(column - 2, Floor - 1);
        game.World.SetTile(rock, TileId.Alabaster);
        game.World.SetTile(soil, TileId.Alabaster);
        Select(game, ItemId.WoodenPickaxe);
        Once(game, PlayerAction.Mine, rock);
        var weak = game.World.GetCell(rock).Damage;
        Once(game, PlayerAction.Mine, rock);
        var accumulated = game.World.GetCell(rock).Damage;
        Select(game, ItemId.CopperPickaxe);
        Once(game, PlayerAction.Mine, soil);
        var strong = game.World.GetCell(soil).Damage;
        var stillThere = game.World.TileAt(rock.X, rock.Y) == TileId.Alabaster;
        // Tool parity (docs/TOOLS_PARITY.md): partial damage does not heal with time; it mends only as later hits age it
        // (ToolParitySelfTests covers the mending).
        Idle(game, 400);
        check(weak > 0 && accumulated > weak && strong > weak && stillThere && game.World.GetCell(rock).Damage == accumulated &&
              game.World.GetCell(soil).Damage == strong,
            "parity B2: hits accumulate damage from tool power vs hardness, and untouched partial damage stays put");
    }

    // B3: tools repeat at their use speed while held; reach is a fixed range; out of range does nothing.
    private static void B3ToolUse(Action<bool, string> check)
    {
        var game = NewArena(7103);
        var column = PlayerColumn(game);
        var target = new GridPoint(column + 2, Floor - 1);
        game.World.SetTile(target, TileId.Alabaster);
        Select(game, ItemId.WoodenPickaxe);
        var cooldown = game.Items[ItemId.WoodenPickaxe].UseCooldownTicks;
        var hits = 0;
        var last = game.World.GetCell(target).Damage;
        for (var i = 0; i < cooldown * 3; i++)
        {
            game.Tick(new GameInput(new MovementInput(0f), PlayerAction.Mine, target));
            var now = game.World.GetCell(target).Damage;
            if (now != last || game.World.TileAt(target.X, target.Y) == TileId.Air)
                hits++;
            last = now;
            if (game.World.TileAt(target.X, target.Y) == TileId.Air)
                break;
        }
        var far = new GridPoint(column + 9, Floor - 1);
        game.World.SetTile(far, TileId.Soil);
        Hold(game, PlayerAction.Mine, far, cooldown * 2);
        check(cooldown > 1 && hits >= 2 && hits <= 4 && game.World.GetCell(far) is { TileId: TileId.Soil, Damage: 0 } &&
              game.LastMessage.Contains("out of reach", StringComparison.Ordinal),
            "parity B3: a held drill auto-swings once per use time; targets beyond reach are untouched");
    }

    // B4: broken blocks pop out as ground items and are collected by walking near them.
    private static void B4GroundDrops(Action<bool, string> check)
    {
        var game = NewArena(7104);
        var column = PlayerColumn(game);
        var near = new GridPoint(column + 1, Floor - 1);
        game.World.SetTile(near, TileId.Soil);
        Select(game, ItemId.CopperPickaxe);
        var before = game.Player.Inventory.Count(ItemId.SoilBlock);
        MineUntilBroken(game, near);
        var dropped = game.GroundItems.FirstOrDefault(item => item.ItemId == ItemId.SoilBlock);
        var notInstant = game.Player.Inventory.Count(ItemId.SoilBlock) == before && dropped is not null && dropped.Velocity.Y < 0f;
        Idle(game, 60);
        var magnet = game.Player.Inventory.Count(ItemId.SoilBlock) == before + 1 && game.GroundItems.All(item => item.ItemId != ItemId.SoilBlock);

        var far = new GridPoint(column + 4, Floor - 1);
        game.World.SetTile(far, TileId.Soil);
        MineUntilBroken(game, far);
        Idle(game, 60);
        var waits = game.GroundItems.Any(item => item.ItemId == ItemId.SoilBlock) &&
                    game.Player.Inventory.Count(ItemId.SoilBlock) == before + 1;
        PutPlayer(game, far.X - 0.4f);
        Idle(game, 60);
        check(notInstant && magnet && waits && game.Player.Inventory.Count(ItemId.SoilBlock) == before + 2,
            "parity B4: mined blocks pop out as ground items and are picked up by walking near them");
    }

    // B5: hammers knock out exposed walls and shape blocks; cutters fell whole trees; drills can't cut trunks.
    private static void B5HammersAndAxes(Action<bool, string> check)
    {
        var game = NewArena(7105);
        var column = PlayerColumn(game);
        // Tool parity (docs/TOOLS_PARITY.md): only natural walls need an open edge (any of the eight cells around them);
        // walls the player built can be knocked out from anywhere and drop their item.
        for (var y = Floor - 5; y <= Floor - 3; y++)
        for (var x = column + 1; x <= column + 3; x++)
            SetWall(game, x, y, WallIds.Rock);
        var inner = new GridPoint(column + 2, Floor - 4);
        var edge = new GridPoint(column + 3, Floor - 4);
        Select(game, ItemId.FerriteDemolisher);
        Once(game, PlayerAction.UseSelected, inner);
        var innerKept = game.World.GetCell(inner).WallId == WallIds.Rock;
        Once(game, PlayerAction.UseSelected, edge);
        var edgeDented = game.World.GetCell(edge).WallId == WallIds.Rock; // demolisher power 45: (int)(45 x 1.5) = 67 of 100
        Once(game, PlayerAction.UseSelected, edge);
        var edgeGone = game.World.GetCell(edge).WallId == WallIds.None;
        Once(game, PlayerAction.UseSelected, inner);
        Once(game, PlayerAction.UseSelected, inner);
        var innerNowExposed = game.World.GetCell(inner).WallId == WallIds.None;
        for (var y = Floor - 5; y <= Floor - 3; y++)
        for (var x = column - 3; x <= column - 1; x++)
            SetWall(game, x, y, WallIds.Plank);
        var builtInner = new GridPoint(column - 2, Floor - 4);
        Once(game, PlayerAction.UseSelected, builtInner);
        Once(game, PlayerAction.UseSelected, builtInner);
        edgeGone &= game.World.GetCell(builtInner).WallId == WallIds.None && game.GroundItems.Any(item => item.ItemId == ItemId.PlankWall);
        var block = new GridPoint(column - 2, Floor - 1);
        game.World.SetTile(block, TileId.Rock);
        Once(game, PlayerAction.UseSelected, block);
        var shaped = game.World.GetCell(block).Shape != TileShape.Full;
        check(innerKept && edgeDented && edgeGone && innerNowExposed && shaped,
            "parity B5: hammers remove only walls with an exposed side (hits add up to 100) and cycle block shapes");

        var treeX = column + 3;
        // Flora parity: trees need tree ground beside the sapling too, and grow on 1 random update in 20.
        for (var gx = treeX - 1; gx <= treeX + 1; gx++)
            game.World.SetTile(new GridPoint(gx, Floor), TileId.Grass);
        game.World.SetTile(new GridPoint(treeX, Floor - 1), TileId.Sapling);
        for (var i = 0; i < 1200 && game.World.TileAt(treeX, Floor - 1) == TileId.Sapling; i++)
            game.Ticker.UpdateCell(treeX, Floor - 1);
        var trunk = new GridPoint(treeX, Floor - 1);
        var grew = TilePhysics.IsTrunk(game.World.TileAt(trunk.X, trunk.Y));
        Select(game, ItemId.CopperPickaxe);
        Hold(game, PlayerAction.Mine, trunk, 60);
        var drillRefused = TilePhysics.IsTrunk(game.World.TileAt(trunk.X, trunk.Y)) && game.World.GetCell(trunk).Damage == 0;
        Select(game, ItemId.FerriteCutter);
        // A chop adds (int)(power / 5 x 1.2) toward 100, so hold long enough for every chop the trunk needs.
        var cutter = game.Items[ItemId.FerriteCutter];
        var chops = (CombatTuning.TileBreakDamage + CombatTuning.CutterDamage(cutter.ToolPower) - 1) / Math.Max(1, CombatTuning.CutterDamage(cutter.ToolPower));
        Hold(game, PlayerAction.UseSelected, trunk, (chops + 2) * (cutter.UseCooldownTicks + 2));
        var parts = 0;
        for (var y = Top; y < Floor; y++)
        for (var x = treeX - 3; x <= treeX + 3; x++)
            if (TilePhysics.IsTreePart(game.World.TileAt(x, y)))
                parts++;
        var timberOnGround = game.GroundItems.Any(item => item.ItemId == ItemId.Timber) ||
                             game.Player.Inventory.Count(ItemId.Timber) > 0;
        // Flora parity: saplings are a 1-in-2 roll per leafy top and branch, so one fell may drop none (not asserted here).
        check(grew && drillRefused && parts == 0 && timberOnGround,
            "parity B5: drills can't cut trunks; a cutter fells the whole tree above for timber and saplings");
    }

    // B6: blocks need a neighbour or wall, can't overlap actors, and swap onto a different block.
    private static void B6PlacingBlocks(Action<bool, string> check)
    {
        var game = NewArena(7106);
        var column = PlayerColumn(game);
        Select(game, ItemId.SoilBlock);
        game.Player.Inventory.Add(ItemId.SoilBlock, 20);
        var floating = new GridPoint(column + 2, Floor - 4);
        Once(game, PlayerAction.UseSelected, floating);
        var floatRefused = game.World.TileAt(floating.X, floating.Y) == TileId.Air;
        var supported = new GridPoint(column + 2, Floor - 1);
        Once(game, PlayerAction.UseSelected, supported);
        var placedNext = game.World.TileAt(supported.X, supported.Y) == TileId.Soil;
        SetWall(game, floating.X, floating.Y, WallIds.Plank);
        Once(game, PlayerAction.UseSelected, floating);
        var onWall = game.World.TileAt(floating.X, floating.Y) == TileId.Soil;

        var critterCell = new GridPoint(column - 3, Floor - 1);
        var critter = game.SpawnCreature(EnemyKind.GelHopper, new Vector2(critterCell.X + 0.1f, Floor - 0.9f));
        critter.Motion.Velocity = Vector2.Zero;
        game.Tick(new GameInput(new MovementInput(0f), PlayerAction.UseSelected,
            RegionMap.CellAt(critter.Motion.Position + critter.Motion.Size / 2f)));
        var creatureCell = RegionMap.CellAt(critter.Motion.Position + critter.Motion.Size / 2f);
        var creatureBlocked = !game.Tiles[game.World.TileAt(creatureCell.X, creatureCell.Y)].IsSolid;
        critter.TakeDamage(1e6f);
        Idle(game, 2);
        var self = new GridPoint(column, Floor - 1);
        Once(game, PlayerAction.UseSelected, self);
        var selfBlocked = game.World.TileAt(self.X, self.Y) == TileId.Air;

        var swap = new GridPoint(column - 2, Floor - 1);
        game.World.SetTile(swap, TileId.Rock);
        var rockBefore = game.Player.Inventory.Count(ItemId.RockBlock);
        Once(game, PlayerAction.UseSelected, swap);
        var swapped = game.World.TileAt(swap.X, swap.Y) == TileId.Soil;
        Idle(game, 60);
        check(floatRefused && placedNext && onWall && creatureBlocked && selfBlocked && swapped &&
              game.Player.Inventory.Count(ItemId.RockBlock) == rockBefore + 1,
            "parity B6: blocks need a neighbour or wall, never overlap actors, and swap a different block (old one drops)");
    }

    // B7: walls need an adjacent wall or block.
    private static void B7PlacingWalls(Action<bool, string> check)
    {
        var game = NewArena(7107);
        var column = PlayerColumn(game);
        Select(game, ItemId.PlankWall);
        game.Player.Inventory.Add(ItemId.PlankWall, 5);
        var floating = new GridPoint(column + 2, Floor - 4);
        Once(game, PlayerAction.UseSelected, floating);
        var refused = game.World.GetCell(floating).WallId == WallIds.None;
        var grounded = new GridPoint(column + 2, Floor - 1);
        Once(game, PlayerAction.UseSelected, grounded);
        var placed = game.World.GetCell(grounded).WallId == WallIds.Plank;
        var chained = new GridPoint(column + 2, Floor - 2);
        Once(game, PlayerAction.UseSelected, chained);
        check(refused && placed && game.World.GetCell(chained).WallId == WallIds.Plank,
            "parity B7: walls need an adjacent wall or block");
    }

    // B8: furniture needs support; doors need three free cells; losing support drops the furniture.
    private static void B8Furniture(Action<bool, string> check)
    {
        var game = NewArena(7108);
        var column = PlayerColumn(game);
        Select(game, ItemId.PlankChair);
        game.Player.Inventory.Add(ItemId.PlankChair, 2);
        var midAir = new GridPoint(column + 2, Floor - 4);
        Once(game, PlayerAction.UseSelected, midAir);
        var airRefused = game.World.TileAt(midAir.X, midAir.Y) == TileId.Air;
        var shelf = new GridPoint(column + 3, Floor - 3);
        game.World.SetTile(shelf, TileId.Rock);
        var seat = new GridPoint(shelf.X, shelf.Y - 1);
        Once(game, PlayerAction.UseSelected, seat);
        var placed = game.World.TileAt(seat.X, seat.Y) == TileId.PlankChair;
        game.World.SetTile(shelf, TileId.Air);
        Idle(game, 3);
        var broke = game.World.TileAt(seat.X, seat.Y) == TileId.Air && game.GroundItems.Any(item => item.ItemId == ItemId.PlankChair);

        Select(game, ItemId.PlankDoor);
        game.Player.Inventory.Add(ItemId.PlankDoor, 2);
        var doorCell = new GridPoint(column - 3, Floor - 1);
        game.World.SetTile(new GridPoint(doorCell.X, Floor - 3), TileId.Rock);
        Once(game, PlayerAction.UseSelected, doorCell);
        var cramped = game.World.TileAt(doorCell.X, doorCell.Y) == TileId.Air;
        game.World.SetTile(new GridPoint(doorCell.X, Floor - 3), TileId.Air);
        Once(game, PlayerAction.UseSelected, doorCell);
        var door = Enumerable.Range(Floor - 3, 3).All(y => game.World.TileAt(doorCell.X, y) == TileId.PlankDoor);
        check(airRefused && placed && broke && cramped && door,
            "parity B8: furniture needs support (and drops when it loses it); doors need three free cells");
    }

    // B9: containers holding items can't be mined; opening shows contents; reach is checked.
    private static void B9Containers(Action<bool, string> check)
    {
        var game = NewArena(7109);
        var column = PlayerColumn(game);
        var crateCell = new GridPoint(column + 2, Floor - 1);
        game.World.SetTile(crateCell, TileId.StorageCrate);
        var crate = game.GetContainer(crateCell)!;
        crate.Add(ItemId.Berry, 3);
        Select(game, ItemId.CopperPickaxe);
        Hold(game, PlayerAction.Mine, crateCell, 120);
        var guarded = game.World.TileAt(crateCell.X, crateCell.Y) == TileId.StorageCrate && game.World.GetCell(crateCell).Damage == 0;
        var opened = game.Interact(crateCell);
        var shows = opened.Kind == WorldInteractionKind.ContainerOpened && opened.Container is { } inside && inside.Count(ItemId.Berry) == 3;
        var inReach = game.ContainerInReach(crateCell);
        PutPlayer(game, crateCell.X + 12f);
        var walkedAway = !game.ContainerInReach(crateCell);
        PutPlayer(game, column - 0.4f);
        crate.TryConsume(ItemId.Berry, 3);
        MineUntilBroken(game, crateCell);
        check(guarded && shows && inReach && walkedAway && game.World.TileAt(crateCell.X, crateCell.Y) == TileId.Air,
            "parity B9: full containers can't be mined, opening shows contents, and the open container is range-checked");
    }

    // B10: an empty bucket scoops a full cell, a full bucket pours one, and the liquid then flows.
    private static void B10Buckets(Action<bool, string> check)
    {
        var game = NewArena(7110);
        var column = PlayerColumn(game);
        var pool = new GridPoint(column + 3, Floor - 1);
        var cell = game.World.GetCell(pool);
        cell.LiquidKind = LiquidKind.Water;
        cell.LiquidDepth = 1f;
        game.World.SetCell(pool, cell);
        Select(game, ItemId.EmptyBucket);
        Once(game, PlayerAction.UseSelected, pool);
        var scooped = game.Player.Inventory.Count(ItemId.BucketWater) == 1 && game.World.GetCell(pool).LiquidDepth < 0.01f;
        Select(game, ItemId.BucketWater);
        var pour = new GridPoint(column + 2, Floor - 3);
        Once(game, PlayerAction.UseSelected, pour);
        var poured = game.Player.Inventory.Count(ItemId.EmptyBucket) >= 1 && game.Player.Inventory.Count(ItemId.BucketWater) == 0;
        Idle(game, 240);
        var floorWater = 0f;
        for (var x = Left; x <= Right; x++)
            floorWater += game.World.GetCell(new GridPoint(x, Floor - 1)).LiquidDepth;
        check(scooped && poured && game.World.GetCell(pour).LiquidDepth < 0.05f && floorWater > 0.9f,
            "parity B10: buckets scoop and pour one cell of liquid, which then flows down and settles");
    }

    // B11: loose blocks fall when unsupported, land as blocks, and hurt creatures they land on.
    private static void B11LooseBlocks(Action<bool, string> check)
    {
        var game = NewArena(7111);
        var column = PlayerColumn(game);
        var x = column + 4;
        var prop = new GridPoint(x, Floor - 6);
        var sand = new GridPoint(x, Floor - 7);
        game.World.SetTile(prop, TileId.Rock);
        game.World.SetTile(sand, TileId.Sand);
        game.World.SetTile(prop, TileId.Air);
        Idle(game, 90);
        var landed = game.World.TileAt(x, Floor - 1) == TileId.Sand && game.World.TileAt(sand.X, sand.Y) == TileId.Air;

        var hx = column - 5;
        var victim = game.SpawnCreature(EnemyKind.GelHopper, new Vector2(hx + 0.05f, Floor - 0.95f));
        victim.Motion.Velocity = Vector2.Zero;
        var hp = victim.HitPoints;
        game.World.SetTile(new GridPoint(hx, Top + 2), TileId.Rock);
        game.World.SetTile(new GridPoint(hx, Top + 1), TileId.Sand);
        game.World.SetTile(new GridPoint(hx, Top + 2), TileId.Air);
        var hurt = false;
        for (var i = 0; i < 120 && !hurt; i++)
        {
            victim.Motion.Position = new Vector2(hx + 0.05f, Floor - victim.Motion.Size.Y - 0.01f);
            victim.Motion.Velocity = Vector2.Zero;
            Idle(game, 1);
            hurt = victim.HitPoints < hp;
        }
        check(landed && hurt, "parity B11: unsupported sand falls, lands as a block, and hurts creatures it lands on");
    }

    // B12: torches mount on walls or blocks, give light, and are knocked out by liquid.
    private static void B12Torches(Action<bool, string> check)
    {
        var game = NewArena(7112);
        var column = PlayerColumn(game);
        Select(game, ItemId.WallTorch);
        game.Player.Inventory.Add(ItemId.WallTorch, 5);
        var midAir = new GridPoint(column + 2, Floor - 4);
        Once(game, PlayerAction.UseSelected, midAir);
        var refused = game.World.TileAt(midAir.X, midAir.Y) == TileId.Air;
        SetWall(game, midAir.X, midAir.Y, WallIds.Plank);
        Once(game, PlayerAction.UseSelected, midAir);
        var onWall = game.World.TileAt(midAir.X, midAir.Y) == TileId.WallTorch;
        var side = new GridPoint(column + 3, Floor - 1);
        game.World.SetTile(new GridPoint(column + 4, Floor - 1), TileId.Rock);
        Once(game, PlayerAction.UseSelected, side);
        var onBlock = game.World.TileAt(side.X, side.Y) == TileId.WallTorch;
        var lit = game.GetCellLight(side.X, side.Y) is not null;
        game.World.TryAddLiquid(side, LiquidKind.Water, 1f, game.Tiles);
        Idle(game, 5);
        check(refused && onWall && onBlock && lit && game.World.TileAt(side.X, side.Y) == TileId.Air,
            "parity B12: torches mount on walls or blocks, give light, and liquid knocks them out");
    }
}
