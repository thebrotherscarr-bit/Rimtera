using System.Numerics;
using System.Runtime.CompilerServices;
using Rimtera.Entities;
using Rimtera.UI;
using Rimtera.World;

namespace Rimtera.Systems;

/// <summary>
/// 0.1.8 leftovers, landed in 0.2.2: D3 PU-15 (the map obeys Map enabled; style, scale and opacities are preferences;
/// housed residents' heads on the maps), D5 PU-24 (Achievements and Credits on the title; machine-global feats migrated
/// from every character), D6 PU-26 (the video effect rows), D7 PE-16 (screen shake and its row) and E1 PE-15 (audio
/// buffers, pitch variance, voice stealing). Rendering and sound themselves are play-tested; these check the rules.
/// </summary>
public static class MenuParitySelfTests
{
    public static void Run(Action<bool, string> check)
    {
        var previousCharacters = CharacterFileStore.Active;
        var previousFeats = FeatStore.Active;
        var root = Path.Combine(Path.GetTempPath(), "rimtera-menu-parity-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            CheckMap(check, root);
            CheckFeats(check, root);
            CheckTitle(check, root);
            CheckVideoRows(check, root);
            CheckScreenShake(check);
            CheckAudio(check);
        }
        finally
        {
            CharacterFileStore.Active = previousCharacters;
            FeatStore.Active = previousFeats;
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static GameSimulation NewGame(int seed) => new(seed: seed, width: 80, height: 48) { FamilySpawnsEnabled = false };

    // ---- D3 · PU-15 ----
    private static void CheckMap(Action<bool, string> check, string root)
    {
        var off = new GamePreferences { MinimapEnabled = false, MapView = MapOverlayStyle.Overlay };
        var offMini = new GamePreferences { MinimapEnabled = false, MapView = MapOverlayStyle.Minimap };
        var on = new GamePreferences { MapView = MapOverlayStyle.Overlay };
        check(!MapDisplay.DrawsOverlay(off) && !MapDisplay.DrawsMinimap(off) && !MapDisplay.DrawsMinimap(offMini) &&
              MapDisplay.DrawsOverlay(on) && !MapDisplay.DrawsMinimap(on) && MapDisplay.DrawsMinimap(new GamePreferences()),
            "PU-15: with the map disabled neither the overlay nor the minimap draws; enabled, the chosen style does");

        var cycle = new GamePreferences();
        var styles = new[] { MapDisplay.CycleStyle(cycle), MapDisplay.CycleStyle(cycle), MapDisplay.CycleStyle(cycle) };
        check(styles.SequenceEqual([MapOverlayStyle.Overlay, MapOverlayStyle.Off, MapOverlayStyle.Minimap]),
            "PU-15: Tab cycles minimap → overlay → off → minimap");

        // Style, scale and both opacities survive a restart (written to config.json, read back and normalised).
        var store = new PreferencesStore<GamePreferences>(Path.Combine(root, "map-config.json"));
        var saved = new GamePreferences { MapView = MapOverlayStyle.Overlay };
        for (var i = 0; i < 20; i++)
            MapDisplay.AdjustOpacity(saved, -MapDisplay.OpacityStep);
        saved.MapView = MapOverlayStyle.Minimap;
        for (var i = 0; i < 10; i++)
            MapDisplay.AdjustOpacity(saved, -MapDisplay.OpacityStep);
        MapDisplay.ZoomMinimap(saved, 2);
        saved.MapView = MapOverlayStyle.Overlay;
        store.Save(saved);
        var loaded = store.Load();
        loaded.Normalize();
        check(loaded.MapView == MapOverlayStyle.Overlay && loaded.MinimapScale == 4 &&
              MathF.Abs(loaded.OverlayOpacity - (0.55f - 20 * MapDisplay.OpacityStep)) < 0.001f &&
              MathF.Abs(loaded.MinimapOpacity - (1f - 10 * MapDisplay.OpacityStep)) < 0.001f,
            $"PU-15: map style, minimap scale and both opacities survive a restart ({loaded.MapView}, ×{loaded.MinimapScale}, {loaded.OverlayOpacity:0.00}, {loaded.MinimapOpacity:0.00})");
        var wild = new GamePreferences { MinimapScale = 40, MinimapOpacity = 0f, OverlayOpacity = float.NaN, MapView = (MapOverlayStyle)9 };
        wild.Normalize();
        check(wild.MinimapScale == 6 && wild.MinimapOpacity == 0.05f && wild.OverlayOpacity == 0.55f && wild.MapView == MapOverlayStyle.Minimap,
            "PU-15: saved map values out of range are clamped on load");

        // N housed residents give N heads; bound survivors and homeless arrivals only on the full map once seen.
        var game = NewGame(9701);
        var housed = new[] { TownNpcKind.Quartermaster, TownNpcKind.StationAi, TownNpcKind.TravellingMerchant }
            .Select((kind, i) => game.AddTownNpc(kind, new Vector2(10 + i * 4, 20), bound: false)).ToArray();
        foreach (var npc in housed)
            npc.HomeCell = new GridPoint((int)npc.Motion.Position.X, (int)npc.Motion.Position.Y);
        var bound = game.AddTownNpc(TownNpcKind.Quartermaster, new Vector2(40, 20), bound: true);
        var homeless = game.AddTownNpc(TownNpcKind.StationAi, new Vector2(50, 20), bound: false);
        var minimapHeads = MapDisplay.NpcHeads(game);
        var fullMapSeen = MapDisplay.NpcHeads(game, (_, _) => true);
        var fullMapUnseen = MapDisplay.NpcHeads(game, (_, _) => false);
        check(minimapHeads.Count == housed.Length && minimapHeads.All(head => head.Housed && head.Sprite == head.Npc.SpriteName && head.Sprite.Length > 0) &&
              fullMapUnseen.Count == housed.Length && fullMapSeen.Count == housed.Length + 2 &&
              fullMapSeen.Any(head => head.Npc == bound && !head.Housed) && fullMapSeen.Any(head => head.Npc == homeless),
            $"PU-15: {housed.Length} housed residents give {minimapHeads.Count} heads on the minimap and full map (drawn with the NPC sprite); others only once seen");
    }

    // ---- D5 · PU-24: machine-global feats ----
    private static void CheckFeats(Action<bool, string> check, string root)
    {
        var characters = Path.Combine(root, "characters");
        Directory.CreateDirectory(characters);
        File.WriteAllText(Path.Combine(characters, Guid.NewGuid().ToString("N") + CharacterFileStore.Extension),
            """{"FormatVersion":1,"Name":"Ada","Achievements":["first-ore","first-blood"]}""");
        File.WriteAllText(Path.Combine(characters, Guid.NewGuid().ToString("N") + CharacterFileStore.Extension),
            """{"FormatVersion":1,"Name":"Bo","Achievements":["first-blood","deep-delver","not-a-real-feat"]}""");
        File.WriteAllText(Path.Combine(characters, Guid.NewGuid().ToString("N") + CharacterFileStore.Extension), "not json");
        var path = Path.Combine(root, "feats.json");
        var first = FeatStore.Open(path, characters);
        check(first.Migrated && File.Exists(path) && first.Count == 3 &&
              first.IsUnlocked("first-ore") && first.IsUnlocked("first-blood") && first.IsUnlocked("deep-delver"),
            "PU-24: the first run builds feats.json as the union of every character's feats (unknown ids and damaged files skipped)");
        var reopened = FeatStore.Open(path, characters);
        check(!reopened.Migrated && reopened.Count == 3, "PU-24: later runs read feats.json and do not migrate again");

        FeatStore.Active = reopened;
        var characterA = NewGame(9702);
        var unlockedByA = characterA.Achievements.TryUnlock("easy-rider");
        var characterB = NewGame(9703);
        var announcedAgain = characterB.Achievements.TryUnlock("easy-rider");
        check(unlockedByA && reopened.IsUnlocked("easy-rider") && characterB.Achievements.IsUnlocked("easy-rider") &&
              characterB.Achievements.IsUnlocked("first-ore") && !announcedAgain && FeatStore.Open(path, null).IsUnlocked("easy-rider"),
            "PU-24: a feat unlocked by character A shows for character B (and is saved machine-wide, not announced twice)");
        FeatStore.Active = null;
        check(!NewGame(9704).Achievements.IsUnlocked("easy-rider"), "PU-24: with no machine store (tools, tests) a new character starts with no feats");
    }

    // ---- D5 · PU-24: the title rows ----
    private static void CheckTitle(Action<bool, string> check, string root)
    {
        var flow = new MenuFlow(new UserProfileStore(Path.Combine(root, "profiles.json")), root);
        flow.Open();
        var labels = flow.CurrentScreen.Entries.Select(entry => entry.Label).ToArray();
        Open(flow, "Achievements");
        var feats = flow.CurrentScreen;
        var featsOpen = feats.Id == "title-feats" && feats.ListLayout == MenuLayout.Cards &&
                        feats.Entries.Count == AchievementCatalog.All.Count + 1 &&
                        feats.Entries.Take(4).Count(entry => entry.Tag is CardIcon { Locked: false }) == 4 && // the 4 feats saved above, first
                        (feats.Body ?? "").StartsWith("4 of", StringComparison.Ordinal);
        flow.Handle(new UiInputFrame(default, BackPressed: true));
        var back = flow.CurrentScreen.Id == "main-menu";
        Open(flow, "Credits");
        var credits = flow.CurrentScreen;
        var creditsOpen = credits.Id == "title-credits" && credits.Entries[^1].Label == "Back" &&
                          (File.Exists(MenuFlow.CreditsPath)
                              ? credits.Title == "Rimtera" && credits.Entries.Any(entry => entry.Label == "Created by Kyle.")
                              : credits.Entries.Any(entry => entry.Label.Contains("CREDITS.md", StringComparison.Ordinal)));
        check(labels.SequenceEqual(["Single Player", "Achievements", "Settings", "Credits", "Exit Game"]) && featsOpen && back && creditsOpen,
            "PU-24: the title lists Achievements and Credits; Achievements shows this computer's feats, Credits reads CREDITS.md");

        var (title, lines) = MenuFlow.ReadCredits(Path.Combine(RepositoryRoot(), "docs", "CREDITS.md"));
        check(title == "Rimtera" && lines.SequenceEqual(["Created by Kyle."]),
            "PU-24: docs/CREDITS.md (copied beside the game) holds the owner's credits: \"Rimtera\", \"Created by Kyle.\"");
        var (missingTitle, missing) = MenuFlow.ReadCredits(Path.Combine(root, "no-credits.md"));
        check(missingTitle == "Credits" && missing.Count == 1, "PU-24: a missing credits file shows a note instead of failing");
    }

    // ---- D6 · PU-26 and D7 · PE-16: the rows ----
    private static void CheckVideoRows(Action<bool, string> check, string root)
    {
        var configRoot = Path.Combine(root, "rows");
        var flow = new MenuFlow(new UserProfileStore(Path.Combine(configRoot, "profiles.json")), configRoot);
        flow.Open();
        Open(flow, "Settings");
        Open(flow, "Video");
        string[] Labels() => flow.CurrentScreen.Entries.Select(entry => entry.Label).ToArray();
        var before = Labels();
        Open(flow, "Particles:");
        Open(flow, "Weather effects:");
        Open(flow, "Flashing effects:");
        var after = Labels();
        var prefs = flow.Preferences;
        check(before.Contains("Particles: High") && before.Contains("Weather effects: On") && before.Contains("Flashing effects: On") &&
              !before.Any(label => label.StartsWith("Background", StringComparison.Ordinal) || label.StartsWith("Parallax", StringComparison.Ordinal)) &&
              prefs.Particles == ParticleAmount.Off && !prefs.WeatherEffects && !prefs.FlashyEffects &&
              after.Contains("Particles: Off") && after.Contains("Weather effects: Off") && after.Contains("Flashing effects: Off"),
            "PU-26: Video has Particles, Weather effects and Flashing effects rows that change their preferences (Background and Parallax stay removed, as 0.1.3 chose)");

        var reloaded = new PreferencesStore<GamePreferences>(Path.Combine(configRoot, "config.json")).Load();
        reloaded.Normalize();
        check(reloaded.Particles == ParticleAmount.Off && !reloaded.WeatherEffects && !reloaded.FlashyEffects &&
              reloaded.BackgroundEnabled && reloaded.BackgroundParallax == 1f,
            "PU-26: the effect choices survive a restart (Normalize no longer forces them back on; background and parallax still are)");

        // The renderer reads each preference (play-test confirms what it looks like).
        var renderer = string.Concat(Directory.GetFiles(Path.Combine(RepositoryRoot(), "UI"), "GraphicalSandbox*.cs").Select(File.ReadAllText));
        var reads = new[] { "Preferences.Particles", "Preferences.WeatherEffects", "Preferences.FlashyEffects", "Preferences.ScreenShake",
                            "Preferences.Lighting", "MapDisplay.DrawsOverlay", "MapDisplay.DrawsMinimap", "MapDisplay.NpcHeads" };
        var missing = reads.Where(read => !renderer.Contains(read, StringComparison.Ordinal)).ToArray();
        check(missing.Length == 0, $"PU-26/PU-15/PE-16: the renderer reads every row's preference ({(missing.Length == 0 ? "all read" : "missing " + string.Join(", ", missing))})");

        flow.Handle(new UiInputFrame(default, BackPressed: true));
        Open(flow, "Interface");
        var shakeRow = Labels().Contains("Screen shake: On") && prefs.ScreenShake;
        Open(flow, "Screen shake:");
        check(shakeRow && !prefs.ScreenShake && Labels().Contains("Screen shake: Off") && new GamePreferences().ScreenShake,
            "PE-16: Interface has a Screen shake row, on by default, that turns it off");
    }

    // ---- D7 · PE-16: the shake model ----
    private static void CheckScreenShake(Action<bool, string> check)
    {
        var game = NewGame(9705);
        var shake = new ScreenShake();
        shake.Observe(game); // first look: a fresh world starts calm
        var center = game.Player.Motion.Position + game.Player.Motion.Size / 2f;
        game.SoundEvents.Emit(SoundEventKind.Explosion, string.Empty, center + new Vector2(3f, 0f), 4);
        shake.Observe(game);
        var near = shake.Trauma;
        var first = shake.Offset(1f / 60f, enabled: true);
        var frames = 0;
        while (shake.Trauma > 0f && frames < 600)
        {
            shake.Offset(1f / 60f, enabled: true);
            frames++;
        }
        var settled = shake.Offset(1f / 60f, enabled: true);
        check(near > 0.3f && first.Length() > 0.5f && first.Length() <= ScreenShake.MaxOffsetPixels && frames <= 60 && settled == Vector2.Zero,
            $"PE-16: a nearby explosion shakes the view ({first.Length():0.0} px) and the shake decays to nothing within a second ({frames} frames)");

        game.SoundEvents.Tick += 1;
        game.SoundEvents.Emit(SoundEventKind.Explosion, string.Empty, center + new Vector2(ScreenShake.ExplosionReach + 5f, 0f), 6);
        shake.Observe(game);
        var far = shake.Trauma;
        game.SoundEvents.Tick += 1;
        game.SoundEvents.Emit(SoundEventKind.Explosion, string.Empty, center, 4);
        shake.Observe(game);
        var disabled = shake.Offset(1f / 60f, enabled: false);
        check(far == 0f && disabled == Vector2.Zero && shake.Trauma == 0f,
            "PE-16: a distant explosion does not shake; with Screen shake off nothing moves and pending shake is dropped");

        game.DebugSummonKeyEnabled = true; // the challenge key is a debug key since 0.2.2 (BO-18)
        var summoned = game.TrySummonSentinel();
        shake.Observe(game);
        check(summoned && shake.Trauma >= ScreenShake.BossRoar - 0.001f, "PE-16: a boss arriving (its roar) shakes the view");
        check(ScreenShake.LandingImpulse(10f) == 0f && ScreenShake.LandingImpulse(ScreenShake.HeavyLandingTiles) > 0f &&
              ScreenShake.LandingImpulse(80f) <= 0.8f && ScreenShake.ExplosionImpulse(0f, 30) <= 0.9f,
            "PE-16: only heavy landings (25+ tiles, where fall damage starts) shake; impulses are capped");
        var fresh = new ScreenShake();
        fresh.AddImpulse(5f);
        check(fresh.Trauma == 1f, "PE-16: stacked impulses cap at full strength");
    }

    // ---- E1 · PE-15 ----
    private static void CheckAudio(Action<bool, string> check)
    {
        check(AudioEngine.BufferFrames == 1024 && AudioEngine.BufferCount == 3 && AudioEngine.LatencyMilliseconds <= 80.0,
            $"PE-15: 3 × 1,024-frame buffers: at most {AudioEngine.LatencyMilliseconds:0} ms from Play to the speaker (was 186 ms)");

        var random = new Random(3);
        var rates = Enumerable.Range(0, 2000).Select(_ => VoicePolicy.PitchJitter(random)).ToArray();
        check(VoicePolicy.VariesPitch("hit") && VoicePolicy.VariesPitch("dig") && VoicePolicy.VariesPitch("swing") &&
              !VoicePolicy.VariesPitch("click") && !VoicePolicy.VariesPitch("boss") &&
              rates.All(rate => rate >= 0.95 && rate <= 1.05) && rates.Min() < 0.96 && rates.Max() > 1.04,
            $"PE-15: repeated effects (hit, dig, swing) vary ±5% in pitch ({rates.Min():0.000}-{rates.Max():0.000}); menu clicks and stings do not");

        var source = Enumerable.Range(0, 1000).Select(i => (float)i).ToArray();
        var left = new float[100];
        var right = new float[100];
        var straight = AudioEngine.MixVoice(source, 0, 1.0, left, right, 100, 1f, 0.5f);
        var copied = left[99] == 99f && right[50] == 25f;
        Array.Clear(left);
        var raised = AudioEngine.MixVoice(source, 0, 1.05, left, right, 100, 1f, 1f);
        Array.Clear(left);
        var tail = AudioEngine.MixVoice(source, 990, 1.05, left, right, 100, 1f, 1f);
        check(straight == 100 && copied && Math.Abs(raised - 105.0) < 1e-6 && MathF.Abs(left[0] - 990f) < 1e-3f && tail >= source.Length && left[20] == 0f,
            "PE-15: a voice resampled 5% higher reads 105 source samples per 100 output samples (interpolated) and stops at its end");

        var bossRoar = SoundCatalog.BossCue(EnemyKind.StoneSentinel, "roar");
        check(VoicePolicy.Priority(bossRoar) == 4 && VoicePolicy.Priority("click") == 3 && VoicePolicy.Priority("dig") == 1 &&
              VoicePolicy.Priority("hit") < VoicePolicy.Priority("hurt"),
            "PE-15: voice priorities: boss and event stings over menu clicks and the player's hurt, over digs and hits");
        var voices = new (int Priority, double Remaining)[] { (4, 100), (1, 5000), (2, 10), (1, 300), (3, 1) };
        check(VoicePolicy.StealIndex(voices, 1) == 3 && VoicePolicy.StealIndex(voices, 4) == 3 &&
              VoicePolicy.StealIndex([(4, 10.0), (3, 5.0)], 1) == -1 && VoicePolicy.StealIndex([(4, 10.0), (3, 5.0)], 3) == 1,
            "PE-15: a full mixer steals the least important voice (nearest its end among equals), never one above the new sound's priority");
    }

    private static void Open(MenuFlow flow, string labelPrefix)
    {
        var index = flow.CurrentScreen.Entries.Select((entry, i) => (entry, i)).First(item => item.entry.Label.StartsWith(labelPrefix, StringComparison.Ordinal)).i;
        flow.CurrentScreen.SelectIndex(index);
        flow.Handle(new UiInputFrame(default, ConfirmPressed: true));
    }

    private static string RepositoryRoot([CallerFilePath] string here = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, ".."));
}
