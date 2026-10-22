using Rimtera.Characters;
using Rimtera.Systems;

namespace Rimtera.UI;

public sealed record MenuEntry(
    string Label,
    Action? Activate = null,
    Action<int>? Adjust = null,
    bool Enabled = true,
    string? SnapLabel = null,
    object? Tag = null,
    IReadOnlyList<MenuRowAction>? RowActions = null,
    string? Description = null);

/// <summary>A small button on a list card (favourite, rename, delete).</summary>
public sealed record MenuRowAction(string Label, Action Activate);

public enum MenuLayout : byte
{
    List,
    Cards
}

/// <summary>What the renderer shows beside a creation screen.</summary>
public enum MenuPreview : byte
{
    None,
    Character,
    World
}

/// <summary>A focused, keyboard/gamepad navigable state with a retained widget tree.</summary>
public sealed class MenuScreen : UiState
{
    private readonly List<MenuEntry> _entries;
    private readonly List<UiButtonElement> _buttons = new();
    private readonly List<int> _buttonEntryIndices = new();
    private readonly UiPanelElement _menuPanel;

    public MenuScreen(string id, string title, IEnumerable<MenuEntry> entries, string? footer = null,
        MenuLayout layout = MenuLayout.List, MenuPreview preview = MenuPreview.None)
        : base(id, style: new UiStyle { Width = UiLength.Pct(1f), Height = UiLength.Pct(1f) })
    {
        Title = title;
        Footer = footer ?? "↑/↓ navigate   Mouse click   Enter select   Esc back";
        ListLayout = layout;
        Preview = preview;
        _entries = entries.ToList();
        // Creation screens move the list right so the renderer can draw a live preview on the left.
        _menuPanel = new UiPanelElement($"{id}-panel", new UiStyle
        {
            Width = UiLength.Pct(preview == MenuPreview.None ? 0.8f : 0.52f), Height = UiLength.Mixed(-220f, 1f),
            MaxWidth = 650f, MaxHeight = 560f, MinHeight = 456f,
            Top = UiLength.Px(0f), HAlign = preview == MenuPreview.None ? 0.5f : 0.86f, VAlign = 0.5f,
            Padding = UiThickness.Uniform(18f), OverflowHidden = true
        });
        _menuPanel.OnScroll((_, delta) => MoveSelection(delta < 0 ? 1 : -1));
        Add(_menuPanel);
        RebuildWidgets();
    }

    public string Title { get; }
    public string Footer { get; }
    public MenuLayout ListLayout { get; }
    public MenuPreview Preview { get; }
    public MenuEntry? SelectedEntry => (uint)SelectedIndex < (uint)_entries.Count ? _entries[SelectedIndex] : null;
    public string StatusMessage { get; set; } = string.Empty;
    public IReadOnlyList<MenuEntry> Entries => _entries;
    public int SelectedIndex { get; private set; } = -1;

    public void ReplaceEntries(IEnumerable<MenuEntry> entries)
    {
        _entries.Clear();
        _entries.AddRange(entries);
        SelectedIndex = FirstEnabled();
        RebuildWidgets();
    }

    public void SetStatus(string status)
    {
        StatusMessage = status;
        RebuildWidgets();
    }

    public void MoveSelection(int direction)
    {
        if (direction == 0 || _entries.Count == 0)
            return;
        var current = SelectedIndex;
        for (var attempt = 0; attempt < _entries.Count; attempt++)
        {
            current = (current + Math.Sign(direction) + _entries.Count) % _entries.Count;
            if (!_entries[current].Enabled)
                continue;
            SelectedIndex = current;
            RebuildWidgets();
            return;
        }
    }

    public void FocusSelection(UserInterface ui)
    {
        var localIndex = _buttonEntryIndices.IndexOf(SelectedIndex);
        if (localIndex >= 0)
            ui.Focus(_buttons[localIndex]);
    }

    public void SelectIndex(int index)
    {
        if ((uint)index >= (uint)_entries.Count || !_entries[index].Enabled)
            return;
        SelectedIndex = index;
        RebuildWidgets();
    }

    public void AdjustSelection(int amount)
    {
        if ((uint)SelectedIndex >= (uint)_entries.Count || amount == 0)
            return;
        _entries[SelectedIndex].Adjust?.Invoke(Math.Sign(amount));
    }

    public void ActivateSelection()
    {
        ActivateEntry(SelectedIndex);
    }

    private void ActivateEntry(int index)
    {
        if ((uint)index >= (uint)_entries.Count || !_entries[index].Enabled)
            return;
        SelectedIndex = index;
        if (_entries[index].Activate is { } activate)
            activate();
        else if (_entries[index].Adjust is { } adjust)
            adjust(1);
    }

    public override void OnEnter()
    {
        if (SelectedIndex < 0)
            SelectedIndex = FirstEnabled();
        RebuildWidgets();
    }

    private int FirstEnabled() => _entries.FindIndex(entry => entry.Enabled);

    private void RebuildWidgets()
    {
        _menuPanel.ClearChildren();
        _buttons.Clear();
        _buttonEntryIndices.Clear();
        _menuPanel.Add(new UiTextElement(Title, $"{Id}-title", new UiStyle
        {
            Width = UiLength.Pct(1f), Height = UiLength.Px(42f), Top = UiLength.Px(8f), HAlign = 0.5f
        }) { Scale = 0.8f, Color = UiColor.HeaderBlue });

        // Cards are taller (portrait + details) so fewer fit; plain rows keep the original geometry.
        var cards = ListLayout == MenuLayout.Cards;
        var visibleRows = cards ? 5 : 8;
        var firstVisible = Math.Clamp(SelectedIndex - visibleRows + 1, 0, Math.Max(0, _entries.Count - visibleRows));
        var lastVisible = Math.Min(_entries.Count, firstVisible + visibleRows);
        var top = 58f;
        for (var index = firstVisible; index < lastVisible; index++)
        {
            var entry = _entries[index];
            var entryIndex = index;
            var isCard = cards && entry.Tag is not null;
            var height = isCard ? 64f : 36f;
            var hasActions = isCard && entry.RowActions is { Count: > 0 };
            var button = new UiButtonElement(entry.Label, () => ActivateEntry(entryIndex), $"{Id}-entry-{index}", new UiStyle
            {
                Width = UiLength.Pct(hasActions ? 0.74f : isCard ? 0.92f : 0.72f), Height = UiLength.Px(height),
                Top = UiLength.Px(top), HAlign = hasActions ? 0.04f : 0.5f,
                Padding = new UiThickness(10f, 5f, 10f, 5f)
            })
            {
                IsEnabled = entry.Enabled,
                GamepadSnapLabel = entry.SnapLabel,
                Color = entry.Enabled ? UiColor.ButtonDefault : new UiColor(50, 50, 58)
            };
            _buttons.Add(button);
            _buttonEntryIndices.Add(index);
            _menuPanel.Add(button);

            if (hasActions)
            {
                for (var action = 0; action < entry.RowActions!.Count; action++)
                {
                    var rowAction = entry.RowActions[action];
                    _menuPanel.Add(new UiButtonElement(rowAction.Label, rowAction.Activate, $"{Id}-entry-{index}-action-{action}", new UiStyle
                    {
                        Width = UiLength.Px(84f), Height = UiLength.Px(19f),
                        Top = UiLength.Px(top + action * 21.5f), HAlign = 0.97f
                    }) { Color = new UiColor(46, 52, 60) });
                }
            }
            top += height + 3f;
        }

        if (SelectedEntry?.Description is { Length: > 0 } description)
        {
            _menuPanel.Add(new UiTextElement(description, $"{Id}-description", new UiStyle
            {
                Width = UiLength.Pct(0.92f), Height = UiLength.Px(26f),
                Top = UiLength.Mixed(-64f, 1f), HAlign = 0.5f
            }) { Scale = 0.75f, Color = new UiColor(242, 176, 64) });
        }

        var footerText = _entries.Count > visibleRows ? $"{Footer}  •  ↑/↓ scroll" : Footer;
        _menuPanel.Add(new UiTextElement(string.IsNullOrWhiteSpace(StatusMessage) ? footerText : $"{footerText}  •  {StatusMessage}",
            $"{Id}-footer", new UiStyle
            {
                Width = UiLength.Pct(1f), Height = UiLength.Px(28f),
                Top = UiLength.Mixed(-32f, 1f), HAlign = 0.5f
            }) { Scale = 0.75f });
    }
}

