using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using Rimtera.World;

namespace Rimtera.UI;

/// <summary>
/// Renderer-side tile lighting. Seeds light from open sky, glowing tiles and liquids, and
/// actor lamps, then spreads it with four directional sweeps whose falloff depends on what
/// the light passes through (air, liquid, solid). Standard and Low draw it as a darkness layer (one shade for all three
/// channels); High multiplies the scene by the light per channel, so a blue lamp tints the stone blue.
/// Structure follows the scan → spread → present pattern; all rates and colours are Rimtera's.
/// </summary>
internal sealed class LightMap
{
    public const int Padding = 10;
    private const float AirFalloff = 0.90f;
    private const float LiquidFalloff = 0.82f;
    private const float SolidFalloff = 0.62f;
    /// <summary>Sub-pixels per tile edge in the presented layers; smoothing is done here, not by GDI+.</summary>
    private int _subdivision = 4;

    /// <summary>1 gives blocky per-tile light; 4 gives smooth gradients.</summary>
    public int Subdivision
    {
        get => _subdivision;
        set
        {
            var clamped = Math.Clamp(value, 1, 4);
            if (clamped == _subdivision)
                return;
            _subdivision = clamped;
            _width = 0; // force the layers to be reallocated at the new resolution
        }
    }

    /// <summary>
    /// High lighting: the light is a multiply layer, each colour channel of the scene scaled by that channel of the light.
    /// Off (Standard, Low): a darkness layer set by the brightest channel, which is cheaper and colour-blind.
    /// </summary>
    public bool ColouredGlow { get; set; } = true;

    /// <summary>
    /// Share of all light taken away (0 = none, 1 = black): the darkness debuff (GameSimulation.DarknessDimming) dims sky,
    /// lamps and glow alike. The renderer sets it before <see cref="Compute"/>.
    /// </summary>
    public float Dimming
    {
        get => _dimming;
        set => _dimming = float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;
    }

    private float _dimming;

    private float[] _r = [];
    private float[] _g = [];
    private float[] _b = [];
    private byte[] _medium = [];
    private int _width;
    private int _height;
    private Bitmap? _darkness;

    public int OriginX { get; private set; }
    public int OriginY { get; private set; }

    public readonly record struct LampLight(Vector2 Position, Color Color, float Strength);

    /// <summary>
    /// Recomputes light for the visible window plus padding. With <paramref name="present"/> off (lighting disabled) only
    /// the light values are kept, for the map's reveal, and the overlay images are not rebuilt.
    /// </summary>
    public void Compute(TileGrid world, TileCatalog tiles, int startX, int startY, int viewW, int viewH,
        float daylight, IEnumerable<LampLight> lamps, bool present = true)
    {
        OriginX = startX - Padding;
        OriginY = startY - Padding;
        Resize(viewW + Padding * 2, viewH + Padding * 2);
        Array.Clear(_r);
        Array.Clear(_g);
        Array.Clear(_b);

        // Sky light reaches straight down through air until the first solid tile, then spreads.
        var sky = new Vector3(daylight, daylight, MathF.Min(1f, daylight + 0.08f));
        for (var lx = 0; lx < _width; lx++)
        {
            var x = OriginX + lx;
            if (x < 0 || x >= world.Width)
                continue;
            var stop = Math.Min(SkyStop(world, tiles, x), OriginY + _height);
            for (var y = Math.Max(0, OriginY); y < stop; y++)
                if (world.GetCell(new GridPoint(x, y)).WallId == 0)
                    Seed(lx, y - OriginY, sky);
        }

        for (var ly = 0; ly < _height; ly++)
        for (var lx = 0; lx < _width; lx++)
        {
            var x = OriginX + lx;
            var y = OriginY + ly;
            var index = ly * _width + lx;
            if (x < 0 || y < 0 || x >= world.Width || y >= world.Height)
            {
                _medium[index] = 2;
                continue;
            }
            var cell = world.GetCell(new GridPoint(x, y));
            var solid = cell.TileId != TileId.Air && !cell.IsInactive && tiles.Contains(cell.TileId) && tiles[cell.TileId].IsSolid;
            _medium[index] = solid ? (byte)2 : cell.LiquidDepth > 0.3f ? (byte)1 : (byte)0;
            if (cell.LiquidKind == LiquidKind.Lava && cell.LiquidDepth > 0.05f)
                Seed(lx, ly, new Vector3(0.35f, 1f, 0.3f) * (0.6f + cell.LiquidDepth * 0.4f)); // liquid uranium glows green
            else if (cell.LiquidKind == LiquidKind.Phase && cell.LiquidDepth > 0.05f)
                Seed(lx, ly, new Vector3(0.75f, 0.45f, 1f) * 0.45f);
            else if (cell.LiquidKind == LiquidKind.Fuel && cell.LiquidDepth > 0.05f && y > 0 && world.GetCell(new GridPoint(x, y - 1)).LiquidUnits == 0)
                Seed(lx, ly, new Vector3(1f, 0.7f, 0.25f) * 0.18f); // faint amber shimmer on the fuel sea's surface
            if (Emission(cell) is { } glow)
                Seed(lx, ly, glow);
        }

        foreach (var lamp in lamps)
        {
            var lx = (int)lamp.Position.X - OriginX;
            var ly = (int)lamp.Position.Y - OriginY;
            if (lx >= 0 && ly >= 0 && lx < _width && ly < _height)
                Seed(lx, ly, new Vector3(lamp.Color.R / 255f, lamp.Color.G / 255f, lamp.Color.B / 255f) * lamp.Strength);
        }

        for (var pass = 0; pass < 2; pass++)
            Spread();
        if (_dimming > 0f)
        {
            var keep = 1f - _dimming;
            for (var i = 0; i < _r.Length; i++)
            {
                _r[i] *= keep;
                _g[i] *= keep;
                _b[i] *= keep;
            }
        }
        if (present)
            Present();
    }

