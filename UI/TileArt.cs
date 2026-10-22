using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Rimtera.World;

namespace Rimtera.UI;

/// <summary>
/// Shaped tile bitmaps built from each tile's texture so terrain reads as natural ground rather than a grid:
/// <list type="bullet">
/// <item>Exposed edges: sides open to air get a dark outline and the top gets a light rim (light from above).</item>
/// <item>Rounded ground: natural tiles chamfer a corner where both of its sides are exposed.</item>
/// <item>Material transitions: a softer material (soil, sand, snow, ash) bleeds into a harder neighbour (rock)
///   along a jagged edge, varied per cell, so layers merge the way dirt merges into stone.</item>
/// <item>Grass fringe: blades over exposed grassy tops.</item>
/// </list>
/// Everything is cached per tile type, size and variant, so drawing a frame only picks bitmaps.
/// </summary>
internal sealed class TileArt
{
    public const int North = 1, East = 2, South = 4, West = 8;

    private readonly SpriteAtlas _atlas;
    private readonly Dictionary<(TileId, int, int), Bitmap?> _bodies = new();
    private readonly Dictionary<(TileId, int, int, int), Bitmap?> _bleeds = new();
    private readonly Dictionary<(TileId, int, int), Bitmap?> _fringes = new();
    private readonly Dictionary<TileId, int> _softness = new();
    private int _size;

    public TileArt(SpriteAtlas atlas) => _atlas = atlas;

    /// <summary>Drops cached bitmaps when the tile size (zoom) changes.</summary>
    public void UseSize(int size)
    {
        if (size == _size)
            return;
        foreach (var bitmap in _bodies.Values.Concat(_bleeds.Values).Concat(_fringes.Values))
            bitmap?.Dispose();
        _bodies.Clear();
        _bleeds.Clear();
        _fringes.Clear();
        _size = size;
    }

    /// <summary>
    /// How soft a natural material is: 3 loose ground (soil, sand, snow, ash, mud, moss), 2 grassy tops,
    /// 1 rock and ore, 0 built or special blocks (bricks, plates, crystals, devices), which never blend.
    /// </summary>
    public int Softness(TileId tile)
    {
        if (_softness.TryGetValue(tile, out var cached))
            return cached;
        var name = tile.ToString();
        bool Has(params string[] words) => words.Any(word => name.Contains(word, StringComparison.Ordinal));
        var value = Has("Brick", "Plank", "Plate", "Hull", "Panel", "Glass", "Platform", "Door", "Chest", "Lamp", "Torch",
                        "Trunk", "Canopy", "Leaf", "Vault", "Dungeon", "Block", "Pipe", "Wire", "Switch", "Crystal", "Core", "Heart")
            ? 0
            : Has("Grass") ? 2
            : Has("Soil", "Dirt", "Mud", "Sand", "Snow", "Ash", "Silt", "Clay", "Moss", "Slush", "Gravel", "Loam", "Regolith", "Dune") ? 3
            : Has("Rock", "Stone", "Slate", "Basalt", "Granite", "Marble", "Alabaster", "Ore", "Ice", "Shale", "Crust", "Obsidian", "Bedrock") ? 1
            : 0;
        _softness[tile] = value;
        return value;
    }

    public static bool IsGrassy(TileId tile) => tile.ToString().Contains("Grass", StringComparison.Ordinal);

    /// <summary>The tile's texture with outlines on exposed sides, a top rim and (natural tiles) chamfered corners.</summary>
    public Bitmap? Body(TileId tile, string? sprite, int exposed)
    {
        var key = (tile, _size, exposed);
        if (_bodies.TryGetValue(key, out var cached))
            return cached;
        Bitmap? result = null;
        if (_atlas.GetScaled(sprite, _size, _size) is { } source)
        {
            result = Copy(source);
            Edit(result, pixels => ShapeBody(pixels, _size, exposed, Softness(tile) > 0));
        }
        _bodies[key] = result;
        return result;
    }

    /// <summary>
    /// A softer neighbour's texture reaching into this cell from <paramref name="side"/> (North/East/South/West)
    /// along a jagged edge; <paramref name="variant"/> (0–3) picks the edge shape.
    /// </summary>
    public Bitmap? Bleed(TileId neighbour, string? sprite, int side, int variant)
    {
        var key = (neighbour, _size, side, variant);
        if (_bleeds.TryGetValue(key, out var cached))
            return cached;
        Bitmap? result = null;
        if (_atlas.GetScaled(sprite, _size, _size) is { } source)
        {
            result = Copy(source);
            Edit(result, pixels => MaskBleed(pixels, _size, side, variant));
        }
        _bleeds[key] = result;
        return result;
    }

