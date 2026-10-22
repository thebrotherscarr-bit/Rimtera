using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;
using Rimtera.Entities;
using Rimtera.Systems;

namespace Rimtera.UI;

/// <summary>
/// How an item is shown while it is used, after Terraria's useStyle (docs/terraria-evidence/full/README.md): swing, thrust,
/// hold out and aim, hold up, eat or drink, throw, and use in front. Derived on the UI side from
/// <see cref="ItemUseKind"/>, <see cref="WeaponStyle"/> and a few item fields; nothing here feeds the simulation.
/// </summary>
internal enum UseAnimation : byte
{
    /// <summary>Not animated when used (materials, ammunition, armour and accessories: those show as gear).</summary>
    None,
    /// <summary>Overhead arc in front (swords, pickaxes, hatchets).</summary>
    Swing,
    /// <summary>The overhead arc with a short wind-up at the top (hammers, demolishers).</summary>
    HeavySwing,
    /// <summary>A wide, low net sweep.</summary>
    NetSwing,
    /// <summary>Held out toward the cursor and shaking (channel drills and saws).</summary>
    Drill,
    /// <summary>Pushed out along the aim and pulled back (shortswords, spears).</summary>
    Stab,
    /// <summary>Held out at the cursor with recoil and a muzzle flash (guns, shotguns, launchers).</summary>
    Aim,
    /// <summary>Held out at the cursor with the string drawn back.</summary>
    Bow,
    /// <summary>Pointed at the cursor with a glowing tip.</summary>
    Staff,
    /// <summary>An open book held in front with a rune ring.</summary>
    Tome,
    /// <summary>A staff held on the beam.</summary>
    Beam,
    /// <summary>Raised overhead (summons, boss and event items, recall, keys, bags).</summary>
    HoldUp,
    /// <summary>Lifted to the mouth with crumbs.</summary>
    Eat,
    /// <summary>Lifted to the mouth and tipped (potions, stims, tonics).</summary>
    Drink,
    /// <summary>Wind up behind the head and fling toward the cursor.</summary>
    Throw,
    /// <summary>Thrown out on a string or chain; the arm follows it (yo-yos, boomerangs, flails).</summary>
    Tether,
    /// <summary>A lash from the hand.</summary>
    Whip,
    /// <summary>A short reach toward the cursor (blocks, walls, furniture, wiring and paint tools, critters).</summary>
    Place,
    /// <summary>Held toward the cursor and tipped (buckets).</summary>
    Pour,
    /// <summary>Pointed at the cursor with a mist (plating sprayer).</summary>
    Spray,
    /// <summary>Rod flicked from behind the head; held out while the bobber is in the water.</summary>
    Cast
}

internal static class UseAnimations
{
    /// <summary>The use animation for <paramref name="item"/> (None for an empty hand).</summary>
    public static UseAnimation For(ItemDefinition? item)
    {
        if (item is null)
            return UseAnimation.None;
        switch (item.Style)
        {
            case WeaponStyle.Broadsword: return UseAnimation.Swing;
            case WeaponStyle.Shortsword or WeaponStyle.Spear: return UseAnimation.Stab;
            case WeaponStyle.Yoyo or WeaponStyle.Boomerang or WeaponStyle.Flail: return UseAnimation.Tether;
            case WeaponStyle.Whip: return UseAnimation.Whip;
            case WeaponStyle.Bow: return UseAnimation.Bow;
            case WeaponStyle.Gun or WeaponStyle.Shotgun or WeaponStyle.Launcher: return UseAnimation.Aim;
            case WeaponStyle.Thrown: return UseAnimation.Throw;
            case WeaponStyle.Staff: return UseAnimation.Staff;
            case WeaponStyle.Tome: return UseAnimation.Tome;
            case WeaponStyle.Beam: return UseAnimation.Beam;
            case WeaponStyle.SummonStaff or WeaponStyle.SentryStaff: return UseAnimation.HoldUp;
        }
        switch (item.UseKind)
        {
            case ItemUseKind.Pickaxe or ItemUseKind.Cutter:
                return ToolParity.IsChannelTool(item.Id) ? UseAnimation.Drill : UseAnimation.Swing;
            case ItemUseKind.Demolisher:
                return ToolParity.IsChannelTool(item.Id) ? UseAnimation.Drill : UseAnimation.HeavySwing;
            case ItemUseKind.Melee:
                return item.Thrown ? UseAnimation.Throw : UseAnimation.Swing;
            case ItemUseKind.Ranged:
                return item.Thrown ? UseAnimation.Throw : UseAnimation.Aim;
            case ItemUseKind.Tech:
                return UseAnimation.Staff;
            case ItemUseKind.Command:
                return UseAnimation.HoldUp;
            case ItemUseKind.Food:
                return IsDrinkName(item.Name) ? UseAnimation.Drink : UseAnimation.Eat;
            case ItemUseKind.Consumable:
                if (item.SummonsBoss is not null || item.StartsEvent is not null)
                    return UseAnimation.HoldUp;
                if (item.FoodRestore > 0f)
                    return IsDrinkName(item.Name) ? UseAnimation.Drink : UseAnimation.Eat;
                return item.HealAmount > 0f || item.ChargeRestore > 0f || item.GrantsStatus is not null
                    ? UseAnimation.Drink
                    : UseAnimation.HoldUp;
            case ItemUseKind.PlaceTile or ItemUseKind.PlaceWall or ItemUseKind.Critter:
                return UseAnimation.Place;
            case ItemUseKind.BugNet:
                return UseAnimation.NetSwing;
            case ItemUseKind.Utility:
                return item.Id switch
                {
                    ItemId.FishingRod => UseAnimation.Cast,
                    ItemId.EmptyBucket or ItemId.BucketWater or ItemId.BucketLava or ItemId.BucketFuel => UseAnimation.Pour,
                    ItemId.PaintSprayer => UseAnimation.Spray,
                    ItemId.GlowSticks => UseAnimation.Throw,
                    ItemId.ShapingHammer => UseAnimation.HeavySwing,
                    ItemId.Paintbrush or ItemId.PaintRoller or ItemId.PaintScraper or ItemId.WireTool or ItemId.WireCutter or
                        ItemId.WireSpool or ItemId.Actuator or ItemId.ClimbingRope => UseAnimation.Place,
                    _ when item.FishingPower > 0 => UseAnimation.Cast,
                    _ => UseAnimation.HoldUp
                };
        }
        return UseAnimation.None;
    }

