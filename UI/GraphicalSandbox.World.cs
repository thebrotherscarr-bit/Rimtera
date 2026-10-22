using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using System.Windows.Forms;
using Rimtera.Entities;
using Rimtera.Systems;
using Rimtera.World;

namespace Rimtera.UI;

public static partial class GraphicalSandbox
{
    /// <summary>
    /// World drawing for shaped and actuated blocks, platforms, ropes, trees, plants, wiring, loose objects
    /// (falling blocks, explosives, ground items), pets, the mount, the grappling hook and worm bodies,
    /// plus the buff bar and breath bubbles.
    /// </summary>
    private sealed partial class GameCanvas
    {
        private static readonly Color[] WireColors =
        {
            Color.FromArgb(230, 60, 60), Color.FromArgb(70, 140, 255), Color.FromArgb(80, 220, 90), Color.FromArgb(240, 210, 60)
        };

        private readonly Brush _barkBrush = new SolidBrush(Color.FromArgb(104, 74, 48));
        private readonly Brush _barkDarkBrush = new SolidBrush(Color.FromArgb(70, 48, 32));
        private readonly Brush _plankBrush = new SolidBrush(Color.FromArgb(170, 124, 76));
        private readonly Brush _metalPlatformBrush = new SolidBrush(Color.FromArgb(150, 160, 175));
        private readonly Brush _cactusBrush = new SolidBrush(Color.FromArgb(70, 150, 70));
        private readonly Brush _inactiveShade = new SolidBrush(Color.FromArgb(150, 10, 12, 20));
        private readonly Pen _ropePen = new(Color.FromArgb(196, 160, 110), 3f);
        private readonly Pen _ladderPen = new(Color.FromArgb(150, 110, 70), 3f);
        private readonly Pen _grapplePen = new(Color.FromArgb(200, 200, 210), 2f);
        private readonly Pen _devicePen = new(Color.FromArgb(220, 30, 34, 44), 2f);

        /// <summary>Blocks that leave part of the cell open show the wall behind them.</summary>
        private static bool ShowsWallBehind(in WorldCell cell) =>
            cell.Shape != TileShape.Full || cell.IsInactive || TilePhysics.IsPlatform(cell.TileId) ||
            TilePhysics.IsClimbable(cell.TileId) || TilePhysics.IsTreePart(cell.TileId) || TilePhysics.IsHerb(cell.TileId) ||
            TilePhysics.IsWiringDevice(cell.TileId) || cell.TileId is TileId.Cactus or TileId.Sapling or TileId.GiantLeaf ||
            FloraParity.IsGrassPlant(cell.TileId);