    private int[] _skyStop = [];
    private long[] _skyStamp = [];

    /// <summary>
    /// The first solid row of a column, where sunlight stops. Cached per column against the world's revision, so a still
    /// world costs nothing; scanning from the sky down every frame cost tens of thousands of reads deep underground.
    /// </summary>
    private int SkyStop(TileGrid world, TileCatalog tiles, int x)
    {
        if (_skyStop.Length != world.Width)
        {
            _skyStop = new int[world.Width];
            _skyStamp = new long[world.Width];
            Array.Fill(_skyStamp, -1);
        }
        var revision = world.TileRevision;
        if (_skyStamp[x] == revision)
            return _skyStop[x];
        var y = 0;
        while (y < world.Height && world.GetCell(new GridPoint(x, y)) is var cell &&
               !(cell.TileId != TileId.Air && !cell.IsInactive && tiles.Contains(cell.TileId) && tiles[cell.TileId].IsSolid))
            y++;
        _skyStop[x] = y;
        _skyStamp[x] = revision;
        return y;
    }

    /// <summary>
    /// Draws the light over the world area: the darkness layer, or on High the per-channel multiply. The multiply reads
    /// the pixels already drawn, so it needs the surface: <paramref name="target"/> when <paramref name="g"/> draws into a
    /// bitmap (the world behind a menu), otherwise the graphics' device context (the window's back buffer).
    /// </summary>
    public void Draw(Graphics g, int screenX, int screenY, int tileSize, Bitmap? target = null)
    {
        if (ColouredGlow)
        {
            DrawMultiply(g, screenX, screenY, tileSize, target);
            return;
        }
        if (_darkness is null)
            return;
        var previous = g.InterpolationMode;
        var previousOffset = g.PixelOffsetMode;
        // The layer is already smoothed per sub-pixel, so plain block scaling avoids GDI+ filter seams.
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        var destination = new Rectangle(screenX - Padding * tileSize, screenY - Padding * tileSize, _width * tileSize, _height * tileSize);
        g.DrawImage(_darkness, destination);
        g.InterpolationMode = previous;
        g.PixelOffsetMode = previousOffset;
    }

    private int[] _screen = [];