    /// <summary>Grass blades drawn above an exposed grassy top (a strip a third of a tile high).</summary>
    public Bitmap? Fringe(TileId tile, string? sprite, int variant)
    {
        var key = (tile, _size, variant);
        if (_fringes.TryGetValue(key, out var cached))
            return cached;
        Bitmap? result = null;
        if (_atlas.GetScaled(sprite, _size, _size) is { } source)
        {
            var height = Math.Max(3, _size / 3);
            result = new Bitmap(_size, height, PixelFormat.Format32bppArgb);
            // Blade colours come from the top rows of the tile, where the grass is.
            var colours = new List<Color>();
            for (var x = 0; x < _size; x++)
            for (var y = 0; y < Math.Max(1, _size / 6); y++)
            {
                var c = source.GetPixel(x, y);
                if (c.A > 200)
                    colours.Add(c);
            }
            if (colours.Count > 0)
            {
                var random = new Random(((int)tile * 31 + variant) * 7919);
                var x = random.Next(0, 2);
                while (x < _size)
                {
                    var blade = random.Next(1, height + 1);
                    var colour = colours[random.Next(colours.Count)];
                    for (var y = height - blade; y < height; y++)
                        result.SetPixel(x, y, y == height - blade ? Shade(colour, 1.18f) : colour);
                    x += random.Next(1, 4);
                }
            }
        }
        _fringes[key] = result;
        return result;
    }

    private static void ShapeBody(int[] pixels, int size, int exposed, bool natural)
    {
        var rim = Math.Max(1, size / 16);
        var chamfer = natural ? Math.Max(2, size / 4) : 0;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var index = y * size + x;
            var argb = pixels[index];
            if (((uint)argb >> 24) == 0)
                continue;
            // Chamfer corners where both adjacent sides face air.
            if (chamfer > 0)
            {
                var cut = (exposed & (North | West)) == (North | West) && x + y < chamfer
                       || (exposed & (North | East)) == (North | East) && (size - 1 - x) + y < chamfer
                       || (exposed & (South | West)) == (South | West) && x + (size - 1 - y) < chamfer
                       || (exposed & (South | East)) == (South | East) && (size - 1 - x) + (size - 1 - y) < chamfer;
                if (cut)
                {
                    pixels[index] = 0;
                    continue;
                }
                var onDiagonal = (exposed & (North | West)) == (North | West) && x + y == chamfer
                              || (exposed & (North | East)) == (North | East) && (size - 1 - x) + y == chamfer
                              || (exposed & (South | West)) == (South | West) && x + (size - 1 - y) == chamfer
                              || (exposed & (South | East)) == (South | East) && (size - 1 - x) + (size - 1 - y) == chamfer;
                if (onDiagonal)
                {
                    pixels[index] = Shade(argb, 0.45f);
                    continue;
                }
            }
            var edge = (exposed & North) != 0 && y < rim || (exposed & South) != 0 && y >= size - rim
                    || (exposed & West) != 0 && x < rim || (exposed & East) != 0 && x >= size - rim;
            if (edge)
                pixels[index] = Shade(argb, 0.45f);
            else if ((exposed & North) != 0 && y < rim * 2)
                pixels[index] = Shade(argb, 1.22f);
            else if ((exposed & South) != 0 && y >= size - rim * 2)
                pixels[index] = Shade(argb, 0.8f);
        }
    }

    private static void MaskBleed(int[] pixels, int size, int side, int variant)
    {
        // Depth of the bleed along the edge: a base plus smoothed per-variant noise, so neighbouring cells
        // don't repeat the same outline.
        var random = new Random(variant * 7907 + side * 104729);
        var depth = new float[size];
        var a = random.NextSingle() * MathF.Tau;
        var b = random.NextSingle() * MathF.Tau;
        for (var i = 0; i < size; i++)
        {
            var t = i / (float)size;
            depth[i] = size * (0.2f + 0.1f * MathF.Sin(t * MathF.Tau * 1.5f + a) + 0.07f * MathF.Sin(t * MathF.Tau * 3.7f + b));
        }
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var (along, inward) = side switch
            {
                North => (x, y),
                South => (x, size - 1 - y),
                West => (y, x),
                _ => (y, size - 1 - x)
            };
            var index = y * size + x;
            if (inward > depth[along])
                pixels[index] = 0;
            else if (inward > depth[along] - 1.2f)
                pixels[index] = Shade(pixels[index], 0.7f);
        }
    }

    private static Bitmap Copy(Bitmap source)
    {
        var copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(copy);
        g.DrawImageUnscaled(source, 0, 0);
        return copy;
    }

    private static void Edit(Bitmap bitmap, Action<int[]> edit)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new int[bitmap.Width * bitmap.Height];
            for (var row = 0; row < bitmap.Height; row++)
                Marshal.Copy(data.Scan0 + row * data.Stride, pixels, row * bitmap.Width, bitmap.Width);
            edit(pixels);
            for (var row = 0; row < bitmap.Height; row++)
                Marshal.Copy(pixels, row * bitmap.Width, data.Scan0 + row * data.Stride, bitmap.Width);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static int Shade(int argb, float factor)
    {
        var a = (argb >> 24) & 0xFF;
        int C(int shift) => Math.Clamp((int)(((argb >> shift) & 0xFF) * factor), 0, 255);
        return (a << 24) | (C(16) << 16) | (C(8) << 8) | C(0);
    }

    private static Color Shade(Color colour, float factor) =>
        Color.FromArgb(colour.A, Math.Clamp((int)(colour.R * factor), 0, 255), Math.Clamp((int)(colour.G * factor), 0, 255),
            Math.Clamp((int)(colour.B * factor), 0, 255));
}
