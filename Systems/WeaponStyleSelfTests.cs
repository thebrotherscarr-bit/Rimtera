using System.Numerics;
using System.Reflection;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Deterministic checks for weapon styles (GameSimulation.WeaponStyles.cs) and the style assignment pass.</summary>
public static class WeaponStyleSelfTests
{
    private static GameSimulation NewGame(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 80, height: 48) { FamilySpawnsEnabled = false };
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e6f);
        for (var y = 4; y <= 21; y++)
        for (var x = 6; x <= 74; x++)
        {
            var cell = new GridPoint(x, y);
            game.World.SetTile(cell, y >= 20 ? TileId.Rock : TileId.Air);
            var data = game.World.GetCell(cell);
            data.LiquidDepth = 0f;
            data.LiquidKind = LiquidKind.None;
            game.World.SetCell(cell, data);
        }
        game.Player.Motion.Position = new Vector2(30f, 20f - game.Player.Motion.Size.Y - 0.01f);
        game.Player.Motion.Velocity = Vector2.Zero;
        Step(game, 3);
        return game;
    }

    private static Vector2 Center(GameSimulation game) => game.Player.Motion.Position + game.Player.Motion.Size / 2f;
    private static GridPoint Cell(Vector2 point) => new((int)MathF.Floor(point.X), (int)MathF.Floor(point.Y));
    private static GridPoint Ahead(GameSimulation game, float dx) => Cell(Center(game) + new Vector2(dx, 0f));

    private static readonly List<(EnemyAgent Enemy, Vector2 Position)> Pinned = new();

    private static EnemyAgent Spawn(GameSimulation game, EnemyKind kind, Vector2 offset)
    {
        var enemy = game.SpawnCreature(kind, Center(game) + offset);
        var position = Center(game) + offset - enemy.Motion.Size / 2f;
        enemy.Motion.Position = position;
        enemy.Motion.Velocity = Vector2.Zero;
        Pinned.Add((enemy, position));
        return enemy;
    }

    private static void Step(GameSimulation game, int ticks = 1, PlayerAction action = PlayerAction.None, GridPoint? target = null)
    {
        for (var i = 0; i < ticks; i++)
        {
            foreach (var (enemy, position) in Pinned)
            {
                enemy.Motion.Position = position;
                enemy.Motion.Velocity = Vector2.Zero;
            }
            game.Tick(new GameInput(default, action, target));
        }
    }

    private static void Give(GameSimulation game, ItemId item, int count = 1)
    {
        game.Player.Inventory.Add(item, count);
        game.TrySelectInventorySlot(game.Player.Inventory.FindSlot(item));
        game.Player.ActionCooldownTicks = 0;
    }

    private static Dictionary<ItemId, ItemDefinition> CatalogOf(GameSimulation game) =>
        (Dictionary<ItemId, ItemDefinition>)typeof(ItemCatalog).GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game.Items)!;

    private static void SetCharge(GameSimulation game, float value) =>
        typeof(GameSimulation).GetField("_charge", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(game, value);

    private static ProjectileState? Styled(GameSimulation game, WeaponStyle style) =>
        game.Projectiles.FirstOrDefault(projectile => projectile.Style == style);

    public static void Run(Action<bool, string> check)
    {
        Pinned.Clear();

        // ---- Assignment pass ----
        var catalog = new ItemCatalog();
        var weapons = catalog.All.Where(item => item.Calibratable && (item.UseKind != ItemUseKind.Command || item.Summons is not null)).ToArray();
        var unstyled = weapons.Where(item => item.Style == WeaponStyle.None).Select(item => item.Id.ToString()).ToArray();
        check(weapons.Length >= 90 && unstyled.Length == 0 &&
              catalog[ItemId.ShockSpear].Style == WeaponStyle.Spear && catalog[ItemId.ReturnBoomerang].Style == WeaponStyle.Boomerang &&
              catalog[ItemId.ChainFlail].Style == WeaponStyle.Flail && catalog[ItemId.YoyoDrone].Style == WeaponStyle.Yoyo &&
              catalog[ItemId.TetherWhip].Style == WeaponStyle.Whip && catalog[ItemId.SignalWhipChain].Style == WeaponStyle.Whip &&
              catalog[ItemId.FerriteShiv].Style == WeaponStyle.Shortsword && catalog[ItemId.ChainsawSword].Style == WeaponStyle.Broadsword &&
              catalog[ItemId.LumeniteLongbow] is { Style: WeaponStyle.Bow, UsesAmmo: AmmoClass.Arrow } &&
              catalog[ItemId.PumpScattergun] is { Style: WeaponStyle.Shotgun, UsesAmmo: AmmoClass.Bullet } &&
              catalog[ItemId.MortarTube] is { Style: WeaponStyle.Launcher, UsesAmmo: AmmoClass.Rocket } &&
              catalog[ItemId.CopperBow] is { Style: WeaponStyle.Gun, UsesAmmo: AmmoClass.Cell } &&
              catalog[ItemId.RadBeamRod].Style == WeaponStyle.Beam && catalog[ItemId.DatapadNova].Style == WeaponStyle.Tome &&
              catalog[ItemId.ScoutDroneRemote].Style == WeaponStyle.SummonStaff && catalog[ItemId.TurretAutogun].Style == WeaponStyle.SentryStaff &&
              catalog[ItemId.Arrow].AmmoKind == AmmoClass.Cell && catalog[ItemId.SlugRounds].AmmoKind == AmmoClass.Bullet,
            $"the style pass gives every existing weapon a style and ammo a family (weapons {weapons.Length}, unstyled: {string.Join(", ", unstyled.Take(5))}; " +
            string.Join(", ", new[] { ItemId.ShockSpear, ItemId.FerriteShiv, ItemId.ChainsawSword, ItemId.LumeniteLongbow, ItemId.PumpScattergun, ItemId.MortarTube, ItemId.CopperBow, ItemId.RadBeamRod, ItemId.DatapadNova, ItemId.TurretAutogun }
                .Select(id => $"{id}={catalog[id].Style}/{catalog[id].UsesAmmo}")) + ")");

        // ---- Broadsword: a wide arc in front, each enemy once ----
        var sword = NewGame(7101);
        var front = Spawn(sword, EnemyKind.GlacialGolem, new Vector2(1.7f, 0f));
        var high = Spawn(sword, EnemyKind.GlacialGolem, new Vector2(1.1f, -1.4f));
        var behind = Spawn(sword, EnemyKind.GlacialGolem, new Vector2(-2.6f, 0f));
        Give(sword, ItemId.CopperSword);
        Step(sword, 1, PlayerAction.UseSelected, Ahead(sword, 4f));
        var swingSeen = sword.PlayerSwing is { Style: WeaponStyle.Broadsword } view && view.Progress > 0f;
        Step(sword, 30);
        var frontDrop = front.MaximumHitPoints - front.HitPoints;
        var highDrop = high.MaximumHitPoints - high.HitPoints;
        check(swingSeen && frontDrop > 0f && Math.Abs(frontDrop - highDrop) < 0.01f && behind.HitPoints == behind.MaximumHitPoints &&
              sword.PlayerSwing is null,
            "broadswords sweep an arc in front, hitting each enemy in it exactly once and nothing behind");

        // ---- Spear outreaches the shortsword ----
        bool Reaches(ItemId weapon, int seed)
        {
            Pinned.Clear();
            var game = NewGame(seed);
            var target = Spawn(game, EnemyKind.GlacialGolem, new Vector2(4.4f, 0f));
            Give(game, weapon);
            Step(game, 1, PlayerAction.UseSelected, Ahead(game, 4.4f));
            Step(game, 30);
            return target.HitPoints < target.MaximumHitPoints;
        }
        var spearHits = Reaches(ItemId.ShockSpear, 7102);
        var shivHits = Reaches(ItemId.FerriteShiv, 7103);
        Pinned.Clear();
        var stab = NewGame(7104);
        var close = Spawn(stab, EnemyKind.GlacialGolem, new Vector2(1.4f, 0f));
        Give(stab, ItemId.FerriteShiv);
        Step(stab, 1, PlayerAction.UseSelected, Ahead(stab, 3f));
        var stabView = stab.PlayerSwing;
        Step(stab, 12);
        check(spearHits && !shivHits && close.HitPoints < close.MaximumHitPoints && stabView is { Style: WeaponStyle.Shortsword } &&
              stab.Items[ItemId.FerriteShiv].UseCooldownTicks < stab.Items[ItemId.ShockSpear].UseCooldownTicks,
            "spears thrust far down the aim line; shortswords stab fast but short");

        // ---- Yo-yo: tethered within range while held, returns on release ----
        Pinned.Clear();
        var yoyoGame = NewGame(7105);
        var yoyoTarget = Spawn(yoyoGame, EnemyKind.GlacialGolem, new Vector2(4.2f, 0f));
        Give(yoyoGame, ItemId.YoyoDrone);
        Step(yoyoGame, 60, PlayerAction.UseSelected, Ahead(yoyoGame, 12f));
        var yoyo = Styled(yoyoGame, WeaponStyle.Yoyo);
        var heldOut = yoyo is { Tethered: true, Returning: false };
        var yoyoRange = 4f + 0.6f * yoyoGame.Items[ItemId.YoyoDrone].Tier;
        var yoyoDistance = yoyo is null ? 0f : Vector2.Distance(yoyo.Position, Center(yoyoGame));
        var perHit = yoyoGame.Items[ItemId.YoyoDrone].Damage * 0.5f;
        var yoyoDamage = yoyoTarget.MaximumHitPoints - yoyoTarget.HitPoints;
        Step(yoyoGame, 90);
        check(heldOut && yoyoDistance > yoyoRange - 0.6f && yoyoDistance <= yoyoRange + 0.01f &&
              yoyoDamage > 2f * perHit && Styled(yoyoGame, WeaponStyle.Yoyo) is null,
            "a held yo-yo stays tethered within its range hitting repeatedly, then returns once released");

        // ---- Boomerang: out, back, both legs, one at a time ----
        Pinned.Clear();
        var rang = NewGame(7106);
        var rangTarget = Spawn(rang, EnemyKind.GlacialGolem, new Vector2(3f, 0f));
        Give(rang, ItemId.ReturnBoomerang);
        Step(rang, 1, PlayerAction.UseSelected, Ahead(rang, 12f));
        rang.Player.ActionCooldownTicks = 0;
        Step(rang, 1, PlayerAction.UseSelected, Ahead(rang, 12f));
        var oneOut = rang.Projectiles.Count(projectile => projectile.Style == WeaponStyle.Boomerang) == 1;
        float maxOut = 0f, firstLeg = 0f;
        var returned = false;
        for (var i = 0; i < 180 && Styled(rang, WeaponStyle.Boomerang) is { } flying; i++)
        {
            maxOut = MathF.Max(maxOut, Vector2.Distance(flying.Position, Center(rang)));
            if (flying.Returning && !returned)
            {
                returned = true;
                firstLeg = rangTarget.MaximumHitPoints - rangTarget.HitPoints;
            }
            Step(rang);
        }
        var bothLegs = rangTarget.MaximumHitPoints - rangTarget.HitPoints;
        check(oneOut && returned && Styled(rang, WeaponStyle.Boomerang) is null && maxOut > 5f && maxOut < 8.5f &&
              firstLeg > 0f && bothLegs > firstLeg * 1.5f,
            "boomerangs fly out, turn around, come back to the thrower and hit on both legs; one out at a time");

        // ---- Flail: launched on a chain and reeled back ----
        Pinned.Clear();
        var flailGame = NewGame(7107);
        Give(flailGame, ItemId.ChainFlail);
        Step(flailGame, 1, PlayerAction.UseSelected, Ahead(flailGame, 12f));
        var ball = Styled(flailGame, WeaponStyle.Flail);
        float flailMax = 0f;
        var reeled = false;
        for (var i = 0; i < 120 && Styled(flailGame, WeaponStyle.Flail) is { } live; i++)
        {
            flailMax = MathF.Max(flailMax, Vector2.Distance(live.Position, Center(flailGame)));
            reeled |= live.Returning;
            Step(flailGame);
        }
        check(ball is { Tethered: true } && reeled && flailMax > 4f && flailMax <= flailGame.Items[ItemId.ChainFlail].Reach + 0.6f &&
              Styled(flailGame, WeaponStyle.Flail) is null,
            "flails launch on a chain toward the aim and reel back to the wielder");

        // ---- Whip marks; drones hit marked targets harder ----
        Pinned.Clear();
        var whipGame = NewGame(7108);
        Give(whipGame, ItemId.ScoutDroneRemote);
        Step(whipGame, 1, PlayerAction.UseSelected, Ahead(whipGame, 1f));
        var marked = Spawn(whipGame, EnemyKind.GlacialGolem, new Vector2(5f, 0f));
        Give(whipGame, ItemId.TetherWhip);
        Step(whipGame, 1, PlayerAction.UseSelected, Ahead(whipGame, 5f));
        var lash = whipGame.PlayerWhip;
        var seen = whipGame.Projectiles.Select(projectile => projectile.Id).ToHashSet();
        Step(whipGame, 20);
        var isMarked = whipGame.IsMarkedByWhip(marked);
        var droneShot = 0f;
        for (var i = 0; i < 120 && droneShot == 0f; i++)
        {
            Step(whipGame);
            droneShot = whipGame.Projectiles.Where(projectile => !seen.Contains(projectile.Id) && projectile.Class == DamageClass.Command)
                .Select(projectile => projectile.Damage).DefaultIfEmpty(0f).Max();
        }
        var droneBase = whipGame.Minions.FirstOrDefault()?.Damage ?? 0f;
        check(lash is { Points.Count: > 4 } && marked.HitPoints < marked.MaximumHitPoints && isMarked &&
              droneBase > 0f && droneShot > droneBase * 1.2f,
            "whips lash along their length and mark the target; drones deal bonus damage to marked enemies");

        // ---- Bow: draws arrow-family ammo, refuses without ---
        Pinned.Clear();
        var archer = NewGame(7109);
        var bow = archer.Items[ItemId.LumeniteLongbow];
        var arrowAmmo = archer.Items.All.FirstOrDefault(item => item.AmmoKind == AmmoClass.Arrow)?.Id ?? bow.AmmoType!.Value;
        foreach (var ammoItem in archer.Items.All.Where(item => item.AmmoKind == AmmoClass.Arrow || item.Id == bow.AmmoType))
            archer.Player.Inventory.TryConsume(ammoItem.Id, archer.Player.Inventory.Count(ammoItem.Id));
        Give(archer, ItemId.LumeniteLongbow);
        var before = archer.Projectiles.Count;
        Step(archer, 1, PlayerAction.UseSelected, Ahead(archer, 8f));
        var dry = archer.Projectiles.Count == before;
        archer.Player.Inventory.Add(arrowAmmo, 3);
        archer.Player.ActionCooldownTicks = 0;
        Step(archer, 1, PlayerAction.UseSelected, Ahead(archer, 8f));
        check(dry && archer.Player.Inventory.Count(arrowAmmo) == 2 && Styled(archer, WeaponStyle.Bow) is { SourceItem: ItemId.LumeniteLongbow },
            "bows consume one arrow-family round per shot and fail to fire without one");

        // ---- Shotgun spread ----
        var shotgun = NewGame(7110);
        Give(shotgun, ItemId.PumpScattergun);
        shotgun.Player.Inventory.Add(ItemId.ShotgunShells, 10);
        Step(shotgun, 1, PlayerAction.UseSelected, Ahead(shotgun, 8f));
        check(shotgun.Projectiles.Count(projectile => projectile.Style == WeaponStyle.Shotgun) == shotgun.Items[ItemId.PumpScattergun].ProjectileCount &&
              shotgun.Player.Inventory.Count(ItemId.ShotgunShells) == 9,
            "shotguns fire their full pellet count per shell");

        // ---- Launcher: area damage, no tiles, no self-harm ----
        Pinned.Clear();
        var boom = NewGame(7111);
        var critterA = Spawn(boom, EnemyKind.GlowGrub, new Vector2(2.2f, 0f));
        var critterB = Spawn(boom, EnemyKind.GlowGrub, new Vector2(3.6f, 0.2f));
        Give(boom, ItemId.MortarTube);
        boom.Player.Inventory.Add(ItemId.MiniRocket, 5);
        var hpBefore = boom.Player.HitPoints;
        var solidBefore = CountSolid(boom);
        Step(boom, 1, PlayerAction.UseSelected, Ahead(boom, 3f));
        var sawBlast = false;
        for (var i = 0; i < 40; i++)
        {
            Step(boom);
            sawBlast |= boom.RecentExplosions.Count > 0;
        }
        check(sawBlast && (!critterA.IsAlive || critterA.HitPoints < critterA.MaximumHitPoints) &&
              (!critterB.IsAlive || critterB.HitPoints < critterB.MaximumHitPoints) &&
              boom.Player.HitPoints >= hpBefore && CountSolid(boom) == solidBefore && boom.RecentExplosions.Count == 0,
            "launcher rounds explode for area damage on several creatures, never the player or tiles");

        // ---- Thrown: consumes the item, arcs under gravity ----
        Pinned.Clear();
        var thrower = NewGame(7112);
        var items = CatalogOf(thrower);
        items[ItemId.ThrowingDisc] = items[ItemId.ThrowingDisc] with { MaxStack = 99, Style = WeaponStyle.Thrown, ProjectileSpeed = 14f };
        Give(thrower, ItemId.ThrowingDisc, 5);
        Step(thrower, 1, PlayerAction.UseSelected, Ahead(thrower, 8f));
        var knife = Styled(thrower, WeaponStyle.Thrown);
        var vy0 = knife?.Velocity.Y ?? 0f;
        Step(thrower, 5);
        check(thrower.Player.Inventory.Count(ItemId.ThrowingDisc) == 4 && knife is not null && knife.Velocity.Y > vy0 + 0.5f,
            "thrown weapons consume one of themselves per throw and arc under gravity");

        // ---- Beam: continuous while held, drains charge, stops on release ----
        Pinned.Clear();
        var beamGame = NewGame(7113);
        var beamTarget = Spawn(beamGame, EnemyKind.GlacialGolem, new Vector2(5f, 0f));
        Give(beamGame, ItemId.RadBeamRod);
        SetCharge(beamGame, beamGame.MaximumCharge);
        var chargeStart = beamGame.Charge;
        Step(beamGame, 20, PlayerAction.UseSelected, Ahead(beamGame, 5f));
        var beamView = beamGame.PlayerBeam;
        var chargeHeld = beamGame.Charge;
        Step(beamGame, 1);
        var released = beamGame.PlayerBeam is null;
        Step(beamGame, 30);
        check(beamView is { } ray && Vector2.Distance(ray.Start, ray.End) > 4f && chargeHeld < chargeStart - 8f &&
              beamTarget.HitPoints < beamTarget.MaximumHitPoints && released && beamGame.Charge >= chargeHeld,
            "beams fire continuously while held, draining charge per damage tick, and stop on release ({beamView} {chargeStart:0}->{chargeHeld:0}->{beamGame.Charge:0} hp={beamTarget.HitPoints:0}/{beamTarget.MaximumHitPoints:0} rel={released} {beamGame.LastMessage})");

        // ---- Staff / tome tagging ----
        var caster = NewGame(7114);
        Give(caster, ItemId.DatapadNova);
        SetCharge(caster, caster.MaximumCharge);
        Step(caster, 1, PlayerAction.UseSelected, Ahead(caster, 8f));
        check(caster.Projectiles.Count(projectile => projectile.Style == WeaponStyle.Tome) == caster.Items[ItemId.DatapadNova].ProjectileCount,
            "tomes burst several charge bolts at once");

        // ---- Legacy Style.None weapons keep the generic attack ----
        Pinned.Clear();
        var legacy = NewGame(7115);
        var legacyItems = CatalogOf(legacy);
        legacyItems[ItemId.CopperSword] = legacyItems[ItemId.CopperSword] with { Style = WeaponStyle.None };
        Give(legacy, ItemId.CopperSword);
        Step(legacy, 1, PlayerAction.UseSelected, Ahead(legacy, 30f));
        var legacyMessage = legacy.LastMessage;
        var laser = NewGame(7116);
        laser.Player.Inventory.Add(ItemId.CopperBow, 1);
        laser.Player.Inventory.Add(ItemId.Arrow, 80);
        laser.TrySelectInventorySlot(laser.Player.Inventory.FindSlot(ItemId.CopperBow));
        var cells = laser.Player.Inventory.Count(ItemId.Arrow);
        Step(laser, 1, PlayerAction.UseSelected, Ahead(laser, 8f));
        check(legacy.PlayerSwing is null && legacyMessage.Contains("out of reach", StringComparison.OrdinalIgnoreCase) &&
              laser.Player.Inventory.Count(ItemId.Arrow) == cells - 1 && laser.Projectiles.Count == 1 &&
              laser.Projectiles[0].Style == WeaponStyle.Gun,
            "Style.None weapons use the original attack; energy-cell guns keep the original firing code");
        Pinned.Clear();
    }

    private static int CountSolid(GameSimulation game)
    {
        var count = 0;
        for (var y = 0; y < game.World.Height; y++)
        for (var x = 0; x < game.World.Width; x++)
            if (game.Tiles[game.World.GetCell(new GridPoint(x, y)).TileId].IsSolid)
                count++;
        return count;
    }
}