    /// <summary>High: copies the drawn scene out, multiplies it by the tint layer and copies it back.</summary>
    private void DrawMultiply(Graphics g, int screenX, int screenY, int tileSize, Bitmap? target)
    {
        if (_tintWidth == 0)
            return;
        // Raw pixels ignore GDI+ transforms; the world only uses the camera's sub-tile translation, applied here by hand.
        float offsetX, offsetY;
        using (var transform = g.Transform)
        {
            offsetX = transform.OffsetX;
            offsetY = transform.OffsetY;
        }
        var layerX = screenX - Padding * tileSize + (int)MathF.Round(offsetX);
        var layerY = screenY - Padding * tileSize + (int)MathF.Round(offsetY);
        var clip = g.VisibleClipBounds;
        var region = Rectangle.Intersect(
            Rectangle.Round(new RectangleF(clip.X + offsetX, clip.Y + offsetY, clip.Width, clip.Height)),
            new Rectangle(layerX, layerY, _width * tileSize, _height * tileSize));
        if (target is not null)
            region.Intersect(new Rectangle(0, 0, target.Width, target.Height));
        if (region.Width <= 0 || region.Height <= 0)
            return;
        var count = region.Width * region.Height;
        if (_screen.Length < count)
            _screen = new int[count]; // kept between frames, like the darkness pixels
        var local = new Rectangle(0, 0, region.Width, region.Height);

        if (target is not null)
        {
            g.Flush(System.Drawing.Drawing2D.FlushIntention.Sync);
            var data = target.LockBits(region, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
            try
            {
                // Row by row: the locked stride may be wider than the region.
                for (var y = 0; y < region.Height; y++)
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, _screen, y * region.Width, region.Width);
                MultiplyInto(_screen, region.Width, local, layerX - region.X, layerY - region.Y, tileSize);
                for (var y = 0; y < region.Height; y++)
                    System.Runtime.InteropServices.Marshal.Copy(_screen, y * region.Width, data.Scan0 + y * data.Stride, region.Width);
            }
            finally
            {
                target.UnlockBits(data);
            }
            return;
        }

        var hdc = g.GetHdc(); // flushes GDI+'s pending drawing first
        try
        {
            if (!Gdi.Ensure(hdc, region.Width, region.Height))
                return;
            Gdi.BitBlt(Gdi.MemoryDc, 0, 0, region.Width, region.Height, hdc, region.X, region.Y, Gdi.SrcCopy);
            Gdi.GdiFlush();
            System.Runtime.InteropServices.Marshal.Copy(Gdi.Bits, _screen, 0, count);
            MultiplyInto(_screen, region.Width, local, layerX - region.X, layerY - region.Y, tileSize);
            System.Runtime.InteropServices.Marshal.Copy(_screen, 0, Gdi.Bits, count);
            Gdi.BitBlt(hdc, region.X, region.Y, region.Width, region.Height, Gdi.MemoryDc, 0, 0, Gdi.SrcCopy);
        }
        finally
        {
            g.ReleaseHdc(hdc);
        }
    }

    private int[] _columns = [];
    private int[] _factors = [];

    /// <summary>
    /// Multiplies a 32-bit pixel buffer (BGRA, premultiplied or opaque; <paramref name="stride"/> in pixels) by the High
    /// tint layer inside <paramref name="region"/>. The light layer's top-left corner sits at (<paramref name="layerX"/>,
    /// <paramref name="layerY"/>) in buffer pixels and a tile covers <paramref name="tileSize"/> pixels; tint sub-pixels
    /// are block-scaled, as the darkness layer is. SIMD on one thread: it is memory-bound, so more cores barely help.
    /// </summary>
    internal void MultiplyInto(int[] pixels, int stride, Rectangle region, int layerX, int layerY, int tileSize)
    {
        if (_tintWidth == 0 || region.Width <= 0 || region.Height <= 0 || tileSize <= 0)
            return;
        var sub = Subdivision;
        var width = region.Width;
        // The tint column under each pixel of the region.
        if (_columns.Length < width)
        {
            _columns = new int[width];
            _factors = new int[width];
        }
        for (var x = 0; x < width; x++)
            _columns[x] = Math.Min(Math.Max(0, region.X + x - layerX) * sub / tileSize, _tintWidth - 1);
        // One tint row widened to screen pixels, reused while the rows below it fall in the same tint row.
        var factors = _factors.AsSpan(0, width);
        var widened = -1;
        var mask = new Vector<int>(0xFF);
        var one = Vector<int>.One;
        var alpha = new Vector<int>(unchecked((int)0xFF000000));
        var lanes = Vector<int>.Count;
        for (var sy = region.Y; sy < region.Bottom; sy++)
        {
            var tintRow = Math.Min(Math.Max(0, sy - layerY) * sub / tileSize, _tintHeight - 1);
            if (tintRow != widened)
            {
                var source = _tint.AsSpan(tintRow * _tintWidth, _tintWidth);
                for (var x = 0; x < width; x++)
                    factors[x] = source[_columns[x]];
                widened = tintRow;
            }
            var row = pixels.AsSpan(sy * stride + region.X, width);
            var i = 0;
            for (; i <= width - lanes; i += lanes)
            {
                var p = new Vector<int>(row[i..]);
                var f = new Vector<int>(factors[i..]);
                var r = Vector.ShiftRightLogical((Vector.ShiftRightLogical(p, 16) & mask) * ((Vector.ShiftRightLogical(f, 16) & mask) + one), 8);
                var g = Vector.ShiftRightLogical((Vector.ShiftRightLogical(p, 8) & mask) * ((Vector.ShiftRightLogical(f, 8) & mask) + one), 8);
                var b = Vector.ShiftRightLogical((p & mask) * ((f & mask) + one), 8);
                ((p & alpha) | Vector.ShiftLeft(r, 16) | Vector.ShiftLeft(g, 8) | b).CopyTo(row[i..]);
            }
            for (; i < width; i++)
                row[i] = MultiplyPixel(row[i], factors[i]);
        }
    }

