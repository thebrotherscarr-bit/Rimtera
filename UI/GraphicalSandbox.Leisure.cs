using System.Drawing;
using System.Numerics;
using Rimtera.Systems;
using Rimtera.World;

namespace Rimtera.UI;

public static partial class GraphicalSandbox
{
    /// <summary>
    /// World &amp; leisure drawing: paint tints on tiles and walls, glow-coated tiles drawn fullbright, kites on their lines,
    /// town pets, the golf ball and its swing charge, playing audio decks, plus the stroke and wave-moon score HUD.
    /// </summary>
    private sealed partial class GameCanvas
    {
        private readonly Dictionary<int, SolidBrush> _paintBrushes = new();
        private readonly Pen _kiteLine = new(Color.FromArgb(220, 235, 235, 225), 1f);
        private readonly SolidBrush _golfBallBrush = new(Color.WhiteSmoke);
        private readonly SolidBrush _chargeBrush = new(Color.FromArgb(230, 255, 190, 60));

        private SolidBrush PaintBrush(int color, int alpha)
        {
            var key = color * 1000 + alpha;
            if (!_paintBrushes.TryGetValue(key, out var brush))
            {
                var (_, r, g, b) = LeisureContent.PaintColors[Math.Clamp(color, 0, LeisureContent.PaintColors.Length - 1)];
                _paintBrushes[key] = brush = new SolidBrush(Color.FromArgb(alpha, r, g, b));
            }
            return brush;
        }

        /// <summary>Paint tint over one drawn cell (hooked from the tile loop of DrawWorld).</summary>
        private void DrawLeisureCell(Graphics g, GameSimulation game, int x, int y, in WorldCell cell, Rectangle rect)
        {
            if (game.PaintedCells.Count == 0)
                return;
            var point = new GridPoint(x, y);
            if (cell.TileId == TileId.Air)
            {
                if (cell.WallId != 0 && game.WallPaintAt(point) is var wall and > 0)
                    g.FillRectangle(PaintBrush(wall, 90), rect);
                return;
            }
            if (game.TilePaintAt(point) is var tile and > 0)
                g.FillRectangle(PaintBrush(tile, 120), rect);
        }

        /// <summary>Leisure overlays drawn after lighting (hooked once in DrawWorld).</summary>
        private void DrawLeisure(Graphics g, GameSimulation game, int startX, int startY, double now)
        {
            var size = TileSize;
            PointF Screen(Vector2 world) => new((world.X - startX) * size, (world.Y - startY) * size);

            // Glow coating: coated cells ignore the darkness.
            foreach (var (cell, value) in game.PaintedCells)
            {
                if ((value & 0x100) == 0 || cell.X < startX || cell.Y < startY || cell.X > startX + Width / size + 1 ||
                    cell.Y > startY + Height / size + 1)
                    continue;
                var worldCell = game.World.GetCell(cell);
                if (worldCell.TileId == TileId.Air)
                    continue;
                var rect = new Rectangle((cell.X - startX) * size, (cell.Y - startY) * size, size, size);
                DrawShapedTile(g, worldCell, cell.X, cell.Y, rect, game);
                if ((value & 0xF) is var tint and > 0)
                    g.FillRectangle(PaintBrush(tint, 120), rect);
            }

            // Town pets.
            foreach (var pet in game.TownPets)
            {
                DrawSpriteActor(g, pet.SpriteName, pet.Position, pet.Size, 1.2f, pet.Facing < 0, startX, startY,
                    pet.Moving ? (float)Math.Abs(Math.Sin(now * 10 + pet.Position.X)) * -2f : 0f);
                if (pet.HappySeconds > 0f)
                {
                    var above = Screen(pet.Position + new Vector2(pet.Size.X / 2f, -0.4f - (4f - pet.HappySeconds) * 0.2f));
                    DrawShadowed(g, "♥", _hud, Brushes.HotPink, above.X - 5, above.Y - 8);
                }
            }

            // Kite on its line.
            if (game.ActiveKite is { } kite)
            {
                var anchor = Screen(kite.Anchor);
                var at = Screen(kite.Position);
                var sag = new PointF((anchor.X + at.X) / 2f, (anchor.Y + at.Y) / 2f + size * 0.6f);
                g.DrawBezier(_kiteLine, anchor, sag, sag, at);
                var sprite = kite.Item switch
                {
                    ItemId.BoxKite => "item_box_kite",
                    ItemId.RibbonKite => "item_ribbon_kite",
                    _ => "item_delta_kite"
                };
                var flutter = (float)Math.Sin(now * 6) * 0.08f;
                DrawSpriteActor(g, sprite, kite.Position - new Vector2(0.6f, 0.6f + flutter), new Vector2(1.2f, 1.2f), 1f, kite.Position.X < kite.Anchor.X,
                    startX, startY);
            }

            // Golf ball and swing charge.
            if (game.GolfBall is { } ball)
            {
                var at = Screen(ball.Position);
                var r = Math.Max(3f, size * 0.18f);
                g.FillEllipse(_golfBallBrush, at.X - r, at.Y - r * 2f, r * 2f, r * 2f);
                g.DrawEllipse(Pens.DimGray, at.X - r, at.Y - r * 2f, r * 2f, r * 2f);
                if (ball.ChargingClub is not null)
                {
                    var bar = new RectangleF(at.X - 20, at.Y - r * 2f - 14, 40, 5);
                    g.FillRectangle(Brushes.Black, bar);
                    g.FillRectangle(_chargeBrush, bar.X, bar.Y, bar.Width * Math.Clamp(ball.Charge, 0f, 1f), bar.Height);
                }
            }

            // Playing audio decks show a note.
            if (game.MusicThemeOverride is not null && ((int)(now * 2) & 1) == 0)
            {
                var head = Screen(game.Player.Motion.Position + new Vector2(game.Player.Motion.Size.X / 2f, -0.6f));
                DrawShadowed(g, "♪", _hud, Brushes.LightSkyBlue, head.X + 10, head.Y - 6);
            }
        }

