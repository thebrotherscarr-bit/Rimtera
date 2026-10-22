using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;
using System.Windows.Forms;
using Rimtera.Entities;
using Rimtera.Systems;

namespace Rimtera.UI;

public static partial class GraphicalSandbox
{
    /// <summary>Emote input: hold the emote key (` by default) and press 1–8 to pick one; a tap on its own waves.</summary>
    private sealed partial class GameForm
    {
        private bool _emotePicked;

        /// <summary>Key-down hook: true when the key was an emote input.</summary>
        private bool HandleEmoteKey(KeyEventArgs e)
        {
            if (_game is null)
                return false;
            if (IsBoundTo("Emote", e.KeyCode))
            {
                if (!_canvas.EmotePaletteOpen)
                    _emotePicked = false;
                _canvas.EmotePaletteOpen = true;
                e.Handled = true;
                return true;
            }
            if (!Held("Emote") || e.KeyCode is < Keys.D1 or > Keys.D8)
                return false;
            _game.PlayerEmote((EmoteKind)(e.KeyCode - Keys.D1));
            _emotePicked = true;
            e.Handled = true;
            e.SuppressKeyPress = true;
            return true;
        }

        /// <summary>Key-up hook: closes the emote palette (a bare tap waves).</summary>
        private void HandleEmoteKeyUp(Keys key)
        {
            if (!IsBoundTo("Emote", key) || !_canvas.EmotePaletteOpen)
                return;
            _canvas.EmotePaletteOpen = false;
            if (!_emotePicked)
                _game?.PlayerEmote(EmoteKind.Wave);
        }
    }

    /// <summary>
    /// Drawing for the player and combat systems: affliction tints and icons over creatures, emote bubbles, the emote
    /// palette, the luck/aggro readout and the dye tint on the colonist's suit.
    /// </summary>
    private sealed partial class GameCanvas
    {
        /// <summary>True while the emote key is held (the palette shows over the player).</summary>
        internal bool EmotePaletteOpen;

        private static readonly string[] EmoteGlyphs = ["o/", ":)", "XD", "<3", ":(", ">:(", "?", "!", "nom?", "!!", "zZ", "~♪"];
        private static readonly Color[] EmoteColors =
        [
            Color.FromArgb(120, 200, 255), Color.FromArgb(255, 220, 90), Color.FromArgb(255, 190, 70), Color.FromArgb(255, 110, 150),
            Color.FromArgb(120, 150, 220), Color.FromArgb(240, 80, 60), Color.FromArgb(200, 200, 210), Color.FromArgb(255, 170, 40),
            Color.FromArgb(220, 160, 100), Color.FromArgb(190, 120, 255), Color.FromArgb(150, 160, 200), Color.FromArgb(140, 240, 180)
        ];

        private static Color AfflictionColor(StatusId id) => id switch
        {
            StatusId.Burning => Color.FromArgb(255, 120, 30),
            StatusId.Poison => Color.FromArgb(120, 220, 60),
            StatusId.Frostbite => Color.FromArgb(120, 200, 255),
            StatusId.ArmorShred => Color.FromArgb(200, 200, 210),
            StatusId.Confused => Color.FromArgb(220, 120, 255),
            StatusId.Marked => Color.FromArgb(255, 70, 90),
            _ => Color.White
        };

        private static string AfflictionGlyph(StatusId id) => id switch
        {
            StatusId.Burning => "F", StatusId.Poison => "P", StatusId.Frostbite => "*", StatusId.ArmorShred => "S",
            StatusId.Confused => "?", StatusId.Marked => "X", _ => "·"
        };

        private void DrawPlayerSystems(Graphics g, GameSimulation game, int startX, int startY, double now)
        {
            DrawAfflictions(g, game, startX, startY, now);
            DrawEmotes(g, game, startX, startY);
            if (EmotePaletteOpen && !MenuOpen)
                DrawEmotePalette(g, game, startX, startY);
            if (!MenuOpen)
                DrawLuckAggro(g, game);
        }

