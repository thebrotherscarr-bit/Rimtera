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

`UI/InventoryUi.cs` binds the real fixed-slot `Inventory` model to hotbar/inventory/chest item-slot elements. It supports Terraria's slot rules: pickup, place, merge and swap on the press; right-click take-one with an accelerating hold (`StackSplitRepeater`); Shift to and from an open chest; Ctrl trash; Alt favourite. The held stack is the player inventory's `Cursor` (Terraria's mouse item): it is the item in hand while the hub is open, right-clicking the world throws it, and the host returns it to the bag when the hub closes. `UI/PointerInput.cs` holds the one mouse-button state the window builds from its own MouseDown/MouseUp events (cleared on focus or capture loss), which feeds both the UI and the world; see `docs/CONTROLS_PARITY.md`. In the terminal build, inventory slots are keyboard-focusable and hotbar slots remain addressable with `1`–`0`.

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

## Crafting guidance

Rimtera's take on Terraria's crafting menu and Guide. The logic is in `Systems/CraftingGuide.cs` and `Systems/GameSimulation.Guide.cs`, and it reads everything from `CraftingSystem` and the item, tile and drop tables, so it follows recipe changes. The canvas drawing is in `UI/GraphicalSandbox.Guide.cs`.

- **Crafting list** (inventory hub, under the grid): everything craftable at the stations in reach. **Close to: shown** adds greyed recipes you nearly have (a red corner mark): at your stations with some of the material, or with every material but away from the station. The selected recipe's materials show as have/needed (short ones outlined in red), with the station it needs (green when in reach, red when not). Click to select, click again to craft, and hold to keep crafting faster. Click a material to look it up. The toggle is saved as `CraftingShowsAlmost`.
- **Recipe guide** (the hub's *Recipe guide* button, the station AI's dialogue, or the rebindable *Recipe guide* action, which is unbound by default like the other panel keys): a slot where the chest would sit. Click it while holding an item, or Shift-click an inventory item, to look that item up. **Used in** lists every recipe that uses it and **Made from** every recipe that makes it. Each row shows the result, every material as have/needed and the station. An uncrafted item says where it comes from (trees, mined tiles, creature drops). Click any item to look it up, and **Back** returns to the item before.
- **Station AI** (right-click it, or *Station AI tips* in the hub): next steps worked out from the player's state, in Terraria's order: first station, light, refinery and forge, a better drill for the next ore gate, armour, life cores, then the next boss and the Fracture. Each step quotes the recipe's materials as have/needed and the first missing link. Talking within earshot still teaches guide-gated recipes.
- **Hint line**: the first next step at the top of the screen. Click it to open the recipe guide on it, and × hides that tip. You can turn it off in Options → Interface → *Guide hints*.
- **Tooltips**: each item says where it is crafted and what it is used in.

## Current boundaries

The project has both a terminal renderer and a WinForms host, including mouse hover/click/wheel dispatch, a fullscreen map with pan/zoom and pylon travel, procedural audio through the Windows waveOut API, and a clickable craftable-recipes panel with the station AI's recipe guide. It does not have a GPU backend, gamepad device polling or multiplayer. Character profiles carry skills, passions, traits, backstory, appearance, derived stats, needs, and mood; generated NPC colonists restore their roster state in world snapshots. Window mode (Windowed / Borderless / Fullscreen) and standard window sizes persist as preferences. The menu, text-field, key-capture, inventory, and colonist-dossier states are built on the renderer-independent tree so both hosts share the same models.