/// <summary>Application-level menu and screen navigation, profile selection, settings, and launch requests.</summary>
public sealed class MenuFlow
{
    private readonly UserProfileStore _profiles;
    private readonly WorldSaveRepository _saves;
    private readonly PreferencesStore<GamePreferences> _preferencesStore;
    private readonly PreferencesStore<InputProfile> _inputStore;
    private CharacterProfile? _selectedCharacter;
    private Action? _pauseResume;
    private Action? _directUiClose;
    private static readonly (string Label, int Width, int Height)[] WorldSizes =
    [
        ("Tiny", 1750, 900),
        ("Small", 4200, 1200),
        ("Medium", 6400, 1800),
        ("Large", 8400, 2400)
    ];

    private CharacterDraft _characterDraft = CharacterDraft.CreateRandom(48271, "Adventurer");
    private string _worldDraft = "New World";
    private int _worldSeed = 48271;
    private int _worldSizeIndex = 1; // Small
    private WorldDifficulty _worldDifficulty = WorldDifficulty.Classic;
    private WorldHazard _worldHazard = WorldHazard.Random;
    public WorldDifficulty WorldDifficultyDraft => _worldDifficulty;
    public WorldHazard WorldHazardDraft => _worldHazard;
    private WorldBlight _worldBlight = WorldBlight.Random;
    public WorldBlight WorldBlightDraft => _worldBlight;
    public (string Label, int Width, int Height) WorldSizeDraft => WorldSizes[_worldSizeIndex];

    /// <summary>
    /// Rough memory while playing a world of this size: about 27 bytes per cell (cells, regions, liquids, map) plus
    /// a fixed ~150 MB for the game. Measured: Large 8400×2400 runs at about 700 MB.
    /// </summary>
    public static int ApproximateMemoryMb(int width, int height) => (int)Math.Round((150 + width * (long)height * 27 / 1_000_000.0) / 10) * 10;

    public MenuFlow(UserProfileStore? profiles = null, string? dataDirectory = null)
    {
        var root = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rimtera");
        _profiles = profiles ?? new UserProfileStore(Path.Combine(root, "profiles.json"));
        _saves = new WorldSaveRepository(Path.Combine(root, "saves"));
        _preferencesStore = new PreferencesStore<GamePreferences>(Path.Combine(root, "config.json"));
        _inputStore = new PreferencesStore<InputProfile>(Path.Combine(root, "input profiles.json"));
        Preferences = _preferencesStore.Load();
        Preferences.Normalize();
        InputProfile = _inputStore.Load();
        if (InputProfile.Bindings is not { Count: > 0 })
            InputProfile = Rimtera.UI.InputProfile.CreateDefault();
        InputProfile.Normalize();
        Ui = new UserInterface();
        Ui.SetViewport(1280, 800);
    }

    public UserInterface Ui { get; }
    public GamePreferences Preferences { get; }
    public InputProfile InputProfile { get; private set; }
    public UserProfileStore Profiles => _profiles;
    public WorldSaveRepository Saves => _saves;
    public MenuScreen CurrentScreen => Ui.CurrentState as MenuScreen ?? throw new InvalidOperationException("The current UI state is not a menu screen.");
    public CharacterProfile? SelectedCharacter => _selectedCharacter;
    public CharacterDraft CharacterDraft => _characterDraft;
    public string WorldDraft => _worldDraft;
    public int WorldSeed => _worldSeed;

    public event Action? ExitRequested;
    public event Action<WorldProfile, CharacterProfile>? WorldLaunchRequested;
    public event Action? DisplayPreferencesChanged;

    public bool IsOpen => Ui.CurrentState is not null;

    public void Open() => ResetTo(CreateMainMenu());

    /// <summary>Closes the retained menu without discarding the flow or its history.</summary>
    public void Close()
    {
        if (Ui.CurrentState is not null)
            Ui.SetState(null, saveCurrent: false);
        Ui.ClearHistory();
    }

    /// <summary>Reports a transient result on the active menu screen.</summary>
    public void SetStatus(string status)
    {
        if (Ui.CurrentState is MenuScreen screen)
        {
            screen.SetStatus(status);
            screen.FocusSelection(Ui);
        }
    }

    public bool IsTextPromptActive => Ui.CurrentState is TextPromptState;
    public bool IsBindingCaptureActive => Ui.CurrentState is BindingCaptureState;

    public void InsertText(string text)
    {
        if (Ui.CurrentState is TextPromptState prompt)
            prompt.Insert(text);
    }

    public void EditText(TextEditCommand command)
    {
        if (Ui.CurrentState is TextPromptState prompt)
            prompt.ApplyEdit(command);
    }

    public void SubmitTextPrompt()
    {
        if (Ui.CurrentState is TextPromptState prompt)
            prompt.Submit();
    }

    public void CancelTextPrompt()
    {
        if (Ui.CurrentState is TextPromptState prompt)
            prompt.Cancel();
    }

    public void CaptureBinding(ConsoleKey key) => CaptureBinding(key.ToString());

    /// <summary>Finishes key capture with a binding name (a ConsoleKey name, or Shift / Ctrl / Alt).</summary>
    public void CaptureBinding(string key)
    {
        if (Ui.CurrentState is BindingCaptureState capture)
            capture.Capture(key);
    }

    public void CancelBindingCapture()
    {
        if (Ui.CurrentState is BindingCaptureState capture)
            capture.Cancel();
    }

