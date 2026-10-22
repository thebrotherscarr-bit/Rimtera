using System.Numerics;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>Self-tests for the travel pass: glide-walks, pylons, and the mount types.</summary>
public static class TravelSelfTests
{
    private static GameSimulation NewGame(int seed)
    {
        var game = new GameSimulation(seed: seed, width: 80, height: 48) { FamilySpawnsEnabled = false };
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e6f);
        return game;
    }

    private static void Tick(GameSimulation game, int ticks = 1, MovementInput movement = default)
    {
        for (var i = 0; i < ticks; i++)
            game.Tick(new GameInput(movement, PlayerAction.None, null));
    }

    /// <summary>Clears a box to air with a rock floor at <paramref name="floorY"/>, and puts the player on it at <paramref name="x"/>.</summary>
    private static void Arena(GameSimulation game, int floorY, float x)
    {
        for (var y = 1; y <= floorY + 1; y++)
        for (var cx = 3; cx <= 76; cx++)
        {
            var cell = new GridPoint(cx, y);
            game.World.SetTile(cell, y >= floorY ? TileId.Rock : TileId.Air);
            var data = game.World.GetCell(cell);
            data.LiquidDepth = 0f;
            data.LiquidKind = LiquidKind.None;
            game.World.SetCell(cell, data);
        }
        Place(game, new Vector2(x, floorY - 1.81f));
    }

    private static void Place(GameSimulation game, Vector2 position)
    {
        game.Player.Motion.Position = position;
        game.Player.Motion.Velocity = Vector2.Zero;
        Tick(game, 2);
    }

    private static void Walkway(GameSimulation game, int left, int right, int row, TileId tile)
    {
        for (var x = left; x <= right; x++)
            game.PlaceTravelTileForTests(new GridPoint(x, row), tile);
    }

    private static float Feet(GameSimulation game) => game.Player.Motion.Position.Y + game.Player.Motion.Size.Y;

    /// <summary>Highest point (smallest y) reached over <paramref name="ticks"/> after a jump with jump held.</summary>
    private static float JumpPeak(GameSimulation game, int ticks)
    {
        var peak = game.Player.Motion.Position.Y;
        game.Tick(new GameInput(new MovementInput(0f, JumpPressed: true, JumpHeld: true), PlayerAction.None, null));
        for (var i = 0; i < ticks; i++)
        {
            game.Tick(new GameInput(new MovementInput(0f, JumpHeld: true), PlayerAction.None, null));
            peak = Math.Min(peak, game.Player.Motion.Position.Y);
        }
        return peak;
    }

    private static GameSimulation Rider(int seed, MountKind kind, int floorY = 40, float x = 40f)
    {
        var game = NewGame(seed);
        Arena(game, floorY, x);
        if (kind != MountKind.None)
        {
            game.Player.Inventory.Add(TravelContent.Mounts[kind].Item, 1);
            game.ToggleMount(kind);
        }
        return game;
    }

    public static void Run(Action<bool, string> check)
    {
        // ---- Catalogue ----
        var catalog = NewGame(9301);
        check(TravelContent.Mounts.Count >= 6 && TravelContent.Mounts.Values.All(spec => catalog.Items.Contains(spec.Item)) &&
              catalog.Tiles.Contains(TileId.GlideWalkRight) && catalog.Tiles[TileId.GlideWalkRight].IsSolid &&
              TileDropTable.GetDrop(TileId.ExpressWalkLeft) == ItemId.ExpressWalk &&
              catalog.Crafting.Recipes.Any(recipe => recipe.Output == ItemId.GlideWalk) && catalog.Crafting.Recipes.Any(recipe => recipe.Output == ItemId.ColonyPylon) &&
              catalog.Crafting.Recipes.Any(recipe => recipe.Output == ItemId.BurrowerKey),
            "travel items, glide-walk tiles, mounts and their recipes are registered");

        // ---- Glide-walks: a belt floor adds its speed to whatever the rider is doing ----
        float Travel(int seed, TileId? belt, float walk, int ticks = 60)
        {
            var game = NewGame(seed);
            Arena(game, 30, 30f);
            if (belt is { } tile)
                Walkway(game, 8, 70, 30, tile);
            Tick(game, 2);
            var start = game.Player.Motion.Position.X;
            Tick(game, ticks, new MovementInput(walk));
            return game.Player.Motion.Position.X - start;
        }
        var drift = Travel(9302, TileId.GlideWalkRight, 0f);
        var expressDrift = Travel(9303, TileId.ExpressWalkRight, 0f);
        check(drift > 3f && expressDrift > drift * 1.5f,
            $"standing on a glide-walk carries the player along ({drift:0.0} tiles), an express one faster ({expressDrift:0.0})");

        var plainWalk = Travel(9304, null, 1f);
        var withBelt = Travel(9305, TileId.GlideWalkRight, 1f);
        var againstBelt = Travel(9306, TileId.GlideWalkRight, -1f);
        check(withBelt > plainWalk + 3f && againstBelt > -plainWalk * 0.6f,
            $"walking with the belt is fast ({withBelt:0.0} vs {plainWalk:0.0} tiles) and against it slow ({againstBelt:0.0})");

        var line = NewGame(9307);
        Arena(line, 30, 14f);
        Walkway(line, 10, 30, 30, TileId.GlideWalkRight);
        Tick(line, 400);
        var stoppedAt = line.Player.Motion.Position.X;
        check(stoppedAt > 29f && stoppedAt < 33f && line.GlideWalkCarry == 0f && line.Player.Motion.Grounded,
            "at the end of the line the rider steps off onto the floor and stops");

        var reverse = NewGame(9308);
        Arena(reverse, 30, 40f);
        Walkway(reverse, 8, 70, 30, TileId.GlideWalkRight);
        for (var x = 8; x <= 70; x++)
            reverse.TryReverseGlideWalk(new GridPoint(x, 30));
        Tick(reverse, 2);
        var reverseStart = reverse.Player.Motion.Position.X;
        Tick(reverse, 60);
        check(reverse.World.GetCell(new GridPoint(40, 30)).TileId == TileId.GlideWalkLeft && reverse.Player.Motion.Position.X < reverseStart - 3f &&
              TravelContent.GlideWalkFacing(ItemId.ExpressWalk, -1) == TileId.ExpressWalkLeft &&
              TravelContent.GlideWalkFacing(ItemId.GlideWalk, 1) == TileId.GlideWalkRight,
            "a glide-walk is laid running the way the placer faces, and interacting reverses it");

        // ---- Pylons ----
        var town = NewGame(9309);
        Arena(town, 30, 20f);
        town.PlaceTravelTileForTests(new GridPoint(20, 29), TileId.ColonyPylon);
        town.PlaceTravelTileForTests(new GridPoint(60, 29), TileId.CavernPylon);
        var dormant = !town.TryPylonTeleport(new GridPoint(60, 29)) && town.Player.Motion.Position.X < 30f &&
                      town.Pylons.Count == 2 && town.Pylons.All(pylon => !pylon.Active);
        var placementRefused = town.PylonPlacementProblem(TileId.FrostPylon, new GridPoint(30, 29), checkBiome: false) is not null;
        town.AddResidentForTests(TownNpcKind.Quartermaster, new GridPoint(38, 27));
        town.AddResidentForTests(TownNpcKind.Medic, new GridPoint(42, 27));
        var placementAllowed = town.PylonPlacementProblem(TileId.FrostPylon, new GridPoint(30, 29), checkBiome: false) is null &&
                               town.PylonPlacementProblem(TileId.ColonyPylon, new GridPoint(30, 29), checkBiome: false) is not null;
        var traveled = town.TryPylonTeleport(new GridPoint(60, 29)) && MathF.Abs(town.Player.Motion.Position.X + 0.34f - 60.5f) < 2.5f;
        check(dormant && placementRefused, "pylons stay dormant and refuse placement without a settled town");
        check(placementAllowed && traveled, "with two housed residents nearby pylons go active and teleport the player between them");

        var far = NewGame(9310);
        Arena(far, 30, 50f);
        far.PlaceTravelTileForTests(new GridPoint(20, 29), TileId.ColonyPylon);
        far.PlaceTravelTileForTests(new GridPoint(60, 29), TileId.CavernPylon);
        far.AddResidentForTests(TownNpcKind.Quartermaster, new GridPoint(38, 27));
        far.AddResidentForTests(TownNpcKind.Medic, new GridPoint(42, 27));
        check(far.PylonInRange is null && !far.TryPylonTeleport(new GridPoint(20, 29)), "travel needs the player standing by a pylon");

        // ---- Mounts ----
        var walker = Rider(9311, MountKind.None);
        Tick(walker, 40, new MovementInput(1f));
        var walkSpeed = walker.Player.Motion.Velocity.X;
        var baseJump = Rider(9312, MountKind.None);
        var basePeak = 40f - JumpPeak(baseJump, 70);

        var strider = Rider(9313, MountKind.DuneStrider);
        Tick(strider, 40, new MovementInput(1f));
        var striderSpeed = strider.Player.Motion.Velocity.X;
        var striderJump = Rider(9314, MountKind.DuneStrider);
        var striderPeak = 40f - JumpPeak(striderJump, 70);
        check(strider.ActiveMount == MountKind.DuneStrider && striderSpeed > walkSpeed * 1.5f && striderPeak > basePeak + 1f,
            "the dune strider runs fast and jumps higher than on foot");

        var hopper = Rider(9315, MountKind.SpringHopper);
        var hopperPeak = 40f - JumpPeak(hopper, 90);
        check(hopper.ActiveMount == MountKind.SpringHopper && hopperPeak > basePeak * 2f && hopperPeak > striderPeak,
            "the spring hopper out-jumps both the base jump and the strider");

        var flyer = Rider(9316, MountKind.RotorSkiff);
        var flyerPeak = 40f - JumpPeak(flyer, 240);
        check(flyer.ActiveMount == MountKind.RotorSkiff && flyerPeak > basePeak + 6f && flyer.MountFlightRemaining < 0.01f,
            "the rotor skiff gains height in flight until its flight time runs out");
        Tick(flyer, 320);
        check(flyer.Player.Motion.Grounded && flyer.MountFlightRemaining > 3f, "the rotor skiff's flight time refills on landing");

        void Flood(GameSimulation game)
        {
            for (var y = 30; y <= 39; y++)
            for (var x = 3; x <= 76; x++)
            {
                var cell = new GridPoint(x, y);
                var data = game.World.GetCell(cell);
                data.LiquidKind = LiquidKind.Water;
                data.LiquidDepth = 1f;
                game.World.SetCell(cell, data);
            }
        }
        var wader = Rider(9317, MountKind.None, x: 20f);
        Flood(wader);
        Tick(wader, 40, new MovementInput(1f));
        var ray = Rider(9318, MountKind.TideRay, x: 20f);
        Flood(ray);
        Tick(ray, 40, new MovementInput(1f));
        check(ray.ActiveMount == MountKind.TideRay && ray.Player.Motion.Velocity.X > wader.Player.Motion.Velocity.X * 2f &&
              ray.Player.Motion.Position.X > wader.Player.Motion.Position.X + 3f,
            "the tide ray swims far faster in water than a wading colonist");

        GameSimulation Dig(MountKind kind, int seed)
        {
            var game = Rider(seed, kind, x: 40f);
            for (var y = 36; y <= 39; y++)
                game.World.SetTile(new GridPoint(43, y), TileId.Soil);
            Tick(game, 120, new MovementInput(1f));
            return game;
        }
        var digger = Dig(MountKind.Burrower, 9319);
        var blocked = Dig(MountKind.None, 9320);
        check(digger.ActiveMount == MountKind.Burrower && digger.World.GetCell(new GridPoint(43, 39)).TileId == TileId.Air &&
              digger.Player.Motion.Position.X > 43f && blocked.World.GetCell(new GridPoint(43, 39)).TileId == TileId.Soil &&
              digger.Player.Inventory.Count(ItemId.SoilBlock) > 0,
            "the burrower chews through soft ground ahead and keeps the soil");

        // The mount key cycles: dismount, then the first carried mount; using another key swaps mounts.
        var keys = Rider(9321, MountKind.None);
        keys.Player.Inventory.Add(ItemId.SpringHopperKey, 1);
        keys.Player.Inventory.Add(ItemId.TideRayCharm, 1);
        var first = keys.ToggleMount() ? keys.ActiveMount : MountKind.None;
        keys.ToggleMount(MountKind.SpringHopper);
        var swapped = keys.ActiveMount;
        keys.ToggleMount();
        check(first == MountKind.TideRay && swapped == MountKind.SpringHopper && !keys.IsMounted &&
              keys.ActiveBuffs.All(buff => buff.Key != "mount"),
            "mount keys mount, swap and dismount");
    }
}
