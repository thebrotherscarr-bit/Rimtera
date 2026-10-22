# Rimtera Handoff — Graphical Start Menu and Persistent Menu State

**Date:** 2026-10-19  
**Working directory:** `C:\Users\novad\Desktop\Rimtera`  
**Current published binary:** `C:\Users\novad\Desktop\Rimtera\publish\Rimtera.exe`  
**Current status:** Debug and Release builds succeed; the full core self-test suite passes; the published executable opens a single WinForms game window. The title menu, character/world creation, integrated name and key-binding inputs, window mode/size options, save/load/continue, pause menu, colonist dossier, return-to-main-menu, and exit-game paths are implemented and were exercised end-to-end.

**Update in this pass:**

- `GraphicalSandbox` launches into a persistent title menu instead of creating a world immediately.
- Title menu: Single Player, Continue, Load Game, Options, Controls, Exit Game.
- Single Player routes through character selection and world selection; `Continue` loads the most recently saved world.
- Character creation stores a persistent profile; world creation stores name, seed, and the current size catalogue (Tiny 1750×900, Small 4200×1200, Medium 6400×1800, Large 8400×2400; the old 160×72 prototype sizes are historical).
- In-game pause menu: Resume, Inventory, Save Game, Load Game, Settings, Save & Quit to Main Menu, Save & Exit Game.
- `Settings` contains Options and Controls, so the pause menu fits on one screen at 480p and above.
- Esc opens/closes the pause menu; the simulation stops ticking while any menu is open.
- Keyboard and mouse input are gated so menu navigation does not move the player. Menus use the arrow keys and mouse hover/click/wheel; `WASD` is reserved for gameplay. Menu key bindings come from the persistent input profile, and legacy profiles migrate away from WASD menu navigation.
- Character names, world names, and key bindings use retained in-game text fields and key-capture panels. No native `TextBox` or `ShowDialog` prompt is used.
- Save/load uses `WorldSaveRepository` and `GameSimulation.CaptureSnapshot()` / `GameSimulation.Restore()`. Save envelopes record the character ID/name so Continue and Load restore the correct character.
- `RIMTERA_DATA_DIR` can point the graphical host at a temporary profile/save directory for testing.
- Verified end-to-end: create character, create sized world, play, save, return to main menu, Continue, Load Game, Save & Exit Game, and title-screen Exit Game.

---

## 1. What Rimtera is right now

Rimtera is a C#/.NET 10 WinForms prototype backed by a deterministic fixed-step simulation. It is **not** a completed Terraria clone. It has:

- seeded tile world generation with snow / jungle / desert biome bands
- mining, placement, crafting, inventory, equipment, status effects
- movement, combat, projectiles, enemies, a phased boss
- survival needs, autonomous NPC behavior, world events, liquids
- version-2 world snapshots saved per world
- retained UI framework with menu, character/world profiles, settings, controls, inventory, integrated text input, and colonist dossier
- a WinForms renderer that draws the live simulation as colored tiles

The persistent title/pause flow is implemented. Current focus is deeper character creation and colonist/NPC behavior before replacing the prototype world generator.

---

## 2. Run and verify

```powershell
cd "C:\Users\novad\Desktop\Rimtera"

dotnet build
dotnet run -- --self-test
dotnet run -- --graphics   # same as --game, and the default with no arguments

dotnet publish -c Release -r win-x64 --self-contained false -o publish
.\publish\Rimtera.exe
```

The executable opens the title menu. Use the arrow keys or mouse hover/click/wheel to navigate, `Enter`/`Space` to select, and `Esc` to go back. `WASD` is reserved for gameplay. In game, `Esc` opens the pause menu, `I` opens inventory, and `K` opens the colonist sheet.

For a disposable test profile/save directory:

```powershell
$env:RIMTERA_DATA_DIR = "C:\Users\novad\AppData\Local\Temp\opencode\rimtera-test-data"
.\publish\Rimtera.exe
Remove-Item Env:RIMTERA_DATA_DIR
```

Useful CLI modes:

| Command | Purpose |
|---|---|
| `--graphics` / `--game` / no args | Windowed WinForms game |
| `--play` | Terminal game |
| `--menu` | Terminal version of the retained menu |
| `--demo` | Headless simulation summary |
| `--self-test` | 76 deterministic checks |

Build target is currently `net10.0-windows` with `UseWindowsForms=true` in `Rimtera.csproj`.

---

