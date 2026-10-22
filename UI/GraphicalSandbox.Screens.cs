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
        private void ShowStorage(Inventory? container, GridPoint? cell, string title)
        {
            if (_game is null)
                return;
            var game = _game;
            _down.Clear();
            _menuDown.Clear();
            var state = new InventoryUiState(game.Player.Inventory, chest: container, game: game, chestCell: cell, chestTitle: title);
            state.BankRequested += () =>
            {
                if (game.OpenBank() is { } bank)
                    ShowStorage(bank, null, "Cargo pod");
            };
            state.SettingsRequested += OpenPauseMenu;
            _menu.ShowInventory(state, onClose: () =>
            {
                game.CloseBank();
                CloseMenu();
            });
            _menuOpen = true;
            UpdateMenuViewport();
            SyncCanvas();
        }

        /// <summary>Sign or tombstone text with an edit option (tombstones keep their epitaph).</summary>
        private void OpenSignScreen(GridPoint cell, string text)
        {
            if (_game is null)
                return;
            var game = _game;
            var entries = new List<MenuEntry>
            {
                new(text, () => { }),
                new("Edit text", () => _menu.PromptForText("Edit sign", "What the sign says.", game.GetSignText(cell) ?? string.Empty, value =>
                {
                    game.SetSignText(cell, value);
                    OpenSignScreen(cell, string.IsNullOrWhiteSpace(value) ? "The sign is blank." : value);
                }, maxLength: 120)),
                new("Close", CloseMenu)
            };
            _down.Clear();
            _menu.ShowDirect(new MenuScreen("sign", "Sign", entries), CloseMenu);
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
                    entries.Add(new MenuEntry($"???  •  Entry {(int)kind + 1}: not yet encountered", () => { }, Tag: new CardIcon(sprite, Locked: true)));
                    continue;
                }
                var parts = new List<string> { entry!.DisplayName };
                parts.Add(level >= 2 ? $"{entry.Kills} defeated" : "Sighted");
                if (entry.Biomes.Count > 0)
                    parts.Add(string.Join(", ", entry.Biomes.Select(zone => zone.ToString())));
                if (level >= 3 && entry.DropsSeen.Count > 0)
                    parts.Add("Drops: " + string.Join(", ", entry.DropsSeen.Take(4).Select(item => _game.Items[item].Name)));
                entries.Add(new MenuEntry(string.Join("  •  ", parts), () => { }, Tag: new CardIcon(sprite)));
            }
            ShowGameList("bestiary", $"Bestiary  ·  {log.SeenCount} of {log.TotalKinds} found  ·  {log.Completion:P0} complete", entries);
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
            ShowGameList("achievements", $"Achievements  ·  {tracker.UnlockedCount} / {tracker.TotalCount}", entries);
        }

        private void ShowGameList(string id, string title, List<MenuEntry> entries)
        {
            entries.Add(new MenuEntry("Close", CloseMenu));
            _down.Clear();
            _menuDown.Clear();
            _menu.ShowDirect(new MenuScreen(id, title, entries, layout: MenuLayout.Cards), CloseMenu);
            _menuOpen = true;
            UpdateMenuViewport();
            SyncCanvas();
        }

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