    public void Handle(UiInputFrame input)
    {
        if (Ui.CurrentState is TextPromptState prompt)
        {
            if (input.BackPressed) { prompt.Cancel(); return; }
            if (input.ConfirmPressed) { prompt.Submit(); return; }
            Ui.Update(input with
            {
                ConfirmPressed = false,
                BackPressed = false,
                NavigationX = 0,
                NavigationY = 0
            });
            return;
        }
        if (Ui.CurrentState is BindingCaptureState capture)
        {
            if (input.BackPressed) { capture.Cancel(); return; }
            Ui.Update(input with
            {
                ConfirmPressed = false,
                BackPressed = false,
                NavigationX = 0,
                NavigationY = 0
            });
            return;
        }
        if (input.BackPressed)
        {
            if (Ui.CurrentState is not null && _directUiClose is not null)
            {
                var close = _directUiClose;
                _directUiClose = null;
                Ui.SetState(null, saveCurrent: false);
                close();
            }
            else if (Ui.CurrentState?.Id == "pause-menu") _pauseResume?.Invoke();
            else if (Ui.CurrentState is not null && Ui.CurrentState.Id != "main-menu") GoBack();
            else ExitRequested?.Invoke();
            return;
        }
        if (Ui.CurrentState is MenuScreen screen)
        {
            if (input.NavigationY != 0)
            {
                screen.MoveSelection(input.NavigationY);
                screen.FocusSelection(Ui);
            }
            if (input.NavigationX != 0) screen.AdjustSelection(input.NavigationX);
            if (input.ConfirmPressed) screen.ActivateSelection();
            Ui.Update(input with
            {
                ConfirmPressed = false,
                BackPressed = false,
                NavigationX = 0,
                NavigationY = 0
            });
            return;
        }

        Ui.Update(input);
    }

    public void GoBack()
    {
        if (!Ui.GoBack())
            ResetTo(CreateMainMenu());
    }

    public void SetCharacterName(string name)
    {
        _characterDraft.Name = NormalizeDraft(name, "Adventurer");
        if (Ui.CurrentState?.Id == "character-create") ReplaceCurrent(CreateCharacterCreationMenu());
    }

    public void SetWorldName(string name)
    {
        _worldDraft = NormalizeDraft(name, "New World");
        if (Ui.CurrentState?.Id == "world-create") ReplaceCurrent(CreateWorldCreationMenu());
    }
    public void SetWorldSeed(int seed)
    {
        _worldSeed = seed;
        if (Ui.CurrentState?.Id == "world-create")
            ReplaceCurrent(CreateWorldCreationMenu());
    }

    private void PromptForCharacterName()
    {
        var prompt = new TextPromptState(
            "Character name",
            "Shown on the colonist sheet, save list, and in-world label.",
            _characterDraft.Name,
            "Enter confirms  ·  Esc cancels  ·  32 characters",
            32);
        prompt.Accepted += value =>
        {
            if (Ui.CurrentState == prompt)
                Ui.GoBack();
            SetCharacterName(value);
        };
        prompt.Cancelled += () =>
        {
            if (Ui.CurrentState == prompt)
                Ui.GoBack();
        };
        Ui.SetState(prompt);
    }

    private void PromptForWorldName()
    {
        var prompt = new TextPromptState(
            "World name",
            "Shown on the load list and in this world's save envelope.",
            _worldDraft,
            "Enter confirms  ·  Esc cancels  ·  32 characters",
            32);
        prompt.Accepted += value =>
        {
            if (Ui.CurrentState == prompt)
                Ui.GoBack();
            SetWorldName(value);
        };
        prompt.Cancelled += () =>
        {
            if (Ui.CurrentState == prompt)
                Ui.GoBack();
        };
        Ui.SetState(prompt);
    }

    private void PromptForBinding(string action)
    {
        var label = ControlCatalog.Find(action)?.Label ?? action;
        var current = InputProfile.KeyLabel(action);
        var capture = new BindingCaptureState(label, current.Length > 0 ? current : "nothing");
        capture.Captured += key =>
        {
            if (Ui.CurrentState == capture)
                Ui.GoBack();
            SetBinding(action, key);
        };
        capture.Cancelled += () =>
        {
            if (Ui.CurrentState == capture)
                Ui.GoBack();
        };
        Ui.SetState(capture);
    }

    /// <summary>
    /// Makes <paramref name="key"/> the action's main key (alternates stay). A key taken from another action is
    /// moved, and the screen says which; menu navigation keeps to the arrow keys.
    /// </summary>
    public void SetBinding(string action, string key)
    {
        if (string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(key))
            return;
        if (!InputBindingResolver.TryParseKeyCode(key, out var code) || InputBindingResolver.NameForKeyCode(code) is not { } name)
        {
            if (Ui.CurrentState is MenuScreen screen)
            {
                screen.SetStatus("That key can't be bound.");
                screen.FocusSelection(Ui);
            }
            return;
        }
        if (action.StartsWith("Menu", StringComparison.OrdinalIgnoreCase) && name is "W" or "A" or "S" or "D")
        {
            if (Ui.CurrentState is MenuScreen screen)
            {
                screen.SetStatus("Menu navigation uses the arrow keys; WASD is reserved for gameplay.");
                screen.FocusSelection(Ui);
            }
            return;
        }
        var displaced = InputProfile.Rebind(action, name);
        SavePreferences();
        var label = ControlCatalog.Find(action)?.Label ?? action;
        var status = $"{label}: {InputBindingResolver.Describe(name)}.";
        if (displaced is not null)
            status += $" {InputBindingResolver.Describe(name)} was on {ControlCatalog.Find(displaced)?.Label ?? displaced}, which is now {(InputProfile.KeyLabel(displaced) is { Length: > 0 } rest ? rest : "unbound")}.";
        ReplaceCurrent(CreateControlsMenu(action));
        CurrentScreen.SetStatus(status);
    }

    public void SavePreferences()
    {
        Preferences.Normalize();
        InputProfile.Normalize();
        _preferencesStore.Save(Preferences);
        _inputStore.Save(InputProfile);
    }

    /// <summary>
    /// The in-game Settings menu, Terraria's: Close Menu, the settings categories, Feats, and the save-and-exit
    /// choices. The graphical game opens it from the inventory's Settings button; Inventory and Crew rows appear only
    /// for hosts without that hub (the terminal).
    /// </summary>
    public void ShowPauseMenu(
        Action resume,
        Action? openInventory,
        Action saveAndExit,
        Action? save = null,
        Action? quitToTitle = null,
        Action? exitGame = null,
        Action? openColonist = null,
        Action? openCreative = null,
        Action? openFeats = null)
    {
        _pauseResume = resume;
        _directUiClose = null;

        var entries = new List<MenuEntry> { new("Close Menu", resume) };
        if (openInventory is not null)
            entries.Add(new MenuEntry("Inventory", openInventory));
        if (openColonist is not null)
            entries.Add(new MenuEntry("Crew", openColonist));
        if (openCreative is not null)
            entries.Add(new MenuEntry("Creative Powers", openCreative));
        if (save is not null)
            entries.Add(new MenuEntry("Save Game", save));
        entries.AddRange(SettingsCategoryEntries());
        if (openFeats is not null)
            entries.Add(new MenuEntry("Feats", openFeats));
        if (quitToTitle is not null)
            entries.Add(new MenuEntry("Save & Exit to Main Menu", quitToTitle));
        else
            entries.Add(new MenuEntry("Save & Exit", saveAndExit));
        if (exitGame is not null)
            entries.Add(new MenuEntry("Save & Exit Game", exitGame));

        var screen = new MenuScreen("pause-menu", "Settings", entries, "Esc closes  ·  the world waits while this is open");
        Ui.ClearHistory();
        Ui.SetState(screen, saveCurrent: false);
        screen.FocusSelection(Ui);
    }

