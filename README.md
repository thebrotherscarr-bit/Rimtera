# Rimtera

Rimtera is an original, engine-neutral 2D sandbox game prototype written in C#. Its current playable slice composes world generation, tile placement/mining, inventory, movement, weapons/projectiles, autonomous enemies, a phased boss, survival needs, world events, and local liquid simulation into one deterministic fixed-step runtime.

## Run

```powershell
dotnet run
dotnet run -- --graphics
dotnet run -- --menu
dotnet run -- --play
dotnet run -- --self-test
dotnet run -- --demo
```

### Continuous integration

Three GitHub Actions workflows guard the repository:

- **Prove** (`prove.yml`) — on every push and pull request: builds in Release, then runs three provers in parallel (`--self-test`, `--batch-a-test`, headless `--demo`) and proves the demo is byte-identical across two runs. Each prover log is kept as a run artifact.
- **Version** (`version.yml`) — validates `<Version>` in `Rimtera.csproj` as SemVer. Run it manually on `main` to create the matching `vX.Y.Z` tag.
- **Publish** (`publish.yml`) — on a `vX.Y.Z` tag: checks the tag matches `<Version>`, re-runs the core proofs, publishes a self-contained win-x64 single-file build, and attaches a zip plus `SHA256SUMS.txt` to the GitHub release. A manual run builds the artifact without creating a release.

Release: bump `<Version>`, run **Version** on `main` to tag, and **Publish** takes it from there — or push the tag yourself (`git tag v0.1.0; git push origin v0.1.0`).

No arguments (or `--graphics` / `--game`) opens the WinForms client at the title menu. The title menu follows Terraria: **Single Player** lists your characters, then your worlds (saved ones marked, each launching with the character it was saved with), with creation for both; **Settings** holds General, Interface, Video, Volume, Cursor and Controls; **Exit Game** closes the app. While playing, `Esc` opens the inventory hub (below); its **Settings** button opens the in-game Settings: Close Menu, Save Game, the same categories, Feats, Save & Exit to Main Menu, and Save & Exit Game. Menus use the arrow keys or the mouse (hover, click, and wheel); `WASD` remains reserved for gameplay. Character names, world names, and key bindings are edited in retained in-game fields and key-capture panels, not native Windows dialogs. `--menu` is the terminal equivalent; `--play` starts directly in a terminal world.

### The inventory hub

As in Terraria 1.4, the inventory is where everything lives:

- **Inventory**: ten columns by five rows, the top row being the hotbar (numbered 1-0). Left click picks up and places; right click takes half (or equips gear); **Shift+click** moves a stack to the open chest (or equips it, or trashes it); **Ctrl+click** trashes; **Alt+click** marks a favorite, which sort, deposit and quick stack leave alone.
- Beside it: **Sort**, **Quick stack** (to nearby chests), the **Cargo pod** when you have one, the **Wires** builder toggle, and the **trash slot** (drop a held stack on it; click it empty-handed to take the stack back).
- **Chests** open underneath with **Loot all**, **Deposit all**, **Quick stack**, **Restock** and **Sort**.
- **Crafting** underneath that: everything craftable nearby as a grid; click a result to see its materials, click it again to craft; the wheel scrolls. **Ask station AI** suggests what your materials make.
- Down the right: worn **gear** (armor, accessories, vanity), **Loadouts 1-3**, and tabs for **Housing** (check the room you stand in; see your crew), **Crew**, **Bestiary** and **Feats**.
- **Settings** in the bottom-right corner.

Right-click a **bed** to sleep and the **station AI** for guidance, as with Terraria's beds and Guide.

### Graphical gameplay controls

Defaults follow Terraria's PC layout, and every key can be rebound under Settings → Controls (crafting, crew, bestiary, feats, sleep, housing, wires and eating start unbound since the inventory and right clicks cover them, but each can take a key) (Shift, Ctrl and Alt included; a key taken from another action moves and the screen says so). The `/` key toggles an on-screen key reference built from the current bindings. One list, `ControlCatalog` in `UI/PreferencesAndProfiles.cs`, drives gameplay input, the controls screen and that reference.

