using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Rimtera.Systems;
using Rimtera.World;

namespace Rimtera.UI;

public static partial class GraphicalSandbox
{
    /// <summary>
    /// The sky, sun or moon, clouds, parallax panoramas and biome tint are several full-screen blends, which GDI+ does
    /// on the CPU. They are drawn into one half-resolution image, refreshed 30 times a second, and copied up to the
    /// screen as a single opaque blit; underground, where the cave backdrop covers them, they are skipped.
    /// </summary>
    private sealed partial class GameCanvas
    {
        private const double BackdropRefreshSeconds = 1.0 / 30.0;
        private Bitmap? _backdrop;
        private double _backdropTime = double.NegativeInfinity;
        private int[]? _caveTop;
        private long[]? _caveStamp;

        private void DrawBackdrop(Graphics g, GameSimulation game, float night, double now, int startX, int startY, int viewW, int viewH)
        {
            var height = Height - HudHeight;
            if (Width <= 0 || height <= 0 || ViewIsUnderground(game, startX, startY, viewW))
                return;
            var width = (Width + 1) / 2;
            var halfHeight = (height + 1) / 2;
            if (_backdrop is null || _backdrop.Width != width || _backdrop.Height != halfHeight)
            {
                _backdrop?.Dispose();
                _backdrop = new Bitmap(width, halfHeight, PixelFormat.Format32bppPArgb);
                _backdropTime = double.NegativeInfinity;
            }
            if (now - _backdropTime >= BackdropRefreshSeconds || now < _backdropTime)
            {
                _backdropTime = now;
                using var layer = Graphics.FromImage(_backdrop);
                layer.InterpolationMode = InterpolationMode.NearestNeighbor;
                layer.SmoothingMode = SmoothingMode.AntiAlias;
                // Drawn in full-screen coordinates; the transform halves them.
                layer.ScaleTransform(0.5f, 0.5f);
                DrawSky(layer, Width, height, night);
                DrawSkyBodies(layer, game, night);
                DrawClouds(layer, game, now);
                DrawParallax(layer, game, _viewStartX, _viewStartY, night);
                if (_menu.Preferences.WeatherEffects && BiomeSkyTint(game.CurrentBiome.Zone) is { } tint)
                {
                    using var tintBrush = new SolidBrush(tint);
                    layer.FillRectangle(tintBrush, 0, 0, Width, height);
                }
            }
            var mode = g.CompositingMode;
            var interpolation = g.InterpolationMode;
            var offset = g.PixelOffsetMode;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_backdrop, new Rectangle(0, 0, width * 2, halfHeight * 2));
            g.CompositingMode = mode;
            g.InterpolationMode = interpolation;
            g.PixelOffsetMode = offset;
        }

        /// <summary>
        /// True when every visible column's cave backdrop starts above the view, so the sky could not show anywhere.
        /// Uses the same surface rule as the chunk renderer (first solid block, or the terrain line below a sky island).
        /// </summary>
        private bool ViewIsUnderground(GameSimulation game, int startX, int startY, int viewW)
        {
            var world = game.World;
            if (_caveTop is null || _caveTop.Length != world.Width)
            {
                _caveTop = new int[world.Width];
                _caveStamp = new long[world.Width];
                Array.Fill(_caveStamp, -1);
            }
            var revision = world.TileRevision;
            for (var x = Math.Max(0, startX); x < Math.Min(world.Width, startX + viewW); x++)
            {
                if (_caveStamp![x] != revision)
                {
                    var surface = 0;
                    while (surface < world.Height && world.GetCell(new GridPoint(x, surface)) is var probe &&
                           (probe.TileId == TileId.Air || !game.Tiles.Contains(probe.TileId) || !game.Tiles[probe.TileId].IsSolid))
                        surface++;
                    var terrain = game.BiomeLayout.SurfaceAt(x);
                    if (surface < terrain - 12)
                        surface = terrain;
                    _caveTop[x] = surface + 1;
                    _caveStamp[x] = revision;
                }
                if (_caveTop[x] > startY)
                    return false;
            }
            return true;
        }

        /// <summary>Where a frame's time goes, shown under the frame rate (F10) as rolling averages in milliseconds.</summary>
        private sealed class FrameProfiler
        {
            private readonly Stopwatch _watch = new();
            private readonly Dictionary<string, double> _average = new();
            private readonly List<string> _order = new();
            private double _last;

            public void Begin()
            {
                _watch.Restart();
                _last = 0;
            }

            public void Mark(string section)
            {
                var now = _watch.Elapsed.TotalMilliseconds;
                var spent = now - _last;
                _last = now;
                if (!_average.TryGetValue(section, out var average))
                {
                    _order.Add(section);
                    average = spent;
                }
                _average[section] = average + (spent - average) * 0.05;
            }

            public double Total => _order.Sum(section => _average[section]);

            public string Describe() => string.Join("  ", _order.Select(section => $"{section} {_average[section]:0.0}"));
        }

        private readonly FrameProfiler _profiler = new();
    }
}
