using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rimtera.Characters;
using Rimtera.Entities;
using Rimtera.UI;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// 0.2.2 stream D (the parity roadmap's 0.1.7 stream D plus 0.1.8 PU-12): save safety (PU-11: backups, stamped cell
/// sidecars, migrations, the 0.1.6 fixture, the save on close), the threaded autosave (PU-12), any character on any world
/// (PU-9), the HP-only player (IC-4), the minion lifecycle (IC-7) and loot-free statue spawns (EN-6).
/// </summary>
public static class SaveSafetySelfTests
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    public static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "rimtera-savesafety-" + Guid.NewGuid().ToString("N"));
        var previousStore = CharacterFileStore.Active;
        try
        {
            Backups(check, Path.Combine(root, "backups"));
            CrashBetweenWrites(check, Path.Combine(root, "crash"));
            Versions(check, Path.Combine(root, "versions"));
            SideFiles(check, Path.Combine(root, "side"));
            Autosave(check, Path.Combine(root, "autosave"));
            Fixture016(check, Path.Combine(root, "fixture"));
            AnyCharacterAnyWorld(check, Path.Combine(root, "characters"));
            HitPointsOnly(check);
            Minions(check);
            StatueSpawns(check);
        }
        finally
        {
            CharacterFileStore.Active = previousStore;
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static GameSimulation NewGame(int seed, CharacterProfile? character = null) =>
        new(seed: seed, width: 80, height: 48, colonist: character) { FamilySpawnsEnabled = false };

    private static CharacterProfile Character(int seed, string name) =>
        CharacterDraft.CreateRandom(seed, name).ToProfile(Guid.NewGuid(), DateTimeOffset.UtcNow).Normalized();

    private static void Tick(GameSimulation game, int ticks = 1, PlayerAction action = PlayerAction.None, GridPoint? target = null)
    {
        for (var i = 0; i < ticks; i++)
            game.Tick(new GameInput(new MovementInput(0f), action, target));
    }

    private static int SidecarCount(string directory, Guid worldId) =>
        Directory.EnumerateFiles(directory, $"{worldId:N}.*.cells").Count();

    // ------------------------------------------------------------------ PU-11: backups

    private static void Backups(Action<bool, string> check, string directory)
    {
        var repo = new WorldSaveRepository(directory);
        var worldId = Guid.NewGuid();
        var game = NewGame(9101);
        Tick(game, 5);
        repo.Save(worldId, game.CaptureSnapshot(binaryCells: true));
        var firstTick = game.TickNumber;
        var noBackupYet = !repo.HasBackup(worldId);
        Tick(game, 7);
        repo.Save(worldId, game.CaptureSnapshot(binaryCells: true));
        Tick(game, 3);
        repo.Save(worldId, game.CaptureSnapshot(binaryCells: true));
        var thirdTick = game.TickNumber;
        var sidecars = SidecarCount(directory, worldId);
        check(noBackupYet && repo.HasBackup(worldId) && sidecars == 2 &&
              repo.TryLoad(worldId, out var latest) && latest!.TickNumber == thirdTick &&
              repo.TryLoad(worldId, out var previous, out _, backup: true) && previous!.TickNumber == firstTick + 7,
            $"PU-11: each save keeps the previous one as a backup, and only the two sidecars they name stay ({sidecars})");

        // A damaged main save: the load fails with a reason, the menu can offer the backup, and the backup restores.
        File.WriteAllText(Path.Combine(directory, $"{worldId:N}.rimtera.json"), "{ \"ContainerVersion\": 1, \"Snap");
        var mainFails = !repo.TryLoad(worldId, out _, out var reason);
        var fromBackup = repo.TryLoad(worldId, out var backup, out _, backup: true) && backup is not null;
        var restored = fromBackup ? GameSimulation.Restore(backup!) : null;
        check(mainFails && reason is { Length: > 0 } && repo.HasBackup(worldId) && restored is not null &&
              restored.World.Cells.Length == game.World.Cells.Length && restored.TickNumber == firstTick + 7,
            $"PU-11: a corrupt world save fails with a reason (\"{reason}\") and its backup loads");

        // Deleting moves the save, its backup and its sidecars aside.
        var deleted = repo.Delete(worldId);
        check(deleted && !repo.Exists(worldId) && !repo.HasBackup(worldId) && SidecarCount(directory, worldId) == 0 &&
              Directory.EnumerateFiles(Path.Combine(directory, "deleted")).Count() >= 3,
            "PU-11: deleting a world moves its save, backup and sidecars into deleted/");
    }

    private static void CrashBetweenWrites(Action<bool, string> check, string directory)
    {
        var repo = new WorldSaveRepository(directory);
        var worldId = Guid.NewGuid();
        var game = NewGame(9102);
        repo.Save(worldId, game.CaptureSnapshot(binaryCells: true));
        var savedCells = game.World.Cells.ToArray();
        // The world changes, then the next save "crashes" after the new sidecar and before the envelope.
        for (var x = 10; x < 30; x++)
            game.World.SetTile(new GridPoint(x, 5), TileId.Rock);
        repo.AfterCellsWritten = () => throw new IOException("simulated crash");
        var crashed = false;
        try
        {
            repo.Save(worldId, game.CaptureSnapshot(binaryCells: true));
        }
        catch (IOException)
        {
            crashed = true;
        }
        repo.AfterCellsWritten = null;
        var loaded = repo.TryLoad(worldId, out var snapshot) && snapshot is not null;
        var restored = loaded ? GameSimulation.Restore(snapshot!) : null;
        check(crashed && loaded && restored is not null && restored.World.Cells.SequenceEqual(savedCells),
            "PU-11: a crash between the sidecar and envelope writes loads the previous consistent pair");
        repo.Save(worldId, game.CaptureSnapshot(binaryCells: true));
        check(SidecarCount(directory, worldId) == 2 && repo.TryLoad(worldId, out var after) &&
              GameSimulation.Restore(after!).World.TileAt(20, 5) == TileId.Rock,
            "PU-11: the next save succeeds and clears the orphaned sidecar");
    }

    // ------------------------------------------------------------------ PU-11: versions and migrations

    private static void Versions(Action<bool, string> check, string directory)
    {
        var repo = new WorldSaveRepository(directory);
        var worldId = Guid.NewGuid();
        var game = NewGame(9103);
        repo.Save(worldId, game.CaptureSnapshot(binaryCells: true), game.PlayerProfile.Id, game.PlayerProfile.Name);
        var path = Path.Combine(directory, $"{worldId:N}.rimtera.json");
        var text = File.ReadAllText(path);
        var current = $"\"FormatVersion\":{GameSnapshot.CurrentFormatVersion}";
        File.WriteAllText(path, text.Replace(current, $"\"FormatVersion\":{GameSnapshot.CurrentFormatVersion + 1}"));
        var refused = !repo.TryReadFile(worldId, out _, out var error);
        check(text.Contains(current) && refused && error?.Contains("newer version", StringComparison.OrdinalIgnoreCase) == true,
            $"PU-11: a save from a newer version is refused with a message (\"{error}\")");
        var restoreRefused = false;
        try
        {
            GameSimulation.Restore(game.CaptureSnapshot() with { FormatVersion = GameSnapshot.CurrentFormatVersion + 1 });
        }
        catch (InvalidDataException)
        {
            restoreRefused = true;
        }
        check(restoreRefused, "PU-11: restoring a newer snapshot directly is refused too");

        // Every version from the oldest supported up to the current one loads through the migration table.
        var chain = Enumerable.Range(SaveMigrations.OldestSupported, GameSnapshot.CurrentFormatVersion - SaveMigrations.OldestSupported)
            .All(SaveMigrations.Steps.ContainsKey);
        var v2 = game.CaptureSnapshot() with { FormatVersion = 2, Players = null };
        var migrated = SaveMigrations.Upgrade(v2);
        var envelopeOnly = SaveMigrations.Upgrade(v2 with { Character = null }, savedBy: game.PlayerProfile.Id);
        check(chain && migrated.FormatVersion == GameSnapshot.CurrentFormatVersion &&
              migrated.Players is [{ } only] && only.CharacterId == game.PlayerProfile.Id && only.X == v2.PlayerX && only.Y == v2.PlayerY &&
              envelopeOnly.Players is [{ } fromEnvelope] && fromEnvelope.CharacterId == game.PlayerProfile.Id &&
              !SaveMigrations.CanRead(1) && SaveMigrations.CanRead(2) && !SaveMigrations.CanRead(GameSnapshot.CurrentFormatVersion + 1),
            "PU-11/PU-9: migrations run from every supported version; the old single position moves into the placement map under the saving character");
    }

    // ------------------------------------------------------------------ PU-11: character, map and profile backups

    private static void SideFiles(Action<bool, string> check, string directory)
    {
        var store = new CharacterFileStore(Path.Combine(directory, "characters"));
        var game = NewGame(9104);
        game.Player.Inventory.Add(ItemId.EmeraldShard, 3);
        store.Save(game.CaptureCharacter());
        game.Player.Inventory.Add(ItemId.EmeraldShard, 2);
        store.Save(game.CaptureCharacter());
        File.WriteAllText(store.PathFor(game.PlayerProfile.Id), "not json");
        var character = store.TryLoad(game.PlayerProfile.Id, out var characterFromBackup);
        check(characterFromBackup && character is not null && character.Inventory.Where(s => s.ItemId == ItemId.EmeraldShard).Sum(s => s.Count) == 3,
            "PU-11: a damaged .rchar loads its .rchar.bak");

        var mapPath = Path.Combine(directory, "maps", "world_character.rmap");
        var map = new WorldMapState(game.World.Width, game.World.Height);
        map.RevealArea(game.World, 40, 20, 8);
        map.Save(mapPath);
        map.RevealArea(game.World, 10, 10, 6);
        map.Save(mapPath);
        File.WriteAllBytes(mapPath, [1, 2, 3]);
        var loadedMap = WorldMapState.Load(mapPath, game.World.Width, game.World.Height);
        check(File.Exists(SafeFile.BackupPath(mapPath)) && loadedMap.IsRevealed(40, 20),
            "PU-11: a damaged .rmap loads its .rmap.bak");

        var profilesPath = Path.Combine(directory, "profiles.json");
        var profiles = new UserProfileStore(profilesPath);
        profiles.CreateWorld("Backup World", 7);
        profiles.CreateCharacter("Backup Pilot");
        File.WriteAllText(profilesPath, "{ broken");
        var reloaded = new UserProfileStore(profilesPath);
        check(reloaded.LoadedFromBackup && reloaded.Worlds.Any(w => w.Name == "Backup World"),
            "PU-11: a damaged profiles.json loads profiles.json.bak (the world list is not lost)");
    }

    // ------------------------------------------------------------------ PU-12: the autosave off the game thread; save on close

    private static void Autosave(Action<bool, string> check, string directory)
    {
        var repo = new WorldSaveRepository(directory);
        var saving = new SaveCoordinator(repo);
        var worldId = Guid.NewGuid();
        var game = NewGame(9105);
        var marker = new GridPoint(12, 6);
        game.World.SetTile(marker, TileId.Air);
        using var gate = new ManualResetEventSlim(false);
        repo.AfterCellsWritten = () => gate.Wait(TimeSpan.FromSeconds(30));
        var started = saving.TryBeginAutosave(game, worldId, game.PlayerProfile.Id, game.PlayerProfile.Name);
        // Play goes on while the worker writes: this change is after the capture, so it is not in this save.
        game.World.SetTile(marker, TileId.Rock);
        var skipped = !saving.TryBeginAutosave(game, worldId, null, null);
        gate.Set();
        saving.WaitForPending();
        repo.AfterCellsWritten = null;
        var loaded = repo.TryLoad(worldId, out var snapshot) && snapshot is not null;
        check(started && skipped && saving.Skipped == 1 && loaded && saving.LastError is null &&
              GameSimulation.Restore(snapshot!).World.TileAt(marker.X, marker.Y) == TileId.Air,
            "PU-12: the autosave captures on the game thread, writes on a worker, and skips a tick while a save is still writing");

        // The game-thread part is only the cell run pass; the compression moves to the worker and decodes the same.
        var runs = CellCodec.EncodeRuns(game.World.Cells);
        var decoded = CellCodec.DecodeBytes(CellCodec.DeflateRuns(runs), game.World.Count);
        var deferred = game.CaptureSnapshot(binaryCells: true, deferCellCompression: true);
        check(decoded.AsSpan().SequenceEqual(game.World.Cells) && deferred.CellData is null && deferred.CellRuns is { Length: > 0 } &&
              GameSimulation.Restore(deferred).World.Cells.SequenceEqual(game.World.Cells),
            "PU-12: deferred cell compression round-trips exactly");

        var big = new GameSimulation(seed: 9106, width: 1750, height: 900);
        var full = double.MaxValue;
        var quick = double.MaxValue;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            big.CaptureSnapshot(binaryCells: true);
            full = Math.Min(full, watch.Elapsed.TotalMilliseconds);
            watch.Restart();
            big.CaptureSnapshot(binaryCells: true, deferCellCompression: true);
            quick = Math.Min(quick, watch.Elapsed.TotalMilliseconds);
        }
        check(quick < full,
            $"PU-12: an autosave's game-thread capture of a Tiny world takes {quick:0} ms against {full:0} ms with the compression inline");

        // D-42: closing the window saves an active world whatever the Autosave setting.
        var closeId = Guid.NewGuid();
        if (SaveCoordinator.SavesOnClose(worldActive: true, autosaveEnabled: false))
            saving.SaveNow(game, closeId, game.PlayerProfile.Id, game.PlayerProfile.Name);
        check(repo.Exists(closeId) && !SaveCoordinator.SavesOnClose(worldActive: false, autosaveEnabled: true),
            "PU-11 (D-42): with Autosave off, closing the window still writes the save");

        var prefs = new PreferencesStore<GamePreferences>(Path.Combine(directory, "prefs.json"));
        prefs.Save(new GamePreferences { AutosaveMinutes = 5, AutoSave = false });
        var back = prefs.Load();
        check(back.AutosaveMinutes == 5 && !back.AutoSave, "PU-12: the autosave interval and switch round-trip in the preferences");
    }

    // ------------------------------------------------------------------ PU-11: the 0.1.6 fixture

    /// <summary>
    /// tests/fixtures/saves/0.1.6/ was written by the v0.1.6 tag's own code (UserProfileStore, CharacterFileStore,
    /// WorldSaveRepository and WorldMapState): a 400×200 world created through the profile store (blight, so the detailed
    /// generator and its metadata), 30 ticks walked, 7 emerald shards added and 12 damage taken.
    /// </summary>
    private static void Fixture016(Action<bool, string> check, string directory)
    {
        var source = FixtureDirectory("0.1.6");
        if (source is null)
        {
            check(false, "PU-11: the 0.1.6 save fixture (tests/fixtures/saves/0.1.6) is present");
            return;
        }
        CopyDirectory(source, directory);
        var profiles = new UserProfileStore(Path.Combine(directory, "profiles.json")); // also makes its characters/ the active store
        var world = profiles.Worlds.SingleOrDefault(w => w.Name == "Fixture World");
        var character = profiles.Characters.SingleOrDefault(c => c.Name == "Fixture Pilot");
        var repo = new WorldSaveRepository(Path.Combine(directory, "saves"));
        WorldSaveFile? file = null;
        var read = world is not null && repo.TryReadFile(world.Id, out file, out _);
        GameSimulation? game = null;
        if (read && character is not null)
            game = GameSimulation.Restore(file!.Snapshot, character);
        var mapPath = Path.Combine(directory, "maps", $"{world?.Id:N}_{character?.Id:N}.rmap");
        var map = world is null ? null : WorldMapState.Load(mapPath, world.Width, world.Height);
        check(world is { Width: 400, Height: 200 } && character is not null && read && game is not null &&
              file!.Snapshot.FormatVersion == GameSnapshot.CurrentFormatVersion &&
              file.Snapshot.Players is [{ } placement] && placement.CharacterId == character.Id &&
              MathF.Abs(game.Player.Motion.Position.X - 202.04f) < 0.5f && game.Player.HitPoints == 88f &&
              game.Player.Inventory.Count(ItemId.EmeraldShard) == 7 && game.TickNumber == 30 &&
              map is { RevealedChunkCount: > 0, DeathMarker: not null },
            "PU-11: the 0.1.6 world, character, map and profile fixture loads (migrated to the current format)");

        if (game is null || world is null || character is null)
            return;
        repo.Save(world.Id, game.CaptureSnapshot(binaryCells: true), character.Id, character.Name);
        Tick(game, 2);
        repo.Save(world.Id, game.CaptureSnapshot(binaryCells: true), character.Id, character.Name);
        check(repo.TryLoad(world.Id, out var resaved) && resaved!.TickNumber == 32 && repo.HasBackup(world.Id) &&
              !File.Exists(Path.Combine(directory, "saves", $"{world.Id:N}.rimtera.cells")) &&
              File.Exists(SafeFile.BackupPath(profiles.CharacterFiles.PathFor(character.Id))),
            "PU-11: the 0.1.6 world saves in the new layout (stamped sidecars, backups); the old unstamped sidecar is cleared");
    }

    private static string? FixtureDirectory(string version, [CallerFilePath] string sourceFile = "")
    {
        var candidates = new List<string>();
        if (Path.GetDirectoryName(sourceFile) is { Length: > 0 } systems && Path.GetDirectoryName(systems) is { } repoRoot)
            candidates.Add(repoRoot);
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                candidates.Add(dir.FullName);
        return candidates.Select(root => Path.Combine(root, "tests", "fixtures", "saves", version)).FirstOrDefault(Directory.Exists);
    }

    private static void CopyDirectory(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    // ------------------------------------------------------------------ PU-9: any character may play any world (D-43)

    private static void AnyCharacterAnyWorld(Action<bool, string> check, string directory)
    {
        var store = new CharacterFileStore(Path.Combine(directory, "characters"));
        CharacterFileStore.Active = store;
        var repo = new WorldSaveRepository(Path.Combine(directory, "saves"));
        var worldId = Guid.NewGuid();
        var ana = Character(9201, "Ana");
        var ben = Character(9202, "Ben");

        // 1. A plays W and saves, away from the landing site, carrying something B never had.
        var asAna = NewGame(9203, ana);
        var landing = asAna.Player.Motion.Position;
        asAna.Player.Inventory.Add(ItemId.EmeraldShard, 5);
        asAna.Player.Motion.Position = landing + new Vector2(9f, -3f);
        asAna.Player.TakeDamage(20f);
        var anaHp = asAna.Player.HitPoints;
        repo.Save(worldId, asAna.CaptureSnapshot(binaryCells: true), ana.Id, ana.Name);
        var anaBytes = File.ReadAllBytes(store.PathFor(ana.Id));

        // 2. B launches W: B, with B's own kit, at the landing site; A's file untouched.
        repo.TryLoad(worldId, out var saved);
        var asBen = GameSimulation.Restore(saved!, ben);
        var benAtLanding = MathF.Abs(asBen.Player.Motion.Position.X - landing.X) < 1.5f;
        check(asBen.PlayerProfile.Id == ben.Id && asBen.Player.Inventory.Count(ItemId.EmeraldShard) == 0 &&
              asBen.Player.Inventory.Count(ItemId.FerriteStiletto) > 0 && benAtLanding &&
              asBen.Player.HitPoints == asBen.Player.MaximumHitPoints,
            "PU-9: another character launching a saved world plays as itself, with its own carried state, at the landing site");
        asBen.Player.Motion.Position = landing + new Vector2(-6f, -2f);
        repo.Save(worldId, asBen.CaptureSnapshot(binaryCells: true), ben.Id, ben.Name);
        check(anaBytes.AsSpan().SequenceEqual(File.ReadAllBytes(store.PathFor(ana.Id))) && store.Exists(ben.Id),
            "PU-9: B playing and saving A's world leaves A's character file byte for byte unchanged");

        // 3. A returns: A's own place, hit points and inventory.
        repo.TryLoad(worldId, out var afterBen);
        var anaAgain = GameSimulation.Restore(afterBen!, ana);
        var benAgain = GameSimulation.Restore(afterBen!, ben);
        check(afterBen!.Players is { Length: 2 } &&
              MathF.Abs(anaAgain.Player.Motion.Position.X - (landing.X + 9f)) < 0.5f && anaAgain.Player.HitPoints == anaHp &&
              anaAgain.Player.Inventory.Count(ItemId.EmeraldShard) == 5 &&
              MathF.Abs(benAgain.Player.Motion.Position.X - (landing.X - 6f)) < 0.5f && benAgain.Player.Inventory.Count(ItemId.EmeraldShard) == 0,
            "PU-9: each character returns to its own place in the world (A's position, health and pack restored)");

        // 0.1.13's Creative split still holds (decided after D-43): Creative and non-Creative stay apart, but a character may
        // always return to a world it saved.
        var creative = ana with { Difficulty = CharacterDifficulty.Creative };
        var classicWorld = new WorldProfile(Guid.NewGuid(), "Classic", 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 160, 72);
        var creativeWorld = classicWorld with { CreatedByCreative = true };
        check(!classicWorld.Admits(creative) && creativeWorld.Admits(creative) && classicWorld.Admits(ben) && !creativeWorld.Admits(ben) &&
              classicWorld.Admits(creative, savedBy: creative.Id),
            "PU-9: any character plays any world within the Creative split (and may always return to a world it saved)");
        CharacterFileStore.Active = null;
    }

    // ------------------------------------------------------------------ IC-4: the player is HP-only (D-36)

    private static void HitPointsOnly(Action<bool, string> check)
    {
        var game = NewGame(9301);
        var random = new Random(9301);
        var kinds = Enum.GetValues<DamageKind>();
        var everDown = false;
        for (var hit = 0; hit < 10_000; hit++)
        {
            var amount = 0.5f + (float)random.NextDouble() * 30f;
            if (game.Player.HitPoints - amount <= 1f)
                game.Player.Heal(game.Player.MaximumHitPoints);
            game.Player.TakeDamage(amount, kinds[random.Next(kinds.Length)]);
            everDown |= game.Player.IsDead || game.Player.IsDowned || game.Player.Health.IsDead || game.Player.Health.IsDowned;
        }
        game.Player.Health.AddWholeBody(ConditionKind.BloodLoss, 1f); // even a body that would bleed out
        Tick(game, 60);
        check(!everDown && !game.Player.IsDead && !game.IsOver && game.Player.Health.Injuries.Count(i => i.Kind != ConditionKind.BloodLoss) == 0 &&
              game.Player.MoveCapacityFactor == 1f,
            "IC-4: 10,000 random creature hits that leave HP above 0 never kill, down, wound or slow the player");

        var dying = NewGame(9302);
        dying.Player.TakeDamage(dying.Player.HitPoints - 0.01f);
        var aliveAtSliver = !dying.Player.IsDead;
        dying.Player.TakeDamage(dying.Player.HitPoints); // the last sliver
        check(aliveAtSliver && dying.Player.IsDead && dying.Player.HitPoints == 0f,
            "IC-4: the player dies exactly when hit points reach 0");

        var brain = new HealthComponent(ColonistAnatomy.CreateTorso(), seed: 9);
        brain.ApplyDamageToPart(brain.FindPart("brain")!, 50f, DamageKind.Shot);
        check(brain.IsDead,
            "IC-4: the full body model still kills on organ loss (it returns for the colony's people in 0.5.3)");

        // Melee never misses: a character whose backstory gives a poor hit chance still connects every swing.
        CharacterProfile? clumsy = null;
        for (var seed = 1; seed < 400 && clumsy is null; seed++)
        {
            var candidate = Character(seed, "Clumsy");
            if (candidate.Stats.MeleeHitChance < 0.9f)
                clumsy = candidate;
        }
        var fighter = NewGame(9304, clumsy);
        var sword = fighter.Items[ItemId.FerriteStiletto];
        var attack = typeof(GameSimulation).GetMethod("AttackMelee", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var spawnAt = fighter.Player.Motion.Position + new Vector2(1.5f, 0f);
        var target = fighter.SpawnCreature(EnemyKind.MossSlime, spawnAt);
        var cell = new GridPoint((int)(target.Motion.Position.X + target.Motion.Size.X / 2f), (int)(target.Motion.Position.Y + target.Motion.Size.Y / 2f));
        var missed = 0;
        for (var swing = 0; swing < 200; swing++)
        {
            if (!target.IsAlive)
            {
                target = fighter.SpawnCreature(EnemyKind.MossSlime, spawnAt);
                target.Motion.Position = spawnAt;
            }
            fighter.Player.ActionCooldownTicks = 0;
            attack.Invoke(fighter, [sword, cell]);
            if (fighter.LastMessage.Contains("wide", StringComparison.OrdinalIgnoreCase) || fighter.LastMessage.Contains("missed", StringComparison.OrdinalIgnoreCase))
                missed++;
        }
        check(clumsy is not null && missed == 0 && fighter.Player.MeleeDamageMultiplier == 1f && fighter.Player.ShootingDamageMultiplier == 1f &&
              CombatStatsCalculator.PlayerSkillMultiplier(clumsy.Stats, DamageClass.Melee) == 1f,
            $"IC-4: player melee never misses and the backstory's melee and shooting multipliers do not apply (hit chance {clumsy?.Stats.MeleeHitChance:P0})");
    }

    // ------------------------------------------------------------------ IC-7: minion lifecycle

    private static void Minions(Action<bool, string> check)
    {
        var game = NewGame(9401);
        var remote = game.Items[ItemId.ScoutDroneRemote];
        var cell = new GridPoint((int)game.Player.Motion.Position.X, (int)game.Player.Motion.Position.Y);
        var drone = game.SummonMinion(remote, MinionKind.ScoutDrone, cell)!;
        Tick(game);
        var before = drone.Damage;
        var multiplierBefore = game.CombatStats.CommandMultiplier;
        var bonusItem = game.Items.All
            .Where(item => item.CommandDamageBonus >= 0.1f && item.EquipmentSlot is not null && item.UseKind == ItemUseKind.Equipment && !item.IsVanity)
            .OrderByDescending(item => item.CommandDamageBonus).First();
        game.Player.Inventory.Add(bonusItem.Id, 1);
        var slot = Enumerable.Range(0, game.Player.Inventory.SlotCount).First(i => game.Player.Inventory.Slots[i].ItemId == bonusItem.Id);
        var equipped = game.TryEquipFromInventory(slot);
        Tick(game, (int)(MinionCatalog.Get(MinionKind.ScoutDrone).FireIntervalSeconds * 60f) + 3);
        var multiplierAfter = game.CombatStats.CommandMultiplier;
        var expected = before * multiplierAfter / multiplierBefore;
        check(equipped && multiplierAfter > multiplierBefore && drone.Damage > before && MathF.Abs(drone.Damage - expected) <= 1.01f &&
              MathF.Abs(drone.Damage - game.ComputeWeaponDamage(remote, drone.Calibration)) < 0.001f,
            $"IC-7: gear equipped after summoning counts on the next shot (+{bonusItem.CommandDamageBonus:P0} command: {before:0.#} → {drone.Damage:0.#})");

        game.Player.Inventory.Add(ItemId.CommandVisor, 1);
        var visorSlot = Enumerable.Range(0, game.Player.Inventory.SlotCount).First(i => game.Player.Inventory.Slots[i].ItemId == ItemId.CommandVisor);
        game.TryEquipFromInventory(visorSlot);
        var slots = game.DroneSlots;
        for (var i = 0; i < slots; i++)
            game.SummonMinion(remote, MinionKind.ScoutDrone, cell);
        var full = game.ActiveDrones;
        var visorAt = Enum.GetValues<EquipmentSlot>().First(s => game.Player.Equipment[s] == ItemId.CommandVisor);
        EquipmentSystem.Unequip(game.Player.Inventory, game.Player.Equipment, visorAt);
        Tick(game);
        check(full == slots && slots >= 2 && game.ActiveDrones == game.DroneSlots && game.DroneSlots == slots - 1,
            $"IC-7: removing a slot item trims the drones to the new cap ({full} → {game.ActiveDrones})");

        game.Player.TakeDamage(1e6f);
        Tick(game);
        check(game.Player.IsDead && game.Minions.Count == 0, "IC-7: death unsummons every drone and sentry");

        var saved = NewGame(9402);
        saved.SummonMinion(remote, MinionKind.ScoutDrone, cell);
        check(saved.Minions.Count == 1 && GameSimulation.Restore(saved.CaptureSnapshot()).Minions.Count == 0,
            "IC-7: minions are not saved: a loaded world starts with none, as in Terraria");

        var spriteDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Sprites");
        var names = Directory.Exists(spriteDir)
            ? Directory.EnumerateFiles(spriteDir, "*.png").Select(Path.GetFileNameWithoutExtension).ToHashSet()!
            : new HashSet<string?>();
        bool Resolves(string name) => names.Contains(SpriteAtlas.ResolveName(name, n => names.Contains(n)));
        var unresolved = MinionCatalog.All
            .Where(spec => !Resolves(spec.Sprite) && !(spec.StandInSprite is { } standIn && Resolves(standIn)))
            .Select(spec => spec.Kind).ToArray();
        check(MinionCatalog.All.Count() == 18 && names.Count > 0 && unresolved.Length == 0,
            $"IC-7: all 18 minion kinds resolve to art (own or the summoning item's stand-in){(unresolved.Length > 0 ? ": missing " + string.Join(", ", unresolved) : "")}");
    }

    // ------------------------------------------------------------------ EN-6: statue spawns give no loot

    private static void StatueSpawns(Action<bool, string> check)
    {
        (int Credits, int Ground, int Kills, int Kinds) Farm(bool fromStatue)
        {
            var game = NewGame(9501);
            game.Player.Inventory.TryConsume(ItemId.Credits, game.Player.Inventory.Count(ItemId.Credits));
            var groundBefore = game.GroundItems.Count;
            for (var i = 0; i < 60; i++)
            {
                var hopper = game.SpawnCreature(EnemyKind.GelHopper, game.Player.Motion.Position + new Vector2(2f, -1f));
                hopper.FromStatue = fromStatue;
                hopper.TakeDamage(1e7f);
                Tick(game);
            }
            return (game.Player.Inventory.Count(ItemId.Credits), game.GroundItems.Count - groundBefore,
                game.KillCounts.GetValueOrDefault(EnemyKind.GelHopper),
                game.Player.Inventory.Slots.Count(s => !s.IsEmpty));
        }
        var wild = Farm(fromStatue: false);
        var statue = Farm(fromStatue: true);
        var baseline = NewGame(9501).Player.Inventory.Slots.Count(s => !s.IsEmpty);
        check(wild.Kills == 60 && (wild.Credits > 0 || wild.Ground > 0) &&
              statue.Credits == 0 && statue.Ground == 0 && statue.Kills == 0 && statue.Kinds <= baseline,
            $"EN-6: 60 statue gel hoppers give 0 credits, 0 items, no kill count and no banner (wild: {wild.Kills} kills, {wild.Credits} credits, {wild.Ground} drops)");

        var game = NewGame(9502);
        var trigger = typeof(GameSimulation).GetMethod("TriggerStatue", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var spot = new GridPoint((int)game.Player.Motion.Position.X + 4, (int)game.Player.Motion.Position.Y);
        var released = (int)trigger.Invoke(game, [spot, TileId.StatueCrab])!;
        var crab = game.Enemies.LastOrDefault(enemy => enemy.Kind == EnemyKind.GeodeCrab);
        var restored = crab is null ? null : GameSimulation.Restore(game.CaptureSnapshot()).Enemies.FirstOrDefault(enemy => enemy.Kind == EnemyKind.GeodeCrab);
        check(released == 1 && crab is { FromStatue: true } && restored is { FromStatue: true },
            "EN-6: a statue's creature is marked as such, and the mark survives a save");
    }
}