        /// <summary>Tiles drawn from primitives or at a special size. Returns false to fall back to sprites and props.</summary>
        private bool DrawSpecialTile(Graphics g, in WorldCell cell, int x, int y, Rectangle rect, GameSimulation game)
        {
            var tile = cell.TileId;
            var s = rect.Width;
            if (TilePhysics.IsPlatform(tile))
            {
                var brush = tile == TileId.MetalPlatform ? _metalPlatformBrush : _plankBrush;
                g.FillRectangle(brush, rect.X, rect.Y, s, Math.Max(3, s / 3));
                g.FillRectangle(_barkDarkBrush, rect.X, rect.Y + Math.Max(3, s / 3) - 1, s, 1);
                return true;
            }
            if (tile is TileId.Rope or TileId.Vine)
            {
                var pen = tile == TileId.Vine ? _vinePen : _ropePen;
                var sway = tile == TileId.Vine ? MathF.Sin((float)(Environment.TickCount64 / 700.0) + y * 0.7f) * s * 0.08f : 0f;
                g.DrawLine(pen, rect.X + s / 2f + sway, rect.Y, rect.X + s / 2f + sway, rect.Bottom);
                if (tile == TileId.Vine && (x + y) % 2 == 0)
                    g.FillEllipse(_cactusBrush, rect.X + s / 2f + sway - s * 0.25f, rect.Y + s * 0.35f, s * 0.3f, s * 0.22f);
                return true;
            }
            if (tile == TileId.Ladder)
            {
                g.DrawLine(_ladderPen, rect.X + s * 0.2f, rect.Y, rect.X + s * 0.2f, rect.Bottom);
                g.DrawLine(_ladderPen, rect.X + s * 0.8f, rect.Y, rect.X + s * 0.8f, rect.Bottom);
                g.DrawLine(_ladderPen, rect.X + s * 0.2f, rect.Y + s * 0.5f, rect.X + s * 0.8f, rect.Y + s * 0.5f);
                return true;
            }
            if (TilePhysics.IsTrunk(tile))
            {
                var inset = tile is TileId.PalmTrunk or TileId.PalmwoodTrunk ? s * 0.3f : s * 0.18f;
                using (var fillBark = ContentFillFlora.BarkColor(tile) is { } bark ? new SolidBrush(Color.FromArgb(bark.R, bark.G, bark.B)) : null)
                    g.FillRectangle(fillBark ?? _barkBrush, rect.X + inset, rect.Y, s - inset * 2, s);
                g.FillRectangle(_barkDarkBrush, rect.X + inset + (s - inset * 2) * ((x * 7 + y * 3) % 5) / 6f, rect.Y + 2, 2, s - 4);
                return true;
            }
            if (TilePhysics.IsCanopy(tile) || tile == TileId.GiantLeaf)
            {
                var leaf = ContentFillFlora.CanopyColor(tile) is { } fillLeaf ? Color.FromArgb(fillLeaf.R, fillLeaf.G, fillLeaf.B) : tile switch
                {
                    TileId.PineCanopy => Color.FromArgb(40, 110, 70),
                    TileId.PalmCanopy => Color.FromArgb(90, 170, 70),
                    TileId.GiantLeaf => Color.FromArgb(60, 150, 80),
                    _ => Color.FromArgb(60, 140, 60)
                };
                using var brush = new SolidBrush(leaf);
                using var light = new SolidBrush(ControlPaint.Light(leaf, 0.25f));
                var grow = s * 0.25f;
                g.FillEllipse(brush, rect.X - grow, rect.Y - grow, s + grow * 2, s + grow * 2);
                g.FillEllipse(light, rect.X + s * 0.15f, rect.Y + s * 0.1f, s * 0.45f, s * 0.35f);
                return true;
            }
            if (tile == TileId.Cactus)
            {
                g.FillRectangle(_cactusBrush, rect.X + s * 0.25f, rect.Y, s * 0.5f, s);
                using var spine = new Pen(Color.FromArgb(200, 230, 200), 1f);
                g.DrawLine(spine, rect.X + s * 0.38f, rect.Y + 2, rect.X + s * 0.38f, rect.Bottom - 2);
                return true;
            }
            if (FloraParity.IsGrassPlant(tile))
            {
                // Flora parity: grass plants take a sprite from the ground they grow on, fixed per cell.
                var options = SpriteCatalog.PlantSpritesFor(game.World.TileAt(x, y + 1, TileId.Air)) ?? SpriteCatalog.PlantSpritesFor(TileId.Grass)!;
                var hash = unchecked((uint)(x * 73856093) ^ (uint)(y * 19349663));
                hash ^= hash >> 13;
                hash *= 0x5bd1e995;
                var sprite = options[(int)((hash >> 8) % (uint)options.Length)];
                var tall = tile == TileId.TallGrassPlant;
                var height = (int)(SpriteCatalog.DecorationHeight(sprite) * s * (tall ? 1f : 0.6f));
                DrawPropOnGround(g, sprite, rect.X + s / 2f, rect.Bottom, (int)(s * (tall ? 1.8f : 1.2f)), Math.Max(4, height), (hash & 1) == 0);
                return true;
            }
            if (TilePhysics.IsHerb(tile) || tile == TileId.Sapling)
            {
                var mature = tile == TileId.Sapling || (cell.State & 1) != 0;
                var sprite = tile == TileId.Sapling ? "deco_sapling" : "deco_" + SpriteCatalog.SnakeCase(tile.ToString());
                var size = (int)(s * (mature ? 1.2f : 0.6f));
                DrawPropOnGround(g, mature ? sprite : "deco_sapling", rect.X + s / 2f, rect.Bottom, size, size);
                if (TilePhysics.IsHerb(tile) && mature && game.IsHerbBlooming(new GridPoint(x, y)) && _menu.Preferences.FlashyEffects)
                {
                    var twinkle = (int)(120 + 100 * MathF.Sin((float)(Environment.TickCount64 / 200.0) + x));
                    using var bloom = new SolidBrush(Color.FromArgb(Math.Clamp(twinkle, 0, 255), 255, 255, 200));
                    g.FillEllipse(bloom, rect.X + s * 0.45f, rect.Y + s * 0.1f, 3, 3);
                }
                return true;
            }
            if (TilePhysics.IsWiringDevice(tile) && _sprites.Find(SpriteCatalog.ForTile(tile)) is null)
            {
                DrawWiringDevice(g, cell, rect);
                return true;
            }
            return false;
        }

