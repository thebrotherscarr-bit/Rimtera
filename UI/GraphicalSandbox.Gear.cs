using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using Rimtera.Entities;
using Rimtera.Systems;

namespace Rimtera.UI;

/// <summary>
/// Visual categories of worn gear. Each worn item maps onto these from its tags (ImmunityTags, e.g. "light:5", "flight",
/// "double-jump") and stat fields (move speed, defense, regeneration…); each category has one small visual.
/// </summary>
[Flags]
internal enum GearVisual : uint
{
    None = 0,
    Wings = 1 << 0,
    RocketPack = 1 << 1,
    RocketBoots = 1 << 2,
    DoubleJump = 1 << 3,
    Speed = 1 << 4,
    Defense = 1 << 5,
    Thorns = 1 << 6,
    Dodge = 1 << 7,
    Light = 1 << 8,
    NightVision = 1 << 9,
    Regen = 1 << 10,
    Charge = 1 << 11,
    Command = 1 << 12,
    MeleeBoost = 1 << 13,
    RangedBoost = 1 << 14,
    TechBoost = 1 << 15,
    Crit = 1 << 16,
    Poison = 1 << 17,
    Worker = 1 << 18,
    OreDetector = 1 << 19,
    DangerSense = 1 << 20,
    Magnet = 1 << 21,
    Water = 1 << 22,
    WaterWalk = 1 << 23,
    Fire = 1 << 24,
    Cold = 1 << 25,
    Climb = 1 << 26,
    Featherfall = 1 << 27,
    Fishing = 1 << 28,
    Grapple = 1 << 29
}

internal static class GearVisuals
{
    /// <summary>The visual categories one worn item belongs to.</summary>
    public static GearVisual For(ItemDefinition item)
    {
        var visual = GearVisual.None;
        foreach (var raw in item.ImmunityTags)
        {
            var colon = raw.IndexOf(':');
            var tag = (colon >= 0 ? raw[..colon] : raw).ToLowerInvariant();
            visual |= tag switch
            {
                "wings" or "glide" or "glide-rig" or "wing-climb" => GearVisual.Wings,
                "flight" => GearVisual.RocketPack,
                "rocket-boots" => GearVisual.RocketBoots,
                "double-jump" => GearVisual.DoubleJump,
                "endurance" or "knockback-immune" => GearVisual.Defense,
                "thorns" => GearVisual.Thorns | GearVisual.Defense,
                "dodge" => GearVisual.Dodge,
                "light" => GearVisual.Light,
                "night-vision" or "dark" => GearVisual.NightVision,
                "lifesteal" => GearVisual.Regen,
                "chargehit" => GearVisual.Charge,
                "auto-swing" => GearVisual.MeleeBoost,
                "poison" => GearVisual.Poison,
                "mining" or "reach" => GearVisual.Worker,
                "ore-detector" => GearVisual.OreDetector,
                "danger-sense" => GearVisual.DangerSense,
                "magnet" => GearVisual.Magnet,
                "gills" or "breath" => GearVisual.Water,
                "water-walk" => GearVisual.WaterWalk,
                "lava" or "fire-block" or "heat" => GearVisual.Fire,
                "cold" => GearVisual.Cold,
                "climb" => GearVisual.Climb,
                "featherfall" => GearVisual.Featherfall,
                "grapple" => GearVisual.Grapple,
                _ => GearVisual.None
            };
        }
        if (item.WingFlightSeconds > 0f || item.Id.ToString().Contains("Wing", StringComparison.Ordinal))
            visual |= GearVisual.Wings;
        if (item.MoveSpeedMultiplier > 1.001f)
            visual |= GearVisual.Speed;
        if (item.EquipmentSlot is not (EquipmentSlot.Head or EquipmentSlot.Body or EquipmentSlot.Legs) && item.Defense > 0)
            visual |= GearVisual.Defense;
        if (item.LifeRegenerationPerHour > 0f || item.MaximumLifeBonus > 0f)
            visual |= GearVisual.Regen;
        if (item.MaximumManaBonus > 0f || item.ChargeRegenBonus > 0f || item.ManaRegenerationPerHour > 0f)
            visual |= GearVisual.Charge;
        if (item.DroneSlotBonus > 0 || item.SentrySlotBonus > 0 || item.CommandDamageBonus > 0f)
            visual |= GearVisual.Command;
        if (item.MeleeDamageBonus > 0f)
            visual |= GearVisual.MeleeBoost;
        if (item.RangedDamageBonus > 0f)
            visual |= GearVisual.RangedBoost;
        if (item.TechDamageBonus > 0f)
            visual |= GearVisual.TechBoost;
        if (item.CritBonus > 0)
            visual |= GearVisual.Crit;
        if (item.FishingPower > 0)
            visual |= GearVisual.Fishing;
        return visual;
    }
}

