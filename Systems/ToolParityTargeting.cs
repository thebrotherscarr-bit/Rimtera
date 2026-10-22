using System.Numerics;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Smart-cursor targeting for tools, following the reference's per-tool steps (docs/TOOLS_PARITY.md §1):
/// drills first take a breakable non-solid thing right under the cursor, then a life core anywhere in reach, then the
/// first solid block hit by lines cast from the body's leading edge toward the cursor (nearest the body wins); cutters
/// take the foot of the trunk, cactus or log nearest the cursor; demolishers take the first exposed wall along those lines
/// (nearest the body), else any exposed wall in reach nearest the cursor. Wire cutters and empty buckets take the nearest
/// wire or scoopable liquid to the cursor. Every candidate lies inside the tool's reach box.
/// </summary>
public static class SmartToolTargeting
{
    /// <summary>
    /// The cell the smart cursor picks for <paramref name="item"/>, or null to fall back to the cell under the cursor.
    /// <paramref name="mouse"/> is the cursor position in cell units (cell centre = x + 0.5).
    /// </summary>
    public static GridPoint? Find(GameSimulation game, ItemDefinition? item, Vector2 mouse)
    {
        if (item is null)
            return null;
        var box = game.ToolReachBox(item);
        return item.UseKind switch
        {
            ItemUseKind.Pickaxe => ForceToBreakable(game, item, mouse, box) ?? LifeCores(game, mouse, box) ?? FirstSolidAlongLines(game, mouse, box),
            ItemUseKind.Cutter => TrunkFoot(game, mouse, box) ?? ForceToBreakable(game, item, mouse, box),
            ItemUseKind.Demolisher => ForceToBreakable(game, item, mouse, box) ?? WallAlongLines(game, mouse, box) ?? NearestWall(game, mouse, box),
            _ when item.Id == ItemId.WireCutter => NearestWire(game, mouse, box),
            _ when item.Id == ItemId.EmptyBucket => NearestScoop(game, mouse, box),
            _ => null
        };
    }

    // ------------------------------------------------------------------ steps

    /// <summary>
    /// A breakable, non-solid tile (or platform) right under the cursor that this tool may hit: cutter tiles need a cutter,
    /// hearts and altars a demolisher, anything else a drill.
    /// </summary>
    private static GridPoint? ForceToBreakable(GameSimulation game, ItemDefinition item, Vector2 mouse, (int, int, int, int) box)
    {
        var cell = new GridPoint((int)MathF.Floor(mouse.X), (int)MathF.Floor(mouse.Y));
        if (!game.World.Contains(cell) || !ToolParity.InBox(box, cell.X, cell.Y))
            return null;
        var tile = game.World.TileAt(cell.X, cell.Y);
        if (tile is TileId.Air or TileId.Bedrock || !ToolParity.CanBreakUnder(game.World, cell))
            return null;
        if (game.Tiles[tile].IsSolid && !TilePhysics.IsPlatform(tile))
            return null;
        var allowed = ToolParity.IsCutterTile(tile) ? item.UseKind == ItemUseKind.Cutter
            : ToolParity.IsDemolisherTile(tile) ? item.UseKind == ItemUseKind.Demolisher
            : item.UseKind == ItemUseKind.Pickaxe;
        return allowed ? cell : null;
    }

    /// <summary>Life cores anywhere in reach, nearest the cursor (the reference's only pick priority tiles are life crystals).</summary>
    private static GridPoint? LifeCores(GameSimulation game, Vector2 mouse, (int MinX, int MinY, int MaxX, int MaxY) box) =>
        Nearest(Cells(game, box).Where(point => game.World.TileAt(point.X, point.Y) == TileId.LifeCore), mouse);

