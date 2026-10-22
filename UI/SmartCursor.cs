using System.Numerics;
using Rimtera.Entities;
using Rimtera.Systems;
using Rimtera.World;

namespace Rimtera.UI;

/// <summary>
/// Smart cursor: per-tool targeting strategies that turn "the tile under the mouse" into the tile the player most likely
/// meant. Drills, cutters, demolishers, wire cutters and empty buckets follow the reference's per-tool steps
/// (<see cref="SmartToolTargeting"/>, docs/TOOLS_PARITY.md §1) inside the tool's reach box; walls go to the nearest
/// open wall spot; blocks fill the nearest valid empty spot; guns lock on to the creature nearest the cursor. While the use
/// button is held the cursor's offset from the body is locked so a tunnel keeps going the same way.
/// </summary>
internal sealed class SmartCursor
{
    public const float Reach = 5f;
    private const float LockOnRadius = 4.5f;
    private Vector2? _lockedOffset;

    public bool Enabled { get; set; }

    /// <summary>Result of a targeting pass: the tile to act on and, for lock-on, the creature.</summary>
    public readonly record struct Target(GridPoint Tile, EnemyAgent? Enemy, string Strategy);

    public void ReleaseLock() => _lockedOffset = null;

    public Target? Resolve(GameSimulation game, GridPoint cursor, bool useHeld)
    {
        if (!Enabled)
            return null;
        var item = game.Player.Inventory.SelectedItem;
        var center = game.Player.Motion.Position + game.Player.Motion.Size / 2f;
        var offset = new Vector2(cursor.X + 0.5f, cursor.Y + 0.5f) - center;
        if (useHeld)
            offset = _lockedOffset ??= offset;
        else
            _lockedOffset = null;
        var mouse = center + offset;

        if (item is not null && SmartToolTargeting.Find(game, item, mouse) is { } tool)
            return new Target(tool, null, item.UseKind switch
            {
                ItemUseKind.Pickaxe => "dig",
                ItemUseKind.Cutter => "fell",
                ItemUseKind.Demolisher => "hammer",
                _ => "tool"
            });
        return item?.UseKind switch
        {
            ItemUseKind.PlaceTile => FillSpot(game, center, cursor),
            ItemUseKind.PlaceWall => WallSpot(game, cursor),
            ItemUseKind.Ranged or ItemUseKind.Melee => LockOn(game, cursor),
            _ => null
        };
    }

    /// <summary>
    /// The nearest cell to the cursor in reach with no wall and no solid block that touches a block or wall on a side (and
    /// isn't an open doorway): where a wall can go next.
    /// </summary>
    private static Target? WallSpot(GameSimulation game, GridPoint cursor)
    {
        var box = game.ToolReachBox();
        Target? best = null;
        var bestDistance = float.MaxValue;
        for (var x = Math.Max(0, box.MinX); x <= Math.Min(game.World.Width - 1, box.MaxX); x++)
        for (var y = Math.Max(0, box.MinY); y <= Math.Min(game.World.Height - 1, box.MaxY); y++)
        {
            var cell = game.World.GetCell(x, y);
            if (cell.WallId != WallIds.None || cell.TileId != TileId.Air && game.Tiles[cell.TileId].IsSolid && !TilePhysics.IsPlatform(cell.TileId))
                continue;
            var touches = false;
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                if (!game.World.Contains(x + dx, y + dy))
                    continue;
                var near = game.World.GetCell(x + dx, y + dy);
                touches |= near.TileId != TileId.Air || near.WallId != WallIds.None;
            }
            if (!touches)
                continue;
            var distance = Vector2.DistanceSquared(new Vector2(x, y), new Vector2(cursor.X, cursor.Y));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = new Target(new GridPoint(x, y), null, "wall");
            }
        }
        return best;
    }

    /// <summary>Nearest empty cell to the cursor that touches solid ground and is clear of the player.</summary>
    private static Target? FillSpot(GameSimulation game, Vector2 center, GridPoint cursor)
    {
        Target? best = null;
        var bestDistance = float.MaxValue;
        var player = game.Player.Motion;
        ForEachInReach(game, center, point =>
        {
            var cell = game.World.GetCell(point);
            if (cell.TileId != TileId.Air || cell.LiquidDepth > 0.5f)
                return;
            if (point.X + 1 > player.Position.X && point.X < player.Position.X + player.Size.X &&
                point.Y + 1 > player.Position.Y && point.Y < player.Position.Y + player.Size.Y)
                return;
            if (!HasSolidNeighbour(game, point))
                return;
            var distance = Vector2.DistanceSquared(new Vector2(point.X, point.Y), new Vector2(cursor.X, cursor.Y));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = new Target(point, null, "place");
            }
        });
        return best;
    }

    /// <summary>The living creature closest to the cursor, if one is near it.</summary>
    private static Target? LockOn(GameSimulation game, GridPoint cursor)
    {
        var cursorCenter = new Vector2(cursor.X + 0.5f, cursor.Y + 0.5f);
        EnemyAgent? best = null;
        var bestDistance = LockOnRadius;
        foreach (var enemy in game.Enemies)
        {
            if (!enemy.IsAlive || enemy.IsPassive)
                continue;
            var distance = Vector2.Distance(enemy.Motion.Position + enemy.Motion.Size / 2f, cursorCenter);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = enemy;
            }
        }
        if (best is null)
            return null;
        var center = best.Motion.Position + best.Motion.Size / 2f;
        return new Target(new GridPoint((int)center.X, (int)center.Y), best, "lock");
    }

    /// <summary>Visits every cell of the held item's reach box (the same box the simulation checks).</summary>
    private static void ForEachInReach(GameSimulation game, Vector2 center, Action<GridPoint> visit)
    {
        var box = game.ToolReachBox();
        for (var y = box.MinY; y <= box.MaxY; y++)
        for (var x = box.MinX; x <= box.MaxX; x++)
        {
            var point = new GridPoint(x, y);
            if (game.World.Contains(point))
                visit(point);
        }
    }

    private static bool HasSolidNeighbour(GameSimulation game, GridPoint point)
    {
        foreach (var (dx, dy) in new[] { (0, 1), (1, 0), (-1, 0), (0, -1) })
        {
            var neighbour = new GridPoint(point.X + dx, point.Y + dy);
            if (!game.World.Contains(neighbour))
                continue;
            var tile = game.World.GetCell(neighbour).TileId;
            if (tile != TileId.Air && game.Tiles.Contains(tile) && game.Tiles[tile].IsSolid)
                return true;
        }
        return false;
    }

    /// <summary>Valuable tiles: anything named as an ore or crystal (covers tiles added later).</summary>
    public static bool IsShiny(TileId tile)
    {
        var name = tile.ToString();
        return name.EndsWith("Ore", StringComparison.Ordinal) || name.Contains("Crystal", StringComparison.Ordinal) ||
               name.Contains("LifeCore", StringComparison.Ordinal);
    }
}