    /// <summary>Darkest a channel goes on High: Standard's darkness tops out at 245/255, so caves read as dark on both.</summary>
    private const int TintFloor = 10;

    /// <summary>High: the per-channel multiplier for a light value, packed 0x00RRGGBB, each byte 10–255 (255 keeps the channel).</summary>
    public static int TintOf(Vector3 light) => TintChannel(light.X) << 16 | TintChannel(light.Y) << 8 | TintChannel(light.Z);

    private static int TintChannel(float value) =>
        float.IsFinite(value) ? (int)(TintFloor + (255 - TintFloor) * Math.Clamp(value, 0f, 1f)) : TintFloor;

    /// <summary>Scales a pixel's colour channels by a packed tint (byte 255 keeps a channel exactly); alpha is kept.</summary>
    public static int MultiplyPixel(int pixel, int tint)
    {
        var r = (((pixel >> 16) & 0xFF) * (((tint >> 16) & 0xFF) + 1)) >> 8;
        var g = (((pixel >> 8) & 0xFF) * (((tint >> 8) & 0xFF) + 1)) >> 8;
        var b = ((pixel & 0xFF) * ((tint & 0xFF) + 1)) >> 8;
        return (pixel & unchecked((int)0xFF000000)) | r << 16 | g << 8 | b;
    }

    /// <summary>Standard and Low: the premultiplied darkness pixel for a light value, set by its brightest channel.</summary>
    public static int DarknessPixel(Vector3 light)
    {
        var brightness = Math.Clamp(MathF.Max(light.X, MathF.Max(light.Y, light.Z)), 0f, 1f);
        // Premultiplied colour: each channel is scaled by its alpha.
        var darkAlpha = (int)(245 * (1f - brightness));
        return darkAlpha << 24 | (0x05 * darkAlpha / 255) << 16 | (0x06 * darkAlpha / 255) << 8 | 0x0A * darkAlpha / 255;
    }

    /// <summary>The packed High tint at a world tile's centre; 0 outside the window or when the last present was not High.</summary>
    internal int TintAt(int x, int y)
    {
        var lx = x - OriginX;
        var ly = y - OriginY;
        if (_tintWidth == 0 || lx < 0 || ly < 0 || lx >= _width || ly >= _height)
            return 0;
        var sub = Subdivision;
        return _tint[(ly * sub + sub / 2) * _tintWidth + lx * sub + sub / 2];
    }