public static partial class GraphicalSandbox
{
    private sealed partial class GameCanvas
    {
        private GearVisual _gear;
        private ItemId? _wingItem;
        private string _gearKey = "";
        private double _gearNow = -1;
        private float _gearDelta;
        private float _lastRocketFuel;
        private readonly List<(Vector2 Position, string Look, bool Flip, float Bob, double Time)> _dashGhosts = new();
        private readonly Dictionary<string, Bitmap?> _wingHalves = new();
        private ImageAttributes? _ghostAttributes;

        /// <summary>Re-reads the worn gear when it changes and keeps the per-frame time step for emitters.</summary>
        private void UpdateGear(GameSimulation game, double now)
        {
            _gearDelta = _gearNow < 0 ? 0f : (float)Math.Clamp(now - _gearNow, 0, 0.1);
            _gearNow = now;
            var equipment = game.Player.Equipment;
            var key = string.Join(",", EquipmentLoadout.AllAccessorySlots.Concat([EquipmentSlot.Head, EquipmentSlot.Body, EquipmentSlot.Legs])
                .Select(slot => equipment[slot]?.ToString() ?? "-"));
            if (key == _gearKey)
                return;
            _gearKey = key;
            _gear = GearVisual.None;
            _wingItem = null;
            foreach (var slot in EquipmentLoadout.AllAccessorySlots.Concat([EquipmentSlot.Head, EquipmentSlot.Body, EquipmentSlot.Legs]))
            {
                if (equipment[slot] is not { } id || !game.Items.Contains(id))
                    continue;
                var visual = GearVisuals.For(game.Items[id]);
                _gear |= visual;
                if ((visual & GearVisual.Wings) != 0)
                    _wingItem ??= id;
            }
        }

        private bool Has(GearVisual visual) => (_gear & visual) != 0;

        /// <summary>Emitter chance this frame for a rate per second, scaled by the particle setting.</summary>
        private bool Every(float perSecond) => _useRandom.NextDouble() < perSecond * _gearDelta * _animator.ParticleDensity;

        // ------------------------------------------------------------------ behind the body

        /// <summary>Dash afterimages: a few fading cyan copies of the body along the dash.</summary>
        private void DrawDashGhosts(Graphics g, GameSimulation game, string look, ActorAnimator.Pose pose, string reference, int startX, int startY, double now)
        {
            var motion = game.Player.Motion;
            if (motion.DashTimeRemaining > 0f && (_dashGhosts.Count == 0 || now - _dashGhosts[^1].Time > 0.025))
                _dashGhosts.Add((motion.Position, look, pose.Flip, pose.Bob, now));
            _dashGhosts.RemoveAll(ghost => now - ghost.Time > 0.22);
            if (_dashGhosts.Count == 0 || !_menu.Preferences.FlashyEffects)
                return;
            _ghostAttributes ??= Ghost();
            foreach (var ghost in _dashGhosts)
                DrawSpriteActor(g, ghost.Look, ghost.Position, motion.Size, 1f, ghost.Flip, startX, startY, ghost.Bob, reference: reference, ghost: _ghostAttributes);
            if (motion.DashTimeRemaining > 0f && Every(60f))
                Spark(_rig, _rig.Feet, Color.FromArgb(200, 190, 170, 140), now, 2f, 6f, 3f, 0.4f);
        }

        private static ImageAttributes Ghost()
        {
            var attributes = new ImageAttributes();
            attributes.SetColorMatrix(new ColorMatrix
            {
                Matrix00 = 0.4f, Matrix11 = 0.7f, Matrix22 = 1f, Matrix33 = 0.32f, Matrix44 = 1f, Matrix40 = 0.1f, Matrix41 = 0.25f, Matrix42 = 0.35f
            });
            return attributes;
        }