        private readonly Pen _vinePen = new(Color.FromArgb(60, 140, 60), 3f);

        /// <summary>Switches, levers, plates, timers and gates as small readable panels.</summary>
        private void DrawWiringDevice(Graphics g, in WorldCell cell, Rectangle rect)
        {
            var s = rect.Width;
            var on = (cell.State & 1) != 0;
            var tile = cell.TileId;
            if (tile == TileId.PressurePlate)
            {
                g.FillRectangle(on ? _amberBrush : _dimTextBrush, rect.X + 2, rect.Bottom - 4, s - 4, 4);
                return;
            }
            var panel = new RectangleF(rect.X + s * 0.15f, rect.Y + s * 0.15f, s * 0.7f, s * 0.7f);
            using (var back = new SolidBrush(Color.FromArgb(70, 76, 90)))
                g.FillRectangle(back, panel);
            g.DrawRectangle(_devicePen, panel.X, panel.Y, panel.Width, panel.Height);
            using var lit = new SolidBrush(on ? Color.FromArgb(120, 255, 140) : Color.FromArgb(150, 50, 50));
            switch (tile)
            {
                case TileId.Switch:
                    g.FillRectangle(lit, panel.X + panel.Width * 0.35f, panel.Y + (on ? 2 : panel.Height * 0.5f), panel.Width * 0.3f, panel.Height * 0.45f);
                    break;
                case TileId.Lever:
                    using (var arm = new Pen(Color.FromArgb(220, 220, 230), 2f))
                        g.DrawLine(arm, panel.X + panel.Width / 2f, panel.Bottom - 2, panel.X + (on ? panel.Width - 2 : 2), panel.Y + 2);
                    g.FillEllipse(lit, panel.X + panel.Width / 2f - 2, panel.Bottom - 4, 4, 4);
                    break;
                default:
                    var label = tile switch
                    {
                        TileId.Timer1s => "1s", TileId.Timer3s => "3s", TileId.Timer5s => "5s",
                        TileId.GateAnd => "&", TileId.GateOr => "|", TileId.GateNot => "!", TileId.GateXor => "^",
                        TileId.TeleporterPad => "»", _ => "•"
                    };
                    g.FillEllipse(lit, panel.Right - 5, panel.Y + 1, 4, 4);
                    g.DrawString(label, _small, _hudBrush, panel.X, panel.Y + panel.Height * 0.2f);
                    break;
            }
        }

        private TileArt? _tileArt;

        /// <summary>True when the cell next to a block leaves that side of it showing (air, liquid, non-solid, shaped or actuated).</summary>
        private static bool OpenAt(GameSimulation game, int x, int y)
        {
            if (!game.World.Contains(x, y))
                return false;
            var cell = game.World.GetCell(x, y);
            return cell.TileId == TileId.Air || cell.IsInactive || !game.Tiles.Contains(cell.TileId) || !game.Tiles[cell.TileId].IsSolid ||
                   cell.Shape != TileShape.Full && TilePhysics.IsShapeable(cell.TileId, game.Tiles);
        }