    /// <summary>
    /// Lines from the body's leading edge (one per body row or column) to a body-sized box at the cursor; each line's
    /// first solid, breakable block is a candidate and the one nearest the body centre wins.
    /// </summary>
    private static GridPoint? FirstSolidAlongLines(GameSimulation game, Vector2 mouse, (int, int, int, int) box)
    {
        var candidates = new List<GridPoint>();
        foreach (var (start, end) in Lines(game, mouse))
        {
            if (Walk(start, end, point => ToolParity.IsSolidBlock(game.World, game.Tiles, point.X, point.Y) &&
                                          game.World.TileAt(point.X, point.Y) != TileId.Bedrock) is { } hit &&
                ToolParity.CanBreakUnder(game.World, hit))
                candidates.Add(hit);
        }
        var center = BodyCenter(game);
        var best = candidates.Count == 0 ? (GridPoint?)null : candidates.MinBy(point => Vector2.Distance(Centre(point), center));
        return best is { } chosen && ToolParity.InBox(box, chosen.X, chosen.Y) ? chosen : null;
    }

    /// <summary>
    /// Every cutter tile in reach, moved to the foot of its column (trunk, cactus or log); the foot nearest the cursor wins.
    /// </summary>
    private static GridPoint? TrunkFoot(GameSimulation game, Vector2 mouse, (int, int, int, int) box)
    {
        var feet = new HashSet<GridPoint>();
        foreach (var point in Cells(game, box))
        {
            var tile = game.World.TileAt(point.X, point.Y);
            if (!ToolParity.IsCutterTile(tile) || TilePhysics.IsCanopy(tile))
                continue;
            var foot = point;
            while (game.World.Contains(foot.X, foot.Y + 1) && game.World.TileAt(foot.X, foot.Y + 1) == tile &&
                   ToolParity.InBox(box, foot.X, foot.Y + 1))
                foot = new GridPoint(foot.X, foot.Y + 1);
            feet.Add(foot);
        }
        return Nearest(feet, mouse);
    }

    /// <summary>The first wall a demolisher can hit along the body-to-cursor lines, nearest the body.</summary>
    private static GridPoint? WallAlongLines(GameSimulation game, Vector2 mouse, (int, int, int, int) box)
    {
        var candidates = new List<GridPoint>();
        foreach (var (start, end) in Lines(game, mouse))
        {
            if (Walk(start, end, point => game.World.GetCell(point).WallId != WallIds.None, includeStart: true) is { } hit &&
                ToolParity.IsWallTarget(game.World, game.Tiles, hit) && !ToolParity.IsDemolisherTile(game.World.TileAt(hit.X, hit.Y)))
                candidates.Add(hit);
        }
        var center = BodyCenter(game);
        var best = candidates.Count == 0 ? (GridPoint?)null : candidates.MinBy(point => Vector2.Distance(Centre(point), center));
        return best is { } chosen && ToolParity.InBox(box, chosen.X, chosen.Y) ? chosen : null;
    }

    private static GridPoint? NearestWall(GameSimulation game, Vector2 mouse, (int, int, int, int) box) =>
        Nearest(Cells(game, box).Where(point => ToolParity.IsWallTarget(game.World, game.Tiles, point) &&
                                                !ToolParity.IsDemolisherTile(game.World.TileAt(point.X, point.Y))), mouse);

    private static GridPoint? NearestWire(GameSimulation game, Vector2 mouse, (int, int, int, int) box) =>
        Nearest(Cells(game, box).Where(point => game.Wires.LayersAt(point) != 0 || game.World.GetCell(point).HasActuator), mouse);

    private static GridPoint? NearestScoop(GameSimulation game, Vector2 mouse, (int, int, int, int) box) =>
        Nearest(Cells(game, box).Where(point => GameSimulation.ScoopableUnits(game.World, point) > GameSimulation.ScoopThresholdUnits), mouse);

    // ------------------------------------------------------------------ geometry

    private static Vector2 BodyCenter(GameSimulation game) => game.Player.Motion.Position + game.Player.Motion.Size / 2f;

    private static Vector2 Centre(GridPoint point) => new(point.X + 0.5f, point.Y + 0.5f);

