using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// 0.2.2 (the 0.1.8 Creative leftovers, docs/roadmap/0.1.8.md C4-C5): research lives on the character and old world research
/// flags migrate into it (PC-13, X-CR-10); the weather and enemy-spread powers (Journey's); the Creative kit's reusable recall
/// and its drone already out (PC-14).
/// </summary>
public static class CreativeResearchSelfTests
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    private static CharacterProfile Maker(int seed) =>
        CharacterDraft.CreateRandom(seed, "Maker").ToProfile(Guid.NewGuid(), DateTimeOffset.UtcNow) with
            { Difficulty = CharacterDifficulty.Creative };

    private static void Tick(GameSimulation game, int ticks = 1, PlayerAction action = PlayerAction.None)
    {
        for (var i = 0; i < ticks; i++)
        {
            game.Tick(new GameInput(new MovementInput(0f), i == 0 ? action : PlayerAction.None));
            foreach (var enemy in game.Enemies.ToArray())
                enemy.TakeDamage(1e7f);
        }
    }

    private static GameSimulation RoundTrip(GameSimulation game, CharacterProfile profile) =>
        GameSimulation.Restore(JsonSerializer.Deserialize<GameSnapshot>(JsonSerializer.Serialize(game.CaptureSnapshot(), Json), Json)!, profile);

    public static void Run(Action<bool, string> check)
    {
        var previous = CharacterFileStore.Active;
        var directory = Path.Combine(Path.GetTempPath(), "rimtera-creative-research-" + Guid.NewGuid().ToString("N"));
        try
        {
            CharacterFileStore.Active = new CharacterFileStore(directory);
            ResearchTravels(check);
            WorldFlagsMigrate(check);
            CharacterFileStore.Active = null;
            Powers(check);
            Kit(check);
        }
        finally
        {
            CharacterFileStore.Active = previous;
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // ---- PC-13: research an item in world A, then world B with the same character: still researched ----
    private static void ResearchTravels(Action<bool, string> check)
    {
        var profile = Maker(9801);
        var worldA = new GameSimulation(seed: 9801, width: 120, height: 60, colonist: profile) { FamilySpawnsEnabled = false };
        worldA.Player.Inventory.Add(ItemId.RockBlock, 40);
        worldA.TrySelectInventorySlot(worldA.Player.Inventory.FindSlot(ItemId.RockBlock));
        worldA.ResearchSelected();
        var researchedInA = worldA.IsResearched(ItemId.RockBlock);
        var noWorldFlag = !worldA.Flags.All.Keys.Any(key => key.StartsWith("creative.research.", StringComparison.Ordinal));
        worldA.SaveCharacterFile();
        var saved = CharacterFileStore.Active!.TryLoad(profile.Id);
        var worldB = new GameSimulation(seed: 9802, width: 120, height: 60, colonist: profile) { FamilySpawnsEnabled = false };
        check(researchedInA && noWorldFlag && saved?.Research?.GetValueOrDefault((int)ItemId.RockBlock) >= worldA.ResearchNeeded(ItemId.RockBlock) &&
              worldB.IsResearched(ItemId.RockBlock) && worldB.ResearchedItems.Contains(ItemId.RockBlock),
            $"PC-13: research is stored on the character, not the world: an item researched in world A is researched in world B " +
            $"(A {researchedInA}, no world flag {noWorldFlag}, B {worldB.IsResearched(ItemId.RockBlock)})");
    }

    // ---- X-CR-10: an older world's creative.research.* flags move into the character on load ----
    private static void WorldFlagsMigrate(Action<bool, string> check)
    {
        var profile = Maker(9803);
        var old = new GameSimulation(seed: 9803, width: 120, height: 60, colonist: profile) { FamilySpawnsEnabled = false };
        var needed = old.ResearchNeeded(ItemId.WoodBlock);
        old.Flags.Increment("creative.research." + (int)ItemId.WoodBlock, needed); // what a 0.2.1 world stored
        old.Flags.Increment("creative.research." + (int)ItemId.WallTorch, 1);
        CharacterFileStore.Active!.Delete(profile.Id); // a character that never stored research
        var loaded = RoundTrip(old, profile);
        var migrated = loaded.IsResearched(ItemId.WoodBlock) && loaded.ResearchProgress(ItemId.WallTorch) == 1 &&
                       !loaded.Flags.All.Keys.Any(key => key.StartsWith("creative.research.", StringComparison.Ordinal));
        loaded.SaveCharacterFile();
        var elsewhere = new GameSimulation(seed: 9804, width: 120, height: 60, colonist: profile) { FamilySpawnsEnabled = false };

        // A non-Creative character never takes them (and the world keeps them).
        var classic = CharacterDraft.CreateRandom(9805, "Plain").ToProfile(Guid.NewGuid(), DateTimeOffset.UtcNow) with
            { Difficulty = CharacterDifficulty.Classic };
        var classicLoaded = RoundTrip(old, classic);
        check(migrated && elsewhere.IsResearched(ItemId.WoodBlock) && !classicLoaded.IsResearched(ItemId.WoodBlock) &&
              classicLoaded.ResearchProgress(ItemId.WoodBlock) == 0,
            $"X-CR-10: a Creative character loading an older world takes its research flags into the character (the world " +
            $"forgets them) and carries them to other worlds; other characters never do (migrated {migrated})");
    }

    // ---- Journey powers: weather control and the enemy-spread freeze ----
    private static void Powers(Action<bool, string> check)
    {
        var profile = Maker(9806);
        var game = new GameSimulation(seed: 9806, width: 160, height: 72, blight: WorldBlight.Radiated, colonist: profile)
            { FamilySpawnsEnabled = false };
        game.CreativeWeatherLocked = true;
        game.CreativeRain = 0.8f;
        game.CreativeWind = -4f;
        Tick(game, 30);
        var held = MathF.Abs(game.Weather.Rain - 0.8f) < 0.01f && MathF.Abs(game.Weather.Wind + 4f) < 0.05f;
        var loaded = RoundTrip(game, profile);
        Tick(loaded, 2);
        var persists = loaded.CreativeWeatherLocked && MathF.Abs(loaded.Weather.Rain - 0.8f) < 0.01f && MathF.Abs(loaded.CreativeWind + 4f) < 0.05f;
        var plain = new GameSimulation(seed: 9807, width: 80, height: 48) { FamilySpawnsEnabled = false };
        plain.CreativeWeatherLocked = true;
        plain.CreativeSpreadFrozen = true;
        check(held && persists && !plain.CreativeWeatherLocked && !plain.CreativeSpreadFrozen,
            $"weather control holds the rain and wind (rain {game.Weather.Rain:0.00}, wind {game.Weather.Wind:0.0}) and persists; " +
            "other characters cannot use it");

        // The spread freeze: a strip of grass beside blight soil, and crystal beside rock after the Fracture.
        const int row = 20;
        for (var x = 10; x < 150; x++)
        {
            game.World.SetTile(new GridPoint(x, row - 1), TileId.Air);
            game.World.SetTile(new GridPoint(x, row), x % 4 == 0 ? TileId.CrimsonSoil : TileId.Grass);
        }
        game.CreativeSpreadFrozen = true;
        Tick(game, 1);
        var ticker = game.Ticker;
        var blightBefore = ticker.BlightConversions;
        var crystalBefore = ticker.CrystalConversions;
        var refused = 0;
        for (var x = 10; x < 150; x++)
            if (x % 4 != 0 && !ticker.TrySpreadInto(x, row))
                refused++;
        var crystalRefused = !ticker.TrySpreadCrystal(11, row);
        ticker.RandomUpdates(200_000);
        var frozen = ticker.BlightConversions == blightBefore && ticker.CrystalConversions == crystalBefore &&
                     refused == 105 && crystalRefused && game.World.GetCell(new GridPoint(11, row)).TileId == TileId.Grass;
        game.CreativeSpreadFrozen = false;
        Tick(game, 1);
        var spreads = ticker.TrySpreadInto(11, row) && game.World.GetCell(new GridPoint(11, row)).TileId == TileId.CrimsonSoil;
        check(frozen && spreads,
            $"the spread freeze stops blight and crystal conversion in the world ticker; released, blight spreads again " +
            $"(frozen {frozen}, refused {refused}/105, spreads {spreads})");
    }

    // ---- PC-14: the Creative kit's reusable recall and its drone already out ----
    private static void Kit(Action<bool, string> check)
    {
        var profile = Maker(9808);
        var game = new GameSimulation(seed: 9808, width: 120, height: 60, colonist: profile) { FamilySpawnsEnabled = false };
        var inventory = game.Player.Inventory;
        var kit = inventory.Count(StarterKits.CreativeRecall) == 1 && inventory.Count(ItemId.RecallCapsule) == 0 && game.ActiveDrones == 1;
        var landing = game.Player.Motion.Position;
        game.Player.Motion.Position = landing + new Vector2(25f, -10f);
        game.Player.Motion.Velocity = Vector2.Zero;
        game.Player.ActionCooldownTicks = 0;
        game.TrySelectInventorySlot(inventory.FindSlot(StarterKits.CreativeRecall));
        Tick(game, 1, PlayerAction.UseSelected);
        var recalled = MathF.Abs(game.Player.Motion.Position.X - landing.X) < 3f && inventory.Count(StarterKits.CreativeRecall) == 1;
        var classic = new GameSimulation(seed: 9809, width: 80, height: 48) { FamilySpawnsEnabled = false };
        check(kit && recalled && classic.ActiveDrones == 0,
            $"PC-14: a Creative first entry holds a reusable recall (the {game.Items[StarterKits.CreativeRecall].Name}, kept after use) " +
            $"and has its drone out (kit {kit}, recalled {recalled}, drones {game.ActiveDrones})");
    }
}