    /// <summary>The scratch device context the High multiply reads the window's back buffer through (Windows GDI).</summary>
    private static class Gdi
    {
        public const uint SrcCopy = 0x00CC0020;
        public static IntPtr MemoryDc;
        public static IntPtr Bits;
        private static IntPtr _section;
        private static IntPtr _previous;
        private static int _width;
        private static int _height;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public int Size;
            public int Width;
            public int Height;
            public short Planes;
            public short BitCount;
            public int Compression;
            public int SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public int ClrUsed;
            public int ClrImportant;
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfoHeader info, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr gdiObject);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr gdiObject);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint rop);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] public static extern bool GdiFlush();

        /// <summary>
        /// A top-down 32-bit DIB exactly <paramref name="width"/> wide (so its rows pack like the managed buffer) selected
        /// into <see cref="MemoryDc"/>; remade only when the window's size changes.
        /// </summary>
        public static bool Ensure(IntPtr hdc, int width, int height)
        {
            if (MemoryDc != IntPtr.Zero && _section != IntPtr.Zero && width == _width && height == _height)
                return true;
            if (MemoryDc == IntPtr.Zero)
                MemoryDc = CreateCompatibleDC(hdc);
            if (MemoryDc == IntPtr.Zero)
                return false;
            if (_section != IntPtr.Zero)
            {
                SelectObject(MemoryDc, _previous);
                DeleteObject(_section);
                _section = IntPtr.Zero;
            }
            var header = new BitmapInfoHeader
            {
                Size = System.Runtime.InteropServices.Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = -height, // negative: top-down rows
                Planes = 1,
                BitCount = 32
            };
            _section = CreateDIBSection(hdc, ref header, 0, out Bits, IntPtr.Zero, 0);
            if (_section == IntPtr.Zero)
            {
                _width = _height = 0;
                return false;
            }
            _previous = SelectObject(MemoryDc, _section);
            _width = width;
            _height = height;
            return true;
        }
    }

    /// <summary>Brightness 0–1 at a world tile, for callers that want to dim sprites.</summary>
    public float BrightnessAt(int x, int y)
    {
        var lx = x - OriginX;
        var ly = y - OriginY;
        if (lx < 0 || ly < 0 || lx >= _width || ly >= _height)
            return 1f;
        var i = ly * _width + lx;
        return MathF.Max(_r[i], MathF.Max(_g[i], _b[i]));
    }

    /// <summary>Glowing tiles. Names cover tiles added later (crystal, uranium, fungal) without new code.</summary>
    private static readonly Dictionary<TileId, Vector3?> NamedGlow = new();

    /// <summary>Placed light sources (torches, lamps, glowing herbs) respect wiring state; terrain glows by name.</summary>
    private static Vector3? Emission(in WorldCell cell)
    {
        if (cell.TileId == TileId.Air)
            return null;
        if (Rimtera.World.TilePhysics.GetTileLight(cell.TileId) is not null)
            return Rimtera.World.TilePhysics.GetCellLight(cell) is { } light
                ? new Vector3(light.R, light.G, light.B) * Math.Clamp(light.Radius / 7f, 0.4f, 1.4f)
                : null;
        if (!NamedGlow.TryGetValue(cell.TileId, out var glow))
            NamedGlow[cell.TileId] = glow = Emission(cell.TileId);
        return glow;
    }

    private static Vector3? Emission(TileId tile)
    {
        switch (tile)
        {
            case TileId.EnergyCrystal: return new Vector3(0.35f, 0.85f, 1f);
            case TileId.FungalSoil: return new Vector3(0.12f, 0.45f, 0.42f);
            case TileId.CorruptRock: return new Vector3(0.28f, 0.10f, 0.36f);
            case TileId.CrimsonSoil: return new Vector3(0.10f, 0.32f, 0.08f);
            case TileId.EmberRock: return new Vector3(0.06f, 0.18f, 0.04f);
            case TileId.IrradiatedRock: return new Vector3(0.04f, 0.14f, 0.03f);
            case TileId.VitrifiedGlass: return new Vector3(0.10f, 0.25f, 0.12f);
            case TileId.Furnace:
            case TileId.Anvil: return new Vector3(0.9f, 0.5f, 0.2f);
            case TileId.OxygenUnit: return new Vector3(0.3f, 0.8f, 0.85f);
            case TileId.MiniTurret: return new Vector3(0.6f, 0.15f, 0.1f);
            case TileId.Workbench: return new Vector3(0.3f, 0.7f, 0.9f);
        }
        var name = tile.ToString();
        if (name.Contains("Uranium", StringComparison.Ordinal)) return new Vector3(0.25f, 0.75f, 0.2f);
        if (name.Contains("Crystal", StringComparison.Ordinal) || name.Contains("Glacial", StringComparison.Ordinal))
            return new Vector3(0.25f, 0.55f, 0.8f);
        if (name.Contains("Pyrocite", StringComparison.Ordinal) || name.Contains("Lava", StringComparison.Ordinal))
            return new Vector3(0.3f, 0.7f, 0.15f);
        if (name.Contains("Spore", StringComparison.Ordinal) || name.Contains("Reactor", StringComparison.Ordinal))
            return new Vector3(0.5f, 0.9f, 0.4f);
        return null;
    }

    private void Resize(int width, int height)
    {
        if (width == _width && height == _height)
            return;
        _width = width;
        _height = height;
        _r = new float[width * height];
        _g = new float[width * height];
        _b = new float[width * height];
        _medium = new byte[width * height];
        _darkness?.Dispose();
        _darkness = null; // made on the first Standard present; High needs no bitmap
        _tintWidth = _tintHeight = 0;
    }

    private void Seed(int lx, int ly, Vector3 light)
    {
        var i = ly * _width + lx;
        _r[i] = MathF.Max(_r[i], light.X);
        _g[i] = MathF.Max(_g[i], light.Y);
        _b[i] = MathF.Max(_b[i], light.Z);
    }

    private float Falloff(int index) => _medium[index] switch
    {
        0 => AirFalloff,
        1 => LiquidFalloff,
        _ => SolidFalloff
    };

    /// <summary>Left→right, right→left, top→bottom, bottom→top; each cell keeps the brighter of itself or its decayed neighbour.</summary>
    private void Spread()
    {
        for (var y = 0; y < _height; y++)
        {
            var row = y * _width;
            for (var x = 1; x < _width; x++) Carry(row + x - 1, row + x);
            for (var x = _width - 2; x >= 0; x--) Carry(row + x + 1, row + x);
        }
        for (var x = 0; x < _width; x++)
        {
            for (var y = 1; y < _height; y++) Carry((y - 1) * _width + x, y * _width + x);
            for (var y = _height - 2; y >= 0; y--) Carry((y + 1) * _width + x, y * _width + x);
        }
    }

    private void Carry(int from, int to)
    {
        var falloff = Falloff(to);
        var r = _r[from] * falloff;
        var g = _g[from] * falloff;
        var b = _b[from] * falloff;
        if (r > _r[to]) _r[to] = r;
        if (g > _g[to]) _g[to] = g;
        if (b > _b[to]) _b[to] = b;
    }

    /// <summary>Light at a fractional tile position, bilinear between tile centres.</summary>
    private Vector3 Sample(float fx, float fy)
    {
        var x0 = Math.Clamp((int)MathF.Floor(fx), 0, _width - 1);
        var y0 = Math.Clamp((int)MathF.Floor(fy), 0, _height - 1);
        var x1 = Math.Min(x0 + 1, _width - 1);
        var y1 = Math.Min(y0 + 1, _height - 1);
        var tx = Math.Clamp(fx - x0, 0f, 1f);
        var ty = Math.Clamp(fy - y0, 0f, 1f);
        Vector3 At(int x, int y)
        {
            var i = y * _width + x;
            return new Vector3(_r[i], _g[i], _b[i]);
        }
        var top = Vector3.Lerp(At(x0, y0), At(x1, y0), tx);
        var bottom = Vector3.Lerp(At(x0, y1), At(x1, y1), tx);
        return Vector3.Lerp(top, bottom, ty);
    }

    private int[] _darkPixels = [];
    private int[] _tint = [];
    private int _tintWidth;
    private int _tintHeight;
    private float[] _rowR = [];
    private float[] _rowG = [];
    private float[] _rowB = [];
    private int[] _axisX0 = [];
    private int[] _axisX1 = [];
    private float[] _axisTx = [];
    private int[] _axisY0 = [];
    private int[] _axisY1 = [];
    private float[] _axisTy = [];

    /// <summary>Per sub-pixel: the two tiles it lies between along one axis and how far it is from the first (as in <see cref="Sample"/>).</summary>
    private void Axis(int pixels, int tiles, ref int[] first, ref int[] second, ref float[] weight)
    {
        if (first.Length != pixels)
        {
            first = new int[pixels];
            second = new int[pixels];
            weight = new float[pixels];
        }
        for (var p = 0; p < pixels; p++)
        {
            var f = (p + 0.5f) / Subdivision - 0.5f;
            var i0 = Math.Clamp((int)MathF.Floor(f), 0, tiles - 1);
            first[p] = i0;
            second[p] = Math.Min(i0 + 1, tiles - 1);
            weight[p] = Math.Clamp(f - i0, 0f, 1f);
        }
    }

    /// <summary>
    /// The High tint layer: the same bilinear light as <see cref="Sample"/>, but separable (each tile row is widened once,
    /// then rows are blended), which is several times cheaper than sampling each sub-pixel on its own.
    /// </summary>
    private void PresentTint(int pixelWidth, int pixelHeight)
    {
        if (_tint.Length != pixelWidth * pixelHeight)
            _tint = new int[pixelWidth * pixelHeight];
        if (_rowR.Length != _height * pixelWidth)
        {
            _rowR = new float[_height * pixelWidth];
            _rowG = new float[_height * pixelWidth];
            _rowB = new float[_height * pixelWidth];
        }
        Axis(pixelWidth, _width, ref _axisX0, ref _axisX1, ref _axisTx);
        Axis(pixelHeight, _height, ref _axisY0, ref _axisY1, ref _axisTy);
        for (var y = 0; y < _height; y++)
        {
            var tiles = y * _width;
            var row = y * pixelWidth;
            for (var px = 0; px < pixelWidth; px++)
            {
                var a = tiles + _axisX0[px];
                var b = tiles + _axisX1[px];
                var t = _axisTx[px];
                _rowR[row + px] = _r[a] * (1f - t) + _r[b] * t;
                _rowG[row + px] = _g[a] * (1f - t) + _g[b] * t;
                _rowB[row + px] = _b[a] * (1f - t) + _b[b] * t;
            }
        }
        for (var py = 0; py < pixelHeight; py++)
        {
            var top = _axisY0[py] * pixelWidth;
            var bottom = _axisY1[py] * pixelWidth;
            var t = _axisTy[py];
            var output = py * pixelWidth;
            for (var px = 0; px < pixelWidth; px++)
                _tint[output + px] = TintOf(new Vector3(
                    _rowR[top + px] * (1f - t) + _rowR[bottom + px] * t,
                    _rowG[top + px] * (1f - t) + _rowG[bottom + px] * t,
                    _rowB[top + px] * (1f - t) + _rowB[bottom + px] * t));
        }
        _tintWidth = pixelWidth;
        _tintHeight = pixelHeight;
    }

    private void Present()
    {
        var pixelWidth = _width * Subdivision;
        var pixelHeight = _height * Subdivision;
        if (ColouredGlow)
        {
            // High: a per-channel multiplier per sub-pixel, applied to the drawn scene in Draw. No bitmap is involved.
            PresentTint(pixelWidth, pixelHeight);
            return;
        }
        _tintWidth = _tintHeight = 0;
        // Premultiplied: the format GDI+ blends fastest, as this covers the whole screen every frame.
        _darkness ??= new Bitmap(pixelWidth, pixelHeight, PixelFormat.Format32bppPArgb);
        var bounds = new Rectangle(0, 0, pixelWidth, pixelHeight);
        var dark = _darkness.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var stride = dark.Stride / 4;
            // Reused between frames: a fresh ~320 KB array a frame landed on the large-object heap and caused hitches.
            if (_darkPixels.Length != stride * pixelHeight)
                _darkPixels = new int[stride * pixelHeight];
            var darkPixels = _darkPixels;
            for (var py = 0; py < pixelHeight; py++)
            for (var px = 0; px < pixelWidth; px++)
                // Sub-pixel centres mapped back to tile space, offset so tile centres sit on samples.
                darkPixels[py * stride + px] = DarknessPixel(Sample((px + 0.5f) / Subdivision - 0.5f, (py + 0.5f) / Subdivision - 0.5f));
            System.Runtime.InteropServices.Marshal.Copy(darkPixels, 0, dark.Scan0, darkPixels.Length);
        }
        finally
        {
            _darkness.UnlockBits(dark);
        }
    }
}