        /// <summary>Stroke counter and wave-moon score (hooked once in DrawWorld's HUD section).</summary>
        private void DrawLeisureHud(Graphics g, GameSimulation game)
        {
            var y = 96f;
            if (game.GolfBall is { } ball)
            {
                DrawShadowed(g, $"Strokes: {ball.Strokes}{(ball.Resting ? "" : "  (rolling)")}", _hud, Brushes.WhiteSmoke, Width / 2f - 60, y);
                y += 18;
            }
            foreach (var moon in game.MoonProgress)
            {
                var label = moon.Wave >= moon.Waves
                    ? $"{moon.Name}  ·  final wave  ·  {moon.Points}/{moon.PointsNeeded}"
                    : $"{moon.Name}  ·  wave {moon.Wave}/{moon.Waves}  ·  {moon.Points}/{moon.PointsNeeded} pts";
                DrawShadowed(g, label, _hud, Brushes.Orange, Width / 2f - 140, y);
                y += 18;
            }
            if (game.IsCreative)
                DrawShadowed(g, game.CreativeTimeFrozen ? "Creative  ·  time frozen" : "Creative", _small, Brushes.Plum, 12, y);
        }
    }

    private sealed partial class GameForm
    {
        /// <summary>Creative powers: research, duplication, time, spawns, godmode and difficulty (pause menu entry).</summary>
        private void OpenCreativeMenu()
        {
            if (_game is null || !_game.IsCreative)
                return;
            var game = _game;
            var entries = new List<MenuEntry>
            {
                new("Research selected item", () => { var status = game.ResearchSelected(); OpenCreativeMenu(); },
                    Description: "Submit copies of the selected item; once researched it can be duplicated freely."),
                new(game.CreativeTimeFrozen ? "Time: Frozen" : "Time: Flowing", () => { game.CreativeTimeFrozen = !game.CreativeTimeFrozen; OpenCreativeMenu(); },
                    Description: "Freeze or release the clock."),
                new("Jump to dawn", () => { game.SetTimeOfDay(6f); OpenCreativeMenu(); }),
                new("Jump to noon", () => { game.SetTimeOfDay(12f); OpenCreativeMenu(); }),
                new("Jump to dusk", () => { game.SetTimeOfDay(18.5f); OpenCreativeMenu(); }),
                new("Jump to midnight", () => { game.SetTimeOfDay(0f); OpenCreativeMenu(); }),
                new($"Spawn rate: {game.CreativeSpawnRate:0.00}×", Adjust: amount =>
                {
                    game.CreativeSpawnRate = Math.Clamp(game.CreativeSpawnRate + amount * 0.25f, 0f, 3f);
                    OpenCreativeMenu();
                }, Description: "Left and right: from 0× (none) to 3×."),
                new(game.CreativeGodmode ? "Godmode: On" : "Godmode: Off", () => { game.CreativeGodmode = !game.CreativeGodmode; OpenCreativeMenu(); }),
                new($"World difficulty: {WorldSettings.Name(game.Difficulty)}", Adjust: amount =>
                {
                    var values = Enum.GetValues<WorldDifficulty>();
                    var index = (Array.IndexOf(values, game.Difficulty) + amount + values.Length) % values.Length;
                    game.SetCreativeDifficulty(values[index]);
                    OpenCreativeMenu();
                }, Description: "Left and right change how hard creatures hit.")
            };
            if (game.ResearchedItems.Any())
                entries.Add(new MenuEntry("— Duplicate —", Enabled: false));
            foreach (var item in game.ResearchedItems.Take(40))
                entries.Add(new MenuEntry(game.Items[item].Name, () => { game.DuplicateItem(item); OpenCreativeMenu(); },
                    Description: "Adds a full stack to your inventory."));
            entries.Add(new MenuEntry("Back", CloseMenu));
            _menu.ShowDirect(new MenuScreen("creative", "Creative Powers", entries, "Enter  choose      ← →  change      Esc  back"), CloseMenu);
            _menuOpen = true;
            UpdateMenuViewport();
            SyncCanvas();
        }
    }
}
