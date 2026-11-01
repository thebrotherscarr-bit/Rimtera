using Rimtera.Entities;
using Rimtera.Systems;
using Rimtera.World;

namespace Rimtera.UI;

/// <summary>Icon for a generic list card; locked icons draw as a dark silhouette.</summary>
internal sealed record CardIcon(string Sprite, bool Locked = false);

public static partial class GraphicalSandbox
{
    /// <summary>Game screens opened from the world: storage, signs, the bestiary and achievements.</summary>
    private sealed partial class GameForm
    {
        private int _achievementsSeen = -1;
        private GameSimulation? _achievementGame;

        /// <summary>Player inventory beside a container, a chest or the Cargo Pod bank.</summary>
        private InventoryUiState? ShowStorage(Inventory? container, GridPoint? cell, string title, GuideLookupState? guide = null)
        {
            if (_game is null)
                return null;
            var game = _game;
            _down.Clear();
            _menuDown.Clear();
            var state = new InventoryUiState(game.Player.Inventory, chest: container, game: game, chestCell: cell, chestTitle: title,
                guide: guide, showAlmostCraftable: _menu.Preferences.CraftingShowsAlmost);
            state.AlmostCraftableChanged += show =>
            {
                _menu.Preferences.CraftingShowsAlmost = show;
                _menu.SavePreferences();
            };
            state.TipsRequested += () => OpenStationAiScreen(null);
            state.GuideRequested += item => OpenRecipeGuide(item);
            state.BankRequested += () =>
            {
                if (game.OpenBank() is { } bank)
                    ShowStorage(bank, null, "Cargo pod");
            };
            state.SettingsRequested += OpenPauseMenu;
            state.RenameRequested += chestCell => _menu.PromptForText("Rename chest", "A name for this chest (up to 20 characters).",
                game.GetChestName(chestCell) ?? string.Empty, value =>
                {
                    game.SetChestName(chestCell, value);
                    // Reopen the chest under its new name, as Terraria's rename returns to the open chest.
                    if (game.GetContainer(chestCell) is { } reopened)
                        ShowStorage(reopened, chestCell, game.GetChestName(chestCell) ?? "Chest");
                }, maxLength: GameSimulation.ChestNameMaxLength);
            state.CrewRequested += OpenColonistSheet;
            state.BestiaryRequested += OpenBestiary;
            state.FeatsRequested += OpenAchievements;
            state.Modifiers = () => ((ModifierKeys & Keys.Shift) != 0, (ModifierKeys & Keys.Control) != 0, (ModifierKeys & Keys.Alt) != 0);
            _menu.ShowInventory(state, onClose: () =>
            {
                game.CloseBank();
                if (cell is { } chest)
                    game.CloseContainer(chest); // the lid closes (PE-4 chest_close)
                CloseMenu();
            });
            _menuOpen = true;
            UpdateMenuViewport();
            SyncCanvas();
            return state;
        }

        /// <summary>Sign or tombstone text with an edit option (tombstones keep their epitaph).</summary>
        private void OpenSignScreen(GridPoint cell, string text)
        {
            if (_game is null)
                return;
            var game = _game;
            var entries = new List<MenuEntry>
            {
                new("Edit text", () => _menu.PromptForText("Edit sign", "What the sign says.", game.GetSignText(cell) ?? string.Empty, value =>
                {
                    game.SetSignText(cell, value);
                    OpenSignScreen(cell, string.IsNullOrWhiteSpace(value) ? "The sign is blank." : value);
                }, maxLength: 120)),
                new("Close", CloseMenu)
            };
            _down.Clear();
            _menu.ShowDirect(new MenuScreen("sign", "Sign", entries, "Enter  choose      Esc  close", body: text), CloseMenu);
            _menuOpen = true;
            UpdateMenuViewport();
            SyncCanvas();
        }

