using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;
using Rimtera.Systems;
using Rimtera.World;

namespace Rimtera.UI;

public static partial class GraphicalSandbox
{
    /// <summary>Travel drawing: glide-walks, pylons, mount art, and pylon map icons.</summary>
    private sealed partial class GameCanvas
    {
        private readonly Brush _walkHousingBrush = new SolidBrush(Color.FromArgb(138, 148, 162));
        private readonly Brush _walkShadeBrush = new SolidBrush(Color.FromArgb(96, 104, 118));
        private readonly Brush _walkBeltBrush = new SolidBrush(Color.FromArgb(38, 42, 50));
        private readonly Pen _walkTrimPen = new(Color.FromArgb(200, 210, 222), 1f);
        private readonly Pen _walkExpressPen = new(Color.FromArgb(255, 190, 60), 2f);
        private readonly Pen _walkChevronPen = new(Color.FromArgb(150, 200, 230), 1.5f);
        private readonly Pen _walkRailPen = new(Color.FromArgb(60, 64, 74), 3f);
        private readonly Pen _walkPostPen = new(Color.FromArgb(170, 180, 196), 2f);
        private readonly Brush _pylonFallbackBrush = new SolidBrush(Color.FromArgb(120, 220, 255));
        private readonly Pen _pylonActivePen = new(Color.FromArgb(220, 120, 240, 255), 2f);

        /// <summary>Draws rails and pylons. False for every other tile.</summary>
        private bool DrawTravelTile(Graphics g, in WorldCell cell, int x, int y, Rectangle rect, GameSimulation game)
        {
            var tile = cell.TileId;
            if (TravelContent.IsPylon(tile))
            {
                var w = rect.Width * 1.6f;
                var h = rect.Height * 2.8f;
                if (_sprites.GetFitted(TravelContent.PylonSprite(tile), (int)w, (int)h) is { } art)
                    g.DrawImageUnscaled(art, (int)(rect.X + rect.Width / 2f - art.Width / 2f), rect.Bottom - art.Height);
                else
                    g.FillRectangle(_pylonFallbackBrush, rect.X + rect.Width * 0.25f, rect.Bottom - h, rect.Width * 0.5f, h);
                return true;
            }
            var belt = TravelContent.BeltSpeed(tile);
            if (belt == 0f)
                return false;
            DrawGlideWalk(g, x, y, rect, game, belt);
            return true;
        }

        /// <summary>
        /// A glide-walk: brushed-steel housing, a dark rubber belt whose chevrons scroll the way it runs, and a
        /// waist-high handrail with end posts (express lines get an amber trim).
        /// </summary>
        private void DrawGlideWalk(Graphics g, int x, int y, Rectangle rect, GameSimulation game, float belt)
        {
            var s = rect.Width;
            var express = MathF.Abs(belt) > TravelContent.GlideWalkSpeed + 0.1f;
            g.FillRectangle(_walkHousingBrush, rect);
            g.FillRectangle(_walkShadeBrush, rect.X, rect.Y + s * 0.62f, s, s * 0.38f);
            var beltTop = rect.Y + s * 0.08f;
            var beltHeight = s * 0.3f;
            g.FillRectangle(_walkBeltBrush, rect.X, beltTop, s, beltHeight);
            g.DrawLine(express ? _walkExpressPen : _walkTrimPen, rect.X, beltTop + beltHeight + 1f, rect.Right, beltTop + beltHeight + 1f);

            // Chevrons ride the belt: phase from world x and time so neighbouring tiles line up.
            var dir = Math.Sign(belt);
            var spacing = s * 0.5f;
            var phase = (float)((FrameClock.Seconds * MathF.Abs(belt) * s) % spacing);
            var clip = g.Clip;
            g.SetClip(new RectangleF(rect.X, beltTop, s, beltHeight));
            for (var cx = rect.X - spacing + (dir > 0 ? phase : spacing - phase); cx < rect.Right + spacing; cx += spacing)
            {
                var tip = cx + dir * s * 0.1f;
                var back = cx - dir * s * 0.1f;
                g.DrawLine(_walkChevronPen, back, beltTop + beltHeight * 0.2f, tip, beltTop + beltHeight * 0.5f);
                g.DrawLine(_walkChevronPen, tip, beltTop + beltHeight * 0.5f, back, beltTop + beltHeight * 0.8f);
            }
            g.Clip = clip;

            // Handrail one cell up; posts where the line starts and ends.
            bool Walk(int cx) => game.World.Contains(cx, y) && TravelContent.IsGlideWalk(game.World.GetCell(new GridPoint(cx, y)).TileId);
            var railY = rect.Y - s * 0.75f;
            g.DrawLine(_walkRailPen, Walk(x - 1) ? rect.X : rect.X + s * 0.2f, railY, Walk(x + 1) ? rect.Right : rect.Right - s * 0.2f, railY);
            if (!Walk(x - 1))
                g.DrawLine(_walkPostPen, rect.X + s * 0.2f, railY, rect.X + s * 0.2f, rect.Y);
            if (!Walk(x + 1))
                g.DrawLine(_walkPostPen, rect.Right - s * 0.2f, railY, rect.Right - s * 0.2f, rect.Y);
        }

