using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Rimtera.Systems;
using Rimtera.World;

namespace Rimtera.UI;

public static partial class GraphicalSandbox
{
    /// <summary>
    /// The world's static layer cached in chunk images: cave backdrop, walls, blocks, plants, trees, ground debris
    /// and furniture props. A frame blits a dozen chunks instead of drawing every cell, and a chunk is redrawn only
    /// when a cell it can show changes (or after a bulk edit, a zoom change, a new world). Anything that moves or
    /// changes by itself (vines, blooming herbs, glide-walks, pylons, wiring panels, paint, cracks, liquids) is
    /// drawn live over the chunks every frame.
    /// </summary>
    private sealed partial class GameCanvas
    {
        private const int ChunkTiles = 24;

        /// <summary>How far a cell's drawing can reach past its own square (props stand two tiles tall, canopies spill).</summary>
        private const int ChunkMargin = 4;

        private sealed class TileChunk
        {
            public Bitmap? Image;
            public bool Dirty = true;
        }

        private readonly Dictionary<(int X, int Y), TileChunk> _chunks = new();
        private GameSimulation? _chunkGame;
        private int _chunkTileSize;
        private long _chunkRevision;
        private CellChangedHandler? _chunkListener;

        /// <summary>Tiles drawn every frame because they animate or carry state the chunks don't track.</summary>
        private bool IsLiveTile(TileId tile) =>
            tile == TileId.Vine || TilePhysics.IsHerb(tile) || TravelContent.IsPylon(tile) || TravelContent.BeltSpeed(tile) != 0f ||
            TilePhysics.IsWiringDevice(tile) && _sprites.Find(SpriteCatalog.ForTile(tile)) is null;

        /// <summary>Draws the cached static layer for the view (tile coordinates relative to <paramref name="startX"/>/<paramref name="startY"/>).</summary>
        private void DrawTileChunks(Graphics g, GameSimulation game, int startX, int startY, int viewW, int viewH)
        {
            EnsureChunkCache(game);
            var size = ChunkTiles * TileSize;
            var lastX = Math.Min(game.World.Width - 1, startX + viewW);
            var lastY = Math.Min(game.World.Height - 1, startY + viewH);
            for (var cy = startY / ChunkTiles; cy <= lastY / ChunkTiles; cy++)
            for (var cx = startX / ChunkTiles; cx <= lastX / ChunkTiles; cx++)
            {
                if (!_chunks.TryGetValue((cx, cy), out var chunk))
                    _chunks[(cx, cy)] = chunk = new TileChunk();
                if (chunk.Dirty || chunk.Image is null)
                    RenderChunk(game, chunk, cx, cy, size);
                g.DrawImageUnscaled(chunk.Image!, (cx * ChunkTiles - startX) * TileSize, (cy * ChunkTiles - startY) * TileSize);
            }
            // Keep memory bounded (a chunk is a few MB at high zoom): beyond 40, chunks more than one chunk outside the view
            // are dropped and rebuilt if the camera comes back.
            if (_chunks.Count > 40)
            {
                int minCx = startX / ChunkTiles - 1, maxCx = lastX / ChunkTiles + 1, minCy = startY / ChunkTiles - 1, maxCy = lastY / ChunkTiles + 1;
                foreach (var key in _chunks.Keys.Where(key => key.X < minCx || key.X > maxCx || key.Y < minCy || key.Y > maxCy).ToArray())
                {
                    _chunks[key].Image?.Dispose();
                    _chunks.Remove(key);
                }
            }
        }

        private void EnsureChunkCache(GameSimulation game)
        {
            if (!ReferenceEquals(game, _chunkGame) || _chunkTileSize != TileSize)
            {
                if (_chunkGame is not null && _chunkListener is not null)
                    _chunkGame.World.CellChanged -= _chunkListener;
                ClearChunks();
                _chunkGame = game;
                _chunkTileSize = TileSize;
                _chunkListener = (int x, int y, in WorldCell before, in WorldCell after) => OnChunkCellChanged(game, x, y, before, after);
                game.World.CellChanged += _chunkListener;
                _chunkRevision = game.World.TileRevision;
            }
            else if (game.World.TileRevision != _chunkRevision)
            {
                // The revision moved without a cell event: a bulk edit (world generation, loading). Redraw everything.
                foreach (var chunk in _chunks.Values)
                    chunk.Dirty = true;
                _chunkRevision = game.World.TileRevision;
            }
        }

        private void ClearChunks()
        {
            foreach (var chunk in _chunks.Values)
                chunk.Image?.Dispose();
            _chunks.Clear();
        }

        private void OnChunkCellChanged(GameSimulation game, int x, int y, in WorldCell before, in WorldCell after)
        {
            _chunkRevision = game.World.TileRevision;
            // Liquid and mining damage are drawn live; only a crossing of the "water above grass" line changes a fringe.
            var wetChanged = before.LiquidDepth < 0.05f != after.LiquidDepth < 0.05f;
            if (before.TileId == after.TileId && before.WallId == after.WallId && before.Flags == after.Flags && !wetChanged)
                return;
            MarkChunksDirty(x - ChunkMargin, y - ChunkMargin, x + ChunkMargin, y + ChunkMargin);
            // The cave backdrop starts below each column's first solid block, so solidity changes reach down the column.
            if (Solid(game, before) != Solid(game, after))
                MarkChunksDirty(x, y, x, game.World.Height - 1);
        }