    /// <summary>Terraria's settings categories, each opening its page (Controls opens the key bindings).</summary>
    private IEnumerable<MenuEntry> SettingsCategoryEntries() => Enum.GetValues<OptionsCategory>().Select(category =>
        new MenuEntry(category.ToString(), () => Navigate(category == OptionsCategory.Controls ? CreateControlsMenu() : CreateOptionsMenu(category)),
            Description: category switch
            {
                OptionsCategory.General => "Autosave, autopause, item text, damage numbers, minimap, building grid.",
                OptionsCategory.Interface => "UI scale, zoom, health bars, name tags, status panel, frame rate.",
                OptionsCategory.Video => "Window mode and size, lighting, frame pacing, background and effects.",
                OptionsCategory.Volume => "Music, sound and ambient volume.",
                OptionsCategory.Cursor => "Cursor colour, border and size; smart cursor toggle or hold.",
                _ => "Every key, rebindable; Terraria's layout by default."
            }));

    /// <summary>The title screen's Settings: the same categories, then Back.</summary>
    private MenuScreen CreateSettingsMenu() =>
        new("settings", "Settings", SettingsCategoryEntries().Append(new MenuEntry("Back", GoBack)), "Choose a category");

    /// <summary>Shows a screen over the game (NPC talk, shop); Back closes it via <paramref name="onClose"/>.</summary>
    public void ShowDirect(MenuScreen screen, Action onClose)
    {
        _directUiClose = onClose;
        Ui.ClearHistory();
        Ui.SetState(screen, saveCurrent: false);
        screen.FocusSelection(Ui);
    }

    public void ShowInventory(InventoryUiState inventory, Action? onClose = null)
    {
        _directUiClose = onClose;
        if (onClose is null)
            Navigate(inventory);
        else
        {
            Ui.ClearHistory();
            Ui.SetState(inventory, saveCurrent: false);
        }
    }

    public void ShowColonistSheet(CharacterProfile profile, GameSimulation? game = null, Action? onClose = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (onClose is null)
        {
            _directUiClose = null;
            Navigate(new ColonistSheetState(profile, game, GoBack));
            return;
        }

        _directUiClose = onClose;
        var state = new ColonistSheetState(profile, game, () =>
        {
            var close = _directUiClose;
            _directUiClose = null;
            Ui.SetState(null, saveCurrent: false);
            close?.Invoke();
        });
        Ui.ClearHistory();
        Ui.SetState(state, saveCurrent: false);
    }

    /// <summary>Terraria's title menu: Single Player (characters, then worlds), Settings, Exit.</summary>
    private MenuScreen CreateMainMenu()
    {
        var hasSave = _profiles.Worlds.Any(world => _saves.Exists(world.Id));
        return new MenuScreen("main-menu", "RIMTERA", new[]
        {
            new MenuEntry("Single Player", () => Navigate(CreateCharacterSelectMenu())),
            new MenuEntry("Settings", () => Navigate(CreateSettingsMenu())),
            new MenuEntry("Exit Game", () => ExitRequested?.Invoke())
        }, hasSave ? "Single Player lists your characters, then your worlds" : "Create a character and world to begin");
    }

    /// <summary>Favourite / rename / delete buttons for the world list.</summary>
    private IReadOnlyList<MenuRowAction> WorldRowActions(WorldProfile world, Func<MenuScreen> rebuild) =>
    [
        new(world.Favorite ? "Unfavorite" : "Favorite", () =>
        {
            _profiles.ToggleWorldFavorite(world.Id);
            ReplaceCurrent(rebuild());
        }),
        new("Rename", () => PromptForText("Rename world", "A new name for this world.", world.Name, value =>
        {
            try { _profiles.RenameWorld(world.Id, value); ReplaceCurrent(rebuild()); }
            catch (InvalidOperationException ex) { ReplaceCurrentWithStatus(rebuild(), ex.Message); }
        })),
        new("Delete", () => ConfirmDelete($"Delete {world.Name}?",
            "The world leaves the list; its save file is moved to the saves 'deleted' folder.",
            () =>
            {
                _saves.Delete(world.Id);
                _profiles.DeleteWorld(world.Id);
                ReplaceCurrentWithStatus(rebuild(), $"Deleted {world.Name}.");
            }))
    ];

    private IReadOnlyList<MenuRowAction> CharacterRowActions(CharacterProfile character) =>
    [
        new(character.Favorite ? "Unfavorite" : "Favorite", () =>
        {
            _profiles.ToggleCharacterFavorite(character.Id);
            ReplaceCurrent(CreateCharacterSelectMenu());
        }),
        new("Rename", () => PromptForText("Rename character", "A new name for this colonist.", character.Name, value =>
        {
            try { _profiles.RenameCharacter(character.Id, value); ReplaceCurrent(CreateCharacterSelectMenu()); }
            catch (InvalidOperationException ex) { ReplaceCurrentWithStatus(CreateCharacterSelectMenu(), ex.Message); }
        })),
        new("Delete", () => ConfirmDelete($"Delete {character.Name}?",
            "This colonist is removed from the character list. Worlds they played stay.",
            () =>
            {
                _profiles.DeleteCharacter(character.Id);
                if (_selectedCharacter?.Id == character.Id)
                    _selectedCharacter = null;
                ReplaceCurrentWithStatus(CreateCharacterSelectMenu(), $"Deleted {character.Name}.");
            }))
    ];

    /// <summary>A two-option confirmation; "Cancel" is first so Enter never deletes by accident.</summary>
    private void ConfirmDelete(string title, string detail, Action confirmed) =>
        Navigate(new MenuScreen("confirm-delete", title, new[]
        {
            new MenuEntry("Cancel", GoBack),
            new MenuEntry("Delete", () => { GoBack(); confirmed(); })
        }, detail));

    private MenuScreen CreateCharacterSelectMenu()
    {
        var entries = _profiles.Characters
            .OrderByDescending(character => character.Favorite)
            .Select(character => new MenuEntry(
                $"{character.Name}  •  {character.BackstoryName}  •  {character.Age}  •  {character.EffectiveMaximumHealth:0} HP" +
                $"  •  {WorldSettings.Name(character.Difficulty)}",
                () =>
                {
                    _selectedCharacter = character;
                    Navigate(CreateWorldSelectMenu());
                },
                SnapLabel: character.Name, Tag: character, RowActions: CharacterRowActions(character))).ToList();
        entries.Add(new MenuEntry("Create Character", () => Navigate(BeginCharacterCreation()), SnapLabel: "New"));
        entries.Add(new MenuEntry("Back", GoBack, SnapLabel: "Back"));
        return new MenuScreen("character-select", "Select Character", entries,
            "Choose a survivor or create a new one", MenuLayout.Cards);
    }

    private MenuScreen BeginCharacterCreation()
    {
        _characterDraft = CharacterDraft.CreateRandom(Random.Shared.Next(), "Adventurer");
        return CreateCharacterCreationMenu();
    }