## 3. Start menu and persistent open/close state

### Implemented behaviour

The requirements below are now implemented in `UI/MenuSystem.cs` and `UI/GraphicalSandbox.cs`. The detailed plan in this section is retained as the specification and as a guide for verification/repair.

### Required behaviour

1. The game opens on a title/start menu instead of immediately creating a world.
2. The menu is retained for the lifetime of the process; opening and closing it does not destroy its state.
3. Closing the menu starts/resumes the game. Opening it pauses the simulation.
4. Esc toggles the in-game pause menu. Menu input must not leak into gameplay.
5. Title menu should expose:
   - Single Player / New Game
   - Continue (latest saved world)
   - Load Game (saved worlds only)
   - Options
   - Controls
   - Exit Game
6. In-game pause menu should expose:
   - Resume
   - Inventory
   - Save Game
   - Load Game
   - Settings (Options and Controls)
   - Save & Quit to Main Menu
   - Save & Exit Game
7. Saving uses the existing per-world `WorldSaveRepository`. Loading restores the snapshot through `GameSimulation.Restore`.
8. Profile, character, world, options, and keybinding data stay in the existing JSON stores under `%LOCALAPPDATA%\Rimtera`.
9. A save may be created/loaded without leaving the application. The menu should remain usable after a save or load.
10. Character name, world name, and key binding must be editable in the graphical host through in-game retained-UI fields and key capture, never native Windows dialogs.
11. Menus use arrow keys and the mouse; `WASD` remains dedicated to gameplay.

### Terraria evidence collected with REA

REA was cloned to:

`C:\Users\novad\AppData\Local\Temp\opencode\rea`

The installed target was inspected with:

```powershell
npx -y rea-agents@latest inspect-managed-artifact "C:\Program Files (x86)\Steam\steamapps\common\Terraria\Terraria.exe" --format json > terraria-artifact.json
npx -y rea-agents@latest inspect-managed-members  "C:\Program Files (x86)\Steam\steamapps\common\Terraria\Terraria.exe" --type Terraria.Main --format json > terraria-main.json
npx -y rea-agents@latest inspect-managed-members  "C:\Program Files (x86)\Steam\steamapps\common\Terraria\Terraria.exe" --type Terraria.GameContent.UI.States.UICharacterSelect --type Terraria.GameContent.UI.States.UIWorldSelect --format json > terraria-menuui.json
```

Evidence summary:

- Terraria assembly version: `1.4.5.8`
- SHA-256: `960a03bff6050cf7be16dfc1a7b19e10fc2c4f8f835a6a3b135a50dd9e6ba2f3`
- `Terraria.Main` exposes `menuMode`, `MenuUI`, `gameMenu`, `_characterSelectMenu`, `_worldSelectMenu`, `selectedPlayer`, `selectedWorld`, `LoadWorlds`, `LoadPlayers`, `SaveSettings`, `LoadSettings`, `OpenPlayerSelect`, `OpenWorldSelectUI`, and `GoToWorldSelect`.
- `OpenPlayerSelect` switches the menu mode to `888` and installs `Terraria.GameContent.UI.States.UICharacterSelect`.
- `OpenWorldSelectUI` / `GoToWorldSelect` switch to menu mode `888` and install `Terraria.GameContent.UI.States.UIWorldSelect`.
- `UICharacterSelect` and `UIWorldSelect` both expose `OnInitialize`, `GoBackClick`, `NewCharacterClick` / `NewWorldClick`, `UpdatePlayersList` / `UpdateWorldsList`, and `SetupGamepadPoints`.
- Their literals include `UI.SelectPlayer`, `UI.SelectWorld`, `UI.New`, and `UI.Back`.

**Use only the behaviour pattern:** title -> character select -> world select -> load, with retained menu state and a back path. Do not copy Terraria code, assets, names, or data tables.

The REA result files are temporary and may be deleted by the OS. Re-run the commands if more evidence is needed.

---

## 4. Files that matter

### Menu and profiles

- `UI/MenuSystem.cs` — `MenuEntry`, `MenuScreen`, `MenuFlow`, pause/inventory state wiring.
- `UI/UiFramework.cs` — retained layout, hit testing, focus, draw commands, `UiInputFrame`.
- `UI/PreferencesAndProfiles.cs` — `GamePreferences`, `InputProfile`, `UserProfileStore`, `PreferencesStore<T>`.
- `UI/WorldSaveRepository.cs` — per-world JSON save envelopes.
- `UI/InventoryUi.cs` — inventory overlay state and controller.
- `UI/GraphicalSandbox.cs` — current WinForms host; currently creates a `GameSimulation` in the form constructor and has no menu overlay.
- `Systems/ConsoleMenu.cs` — terminal host; shows how `MenuFlow` events are wired and how saves are loaded.

