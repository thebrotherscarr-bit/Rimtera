using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// Self-tests for the world &amp; leisure pass (docs/TERRARIA_GAPS.md plan item 3): wave moons, the cryo brigade, wind, kites,
/// audio decks, town pets, golf, paint, secret seeds and creative research.
/// </summary>
public static class WorldLeisureSelfTests
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    private static GameSimulation RoundTrip(GameSimulation game) =>
        GameSimulation.Restore(JsonSerializer.Deserialize<GameSnapshot>(JsonSerializer.Serialize(game.CaptureSnapshot(), Json), Json)!);

    private static void Tick(GameSimulation game, int ticks = 1, PlayerAction action = PlayerAction.None, GridPoint? target = null)
    {
        for (var i = 0; i < ticks; i++)
            game.Tick(new GameInput(new MovementInput(0f), i == 0 ? action : PlayerAction.None, target));
    }

    private static GameSimulation NewGame(int seed, CharacterProfile? profile = null)
    {
        var game = new GameSimulation(seed: seed, width: 160, height: 72, colonist: profile) { FamilySpawnsEnabled = false };
        Calm(game);
        return game;
    }

    private static void Calm(GameSimulation game)
    {
        foreach (var enemy in game.Enemies.ToArray())
            enemy.TakeDamage(1e7f);
        game.Player.Heal(1e6f);
    }

    private static void Use(GameSimulation game, ItemId item, GridPoint target)
    {
        game.Player.ActionCooldownTicks = 0;
        game.TrySelectInventorySlot(game.Player.Inventory.FindSlot(item));
        Tick(game, 1, PlayerAction.UseSelected, target);
    }

    /// <summary>Flat open ground at row <paramref name="floor"/> from x0 to x1 with clear sky above; the player stands at x0.</summary>
    private static void FlatGround(GameSimulation game, int x0, int x1, int floor, TileId surface = TileId.Grass)
    {
        for (var x = x0 - 2; x <= x1 + 2; x++)
        {
            for (var y = 0; y < floor; y++)
            {
                game.World.SetTile(new GridPoint(x, y), TileId.Air);
                game.World.SetWall(new GridPoint(x, y), WallIds.None);
            }
            game.World.SetTile(new GridPoint(x, floor), surface);
            game.World.SetTile(new GridPoint(x, floor + 1), TileId.Rock);
        }
        game.Player.Motion.Position = new Vector2(x0, floor - game.Player.Motion.Size.Y - 0.01f);
        game.Player.Motion.Velocity = Vector2.Zero;
    }

    private static GridPoint BuildRoom(GameSimulation game, int x0, int top = 2)
    {
        var bottom = top + 9;
        for (var y = top; y <= bottom; y++)
        for (var x = x0; x <= x0 + 13; x++)
        {
            var shell = y == top || y == bottom || x == x0 || x == x0 + 13;
            game.World.SetTile(new GridPoint(x, y), shell ? TileId.Plate : TileId.Air);
            if (!shell)
                game.World.SetWall(new GridPoint(x, y), WallIds.PlatingGrey);
        }
        for (var y = bottom - 3; y <= bottom - 1; y++)
            game.World.SetTile(new GridPoint(x0, y), TileId.PlankDoor);
        game.World.SetTile(new GridPoint(x0 + 3, bottom - 1), TileId.WallTorch);
        game.World.SetTile(new GridPoint(x0 + 5, bottom - 1), TileId.PlankTable);
        game.World.SetTile(new GridPoint(x0 + 6, bottom - 1), TileId.PlankChair);
        game.Regions.ForceFullRebuild();
        return new GridPoint(x0 + 8, top + 4);
    }

    private static void KillTagged(GameSimulation game, WorldEventKind kind)
    {
        foreach (var enemy in game.Enemies.Where(enemy => enemy.IsAlive && enemy.EventTag == kind).ToArray())
        {
            enemy.Shielded = false;
            enemy.TakeDamage(1e7f);
        }
        Tick(game);
        game.Player.Heal(1e6f);
    }

    public static void Run(Action<bool, string> check)
    {
        // ---- 1. Wave moon: kills score points, waves advance, mini-boss mid-way and the boss at the final waves ----
        var moon = NewGame(8401);
        moon.Flags.Set("blight.fracture");
        moon.SetTimeOfDay(12f);
        moon.Player.Inventory.Add(ItemId.HollowLantern, 2);
        Use(moon, ItemId.HollowLantern, new GridPoint(1, 1));
        var refusedByDay = !moon.WorldEvents.IsActive(WorldEventKind.HollowMoon) && moon.Player.Inventory.Count(ItemId.HollowLantern) == 2;
        moon.SetTimeOfDay(21f);
        Use(moon, ItemId.HollowLantern, new GridPoint(1, 1));
        var started = moon.WorldEvents.IsActive(WorldEventKind.HollowMoon);
        var waves = new List<int>();
        var miniBossSeen = false;
        EnemyAgent? boss = null;
        for (var round = 0; round < 200 && boss is null; round++)
        {
            Tick(moon, 75);
            miniBossSeen |= moon.Enemies.Any(enemy => enemy.IsAlive && enemy.Kind == SeasonalEvents.MiniBoss(WorldEventKind.HollowMoon));
            boss = moon.Enemies.FirstOrDefault(enemy => enemy.IsAlive && enemy.IsBoss && enemy.EventTag == WorldEventKind.HollowMoon);
            if (boss is not null)
                break;
            // Extra tagged creatures speed the waves along.
            var roster = SeasonalEvents.WaveRoster(WorldEventKind.HollowMoon, moon.MoonProgress.FirstOrDefault().Wave);
            for (var i = 0; i < 6; i++)
                moon.SpawnCreature(roster[i % roster.Length].Kind, moon.Player.Motion.Position + new Vector2(10f + i, -3f)).EventTag = WorldEventKind.HollowMoon;
            KillTagged(moon, WorldEventKind.HollowMoon);
            if (moon.MoonProgress.FirstOrDefault() is { Wave: > 0 } progress)
                waves.Add(progress.Wave);
        }
        var bossWave = moon.MoonProgress.FirstOrDefault().Wave;
        check(refusedByDay && started && waves.Count > 2 && waves.Zip(waves.Skip(1)).All(pair => pair.Second >= pair.First) &&
              waves.Max() > 1 && miniBossSeen && boss is not null && boss.Kind == SeasonalEvents.Boss(WorldEventKind.HollowMoon) &&
              bossWave >= SeasonalEvents.BossWave,
            $"a wave moon needs night after the Fracture, kills advance its waves, a mini-boss joins mid-way and its boss arrives at the final waves (reached wave {bossWave})");

        // The moon setting pays out its wave-tier loot.
        var embersBefore = moon.Player.Inventory.Count(ItemId.HollowEmber);
        moon.WorldEvents.Stop(WorldEventKind.HollowMoon, moon);
        Tick(moon);
        check(!moon.WorldEvents.IsActive(WorldEventKind.HollowMoon) && moon.Player.Inventory.Count(ItemId.HollowEmber) >= embersBefore + 2 * bossWave,
            "a wave moon's loot tier follows the wave reached");

        // ---- 2. Cryo brigade: an item-summoned invasion that completes on its kill count ----
        var brigade = NewGame(8402);
        brigade.Flags.Set("blight.fracture");
        brigade.Player.Inventory.Add(ItemId.CryoOrders, 1);
        Use(brigade, ItemId.CryoOrders, new GridPoint(1, 1));
        var brigadeStarted = brigade.WorldEvents.IsActive(WorldEventKind.CryoBrigade);
        for (var round = 0; round < 80 && brigade.WorldEvents.IsActive(WorldEventKind.CryoBrigade); round++)
        {
            if (!brigade.Enemies.Any(enemy => enemy.IsAlive && enemy.EventTag == WorldEventKind.CryoBrigade))
                for (var i = 0; i < 5; i++)
                    brigade.SpawnCreature(EnemyKind.SnowWolf, brigade.Player.Motion.Position + new Vector2(8f + i, -2f)).EventTag =
                        WorldEventKind.CryoBrigade;
            KillTagged(brigade, WorldEventKind.CryoBrigade);
        }
        check(brigadeStarted && !brigade.WorldEvents.IsActive(WorldEventKind.CryoBrigade) &&
              brigade.WorldEvents.Completions(WorldEventKind.CryoBrigade) == 1 && brigade.Player.Inventory.Count(ItemId.RimeShard) >= 6,
            "the cryo brigade invasion completes once its kill count is reached and pays its rewards");

        // ---- 3. Wind pushes a dropped item and lifts a kite ----
        GameSimulation WindGame(float wind)
        {
            var game = NewGame(8403);
            FlatGround(game, 40, 90, 40);
            game.WeatherControl.Force(wind: wind, rain: 0f, immediate: true);
            return game;
        }
        var calm = WindGame(0f);
        var windy = WindGame(6f);
        var calmItem = calm.SpawnGroundItem(ItemId.SoilBlock, 1, new Vector2(70f, 20f))!;
        var windyItem = windy.SpawnGroundItem(ItemId.SoilBlock, 1, new Vector2(70f, 20f))!;
        Tick(calm, 40);
        Tick(windy, 40);
        check(windyItem.Position.X > calmItem.Position.X + 0.3f,
            $"wind pushes an airborne dropped item downwind ({calmItem.Position.X:0.00} → {windyItem.Position.X:0.00})");

        calm.Player.Inventory.Add(ItemId.DeltaKite, 1);
        windy.Player.Inventory.Add(ItemId.DeltaKite, 1);
        Use(calm, ItemId.DeltaKite, new GridPoint(1, 1));
        Use(windy, ItemId.DeltaKite, new GridPoint(1, 1));
        Tick(windy, 180);
        var kite = windy.ActiveKite;
        check(calm.ActiveKite is null && kite is not null && kite.Altitude > 2.5f && kite.Position.X > kite.Anchor.X + 1f,
            $"a kite needs wind; in a breeze it rises downwind on its line (altitude {kite?.Altitude:0.0})");

        // ---- 4. Audio decks: recording the heard theme, then a switched-on deck overrides the music ----
        var deck = NewGame(8404);
        deck.Player.Inventory.Add(ItemId.AudioDeck, 1);
        deck.TrySelectInventorySlot(deck.Player.Inventory.FindSlot(ItemId.AudioDeck));
        deck.HeardMusicTheme = "frozen";
        Tick(deck, (int)(GameSimulation.DeckRecordSeconds * 60) + 5);
        var recorded = deck.Player.Inventory.Count(ItemId.DeckFrozen) == 1 && deck.Player.Inventory.Count(ItemId.AudioDeck) == 0;
        var feet = new GridPoint((int)MathF.Floor(deck.Player.Motion.Position.X) + 1,
            (int)MathF.Floor(deck.Player.Motion.Position.Y + deck.Player.Motion.Size.Y) - 1);
        deck.World.SetTile(feet, TileId.DeckCoast);
        Tick(deck, 25);
        var silentWhileOff = deck.MusicThemeOverride is null;
        deck.Interact(feet);
        Tick(deck);
        var playing = deck.MusicThemeOverride == "coast";
        deck.Interact(feet);
        Tick(deck);
        check(recorded && silentWhileOff && playing && deck.MusicThemeOverride is null,
            "an audio deck records the theme it hears; a placed deck switched on overrides the nearby music and off restores it");

        // ---- 5. Town pets: a permit needs a free house ----
        var pets = new GameSimulation(seed: 6666, width: 80, height: 48);
        pets.Player.Inventory.Add(ItemId.PurrbotPermit, 1);
        Use(pets, ItemId.PurrbotPermit, new GridPoint(1, 1));
        var refusedHomeless = pets.TownPets.Count == 0 && pets.Player.Inventory.Count(ItemId.PurrbotPermit) == 1;
        BuildRoom(pets, 6);
        Use(pets, ItemId.PurrbotPermit, new GridPoint(1, 1));
        var pet = pets.TownPets.FirstOrDefault();
        var moved = pet is not null && pet.Kind == TownPetKind.Purrbot && pets.Player.Inventory.Count(ItemId.PurrbotPermit) == 0;
        var petted = false;
        if (pet is not null)
        {
            pets.Player.Motion.Position = pet.Position + new Vector2(-1f, -1.2f);
            var at = pet.Position + pet.Size / 2f;
            petted = pets.Interact(new GridPoint((int)at.X, (int)at.Y)).Message.Contains("pet") && pet.HappySeconds > 0f;
        }
        var petsLoaded = RoundTrip(pets);
        Tick(petsLoaded);
        check(refusedHomeless && moved && petted && petsLoaded.TownPets.Count == 1,
            "a pet permit is refused without a free house, spawns a wandering pet in one, petting cheers it, and it persists");

        // ---- 6. Golf: the ball comes to rest, strokes count, sinking scores ----
        var golf = NewGame(8406);
        FlatGround(golf, 40, 70, 40);
        golf.World.SetTile(new GridPoint(52, 39), TileId.GolfCup);
        golf.DropGolfBallForTests(new Vector2(41.5f, 38.5f));
        Tick(golf, 90);
        var rested = golf.GolfBall is { Resting: true } ball0 && MathF.Abs(ball0.Position.Y - 40f) < 0.05f;
        golf.StrikeGolfBall(ItemId.GolfPutter, new Vector2(1f, 0f), 0.6f);
        Tick(golf, 240);
        var firstStroke = golf.GolfBall is { Resting: true, Strokes: 1 } ball1 && ball1.Position.X > 42f && ball1.Position.X < 52f;
        // Second stroke: a full charged swing with the putter, released by letting go of the use button.
        golf.Player.Motion.Position = new Vector2(golf.GolfBall!.Position.X - 1f, 40f - golf.Player.Motion.Size.Y - 0.01f);
        golf.Player.Inventory.Add(ItemId.GolfPutter, 1);
        golf.TrySelectInventorySlot(golf.Player.Inventory.FindSlot(ItemId.GolfPutter));
        for (var i = 0; i < 60; i++)
            golf.Tick(new GameInput(new MovementInput(0f), PlayerAction.UseSelected, new GridPoint(60, 39)));
        Tick(golf, 300);
        check(rested && firstStroke && golf.GolfBall is null && golf.LastGolfScore == 2 && golf.GolfHolesSunk == 1 &&
              golf.Player.Inventory.Count(ItemId.GolfBall) == 1 && golf.Flags.IsSet("npc.greenskeeper.unlocked"),
            $"a golf ball rolls to rest, each swing counts a stroke, and sinking it scores the hole (score {golf.LastGolfScore})");

        // ---- 7. Paint persists through save/load and tints ----
        var paint = NewGame(8407);
        FlatGround(paint, 40, 60, 40);
        var block = new GridPoint(42, 40);
        paint.Player.Inventory.Add(ItemId.Paintbrush, 1);
        paint.Player.Inventory.Add(ItemId.PaintIonCyan, 5);
        Use(paint, ItemId.Paintbrush, block);
        paint.PaintCell(new GridPoint(44, 40), 3, glow: true);
        var paintLoaded = RoundTrip(paint);
        check(paint.TilePaintAt(block) == 7 && paint.Player.Inventory.Count(ItemId.PaintIonCyan) == 4 &&
              paintLoaded.TilePaintAt(block) == 7 && LeisureContent.PaintColors[paintLoaded.TilePaintAt(block)].Name == "Ion cyan" &&
              paintLoaded.IsGlowCoated(new GridPoint(44, 40)) && paintLoaded.TilePaintAt(new GridPoint(44, 40)) == 3 &&
              paintLoaded.PaintedCells.Count == 2,
            "painting a block uses paint, and paint and glow coating persist through a save and choose the tint");

        // ---- 8. Secret seeds change generation ----
        var twinSeed = SecretSeeds.SeedFor("twin blights");
        var twin = new GameSimulation(seed: twinSeed, width: 400, height: 200, blight: WorldBlight.Radiated);
        var plain = new GameSimulation(seed: 8408, width: 400, height: 200, blight: WorldBlight.Radiated);
        int CountBlight(GameSimulation game, WorldBlight blight) => game.World.Cells.ToArray().Count(cell => BlightRules.BlightOf(cell.TileId) == blight);
        check(SecretSeeds.Detect(SecretSeeds.SeedFor("Twin Blights")) == SecretSeedVariant.TwinBlight &&
              SecretSeeds.Detect(8408) == SecretSeedVariant.None &&
              CountBlight(twin, WorldBlight.Radiated) > 0 && CountBlight(twin, WorldBlight.Overgrowth) > 0 &&
              CountBlight(plain, WorldBlight.Overgrowth) == 0,
            "the 'twin blights' seed grows both blights in one world; an ordinary seed grows one");

        // ---- 9. Creative: research unlocks duplication; powers stay with creative characters ----
        var profile = CharacterDraft.CreateRandom(8409, "Maker").ToProfile(Guid.NewGuid(), DateTimeOffset.UtcNow) with
            { Difficulty = CharacterDifficulty.Creative };
        var creative = NewGame(8409, profile);
        var plainPlayer = NewGame(8409);
        creative.Player.Inventory.Add(ItemId.RockBlock, 50);
        plainPlayer.Player.Inventory.Add(ItemId.RockBlock, 50);
        creative.TrySelectInventorySlot(creative.Player.Inventory.FindSlot(ItemId.RockBlock));
        var notYet = !creative.DuplicateItem(ItemId.RockBlock);
        creative.ResearchSelected();
        var before = creative.Player.Inventory.Count(ItemId.RockBlock);
        var duplicated = creative.IsResearched(ItemId.RockBlock) && creative.DuplicateItem(ItemId.RockBlock) &&
                         creative.Player.Inventory.Count(ItemId.RockBlock) > before;
        plainPlayer.TrySelectInventorySlot(plainPlayer.Player.Inventory.FindSlot(ItemId.RockBlock));
        plainPlayer.ResearchSelected();
        creative.SetTimeOfDay(10f);
        creative.CreativeTimeFrozen = true;
        Tick(creative, 300);
        var frozen = MathF.Abs(creative.ClockHour - 10f) < 0.01f;
        creative.CreativeTimeFrozen = false;
        creative.CreativeSpawnRate = 0f;
        var creativeLoaded = RoundTrip(creative);
        check(notYet && duplicated && !plainPlayer.IsResearched(ItemId.RockBlock) && !plainPlayer.DuplicateItem(ItemId.RockBlock) && frozen &&
              WorldSettings.Name(CharacterDifficulty.Creative) == "Creative" && creativeLoaded.Flags.Get("creative.spawn_rate") == 1,
            "creative research unlocks duplication; the time freeze and spawn slider work and persist; others cannot research");
    }
}