| Key | Action |
|---|---|
| `W` `A` `S` `D` | Up / climb, left, down / drop through platforms, right |
| `Space` | Jump (also lets go of ropes) |
| Double-tap `A` / `D` | Dash (a dash key can be bound too) |
| Left mouse | Use the selected item; placing a block on another swaps it |
| Right mouse | Interact: doors, chests, stations, people, beds (sleep), the station AI |
| Mouse wheel / `1`–`0` | Hotbar (`Ctrl` + wheel zooms) |
| `Esc` / `I` | Inventory hub: crafting, chests, gear, crew, housing, bestiary, feats, settings |
| `E` | Grappling hook |
| `T` | Throw the held item |
| `Shift` (hold) | Smart select: switch to the tool that fits what's under the cursor |
| `Ctrl` | Smart cursor (toggle or hold, per Settings → Cursor) |
| `R` | Quick mount |
| `H` / `J` / `B` | Quick heal / charge / buff |
| `M` / `Tab` | Full map / map style; `+` and `-` zoom the minimap |
| `` ` `` (hold) + `1`–`8` | Emotes |

### Terminal gameplay controls

The terminal reads the same bindings where they make sense and fixes a few keys of its own, since it has no mouse.

| Key | Action |
|---|---|
| `A` / `D` | Move left/right |
| `Space` | Jump |
| `X` | Dash |
| `1`–`0` | Select first ten inventory slots |
| `Tab` | Cycle to the next non-empty slot |
| `M` | Mine the facing tile with a selected pickaxe |
| `E` | Use the selected item (place, melee, or fire) |
| `R` | Replace the facing tile with the selected block (tool-gated; returns the old tile) |
| `[` / `]` | Cycle through currently craftable recipes |
| `C` | Craft the selected recipe (or the first available if none is selected) |
| `V` | Consult the guide for recipes unlocked by carried materials |
| `F` | Eat a berry |
| `K` | Open the colonist sheet |
| `Esc` / `I` | Open inventory UI |
| `P` | Pause / resume |
| `Q` | End the session and save |

## Integrated modules

- `Systems/GameSimulation.cs` owns the fixed-step tick and coordinates all world systems.
- `World/TileGrid.cs` stores tile, wall, damage, normalized liquid, and frame-atlas state; `World/WorldGeneration.cs` builds seeded terrain and runs bounded liquid relaxation.
- `World/PlacementSystem.cs` validates range, grid-ray LOS, support, wall backing, liquids, explicit replacement allow-lists, and tool power before atomic writes.
- `World/TileFramingSystem.cs` updates 8-neighbor masks and atlas coordinates over dirty regions.
- `Systems/ItemSystem.cs` defines the project’s items, stack inventory, mining drops, tools, weapons, ammunition, stations, and food.
- `Systems/CraftingSystem.cs` indexes recipes by ingredient/output/station, checks physical stations within a Euclidean tile radius, applies atomic inventory exchanges, and tracks guide-material unlocks.
- `Entities/PawnMovement.cs` drives the player and ground NPC movement with tunable acceleration, gravity, jumping, dashes, and collision.
- `Entities/Actors.cs` defines the player, enemy actors, projectiles, and health/need attachments.
- `Systems/GameSimulation.cs` includes melee/ranged combat, projectile collision, off-screen spawn selection, slime/moth behavior, and a two-phase Stone Sentinel controller.
- `Systems/EventManager.cs` schedules original dusk/dawn events whose spawn/cap modifiers feed the NPC spawner.
- `World/WorldGeneration.cs` `BiomePass` lays out the seeded biomes (Frozen Wasteland left, Grassland centre, Junk Wasteland right, Radiated Deeps pockets mid-depth, Molten Chasm with lava pools at the bottom) and grows biome ores as random-walk veins; `Systems/BiomeScanner.cs` reads the player's biome and `Systems/BiomeSpawnTables.cs` picks biome creatures.
- `Systems/RegionMap.cs` flood-fills open cells into regions/rooms; `Systems/TemperatureModel.cs` derives outdoor and per-room temperatures (lava heats, water cools, Oxygen units stabilise) that drive comfort and exposure; `Systems/Storyteller.cs` is the hourly incident director (raids, cargo drops, cold snaps/heat waves, toxic fallout, wanderers, solar flares).
- `AI/SurvivalBehavior.cs` evaluates food/rest priorities; `Entities/HealthComponent.cs` tracks body injuries and derived capacities.
- `Entities/BodyConditions.cs` + `Entities/HealthComponent.cs`: original anatomy (organs inside, limbs/head outside), coverage-weighted hits with inward propagation and part destruction, condition kinds (cuts, bites, burns, frostbite, radiation sickness, infection, scars, blood loss, missing parts), tending, capacities (moving, manipulation, sight, consciousness), pain shock, downed and death states.
- `Characters/Thoughts.cs`, `Characters/MentalBreaks.cs`, `Characters/Relations.cs`: memories and situational thoughts driving mood, trait-shifted mental breaks with catharsis, compatibility/opinion/relations, interactions (chats, kind words, insults, romance), social fights and grief. Design notes: [docs/COLONIST_SYSTEMS.md](docs/COLONIST_SYSTEMS.md).
- `Systems/ConsoleSandbox.cs` is a thin terminal frontend over the simulation API.
- `Characters/ColonistSystem.cs` defines the colonist sheet: skills, passions, traits, backstories, appearance, derived stats, mood, deterministic drafts, and generated crew profiles.
- `UI/UiFramework.cs` provides retained layout, hit testing, pointer/focus routing, interface history, text-field draw commands, and renderer-neutral draw commands.
- `UI/MenuSystem.cs` composes the main/world/character/options/controls/pause states; `Systems/ConsoleMenu.cs` hosts them in the terminal.
- `UI/TextPromptState.cs` provides the integrated name field and key-capture overlays used by the graphical and terminal hosts.
- `UI/InventoryUi.cs` binds hotbar/inventory/chest grids to stack transfer, splitting, merging, swapping, and quick-move operations.
- `UI/PreferencesAndProfiles.cs` persists normalized options, input bindings, and world/character metadata as separate JSON data; legacy profiles migrate menu navigation away from WASD once.
- `UI/WorldSaveRepository.cs` stores versioned Rimtera snapshots separately from profile metadata; snapshots restore the grid, generation metadata, player/colonist state, injuries, equipment, events and the world map.

## Design and scope

The seven bounded research sheets in `docs/Terraria_Specs/` were used as architectural references only. Rimtera uses newly authored algorithms, tuning, item definitions, names, and encounter behavior; it does not include a full per-item numeric database or copied game code. The installed decompilation folder was not available when those bounded sheets were generated.

This is an integrated prototype, not yet a feature-complete commercial game. It has a WinForms graphical client (title/pause menus, HUD, world rendering, inventory, fullscreen map) plus a terminal UI adapter. Audio is synthesized in-process and played through the Windows waveOut API; volume and device settings persist. Saves restore world tiles and generation metadata, player and colonist state, body injuries, equipment/calibrations, running events and the explored world map. Live enemies and projectiles are not yet persisted, and there is no multiplayer state. The inventory has a craftable-recipes panel but no browse-all recipe browser; the content catalogue is broad but still smaller than the reference, and there is no engine adapter. The UI and simulation APIs are separated so those can be added incrementally. See [docs/UI_ARCHITECTURE.md](docs/UI_ARCHITECTURE.md) for screen, input, and persistence details.

## Research

Prior static research and its evidence boundaries are recorded in [docs/RESEARCH.md](docs/RESEARCH.md). The bounded architecture sheets cover [movement](docs/Terraria_Specs/Phase1_Physics_Player.md), [items](docs/Terraria_Specs/Phase2_ID_Item.md), [NPCs](docs/Terraria_Specs/Phase3_NPC_AI.md), [world liquids](docs/Terraria_Specs/Phase4_WorldGen_Liquids.md), [crafting](docs/Terraria_Specs/Phase5_Recipe_Item_Crafting.md), [placement](docs/Terraria_Specs/Phase6_Placement_GridConstraints.md), and [tile framing](docs/Terraria_Specs/Phase7_TileFrame_SmartFraming.md).