        /// <summary>Every creature kind: unknown until seen, details unlock with kills.</summary>
        private void OpenBestiary()
        {
            if (_game is null)
                return;
            var log = _game.Bestiary;
            var entries = new List<MenuEntry>();
            foreach (var kind in Enum.GetValues<EnemyKind>())
            {
                var entry = log.Get(kind);
                var level = entry?.UnlockLevel ?? 0;
                var sprite = SpriteCatalog.ForEnemy(kind);
                if (level == 0)
                {
                    entries.Add(new MenuEntry($"Unknown  •  Entry {(int)kind + 1}  •  Not met yet", () => { }, Tag: new CardIcon(sprite, Locked: true)));
                    continue;
                }
                var parts = new List<string> { entry!.DisplayName };
                parts.Add(level >= 2 ? $"{entry.Kills} defeated" : "Sighted");
                if (entry.Biomes.Count > 0)
                    parts.Add(string.Join(", ", entry.Biomes.Select(zone => SplitWords(zone.ToString()))));
                if (level >= 3 && entry.DropsSeen.Count > 0)
                    parts.Add("Drops: " + string.Join(", ", entry.DropsSeen.Take(4).Select(item => _game.Items[item].Name)));
                entries.Add(new MenuEntry(string.Join("  •  ", parts), () => { }, Tag: new CardIcon(sprite)));
            }
            ShowGameList("bestiary", "Bestiary", entries, $"{log.SeenCount} of {log.TotalKinds} creatures found  ·  {MathF.Round(log.Completion * 100f):0}% complete");
        }

        private void OpenAchievements()
        {
            if (_game is null)
                return;
            var tracker = _game.Achievements;
            var entries = AchievementCatalog.All
                .OrderBy(definition => tracker.IsUnlocked(definition.Id) ? 0 : 1)
                .Select(definition =>
                {
                    var unlocked = tracker.IsUnlocked(definition.Id);
                    var when = unlocked && tracker.UnlockTimes.TryGetValue(definition.Id, out var time) ? $"Unlocked {time.LocalDateTime:d MMM yyyy}" : "Locked";
                    return new MenuEntry($"{definition.Name}  •  {definition.Description}  •  {definition.Category}  •  {when}", () => { },
                        Tag: new CardIcon(definition.IconName, Locked: !unlocked));
                })
                .ToList();
            ShowGameList("achievements", "Feats", entries, $"{tracker.UnlockedCount} of {tracker.TotalCount} unlocked");
        }

        private void ShowGameList(string id, string title, List<MenuEntry> entries, string summary)
        {
            entries.Add(new MenuEntry("Close", CloseMenu));
            _down.Clear();
            _menuDown.Clear();
            _menu.ShowDirect(new MenuScreen(id, title, entries, "↑ ↓  move      Esc  close", MenuLayout.Cards, body: summary), CloseMenu);
            _menuOpen = true;
            UpdateMenuViewport();
            SyncCanvas();
        }

        /// <summary>
        /// The end of a run: why it ended and its chronicle, newest last, then back to the title. The world is saved with
        /// its outcome, so loading it again shows this screen rather than playing on.
        /// </summary>
        private void ShowRunEnd(GameSimulation game)
        {
            var title = game.Outcome == RunOutcome.Won ? "Escaped" : "The colony has fallen";
            var entries = new List<MenuEntry> { new("— The story of this run —", Enabled: false) };
            entries.AddRange(game.Chronicle.Select((entry, index) => new MenuEntry(
                $"Day {entry.Day}, {(int)entry.Hour:00}:{(int)((entry.Hour % 1f) * 60f):00}   ·   {entry.Text}", Enabled: false, Tag: index)));
            entries.Add(new MenuEntry("Return to title", ReturnFromRunEnd));
            _down.Clear();
            _menuDown.Clear();
            var screen = new MenuScreen("run-end", title, entries, "↑ ↓  read      Enter  return to title",
                body: game.OutcomeReason ?? "The run is over.");
            screen.SelectIndex(entries.Count - 1);
            _menu.ShowDirect(screen, ReturnFromRunEnd);
            _menuOpen = true;
            UpdateMenuViewport();
            SyncCanvas();
        }

        private void ReturnFromRunEnd()
        {
            TrySaveGame(out _);
            QuitToTitle(_game?.OutcomeReason);
        }

        /// <summary>"FrozenWasteland" → "Frozen wasteland".</summary>
        private static string SplitWords(string name) =>
            string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString()));

        /// <summary>Toast and chime for achievements unlocked since the last frame.</summary>
        private void AnnounceAchievements()
        {
            if (_game is null)
                return;
            var tracker = _game.Achievements;
            if (!ReferenceEquals(_achievementGame, _game))
            {
                _achievementGame = _game;
                _achievementsSeen = tracker.UnlockedCount;
                return;
            }
            if (tracker.UnlockedCount <= _achievementsSeen)
                return;
            foreach (var unlocked in tracker.RecentUnlocks.TakeLast(tracker.UnlockedCount - _achievementsSeen))
                _canvas.Notify($"Achievement unlocked: {unlocked.Name}");
            _audioDirector.Ui("event");
            _achievementsSeen = tracker.UnlockedCount;
        }
    }
}