        private static bool Solid(GameSimulation game, in WorldCell cell) =>
            cell.TileId != TileId.Air && game.Tiles.Contains(cell.TileId) && game.Tiles[cell.TileId].IsSolid;

        private void MarkChunksDirty(int minX, int minY, int maxX, int maxY)
        {
            for (var cy = Math.Max(0, minY) / ChunkTiles; cy <= Math.Max(0, maxY) / ChunkTiles; cy++)
            for (var cx = Math.Max(0, minX) / ChunkTiles; cx <= Math.Max(0, maxX) / ChunkTiles; cx++)
                if (_chunks.TryGetValue((cx, cy), out var chunk))
                    chunk.Dirty = true;
        }

        /// <summary>
        /// Draws one chunk in the same order the old per-frame loop used, over the chunk plus a margin so drawings that
        /// spill across a chunk edge (props, canopies, grass fringes) come out the same on both sides of it.
        /// </summary>
        private void RenderChunk(GameSimulation game, TileChunk chunk, int cx, int cy, int size)
        {
            if (chunk.Image is null || chunk.Image.Width != size)
            {
                chunk.Image?.Dispose();
                chunk.Image = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            }
            using var g = Graphics.FromImage(chunk.Image);
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            var x0 = cx * ChunkTiles;
            var y0 = cy * ChunkTiles;
            // Draw in world pixels; the chunk image is the window onto them.
            g.TranslateTransform(-x0 * TileSize, -y0 * TileSize);
            var world = game.World;
            var minX = Math.Max(0, x0 - ChunkMargin);
            var maxX = Math.Min(world.Width - 1, x0 + ChunkTiles + ChunkMargin - 1);
            var minY = Math.Max(0, y0 - ChunkMargin);
            var maxY = Math.Min(world.Height - 1, y0 + ChunkTiles + ChunkMargin - 1);

            // Cave backdrop below each column's surface so tunnels don't show sky.
            for (var x = x0; x < x0 + ChunkTiles && x < world.Width; x++)
            {
                var surface = 0;
                // Trees, plants, platforms and ropes are not ground: the backdrop starts at the first solid block.
                while (surface < world.Height && world.GetCell(new GridPoint(x, surface)) is var probe &&
                       (probe.TileId == TileId.Air || !game.Tiles.Contains(probe.TileId) || !game.Tiles[probe.TileId].IsSolid))
                    surface++;
                // A sky island is not the ground: below one, the open sky runs down to the terrain surface.
                var terrain = game.BiomeLayout.SurfaceAt(Math.Clamp(x, 0, world.Width - 1));
                if (surface < terrain - 12)
                    surface = terrain;
                var caveTop = Math.Max(y0, surface + 1);
                if (caveTop < y0 + ChunkTiles)
                    DrawDepthBackdrop(g, game, x, x * TileSize, caveTop, y0 + ChunkTiles, 0);
            }

            var stations = new List<(TileId Tile, int X, int Y)>();
            for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                var cell = world.GetCell(new GridPoint(x, y));
                var rect = new Rectangle(x * TileSize, y * TileSize, TileSize, TileSize);
                if (cell.WallId != 0 && (cell.TileId == TileId.Air || ShowsWallBehind(cell)))
                {
                    if (_sprites.GetScaled(SpriteCatalog.ForWall(cell.WallId), TileSize, TileSize) is { } wall)
                        g.DrawImageUnscaled(wall, rect.X, rect.Y);
                    g.FillRectangle(_wallShade, rect);
                }
                if (cell.TileId == TileId.Air || IsLiveTile(cell.TileId) || DrawSpecialTile(g, cell, x, y, rect, game))
                {
                }
                else if (PropFor(cell.TileId) is not null)
                    stations.Add((cell.TileId, rect.X, rect.Bottom));
                else
                    DrawShapedTile(g, cell, x, y, rect, game);
            }

            // Biome flora/debris on exposed surface tiles, deterministic per world position.
            for (var y = Math.Max(1, minY); y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                var ground = world.GetCell(new GridPoint(x, y));
                // Grass plants are real tiles now (drawn with the tiles); this layer keeps rocks and debris only.
                if (SpriteCatalog.DebrisFor(ground.TileId) is not { } options)
                    continue;
                var above = world.GetCell(new GridPoint(x, y - 1));
                if (above.TileId != TileId.Air || above.LiquidDepth > 0.05f)
                    continue;
                var hash = unchecked((uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)world.Width * 83492791u);
                hash ^= hash >> 13;
                hash *= 0x5bd1e995;
                if (hash % 100 >= 16)
                    continue;
                var sprite = options[(int)((hash >> 8) % (uint)options.Length)];
                var height = (int)(SpriteCatalog.DecorationHeight(sprite) * TileSize);
                DrawPropOnGround(g, sprite, x * TileSize + TileSize / 2f, y * TileSize, (int)(TileSize * 1.8f), height, flip: (hash & 1) == 0);
            }

            foreach (var (tile, x, bottom) in stations)
                DrawPropOnGround(g, PropFor(tile)!, x + TileSize / 2f, bottom, TileSize * 2, TileSize * 2);
            chunk.Dirty = false;
        }
    }
}