### Simulation

- `Systems/GameSimulation.cs` — `GameSimulation(seed, width, height)`, `CaptureSnapshot()`, `Restore(snapshot)`, `Tick(GameInput)`.
- `Systems/GameSaveData.cs` — `GameSnapshot`; current format version is `2`.

---

## 5. Exact implementation plan

### Step 1 — extend `MenuFlow` in `UI/MenuSystem.cs`

Add:

```csharp
public bool IsOpen => Ui.CurrentState is not null;

public void Close()
{
    if (Ui.CurrentState is not null)
        Ui.SetState(null, saveCurrent: false);
    Ui.ClearHistory();
}

public void SetStatus(string status)
{
    if (Ui.CurrentState is MenuScreen screen)
    {
        screen.SetStatus(status);
        screen.FocusSelection(Ui);
    }
}
```

Add a save-aware main menu. Keep the existing self-test sequence working: with no save present, the first enabled entry must still lead to character select.

```csharp
private MenuScreen CreateMainMenu()
{
    var hasSaves = _profiles.Worlds.Any(world => _saves.Exists(world.Id));
    return new MenuScreen("main-menu", "RIMTERA", new[]
    {
        new MenuEntry("Single Player", () => Navigate(CreateCharacterSelectMenu())),
        new MenuEntry("Continue", ContinueLatest, Enabled: hasSaves),
        new MenuEntry("Load Game", () => Navigate(CreateLoadSelectMenu()), Enabled: _profiles.Worlds.Count > 0),
        new MenuEntry("Worlds", () => Navigate(CreateWorldSelectMenu())),
        new MenuEntry("Options", () => Navigate(CreateOptionsMenu())),
        new MenuEntry("Controls", () => Navigate(CreateControlsMenu())),
        new MenuEntry("Quit", () => ExitRequested?.Invoke())
    }, hasSaves ? "Saved worlds are ready" : "Create a character and world to begin");
}
```

`ContinueLatest` should select the most recent world with a save, ensure a character exists, then call `Launch(world)`.

Add a saved-only load list:

```csharp
private MenuScreen CreateLoadSelectMenu()
{
    var entries = _profiles.Worlds
        .Where(world => _saves.Exists(world.Id))
        .OrderByDescending(world => world.LastPlayed)
        .Select(world => new MenuEntry(
            $"{world.Name}  •  seed {world.Seed}",
            () => Launch(world),
            SnapLabel: world.Name))
        .ToList();
    entries.Add(new MenuEntry("Back", GoBack, SnapLabel: "Back"));
    return new MenuScreen("load-select", "Load Game", entries,
        entries.Count > 1 ? "Choose a saved world" : "No saved worlds yet");
}
```

Extend the pause menu without breaking the existing 3-argument call:

```csharp
public void ShowPauseMenu(
    Action resume,
    Action openInventory,
    Action saveAndExit,
    Action? save = null,
    Action? load = null,
    Action? quitToTitle = null)
{
    _pauseResume = resume;
    _directUiClose = null;

    var entries = new List<MenuEntry>
    {
        new("Resume", resume),
        new("Inventory", openInventory)
    };
    if (save is not null) entries.Add(new("Save Game", save));
    if (load is not null) entries.Add(new("Load Game", load));
    entries.Add(new("Settings", () => Navigate(CreatePauseSettingsMenu())));

    if (quitToTitle is not null)
        entries.Add(new("Save & Quit to Main Menu", quitToTitle));
    else
        entries.Add(new("Save & Exit", saveAndExit));
    if (exitGame is not null)
        entries.Add(new("Save & Exit Game", exitGame));

    var screen = new MenuScreen("pause-menu", "Paused", entries);
    Ui.ClearHistory();
    Ui.SetState(screen, saveCurrent: false);
    screen.FocusSelection(Ui);
}
```

Do **not** remove the `saveAndExit` parameter or the existing back-resume behaviour: `Systems/CoreSelfTests.cs` checks it.

### Step 2 — refactor `UI/GraphicalSandbox.cs`