        /// <summary>
        /// A block: full blocks use shaped art (exposed-edge outlines, rim light, rounded natural corners), softer
        /// neighbouring materials bleed in along jagged edges, and grassy tops grow a fringe. Slopes and half blocks
        /// are clipped; actuated (inactive) blocks are dimmed.
        /// </summary>
        private void DrawShapedTile(Graphics g, in WorldCell cell, int x, int y, Rectangle rect, GameSimulation game)
        {
            var sprite = SpriteCatalog.ForTile(cell.TileId);
            var art = _tileArt ??= new TileArt(_sprites);
            art.UseSize(TileSize);
            var shaped = cell.Shape != TileShape.Full && TilePhysics.IsShapeable(cell.TileId, game.Tiles);
            if (!shaped)
            {
                var exposed = (OpenAt(game, x, y - 1) ? TileArt.North : 0) | (OpenAt(game, x + 1, y) ? TileArt.East : 0)
                            | (OpenAt(game, x, y + 1) ? TileArt.South : 0) | (OpenAt(game, x - 1, y) ? TileArt.West : 0);
                if (art.Body(cell.TileId, sprite, exposed) is { } body)
                    g.DrawImageUnscaled(body, rect.X, rect.Y);
                else
                    DrawTileSprite(g, sprite, cell.TileId, rect, TileSize);
                // Softer neighbours (soil, sand, snow) reach into harder ones (rock) along a jagged edge.
                var softness = art.Softness(cell.TileId);
                if (softness > 0)
                {
                    var variant = (int)((uint)(x * 73856093 ^ y * 19349663) % 4);
                    foreach (var (dx, dy, side) in new[] { (0, -1, TileArt.North), (1, 0, TileArt.East), (0, 1, TileArt.South), (-1, 0, TileArt.West) })
                    {
                        if ((exposed & side) != 0 || !game.World.Contains(x + dx, y + dy))
                            continue;
                        var neighbour = game.World.GetCell(x + dx, y + dy).TileId;
                        if (neighbour == cell.TileId || art.Softness(neighbour) <= softness)
                            continue;
                        if (art.Bleed(neighbour, SpriteCatalog.ForTile(neighbour), side, variant) is { } bleed)
                            g.DrawImageUnscaled(bleed, rect.X, rect.Y);
                    }
                }
                if ((exposed & TileArt.North) != 0 && TileArt.IsGrassy(cell.TileId) && game.World.Contains(x, y - 1) &&
                    game.World.GetCell(x, y - 1).LiquidDepth < 0.05f && art.Fringe(cell.TileId, sprite, (x * 7 + y) & 3) is { } fringe)
                    g.DrawImageUnscaled(fringe, rect.X, rect.Y - fringe.Height);
            }
            else
            {
                var state = g.Save();
                using var path = new GraphicsPath();
                path.AddPolygon(ShapePolygon(cell.Shape, rect));
                g.SetClip(path, CombineMode.Intersect);
                DrawTileSprite(g, sprite, cell.TileId, rect, TileSize);
                g.Restore(state);
            }
            if (cell.IsInactive)
                g.FillRectangle(_inactiveShade, rect);
        }

        private static PointF[] ShapePolygon(TileShape shape, Rectangle r)
        {
            PointF P(float fx, float fy) => new(r.X + fx * r.Width, r.Y + fy * r.Height);
            return shape switch
            {
                TileShape.Half => new[] { P(0, 0.5f), P(1, 0.5f), P(1, 1), P(0, 1) },
                TileShape.SlopeUpRight => new[] { P(0, 1), P(1, 0), P(1, 1) },
                TileShape.SlopeUpLeft => new[] { P(0, 0), P(1, 1), P(0, 1) },
                TileShape.CeilingRight => new[] { P(0, 0), P(1, 0), P(1, 1) },
                TileShape.CeilingLeft => new[] { P(0, 0), P(1, 0), P(0, 1) },
                _ => new[] { P(0, 0), P(1, 0), P(1, 1), P(0, 1) }
            };
        }

        /// <summary>Falling blocks, lit explosives and dropped items lying in the world.</summary>
        private void DrawLooseObjects(Graphics g, GameSimulation game, int startX, int startY, double now)
        {
            foreach (var block in game.FallingBlocks)
            {
                var rect = new Rectangle((int)((block.X - startX) * TileSize), (int)((block.Y - startY) * TileSize), TileSize, TileSize);
                DrawTileSprite(g, SpriteCatalog.ForTile(block.Tile), block.Tile, rect, TileSize);
            }

            foreach (var bomb in game.Explosives)
            {
                var size = (int)(TileSize * 0.9f);
                var px = (bomb.Position.X - startX) * TileSize - size / 2f;
                var py = (bomb.Position.Y - startY) * TileSize - size / 2f;
                if (_sprites.GetFitted(SpriteCatalog.ForItem(bomb.Kind), size, size) is { } art)
                    g.DrawImage(art, px, py, art.Width, art.Height);
                else
                    g.FillEllipse(_barkDarkBrush, px, py, size, size);
                // Fuse spark blinks faster as it burns down.
                var rate = bomb.FuseSeconds < 1f ? 16.0 : 6.0;
                if (((int)(now * rate) & 1) == 0)
                    g.FillEllipse(_amberBrush, px + size * 0.6f, py - 3, 5, 5);
            }

            foreach (var item in game.GroundItems)
            {
                var size = (int)(TileSize * 0.8f);
                var bob = item.Grounded ? MathF.Sin((float)now * 2.5f + item.Id) * 1.5f : 0f;
                var px = (item.Position.X - startX) * TileSize - size / 2f;
                var py = (item.Position.Y - startY) * TileSize - size + bob;
                if (_sprites.GetFitted(SpriteCatalog.ForItem(item.ItemId), size, size) is { } art)
                    g.DrawImage(art, px + (size - art.Width) / 2f, py + (size - art.Height), art.Width, art.Height);
                else
                    g.FillRectangle(_amberBrush, px + size / 4f, py + size / 2f, size / 2f, size / 2f);
                if (item.Count > 1)
                    g.DrawString(item.Count.ToString(), _small, _hudBrush, px + size - 6, py + size - 10);
            }
        }