        /// <summary>Wings on the back (folded on the ground, flapping in the air) and the rocket pack's jet.</summary>
        private void DrawGearBehind(Graphics g, GameSimulation game, in PlayerRig rig, double now)
        {
            var motion = game.Player.Motion;
            var airborne = !motion.Grounded && !motion.Climbing;
            var rocketing = game.IsRocketing;
            if (Has(GearVisual.Wings) && !game.IsMounted)
                DrawWings(g, game, rig, airborne, rocketing, now);
            if (Has(GearVisual.RocketPack) && rocketing && !Has(GearVisual.Wings))
            {
                var nozzle = new PointF(rig.Shoulder.X - rig.Facing * rig.BodyHeight * 0.2f, rig.Shoulder.Y + rig.BodyHeight * 0.22f);
                DrawJet(g, nozzle, rig.BodyHeight * 0.32f, now);
                if (Every(40f))
                    Spark(rig, new PointF(nozzle.X, nozzle.Y + rig.BodyHeight * 0.3f), Color.FromArgb(160, 170, 170, 175), now, 1.5f, -2f, 4f, 0.7f);
            }
        }

        private void DrawWings(Graphics g, GameSimulation game, in PlayerRig rig, bool airborne, bool rocketing, double now)
        {
            var motion = game.Player.Motion;
            // Folded on the ground; spread while gliding; beating while climbing or on wing power.
            float lift;
            if (!airborne)
                lift = 0.35f + 0.04f * (float)Math.Sin(now * 2.2);
            else if (rocketing || motion.Velocity.Y < -0.5f)
                lift = 0.25f + 0.75f * MathF.Abs((float)Math.Sin(now * (rocketing ? 15 : 10)));
            else if (game.IsGliding)
                lift = 0.95f + 0.05f * (float)Math.Sin(now * 5);
            else
                lift = 0.7f + 0.2f * (float)Math.Sin(now * 6);
            var root = new PointF(rig.Shoulder.X - rig.Facing * rig.BodyHeight * 0.1f, rig.Shoulder.Y + rig.BodyHeight * 0.06f);
            var span = rig.BodyHeight * 0.62f;
            if (_wingItem is { } wingItem && WingHalf(wingItem) is { } half)
            {
                var scale = span / half.Width;
                // Two wings: the far one a little higher and darker shows through behind the near one.
                for (var layer = 0; layer < 2; layer++)
                {
                    var state = g.Save();
                    g.TranslateTransform(root.X + rig.Facing * layer * 2f, root.Y - layer * 2f);
                    // The wing beats about its root: down-swept when folded, raised at the top of a beat.
                    var flap = Lerp(-75f, 8f, lift) + (layer == 0 ? 0f : 10f); // the icon's wing already rises ~45°
                    g.RotateTransform(rig.Facing * flap);
                    // The half points away from the root (backward); mirror it for a left-facing colonist.
                    g.ScaleTransform(rig.Facing > 0 ? scale : -scale, scale * (0.75f + 0.25f * lift));
                    g.DrawImage(half, -half.Width, -half.Height * 0.8f, half.Width, half.Height);
                    g.Restore(state);
                }
            }
            else
            {
                // No art: a plain feathered wing.
                using var fill = new SolidBrush(Color.FromArgb(210, 225, 230, 240));
                using var edge = new Pen(Color.FromArgb(200, 40, 50, 70), 1f);
                var tipX = root.X - rig.Facing * span;
                PointF[] wing = [root, new PointF(tipX, root.Y - span * 0.9f * lift), new PointF(tipX + rig.Facing * span * 0.3f, root.Y - span * 0.2f * lift),
                    new PointF(root.X - rig.Facing * span * 0.4f, root.Y + span * 0.25f)];
                g.FillPolygon(fill, wing);
                g.DrawPolygon(edge, wing);
            }
            if (airborne && _menu.Preferences.FlashyEffects && Every(18f))
                Spark(rig, new PointF(root.X - rig.Facing * span * 0.6f, root.Y - span * 0.3f * lift), Color.FromArgb(200, 230, 240, 255), now, 1.5f, 3f, 2f, 0.5f);
        }