        /// <summary>Mount art under the rider for every mount but the hover-bike (drawn with the companions).</summary>
        private void DrawTravelMount(Graphics g, GameSimulation game, int startX, int startY, double now)
        {
            if (game.ActiveMountSpec is not { } spec || spec.Kind == MountKind.HoverBike)
                return;
            var motion = game.Player.Motion;
            var size = new Vector2(spec.DrawWidth, spec.DrawHeight);
            const float lift = 0.35f;
            var position = new Vector2(motion.Position.X + motion.Size.X / 2f - size.X / 2f, motion.Position.Y + motion.Size.Y - size.Y + lift);
            var moving = MathF.Abs(motion.Velocity.X) > 0.4f;
            var bob = spec.Kind switch
            {
                MountKind.RotorSkiff => (float)Math.Sin(now * 8) * 1.5f,
                MountKind.DuneStrider or MountKind.SpringHopper when moving && motion.Grounded => (float)Math.Abs(Math.Sin(now * 10)) * -2f,
                _ => 0f
            };
            DrawSpriteActor(g, spec.Sprite, position, size, 1f, motion.Facing < 0, startX, startY, bob);
        }

        private int PylonIconSize => (int)Math.Clamp(_mapZoom * 3f, 18f, 34f) + 4;

        /// <summary>Pylon icons on the fullscreen map; active ones are ringed, and travel hints show when one is in range.</summary>
        private void DrawPylonMapIcons(Graphics g, GameSimulation game)
        {
            if (Map is null)
                return;
            var size = PylonIconSize;
            var canTravel = game.PylonInRange is not null;
            foreach (var pylon in game.Pylons)
            {
                if (!Map.IsRevealed(pylon.Cell.X, pylon.Cell.Y))
                    continue;
                var at = MapToScreen(pylon.Cell.X + 0.5f, pylon.Cell.Y - 0.5f);
                if (pylon.Active)
                    g.DrawEllipse(_pylonActivePen, at.X - size / 2f - 2, at.Y - size / 2f - 2, size + 4, size + 4);
                if (_sprites.GetFitted(TravelContent.PylonSprite(pylon.Kind), size, size) is { } bitmap)
                    g.DrawImageUnscaled(bitmap, (int)(at.X - bitmap.Width / 2f), (int)(at.Y - bitmap.Height / 2f));
                else
                    g.FillEllipse(_pylonFallbackBrush, at.X - size / 4f, at.Y - size / 4f, size / 2f, size / 2f);
                if (!pylon.Active)
                    g.DrawString("dormant", _small, _dimTextBrush, at.X - size / 2f, at.Y + size / 2f);
            }
            if (canTravel)
                g.DrawString("Click a ringed pylon to travel there.", _small, _amberBrush, 24, 72);
        }

        /// <summary>Map click: teleports to the pylon under the cursor. True when a pylon icon was clicked.</summary>
        public bool TryMapPylonTeleport(GameSimulation game, Point point)
        {
            if (Map is null)
                return false;
            var size = PylonIconSize;
            foreach (var pylon in game.Pylons)
            {
                if (!Map.IsRevealed(pylon.Cell.X, pylon.Cell.Y))
                    continue;
                var at = MapToScreen(pylon.Cell.X + 0.5f, pylon.Cell.Y - 0.5f);
                if (MathF.Abs(point.X - at.X) > size / 2f || MathF.Abs(point.Y - at.Y) > size / 2f)
                    continue;
                if (game.TryPylonTeleport(pylon.Cell))
                {
                    FullscreenMapOpen = false;
                    Invalidate();
                }
                else
                    Notify(game.PylonInRange is null ? "Stand by an active pylon to travel." : $"The {pylon.Name.ToLowerInvariant()} cannot be reached.");
                return true;
            }
            return false;
        }
    }
}