        /// <summary>Pets follow the player; the hover-bike sits under the colonist while mounted.</summary>
        private void DrawCompanions(Graphics g, GameSimulation game, int startX, int startY, double now)
        {
            foreach (var pet in new[] { game.ActivePet, game.ActiveLightPet })
            {
                if (pet is null)
                    continue;
                var bob = pet.Flies ? (float)Math.Sin(now * 3 + (pet.IsLightPet ? 1 : 0)) * 3f : 0f;
                var size = new Vector2(0.9f, 0.9f);
                var petMoving = Vector2.Distance(pet.Position, game.Player.Motion.Position) > 1.5f;
                DrawSpriteActor(g, SpriteCatalog.AnimatedFrame(pet.SpriteName, petMoving, false, now, (long)pet.SourceItem),
                    pet.Position - size / 2f, size, 1.1f, pet.Facing < 0, startX, startY, bob);
            }
            if (game.ActiveMount == MountKind.HoverBike)
            {
                var motion = game.Player.Motion;
                var size = new Vector2(2.6f, 1.4f);
                var position = new Vector2(motion.Position.X + motion.Size.X / 2f - size.X / 2f, motion.Position.Y + motion.Size.Y - size.Y + 0.3f);
                DrawSpriteActor(g, "mount_hover_bike", position, size, 1f, motion.Facing < 0, startX, startY,
                    bob: (float)Math.Sin(now * 5) * 1.5f);
            }
            // Grapple hooks are fired from the hand: drawn with the arm in DrawHeldItem (GraphicalSandbox.HeldItems.cs).
        }

        /// <summary>Segmented bodies drawn tail first so the head (the enemy sprite) sits on top.</summary>
        private void DrawWormBody(Graphics g, EnemyAgent enemy, int startX, int startY)
        {
            var segments = enemy.Segments;
            if (segments.Count == 0)
                return;
            var radius = MathF.Max(enemy.Motion.Size.X, enemy.Motion.Size.Y) * 0.42f * TileSize;
            var tint = enemy.Kind == EnemyKind.DuneBorer ? Color.FromArgb(206, 176, 112) : Color.FromArgb(120, 90, 72);
            using var body = new SolidBrush(tint);
            using var shade = new SolidBrush(ControlPaint.Dark(tint, 0.2f));
            for (var i = segments.Count - 1; i >= 0; i--)
            {
                var r = radius * (0.75f + 0.25f * (1f - i / (float)segments.Count));
                var cx = (segments[i].X - startX) * TileSize;
                var cy = (segments[i].Y - startY) * TileSize;
                g.FillEllipse(shade, cx - r, cy - r, r * 2, r * 2);
                g.FillEllipse(body, cx - r * 0.85f, cy - r * 0.9f, r * 1.7f, r * 1.6f);
            }
        }