        /// <summary>The left half of a wing item's icon (the icons show both wings spread from a body in the middle).</summary>
        private Bitmap? WingHalf(ItemId item)
        {
            var name = SpriteCatalog.ForItem(item);
            if (name is null)
                return null;
            if (_wingHalves.TryGetValue(name, out var cached))
                return cached;
            Bitmap? half = null;
            if (_sprites.Get(name) is { } art && art.Width >= 8)
            {
                var width = art.Width / 2;
                half = new Bitmap(width, art.Height, PixelFormat.Format32bppPArgb);
                using var g = Graphics.FromImage(half);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.DrawImage(art, new Rectangle(0, 0, width, art.Height), 0, 0, width, art.Height, GraphicsUnit.Pixel);
            }
            return _wingHalves[name] = half;
        }

        private void DrawJet(Graphics g, PointF nozzle, float length, double now)
        {
            var flicker = 0.75f + 0.25f * (float)Math.Sin(now * 47) + 0.1f * (float)Math.Sin(now * 83);
            var l = length * flicker;
            var w = length * 0.28f;
            using var outer = new SolidBrush(Color.FromArgb(170, 255, 140, 40));
            using var inner = new SolidBrush(Color.FromArgb(230, 255, 240, 170));
            g.FillPolygon(outer, [new PointF(nozzle.X - w, nozzle.Y), new PointF(nozzle.X + w, nozzle.Y), new PointF(nozzle.X, nozzle.Y + l)]);
            g.FillPolygon(inner, [new PointF(nozzle.X - w * 0.5f, nozzle.Y), new PointF(nozzle.X + w * 0.5f, nozzle.Y), new PointF(nozzle.X, nozzle.Y + l * 0.55f)]);
        }

        // ------------------------------------------------------------------ in front of the body