    private MenuScreen CreateCharacterCreationMenu() => new("character-create", "Create Character", new[]
    {
        new MenuEntry($"Name: {_characterDraft.Name}", PromptForCharacterName,
            Description: "Shown above your colonist and in every save list."),
        new MenuEntry($"Difficulty: {WorldSettings.Name(_characterDraft.Difficulty)}",
            Adjust: amount =>
            {
                _characterDraft.Difficulty = Cycle(_characterDraft.Difficulty, amount);
                ReplaceCurrent(CreateCharacterCreationMenu());
            },
            Description: WorldSettings.Describe(_characterDraft.Difficulty)),
        new MenuEntry($"Gender: {_characterDraft.Gender}",
            Adjust: amount =>
            {
                _characterDraft.Gender = Cycle(_characterDraft.Gender, amount);
                ReplaceCurrent(CreateCharacterCreationMenu());
            }),
        new MenuEntry($"Age: {_characterDraft.Age}",
            Adjust: amount =>
            {
                _characterDraft.Age = Math.Clamp(_characterDraft.Age + Math.Sign(amount), 16, 90);
                ReplaceCurrent(CreateCharacterCreationMenu());
            }),
        new MenuEntry($"Appearance: {_characterDraft.Appearance.Summary}",
            () => Navigate(CreateAppearanceMenu())),
        new MenuEntry($"Backstory: {_characterDraft.BackstoryName}",
            () => Navigate(CreateBackstoryMenu())),
        new MenuEntry($"Traits ({_characterDraft.Traits.Count}/3): {_characterDraft.TraitSummary}",
            () => Navigate(CreateTraitsMenu())),
        new MenuEntry("Skills  •  levels, passions, and focus",
            () => Navigate(CreateSkillsMenu())),
        new MenuEntry($"Focus: {_characterDraft.FocusName}",
            Adjust: amount =>
            {
                var values = Enum.GetValues<ColonistSkill>();
                var index = Array.IndexOf(values, _characterDraft.Focus);
                _characterDraft.SetFocus(values[(index + Math.Sign(amount) + values.Length) % values.Length]);
                ReplaceCurrent(CreateCharacterCreationMenu());
            }),
        new MenuEntry("Reroll skills",
            () =>
            {
                _characterDraft.RerollSkills(Random.Shared.Next());
                ReplaceCurrentWithStatus(CreateCharacterCreationMenu(), "Skill rolls refreshed.");
            }),
        new MenuEntry("Randomize colonist",
            () =>
            {
                _characterDraft.Randomize(Random.Shared.Next(), _characterDraft.Name);
                ReplaceCurrentWithStatus(CreateCharacterCreationMenu(), "Colonist randomized.");
            }),
        new MenuEntry("View colonist sheet",
            () => ShowColonistSheet(_characterDraft.ToProfile(Guid.NewGuid(), DateTimeOffset.UtcNow))),
        new MenuEntry("Create", () =>
        {
            try
            {
                _selectedCharacter = _profiles.CreateCharacter(_characterDraft);
                Navigate(CreateWorldSelectMenu());
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            { ReplaceCurrentWithStatus(CreateCharacterCreationMenu(), ex.Message); }
        }),
        new MenuEntry("Back", GoBack)
    }, CharacterCreationFooter(), preview: MenuPreview.Character);

    private MenuScreen CreateAppearanceMenu() => new("character-appearance", "Appearance", new[]
    {
        new MenuEntry($"Skin: {ColonistAppearance.SkinToneNames[_characterDraft.Appearance.SkinTone]}",
            Adjust: amount =>
            {
                _characterDraft.Appearance = _characterDraft.Appearance with
                {
                    SkinTone = Wrap(_characterDraft.Appearance.SkinTone, amount, ColonistAppearance.SkinToneCount)
                };
                ReplaceCurrent(CreateAppearanceMenu());
            }),
        new MenuEntry($"Hair: {ColonistAppearance.HairStyleNames[_characterDraft.Appearance.HairStyle]}",
            Adjust: amount =>
            {
                _characterDraft.Appearance = _characterDraft.Appearance with
                {
                    HairStyle = Wrap(_characterDraft.Appearance.HairStyle, amount, ColonistAppearance.HairStyleCount)
                };
                ReplaceCurrent(CreateAppearanceMenu());
            }),
        new MenuEntry($"Hair color: {ColonistAppearance.HairColorNames[_characterDraft.Appearance.HairColor]}",
            Adjust: amount =>
            {
                _characterDraft.Appearance = _characterDraft.Appearance with
                {
                    HairColor = Wrap(_characterDraft.Appearance.HairColor, amount, ColonistAppearance.HairColorCount)
                };
                ReplaceCurrent(CreateAppearanceMenu());
            }),
        new MenuEntry($"Eyes: {ColonistAppearance.EyeColorNames[_characterDraft.Appearance.EyeColor]}",
            Adjust: amount =>
            {
                _characterDraft.Appearance = _characterDraft.Appearance with
                {
                    EyeColor = Wrap(_characterDraft.Appearance.EyeColor, amount, ColonistAppearance.EyeColorCount)
                };
                ReplaceCurrent(CreateAppearanceMenu());
            }),
        new MenuEntry($"Outfit: {ColonistAppearance.OutfitColorNames[_characterDraft.Appearance.OutfitColor]}",
            Adjust: amount =>
            {
                _characterDraft.Appearance = _characterDraft.Appearance with
                {
                    OutfitColor = Wrap(_characterDraft.Appearance.OutfitColor, amount, ColonistAppearance.OutfitColorCount)
                };
                ReplaceCurrent(CreateAppearanceMenu());
            }),
        new MenuEntry("Randomize appearance",
            () =>
            {
                _characterDraft.Appearance = RandomAppearance();
                ReplaceCurrent(CreateAppearanceMenu());
            }),
        new MenuEntry("Back", GoBack)
    }, "←/→ change   The preview updates as you choose.", preview: MenuPreview.Character);

    private MenuScreen CreateBackstoryMenu()
    {
        var entries = ColonistCatalog.Backstories.Select(backstory => new MenuEntry(
            $"{(_characterDraft.Backstory == backstory.Id ? "●" : "○")} {backstory.Name}  •  {BackstorySkills(backstory)}",
            () =>
            {
                _characterDraft.SetBackstory(backstory.Id);
                GoBack();
            },
            SnapLabel: backstory.Name)).ToList();
        entries.Add(new MenuEntry("Back", GoBack));
        return new MenuScreen("character-backstory", "Backstory", entries,
            ColonistCatalog.GetBackstory(_characterDraft.Backstory).Description);
    }

    private MenuScreen CreateTraitsMenu()
    {
        var entries = ColonistCatalog.Traits.Select(trait =>
        {
            var selected = _characterDraft.Traits.Contains(trait.Id);
            return new MenuEntry(
                $"{(selected ? "[x]" : "[ ]")} {trait.Name}  •  {TraitEffect(trait)}",
                () =>
                {
                    if (_characterDraft.ToggleTrait(trait.Id))
                        ReplaceCurrentWithStatus(CreateTraitsMenu(), $"Traits: {_characterDraft.TraitSummary}");
                    else
                        ReplaceCurrentWithStatus(CreateTraitsMenu(), "That trait conflicts with a selected trait or the limit of three.");
                },
                Enabled: selected || _characterDraft.CanSelectTrait(trait.Id),
                SnapLabel: trait.Name);
        }).ToList();
        entries.Add(new MenuEntry("Clear traits",
            () =>
            {
                _characterDraft.ClearTraits();
                ReplaceCurrentWithStatus(CreateTraitsMenu(), "Traits cleared.");
            }));
        entries.Add(new MenuEntry("Randomize traits",
            () =>
            {
                _characterDraft.RandomizeTraits(Random.Shared.Next());
                ReplaceCurrentWithStatus(CreateTraitsMenu(), $"Traits: {_characterDraft.TraitSummary}");
            }));
        entries.Add(new MenuEntry("Back", GoBack));
        return new MenuScreen("character-traits", "Traits", entries,
            $"Choose up to 3. Selected: {_characterDraft.TraitSummary}");
    }

    private MenuScreen CreateSkillsMenu()
    {
        var entries = Enum.GetValues<ColonistSkill>().Select(skill => new MenuEntry(
            $"{ColonistCatalog.PassionMarker(_characterDraft.Passions[(int)skill])} " +
            $"{ColonistCatalog.SkillName(skill)}: {_characterDraft.SkillLevels[(int)skill]}",
            () =>
            {
                _characterDraft.CyclePassion(skill);
                ReplaceCurrent(CreateSkillsMenu());
            },
            amount =>
            {
                _characterDraft.AdjustSkill(skill, amount);
                ReplaceCurrent(CreateSkillsMenu());
            },
            SnapLabel: ColonistCatalog.SkillName(skill))).ToList();
        entries.Add(new MenuEntry("Reroll skills",
            () =>
            {
                _characterDraft.RerollSkills(Random.Shared.Next());
                ReplaceCurrentWithStatus(CreateSkillsMenu(), "Skill rolls refreshed.");
            }));
        entries.Add(new MenuEntry("Back", GoBack));
        return new MenuScreen("character-skills", "Skills", entries,
            "←/→ level   Enter cycles passion   ▲▲ burning   ▲ interested");
    }

    private string CharacterCreationFooter()
    {
        var top = _characterDraft.Stats.SkillLevels
            .Select((level, index) => (Level: level, Skill: (ColonistSkill)index))
            .OrderByDescending(item => item.Level)
            .Take(3)
            .Select(item => $"{ColonistCatalog.SkillName(item.Skill)} {item.Level}");
        return $"{_characterDraft.BackstoryName}  •  {_characterDraft.TraitSummary}  •  {string.Join(" · ", top)}";
    }

    private static string BackstorySkills(BackstoryDefinition backstory) =>
        string.Join(" · ", backstory.SkillBonuses
            .Select((amount, index) => (Amount: amount, Skill: (ColonistSkill)index))
            .Where(item => item.Amount != 0)
            .OrderByDescending(item => item.Amount)
            .Take(3)
            .Select(item => $"{ColonistCatalog.SkillName(item.Skill)} +{item.Amount}"));

    private static string TraitEffect(ColonistTraitDefinition trait)
    {
        var parts = new List<string>();
        for (var i = 0; i < trait.SkillBonuses.Length; i++)
        {
            if (trait.SkillBonuses[i] != 0)
                parts.Add($"{ColonistCatalog.SkillName((ColonistSkill)i)} {trait.SkillBonuses[i]:+0;-0}");
        }
        if (trait.MaximumHealthBonus != 0) parts.Add($"HP {trait.MaximumHealthBonus:+0;-0}");
        if (Math.Abs(trait.WorkSpeed - 1f) > 0.001f) parts.Add($"work {trait.WorkSpeed - 1f:+0%;-0%}");
        if (Math.Abs(trait.MoveSpeed - 1f) > 0.001f) parts.Add($"move {trait.MoveSpeed - 1f:+0%;-0%}");
        if (Math.Abs(trait.MoodOffset) > 0.001f) parts.Add($"mood {trait.MoodOffset:+0%;-0%}");
        if (Math.Abs(trait.FoodConsumption - 1f) > 0.001f) parts.Add($"food {trait.FoodConsumption - 1f:+0%;-0%}");
        return parts.Count == 0 ? "no bonuses" : string.Join(", ", parts);
    }

    private static ColonistAppearance RandomAppearance() => new(
        Random.Shared.Next(ColonistAppearance.SkinToneCount),
        Random.Shared.Next(ColonistAppearance.HairStyleCount),
        Random.Shared.Next(ColonistAppearance.HairColorCount),
        Random.Shared.Next(ColonistAppearance.EyeColorCount),
        Random.Shared.Next(ColonistAppearance.OutfitColorCount));

    private static int Wrap(int value, int amount, int count) =>
        (value + Math.Sign(amount) + count) % count;

    private MenuScreen CreateWorldSelectMenu()
    {
        var entries = _profiles.Worlds
            .OrderByDescending(world => world.Favorite)
            .ThenByDescending(world => world.LastPlayed)
            .Select(world => new MenuEntry(
                $"{world.Name}  •  {world.Width}×{world.Height}  •  {WorldSettings.Name(world.Difficulty)}  •  " +
                $"{WorldSettings.Name(WorldSettings.Resolve(world.Blight, world.Seed))}  •  {(_saves.Exists(world.Id) ? "Saved" : "New")}",
                () => Launch(world), SnapLabel: world.Name, Tag: world,
                RowActions: WorldRowActions(world, CreateWorldSelectMenu))).ToList();
        entries.Add(new MenuEntry("Create World", () => Navigate(BeginWorldCreation()), SnapLabel: "New"));
        entries.Add(new MenuEntry("Back", GoBack, SnapLabel: "Back"));
        return new MenuScreen("world-select", "Select World", entries,
            $"Playing as {_selectedCharacter?.Name ?? "no character"}  •  choose or create a world", MenuLayout.Cards);
    }

    private MenuScreen BeginWorldCreation()
    {
        _worldDraft = WorldSettings.RandomWorldName(Random.Shared);
        _worldSeed = Random.Shared.Next();
        return CreateWorldCreationMenu();
    }

    private MenuScreen CreateWorldCreationMenu() => new("world-create", "Create World", new[]
    {
        new MenuEntry($"Name: {_worldDraft}", PromptForWorldName,
            Description: "Enter to type a name."),
        new MenuEntry("Random name", () =>
        {
            _worldDraft = WorldSettings.RandomWorldName(Random.Shared);
            ReplaceCurrent(CreateWorldCreationMenu());
        }, Description: "Roll a new world name."),
        new MenuEntry($"Size: {WorldSizes[_worldSizeIndex].Label} " +
                      $"({WorldSizes[_worldSizeIndex].Width}×{WorldSizes[_worldSizeIndex].Height}, ≈{ApproximateMemoryMb(WorldSizes[_worldSizeIndex].Width, WorldSizes[_worldSizeIndex].Height)} MB)",
            Adjust: amount =>
            {
                _worldSizeIndex = (_worldSizeIndex + Math.Sign(amount) + WorldSizes.Length) % WorldSizes.Length;
                ReplaceCurrent(CreateWorldCreationMenu());
            },
            Description: "Larger worlds have wider biomes and deeper hazards, and take longer to generate."),
        new MenuEntry($"Difficulty: {WorldSettings.Name(_worldDifficulty)}",
            Adjust: amount =>
            {
                _worldDifficulty = Cycle(_worldDifficulty, amount);
                ReplaceCurrent(CreateWorldCreationMenu());
            },
            Description: WorldSettings.Describe(_worldDifficulty)),
        new MenuEntry($"Blight: {WorldSettings.Name(_worldBlight)}",
            Adjust: amount =>
            {
                _worldBlight = Cycle(_worldBlight, amount);
                ReplaceCurrent(CreateWorldCreationMenu());
            },
            Description: WorldSettings.Describe(_worldBlight)),
        new MenuEntry($"Seed: {_worldSeed}", () => PromptForText("World seed", "Any whole number. The same seed makes the same world.",
                _worldSeed.ToString(System.Globalization.CultureInfo.InvariantCulture),
                value => SetWorldSeed(int.TryParse(value, out var parsed) ? parsed : unchecked((int)StableHash(value)))),
            amount =>
            {
                _worldSeed = unchecked(_worldSeed + amount * 101);
                ReplaceCurrent(CreateWorldCreationMenu());
            },
            Description: "Enter to type a seed (text is hashed); left/right steps it."),
        new MenuEntry("Random seed", () =>
        {
            _worldSeed = Random.Shared.Next();
            ReplaceCurrent(CreateWorldCreationMenu());
        }, Description: "Roll a new seed."),
        new MenuEntry("Create & Play", () =>
        {
            try
            {
                var size = WorldSizes[_worldSizeIndex];
                Launch(_profiles.CreateWorld(_worldDraft, _worldSeed, size.Width, size.Height, _worldDifficulty, _worldHazard, _worldBlight));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            { ReplaceCurrentWithStatus(CreateWorldCreationMenu(), ex.Message); }
        }, Description: "Generate the world and land your colonist."),
        new MenuEntry("Back", GoBack)
    }, $"Playing as {_selectedCharacter?.Name ?? "no character"}  •  choose options, then create", preview: MenuPreview.World);

    /// <summary>FNV-1a so text seeds map to the same world on every machine.</summary>
    private static uint StableHash(string text)
    {
        var hash = 2166136261u;
        foreach (var character in text)
            hash = unchecked((hash ^ character) * 16777619u);
        return hash;
    }

    public void PromptForText(string title, string detail, string initial, Action<string> accepted, int maxLength = 32)
    {
        var prompt = new TextPromptState(title, detail, initial, $"Enter confirms  ·  Esc cancels  ·  {maxLength} characters", maxLength);
        prompt.Accepted += value =>
        {
            if (Ui.CurrentState == prompt)
                Ui.GoBack();
            accepted(value);
        };
        prompt.Cancelled += () =>
        {
            if (Ui.CurrentState == prompt)
                Ui.GoBack();
        };
        Ui.SetState(prompt);
    }


    /// <summary>
    /// Options in categories like the reference game's settings sidebar: General, Interface, Video,
    /// Volume, Cursor and Controls. The first row switches category with ←/→; Video is the default.
    /// </summary>
    /// <summary>One settings category's page (Terraria's General, Interface, Video, Volume and Cursor).</summary>
    private MenuScreen CreateOptionsMenu(OptionsCategory category)
    {
        MenuEntry Percent(string name, Func<float> get, Action<float> set, float step, float min, float max, string? description = null) => new(
            $"{name}: {get():P0}",
            Adjust: amount => { set(Math.Clamp(get() + amount * step, min, max)); ApplyPreference(); },
            Description: description);
        MenuEntry Toggle(string name, Func<bool> get, Action<bool> set, string? description = null) => new(
            $"{name}: {(get() ? "On" : "Off")}",
            () => { set(!get()); ApplyPreference(); },
            amount => { set(!get()); ApplyPreference(); },
            Description: description);
        MenuEntry Choice<T>(string name, Func<T> get, Action<T> set, string? description = null) where T : struct, Enum => new(
            $"{name}: {get()}",
            () => { set(Cycle(get(), 1)); ApplyPreference(); },
            amount => { set(Cycle(get(), amount)); ApplyPreference(); },
            Description: description);
        void ApplyPreference()
        {
            SavePreferences();
            DisplayPreferencesChanged?.Invoke();
            ReplaceCurrent(CreateOptionsMenu(category));
        }

        var entries = new List<MenuEntry>();
        switch (category)
        {
            case OptionsCategory.General:
                entries.Add(Toggle("Autosave", () => Preferences.AutoSave, value => Preferences.AutoSave = value, "Save the world every 90 seconds and on exit."));
                entries.Add(Toggle("Autopause", () => Preferences.AutoPause, value => Preferences.AutoPause = value, "Pause the world while the inventory or colonist sheet is open."));
                entries.Add(Toggle("Item and event text", () => Preferences.ShowItemText, value => Preferences.ShowItemText = value, "Show the message log above the hotbar."));
                entries.Add(Toggle("Damage numbers", () => Preferences.DamageNumbers, value => Preferences.DamageNumbers = value, "Floating numbers when anything takes damage."));
                entries.Add(Toggle("Minimap", () => Preferences.MinimapEnabled, value => Preferences.MinimapEnabled = value, "Explored-area map in the top-right corner."));
                entries.Add(Toggle("Building grid", () => Preferences.ShowBuildingGrid, value => Preferences.ShowBuildingGrid = value, "Grid around the cursor while holding a placeable block."));
                break;
            case OptionsCategory.Interface:
                entries.Add(Percent("UI scale", () => Preferences.UiScale, value => Preferences.UiScale = value, 0.05f, 0.75f, 1.5f, "Size of menus and panels."));
                entries.Add(Percent("Zoom", () => Preferences.Zoom, value => Preferences.Zoom = value, 0.1f, 0.6f, 2f, "How close the camera is to the world. In game, Ctrl + mouse wheel zooms too."));
                entries.Add(Choice("Enemy health bars", () => Preferences.EnemyHealthBars, value => Preferences.EnemyHealthBars = value, "Above or below damaged creatures, or hidden."));
                entries.Add(Toggle("Name tags", () => Preferences.NameTags, value => Preferences.NameTags = value, "Names over your colonist and crew."));
                entries.Add(Toggle("Status panel", () => Preferences.StatusPanel, value => Preferences.StatusPanel = value, "Biome, temperature, shelter and incident readout."));
                entries.Add(Toggle("Frame rate", () => Preferences.ShowFrameRate, value => Preferences.ShowFrameRate = value, "Show frames per second in the corner."));
                break;
            case OptionsCategory.Video:
                entries.Add(new($"Window mode: {DisplayOptions.ModeLabel(Preferences.ScreenMode)}", Adjust: amount =>
                {
                    Preferences.ScreenMode = DisplayOptions.CycleMode(Preferences.ScreenMode, amount);
                    Preferences.SetScreenMode(Preferences.ScreenMode);
                    ApplyPreference();
                }, Description: "Windowed, borderless fullscreen, or exclusive fullscreen."));
                entries.Add(new($"Window size: {Preferences.DisplayWidth}×{Preferences.DisplayHeight}", Adjust: amount =>
                {
                    var sizes = DisplayOptions.StandardSizes;
                    var current = DisplayOptions.NearestSizeIndex(Preferences.DisplayWidth, Preferences.DisplayHeight);
                    var index = Math.Clamp(current + amount, 0, sizes.Length - 1);
                    Preferences.DisplayWidth = sizes[index].Width;
                    Preferences.DisplayHeight = sizes[index].Height;
                    ApplyPreference();
                }, Description: "Standard window sizes; applies immediately."));
                entries.Add(Choice("Lighting", () => Preferences.Lighting, value => Preferences.Lighting = value, "Low: blocky. Standard: smooth. High: smooth with coloured glow. Off: full bright."));
                entries.Add(Choice("Frame pacing", () => Preferences.FramePacing, value => Preferences.FramePacing = value, "Smooth uses the most CPU; Save power redraws less often."));
                entries.Add(Toggle("Background", () => Preferences.BackgroundEnabled, value => Preferences.BackgroundEnabled = value, "Sky gradient, stars and planets behind the world."));
                entries.Add(Percent("Parallax", () => Preferences.BackgroundParallax, value => Preferences.BackgroundParallax = value, 0.1f, 0f, 2f, "How fast the title backdrop drifts."));
                entries.Add(Toggle("Weather effects", () => Preferences.WeatherEffects, value => Preferences.WeatherEffects = value, "Biome sky tints, toxic fallout and solar flare overlays."));
                entries.Add(Toggle("Flashy effects", () => Preferences.FlashyEffects, value => Preferences.FlashyEffects = value, "Hit flashes and muzzle flashes."));
                entries.Add(Choice("Particles", () => Preferences.Particles, value => Preferences.Particles = value, "Debris and death bursts."));
                break;
            case OptionsCategory.Volume:
                entries.Add(Percent("Music", () => Preferences.MusicVolume, value => Preferences.MusicVolume = value, 0.05f, 0f, 1f, "The soundtrack."));
                entries.Add(Percent("Sound", () => Preferences.SoundVolume, value => Preferences.SoundVolume = value, 0.05f, 0f, 1f, "Effects: tools, hits, voices and menus."));
                entries.Add(Percent("Ambient", () => Preferences.AmbientVolume, value => Preferences.AmbientVolume = value, 0.05f, 0f, 1f, "Rain and other weather."));
                break;
            case OptionsCategory.Cursor:
                entries.Add(new($"Cursor color: {GamePreferences.CursorColors[Preferences.CursorColor].Name}",
                    () => { Preferences.CursorColor = (Preferences.CursorColor + 1) % GamePreferences.CursorColors.Length; ApplyPreference(); },
                    amount => { Preferences.CursorColor = (Preferences.CursorColor + amount + GamePreferences.CursorColors.Length) % GamePreferences.CursorColors.Length; ApplyPreference(); },
                    Description: "Colour of the drawn cursor."));
                entries.Add(Toggle("Cursor border", () => Preferences.CursorBorder, value => Preferences.CursorBorder = value, "Dark outline around the cursor."));
                entries.Add(Percent("Cursor size", () => Preferences.CursorScale, value => Preferences.CursorScale = value, 0.05f, 0.75f, 1.5f));
                entries.Add(Choice("Smart cursor", () => Preferences.SmartCursor, value => Preferences.SmartCursor = value, "Toggle: Ctrl switches it on and off. Hold: active while Ctrl is held."));
                break;
        }

        entries.Add(new MenuEntry("Back", GoBack));
        return new MenuScreen("options", category.ToString(), entries, "←/→ adjust  ·  changes apply and persist immediately");
    }

    /// <summary>
    /// The controls screen: every action from <see cref="ControlCatalog"/> under its group, with its keys. Selecting
    /// one captures a new main key; Terraria's defaults are one entry away.
    /// </summary>
    private MenuScreen CreateControlsMenu(string? focus = null)
    {
        var entries = new List<MenuEntry>
        {
            new("Mouse: left use · right interact · wheel hotbar · Ctrl+wheel zoom", Enabled: false)
        };
        foreach (var group in ControlCatalog.All.GroupBy(action => action.Group))
        {
            entries.Add(new MenuEntry($"— {ControlCatalog.GroupName(group.Key)} —", Enabled: false));
            foreach (var action in group)
            {
                var keys = InputProfile.KeyLabel(action.Id);
                entries.Add(new MenuEntry($"{action.Label}: {(keys.Length > 0 ? keys : "unbound")}",
                    () => PromptForBinding(action.Id), Tag: action.Id,
                    Description: action.Hint.Length > 0 ? action.Hint : "Enter to change the main key; other keys stay."));
            }
        }
        entries.Add(new MenuEntry("Reset to defaults", () =>
        {
            InputProfile = Rimtera.UI.InputProfile.CreateDefault();
            SavePreferences();
            ReplaceCurrent(CreateControlsMenu());
            CurrentScreen.SetStatus("Controls reset to the Terraria-style defaults.");
        }, Description: "Restores every key to the defaults (Terraria's layout)."));
        entries.Add(new MenuEntry("Back", GoBack));
        var screen = new MenuScreen("controls", "Controls", entries, "Enter rebind   ↑/↓ navigate   Esc back");
        if (focus is not null && entries.FindIndex(entry => Equals(entry.Tag, focus)) is >= 0 and var index)
            screen.SelectIndex(index);
        return screen;
    }

    private void Launch(WorldProfile world)
    {
        var character = ResolveCharacter(world);
        _profiles.MarkWorldPlayed(world.Id);
        WorldLaunchRequested?.Invoke(world, character);
    }

    /// <summary>
    /// Saved worlds remember which character made the save. Continue and Load use that
    /// association; new worlds fall back to the character chosen in the menu.
    /// </summary>
    private CharacterProfile ResolveCharacter(WorldProfile world)
    {
        if (_saves.TryReadFile(world.Id, out var file) && file is not null)
        {
            var saved = file.CharacterId is Guid id
                ? _profiles.Characters.FirstOrDefault(character => character.Id == id)
                : null;
            saved ??= _profiles.Characters.FirstOrDefault(character =>
                !string.IsNullOrWhiteSpace(file.CharacterName) &&
                string.Equals(character.Name, file.CharacterName, StringComparison.OrdinalIgnoreCase));
            if (saved is not null)
            {
                _selectedCharacter = saved;
                return saved;
            }
        }

        var fallback = _selectedCharacter ?? _profiles.Characters.FirstOrDefault();
        fallback ??= _profiles.CreateCharacter("Adventurer");
        _selectedCharacter = fallback;
        return fallback;
    }

    private void Navigate(UiState screen) => Ui.SetState(screen);

    private void ResetTo(MenuScreen screen)
    {
        Ui.ClearHistory();
        Ui.SetState(screen, saveCurrent: false);
    }

    private void ReplaceCurrent(MenuScreen replacement)
    {
        if (Ui.CurrentState is MenuScreen previous)
            replacement.SelectIndex(previous.SelectedIndex);
        Ui.SetState(replacement, saveCurrent: false);
        replacement.FocusSelection(Ui);
    }

    private void ReplaceCurrentWithStatus(MenuScreen replacement, string status)
    {
        replacement.SetStatus(status);
        ReplaceCurrent(replacement);
    }

    private static TEnum Cycle<TEnum>(TEnum current, int direction) where TEnum : struct, Enum
    {
        var values = Enum.GetValues<TEnum>();
        var index = Array.IndexOf(values, current);
        return values[(index + Math.Sign(direction) + values.Length) % values.Length];
    }

    private static string NormalizeDraft(string value, string fallback)
    {
        var name = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return name.Length > 32 ? name[..32] : name;
    }
}
