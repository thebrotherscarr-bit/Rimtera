using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Probes for the tool rules matched to the reference in docs/TOOLS_PARITY.md: use profiles and per-tool reach, the
/// swing and press/hold model, which tool hits which tile, the demolisher's shape cycle and wall rules, the hit ledger,
/// smart-cursor targeting, buckets, wire tools and paint tools. Check names start with "tools".
/// </summary>
public static class ToolParitySelfTests
{
    private static GameSimulation NewGame(int seed, int width = 80, int height = 48)
    {
        var game = new GameSimulation(seed: seed, width: width, height: height) { FamilySpawnsEnabled = false };
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e6f);
        return game;
    }

    private const int Floor = 30;

    /// <summary>Clears an air box with a basalt floor at <see cref="Floor"/> and stands the player at its middle.</summary>
    private static GameSimulation Arena(int seed)
    {
        var game = NewGame(seed);
        for (var y = 6; y <= Floor + 4; y++)
        for (var x = 10; x <= 70; x++)
        {
            var cell = new GridPoint(x, y);
            game.World.SetTile(cell, y >= Floor ? TileId.Rock : TileId.Air);
            game.World.SetWall(cell, WallIds.None);
            var data = game.World.GetCell(cell);
            data.LiquidDepth = 0f;
            data.LiquidKind = LiquidKind.None;
            data.Shape = TileShape.Full;
            game.World.SetCell(cell, data);
        }
        game.Player.Motion.Position = new Vector2(40.14f, Floor - game.Player.Motion.Size.Y - 0.01f);
        game.Player.Motion.Velocity = Vector2.Zero;
        Idle(game, 3);
        return game;
    }

    private static void Idle(GameSimulation game, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            game.Tick(new GameInput(new MovementInput(0f)));
    }

    /// <summary>One fresh press: waits out the cooldown with the button up, then uses once.</summary>
    private static void Press(GameSimulation game, PlayerAction action, GridPoint target)
    {
        Idle(game, 1);
        for (var i = 0; i < 120 && game.Player.ActionCooldownTicks > 0; i++)
            Idle(game, 1);
        game.Tick(new GameInput(new MovementInput(0f), action, target));
    }

    private static void Hold(GameSimulation game, PlayerAction action, GridPoint target, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            game.Tick(new GameInput(new MovementInput(0f), action, target));
    }

    private static ItemDefinition Select(GameSimulation game, ItemId id)
    {
        if (game.Player.Inventory.Count(id) == 0)
            game.Player.Inventory.Add(id, 1);
        game.TrySelectInventorySlot(game.Player.Inventory.FindSlot(id));
        return game.Items[id];
    }

    private static int Right(GameSimulation game) => (int)MathF.Floor(game.Player.Motion.Position.X + game.Player.Motion.Size.X - 0.001f);

    public static void Run(Action<bool, string> check)
    {
        Profiles(check);
        Reach(check);
        UseModel(check);
        WhichToolHitsWhat(check);
        Demolishers(check);
        Ledger(check);
        SmartCursor(check);
        Utilities(check);
    }

    // ------------------------------------------------------------------ profiles and reach

    private static void Profiles(Action<bool, string> check)
    {
        var items = NewGame(9900).Items;
        var copper = ToolParity.Profile(items[ItemId.CopperPickaxe]);
        var ferriteDrill = ToolParity.Profile(items[ItemId.FerriteDrill]);
        var astralPick = ToolParity.Profile(items[ItemId.AstralPick]);
        var astralDrill = ToolParity.Profile(items[ItemId.AstralDrill]);
        var astralHatchet = ToolParity.Profile(items[ItemId.AstralHatchet]);
        var cobaltPick = ToolParity.Profile(items[ItemId.CobaltPick]);
        check(copper.TileBoost == -1 && ToolParity.TileBoost(items[ItemId.FerriteHatchet]) == -1 &&
              ToolParity.TileBoost(items[ItemId.FerriteDemolisher]) == -1 && cobaltPick.TileBoost == 0 &&
              astralPick.TileBoost == 4 && astralDrill.TileBoost == 3 && astralHatchet.TileBoost == 4,
            "tools profile: copper-tier tools reach one cell less, lunar-tier tools four more (their drills three)");
        check(copper.UseAnimation == 23 && cobaltPick.UseAnimation == 20 && astralPick.UseAnimation == 12 &&
              ferriteDrill.UseAnimation == ToolParity.ChannelSwing && ToolParity.Profile(items[ItemId.FerriteHatchet]).UseAnimation == 30 &&
              ToolParity.Profile(items[ItemId.FerriteDemolisher]).UseAnimation == 33 && copper.UseTime == items[ItemId.CopperPickaxe].UseCooldownTicks,
            "tools profile: swing length is the reference's useAnimation ladder; hit time stays the tool's use time");
        check(ferriteDrill.Channel && !ferriteDrill.AutoReuse && ToolParity.Profile(items[ItemId.PlasmaSaw]).Channel &&
              !copper.Channel && copper.AutoReuse && !ToolParity.IsChannelTool(ItemId.CopperPickaxe),
            "tools profile: drills and saws channel while held; picks, hatchets and demolishers auto-repeat");
        var wire = ToolParity.Profile(items[ItemId.WireTool]);
        check(wire is { UseTime: 5, UseAnimation: 15, TileBoost: 20 } && ToolParity.Profile(items[ItemId.WireCutter]).TileBoost == 20 &&
              ToolParity.Profile(items[ItemId.EmptyBucket]).UseTime == 10 && ToolParity.Profile(items[ItemId.ClimbingRope]) is { UseTime: 8, TileBoost: 3 } &&
              ToolParity.Profile(items[ItemId.BugNet]).UseTime == 25 && GameSimulation.BlockPlaceTicks == 10 && GameSimulation.WallPlaceTicks == 7,
            "tools profile: wire tools 5/15 ticks and +20 reach, buckets 10, rope 8 and +3, bug net 25, blocks 10 and walls 7");
    }

    private static void Reach(Action<bool, string> check)
    {
        var box = ToolParity.ReachBox(10f, 10f, 0.72f, 1.8f, 0, 0);
        var copperBox = ToolParity.ReachBox(10f, 10f, 0.72f, 1.8f, 0, -1);
        var cappedBox = ToolParity.ReachBox(10f, 10f, 0.72f, 1.8f, 30, 0);
        check(box == (5, 7, 15, 14) && copperBox == (6, 8, 14, 13) && cappedBox == (-10, -10, 30, 31),
            "tools reach: a box 5 sideways and 3 up/down past the body, base capped at 20, plus the tool's boost");

        var game = Arena(9901);
        var right = Right(game);
        var copper = Select(game, ItemId.CopperPickaxe);
        var row = Floor - 1;
        var four = new GridPoint(right + 4, row);
        var five = new GridPoint(right + 5, row);
        game.World.SetTile(four, TileId.Soil);
        game.World.SetTile(five, TileId.Soil);
        Press(game, PlayerAction.Mine, five);
        var fiveUntouched = game.TileHitDamage(five) == 0 && game.World.TileAt(five.X, five.Y) == TileId.Soil;
        Press(game, PlayerAction.Mine, four);
        var fourHit = game.TileHitDamage(four) > 0 || game.World.TileAt(four.X, four.Y) == TileId.Air;
        Select(game, ItemId.AstralPick);
        var nine = new GridPoint(right + 9, row);
        var ten = new GridPoint(right + 10, row);
        game.World.SetTile(nine, TileId.Rock);
        game.World.SetTile(ten, TileId.Rock);
        Press(game, PlayerAction.Mine, ten);
        var tenUntouched = game.World.TileAt(ten.X, ten.Y) == TileId.Rock && game.TileHitDamage(ten) == 0;
        Press(game, PlayerAction.Mine, nine);
        var nineHit = game.World.TileAt(nine.X, nine.Y) != TileId.Rock || game.TileHitDamage(nine) > 0;
        check(copper.ToolPower > 0 && fiveUntouched && fourHit && tenUntouched && nineHit && game.IsInToolReach(nine) && !game.IsInToolReach(ten),
            "tools reach: the copper-tier drill reaches 4 cells past the body, the astral pick 9");
    }

    // ------------------------------------------------------------------ use model

    private static void UseModel(Action<bool, string> check)
    {
        var game = Arena(9902);
        var right = Right(game);
        var pick = Select(game, ItemId.CobaltPick);
        var target = new GridPoint(right + 2, Floor - 1);
        game.World.SetTile(target, TileId.Rock);
        game.Tick(new GameInput(new MovementInput(0f), PlayerAction.Mine, target));
        var swing = game.ToolSwingLength;
        var firstHit = game.TileHitDamage(target);
        var cooldown = game.Player.ActionCooldownTicks;
        var hits = 1;
        var previous = firstHit;
        for (var tick = 0; tick < 200 && game.World.TileAt(target.X, target.Y) == TileId.Rock; tick++)
        {
            game.Tick(new GameInput(new MovementInput(0f), PlayerAction.Mine, target));
            var now = game.World.TileAt(target.X, target.Y) == TileId.Rock ? game.TileHitDamage(target) : 100;
            if (now != previous)
                hits++;
            previous = now;
        }
        var expectedHits = (CombatTuning.TileBreakDamage + Math.Min(99, pick.ToolPower) - 1) / Math.Min(99, pick.ToolPower);
        var expectedCooldown = Math.Max(0, (int)MathF.Ceiling(game.ProbeDrillTicks(pick) * game.Player.ActionCooldownScale - 0.001f));
        check(swing == ToolParity.Profile(pick).UseAnimation && firstHit > 0 && cooldown == expectedCooldown &&
              hits == expectedHits && game.World.TileAt(target.X, target.Y) == TileId.Air,
            $"tools use: the first press hits at once, holding keeps hitting every {cooldown} ticks ({hits} hits), the swing lasts {swing} ticks");

        // The swing keeps restarting while held (auto-reuse) and stops when released.
        Hold(game, PlayerAction.Mine, new GridPoint(right + 2, Floor - 3), swing + 2);
        var stillSwinging = game.ToolSwingTicksRemaining > 0;
        Idle(game, swing + 1);
        check(stillSwinging && game.ToolSwingTicksRemaining == 0 && game.ToolSwingProgress is null,
            "tools use: holding the button keeps swinging, releasing it lets the swing end");

        // Channel drills run on their own use time: the mining-speed bonus is for picks only.
        var drill = game.Items[ItemId.CobaltDrill];
        var skill = Math.Max(0.1f, game.Player.MiningSpeedMultiplier);
        check(game.ProbeDrillTicks(drill) == Math.Max(1, (int)MathF.Round(drill.UseCooldownTicks / skill, MidpointRounding.ToEven)),
            "tools use: a channel drill hits on its use time (divided only by the colonist's mining skill)");
    }

    // ------------------------------------------------------------------ which tool hits what

    private static void WhichToolHitsWhat(Action<bool, string> check)
    {
        var game = Arena(9903);
        var right = Right(game);
        Select(game, ItemId.CobaltPick);
        var cactus = new GridPoint(right + 2, Floor - 1);
        game.World.SetTile(cactus, TileId.Cactus);
        Press(game, PlayerAction.Mine, cactus);
        var cactusRefused = game.World.TileAt(cactus.X, cactus.Y) == TileId.Cactus && game.TileHitDamage(cactus) == 0 &&
                            game.Player.ActionCooldownTicks > 0;
        game.World.SetTile(cactus, TileId.Air);

        var heart = new GridPoint(right + 3, Floor - 2);
        for (var dy = 0; dy < 2; dy++)
        for (var dx = 0; dx < 2; dx++)
            game.World.SetTile(new GridPoint(heart.X + dx, heart.Y + dy), TileId.ReactorCore);
        Press(game, PlayerAction.Mine, heart);
        var heartRefused = game.TileHitDamage(heart) == 0 && game.World.TileAt(heart.X, heart.Y) == TileId.ReactorCore;
        var demolisher = Select(game, ItemId.CobaltDemolisher);
        var heartsBefore = game.HeartsBroken;
        var smashHits = 0;
        while (game.World.TileAt(heart.X, heart.Y) == TileId.ReactorCore && smashHits < 10)
        {
            Press(game, PlayerAction.UseSelected, heart);
            smashHits++;
        }
        check(cactusRefused && heartRefused && game.HeartsBroken == heartsBefore + 1 &&
              smashHits == (CombatTuning.TileBreakDamage + demolisher.ToolPower - 1) / demolisher.ToolPower,
            $"tools targets: drills can't hit cactus (cutter) or hearts (demolisher); a demolisher smashes a heart in {smashHits} hits of its power");

        // Cutters hit only cutter tiles; a miss costs no time. Cactus takes triple damage.
        var cutter = Select(game, ItemId.FerriteHatchet);
        var rock = new GridPoint(right + 2, Floor - 1);
        game.World.SetTile(rock, TileId.Rock);
        Press(game, PlayerAction.UseSelected, rock);
        var rockIgnored = game.TileHitDamage(rock) == 0 && game.Player.ActionCooldownTicks == 0;
        game.World.SetTile(rock, TileId.Air);
        game.World.SetTile(cactus, TileId.Cactus);
        Press(game, PlayerAction.UseSelected, cactus);
        var cactusDamage = game.World.TileAt(cactus.X, cactus.Y) == TileId.Cactus ? game.TileHitDamage(cactus) : 100;
        check(rockIgnored && cactusDamage == Math.Min(100, CombatTuning.CutterDamage(cutter.ToolPower, cactus: true)),
            "tools targets: cutters ignore blocks (no time spent) and chop cactus for triple damage");

        // Torches, rope and plants break in one hit, even with the weakest drill.
        game.World.SetTile(cactus, TileId.StandingTorch);
        Select(game, ItemId.WoodenPickaxe);
        Press(game, PlayerAction.Mine, cactus);
        var torchOneHit = game.World.TileAt(cactus.X, cactus.Y) != TileId.StandingTorch;
        game.World.SetTile(new GridPoint(cactus.X, cactus.Y - 2), TileId.Rock);
        game.World.SetTile(new GridPoint(cactus.X, cactus.Y - 1), TileId.Rope);
        Press(game, PlayerAction.Mine, new GridPoint(cactus.X, cactus.Y - 1));
        check(torchOneHit && game.World.TileAt(cactus.X, cactus.Y - 1) != TileId.Rope,
            "tools targets: torches and rope break in one hit with any drill");
        game.World.SetTile(new GridPoint(cactus.X, cactus.Y - 2), TileId.Air);

        // A block holding up a container takes no damage until the container goes.
        game.World.SetTile(cactus, TileId.Air);
        Select(game, ItemId.CobaltPick);
        var under = new GridPoint(right + 2, Floor - 1);
        game.World.SetTile(under, TileId.Soil);
        game.World.SetTile(new GridPoint(under.X, under.Y - 1), TileId.SurfaceCrate);
        Press(game, PlayerAction.Mine, under);
        check(game.World.TileAt(under.X, under.Y) == TileId.Soil && game.TileHitDamage(under) == 0 &&
              !ToolParity.CanBreakUnder(game.World, under) && ToolParity.CanBreakUnder(game.World, new GridPoint(under.X + 1, under.Y)),
            "tools targets: a block with a container standing on it can't be mined");
    }

    // ------------------------------------------------------------------ demolishers

    private static void Demolishers(Action<bool, string> check)
    {
        var game = Arena(9904);
        var right = Right(game);
        Select(game, ItemId.CobaltDemolisher);
        // A free-standing block on the floor: walls on neither side.
        var block = new GridPoint(right + 3, Floor - 1);
        game.World.SetTile(block, TileId.Rock);
        var shapes = new List<TileShape>();
        for (var i = 0; i < 6; i++)
        {
            Press(game, PlayerAction.UseSelected, block);
            shapes.Add(game.World.GetCell(block).Shape);
        }
        check(shapes.SequenceEqual(new[] { TileShape.Half, TileShape.SlopeUpLeft, TileShape.SlopeUpRight, TileShape.CeilingLeft, TileShape.CeilingRight, TileShape.Full }),
            $"tools pound: full -> half -> slope -> mirrored slope -> ceiling slopes -> full ({string.Join(",", shapes)})");

        // A wall on the right and open space on the left: the first slope leans its high side on the wall.
        game.World.SetTile(new GridPoint(block.X + 1, block.Y), TileId.Rock);
        Press(game, PlayerAction.UseSelected, block);
        Press(game, PlayerAction.UseSelected, block);
        var leansRight = game.World.GetCell(block).Shape == TileShape.SlopeUpRight;
        game.World.SetTile(new GridPoint(block.X + 1, block.Y), TileId.Air);
        game.SetShape(block, TileShape.Full);
        // A block hanging under a ceiling: ceiling slopes come first.
        var hanging = new GridPoint(right + 3, Floor - 4);
        game.World.SetTile(hanging, TileId.Rock);
        game.World.SetTile(new GridPoint(hanging.X, hanging.Y - 1), TileId.Rock);
        Press(game, PlayerAction.UseSelected, hanging);
        Press(game, PlayerAction.UseSelected, hanging);
        var ceilingFirst = game.World.GetCell(hanging).Shape == TileShape.CeilingLeft;
        game.World.SetTile(hanging, TileId.Air);
        game.World.SetTile(new GridPoint(hanging.X, hanging.Y - 1), TileId.Air);
        // Platforms: full -> slope -> mirrored slope -> full, toward a stair neighbour first.
        var platform = new GridPoint(right + 2, Floor - 3);
        game.World.SetTile(platform, TileId.PlankPlatform);
        game.World.SetTile(new GridPoint(platform.X + 1, platform.Y - 1), TileId.PlankPlatform);
        var platformShapes = new List<TileShape>();
        for (var i = 0; i < 3; i++)
        {
            Press(game, PlayerAction.UseSelected, platform);
            platformShapes.Add(game.World.GetCell(platform).Shape);
        }
        check(leansRight && ceilingFirst && platformShapes.SequenceEqual(new[] { TileShape.SlopeUpRight, TileShape.SlopeUpLeft, TileShape.Full }),
            "tools pound: slopes lean against a wall on the right, ceiling slopes come first under a ceiling, platforms slope toward a stair");
        game.World.SetTile(platform, TileId.Air);
        game.World.SetTile(new GridPoint(platform.X + 1, platform.Y - 1), TileId.Air);

        // One pound per press: holding the button pounds once, then hits the wall behind the block.
        game.SetShape(block, TileShape.Full);
        game.World.SetWall(block, WallIds.Plank);
        Idle(game, 30);
        var demolisher = game.Items[ItemId.CobaltDemolisher];
        Hold(game, PlayerAction.UseSelected, block, demolisher.UseCooldownTicks * 3);
        var poundedOnce = game.World.GetCell(block).Shape == TileShape.Half;
        var wallHitBehind = game.WallHitDamage(block) > 0 || game.World.GetCell(block).WallId == WallIds.None;
        check(poundedOnce && wallHitBehind,
            "tools pound: holding the button pounds once per press, then works on the wall behind the block");

        // Walls: natural walls need an open neighbour (diagonals count); built walls can go from anywhere.
        var wallGame = Arena(9905);
        var wr = Right(wallGame);
        Select(wallGame, ItemId.CobaltDemolisher);
        for (var y = Floor - 6; y <= Floor - 4; y++)
        for (var x = wr + 2; x <= wr + 4; x++)
            wallGame.World.SetWall(new GridPoint(x, y), WallIds.Rock);
        var middle = new GridPoint(wr + 3, Floor - 5);
        var surrounded = !ToolParity.CanSmashWall(wallGame.World, middle);
        Press(wallGame, PlayerAction.UseSelected, middle);
        var refused = wallGame.WallHitDamage(middle) == 0;
        wallGame.World.SetWall(new GridPoint(wr + 4, Floor - 6), WallIds.None); // open only the diagonal
        var diagonal = ToolParity.CanSmashWall(wallGame.World, middle);
        wallGame.World.SetWall(new GridPoint(wr + 4, Floor - 6), WallIds.Rock);
        wallGame.World.SetWall(middle, WallIds.Plank);
        var built = ToolParity.CanSmashWall(wallGame.World, middle);
        var hammer = wallGame.Items[ItemId.CobaltDemolisher];
        Press(wallGame, PlayerAction.UseSelected, middle);
        var wallTicks = wallGame.Player.ActionCooldownTicks;
        check(surrounded && refused && diagonal && built && wallGame.WallHitDamage(middle) == Math.Min(99, CombatTuning.WallHammerDamage(hammer.ToolPower)) &&
              wallTicks <= Math.Max(1, (int)MathF.Ceiling(hammer.UseCooldownTicks / 2 * wallGame.Player.ActionCooldownScale)),
            "tools walls: natural walls need an open neighbour (diagonals count), built walls don't; wall hits take half the use time");
    }

    // ------------------------------------------------------------------ hit ledger

    private static void Ledger(Action<bool, string> check)
    {
        var ledger = new HitLedger();
        var a = new GridPoint(1, 1);
        ledger.Add(a, HitLedger.Layer.Tile, 7, 50);
        bool Always(GridPoint _, HitLedger.Layer __, ushort ___) => true;
        var trace = new List<int>();
        for (var hit = 0; hit < 60; hit++)
        {
            ledger.Prune(Always);
            trace.Add(ledger.Damage(a, HitLedger.Layer.Tile, 7));
        }
        // Life 60 → 48 after 12 other hits: no mending yet; then −2 per hit, −5, −7, −10.
        check(trace[11] == 50 && trace[12] == 48 && trace[13] == 46 && trace.Last() == 0 && !ledger.Contains(a, HitLedger.Layer.Tile),
            $"tools ledger: damage holds for 12 further hits, then mends faster and faster (50 -> {trace[12]}, {trace[13]}, ... 0)");

        var game = Arena(9906);
        var right = Right(game);
        Select(game, ItemId.CobaltPick);
        var dent = new GridPoint(right + 2, Floor - 1);
        game.World.SetTile(dent, TileId.Rock);
        Press(game, PlayerAction.Mine, dent);
        var dented = game.TileHitDamage(dent);
        Idle(game, 600);
        var heldOverTime = game.TileHitDamage(dent) == dented && game.World.GetCell(dent).Damage > 0;
        // Mine a column of soil elsewhere: every damaging hit ages the dent.
        var other = new GridPoint(right - 2, Floor);
        for (var i = 0; i < 80 && game.TileHitDamage(dent) > 0; i++)
        {
            if (game.World.TileAt(other.X, other.Y) == TileId.Air)
                game.World.SetTile(other, TileId.Rock);
            Press(game, PlayerAction.Mine, other);
        }
        check(dented > 0 && heldOverTime && game.TileHitDamage(dent) == 0 && game.World.GetCell(dent).Damage == 0 &&
              game.World.TileAt(dent.X, dent.Y) == TileId.Rock,
            "tools ledger: a dent stays while left alone and mends as later hits elsewhere age it");
    }

    // ------------------------------------------------------------------ smart cursor

    private static void SmartCursor(Action<bool, string> check)
    {
        var game = Arena(9907);
        var right = Right(game);
        var pick = Select(game, ItemId.CobaltPick);
        var center = game.Player.Motion.Position + game.Player.Motion.Size / 2f;
        // A rock column three cells to the right; the cursor points beyond it, at head height.
        for (var y = Floor - 3; y < Floor; y++)
            game.World.SetTile(new GridPoint(right + 3, y), TileId.Rock);
        var mouse = new Vector2(right + 7.5f, center.Y);
        var dig = SmartToolTargeting.Find(game, pick, mouse);
        check(dig is { } d && d.X == right + 3 && d.Y >= Floor - 3 && d.Y < Floor,
            $"tools smart: a drill takes the first block along the line toward the cursor ({dig})");

        game.World.SetTile(new GridPoint(right + 1, Floor - 5), TileId.LifeCore);
        var core = SmartToolTargeting.Find(game, pick, mouse);
        check(core == new GridPoint(right + 1, Floor - 5), "tools smart: a life core in reach comes before any block");
        game.World.SetTile(new GridPoint(right + 1, Floor - 5), TileId.Air);

        // Cutter: the foot of the trunk nearest the cursor.
        var hatchet = Select(game, ItemId.CobaltHatchet);
        var trunkX = right + 2;
        for (var y = Floor - 4; y < Floor; y++)
            game.World.SetTile(new GridPoint(trunkX, y), TileId.TreeTrunk);
        var fell = SmartToolTargeting.Find(game, hatchet, new Vector2(trunkX + 0.5f, Floor - 3.5f));
        check(fell == new GridPoint(trunkX, Floor - 1), $"tools smart: a cutter takes the foot of the trunk ({fell})");
        for (var y = Floor - 4; y < Floor; y++)
            game.World.SetTile(new GridPoint(trunkX, y), TileId.Air);

        // Demolisher: an exposed wall along the line, never a block.
        var demolisher = Select(game, ItemId.CobaltDemolisher);
        var wallCell = new GridPoint(right + 2, (int)MathF.Floor(center.Y));
        game.World.SetWall(wallCell, WallIds.Rock);
        var hammerTarget = SmartToolTargeting.Find(game, demolisher, new Vector2(right + 6.5f, center.Y));
        var wallOnly = hammerTarget is { } h && game.World.GetCell(h).WallId != WallIds.None;
        game.World.SetWall(wallCell, WallIds.None);
        var nothing = SmartToolTargeting.Find(game, demolisher, new Vector2(right + 6.5f, center.Y));
        check(wallOnly && nothing is null, $"tools smart: a demolisher takes an exposed wall ({hammerTarget}) and ignores blocks");

        // Out of reach: nothing beyond the box is ever chosen.
        game.World.SetTile(new GridPoint(right + 3, Floor - 1), TileId.Air);
        game.World.SetTile(new GridPoint(right + 3, Floor - 2), TileId.Air);
        game.World.SetTile(new GridPoint(right + 3, Floor - 3), TileId.Air);
        game.World.SetTile(new GridPoint(right + 12, Floor - 2), TileId.Rock);
        check(SmartToolTargeting.Find(game, pick, new Vector2(right + 12.5f, Floor - 1.5f)) is null,
            "tools smart: targets stay inside the tool's reach box");
    }

    // ------------------------------------------------------------------ utilities

    private static void Utilities(Action<bool, string> check)
    {
        var game = Arena(9908);
        var right = Right(game);
        // Buckets: a thin film can't be scooped; a full cell tops up from its neighbours. The liquid is set right before the
        // use tick (the use runs before that tick's liquid step), inside a rock basin so nothing flows away.
        Select(game, ItemId.EmptyBucket);
        var film = new GridPoint(right + 2, Floor - 1);
        var side = new GridPoint(film.X + 1, film.Y);
        game.World.SetTile(new GridPoint(film.X - 1, film.Y), TileId.Rock);
        game.World.SetTile(new GridPoint(film.X + 2, film.Y), TileId.Rock);
        Idle(game, 30);
        SetLiquid(game, film, LiquidKind.Water, 60);
        game.Tick(new GameInput(new MovementInput(0f), PlayerAction.UseSelected, film));
        var filmRefused = game.Player.Inventory.Count(ItemId.BucketWater) == 0;
        Idle(game, 30);
        SetLiquid(game, film, LiquidKind.Water, 120);
        SetLiquid(game, side, LiquidKind.Water, 200);
        game.Tick(new GameInput(new MovementInput(0f), PlayerAction.UseSelected, film));
        var left = game.World.GetCell(film).LiquidUnits + game.World.GetCell(side).LiquidUnits;
        var toppedUp = game.Player.Inventory.Count(ItemId.BucketWater) == 1 && left == 120 + 200 - WorldCell.LiquidUnitsMax;
        SetLiquid(game, film, LiquidKind.None, 0);
        SetLiquid(game, side, LiquidKind.None, 0);
        Select(game, ItemId.BucketWater);
        Idle(game, 30);
        SetLiquid(game, film, LiquidKind.Water, 220);
        game.Tick(new GameInput(new MovementInput(0f), PlayerAction.UseSelected, film));
        var deepRefused = game.Player.Inventory.Count(ItemId.BucketWater) == 1;
        check(filmRefused && toppedUp && deepRefused,
            $"tools buckets: a scoop needs > 100/255 of a cell nearby and tops a full cell up from neighbours ({left} left); pouring needs < 200/255 there");
        SetLiquid(game, film, LiquidKind.None, 0);
        game.World.SetTile(new GridPoint(film.X - 1, film.Y), TileId.Air);
        game.World.SetTile(new GridPoint(film.X + 2, film.Y), TileId.Air);

        // Wire tools: the wire tool only lays wire; the cutter takes one piece per use, actuator first, top layer down.
        game.Player.Inventory.Add(ItemId.WireSpool, 5);
        Select(game, ItemId.WireTool);
        var wired = new GridPoint(right + 2, Floor - 3);
        Press(game, PlayerAction.UseSelected, wired);
        Press(game, PlayerAction.UseSelected, wired);
        var stillWired = game.Wires.Has(wired, game.ActiveWireLayer);
        game.PlaceWire(wired, 2);
        game.PlaceWire(wired, 3);
        var cellData = game.World.GetCell(wired);
        game.World.SetTile(wired, TileId.Rock);
        cellData = game.World.GetCell(wired);
        cellData.HasActuator = true;
        game.World.SetCell(wired, cellData);
        Select(game, ItemId.WireCutter);
        Press(game, PlayerAction.UseSelected, wired);
        var actuatorFirst = !game.World.GetCell(wired).HasActuator && game.Wires.LayersAt(wired) == 0b111;
        Press(game, PlayerAction.UseSelected, wired);
        var topLayerNext = !game.Wires.Has(wired, 3) && game.Wires.Has(wired, 2) && game.Wires.Has(wired, 1);
        check(stillWired && actuatorFirst && topLayerNext,
            "tools wires: the wire tool never removes wire; the cutter takes the actuator, then the highest layer, one per use");

        // Paint scraper: the block's paint first, then the wall's.
        var painted = new GridPoint(right + 2, Floor - 2);
        game.World.SetTile(painted, TileId.Rock);
        game.World.SetWall(painted, WallIds.Plank);
        game.PaintCell(painted, 3, 5);
        Select(game, ItemId.PaintScraper);
        Press(game, PlayerAction.UseSelected, painted);
        var tileFirst = game.TilePaintAt(painted) == 0 && game.WallPaintAt(painted) == 5;
        Press(game, PlayerAction.UseSelected, painted);
        check(tileFirst && game.WallPaintAt(painted) == 0, "tools paint: the scraper takes the block's paint first, then the wall's");
    }

    private static void SetLiquid(GameSimulation game, GridPoint point, LiquidKind kind, int units)
    {
        var cell = game.World.GetCell(point);
        cell.LiquidUnits = (byte)units;
        cell.LiquidKind = units == 0 ? LiquidKind.None : kind;
        game.World.SetCell(point, cell);
    }
}
