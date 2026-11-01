namespace Rimtera.World;

/// <summary>Derelict transit walkways in the cavern layer (travel pass): short level tunnels floored with a running glide-walk.</summary>
public sealed partial class LargeWorldGenerator
{
    private void TransitWalkways(TileGrid grid, TileCatalog catalog, Random random, CancellationToken token)
    {
        // Own random stream (seeded from the terrain) so earlier passes' draws, and everything after, stay unchanged.
        if (_width < 1000 || _surface.Length == 0)
            return;
        random = new Random(unchecked(_surface.Aggregate(17, (hash, row) => hash * 31 + row) ^ _width * 7919));
        var lines = Math.Clamp(_width / 900, 2, 9);
        var laid = 0;
        for (var attempt = 0; attempt < lines * 40 && laid < lines; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var x = random.Next(40, Math.Max(41, _width - 120));
            if (x >= _moltenTop.Length || _cavernRow <= 0)
                continue;
            var y = RandomRow(random, _cavernRow + 4, _moltenTop[x] - 12);
            var length = random.Next(36, 90);
            if (TryLayWalkway(grid, catalog, random, x, y, length))
                laid++;
        }
    }

    /// <summary>Carves a level three-high tunnel and floors it with glide-walk running one way (express on long lines).</summary>
    private bool TryLayWalkway(TileGrid grid, TileCatalog catalog, Random random, int startX, int row, int length)
    {
        // Survey first: refuse lines through protected structures, liquids or the world edge, and lines whose roof holds
        // sand or another falling block (it would be left hanging over the tunnel).
        for (var i = 0; i < length; i++)
        {
            if (grid.Contains(startX + i, row - 4) && TilePhysics.Falls(grid.TileAt(startX + i, row - 4)))
                return false;
            for (var y = row - 3; y <= row + 1; y++)
            {
                var x = startX + i;
                if (!grid.Contains(x, y) || IsProtected(grid.TileAt(x, y)) || grid.GetCell(x, y).LiquidDepth > 0f)
                    return false;
            }
        }
        var rightward = random.Next(2) == 0;
        var tile = length >= 70
            ? rightward ? TileId.ExpressWalkRight : TileId.ExpressWalkLeft
            : rightward ? TileId.GlideWalkRight : TileId.GlideWalkLeft;
        for (var i = 0; i < length; i++)
        {
            var x = startX + i;
            for (var dy = -3; dy <= -1; dy++)
                grid.SetTile(new GridPoint(x, row + dy), TileId.Air);
            if (!catalog[grid.TileAt(x, row + 1)].IsSolid)
                grid.SetTile(new GridPoint(x, row + 1), TileId.Rock);
            grid.SetTile(new GridPoint(x, row), tile);
        }
        Features.AddSite("transit_walkway", startX, row - 3, startX + length - 1, row);
        Features.AddCount("walkway_cells", length);
        return true;
    }
}
