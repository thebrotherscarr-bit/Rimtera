using System.ComponentModel;
using System.Drawing;
using System.Numerics;
using Rimtera.Systems;

namespace Rimtera.UI;

/// <summary>
/// The colonist bar and the inspect card (0.2.8). The bar sits along the top between the hotbar and the life panel
/// (UI/ColonistBar.cs decides what and where); the card sits under the hotbar and follows whoever was inspected
/// (Systems/GameSimulation.Inspect.cs builds it). Middle-click a character, or click a portrait, to inspect; middle-click
/// empty ground, click the card or press Escape to close it.
/// </summary>
public static partial class GraphicalSandbox
{
    private sealed partial class GameCanvas
    {
        private readonly List<(RectangleF Rect, long Id)> _colonistBarHits = new();
        private RectangleF _inspectCardRect;

        /// <summary>Whoever the inspect card follows; null when it is closed.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public InspectTarget? Inspecting { get; set; }

        /// <summary>The world point (in tiles) under a screen point.</summary>
        public Vector2 WorldPointAt(Point point) =>
            new(_viewStartX + point.X / (float)TileSize, _viewStartY + point.Y / (float)TileSize);

        /// <summary>The crew member whose portrait is under <paramref name="point"/>.</summary>
        public long? ColonistBarAt(Point point) =>
            _colonistBarHits.FirstOrDefault(hit => hit.Rect.Contains(point)) is { Rect.Width: > 0 } hit ? hit.Id : null;

        public bool InspectCardAt(Point point) => Inspecting is not null && _inspectCardRect.Contains(point);

        private void DrawColonyHud(Graphics g, GameSimulation game)
        {
            DrawColonistBar(g, game);
            DrawInspectCard(g, game);
            DrawLetters(g, game); // 0.6.3
        }

        // ---- 0.6.3: letters stack on the right edge; click one to read it, click it again (or the page) to put it away ----
        private readonly List<(RectangleF Rect, long Id)> _letterHits = new();
        private RectangleF _letterPageRect;

        /// <summary>The letter being read, or null.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public long? ReadingLetter { get; set; }

        /// <summary>The letter whose tab is under <paramref name="point"/>.</summary>
        public long? LetterAt(Point point) =>
            _letterHits.FirstOrDefault(hit => hit.Rect.Contains(point)) is { Rect.Width: > 0 } hit ? hit.Id : null;

        public bool LetterPageAt(Point point) => ReadingLetter is not null && _letterPageRect.Contains(point);

        private void DrawLetters(Graphics g, GameSimulation game)
        {
            _letterHits.Clear();
            _letterPageRect = RectangleF.Empty;
            var letters = game.Letters;
            if (ReadingLetter is { } reading && letters.All(letter => letter.Id != reading))
                ReadingLetter = null;
            if (letters.Count == 0)
                return;
            const float width = 230f, height = 26f;
            var x = Width - width - 10f;
            var y = Height - 96f - height;
            using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            // Newest at the bottom, nearest the eye; older ones climb.
            for (var i = letters.Count - 1; i >= 0; i--)
            {
                var letter = letters[i];
                var rect = new RectangleF(x, y, width, height);
                _letterHits.Add((rect, letter.Id));
                _hudRects.Add(rect);
                var open = ReadingLetter == letter.Id;
                using (var back = new SolidBrush(open ? Color.FromArgb(230, 40, 46, 58) : Color.FromArgb(210, 22, 26, 34)))
                    g.FillRectangle(back, rect);
                using (var stripe = new SolidBrush(LetterColor(letter.Kind)))
                    g.FillRectangle(stripe, rect.X, rect.Y, 5f, rect.Height);
                var countdown = letter.DueHour is null ? null : GameSimulation.Countdown(game.LetterHoursLeft(letter));
                var titleWidth = width - 14f - (countdown is null ? 0f : 44f);
                g.DrawString(letter.Title, _small, _hudBrush, new RectangleF(rect.X + 10f, rect.Y + 6f, titleWidth, 16f), format);
                if (countdown is not null)
                    using (var due = new SolidBrush(LetterColor(letter.Kind)))
                        g.DrawString(countdown, _small, due, rect.Right - 42f, rect.Y + 6f);
                y -= height + 4f;
            }
            if (ReadingLetter is not { } id || letters.FirstOrDefault(letter => letter.Id == id) is not { } page)
                return;
            // The page opens to the left of the stack, beside its tab.
            var tab = _letterHits.First(hit => hit.Id == id).Rect;
            const float pageWidth = 300f;
            var body = page.DueHour is null ? page.Body : $"{page.Body}\n\nDue in {GameSimulation.Countdown(game.LetterHoursLeft(page))}.";
            var bodySize = g.MeasureString(body, _small, (int)(pageWidth - 20f));
            var pageHeight = 34f + bodySize.Height + 22f;
            _letterPageRect = new RectangleF(tab.X - pageWidth - 8f, Math.Max(8f, tab.Bottom - pageHeight), pageWidth, pageHeight);
            _hudRects.Add(_letterPageRect);
            DrawRivetedPanel(g, _letterPageRect);
            using (var title = new SolidBrush(LetterColor(page.Kind)))
                g.DrawString(page.Title, _hud, title, new RectangleF(_letterPageRect.X + 10f, _letterPageRect.Y + 8f, pageWidth - 20f, 18f), format);
            g.DrawString(body, _small, _hudBrush, new RectangleF(_letterPageRect.X + 10f, _letterPageRect.Y + 30f, pageWidth - 20f, bodySize.Height + 2f));
            g.DrawString($"Day {page.Day} · click to put away", _small, _dimTextBrush, _letterPageRect.X + 10f, _letterPageRect.Bottom - 20f);
        }