        /// <summary>Boot jets and trails, climbing scrapes, the magnet field, the drone-link light, buff motes.</summary>
        private void DrawGearFront(Graphics g, GameSimulation game, in PlayerRig rig, double now)
        {
            var motion = game.Player.Motion;
            var speed = MathF.Abs(motion.Velocity.X);
            var running = motion.Grounded && speed > 3f;
            var feet = rig.Feet;
            var body = rig.BodyHeight;

            if (Has(GearVisual.RocketBoots) && game.IsRocketing)
            {
                DrawJet(g, new PointF(feet.X - body * 0.07f, feet.Y - 1f), body * 0.24f, now);
                DrawJet(g, new PointF(feet.X + body * 0.07f, feet.Y - 1f), body * 0.24f, now + 0.13);
                if (Every(35f))
                    Spark(rig, new PointF(feet.X, feet.Y + body * 0.2f), Color.FromArgb(160, 175, 175, 180), now, 1.5f, -1.5f, 4f, 0.7f);
            }
            // Wings and the rocket pack burn their fuel too: the pack shows embers while the fuel drops in flight.
            if (game.RocketFuel < _lastRocketFuel - 0.0001f && Has(GearVisual.Wings) && Every(25f))
                Spark(rig, new PointF(rig.Shoulder.X - rig.Facing * body * 0.15f, rig.Shoulder.Y + body * 0.1f), Color.FromArgb(220, 255, 230, 160), now, 1.5f, 4f, 2f, 0.4f);
            _lastRocketFuel = game.RocketFuel;

            // Speed boots kick up dust and leave streaks; fire, frost and water-walking boots leave their own trail.
            if (running && (Has(GearVisual.Speed) || speed > 9f))
            {
                if (Every(14f + speed * 2f))
                    Spark(rig, new PointF(feet.X - rig.Facing * body * 0.1f, feet.Y - 1f), Color.FromArgb(200, 175, 155, 120), now, 1.5f, 4f, 3f, 0.4f);
                if (_menu.Preferences.FlashyEffects && Has(GearVisual.Speed) && speed > 5f)
                {
                    var alpha = (int)Math.Clamp((speed - 5f) * 14f, 0f, 90f);
                    using var streak = new Pen(Color.FromArgb(alpha, 230, 240, 255), 1.2f);
                    for (var i = 0; i < 3; i++)
                    {
                        var y = rig.Shoulder.Y + body * (0.05f + i * 0.17f);
                        var x = rig.Center.X - rig.Facing * body * (0.25f + i * 0.06f);
                        g.DrawLine(streak, x, y, x - rig.Facing * body * (0.35f + 0.1f * (float)Math.Sin(now * 30 + i)), y);
                    }
                }
            }
            var moving = speed > 1.5f || !motion.Grounded;
            if (Has(GearVisual.Fire) && moving && Every(16f))
                Spark(rig, feet, Color.FromArgb(255, _useRandom.Next(110, 200), 40), now, 1.5f, -6f, 2.5f, 0.5f);
            if (Has(GearVisual.Cold) && moving && Every(10f))
                Spark(rig, feet, Color.FromArgb(220, 200, 235, 255), now, 1.2f, 2f, 2.5f, 0.7f);
            if (Has(GearVisual.WaterWalk) && running && Every(14f))
                Spark(rig, feet, Color.FromArgb(200, 120, 180, 255), now, 2f, 12f, 2.5f, 0.4f);

            // Featherfall: a slow fall sheds drifting motes.
            if ((Has(GearVisual.Featherfall) || game.Player.Statuses.IsActive(StatusId.Featherfall)) && !motion.Grounded && motion.Velocity.Y > 0.5f && Every(8f))
                Spark(rig, new PointF(rig.Center.X, rig.Shoulder.Y), Color.FromArgb(230, 250, 250, 255), now, 1f, 0.6f, 2.5f, 1.2f);

            // Climbing claws scrape the wall.
            if ((Has(GearVisual.Climb) || Has(GearVisual.Wings)) && game.IsWallClinging && Every(20f))
                Spark(rig, new PointF(rig.Center.X + rig.Facing * body * 0.22f, rig.Shoulder.Y + body * 0.2f), Color.FromArgb(220, 230, 220, 200), now, 1.5f, 8f, 2f, 0.3f);

            // Under water every colonist breathes out bubbles; gills make it a steady stream.
            if (game.IsSwimming && Every(Has(GearVisual.Water) ? 9f : 2.5f))
                Spark(rig, rig.Mouth, Color.FromArgb(200, 200, 230, 255), now, 0.8f, -6f, 3f, 0.9f);

            // A magnet draws faint turning arcs around the body.
            if (Has(GearVisual.Magnet) && _menu.Preferences.FlashyEffects)
            {
                var r = body * 0.62f;
                using var field = new Pen(Color.FromArgb(55, 200, 120, 255), 1.5f);
                var spin = (float)(now * 140 % 360);
                g.DrawArc(field, rig.Center.X - r, rig.Center.Y - r, r * 2f, r * 2f, spin, 50f);
                g.DrawArc(field, rig.Center.X - r, rig.Center.Y - r, r * 2f, r * 2f, spin + 180f, 50f);
            }

            // Drone and sentry links: a small light circling above the helmet.
            if (Has(GearVisual.Command))
            {
                var angle = now * 2.6;
                var at = new PointF(rig.Center.X + (float)Math.Cos(angle) * body * 0.3f, rig.Feet.Y - body * 1.08f + (float)Math.Sin(angle) * body * 0.05f);
                var blink = (int)(now * 3) % 2 == 0;
                using var dot = new SolidBrush(blink ? Color.FromArgb(230, 200, 140, 255) : Color.FromArgb(160, 120, 90, 200));
                g.FillEllipse(dot, at.X - 2f, at.Y - 2f, 4f, 4f);
            }

            // Class boosters and crit gear: sparks of the class colour off the hand while attacking.
            if (_heldHand is { } hand && _animator.UseProgress(now) is not null)
            {
                var item = game.Player.Inventory.SelectedItem;
                var color = item?.EffectiveClass switch
                {
                    DamageClass.Melee when Has(GearVisual.MeleeBoost) => Color.FromArgb(255, 160, 80),
                    DamageClass.Ranged when Has(GearVisual.RangedBoost) => Color.FromArgb(255, 225, 110),
                    DamageClass.Tech when Has(GearVisual.TechBoost) || Has(GearVisual.Charge) => Color.FromArgb(120, 220, 255),
                    DamageClass.Command when Has(GearVisual.Command) => Color.FromArgb(200, 140, 255),
                    _ => Color.Empty
                };
                if (!color.IsEmpty && Every(14f))
                    Spark(rig, hand, color, now, 2f, 2f, 2f, 0.35f);
                if (Has(GearVisual.Crit) && Every(4f))
                    Spark(rig, hand, Color.White, now, 3f, 0f, 3f, 0.25f);
                if (Has(GearVisual.Poison) && Every(6f))
                    Spark(rig, hand, Color.FromArgb(130, 230, 70), now, 0.6f, 12f, 2.5f, 0.5f);
                if (Has(GearVisual.Worker) && item is not null && ToolParity.IsTool(item) && Every(10f))
                    Spark(rig, hand, Color.FromArgb(255, 210, 120), now, 2.5f, 8f, 2f, 0.3f);
            }

            // Regeneration and charge gear: slow rising motes; buffs and debuffs add theirs.
            if (Has(GearVisual.Regen) && Every(1.6f))
                Spark(rig, new PointF(rig.Center.X + (float)(_useRandom.NextDouble() - 0.5) * body * 0.4f, rig.Center.Y), Color.FromArgb(220, 120, 255, 140), now, 0.6f, -3f, 3f, 1f);
            if (Has(GearVisual.Charge) && Every(1.6f))
                Spark(rig, new PointF(rig.Center.X + (float)(_useRandom.NextDouble() - 0.5) * body * 0.5f, rig.Shoulder.Y), Color.FromArgb(220, 120, 200, 255), now, 1f, -2f, 2.5f, 0.8f);
            if (game.Bobber is { } bobber && Has(GearVisual.Fishing) && Every(3f))
                _animator.Emit(bobber.Position, new Vector2(0f, -1.5f), Color.FromArgb(230, 255, 240, 160), now, 0.6f, 2.5f, 0f);
            EmitBuffMotes(game, rig, now);
        }