        /// <summary>Wires on every layer while a wiring tool or device is held, plus actuator marks.</summary>
        private void DrawWireOverlay(Graphics g, GameSimulation game, int startX, int startY, int viewW, int viewH)
        {
            var item = game.Player.Inventory.SelectedItem;
            if (item is null || !(item.Id is ItemId.WireSpool or ItemId.WireCutter or ItemId.Actuator ||
                                  item.PlacedTile != TileId.Air && TilePhysics.IsWiringDevice(item.PlacedTile)))
                return;
            var wires = game.Wires;
            var half = TileSize / 2f;
            for (var layer = 1; layer <= 4; layer++)
            {
                var active = layer == game.ActiveWireLayer;
                using var pen = new Pen(Color.FromArgb(active ? 240 : 110, WireColors[layer - 1]), active ? 3f : 2f);
                var offset = (layer - 2.5f) * 2f;
                foreach (var (cell, bits) in wires.Cells)
                {
                    if ((bits & (1 << (layer - 1))) == 0 || cell.X < startX - 1 || cell.Y < startY - 1 || cell.X > startX + viewW || cell.Y > startY + viewH)
                        continue;
                    var cx = (cell.X - startX) * TileSize + half + offset;
                    var cy = (cell.Y - startY) * TileSize + half + offset;
                    var linked = false;
                    if (wires.Has(new GridPoint(cell.X + 1, cell.Y), layer)) { g.DrawLine(pen, cx, cy, cx + TileSize, cy); linked = true; }
                    if (wires.Has(new GridPoint(cell.X, cell.Y + 1), layer)) { g.DrawLine(pen, cx, cy, cx, cy + TileSize); linked = true; }
                    if (!linked)
                        g.DrawRectangle(pen, cx - 2, cy - 2, 4, 4);
                }
            }
            using var actuatorPen = new Pen(Color.FromArgb(220, 255, 140, 40), 2f);
            for (var row = 0; row < viewH; row++)
            for (var col = 0; col < viewW; col++)
            {
                var x = startX + col;
                var y = startY + row;
                if (game.World.Contains(x, y) && game.World.GetCell(x, y).HasActuator)
                    g.DrawRectangle(actuatorPen, col * TileSize + 3, row * TileSize + 3, TileSize - 6, TileSize - 6);
            }
            g.DrawString($"WIRE LAYER {game.ActiveWireLayer}  (L: next layer)", _small, new SolidBrush(WireColors[game.ActiveWireLayer - 1]), 10, HotbarTop + HotbarSlot + 64);
        }

        private readonly Dictionary<(ushort Wall, int Alpha, int Size), TextureBrush?> _backdropBrushes = new();
        private int _backdropSize;

        /// <summary>
        /// Darkened wall texture used as the far backdrop of a depth band, at one of four opacities (1–4 quarters)
        /// so band boundaries can cross-fade.
        /// </summary>
        private TextureBrush? BackdropBrush(ushort wall, int alpha)
        {
            if (_backdropSize != TileSize)
            {
                foreach (var brush in _backdropBrushes.Values)
                    brush?.Dispose();
                _backdropBrushes.Clear();
                _backdropSize = TileSize;
            }
            var key = (wall, alpha, TileSize);
            if (_backdropBrushes.TryGetValue(key, out var cached))
                return cached;
            TextureBrush? result = null;
            if (_sprites.GetScaled(SpriteCatalog.ForWall(wall), TileSize, TileSize) is { } texture)
            {
                var dark = new Bitmap(TileSize, TileSize, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(dark))
                using (var attributes = new ImageAttributes())
                {
                    attributes.SetColorMatrix(new ColorMatrix
                    {
                        Matrix00 = 0.42f, Matrix11 = 0.42f, Matrix22 = 0.46f, Matrix33 = alpha / 4f, Matrix44 = 1f
                    });
                    g.DrawImage(texture, new Rectangle(0, 0, TileSize, TileSize), 0, 0, TileSize, TileSize, GraphicsUnit.Pixel, attributes);
                }
                result = new TextureBrush(dark, WrapMode.Tile);
            }
            _backdropBrushes[key] = result;
            return result;
        }

