# Rimtera Menu, UI, Preferences, and Save Architecture

This is an original implementation guided by the bounded structural findings in `Phase11_UI_Menu_Inventory.md`, `Phase12_IO_Save_Serialization.md`, and `Phase13_Main_Preferences_Input.md`. It does not reproduce their target game's source or binary formats.

## Retained UI core

`UI/UiFramework.cs` provides a retained element tree, parent-relative pixel/percentage dimensions, min/max clamping, padding/margins/alignment, recursive layout, rectangle hit testing, hover and focus state, click/scroll/activate events, host-supplied directional focus traversal, a state history, draw commands, and an `IUiRenderer` adapter seam. Double-click dispatch uses a 500 ms interval. The root tree recalculates against a replaceable viewport.

## Screens and navigation

`UI/MenuSystem.cs` builds these navigable states:

- Main menu, Continue/World selection, Character selection/creation, and World creation.
- Options for volume scalars, window mode (Windowed / Borderless / Fullscreen), resolution (standard sizes from 1024×768 to 1920×1080), frame skip (Off / On / Subtle, as Terraria), lighting, placement preview, UI scale, and autosave. F10 shows the frame rate.
- A rebindable controls list with semantic action names.
- Pause and inventory entry points used by the interactive simulation.

The world/character list panel uses a centered 80%-width root capped at 650 logical pixels, a 220-pixel top offset, a scroll-windowed button list, and snap labels. Character creation exposes an editable name field. World creation exposes a name and deterministic seed. Menu actions are callbacks, not simulation dependencies.

`UI/TextPromptState.cs` adds two retained overlays: a focused text field for character/world names and a key-capture panel for the controls screen. `UiTextInputElement` owns its value, caret, maximum length, editing commands, and input draw command. Typed characters reach it through the graphical host's `KeyPress` event; Enter submits, Escape cancels, and mouse clicks focus the field or activate OK/Cancel. No native `TextBox` or `ShowDialog` prompt is used.

`UI/InventoryUi.cs` binds the real fixed-slot `Inventory` model to hotbar/inventory/chest item-slot elements. It supports stack pickup, half-stack pickup, single-item placement, compatible-stack merges, incompatible-stack swaps, and quick-transfer between inventories. In the terminal build, inventory slots are keyboard-focusable and hotbar slots remain addressable with `1`–`0`.

## Preferences, profiles, and input

`UI/PreferencesAndProfiles.cs` stores normalized client options in `config.json`, key bindings in `input profiles.json`, and a separate profile index for world and character metadata. Files use Rimtera-owned JSON schemas and temp-file replacement. Volumes clamp to `[0,1]`; display dimensions, UI scale, zoom, quality, and deadzones are validated on load/save. `InputTriggerSet` calculates current, just-pressed, and just-released action sets from the prior frame. Menu navigation defaults to the arrow keys, with mouse hover/click/wheel as the pointer path; WASD is reserved for gameplay. `InputProfile.MenuBindingVersion` migrates older profiles off WASD menu bindings once, and the controls screen refuses new WASD bindings for `Menu*` actions.

The profile index is not a world save. World payloads live separately through `UI/WorldSaveRepository.cs`, keyed by world GUID. Container version 1 encloses `GameSnapshot` version 2. A snapshot stores compressed grid cells plus generation metadata, seed/day/clock/tick, player position/velocity/facing, hit points and needs, inventory/equipment/calibrations/statuses, the colonist roster and each pawn's injury/memory/relation/break/mood state, storyteller state, defeated bosses, blight/Fracture/altar progress, running content/events, player-world flags and character-file data. The explored map is a separate per-character, per-world `.rmap` file. Loading validates dimensions, enums, finite numeric values, stack limits and generation metadata before restoring; older snapshots load through optional fields. The format is intentionally not compatible with any third-party game's save file.

## Runtime integration

- `dotnet run -- --graphics` opens the WinForms host; menus use arrow keys or mouse, and gameplay uses WASD.
- `dotnet run -- --menu` opens the terminal renderer and menu workflows.
- `dotnet run -- --play` starts directly in the sandbox; `Esc` opens pause, `I` opens inventory, `K` opens the colonist sheet, `Enter` activates focused UI controls, arrows navigate, and `Esc` returns/resumes.
- A selected world resumes its last snapshot if present; leaving a session writes a save, with periodic snapshots when autosave is enabled.
- `dotnet run -- --self-test` covers inventory interactions, hit testing/click dispatch, integrated text fields and key capture, profile persistence, colonist state, and save/restore in addition to simulation checks.

## Neo-Terra presentation and screen flow

- Title → Select Character (cards: face portrait, backstory, difficulty; Favorite/Rename/Delete; favourites first) → Create Character (live recoloured face and suit preview; name, difficulty Explorer/Classic/Ironman, appearance, backstory, traits, skills) → Select World (cards: biome icon, size, difficulty, hazard, saved/new) → Create World (name + random name, size, difficulty Explorer/Classic/Brutal, hazard Random/Radiated Deeps/Molten Chasm, seed + random seed, option descriptions, schematic preview) → generation screen (pass bar + overall bar) → fade into the world.
- Continue and Load show a loading screen; Save & Quit and Save & Exit show a saving screen while the snapshot is written on a worker thread. Deletes move save files into `saves\deleted` instead of erasing them.
- `UI/SpriteAtlas.cs` loads `Assets/Sprites/*.png` (cut from the Neo-Terra concept sheets by `tools/sprites/extract_sprites.py`) and caches scaled, flipped and recoloured variants; `UI/CharacterLook.cs` maps a colonist's appearance onto the suit and face sprites.
- `dotnet run -- --quickstart` skips the menus and generates a throwaway world (development aid).

## Current boundaries

The project has both a terminal renderer and a WinForms host, including mouse hover/click/wheel dispatch, a fullscreen map with pan/zoom and pylon travel, procedural audio through the Windows waveOut API, and a clickable craftable-recipes panel. It does not have a GPU backend, gamepad device polling, multiplayer, or a browse-all recipe catalogue. Character profiles carry skills, passions, traits, backstory, appearance, derived stats, needs, and mood; generated NPC colonists restore their roster state in world snapshots. Window mode (Windowed / Borderless / Fullscreen) and standard window sizes persist as preferences. The menu, text-field, key-capture, inventory, and colonist-dossier states are built on the renderer-independent tree so both hosts share the same models.