Current form creates `_game` as a readonly field in the constructor. Replace that with:

```csharp
private readonly MenuFlow _menu;
private GameSimulation? _game;
private WorldProfile? _activeWorld;
private CharacterProfile? _activeCharacter;
private bool _menuOpen = true;
private float _autosaveSeconds;
```

Constructor changes:

```csharp
_menu = new MenuFlow();
_menu.ExitRequested += Close;
_menu.WorldLaunchRequested += StartWorld;
_menu.TextPromptRequested += PromptForText;
_menu.BindingPromptRequested += PromptForBinding;

_canvas = new GameCanvas(_menu) { Dock = DockStyle.Fill };
Controls.Add(_canvas);

_menu.Open();
_menuOpen = true;
UpdateMenuViewport();
```

Wire the timer:

```csharp
private void Advance()
{
    if (_menuOpen || _game is null)
    {
        _canvas.Invalidate();
        return;
    }

    // existing gameplay input and Tick logic here
    // use _game instead of _game!
    // then autosave if Preferences.AutoSave and enough time has elapsed
    _canvas.Invalidate();
}
```

Starting a world:

```csharp
private void StartWorld(WorldProfile world, CharacterProfile character)
{
    try
    {
        GameSimulation? restored = null;
        if (_menu.Saves.TryLoad(world.Id, out var snapshot) && snapshot is not null)
            restored = GameSimulation.Restore(snapshot);

        _game = restored ?? new GameSimulation(world.Seed);
        _activeWorld = world;
        _activeCharacter = character;
        _menu.Close();
        _menuOpen = false;
        _canvas.Game = _game;
        _canvas.ActiveWorldName = world.Name;
        _canvas.ActiveCharacterName = character.Name;
        _canvas.Invalidate();
    }
    catch (InvalidDataException ex)
    {
        _menu.SetStatus($"Could not load: {ex.Message}");
        _menu.Open();
        _menuOpen = true;
    }
}
```

Save:

```csharp
private bool SaveGame()
{
    if (_game is null || _activeWorld is null)
        return false;
    try
    {
        _menu.Saves.Save(_activeWorld.Id, _game.CaptureSnapshot());
        _menu.SetStatus("World saved.");
        return true;
    }
    catch (IOException ex)
    {
        _menu.SetStatus($"Save failed: {ex.Message}");
        return false;
    }
}
```

Pause open:

```csharp
private void OpenPauseMenu()
{
    if (_game is null)
    {
        _menu.Open();
        _menuOpen = true;
        return;
    }

    _menu.ShowPauseMenu(
        resume: () => CloseMenu(),
        openInventory: () => _menu.ShowInventory(
            new InventoryUiState(_game.Player.Inventory),
            onClose: () => CloseMenu()),
        saveAndExit: () => { SaveGame(); QuitToTitle(); },
        save: () => SaveGame(),
        load: () => _menu.ShowLoadMenu(),
        quitToTitle: () => { SaveGame(); QuitToTitle(); });
    _menuOpen = true;
}
```

`CloseMenu()` must call `_menu.Close()` if the current state is the pause menu (not if a text prompt is open), set `_menuOpen = false`, and restore canvas focus.

Quit to title:

```csharp
private void QuitToTitle()
{
    if (_game is not null && _activeWorld is not null && _menu.Preferences.AutoSave)
        SaveGame();
    _game = null;
    _activeWorld = null;
    _activeCharacter = null;
    _canvas.Game = null;
    _menu.Open();
    _menuOpen = true;
}
```

Input gating in `OnKeyDown`:

```csharp
if (_menuOpen)
{
    var frame = TranslateMenuKey(e.KeyCode);
    if (frame is { } input)
    {
        _menu.Handle(input);
        _menuOpen = _menu.IsOpen;
        _canvas.Invalidate();
        e.Handled = true;
        e.SuppressKeyPress = true;
    }
    return;
}

if (e.KeyCode == Keys.Escape)
{
    OpenPauseMenu();
    e.Handled = true;
    return;
}
// existing gameplay keys...
```

`TranslateMenuKey` maps:

- `Up` / `W` -> `NavigationY: -1`
- `Down` / `S` -> `NavigationY: 1`
- `Left` / `A` -> `NavigationX: -1`
- `Right` / `D` -> `NavigationX: 1`
- `Enter` / `Space` -> `ConfirmPressed: true`
- `Escape` / `Backspace` -> `BackPressed: true`