        /// <summary>
        /// Far backdrop for one column below its surface, banded by depth like the reference's layered cave
        /// backgrounds: soil wall near the surface, rock wall from the cavern row, irradiated wall in the core.
        /// Each boundary cross-fades over 8 rows so the layers blend instead of switching.
        /// </summary>
        private void DrawDepthBackdrop(Graphics g, GameSimulation game, int worldX, int screenX, int fromRow, int toRow, int startY)
        {
            var surface = game.BiomeLayout.SurfaceAt(worldX);
            var cavern = game.Features?.CavernRow is > 0 and var row ? row : surface + (game.World.Height - surface) / 4;
            var core = game.BiomeLayout.MoltenTopAt(worldX);
            (int Top, ushort Wall)[] bands = [(int.MinValue, WallIds.Regolith), (cavern, WallIds.Rock), (core, WallIds.Radiated)];
            const int fade = 8;
            for (var i = 0; i < bands.Length; i++)
            {
                var top = Math.Max(fromRow, bands[i].Top);
                var bottom = i + 1 < bands.Length ? Math.Min(toRow, bands[i + 1].Top) : toRow;
                if (bottom <= top)
                    continue;
                if (BackdropBrush(bands[i].Wall, 4) is { } brush)
                {
                    brush.ResetTransform();
                    brush.TranslateTransform(screenX, 0);
                    g.FillRectangle(brush, screenX, (top - startY) * TileSize, TileSize, (bottom - top) * TileSize);
                }
                else
                    g.FillRectangle(_caveBrush, screenX, (top - startY) * TileSize, TileSize, (bottom - top) * TileSize);
                // Cross-fade: the next band's texture creeps up over the last rows of this one.
                if (i + 1 < bands.Length)
                    for (var step = 1; step <= fade; step++)
                    {
                        var rowY = bands[i + 1].Top - step;
                        if (rowY < fromRow || rowY >= toRow || BackdropBrush(bands[i + 1].Wall, Math.Clamp(4 - step / 2, 1, 4)) is not { } next)
                            continue;
                        next.ResetTransform();
                        next.TranslateTransform(screenX, 0);
                        g.FillRectangle(next, screenX, (rowY - startY) * TileSize, TileSize, TileSize);
                    }
            }
        }

        /// <summary>Active buffs and debuffs in the top-left with remaining time; hovering shows the name.</summary>
        private void DrawBuffBar(Graphics g, GameSimulation game)
        {
            const int icon = 30;
            var x = 10f;
            var y = HotbarTop + HotbarSlot + 10f;
            string? hover = null;
            foreach (var buff in game.ActiveBuffs.Take(22))
            {
                var rect = new RectangleF(x, y, icon, icon);
                using (var back = new SolidBrush(buff.IsDebuff ? Color.FromArgb(170, 90, 24, 30) : Color.FromArgb(170, 20, 40, 60)))
                    g.FillRectangle(back, rect);
                g.DrawRectangle(buff.IsDebuff ? Pens.IndianRed : Pens.SteelBlue, rect.X, rect.Y, rect.Width, rect.Height);
                if (_sprites.GetFitted(buff.IconName, icon - 6, icon - 6) is { } art)
                    g.DrawImage(art, rect.X + (icon - art.Width) / 2f, rect.Y + (icon - art.Height) / 2f, art.Width, art.Height);
                else
                    g.DrawString(buff.Name[..Math.Min(2, buff.Name.Length)], _small, _hudBrush, rect.X + 4, rect.Y + 8);
                var time = buff.RemainingSeconds >= 60f ? $"{buff.RemainingSeconds / 60f:0}m" : $"{buff.RemainingSeconds:0}s";
                if (buff.RemainingSeconds < 3600f)
                    g.DrawString(time, _small, _dimTextBrush, rect.X, rect.Bottom);
                if (MousePosition is { } mouse && rect.Contains(mouse))
                    hover = buff.Name;
                x += icon + 6;
                if (x > 10 + 11 * (icon + 6))
                {
                    x = 10;
                    y += icon + 18;
                }
            }
            if (hover is not null && MousePosition is { } at)
                g.DrawString(hover, _hud, _amberBrush, at.X + 14, at.Y + 4);
        }

        /// <summary>Breath bubbles above the head while submerged; respawn time while waiting.</summary>
        private void DrawBreath(Graphics g, GameSimulation game, int startX, int startY)
        {
            if (game.Breath >= game.MaximumBreath - 0.01f)
                return;
            var motion = game.Player.Motion;
            const int bubbles = 10;
            var full = (int)MathF.Ceiling(game.Breath / game.MaximumBreath * bubbles);
            var size = 9f;
            var left = (motion.Position.X + motion.Size.X / 2f - startX) * TileSize - bubbles * (size + 1) / 2f;
            var top = (motion.Position.Y - startY) * TileSize - TileSize * 1.5f;
            using var bubble = new SolidBrush(Color.FromArgb(220, 150, 210, 255));
            using var empty = new Pen(Color.FromArgb(120, 150, 210, 255));
            for (var i = 0; i < bubbles; i++)
            {
                if (i < full)
                    g.FillEllipse(bubble, left + i * (size + 1), top, size, size);
                else
                    g.DrawEllipse(empty, left + i * (size + 1), top, size, size);
            }
        }
    }
}
