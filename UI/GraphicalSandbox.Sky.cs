using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Rimtera.Systems;
using Rimtera.World;

namespace Rimtera.UI;

public static partial class GraphicalSandbox
{
    /// <summary>
    /// Sky layer art: sun and moon (with the current phase) on an arc across the sky, sprite clouds, and parallax
    /// background panoramas per biome anchored to the surface line. Each piece uses generated art when it exists
    /// (sky_sun, sky_moon, sky_cloud_1..6, bg_&lt;theme&gt;_far / _near — tools/comfy/gen_env.py) and otherwise
    /// leaves the procedural sky as it was.
    /// </summary>
    private sealed partial class GameCanvas
    {
        private static readonly string[] CloudSprites = ["sky_cloud_1", "sky_cloud_2", "sky_cloud_3", "sky_cloud_4", "sky_cloud_5", "sky_cloud_6"];

        /// <summary>Parallax theme for the biome the player is in (matches gen_env.py's background list).</summary>
        private static string BackgroundTheme(GameSimulation game) => game.CurrentBiome.Zone switch
        {
            BiomeZone.FrozenWasteland => "frozen",
            BiomeZone.JunkWasteland => "junk",
            BiomeZone.RadiatedDeeps => "radiated",
            BiomeZone.Overgrowth => "overgrowth",
            BiomeZone.MoltenChasm => "core",
            _ => game.CurrentBiome.DisplayName.ToLowerInvariant() switch
            {
                var name when name.Contains("ocean") || name.Contains("coast") => "ocean",
                var name when name.Contains("canopy") || name.Contains("verdant") => "canopy",
                var name when name.Contains("fungal") || name.Contains("spore") => "fungal",
                var name when name.Contains("crystal") => "crystal",
                _ => "grassland"
            }
        };

        /// <summary>Sun by day and moon by night on an arc; the moon is shaded to the current phase.</summary>
        private void DrawSkyBodies(Graphics g, GameSimulation game, float nightFactor)
        {
            if (!_menu.Preferences.BackgroundEnabled)
                return;
            var hour = game.ClockHour;
            var day = hour is >= 5f and < 19f;
            // Fraction of the way across the sky: sunrise 5h to sunset 19h, moonrise 19h to moonset 5h.
            var t = day ? (hour - 5f) / 14f : ((hour + 24f - 19f) % 24f) / 10f;
            var size = (int)Math.Clamp(Width * 0.06f, 40f, 90f);
            var x = Width * (0.08f + 0.84f * t) - size / 2f;
            var y = (Height - HudHeight) * (0.42f - 0.32f * MathF.Sin(MathF.PI * t)) - size / 2f;
            if (day)
            {
                if (_sprites.GetFitted("sky_sun", size, size) is { } sun)
                    g.DrawImage(sun, x, y, sun.Width, sun.Height);
                return;
            }
            if (_sprites.GetFitted("sky_moon", size, size) is not { } moon)
                return;
            g.DrawImage(moon, x, y, moon.Width, moon.Height);
            // Phase 0 full … 4 new … back to full: cover the unlit part with the night sky colour.
            var phase = game.MoonPhase;
            var lit = 1f - MathF.Abs(phase - 4) / 4f;
            if (lit < 0.98f)
            {
                using var shadow = new SolidBrush(Color.FromArgb(235, 10, 12, 26));
                var offset = moon.Width * (phase < 4 ? 1f - lit : -(1f - lit)) * 1.05f;
                var state = g.Save();
                using var clip = new GraphicsPath();
                clip.AddEllipse(x, y, moon.Width, moon.Height);
                g.SetClip(clip);
                g.FillEllipse(shadow, x + offset, y - 1, moon.Width, moon.Height + 2);
                g.Restore(state);
            }
        }