Only return a frame when the key is relevant. Keep menu state retained when the menu is closed.

### Step 3 — graphical menu renderer

`UiFramework` already produces renderer-neutral `UiDrawCommand`s. Add a small WinForms renderer, either in `GraphicalSandbox.cs` or `UI/GraphicalUiRenderer.cs`:

```csharp
private static void DrawMenu(Graphics g, MenuFlow menu)
{
    foreach (var command in menu.Ui.BuildDrawCommands())
    {
        var rect = new RectangleF(command.Bounds.X, command.Bounds.Y,
            command.Bounds.Width, command.Bounds.Height);

        switch (command.Kind)
        {
            case UiDrawKind.Panel:
                using (var brush = new SolidBrush(Color.FromArgb(command.Color.A, command.Color.R, command.Color.G, command.Color.B)))
                using (var pen = new Pen(Color.FromArgb(120, 120, 180)))
                    g.FillRectangle(brush, rect), g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                break;

            case UiDrawKind.Button:
                var color = command.Focused || command.Hovered ? UiColor.ButtonHover : command.Color;
                using (var brush = new SolidBrush(Color.FromArgb(255, color.R, color.G, color.B)))
                using (var pen = new Pen(command.Focused ? Color.White : Color.FromArgb(90, 100, 140), command.Focused ? 2f : 1f))
                {
                    g.FillRectangle(brush, rect);
                    g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                }
                DrawCenteredText(g, command.Text, rect);
                break;

            case UiDrawKind.Text:
                DrawCenteredOrLeftText(g, command, rect);
                break;

            case UiDrawKind.ItemSlot:
                using (var brush = new SolidBrush(Color.FromArgb(220, 24, 28, 40)))
                using (var pen = new Pen(Color.FromArgb(120, 130, 160)))
                {
                    g.FillRectangle(brush, rect);
                    g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                }
                g.DrawString(command.Text, UiFont, Brushes.White, rect.X + 6, rect.Y + 6);
                break;
        }
    }
}
```

Important: the menu viewport must be the canvas size:

```csharp
private void UpdateMenuViewport() => _menu.Ui.SetViewport(_canvas.Width, _canvas.Height);
```

Call it from `OnResize`, after creating the canvas, and before drawing commands.

### Step 4 — `GameCanvas` changes

- Accept `MenuFlow` in the constructor.
- Add `public GameSimulation? Game { get; set; }`.
- Add `ActiveWorldName` / `ActiveCharacterName` for the HUD.
- If `Game is null`, draw a title backdrop (gradient, silhouettes, stars, or simple animated bars).
- If `Game is not null`, draw the existing world render.
- When the menu is open, draw the game dimmed and then call `DrawMenu`.
- Only draw the in-game HUD when the menu is closed.
- Keep a `public bool MenuOpen { get; set; }` or read from the form.
- Invalidate on every timer tick.

### Step 5 — mouse and focus

Optional but recommended:

- `_canvas.MouseMove` -> call `_menu.Handle(new UiInputFrame(point))` while open.
- `_canvas.MouseDown` / `MouseUp` -> send `LeftDown` true/false.
- `UiFramework` already handles click, hover, focus, and button activation.
- Do not forward mouse input to gameplay while `_menuOpen`.

### Step 6 — integrated text fields and key capture

Do **not** add a native `TextBox`, `InputBox`, or `PromptForm.ShowDialog` path. Text boxes are part of the game surface. `UI/TextPromptState.cs` supplies two retained states:

- `TextPromptState` — title, description, focused `UiTextInputElement`, hint, OK, and Cancel. The input owns its value, caret, max length, and editing methods. `Enter` submits, `Esc` cancels, arrow keys move the caret, Backspace/Delete edit, and mouse clicks focus the field or activate the buttons.
- `BindingCaptureState` — shows the current binding and captures the next key press. `Esc` cancels. The controls screen refuses `WASD` bindings for `Menu*` actions.

The graphical host routes printable characters through `KeyPress` into `MenuFlow.InsertText`, control keys through `MenuFlow.EditText`, and Enter/Escape through `MenuFlow.SubmitTextPrompt` / `CancelTextPrompt`. It forwards mouse frames exactly as it does for menus. `UiTextInputElement` is drawn through a dedicated `UiDrawKind.Input` command, so both the graphical and terminal adapters render the same field.

`MenuFlow` opens these states directly:

