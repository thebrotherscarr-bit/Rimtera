using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using Rimtera.Systems;
using Rimtera.World;

namespace Rimtera.UI;

/// <summary>
/// The player's remembered world map. A cell is revealed only when it is lit near the player
/// (see <see cref="RevealFromLight"/>); it then remembers what was seen there and how bright it
/// was, and does not change until it is lit again. Storage is 64×64 chunks allocated on first
/// reveal, so unexplored space costs nothing. Saved per character per world.
/// </summary>
internal sealed class WorldMapState
{
    public const int ChunkSize = 64;
    private const float RevealThreshold = 0.12f;
    private const int FormatVersion = 2;

    /// <summary>Kind of a remembered cell, packed in the top bits of the key.</summary>
    public enum CellKind : byte { Unrevealed, Tile, Wall, Water, Lava, Air }

    private readonly int _chunksX;
    private readonly int _chunksY;
    // Per chunk: key per cell (kind << 12 | id) and light per cell (0 = unrevealed).
    private readonly ushort[]?[] _keys;
    private readonly byte[]?[] _light;

    public WorldMapState(int width, int height)
    {
        Width = width;
        Height = height;
        _chunksX = (width + ChunkSize - 1) / ChunkSize;
        _chunksY = (height + ChunkSize - 1) / ChunkSize;
        _keys = new ushort[]?[_chunksX * _chunksY];
        _light = new byte[]?[_chunksX * _chunksY];
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>Bumped whenever cells change, so map images know to redraw.</summary>
    public int Revision { get; private set; }

    public GridPointF? DeathMarker { get; set; }
    public GridPointF? SpawnMarker { get; set; }
    public readonly record struct GridPointF(float X, float Y);

    public int RevealedChunkCount => _light.Count(chunk => chunk is not null);

    public bool IsRevealed(int x, int y) => Light(x, y) > 0;

    public byte Light(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return 0;
        var chunk = _light[ChunkIndex(x, y)];
        return chunk?[LocalIndex(x, y)] ?? 0;
    }

    public (CellKind Kind, int Id) Seen(int x, int y)
    {
        if (!IsRevealed(x, y))
            return (CellKind.Unrevealed, 0);
        var key = _keys[ChunkIndex(x, y)]![LocalIndex(x, y)];
        return ((CellKind)(key >> 12), key & 0xFFF);
    }

    /// <summary>
    /// Records every cell in the lit window whose light passes the threshold. A darker sighting
    /// keeps the brighter remembered light; what is there now always replaces what was seen.
    /// </summary>
    public void RevealFromLight(TileGrid world, LightMap light, int startX, int startY, int width, int height)
    {
        var changed = false;
        var x0 = Math.Max(0, startX);
        var y0 = Math.Max(0, startY);
        var x1 = Math.Min(Width, startX + width);
        var y1 = Math.Min(Height, startY + height);
        for (var y = y0; y < y1; y++)
        for (var x = x0; x < x1; x++)
        {
            var brightness = light.BrightnessAt(x, y);
            if (brightness < RevealThreshold)
                continue;
            changed |= Record(world, x, y, (byte)Math.Clamp(brightness * 255f, 1f, 255f));
        }
        if (changed)
            Revision++;
    }

    /// <summary>Reveals a disc regardless of light (landing site, respawn).</summary>
    public void RevealArea(TileGrid world, int centerX, int centerY, int radius, byte light = 200)
    {
        for (var y = centerY - radius; y <= centerY + radius; y++)
        for (var x = centerX - radius; x <= centerX + radius; x++)
            if ((x - centerX) * (x - centerX) + (y - centerY) * (y - centerY) <= radius * radius && world.Contains(new GridPoint(x, y)))
                Record(world, x, y, light);
        Revision++;
    }

    private bool Record(TileGrid world, int x, int y, byte light)
    {
        var index = ChunkIndex(x, y);
        var keys = _keys[index] ??= new ushort[ChunkSize * ChunkSize];
        var lights = _light[index] ??= new byte[ChunkSize * ChunkSize];
        var local = LocalIndex(x, y);
        var cell = world.GetCell(new GridPoint(x, y));
        var key = cell.TileId != TileId.Air ? Key(CellKind.Tile, (int)cell.TileId)
            : cell.LiquidDepth > 0.3f ? Key(cell.LiquidKind == LiquidKind.Lava ? CellKind.Lava : CellKind.Water, (int)cell.LiquidKind)
            : cell.WallId != 0 ? Key(CellKind.Wall, cell.WallId)
            : Key(CellKind.Air, 0);
        var newLight = Math.Max(lights[local], light);
        if (keys[local] == key && lights[local] == newLight)
            return false;
        keys[local] = key;
        lights[local] = newLight;
        return true;
    }

    private static ushort Key(CellKind kind, int id) => (ushort)((int)kind << 12 | (id & 0xFFF));

    private int ChunkIndex(int x, int y) => y / ChunkSize * _chunksX + x / ChunkSize;

    private static int LocalIndex(int x, int y) => y % ChunkSize * ChunkSize + x % ChunkSize;

    /// <summary>
    /// Renders a region of the map into a bitmap, one pixel per <paramref name="step"/> tiles.
    /// <paramref name="colorOf"/> maps a remembered cell to a base colour; light shades it.
    /// </summary>
    public void Render(Bitmap target, int originX, int originY, float step, Func<CellKind, int, int, Color> colorOf)
    {
        var data = target.LockBits(new Rectangle(0, 0, target.Width, target.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride / 4;
            var pixels = new int[stride * target.Height];
            var black = Color.FromArgb(255, 8, 9, 13).ToArgb();
            for (var py = 0; py < target.Height; py++)
            for (var px = 0; px < target.Width; px++)
            {
                var x = originX + (int)(px * step);
                var y = originY + (int)(py * step);
                var light = Light(x, y);
                if (light == 0)
                {
                    pixels[py * stride + px] = black;
                    continue;
                }
                var (kind, id) = Seen(x, y);
                var color = colorOf(kind, id, y);
                // Remembered light dims the colour, never below a readable floor.
                var shade = 0.35f + 0.65f * (light / 255f);
                pixels[py * stride + px] = Color.FromArgb(255, (int)(color.R * shade), (int)(color.G * shade), (int)(color.B * shade)).ToArgb();
            }
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            target.UnlockBits(data);
        }
    }

    private static readonly object WriteGate = new();

    /// <summary>Writes the map now (atomic, keeping the previous file as <c>.rmap.bak</c>).</summary>
    public void Save(string path) => CaptureForSave()(path);

    /// <summary>
    /// Freezes what a save writes (markers and the list of revealed chunks) so the file can be written on a worker while play
    /// goes on (PU-12). Cells revealed later inside an already-listed chunk may or may not make it into that file.
    /// </summary>
    public Action<string> CaptureForSave()
    {
        var death = DeathMarker;
        var spawn = SpawnMarker;
        var chunks = new List<(int Index, byte[] Light, ushort[] Keys)>();
        for (var index = 0; index < _light.Length; index++)
            if (_light[index] is { } lights && _keys[index] is { } keys)
                chunks.Add((index, lights, keys));
        var (width, height) = (Width, Height);
        return path =>
        {
            lock (WriteGate)
                SafeFile.Write(path, file =>
                {
                    using var deflate = new DeflateStream(file, CompressionLevel.Fastest);
                    using var writer = new BinaryWriter(deflate);
                    writer.Write(FormatVersion);
                    writer.Write(width);
                    writer.Write(height);
                    writer.Write(death is not null);
                    writer.Write(death?.X ?? 0f);
                    writer.Write(death?.Y ?? 0f);
                    writer.Write(spawn is not null);
                    writer.Write(spawn?.X ?? 0f);
                    writer.Write(spawn?.Y ?? 0f);
                    writer.Write(chunks.Count);
                    foreach (var (index, lights, keys) in chunks)
                    {
                        writer.Write(index);
                        writer.Write(lights);
                        foreach (var key in keys)
                            writer.Write(key);
                    }
                });
        };
    }

    /// <summary>
    /// Loads a saved map, or returns a fresh one if the file is missing, damaged or for a different size. A damaged file falls
    /// back to its backup (PU-11).
    /// </summary>
    public static WorldMapState Load(string path, int width, int height) =>
        SafeFile.ReadWithBackup(path, file => TryRead(file, width, height), out _) ?? new WorldMapState(width, height);

    private static WorldMapState? TryRead(string path, int width, int height)
    {
        var map = new WorldMapState(width, height);
        using var file = File.OpenRead(path);
        using var deflate = new DeflateStream(file, CompressionMode.Decompress);
        using var reader = new BinaryReader(deflate);
        if (reader.ReadInt32() != FormatVersion || reader.ReadInt32() != width || reader.ReadInt32() != height)
            return null;
        var hasMarker = reader.ReadBoolean();
        var markerX = reader.ReadSingle();
        var markerY = reader.ReadSingle();
        if (hasMarker)
            map.DeathMarker = new GridPointF(markerX, markerY);
        var hasSpawn = reader.ReadBoolean();
        var spawnX = reader.ReadSingle();
        var spawnY = reader.ReadSingle();
        if (hasSpawn)
            map.SpawnMarker = new GridPointF(spawnX, spawnY);
        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            var index = reader.ReadInt32();
            if ((uint)index >= (uint)map._light.Length)
                return null;
            var lights = reader.ReadBytes(ChunkSize * ChunkSize);
            if (lights.Length != ChunkSize * ChunkSize)
                return null;
            map._light[index] = lights;
            var keys = new ushort[ChunkSize * ChunkSize];
            for (var k = 0; k < keys.Length; k++)
                keys[k] = reader.ReadUInt16();
            map._keys[index] = keys;
        }
        map.Revision++;
        return map;
    }
}