        private void DrawAfflictions(Graphics g, GameSimulation game, int startX, int startY, double now)
        {
            foreach (var enemy in game.Enemies)
            {
                if (!enemy.IsAlive || enemy.Afflictions.Count == 0 || enemy.Disguised)
                    continue;
                var box = new RectangleF((enemy.Motion.Position.X - startX) * TileSize, (enemy.Motion.Position.Y - startY) * TileSize,
                    enemy.Motion.Size.X * TileSize, enemy.Motion.Size.Y * TileSize);
                if (box.Right < 0 || box.Bottom < 0 || box.Left > Width || box.Top > Height)
                    continue;
                // A soft pulsing tint in the colour of the strongest affliction.
                var first = enemy.Afflictions.Keys.OrderBy(id => (int)id).First();
                var pulse = (int)(40 + 25 * Math.Sin(now * 6 + enemy.Id));
                using (var tint = new SolidBrush(Color.FromArgb(pulse, AfflictionColor(first))))
                    g.FillEllipse(tint, box.X - 2, box.Y - 2, box.Width + 4, box.Height + 4);
                // One small icon per affliction above the creature (above its health bar).
                var icons = enemy.Afflictions.Keys.OrderBy(id => (int)id).ToArray();
                const float size = 11f;
                var x = box.X + box.Width / 2f - icons.Length * (size + 2f) / 2f;
                var y = box.Y - size - 12f;
                foreach (var id in icons)
                {
                    using var fill = new SolidBrush(Color.FromArgb(220, AfflictionColor(id)));
                    g.FillEllipse(fill, x, y, size, size);
                    g.DrawEllipse(Pens.Black, x, y, size, size);
                    var glyph = AfflictionGlyph(id);
                    var measure = g.MeasureString(glyph, _small);
                    g.DrawString(glyph, _small, Brushes.Black, x + (size - measure.Width) / 2f + 0.5f, y + (size - measure.Height) / 2f);
                    x += size + 2f;
                }
            }
        }

        private void DrawEmotes(Graphics g, GameSimulation game, int startX, int startY)
        {
            foreach (var bubble in game.Emotes)
            {
                var glyph = EmoteGlyphs[(int)bubble.Kind % EmoteGlyphs.Length];
                var color = EmoteColors[(int)bubble.Kind % EmoteColors.Length];
                var fade = Math.Clamp(bubble.Remaining / 0.4f, 0f, 1f);
                var rise = Math.Clamp((GameSimulation.EmoteSeconds - bubble.Remaining) / 0.25f, 0f, 1f);
                var measure = g.MeasureString(glyph, _hud);
                var width = Math.Max(26f, measure.Width + 10f);
                var height = measure.Height + 6f;
                var cx = (bubble.Anchor.X - startX) * TileSize;
                var bottom = (bubble.Anchor.Y - startY) * TileSize - 4f - rise * 6f;
                var box = new RectangleF(cx - width / 2f, bottom - height, width, height);
                using var path = RoundedBubble(box, 6f, cx, bottom + 6f);
                using var fill = new SolidBrush(Color.FromArgb((int)(230 * fade), 250, 250, 255));
                using var edge = new Pen(Color.FromArgb((int)(255 * fade), color), 2f);
                using var text = new SolidBrush(Color.FromArgb((int)(255 * fade), Color.FromArgb(color.R / 2, color.G / 2, color.B / 2)));
                g.FillPath(fill, path);
                g.DrawPath(edge, path);
                g.DrawString(glyph, _hud, text, box.X + (box.Width - measure.Width) / 2f, box.Y + 3f);
            }
        }