    private static GridPoint? Nearest(IEnumerable<GridPoint> points, Vector2 mouse)
    {
        GridPoint? best = null;
        var bestDistance = float.MaxValue;
        foreach (var point in points)
        {
            var distance = Vector2.Distance(Centre(point), mouse);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = point;
            }
        }
        return best;
    }

    private static IEnumerable<GridPoint> Cells(GameSimulation game, (int MinX, int MinY, int MaxX, int MaxY) box)
    {
        for (var x = Math.Max(0, box.MinX); x <= Math.Min(game.World.Width - 1, box.MaxX); x++)
        for (var y = Math.Max(0, box.MinY); y <= Math.Min(game.World.Height - 1, box.MaxY); y++)
            yield return new GridPoint(x, y);
    }

    /// <summary>
    /// The reference's line set: the direction to the cursor snaps to straight sideways or straight up/down when one
    /// axis is three times the other. Sideways lines start at each body row on the leading edge; vertical lines at each
    /// body column on the head (or feet) row; each ends at the matching cell of a body-sized box at the cursor.
    /// </summary>
    private static List<(GridPoint Start, GridPoint End)> Lines(GameSimulation game, Vector2 mouse)
    {
        const float Pixel = 1f / 16f;
        var motion = game.Player.Motion;
        var position = motion.Position;
        var width = motion.Size.X;
        var height = motion.Size.Y;
        var center = BodyCenter(game);
        var delta = mouse - center;
        var sx = Math.Sign(delta.X);
        var sy = Math.Sign(delta.Y);
        if (MathF.Abs(delta.X) > MathF.Abs(delta.Y) * 3f)
        {
            sy = 0;
            mouse.Y = center.Y;
        }
        if (MathF.Abs(delta.Y) > MathF.Abs(delta.X) * 3f)
        {
            sx = 0;
            mouse.X = center.X;
        }
        var stack = sy == -1 && sx != 0 ? -1 : 1;
        var columns = (int)MathF.Ceiling(width - 0.001f);
        var rows = (int)MathF.Ceiling(height - 0.001f);

        var px = (int)MathF.Floor(position.X + width / 2f + (width / 2f - Pixel) * sx);
        var py = stack == -1 ? (int)MathF.Floor(position.Y + height - Pixel) : (int)MathF.Floor(position.Y + 0.1f * Pixel);
        var ex = (int)MathF.Floor(mouse.X + (width / 2f - Pixel) * sx);
        var ey = stack == -1
            ? (int)MathF.Floor(mouse.Y + height / 2f - Pixel)
            : (int)MathF.Floor(mouse.Y + 0.1f * Pixel - (height / 2f + Pixel));
        if (sx == 0)
            ey++;
        ey = Math.Clamp(ey, 0, game.World.Height - 1);

        var starts = new List<GridPoint>();
        var ends = new List<GridPoint>();
        if (sx != 0)
            for (var i = 0; i < rows; i++)
            {
                starts.Add(new GridPoint(px, py + i * stack));
                ends.Add(new GridPoint(ex, ey + i * stack));
            }
        if (sy != 0)
            for (var i = 0; i < columns; i++)
            {
                starts.Add(new GridPoint((int)MathF.Floor(position.X) + i, py));
                ends.Add(new GridPoint((int)MathF.Floor(mouse.X - width / 2f) + i, ey));
            }
        return starts.Zip(ends).Where(pair => game.World.Contains(pair.First) && game.World.Contains(pair.Second)).ToList();
    }

    /// <summary>Steps cell by cell from <paramref name="start"/> to <paramref name="end"/> and returns the first cell that matches.</summary>
    private static GridPoint? Walk(GridPoint start, GridPoint end, Func<GridPoint, bool> match, bool includeStart = false)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
        GridPoint? previous = null;
        for (var i = includeStart ? 0 : 1; i <= steps; i++)
        {
            var t = steps == 0 ? 0f : i / (float)steps;
            var point = new GridPoint((int)MathF.Round(start.X + dx * t), (int)MathF.Round(start.Y + dy * t));
            if (previous == point)
                continue;
            previous = point;
            if (match(point))
                return point;
        }
        return null;
    }
}
