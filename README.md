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

No arguments (or `--graphics` / `--game`) opens the WinForms client at the title menu. From there: Single Player routes through character and world selection/creation; Continue resumes the most recently saved world; Load Game lists saved worlds; Options and Controls persist; Exit Game closes the app. While playing, `Esc` opens the pause menu with Resume, Inventory, Save Game, Load Game, Settings, Save & Quit to Main Menu, and Save & Exit Game. Menus use the arrow keys or the mouse (hover, click, and wheel); `WASD` remains reserved for gameplay. Character names, world names, and key bindings are edited in retained in-game fields and key-capture panels, not native Windows dialogs. `--menu` is the terminal equivalent; `--play` starts directly in a terminal world.

### Terminal gameplay controls

| Key | Action |
|---|---|
| `A` / `D` | Move left/right |
| `W` / `Space` | Jump |
| `X` | Dash |
| `1`–`0` | Select first ten inventory slots |
| `Tab` | Cycle to the next non-empty slot |
| `M` | Mine the facing tile with a selected pickaxe |
| `E` / `Enter` | Use the selected item (place, melee, or fire) |
| `R` | Replace the facing tile with the selected block (tool-gated; returns the old tile) |
| `[` / `]` | Cycle through currently craftable recipes |
| `C` | Craft the selected recipe (or the first available if none is selected) |
| `V` | Consult the guide for recipes unlocked by carried materials |
| `F` | Eat a berry |
| `B` | Summon a Stone Sentinel encounter |
| `K` | Open the colonist sheet |
| `I` | Open inventory UI |
| `Esc` | Pause / resume |
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