        /// <summary>Generated cloud sprites drifting with the wind; storm clouds are darkened. False when no cloud art exists.</summary>
        private bool DrawSpriteClouds(Graphics g, GameSimulation game, double now)
        {
            if (_sprites.Find(CloudSprites[0]) is null)
                return false;
            var cover = game.Weather.CloudCover;
            var count = (int)(cover * 12);
            var height = Height - HudHeight;
            var shade = game.Weather.Storm ? 0.45f : 1f;
            for (var i = 0; i < count; i++)
            {
                var w = 140 + i * 37 % 180;
                if (_sprites.GetFitted(CloudSprites[i % CloudSprites.Length], w, w / 2) is not { } fitted)
                    continue;
                var cloud = Tinted(fitted, shade, 0.5f + cover * 0.45f);
                var span = Width + cloud.Width * 2;
                var x = (float)(((i * 263 + now * (8 + game.Weather.Wind * 12) * (1 + i % 3 * 0.4)) % span + span) % span) - cloud.Width;
                var y = 20 + i * 53 % Math.Max(1, height / 3);
                g.DrawImageUnscaled(cloud, (int)x, y);
            }
            return true;
        }

        /// <summary>
        /// Far and near parallax panoramas for the current biome, their bottom edge on the surface line under the
        /// screen centre, scrolling slower than the world. Fades out once the surface is far above the view.
        /// </summary>
        private void DrawParallax(Graphics g, GameSimulation game, float startX, float startY, float nightFactor)
        {
            if (!_menu.Preferences.BackgroundEnabled)
                return;
            var theme = BackgroundTheme(game);
            var centerX = (int)(startX + Width / TileSize / 2);
            var horizon = (int)((game.BiomeLayout.SurfaceAt(Math.Clamp(centerX, 0, game.World.Width - 1)) - startY) * TileSize) + TileSize * 2;
            var height = Height - HudHeight;
            if (horizon < -height / 2)
                return;
            var fade = Math.Clamp(1f - (-horizon) / (height * 0.5f), 0f, 1f);
            var dark = 1f - nightFactor * 0.55f;
            foreach (var (layer, parallax, scale, tone) in new[] { ("far", 0.08f, 0.55f, 0.75f), ("near", 0.22f, 0.42f, 0.9f) })
            {
                var name = $"bg_{theme}_{layer}";
                if (_sprites.Get(name) is not { } source)
                    continue;
                var drawH = (int)(height * scale);
                var drawW = Math.Max(1, source.Width * drawH / Math.Max(1, source.Height));
                if (_sprites.GetScaled(name, drawW, drawH) is not { } scaled)
                    continue;
                var art = Tinted(scaled, tone * dark, fade);
                var scroll = (int)(startX * TileSize * parallax) % drawW;
                for (var x = -scroll - drawW; x < Width; x += drawW)
                    g.DrawImageUnscaled(art, x, horizon - drawH);
            }
        }

        private readonly Dictionary<(Bitmap Source, int Shade, int Alpha), Bitmap> _tinted = new();

        /// <summary>
        /// A copy of <paramref name="source"/> darkened by <paramref name="shade"/> (blue kept a touch brighter) at
        /// <paramref name="alpha"/>, made once per step of 1/32 and reused: drawing through a colour matrix every frame is
        /// GDI+'s slowest image path, and these images span the screen.
        /// </summary>
        private Bitmap Tinted(Bitmap source, float shade, float alpha)
        {
            var key = (source, (int)MathF.Round(Math.Clamp(shade, 0f, 1.2f) * 32), (int)MathF.Round(Math.Clamp(alpha, 0f, 1f) * 32));
            if (_tinted.TryGetValue(key, out var cached))
                return cached;
            if (_tinted.Count > 160)
            {
                foreach (var old in _tinted.Values)
                    old.Dispose();
                _tinted.Clear();
            }
            var k = key.Item2 / 32f;
            var copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(copy))
            using (var attributes = new ImageAttributes())
            {
                attributes.SetColorMatrix(new ColorMatrix { Matrix00 = k, Matrix11 = k, Matrix22 = k * 1.05f, Matrix33 = key.Item3 / 32f, Matrix44 = 1f });
                g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }
            return _tinted[key] = copy;
        }
    }
}
