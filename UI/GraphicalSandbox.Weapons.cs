using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;
using Rimtera.Entities;
using Rimtera.Systems;

namespace Rimtera.UI;

public static partial class GraphicalSandbox
{
    /// <summary>
    /// Drawing for weapon styles: melee swings and thrusts, yoyo strings and flail chains, spinning thrown
    /// weapons, whip lashes, tech beams, explosions and whip marks on enemies.
    /// </summary>
    private sealed partial class GameCanvas
    {
        private readonly Pen _yoyoString = new(Color.FromArgb(200, 230, 235, 240), 1.5f);
        private readonly Pen _flailChain = new(Color.FromArgb(220, 150, 155, 165), 3f) { DashStyle = DashStyle.Dot };
        private readonly Pen _markPen = new(Color.FromArgb(230, 255, 90, 200), 2f);

        private PointF Screen(Vector2 world, int startX, int startY) => new((world.X - startX) * TileSize, (world.Y - startY) * TileSize);

        /// <summary>True when the style drawing already shows the weapon (the held-item sprite is skipped).</summary>
        private static bool StyleDrawsWeapon(GameSimulation game) =>
            game.PlayerSwing is not null || game.PlayerWhip is not null || game.PlayerBeam is not null ||
            game.Projectiles.Any(projectile => projectile.Team == ProjectileTeam.Player &&
                                               projectile.Style is WeaponStyle.Yoyo or WeaponStyle.Boomerang or WeaponStyle.Flail);