    private static readonly string[] DrinkWords = ["juice", "coffee", "soda", "milk", "drink", "tonic", "brew", "smoothie", "broth", "nectar", "elixir", "potion", "stim", "flask"];

    private static bool IsDrinkName(string name) =>
        DrinkWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)) ||
        name.EndsWith(" tea", StringComparison.OrdinalIgnoreCase) || name.Equals("tea", StringComparison.OrdinalIgnoreCase);

    /// <summary>Torches are held up in the hand while selected (Terraria's holdStyle), not only when used.</summary>
    public static bool IsHeldLight(ItemDefinition? item) =>
        item is not null && item.Name.Contains("torch", StringComparison.OrdinalIgnoreCase);
}

public static partial class GraphicalSandbox
{
    private sealed partial class GameCanvas
    {
        // Measured on the player body frames (Assets/Sprites/anim_idle.png, 64×117, feet on the bottom edge): the helmet
        // takes the top third, the shoulder joint sits 36% down from the top, the hanging hand at 75%, the mouth at 27%.
        // Held items pivot at the shoulder and sit in the hand at the end of a drawn arm.
        internal const float ShoulderFromTop = 0.36f;
        internal const float ShoulderForward = 0.04f;
        internal const float ArmLengthOfBody = 0.30f;
        internal const float MouthFromTop = 0.27f;
        internal const float MouthForward = 0.13f;

        /// <summary>Screen-space joints of the colonist this frame.</summary>
        internal readonly record struct PlayerRig(PointF Shoulder, PointF Mouth, PointF Feet, PointF Center, float BodyHeight,
            float ArmLength, int Facing, int StartX, int StartY)
        {
            /// <summary>Where the hand hangs at rest (down and a little forward).</summary>
            public PointF RestHand => new(Shoulder.X + Facing * ArmLength * 0.25f, Shoulder.Y + ArmLength * 0.95f);

            /// <summary>Facing-relative degrees (0 forward, −90 up, 90 down) to screen degrees.</summary>
            public float Screen(float relative) => Facing > 0 ? relative : 180f - relative;

            /// <summary>Screen degrees to facing-relative degrees in (−180, 180].</summary>
            public float Relative(float screen) => Wrap(Facing > 0 ? screen : 180f - screen);

            public PointF Hand(float screenDegrees, float reach = 1f)
            {
                var r = screenDegrees * MathF.PI / 180f;
                return new PointF(Shoulder.X + MathF.Cos(r) * ArmLength * reach, Shoulder.Y + MathF.Sin(r) * ArmLength * reach);
            }
        }

        private PlayerRig _rig;
        /// <summary>The hand while it holds something this frame (strings, chains, hooks and lines leave from it).</summary>
        private PointF? _heldHand;
        private PointF? _rodTip;
        private PointF? _beamTip;
        private readonly Dictionary<string, (Color Sleeve, Color Glove)> _armColors = new();
        private readonly Random _useRandom = new(77);

        private static float Wrap(float degrees)
        {
            degrees %= 360f;
            return degrees > 180f ? degrees - 360f : degrees <= -180f ? degrees + 360f : degrees;
        }

        private static float Deg(float radians) => radians * 180f / MathF.PI;
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
        private static float Smooth(float t) => t * t * (3f - 2f * t);
        private static PointF Along(PointF from, float degrees, float distance) =>
            new(from.X + MathF.Cos(degrees * MathF.PI / 180f) * distance, from.Y + MathF.Sin(degrees * MathF.PI / 180f) * distance);

        private PlayerRig RigFor(GameSimulation game, ActorAnimator.Pose pose, int startX, int startY)
        {
            var motion = game.Player.Motion;
            var facing = motion.Facing >= 0 ? 1 : -1;
            var body = motion.Size.Y * TileSize;
            var cx = (motion.Position.X + motion.Size.X / 2f - startX) * TileSize;
            var feet = (motion.Position.Y + motion.Size.Y - startY) * TileSize + pose.Bob;
            return new PlayerRig(
                new PointF(cx + facing * body * ShoulderForward, feet - body * (1f - ShoulderFromTop)),
                new PointF(cx + facing * body * MouthForward, feet - body * (1f - MouthFromTop)),
                new PointF(cx, feet), new PointF(cx, feet - body / 2f), body, body * ArmLengthOfBody, facing, startX, startY);
        }

        /// <summary>The mouse in the same tile-space screen coordinates as the rig.</summary>
        private PointF AimPoint(in PlayerRig rig)
        {
            if (MousePosition is { } mouse)
                return new PointF((_viewStartX + mouse.X / (float)TileSize - rig.StartX) * TileSize,
                    (_viewStartY + mouse.Y / (float)TileSize - rig.StartY) * TileSize);
            return new PointF(rig.Shoulder.X + rig.Facing * 120f, rig.Shoulder.Y);
        }

        private Vector2 ToWorld(in PlayerRig rig, PointF screen) =>
            new(screen.X / TileSize + rig.StartX, screen.Y / TileSize + rig.StartY);

        /// <summary>Sleeve and glove colours sampled from the idle body (so outfit, skin and dye recolours carry over).</summary>
        private (Color Sleeve, Color Glove) ArmColors(GameSimulation game)
        {
            var fallback = (Color.FromArgb(89, 142, 160), Color.FromArgb(103, 97, 111));
            if (!_animator.HasAnimationFrames)
                return (Color.FromArgb(214, 120, 52), Color.FromArgb(90, 80, 70));
            var look = DyedLook(game, CharacterLook.Body(_sprites, "anim_idle", game.Player.Colonist.AppearanceValue.Normalized()));
            if (_armColors.TryGetValue(look, out var cached))
                return cached;
            if (_sprites.Get(look) is not { } body)
                return fallback;
            Color Sample(float u, float v)
            {
                var pixel = body.GetPixel(Math.Clamp((int)(u * body.Width), 0, body.Width - 1), Math.Clamp((int)(v * body.Height), 0, body.Height - 1));
                return pixel.A > 128 ? Color.FromArgb(pixel.R, pixel.G, pixel.B) : Color.Empty;
            }
            var sleeve = Sample(12f / 64f, 64f / 117f);
            var glove = Sample(12f / 64f, 88f / 117f);
            return _armColors[look] = (sleeve.IsEmpty ? fallback.Item1 : sleeve, glove.IsEmpty ? fallback.Item2 : glove);
        }