        private static GraphicsPath RoundedBubble(RectangleF box, float radius, float tailX, float tailY)
        {
            var path = new GraphicsPath();
            var d = radius * 2f;
            path.AddArc(box.X, box.Y, d, d, 180, 90);
            path.AddArc(box.Right - d, box.Y, d, d, 270, 90);
            path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90);
            path.AddLine(tailX + 4f, box.Bottom, tailX, tailY);
            path.AddLine(tailX, tailY, tailX - 4f, box.Bottom);
            path.AddArc(box.X, box.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void DrawEmotePalette(Graphics g, GameSimulation game, int startX, int startY)
        {
            var center = game.Player.Motion.Position + game.Player.Motion.Size / 2f;
            var cx = (center.X - startX) * TileSize;
            var cy = (center.Y - startY) * TileSize;
            const float radius = 78f;
            using var back = new SolidBrush(Color.FromArgb(170, 12, 18, 28));
            for (var i = 0; i < 8; i++)
            {
                var angle = -MathF.PI / 2f + i * MathF.Tau / 8f;
                var x = cx + MathF.Cos(angle) * radius;
                var y = cy + MathF.Sin(angle) * radius;
                var box = new RectangleF(x - 22f, y - 14f, 44f, 28f);
                g.FillRectangle(back, box);
                using var edge = new Pen(EmoteColors[i], 1.5f);
                g.DrawRectangle(edge, box.X, box.Y, box.Width, box.Height);
                g.DrawString($"{i + 1}", _small, Brushes.Gray, box.X + 2f, box.Y + 1f);
                var glyph = EmoteGlyphs[i];
                var measure = g.MeasureString(glyph, _hud);
                using var text = new SolidBrush(EmoteColors[i]);
                g.DrawString(glyph, _hud, text, box.X + (box.Width - measure.Width) / 2f + 4f, box.Y + (box.Height - measure.Height) / 2f);
            }
        }

        private void DrawLuckAggro(Graphics g, GameSimulation game)
        {
            var luck = game.Luck;
            var aggro = game.PlayerAggro;
            if (MathF.Abs(luck) < 0.001f && aggro == 0)
                return;
            var parts = new List<string>();
            if (MathF.Abs(luck) >= 0.001f)
                parts.Add($"Luck {luck:+0.00;-0.00}");
            if (aggro != 0)
                parts.Add($"Aggro {aggro:+0;-0}");
            var line = string.Join("   ", parts);
            var y = Height - HudHeight - 24f;
            using var shadow = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
            g.DrawString(line, _small, shadow, 13f, y + 1f);
            using var text = new SolidBrush(luck >= 0f ? Color.FromArgb(150, 240, 170) : Color.FromArgb(240, 150, 150));
            g.DrawString(line, _small, text, 12f, y);
        }

        /// <summary>The colonist's body frame with its dye: the body dye (else head, else legs) recolours the suit.</summary>
        private string DyedLook(GameSimulation game, string look)
        {
            if ((game.DyeTint(DyeSlot.Body) ?? game.DyeTint(DyeSlot.Head) ?? game.DyeTint(DyeSlot.Legs)) is not { } rgb)
                return look;
            var dye = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            return _sprites.Variant(look, $"dye{rgb:X6}", color =>
            {
                if (color.A == 0)
                    return color;
                var max = Math.Max(color.R, Math.Max(color.G, color.B)) / 255f;
                var min = Math.Min(color.R, Math.Min(color.G, color.B)) / 255f;
                var saturation = max <= 0f ? 0f : (max - min) / max;
                var hue = color.GetHue();
                var skin = hue is >= 12f and <= 40f && saturation is > 0.2f and <= 0.58f && max > 0.5f;
                if (skin || saturation < 0.3f || max < 0.2f)
                    return color;
                var shade = Math.Clamp(max / 0.8f, 0.25f, 1.3f);
                return Color.FromArgb(color.A, Math.Clamp((int)(dye.R * shade), 0, 255), Math.Clamp((int)(dye.G * shade), 0, 255),
                    Math.Clamp((int)(dye.B * shade), 0, 255));
            });
        }

        /// <summary>Icons for the boss caches, trophies and relics (dynamic ids): a placeholder sprite tinted per boss.</summary>
        internal static Func<ItemId, string, string> PlayerSystemsItemArt(SpriteAtlas atlas, Func<ItemId, string, string> inner) => (item, named) =>
        {
            string? baseSprite = PlayerSystemsContent.BagBoss(item) is not null ? "item_salvage_cache"
                : PlayerSystemsContent.TrophyBoss(item) is not null ? "deco_boss_trophy"
                : PlayerSystemsContent.RelicBoss(item) is not null ? "deco_boss_relic"
                : null;
            if (baseSprite is null || atlas.Find(baseSprite) is not { } found)
                return inner(item, named);
            var hue = ((int)item * 47) % 360;
            var tint = HueColor(hue);
            return atlas.Variant(found, $"boss{(int)item}", color =>
            {
                if (color.A == 0 || color.GetSaturation() < 0.25f)
                    return color;
                var value = Math.Max(color.R, Math.Max(color.G, color.B)) / 255f;
                return Color.FromArgb(color.A, (int)(tint.R * value), (int)(tint.G * value), (int)(tint.B * value));
            });
        };

        private static Color HueColor(int hue)
        {
            var h = hue / 60f;
            var x = 1f - MathF.Abs(h % 2f - 1f);
            var (r, g, b) = (int)h switch { 0 => (1f, x, 0f), 1 => (x, 1f, 0f), 2 => (0f, 1f, x), 3 => (0f, x, 1f), 4 => (x, 0f, 1f), _ => (1f, 0f, x) };
            return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
        }
    }
}
