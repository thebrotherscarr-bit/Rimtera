namespace Rimtera.World;

/// <summary>Project-owned 8-neighbor autotiling and 16x16 mask-atlas address mapping.</summary>
public sealed class TileFramingSystem
{
    public const int FrameWidth = 16;
    public const int FrameHeight = 16;

    private const byte North = 1 << 0;
    private const byte NorthEast = 1 << 1;
    private const byte East = 1 << 2;
    private const byte SouthEast = 1 << 3;
    private const byte South = 1 << 4;
    private const byte SouthWest = 1 << 5;
    private const byte West = 1 << 6;
    private const byte NorthWest = 1 << 7;

    /// <summary>
    /// Reframes every cell. Rows are processed in parallel; only frame masks change, so with no
    /// <see cref="TileGrid.CellChanged"/> listeners the masks are written directly (no events), otherwise
    /// changed cells go through <see cref="TileGrid.SetCell"/> as before.
    /// </summary>
    public int ReframeAll(TileGrid world, TileCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(catalog);
        var groups = FrameGroups(catalog);
        var cells = world.RawCells;
        var width = world.Width;
        var height = world.Height;
        var changed = 0;
        if (world.HasCellListeners)
        {
            for (var index = 0; index < cells.Length; index++)
            {
                var x = index % width;
                var y = index / width;
                var mask = ComputeMask(cells, groups, width, height, x, y);
                if (cells[index].FrameMask == mask)
                    continue;
                var cell = cells[index];
                cell.FrameMask = mask;
                world.SetCell(new GridPoint(x, y), cell);
                changed++;
            }
            return changed;
        }
        Parallel.For(0, (height + 63) / 64, band =>
        {
            var local = 0;
            for (var y = band * 64; y < Math.Min(height, band * 64 + 64); y++)
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                var mask = ComputeMask(cells, groups, width, height, x, y);
                if (cells[index].FrameMask == mask)
                    continue;
                cells[index].FrameMask = mask;
                local++;
            }
            Interlocked.Add(ref changed, local);
        });
        return changed;
    }

    private static ushort[] FrameGroups(TileCatalog catalog)
    {
        var groups = new ushort[ushort.MaxValue + 1];
        for (var id = 0; id < groups.Length; id++)
            if (catalog.Contains((TileId)id))
                groups[id] = catalog[(TileId)id].FrameGroup;
        return groups;
    }

    private static byte ComputeMask(WorldCell[] cells, ushort[] groups, int width, int height, int x, int y)
    {
        var group = groups[(int)cells[y * width + x].TileId];
        if (group == 0)
            return 0;
        bool Connects(int dx, int dy)
        {
            var nx = x + dx;
            var ny = y + dy;
            return (uint)nx < (uint)width && (uint)ny < (uint)height && groups[(int)cells[ny * width + nx].TileId] == group;
        }
        var north = Connects(0, -1);
        var east = Connects(1, 0);
        var south = Connects(0, 1);
        var west = Connects(-1, 0);
        byte mask = 0;
        if (north) mask |= North;
        if (north && east && Connects(1, -1)) mask |= NorthEast;
        if (east) mask |= East;
        if (south && east && Connects(1, 1)) mask |= SouthEast;
        if (south) mask |= South;
        if (south && west && Connects(-1, 1)) mask |= SouthWest;
        if (west) mask |= West;
        if (north && west && Connects(-1, -1)) mask |= NorthWest;
        return mask;
    }

    public int ReframeRegion(TileGrid world, TileCatalog catalog, IEnumerable<GridPoint> points)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(points);
        var changed = 0;
        foreach (var point in points.Distinct())
        {
            if (world.Contains(point) && ReframeCell(world, catalog, point))
                changed++;
        }
        return changed;
    }

    public static IReadOnlyList<GridPoint> GetDirtyRegion(TileGrid world, IEnumerable<GridPoint> changedCells)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(changedCells);
        var dirty = new HashSet<GridPoint>();
        foreach (var point in changedCells)
        {
            for (var y = point.Y - 1; y <= point.Y + 1; y++)
            for (var x = point.X - 1; x <= point.X + 1; x++)
            {
                var neighbor = new GridPoint(x, y);
                if (world.Contains(neighbor))
                    dirty.Add(neighbor);
            }
        }
        return dirty.OrderBy(point => point.Y).ThenBy(point => point.X).ToArray();
    }

    private static bool ReframeCell(TileGrid world, TileCatalog catalog, GridPoint point)
    {
        var cell = world.GetCell(point);
        var group = catalog[cell.TileId].FrameGroup;
        var mask = group == 0 ? (byte)0 : GetNeighborMask(world, catalog, point, group);
        if (cell.FrameMask == mask)
            return false;

        // Atlas coordinates (FrameX/FrameY) are derived from the mask.
        cell.FrameMask = mask;
        world.SetCell(point, cell);
        return true;
    }

    private static byte GetNeighborMask(TileGrid world, TileCatalog catalog, GridPoint center, ushort group)
    {
        var north = Connects(0, -1);
        var east = Connects(1, 0);
        var south = Connects(0, 1);
        var west = Connects(-1, 0);
        var northEast = north && east && Connects(1, -1);
        var southEast = south && east && Connects(1, 1);
        var southWest = south && west && Connects(-1, 1);
        var northWest = north && west && Connects(-1, -1);

        byte mask = 0;
        if (north) mask |= North;
        if (northEast) mask |= NorthEast;
        if (east) mask |= East;
        if (southEast) mask |= SouthEast;
        if (south) mask |= South;
        if (southWest) mask |= SouthWest;
        if (west) mask |= West;
        if (northWest) mask |= NorthWest;
        return mask;

        bool Connects(int dx, int dy)
        {
            var neighbor = new GridPoint(center.X + dx, center.Y + dy);
            return world.Contains(neighbor) && catalog[world.GetCell(neighbor).TileId].FrameGroup == group;
        }
    }
}