```csharp
var prompt = new TextPromptState("Character name", description, _characterDraft.Name, hint, 32);
prompt.Accepted += value => { if (Ui.CurrentState == prompt) Ui.GoBack(); SetCharacterName(value); };
prompt.Cancelled += () => { if (Ui.CurrentState == prompt) Ui.GoBack(); };
Ui.SetState(prompt);
```

Call `_menu.SavePreferences()` when settings change if the existing menu code does not already do it.

### Step 7 — tests

The existing self-test at `Systems/CoreSelfTests.cs` around line 260 depends on:

- `Open()` producing `main-menu`
- first confirm entering character select
- second confirm entering world select
- third confirm producing a `WorldLaunchRequested`
- `ShowPauseMenu(resume, inventory, saveAndExit)` still resuming on back

Keep that behaviour. Add a new check for:

- `MenuFlow.IsOpen` true after `Open()`
- `MenuFlow.Close()` makes `IsOpen` false
- `MenuFlow.ShowLoadMenu()` builds only saved worlds
- `Continue` is disabled when there are no saves
- save/load round trip still restores the world

Then run:

```powershell
dotnet build
dotnet run -- --self-test
```

---

## 6. Acceptance criteria

- Launching `Rimtera.exe` shows a title/start menu, not a world.
- The menu can be closed (start/resume) and reopened (Esc).
- The menu object and its selected screen survive open/close.
- Single Player -> character -> world -> game works.
- Continue loads the latest saved world.
- Load Game lists only worlds with saves.
- Esc pauses the running simulation.
- Save Game writes a version-2 snapshot and reports success/failure.
- Save & Quit to Main Menu returns to the title menu without closing the app.
- Save & Exit Game saves and closes the app.
- Exit Game on the title menu closes the app.
- Options and Controls remain usable from both title and pause menus.
- Mouse or keyboard navigation works; menu input never moves the player.
- `dotnet run -- --self-test` passes (76 checks).
- Published exe launches and stays alive for at least a few seconds.
- Window mode options in Options: Windowed (default), Borderless fullscreen, Fullscreen; cycles with left/right arrow and applies immediately.
- Standard window sizes: 1024×768, 1280×720 (default), 1366×768, 1600×900, 1920×1080; cycles with left/right arrow, applies immediately, and persists to `config.json`.
- Mode and size changes take effect live without leaving the menu; sizes are clamped to the monitor working area.

---

## 7. Ollama / local-model handoff

The project already contains `opencode.json` with an Ollama provider:

```json
{
  "provider": {
    "ollama": {
      "npm": "@ai-sdk/openai-compatible",
      "name": "Ollama Local Engine",
      "options": { "baseURL": "http://127.0.0.1:11434/v1" },
      "models": {
        "qwen3.5:14b": { "name": "Qwen Code Local", "tool_call": true }
      }
    }
  },
  "model": "ollama/qwen3.5:14b"
}
```

To use it:

```powershell
ollama serve
ollama pull qwen3.5:14b
ollama list
```

Then start OpenCode in `C:\Users\novad\Desktop\Rimtera` and select `ollama/qwen3.5:14b` (or set the `model` field above). If the local model has weak tool-calling, keep the work in small steps and paste the exact file/edit commands rather than asking it to hold the whole design in one shot.

Local-model rules:

1. Read this file first.
2. Read the actual files before editing.
3. Make one subsystem change at a time.
4. Build and run self-tests after every change.
5. Never claim full Terraria parity.
6. Do not copy Terraria assets or code; use it only as behavioural evidence.

---

## 8. Suggested first prompt for the local model

> You are continuing the Rimtera project at `C:\Users\novad\Desktop\Rimtera`. Read `docs/HANDOFF_START_MENU.md` in full first. Your immediate task is the graphical persistent start menu described there. Use the existing `MenuFlow`, `UiFramework`, `WorldSaveRepository`, and `GameSimulation` APIs. Do not rewrite the simulation. Make incremental edits, run `dotnet build` and `dotnet run -- --self-test` after each step, and report exact files changed and test results. Do not claim full Terraria or RimWorld parity.

---

## 9. Current limitations / non-goals

- No full Terraria world, item, NPC, or asset database.
- No multiplayer.
- Saves do not serialize enemies, projectiles, live world events, or injuries.
- The renderer is simple colored rectangles, not sprite graphics.
- RimWorld-style AI is limited to the existing autonomous activity/needs behavior.
- The project is not a git repository; changes are local files.