        private void DrawWeaponStyles(Graphics g, GameSimulation game, int startX, int startY, double now)
        {
            var hand = game.Player.Motion.Position + new Vector2(game.Player.Motion.Size.X / 2f, game.Player.Motion.Size.Y * 0.55f);
            DrawStyledProjectiles(g, game, hand, startX, startY, now);
            if (game.PlayerSwing is { } swing)
                DrawSwing(g, swing, startX, startY);
            if (game.PlayerWhip is { } whip && whip.Points.Count > 1)
            {
                var points = whip.Points.Select(point => Screen(point, startX, startY)).ToArray();
                for (var i = 1; i < points.Length; i++)
                {
                    var thickness = MathF.Max(1.5f, 5f * (1f - i / (float)points.Length));
                    using var pen = new Pen(Color.FromArgb(235, 170, 120, 255), thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                    g.DrawLine(pen, points[i - 1], points[i]);
                }
                if (_menu.Preferences.FlashyEffects)
                    g.FillEllipse(_amberBrush, points[^1].X - 3, points[^1].Y - 3, 6, 6);
            }
            if (game.PlayerBeam is { } beam)
            {
                var a = Screen(beam.Start, startX, startY);
                var b = Screen(beam.End, startX, startY);
                var width = MathF.Max(3f, beam.Width * TileSize);
                var flicker = 0.85f + 0.15f * MathF.Sin((float)now * 40f);
                using var glow = new Pen(Color.FromArgb((int)(90 * flicker), 120, 220, 255), width * 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                using var core = new Pen(Color.FromArgb(240, 220, 250, 255), width * 0.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(glow, a, b);
                g.DrawLine(core, a, b);
            }
            foreach (var explosion in game.RecentExplosions)
            {
                var t = Math.Clamp(explosion.AgeSeconds / 0.5f, 0f, 1f);
                var center = Screen(explosion.Center, startX, startY);
                var radius = explosion.Radius * TileSize * (0.4f + 0.6f * t);
                using var fill = new SolidBrush(Color.FromArgb((int)(150 * (1 - t)), 255, 170, 60));
                using var ring = new Pen(Color.FromArgb((int)(220 * (1 - t)), 255, 230, 160), 3f);
                g.FillEllipse(fill, center.X - radius, center.Y - radius, radius * 2, radius * 2);
                g.DrawEllipse(ring, center.X - radius, center.Y - radius, radius * 2, radius * 2);
            }
            foreach (var enemy in game.Enemies)
            {
                if (!enemy.IsAlive || !game.IsMarkedByWhip(enemy))
                    continue;
                var top = Screen(enemy.Motion.Position + new Vector2(enemy.Motion.Size.X / 2f, -0.4f), startX, startY);
                var spin = (float)(now * 3.0);
                for (var i = 0; i < 3; i++)
                {
                    var angle = spin + i * MathF.Tau / 3f;
                    g.DrawLine(_markPen, top.X + MathF.Cos(angle) * 4, top.Y + MathF.Sin(angle) * 4, top.X + MathF.Cos(angle) * 9, top.Y + MathF.Sin(angle) * 9);
                }
            }
        }

        /// <summary>Long axis of a weapon sprite: direction from grip to tip (tip = the upper end) and its pixel length.</summary>
        private readonly record struct WeaponAxis(float TipX, float TipY, float Length, float CenterX, float CenterY);

        private readonly Dictionary<Bitmap, WeaponAxis> _weaponAxes = new();

        /// <summary>Principal axis of the opaque pixels (sheet art points in different directions).</summary>
        private WeaponAxis AxisOf(Bitmap art)
        {
            if (_weaponAxes.TryGetValue(art, out var cached))
                return cached;
            double sx = 0, sy = 0, n = 0;
            var points = new List<(int X, int Y)>();
            for (var y = 0; y < art.Height; y++)
            for (var x = 0; x < art.Width; x++)
                if (art.GetPixel(x, y).A > 40)
                {
                    points.Add((x, y));
                    sx += x;
                    sy += y;
                    n++;
                }
            if (n == 0)
                return _weaponAxes[art] = new WeaponAxis(1, 0, art.Width, art.Width / 2f, art.Height / 2f);
            double cx = sx / n, cy = sy / n, xx = 0, yy = 0, xy = 0;
            foreach (var (x, y) in points)
            {
                xx += (x - cx) * (x - cx);
                yy += (y - cy) * (y - cy);
                xy += (x - cx) * (y - cy);
            }
            var angle = 0.5 * Math.Atan2(2 * xy, xx - yy);
            var ux = (float)Math.Cos(angle);
            var uy = (float)Math.Sin(angle);
            if (uy > 0 || (Math.Abs(uy) < 0.01f && ux < 0))
            {
                ux = -ux;
                uy = -uy;
            }
            float min = 0, max = 0;
            foreach (var (x, y) in points)
            {
                var along = (float)((x - cx) * ux + (y - cy) * uy);
                min = Math.Min(min, along);
                max = Math.Max(max, along);
            }
            return _weaponAxes[art] = new WeaponAxis(ux, uy, max - min, (float)cx + ux * (max + min) / 2f, (float)cy + uy * (max + min) / 2f);
        }

        /// <summary>
        /// Broadswords sweep the blade through the arc; shortswords and spears push it out along the aim line
        /// and pull it back. The sprite's own axis is turned onto the swing angle with its grip at the hand.
        /// </summary>
        private void DrawSwing(Graphics g, MeleeSwingView swing, int startX, int startY)
        {
            var origin = Screen(swing.Origin, startX, startY);
            var reach = Math.Max(TileSize, swing.Reach * TileSize);
            if (_sprites.GetFitted(SpriteCatalog.ForItem(swing.Item), (int)reach * 2, (int)reach * 2) is not { } art)
                return;
            var axis = AxisOf(art);
            var thrust = swing.Style is WeaponStyle.Spear or WeaponStyle.Shortsword;
            // Blade length on screen: the whole reach for swords; a spear's shaft is most of it, the thrust covers the rest.
            var bladeLength = thrust ? reach * (swing.Style == WeaponStyle.Spear ? 0.7f : 0.8f) : reach;
            var scale = bladeLength / Math.Max(1f, axis.Length);
            var push = thrust ? MathF.Sin(MathF.PI * Math.Clamp(swing.Progress, 0f, 1f)) * (reach - bladeLength) : 0f;
            var artDegrees = MathF.Atan2(axis.TipY, axis.TipX) * 180f / MathF.PI;
            var aimDegrees = swing.AngleRadians * 180f / MathF.PI;
            var state = g.Save();
            g.TranslateTransform(origin.X + MathF.Cos(swing.AngleRadians) * push, origin.Y + MathF.Sin(swing.AngleRadians) * push);
            g.RotateTransform(aimDegrees - artDegrees);
            g.ScaleTransform(scale, scale);
            // Put the grip end (opposite the tip) on the hand.
            var gripX = axis.CenterX - axis.TipX * axis.Length / 2f;
            var gripY = axis.CenterY - axis.TipY * axis.Length / 2f;
            g.DrawImage(art, -gripX, -gripY, art.Width, art.Height);
            g.Restore(state);
            if (swing.Style == WeaponStyle.Broadsword && _menu.Preferences.FlashyEffects)
            {
                using var arc = new Pen(Color.FromArgb(70, 255, 255, 255), 3f);
                var r = reach * 0.95f;
                g.DrawArc(arc, origin.X - r, origin.Y - r, r * 2, r * 2, aimDegrees - 40f, 40f);
            }
        }

        private void DrawStyledProjectiles(Graphics g, GameSimulation game, Vector2 hand, int startX, int startY, double now)
        {
            var handScreen = Screen(hand, startX, startY);
            foreach (var projectile in game.Projectiles)
            {
                if (projectile.Style == WeaponStyle.None || projectile.SourceItem is not { } item)
                    continue;
                var at = Screen(projectile.Position, startX, startY);
                if (projectile.Tethered)
                    g.DrawLine(projectile.Style == WeaponStyle.Flail ? _flailChain : _yoyoString, handScreen, at);
                var spins = projectile.Style is WeaponStyle.Yoyo or WeaponStyle.Boomerang or WeaponStyle.Flail or WeaponStyle.Thrown;
                if (!spins)
                    continue;
                var size = (int)(TileSize * (projectile.Style == WeaponStyle.Flail ? 1.1f : 0.9f));
                if (_sprites.GetFitted(SpriteCatalog.ForItem(item), size, size) is not { } art)
                    continue;
                var state = g.Save();
                g.TranslateTransform(at.X, at.Y);
                g.RotateTransform((float)(now * (projectile.Style == WeaponStyle.Yoyo ? 900 : 720) % 360));
                g.DrawImage(art, -art.Width / 2f, -art.Height / 2f, art.Width, art.Height);
                g.Restore(state);
            }
        }
    }
}