        private void EmitBuffMotes(GameSimulation game, in PlayerRig rig, double now)
        {
            var shown = 0;
            foreach (var status in game.Player.Statuses.Active)
            {
                if (shown++ >= 5)
                    break;
                var (color, gravity) = status.Id switch
                {
                    StatusId.Burning => (Color.FromArgb(255, 140, 40), -8f),
                    StatusId.Poison => (Color.FromArgb(120, 220, 60), 10f),
                    StatusId.Frostbite or StatusId.FrostGuard => (Color.FromArgb(170, 220, 255), 3f),
                    StatusId.Regeneration or StatusId.Rejuvenation or StatusId.Vitality or StatusId.Lifeforce => (Color.FromArgb(120, 255, 140), -3f),
                    StatusId.ManaFocus or StatusId.ChargeSurge or StatusId.Overcharge or StatusId.TechBoost => (Color.FromArgb(120, 200, 255), -3f),
                    StatusId.Haste or StatusId.Sprint => (Color.FromArgb(230, 240, 255), 0f),
                    StatusId.Guarded or StatusId.Armored or StatusId.Fortified or StatusId.Endurance or StatusId.Thornskin => (Color.FromArgb(200, 205, 220), -1f),
                    StatusId.Wrath or StatusId.CloseQuartersBoost => (Color.FromArgb(255, 110, 80), -3f),
                    StatusId.BallisticsBoost or StatusId.Precision => (Color.FromArgb(255, 220, 110), -3f),
                    StatusId.CommandBoost or StatusId.DroneLink or StatusId.SentryLink => (Color.FromArgb(200, 140, 255), -3f),
                    StatusId.Spelunker or StatusId.MiningBoost => (Color.FromArgb(255, 210, 90), -2f),
                    StatusId.Illuminated or StatusId.NightSight => (Color.FromArgb(255, 245, 190), -2f),
                    StatusId.Lucky => (Color.FromArgb(120, 255, 170), -2f),
                    StatusId.WellFed or StatusId.Satiated or StatusId.Gourmet => (Color.FromArgb(255, 200, 140), -2f),
                    StatusId.PotionSickness => (Color.FromArgb(170, 160, 120), 2f),
                    _ => (Color.FromArgb(220, 230, 230, 240), -2f)
                };
                if (Every(status.Id is StatusId.Burning or StatusId.Poison ? 8f : 1.4f))
                    Spark(rig, new PointF(rig.Center.X + (float)(_useRandom.NextDouble() - 0.5) * rig.BodyHeight * 0.45f,
                        rig.Center.Y + (float)(_useRandom.NextDouble() - 0.5) * rig.BodyHeight * 0.6f), color, now, 0.7f, gravity, 2.5f, 0.9f);
            }
        }

        // ------------------------------------------------------------------ over the lighting