        /// <summary>The front arm from the shoulder to <paramref name="hand"/>; it bends at the elbow when the hand is close.</summary>
        private void DrawArm(Graphics g, in PlayerRig rig, PointF hand, (Color Sleeve, Color Glove) colors)
        {
            var width = MathF.Max(2.5f, rig.BodyHeight * 0.075f);
            var dx = hand.X - rig.Shoulder.X;
            var dy = hand.Y - rig.Shoulder.Y;
            var distance = MathF.Max(0.001f, MathF.Sqrt(dx * dx + dy * dy));
            var half = rig.ArmLength * 0.5f;
            var mid = new PointF(rig.Shoulder.X + dx / 2f, rig.Shoulder.Y + dy / 2f);
            var elbow = mid;
            if (distance < rig.ArmLength * 0.98f)
            {
                // Bend downward and back, like a real elbow.
                var bend = MathF.Sqrt(MathF.Max(0f, half * half - distance * distance / 4f));
                var nx = -dy / distance;
                var ny = dx / distance;
                if (ny < 0f || (MathF.Abs(ny) < 0.2f && nx * rig.Facing > 0f))
                {
                    nx = -nx;
                    ny = -ny;
                }
                elbow = new PointF(mid.X + nx * bend, mid.Y + ny * bend);
            }
            PointF[] arm = [rig.Shoulder, elbow, hand];
            using (var outline = new Pen(Color.FromArgb(220, 10, 12, 20), width + 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(outline, arm);
            using (var sleeve = new Pen(colors.Sleeve, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(sleeve, arm);
            var glove = width * 0.68f;
            using (var gloveBrush = new SolidBrush(colors.Glove))
                g.FillEllipse(gloveBrush, hand.X - glove, hand.Y - glove, glove * 2f, glove * 2f);
            using (var rim = new Pen(Color.FromArgb(200, 10, 12, 20), 1f))
                g.DrawEllipse(rim, hand.X - glove, hand.Y - glove, glove * 2f, glove * 2f);
        }

        /// <summary>Item art turned onto <paramref name="degrees"/> with the point <paramref name="gripAt"/> of the way from its
        /// grip end toward its tip in the hand; mirrored across its own axis when <paramref name="mirror"/>. Returns the tip.</summary>
        private PointF DrawItemAlong(Graphics g, Bitmap art, PointF grip, float degrees, float length, bool mirror, float gripAt = 0.12f,
            bool horizontal = false)
        {
            // Firearm icons point right with a grip hanging below the barrel, which skews the principal axis: use the barrel line.
            var axis = horizontal ? new WeaponAxis(1f, 0f, art.Width, art.Width / 2f, art.Height * 0.4f) : AxisOf(art);
            var scale = length / Math.Max(1f, axis.Length);
            var artDegrees = Deg(MathF.Atan2(axis.TipY, axis.TipX));
            var state = g.Save();
            g.TranslateTransform(grip.X, grip.Y);
            g.RotateTransform(degrees);
            if (mirror)
                g.ScaleTransform(1f, -1f);
            g.RotateTransform(-artDegrees);
            g.ScaleTransform(scale, scale);
            var gx = axis.CenterX - axis.TipX * axis.Length * (0.5f - gripAt);
            var gy = axis.CenterY - axis.TipY * axis.Length * (0.5f - gripAt);
            g.DrawImage(art, -gx, -gy, art.Width, art.Height);
            g.Restore(state);
            return Along(grip, degrees, length * (1f - gripAt));
        }

        /// <summary>Item art upright (or tilted by <paramref name="degrees"/>) centred on <paramref name="center"/>.</summary>
        private void DrawItemCentered(Graphics g, Bitmap art, PointF center, float degrees)
        {
            var state = g.Save();
            g.TranslateTransform(center.X, center.Y);
            if (MathF.Abs(degrees) > 0.5f)
                g.RotateTransform(degrees);
            g.DrawImage(art, -art.Width / 2f, -art.Height / 2f, art.Width, art.Height);
            g.Restore(state);
        }

        private Bitmap? ItemArt(ItemId id, float size, bool flip = false) =>
            _sprites.GetFitted(SpriteCatalog.ForItem(id), Math.Max(4, (int)size), Math.Max(4, (int)size), flip);

        /// <summary>The faint trail of a swing's tip from where it started to where it is now.</summary>
        private void DrawSmear(Graphics g, in PlayerRig rig, float fromDegrees, float toDegrees, float radius, Color color)
        {
            if (!_menu.Preferences.FlashyEffects || MathF.Abs(toDegrees - fromDegrees) < 4f)
                return;
            var box = new RectangleF(rig.Shoulder.X - radius, rig.Shoulder.Y - radius, radius * 2f, radius * 2f);
            using var wide = new Pen(Color.FromArgb(38, color), 7f);
            using var thin = new Pen(Color.FromArgb(90, color), 2.5f);
            g.DrawArc(wide, box, fromDegrees, toDegrees - fromDegrees);
            g.DrawArc(thin, box, fromDegrees, toDegrees - fromDegrees);
        }

        private void Spark(in PlayerRig rig, PointF at, Color color, double now, float speed = 4f, float gravity = 10f, float size = 2.5f, float life = 0.35f)
        {
            if (_animator.ParticleDensity <= 0f)
                return;
            var angle = _useRandom.NextDouble() * Math.PI * 2;
            var magnitude = speed * (0.4f + (float)_useRandom.NextDouble());
            _animator.Emit(ToWorld(rig, at), new Vector2((float)Math.Cos(angle) * magnitude, (float)Math.Sin(angle) * magnitude - speed * 0.3f),
                color, now, life * (0.7f + (float)_useRandom.NextDouble() * 0.6f), size, gravity);
        }

        private bool Chance(float perFrame) => _useRandom.NextDouble() < perFrame * Math.Max(0.25f, _animator.ParticleDensity);

        /// <summary>
        /// The selected item in the hand, posed by its use style (Terraria's useStyle) on the simulation's own swing timing,
        /// plus the front arm from the shoulder to the hand. Also aims grapple hooks, holds torches and rods.
        /// </summary>
        private void DrawHeldItem(Graphics g, GameSimulation game, in PlayerRig rig, double now)
        {
            _heldHand = null;
            _rodTip = null;
            _beamTip = null;
            var colors = ArmColors(game);
            var item = game.Player.Inventory.SelectedItem;
            var aim = AimPoint(rig);
            var aimDegrees = Deg(MathF.Atan2(aim.Y - rig.Shoulder.Y, aim.X - rig.Shoulder.X));
            var aimLeft = MathF.Cos(aimDegrees * MathF.PI / 180f) < 0f;
            var body = rig.BodyHeight;

            // Styled melee and lashes follow the simulation's own swing state.
            if (game.PlayerSwing is { } swing)
            {
                DrawSimSwing(g, rig, swing, colors);
                DrawHooks(g, game, rig, colors, armFree: false);
                return;
            }
            if (game.PlayerWhip is { } whip && whip.Points.Count > 1)
            {
                var tip = whip.Points[^1];
                var tipScreen = new PointF((tip.X - rig.StartX) * TileSize, (tip.Y - rig.StartY) * TileSize);
                var toTip = Deg(MathF.Atan2(tipScreen.Y - rig.Shoulder.Y, tipScreen.X - rig.Shoulder.X));
                var wind = Math.Clamp(whip.Progress / 0.3f, 0f, 1f);
                var degrees = whip.Progress < 0.3f ? rig.Screen(Lerp(-60f, -155f, Smooth(wind))) : toTip;
                var hand = rig.Hand(degrees);
                if (item is not null && ItemArt(item.Id, body * 0.42f, rig.Facing < 0) is { } handle)
                    DrawItemAlong(g, handle, hand, degrees, body * 0.3f, rig.Facing < 0, 0.3f);
                DrawArm(g, rig, hand, colors);
                _heldHand = hand;
                DrawHooks(g, game, rig, colors, armFree: false);
                return;
            }
            if (game.PlayerBeam is { } beam && item is not null)
            {
                var end = new PointF((beam.End.X - rig.StartX) * TileSize, (beam.End.Y - rig.StartY) * TileSize);
                var degrees = Deg(MathF.Atan2(end.Y - rig.Shoulder.Y, end.X - rig.Shoulder.X));
                var hand = rig.Hand(degrees, 0.9f);
                hand = new PointF(hand.X + (float)Math.Sin(now * 70) * 0.6f, hand.Y + (float)Math.Cos(now * 63) * 0.6f);
                if (ItemArt(item.Id, body) is { } art)
                    _beamTip = DrawItemAlong(g, art, hand, degrees, body * 0.85f, MathF.Cos(degrees * MathF.PI / 180f) < 0f, 0.3f);
                DrawGlowDot(g, _beamTip ?? hand, 10f + (float)Math.Sin(now * 30) * 3f, Color.FromArgb(140, 220, 255));
                DrawArm(g, rig, hand, colors);
                _heldHand = hand;
                DrawHooks(g, game, rig, colors, armFree: false);
                return;
            }

            var anim = UseAnimations.For(item);
            // A thrown yo-yo, boomerang or flail: the arm follows it out on its string or chain.
            if (anim == UseAnimation.Tether && TetheredProjectile(game) is { } thrown)
            {
                var at = new PointF((thrown.Position.X - rig.StartX) * TileSize, (thrown.Position.Y - rig.StartY) * TileSize);
                var degrees = Deg(MathF.Atan2(at.Y - rig.Shoulder.Y, at.X - rig.Shoulder.X));
                var near = MathF.Min(1f, MathF.Sqrt((at.X - rig.Shoulder.X) * (at.X - rig.Shoulder.X) + (at.Y - rig.Shoulder.Y) * (at.Y - rig.Shoulder.Y)) / rig.ArmLength);
                var hand = rig.Hand(degrees, Math.Clamp(near, 0.55f, 1f));
                DrawArm(g, rig, hand, colors);
                _heldHand = hand;
                DrawHooks(g, game, rig, colors, armFree: false);
                return;
            }

            var progress = _animator.UseProgress(now);
            if (item is null || progress is not { } p || anim == UseAnimation.None)
            {
                DrawIdleHold(g, game, rig, item, anim, colors, now);
                return;
            }

            p = Math.Clamp(p, 0f, 1f);
            switch (anim)
            {
                case UseAnimation.Swing or UseAnimation.HeavySwing or UseAnimation.NetSwing:
                {
                    var (from, to, length) = anim switch
                    {
                        UseAnimation.HeavySwing => (-165f, 50f, body * 0.78f),
                        UseAnimation.NetSwing => (-160f, 75f, body * 0.95f),
                        _ => (-150f, 45f, body * (item.UseKind == ItemUseKind.Melee ? 0.85f : 0.75f))
                    };
                    // Hammers hang at the top for a beat before they come down.
                    var t = anim == UseAnimation.HeavySwing ? (p < 0.18f ? 0f : EaseOut((p - 0.18f) / 0.82f)) : EaseOut(p);
                    var degrees = rig.Screen(Lerp(from, to, t));
                    var hand = rig.Hand(degrees);
                    // Weapons leave a short trail behind the tip (tools swing clean, as in Terraria).
                    if (!ToolParity.IsTool(item))
                        DrawSmear(g, rig, rig.Screen(MathF.Max(from, Lerp(from, to, t) - 70f)), degrees, rig.ArmLength + length * 0.8f, Color.White);
                    if (ItemArt(item.Id, length) is { } art)
                    {
                        var tip = DrawItemAlong(g, art, hand, degrees, length, rig.Facing < 0, 0.1f);
                        // Tools strike sparks at the bottom of the swing.
                        if (ToolParity.IsTool(item) && t is > 0.55f and < 0.85f && Chance(0.5f))
                            Spark(rig, tip, Color.FromArgb(255, 220, 140), now);
                    }
                    DrawArm(g, rig, hand, colors);
                    _heldHand = hand;
                    break;
                }
                case UseAnimation.Drill:
                {
                    var shake = new SizeF((float)Math.Sin(now * 83) * 1.2f, (float)Math.Cos(now * 71) * 1.2f);
                    var hand = rig.Hand(aimDegrees, 0.9f) + shake;
                    if (ItemArt(item.Id, body * 0.85f) is { } art)
                    {
                        var tip = DrawItemAlong(g, art, hand, aimDegrees, body * 0.85f, aimLeft, 0.25f);
                        if (Chance(0.6f))
                            Spark(rig, tip, Color.FromArgb(255, 200, 110), now, 5f);
                    }
                    DrawArm(g, rig, hand, colors);
                    _heldHand = hand;
                    break;
                }
                case UseAnimation.Stab:
                {
                    var push = MathF.Sin(MathF.PI * p);
                    var hand = rig.Hand(aimDegrees, 0.45f + 0.55f * push);
                    if (ItemArt(item.Id, body * 0.75f) is { } art)
                        DrawItemAlong(g, art, hand, aimDegrees, body * 0.75f, aimLeft, 0.1f);
                    DrawArm(g, rig, hand, colors);
                    _heldHand = hand;
                    break;
                }
                case UseAnimation.Aim or UseAnimation.Spray or UseAnimation.Staff or UseAnimation.Beam:
                {
                    var recoil = anim == UseAnimation.Aim && p < 0.25f ? 1f - p / 0.25f : 0f;
                    var kick = aimDegrees + (aimLeft ? 1f : -1f) * recoil * (item.Style == WeaponStyle.Launcher ? 6f : 11f);
                    var hand = Along(rig.Hand(kick, 0.85f), kick, -recoil * 4f);
                    var length = body * (item.Style switch
                    {
                        WeaponStyle.Launcher => 1.05f,
                        WeaponStyle.Shotgun => 0.92f,
                        _ when anim is UseAnimation.Staff or UseAnimation.Beam => 0.9f,
                        _ when anim == UseAnimation.Spray => 0.55f,
                        _ => 0.78f
                    });
                    PointF tip = hand;
                    if (ItemArt(item.Id, length) is { } art)
                        tip = DrawItemAlong(g, art, hand, kick, length, aimLeft, anim == UseAnimation.Aim ? 0.3f : 0.28f,
                            horizontal: anim == UseAnimation.Aim && art.Width >= art.Height * 1.15f);
                    if (anim == UseAnimation.Aim && p < 0.16f && _menu.Preferences.FlashyEffects)
                        DrawMuzzleFlash(g, tip, kick, item.Style == WeaponStyle.Launcher ? 1.6f : item.Style == WeaponStyle.Shotgun ? 1.3f : 1f);
                    if (anim == UseAnimation.Aim && item.Style == WeaponStyle.Launcher && p < 0.3f && Chance(0.7f))
                        Spark(rig, Along(hand, kick, -length * 0.3f), Color.FromArgb(180, 190, 190, 195), now, 2f, -3f, 4f, 0.6f);
                    if (anim is UseAnimation.Staff or UseAnimation.Beam)
                    {
                        // Staves point and glow: bright on the cast, then a soft pulse while the use lasts.
                        var glow = (p < 0.25f ? 9f * (1f - p / 0.25f) : 0f) + 4f + (float)Math.Sin(now * 12) * 1.2f;
                        DrawGlowDot(g, tip, glow * 2f, StaffColor(item));
                        if (Chance(0.35f))
                            Spark(rig, tip, StaffColor(item), now, 2f, -2f, 2f, 0.5f);
                    }
                    if (anim == UseAnimation.Spray && Chance(1f))
                        for (var i = 0; i < 2; i++)
                            _animator.Emit(ToWorld(rig, tip), new Vector2(MathF.Cos(kick * MathF.PI / 180f), MathF.Sin(kick * MathF.PI / 180f)) * 7f +
                                new Vector2((float)_useRandom.NextDouble() - 0.5f, (float)_useRandom.NextDouble() - 0.5f) * 3f,
                                Color.FromArgb(170, Color.FromArgb(_useRandom.Next(120, 255), _useRandom.Next(120, 255), _useRandom.Next(120, 255))), now, 0.4f, 3f, 2f);
                    DrawArm(g, rig, hand, colors);
                    _heldHand = hand;
                    if (anim is UseAnimation.Staff or UseAnimation.Beam)
                        _beamTip = tip;
                    break;
                }
                case UseAnimation.Bow:
                    DrawBow(g, rig, item, aimDegrees, aimLeft, p, colors);
                    break;
                case UseAnimation.Tome:
                {
                    var degrees = rig.Screen(Math.Clamp(rig.Relative(aimDegrees), -35f, 35f));
                    var hand = rig.Hand(degrees, 0.8f);
                    var size = body * 0.42f;
                    var center = Along(hand, degrees, size * 0.25f);
                    DrawRuneRing(g, center, size * 0.75f, p, now, StaffColor(item));
                    if (ItemArt(item.Id, size, rig.Facing < 0) is { } art)
                        DrawItemCentered(g, art, center, 0f);
                    if (Chance(0.5f))
                        Spark(rig, center, StaffColor(item), now, 2.5f, -3f, 2f, 0.6f);
                    DrawArm(g, rig, hand, colors);
                    _heldHand = hand;
                    break;
                }
                case UseAnimation.HoldUp:
                    DrawHoldUp(g, rig, item, p, colors, now);
                    break;
                case UseAnimation.Eat or UseAnimation.Drink:
                    DrawEatDrink(g, rig, item, anim == UseAnimation.Drink, p, colors, now);
                    break;
                case UseAnimation.Throw or UseAnimation.Tether:
                {
                    // Wind up behind the head, fling through the top toward the cursor, follow through low.
                    var target = Math.Clamp(rig.Relative(aimDegrees), -100f, 70f);
                    float relative;
                    if (p < 0.3f)
                        relative = Lerp(-40f, -160f, Smooth(p / 0.3f));
                    else if (p < 0.55f)
                        relative = Lerp(-160f, target, Smooth((p - 0.3f) / 0.25f));
                    else
                        relative = Lerp(target, 55f, Smooth((p - 0.55f) / 0.45f));
                    var degrees = rig.Screen(relative);
                    var hand = rig.Hand(degrees);
                    if (p < 0.42f && ItemArt(item.Id, body * 0.4f) is { } art)
                        DrawItemAlong(g, art, hand, degrees, body * 0.38f, rig.Facing < 0, 0.3f);
                    if (p is > 0.3f and < 0.6f)
                        DrawSmear(g, rig, rig.Screen(MathF.Max(-160f, relative - 60f)), degrees, rig.ArmLength * 1.3f, Color.White);
                    DrawArm(g, rig, hand, colors);
                    _heldHand = hand;
                    break;
                }
                case UseAnimation.Place:
                {
                    // A short reach toward the cursor, from above it down onto it.
                    var target = Math.Clamp(rig.Relative(aimDegrees), -110f, 80f);
                    var degrees = rig.Screen(Lerp(target - 45f, target + 12f, EaseOut(p)));
                    var hand = rig.Hand(degrees);
                    if (item.UseKind == ItemUseKind.Utility)
                    {
                        if (ItemArt(item.Id, body * 0.5f) is { } tool)
                        {
                            var tip = DrawItemAlong(g, tool, hand, degrees, body * 0.45f, rig.Facing < 0, 0.2f);
                            if (p is > 0.5f and < 0.8f && Chance(0.5f))
                                Spark(rig, tip, item.Id is ItemId.WireTool or ItemId.WireCutter or ItemId.WireSpool or ItemId.Actuator
                                    ? Color.FromArgb(255, 90, 90) : Color.FromArgb(_useRandom.Next(140, 255), _useRandom.Next(140, 255), 200), now, 2.5f);
                        }
                    }
                    else if (ItemArt(item.Id, body * 0.32f, rig.Facing < 0) is { } block)
                        DrawItemCentered(g, block, Along(hand, degrees, body * 0.1f), 0f);
                    DrawArm(g, rig, hand, colors);
                    _heldHand = hand;
                    break;
                }
                case UseAnimation.Pour:
                {
                    var hand = rig.Hand(aimDegrees, 0.85f);
                    var empty = item.Id == ItemId.EmptyBucket;
                    var tilt = rig.Facing * (empty ? -45f : 80f) * EaseOut(Math.Min(1f, p / 0.45f));
                    var size = body * 0.36f;
                    if (ItemArt(item.Id, size, rig.Facing < 0) is { } art)
                        DrawItemCentered(g, art, new PointF(hand.X, hand.Y + size * 0.3f), tilt);
                    if (!empty && p is > 0.25f and < 0.9f && Chance(1f))
                    {
                        var liquid = item.Id switch
                        {
                            ItemId.BucketLava => Color.FromArgb(130, 255, 90),
                            ItemId.BucketFuel => Color.FromArgb(235, 170, 60),
                            _ => Color.FromArgb(90, 150, 255)
                        };
                        var lip = new PointF(hand.X + rig.Facing * size * 0.45f, hand.Y + size * 0.1f);
                        _animator.Emit(ToWorld(rig, lip), new Vector2(rig.Facing * 2.5f, 0.5f), liquid, now, 0.5f, 3f, 22f);
                    }
                    DrawArm(g, rig, hand, colors);
                    _heldHand = hand;
                    break;
                }
                case UseAnimation.Cast:
                {
                    var t = EaseOut(p);
                    var arm = rig.Screen(Lerp(-130f, 5f, t));
                    var rod = rig.Screen(Lerp(-160f, -40f, t));
                    var hand = rig.Hand(arm);
                    if (ItemArt(item.Id, body * 1.1f) is { } art)
                        _rodTip = DrawItemAlong(g, art, hand, rod, body * 1.05f, rig.Facing < 0, 0.08f);
                    DrawArm(g, rig, hand, colors);
                    _heldHand = hand;
                    break;
                }
                default:
                    DrawIdleHold(g, game, rig, item, anim, colors, now);
                    return;
            }
            DrawHooks(g, game, rig, colors, armFree: false);
        }

        /// <summary>Holding without using: a rod while its bobber floats, a lit torch, or an arm aimed along a grapple line.</summary>
        private void DrawIdleHold(Graphics g, GameSimulation game, in PlayerRig rig, ItemDefinition? item, UseAnimation anim,
            (Color Sleeve, Color Glove) colors, double now)
        {
            var body = rig.BodyHeight;
            if (item is not null && anim == UseAnimation.Cast && game.Bobber is not null)
            {
                var hand = rig.Hand(rig.Screen(10f), 0.95f);
                if (ItemArt(item.Id, body * 1.1f) is { } art)
                    _rodTip = DrawItemAlong(g, art, hand, rig.Screen(-38f), body * 1.05f, rig.Facing < 0, 0.08f);
                DrawArm(g, rig, hand, colors);
                _heldHand = hand;
            }
            else if (UseAnimations.IsHeldLight(item) && game.Grapple is null)
            {
                var hand = rig.Hand(rig.Screen(-28f), 0.95f);
                var size = body * 0.34f;
                if (ItemArt(item!.Id, size, rig.Facing < 0) is { } art)
                    DrawItemCentered(g, art, new PointF(hand.X, hand.Y - size * 0.3f), 0f);
                var flicker = 5f + (float)Math.Sin(now * 17) * 1f + (float)Math.Sin(now * 29) * 0.7f;
                DrawGlowDot(g, new PointF(hand.X, hand.Y - size * 0.7f), flicker * 2f, Color.FromArgb(255, 190, 90));
                if (Chance(0.12f))
                    Spark(rig, new PointF(hand.X, hand.Y - size * 0.75f), Color.FromArgb(255, 200, 100), now, 1.2f, -4f, 2f, 0.5f);
                DrawArm(g, rig, hand, colors);
                _heldHand = hand;
            }
            DrawHooks(g, game, rig, colors, armFree: _heldHand is null);
        }

        /// <summary>Grapple lines from the hand; with a free hand the arm points along the newest hook.</summary>
        private void DrawHooks(Graphics g, GameSimulation game, in PlayerRig rig, (Color Sleeve, Color Glove) colors, bool armFree)
        {
            if (game.Grapple is null && game.GrappleSpare is null)
                return;
            var origin = new Vector2(rig.StartX, rig.StartY);
            var tile = TileSize;
            PointF Screen(Vector2 world) => new((world.X - origin.X) * tile, (world.Y - origin.Y) * tile);
            if (armFree && game.Grapple is { } newest)
            {
                var at = Screen(newest.Position);
                var hand = rig.Hand(Deg(MathF.Atan2(at.Y - rig.Shoulder.Y, at.X - rig.Shoulder.X)));
                DrawArm(g, rig, hand, colors);
                _heldHand = hand;
            }
            var from = _heldHand ?? rig.RestHand;
            foreach (var hook in new[] { game.Grapple, game.GrappleSpare })
            {
                if (hook is null)
                    continue;
                var b = Screen(hook.Position);
                g.DrawLine(_grapplePen, from, b);
                // A small three-pronged hook head pointing along the line.
                var degrees = Deg(MathF.Atan2(b.Y - from.Y, b.X - from.X));
                using var prong = new Pen(hook.Latched ? Color.FromArgb(255, 200, 90) : Color.FromArgb(220, 225, 235), 2f) { EndCap = LineCap.Round };
                foreach (var spread in new[] { -40f, 0f, 40f })
                    g.DrawLine(prong, b, Along(b, degrees + 180f + spread, 5f));
                g.FillEllipse(hook.Latched ? _amberBrush : _hudBrush, b.X - 2.5f, b.Y - 2.5f, 5, 5);
            }
        }

        private static ProjectileState? TetheredProjectile(GameSimulation game)
        {
            ProjectileState? best = null;
            foreach (var projectile in game.Projectiles)
                if (projectile.Team == ProjectileTeam.Player && projectile.Style is WeaponStyle.Yoyo or WeaponStyle.Boomerang or WeaponStyle.Flail)
                    best = projectile;
            return best;
        }

        /// <summary>Broadsword arcs and shortsword/spear thrusts from the simulation, pivoting at the shoulder.</summary>
        private void DrawSimSwing(Graphics g, in PlayerRig rig, MeleeSwingView swing, (Color Sleeve, Color Glove) colors)
        {
            var degrees = Deg(swing.AngleRadians);
            var left = MathF.Cos(swing.AngleRadians) < 0f;
            var reach = MathF.Max(TileSize, swing.Reach * TileSize);
            var thrust = swing.Style is WeaponStyle.Spear or WeaponStyle.Shortsword;
            var progress = Math.Clamp(swing.Progress, 0f, 1f);
            PointF hand;
            float length;
            if (thrust)
            {
                // The arm punches out and draws back; the tip reaches the simulation's reach at full extension.
                var push = MathF.Sin(MathF.PI * progress);
                hand = rig.Hand(degrees, 0.4f + 0.6f * push);
                length = MathF.Max(rig.BodyHeight * 0.5f, (reach - rig.ArmLength) / 0.92f);
            }
            else
            {
                hand = rig.Hand(degrees);
                length = MathF.Max(rig.BodyHeight * 0.75f, (reach - rig.ArmLength) / 0.92f);
                var trail = MathF.Min(70f, 132f * progress);
                var start = degrees - (left ? -1f : 1f) * trail;
                DrawSmear(g, rig, start, degrees, rig.ArmLength + length * 0.85f, Color.White);
            }
            if (_sprites.GetFitted(SpriteCatalog.ForItem(swing.Item), (int)length, (int)length) is { } art)
                DrawItemAlong(g, art, hand, degrees, length, left, 0.08f);
            DrawArm(g, rig, hand, colors);
            _heldHand = hand;
        }

        private void DrawBow(Graphics g, in PlayerRig rig, ItemDefinition item, float aimDegrees, bool aimLeft, float p, (Color Sleeve, Color Glove) colors)
        {
            var body = rig.BodyHeight;
            var hand = rig.Hand(aimDegrees, 0.95f);
            var length = body * 0.72f;
            // The limbs stand across the aim line with the grip in the hand.
            if (ItemArt(item.Id, length) is { } art)
                DrawItemAlong(g, art, Along(hand, aimDegrees - 90f, -length * 0.5f), aimDegrees - 90f, length, aimLeft, 0f);
            var top = Along(hand, aimDegrees - 90f, length * 0.47f);
            var bottom = Along(hand, aimDegrees + 90f, length * 0.47f);
            // Loosed at the start of the use; the string is drawn back again for the next arrow.
            var pull = p < 0.2f ? 0f : Smooth(Math.Min(1f, (p - 0.2f) / 0.6f)) * length * 0.38f;
            var nock = Along(Along(hand, aimDegrees, -length * 0.12f), aimDegrees, -pull);
            using (var bowstring = new Pen(Color.FromArgb(230, 235, 230, 210), 1f))
                g.DrawLines(bowstring, [Along(top, aimDegrees, -length * 0.1f), nock, Along(bottom, aimDegrees, -length * 0.1f)]);
            if (pull > 2f)
            {
                // The drawing hand on the string, with an arrow nocked.
                using var arrow = new Pen(Color.FromArgb(220, 200, 170, 120), 1.5f);
                g.DrawLine(arrow, nock, Along(nock, aimDegrees, length * 0.55f));
                var glove = MathF.Max(2f, body * 0.05f);
                using var gloveBrush = new SolidBrush(colors.Glove);
                g.FillEllipse(gloveBrush, nock.X - glove, nock.Y - glove, glove * 2f, glove * 2f);
            }
            DrawArm(g, rig, hand, colors);
            _heldHand = hand;
        }

        private void DrawHoldUp(Graphics g, in PlayerRig rig, ItemDefinition item, float p, (Color Sleeve, Color Glove) colors, double now)
        {
            var body = rig.BodyHeight;
            var raise = EaseOut(Math.Min(1f, p / 0.22f));
            var lower = p > 0.85f ? (p - 0.85f) / 0.15f : 0f;
            var degrees = rig.Screen(Lerp(Lerp(10f, -88f, raise), 10f, lower * 0.6f));
            var hand = rig.Hand(degrees);
            var color = item.SummonsBoss is not null ? Color.FromArgb(255, 80, 120)
                : item.StartsEvent is not null ? Color.FromArgb(255, 190, 70)
                : item.Style is WeaponStyle.SummonStaff or WeaponStyle.SentryStaff || item.UseKind == ItemUseKind.Command ? Color.FromArgb(190, 130, 255)
                : Color.FromArgb(130, 230, 255);
            var staff = item.Style is WeaponStyle.SummonStaff or WeaponStyle.SentryStaff;
            PointF top;
            if (staff)
            {
                var art = ItemArt(item.Id, body * 0.75f);
                // Raised up and forward, clear of the helmet.
                top = art is null ? Along(hand, degrees, body * 0.6f) : DrawItemAlong(g, art, hand, rig.Screen(-62f), body * 0.72f, rig.Facing < 0, 0.15f);
            }
            else
            {
                var size = body * 0.42f;
                top = new PointF(hand.X, hand.Y - size * 0.55f);
                // A soft halo behind the item while it is held aloft.
                if (raise > 0.6f)
                    DrawGlowDot(g, top, size * (1.3f + 0.2f * (float)Math.Sin(now * 9)), color, 200);
                if (ItemArt(item.Id, size, rig.Facing < 0) is { } art)
                    DrawItemCentered(g, art, top, (float)Math.Sin(now * 6) * 4f);
            }
            if (raise > 0.6f)
            {
                if (staff)
                    DrawGlowDot(g, top, 10f + 5f * (float)Math.Sin(now * 10), color);
                if (Chance(0.6f))
                    Spark(rig, top, color, now, 2.5f, -5f, 2.5f, 0.6f);
            }
            DrawArm(g, rig, hand, colors);
            _heldHand = hand;
        }

        private void DrawEatDrink(Graphics g, in PlayerRig rig, ItemDefinition item, bool drink, float p, (Color Sleeve, Color Glove) colors, double now)
        {
            var body = rig.BodyHeight;
            var rest = rig.Hand(rig.Screen(55f));
            var mouth = new PointF(rig.Mouth.X + rig.Facing * body * 0.05f, rig.Mouth.Y + body * 0.04f);
            var up = p < 0.2f ? Smooth(p / 0.2f) : p > 0.85f ? 1f - Smooth((p - 0.85f) / 0.15f) : 1f;
            var chewing = p is >= 0.2f and <= 0.85f;
            var chew = chewing && !drink ? MathF.Abs((float)Math.Sin(p * 38f)) * body * 0.03f : 0f;
            var hand = new PointF(Lerp(rest.X, mouth.X, up), Lerp(rest.Y, mouth.Y, up) + chew);
            var size = body * 0.34f;
            // A bottle tips up toward the face; food turns a little as it is bitten.
            var tilt = drink ? -rig.Facing * 75f * Smooth(Math.Clamp((p - 0.15f) / 0.3f, 0f, 1f)) * up : -rig.Facing * 15f * up;
            if (ItemArt(item.Id, size, rig.Facing < 0) is { } art)
                DrawItemCentered(g, art, new PointF(hand.X + rig.Facing * size * 0.25f, hand.Y - size * 0.15f), tilt);
            if (chewing && Chance(drink ? 0.25f : 0.4f))
            {
                var color = drink
                    ? item.ChargeRestore > 0f ? Color.FromArgb(110, 170, 255) : item.HealAmount > 0f ? Color.FromArgb(255, 90, 100) : Color.FromArgb(150, 240, 150)
                    : ItemTone(item);
                _animator.Emit(ToWorld(rig, mouth), new Vector2(rig.Facing * ((float)_useRandom.NextDouble() * 2f), -1f - (float)_useRandom.NextDouble() * 1.5f),
                    color, now, 0.5f, 2.5f, 20f);
            }
            DrawArm(g, rig, hand, colors);
            _heldHand = hand;
        }

        private readonly Dictionary<ItemId, Color> _itemTones = new();

        /// <summary>The average opaque colour of an item's art (crumbs take it).</summary>
        private Color ItemTone(ItemDefinition item)
        {
            if (_itemTones.TryGetValue(item.Id, out var tone))
                return tone;
            tone = Color.FromArgb(210, 170, 110);
            if (ItemArt(item.Id, 16) is { } art)
            {
                long r = 0, gr = 0, b = 0, n = 0;
                for (var y = 0; y < art.Height; y += 2)
                for (var x = 0; x < art.Width; x += 2)
                {
                    var c = art.GetPixel(x, y);
                    if (c.A < 128)
                        continue;
                    r += c.R;
                    gr += c.G;
                    b += c.B;
                    n++;
                }
                if (n > 0)
                    tone = Color.FromArgb((int)(r / n), (int)(gr / n), (int)(b / n));
            }
            return _itemTones[item.Id] = tone;
        }

        private static Color StaffColor(ItemDefinition item) => item.Class switch
        {
            DamageClass.Command => Color.FromArgb(200, 140, 255),
            DamageClass.Ranged => Color.FromArgb(255, 220, 120),
            DamageClass.Melee => Color.FromArgb(255, 160, 90),
            _ => item.Tier >= 6 ? Color.FromArgb(255, 140, 230) : Color.FromArgb(130, 225, 255)
        };

        private void DrawMuzzleFlash(Graphics g, PointF tip, float degrees, float size)
        {
            var state = g.Save();
            g.TranslateTransform(tip.X, tip.Y);
            g.RotateTransform(degrees);
            using var outer = new SolidBrush(Color.FromArgb(170, 255, 200, 90));
            using var inner = new SolidBrush(Color.FromArgb(240, 255, 250, 220));
            g.FillPolygon(outer, [new PointF(0, -4 * size), new PointF(11 * size, 0), new PointF(0, 4 * size), new PointF(3 * size, 0)]);
            g.FillEllipse(inner, -2 * size, -2.5f * size, 6 * size, 5 * size);
            g.Restore(state);
        }

        private void DrawRuneRing(Graphics g, PointF center, float radius, float p, double now, Color color)
        {
            var alpha = (int)(170 * (p < 0.2f ? p / 0.2f : 1f - Math.Max(0f, p - 0.7f) / 0.3f));
            if (alpha <= 4)
                return;
            using var ring = new Pen(Color.FromArgb(alpha, color), 1.5f);
            g.DrawEllipse(ring, center.X - radius, center.Y - radius * 0.55f, radius * 2f, radius * 1.1f);
            using var rune = new SolidBrush(Color.FromArgb(alpha, color));
            for (var i = 0; i < 5; i++)
            {
                var angle = now * 2.4 + i * Math.PI * 2 / 5;
                g.FillRectangle(rune, center.X + (float)Math.Cos(angle) * radius - 1.5f, center.Y + (float)Math.Sin(angle) * radius * 0.55f - 1.5f, 3f, 3f);
            }
        }

        private readonly Dictionary<int, Bitmap> _glowSprites = new();

        /// <summary>A soft radial glow of <paramref name="radius"/> (pre-rendered once per colour, then just scaled).</summary>
        private void DrawGlowDot(Graphics g, PointF center, float radius, Color color, int alpha = 255)
        {
            if (radius < 0.5f || alpha <= 0)
                return;
            var key = color.ToArgb() & 0xFFFFFF | (alpha / 16) << 24;
            if (!_glowSprites.TryGetValue(key, out var glow))
            {
                // Alpha falls off with the square of the distance from the centre; built pixel by pixel once.
                glow = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var peak = Math.Min(255, alpha / 16 * 16 + 15);
                for (var y = 0; y < 32; y++)
                for (var x = 0; x < 32; x++)
                {
                    var d = MathF.Sqrt((x - 15.5f) * (x - 15.5f) + (y - 15.5f) * (y - 15.5f)) / 16f;
                    var a = d >= 1f ? 0 : (int)(peak * (1f - d) * (1f - d));
                    glow.SetPixel(x, y, Color.FromArgb(a, color.R, color.G, color.B));
                }
                _glowSprites[key] = glow;
            }
            g.DrawImage(glow, center.X - radius, center.Y - radius, radius * 2f, radius * 2f);
        }
    }
}