        private static Color LetterColor(LetterKind kind) => kind switch
        {
            LetterKind.Threat => Color.FromArgb(230, 70, 64),
            LetterKind.BadNews => Color.FromArgb(236, 170, 60),
            LetterKind.Good => Color.FromArgb(96, 200, 110),
            _ => Color.FromArgb(160, 168, 182)
        };

        private void DrawColonistBar(Graphics g, GameSimulation game)
        {
            _colonistBarHits.Clear();
            var entries = ColonistBar.Entries(game);
            if (entries.Count == 0)
                return;
            var left = 10f + 10 * (HotbarSlot + 4) + 12f;
            var right = Width - MinimapWidth - 22f;
            var rects = ColonistBar.Layout(entries.Count, left, right);
            var mouse = MousePosition;
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var rect = rects[i];
                _colonistBarHits.Add((rect, entry.Id));
                _hudRects.Add(rect);
                var selected = Inspecting is { Kind: InspectKind.Colonist } target && target.Id == entry.Id;
                using (var back = new SolidBrush(Color.FromArgb(200, 22, 26, 34)))
                    g.FillRectangle(back, rect);
                var border = entry.Downed ? Color.FromArgb(230, 200, 60, 60) : entry.Breaking ? Color.FromArgb(230, 170, 90, 220)
                    : selected ? Color.FromArgb(240, 242, 176, 64) : Color.FromArgb(160, 120, 130, 150);
                using (var pen = new Pen(border, selected || entry.Downed || entry.Breaking ? 2f : 1f))
                    g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                var size = g.MeasureString(entry.Initials, _hud);
                DrawShadowed(g, entry.Initials, _hud, _hudBrush, rect.X + (rect.Width - size.Width) / 2f, rect.Y + rect.Height * 0.18f);
                // Health (green to red) and mood (blue) bars along the bottom of the portrait.
                var barWidth = rect.Width - 6f;
                using (var empty = new SolidBrush(Color.FromArgb(160, 50, 50, 56)))
                {
                    g.FillRectangle(empty, rect.X + 3f, rect.Bottom - 10f, barWidth, 3f);
                    g.FillRectangle(empty, rect.X + 3f, rect.Bottom - 5f, barWidth, 3f);
                }
                using (var health = new SolidBrush(Color.FromArgb(230, (int)(220 * (1f - entry.Health)), (int)(60 + 150 * entry.Health), 70)))
                    g.FillRectangle(health, rect.X + 3f, rect.Bottom - 10f, barWidth * entry.Health, 3f);
                using (var mood = new SolidBrush(Color.FromArgb(230, 90, 150, 240)))
                    g.FillRectangle(mood, rect.X + 3f, rect.Bottom - 5f, barWidth * entry.Mood, 3f);
                // The first name under the portrait; the hovered portrait also says what they are doing.
                var first = entry.Name.Split(' ')[0];
                var nameSize = g.MeasureString(first, _small);
                DrawShadowed(g, first, _small, _hudBrush, rect.X + (rect.Width - nameSize.Width) / 2f, rect.Bottom + 1f);
                if (mouse is { } point && rect.Contains(point))
                {
                    var tip = $"{entry.Name}: {entry.Activity}";
                    var tipSize = g.MeasureString(tip, _small);
                    DrawShadowed(g, tip, _small, _amberBrush, Math.Clamp(rect.X, 4f, Width - tipSize.Width - 4f), rect.Bottom + 16f);
                }
            }
        }

        private void DrawInspectCard(Graphics g, GameSimulation game)
        {
            _inspectCardRect = RectangleF.Empty;
            if (Inspecting is not { } target)
                return;
            if (game.Inspect(target) is not { } card)
            {
                Inspecting = null; // dead or gone
                return;
            }
            const float width = 300f, line = 15f;
            var height = 52f + card.Lines.Count * line + 8f;
            var rect = new RectangleF(10f, HotbarTop + HotbarSlot + 34f, width, height);
            _inspectCardRect = rect;
            _hudRects.Add(rect);
            DrawRivetedPanel(g, rect);
            DrawShadowed(g, card.Title, _hud, _amberBrush, rect.X + 10f, rect.Y + 8f);
            DrawShadowed(g, card.Subtitle, _small, _hudBrush, rect.X + 10f, rect.Y + 24f);
            DrawShadowed(g, "×", _hud, _hudBrush, rect.Right - 18f, rect.Y + 6f);
            var barY = rect.Y + 41f;
            var barWidth = (width - 30f) / (card.Mood is null ? 1f : 2f);
            using (var empty = new SolidBrush(Color.FromArgb(160, 50, 50, 56)))
            {
                g.FillRectangle(empty, rect.X + 10f, barY, barWidth, 4f);
                if (card.Mood is not null)
                    g.FillRectangle(empty, rect.X + 20f + barWidth, barY, barWidth, 4f);
            }
            using (var health = new SolidBrush(Color.FromArgb(230, 90, 200, 90)))
                g.FillRectangle(health, rect.X + 10f, barY, barWidth * Math.Clamp(card.Health, 0f, 1f), 4f);
            if (card.Mood is { } moodValue)
                using (var mood = new SolidBrush(Color.FromArgb(230, 90, 150, 240)))
                    g.FillRectangle(mood, rect.X + 20f + barWidth, barY, barWidth * Math.Clamp(moodValue, 0f, 1f), 4f);
            for (var i = 0; i < card.Lines.Count; i++)
                DrawShadowed(g, card.Lines[i], _small, _hudBrush, rect.X + 10f, rect.Y + 52f + i * line);
        }
    }
}