        /// <summary>Glows that should not be darkened: lamps and visors, shield glints on a hit, detector pings, the double-jump ring.</summary>
        private void DrawGearAfterLight(Graphics g, GameSimulation game, in PlayerRig rig, double now)
        {
            if (game.Player.IsDowned)
                return;
            var body = rig.BodyHeight;
            var head = new PointF(rig.Mouth.X - rig.Facing * body * 0.05f, rig.Mouth.Y - body * 0.06f);
            if (Has(GearVisual.Light) || game.Player.Statuses.IsActive(StatusId.Illuminated))
                DrawGlowDot(g, new PointF(head.X + rig.Facing * body * 0.08f, head.Y - body * 0.04f), body * (0.6f + 0.04f * (float)Math.Sin(now * 3)),
                    Color.FromArgb(255, 235, 180), 90);
            if (Has(GearVisual.NightVision) || game.Player.Statuses.IsActive(StatusId.NightSight))
                DrawGlowDot(g, new PointF(rig.Mouth.X + rig.Facing * body * 0.02f, rig.Mouth.Y - body * 0.04f), body * 0.06f, Color.FromArgb(120, 255, 150), 150);

            var sinceHit = (float)(now - _animator.PlayerHitAt);
            if (Has(GearVisual.Defense | GearVisual.Dodge) && sinceHit is >= 0f and < 0.35f)
            {
                // A shield shimmer on the facing side with a highlight sweeping across it.
                var fade = 1f - sinceHit / 0.35f;
                var r = body * 0.55f;
                var box = new RectangleF(rig.Center.X - r, rig.Center.Y - r * 1.05f, r * 2f, r * 2.1f);
                using var shell = new Pen(Color.FromArgb((int)(150 * fade), 170, 220, 255), 2f);
                using var sheen = new Pen(Color.FromArgb((int)(230 * fade), 245, 250, 255), 2.5f);
                var face = rig.Facing > 0 ? -70f : 110f;
                g.DrawArc(shell, box, face, 140f);
                g.DrawArc(sheen, box, face + 140f * (sinceHit / 0.35f), 22f);
            }
            if (Has(GearVisual.Thorns) && sinceHit is >= 0f and < 0.25f)
            {
                var fade = 1f - sinceHit / 0.25f;
                using var spike = new Pen(Color.FromArgb((int)(220 * fade), 200, 120, 80), 2f);
                for (var i = 0; i < 8; i++)
                {
                    var angle = i * 45f + 22f;
                    var inner = Along(rig.Center, angle, body * 0.4f);
                    g.DrawLine(spike, inner, Along(inner, angle, body * 0.18f * (0.5f + fade)));
                }
            }

            // Ore detectors and danger sense ping outward every few seconds.
            DrawPing(g, rig, now, Has(GearVisual.OreDetector) || game.Player.Statuses.IsActive(StatusId.Spelunker), 3.0, 0.0, Color.FromArgb(255, 215, 90));
            DrawPing(g, rig, now, Has(GearVisual.DangerSense) || game.Player.Statuses.IsActive(StatusId.DangerSense), 3.0, 1.5, Color.FromArgb(255, 90, 90));

            var sinceDouble = (float)(now - _animator.DoubleJumpAt);
            if (sinceDouble is >= 0f and < 0.3f && _menu.Preferences.FlashyEffects)
            {
                var t = sinceDouble / 0.3f;
                var r = body * (0.2f + 0.45f * t);
                using var ring = new Pen(Color.FromArgb((int)(200 * (1f - t)), 240, 245, 255), 2f);
                g.DrawEllipse(ring, rig.Feet.X - r, rig.Feet.Y - r * 0.3f, r * 2f, r * 0.6f);
            }
        }

        private void DrawPing(Graphics g, in PlayerRig rig, double now, bool on, double period, double offset, Color color)
        {
            if (!on)
                return;
            var t = (float)(((now + offset) % period) / 1.1);
            if (t >= 1f)
                return;
            var r = rig.BodyHeight * (0.4f + 3.2f * t);
            using var ring = new Pen(Color.FromArgb((int)(120 * (1f - t)), color), 1.5f);
            g.DrawEllipse(ring, rig.Center.X - r, rig.Center.Y - r, r * 2f, r * 2f);
        }
    }
}
