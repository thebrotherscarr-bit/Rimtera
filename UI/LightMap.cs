using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using Rimtera.World;

namespace Rimtera.UI;

/// <summary>
/// Renderer-side tile lighting. Seeds light from open sky, glowing tiles and liquids, and
/// actor lamps, then spreads it with four directional sweeps whose falloff depends on what
/// the light passes through (air, liquid, solid). The result is drawn as a darkness layer and
/// a coloured glow layer, one pixel per tile, scaled up with bilinear filtering.
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

    /// <summary>Whether coloured light tints the scene (High lighting).</summary>
    public bool ColouredGlow { get; set; } = true;

    private float[] _r = [];
    private float[] _g = [];
    private float[] _b = [];
    private byte[] _medium = [];
    private int _width;
    private int _height;
    private Bitmap? _darkness;
    private Bitmap? _glow;

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

    /// <summary>Draws the darkness and glow layers over the world area.</summary>
    public void Draw(Graphics g, int screenX, int screenY, int tileSize)
    {
        if (_darkness is null || _glow is null)
            return;
        var previous = g.InterpolationMode;
        var previousOffset = g.PixelOffsetMode;
        // The layers are already smoothed per sub-pixel, so plain block scaling avoids GDI+ filter seams.
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        var destination = new Rectangle(screenX - Padding * tileSize, screenY - Padding * tileSize, _width * tileSize, _height * tileSize);
        if (ColouredGlow)
            g.DrawImage(_glow, destination);
        g.DrawImage(_darkness, destination);
        g.InterpolationMode = previous;
        g.PixelOffsetMode = previousOffset;
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
        _glow?.Dispose();
        // Premultiplied: the format GDI+ blends fastest, as these cover the whole screen every frame.
        _darkness = new Bitmap(width * Subdivision, height * Subdivision, PixelFormat.Format32bppPArgb);
        _glow = new Bitmap(width * Subdivision, height * Subdivision, PixelFormat.Format32bppPArgb);
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
    private int[] _glowPixels = [];

    private void Present()
    {
        var pixelWidth = _width * Subdivision;
        var pixelHeight = _height * Subdivision;
        var bounds = new Rectangle(0, 0, pixelWidth, pixelHeight);
        var dark = _darkness!.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        var glow = _glow!.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var stride = dark.Stride / 4;
            // Reused between frames: two fresh ~320 KB arrays a frame landed on the large-object heap and caused hitches.
            if (_darkPixels.Length != stride * pixelHeight)
            {
                _darkPixels = new int[stride * pixelHeight];
                _glowPixels = new int[stride * pixelHeight];
            }
            var darkPixels = _darkPixels;
            var glowPixels = _glowPixels;
            for (var py = 0; py < pixelHeight; py++)
            for (var px = 0; px < pixelWidth; px++)
            {
                // Sub-pixel centres mapped back to tile space, offset so tile centres sit on samples.
                var light = Sample((px + 0.5f) / Subdivision - 0.5f, (py + 0.5f) / Subdivision - 0.5f);
                var max = MathF.Max(light.X, MathF.Max(light.Y, light.Z));
                var min = MathF.Min(light.X, MathF.Min(light.Y, light.Z));
                var brightness = Math.Clamp(max, 0f, 1f);
                var p = py * stride + px;
                // Premultiplied colour: each channel is scaled by its alpha.
                var darkAlpha = (int)(245 * (1f - brightness));
                darkPixels[p] = darkAlpha << 24 | (0x05 * darkAlpha / 255) << 16 | (0x06 * darkAlpha / 255) << 8 | 0x0A * darkAlpha / 255;
                // Coloured light shows as a faint tint where one channel dominates.
                var glowAlpha = (int)Math.Clamp((max - min) * 120f, 0f, 90f);
                var scale = max > 0.001f ? 255f / max * glowAlpha / 255f : 0f;
                glowPixels[p] = glowAlpha << 24
                                | (int)Math.Clamp(light.X * scale, 0, 255) << 16
                                | (int)Math.Clamp(light.Y * scale, 0, 255) << 8
                                | (int)Math.Clamp(light.Z * scale, 0, 255);
            }
            System.Runtime.InteropServices.Marshal.Copy(darkPixels, 0, dark.Scan0, darkPixels.Length);
            System.Runtime.InteropServices.Marshal.Copy(glowPixels, 0, glow.Scan0, glowPixels.Length);
        }
        finally
        {
            _darkness.UnlockBits(dark);
            _glow.UnlockBits(glow);
        }
    }
}
