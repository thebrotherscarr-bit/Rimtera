# Rimtera

Rimtera is an original, engine-neutral 2D sandbox game prototype written in C#. Its current playable slice composes world generation, tile placement/mining, inventory, movement, weapons/projectiles, autonomous enemies, a phased boss, survival needs, world events, and local liquid simulation into one deterministic fixed-step runtime.

## Features (0.2.0)

Terraria is the baseline for feel and base mechanics; Rimtera adds a colony layer, a station AI and a storyteller. Terraria is used as behavioural evidence only: no code, names or assets are copied. What is still missing is listed in [docs/TERRARIA_GAPS.md](docs/TERRARIA_GAPS.md) and planned in [docs/PLAN.md](docs/PLAN.md).

- **Movement:** Terraria's run, jump, gravity and fall numbers; double jump, wall jump, rocket boots, wings, hooks, ropes, platforms, slopes. Swimming needs fins (the Diver's kit). Six mounts, each with its own fall rule.
- **Clock:** Terraria's 24-minute day (15 minutes of day, 9 of night). Buffs, regeneration and needs run on real time.
- **Stakes:** death drops by character difficulty (Explorer keeps everything; Easy, Medium, Hard; Ironman is the impossible run, with a ghost camera after death). Town NPCs and crew die for good, and a storyteller arrival stream brings new people. The station AI is the colony's heart: if it dies, the run ends. A run chronicle records the story and is shown on the end screen.
- **Combat:** creature defense, immunities and lava; 77 creatures inflict debuffs; weapon afflictions; box contact and ±15% hit variance; a four-tier healing and charge ladder with 60 s potion sickness; boss potions and hearts; heart and charge pickups; drop chances and conditions; stacks of 9999. Combat text, block dust and death gore, and a sound for every action.
- **World:** seeded worlds in three sizes with biomes, a dungeon, a temple, sky islands, six liquids, flora that grows and spreads, wiring, paint, glide-walks, and the Fracture as hardmode. World creation has a Hazard row.
- **Content:** 33 bosses and 9 event mini-bosses, 330 hostile creature kinds, 102 critters, 1,990 items, events and weather, fishing, the bestiary, feats, banners, trophies, golf and kites.
- **Town and colony:** town NPCs with housing, happiness and shops (sell any stack, buy back the last ten sales); crew with skills, moods and needs.
- **Inventory:** Terraria's 50-slot bag with coin and ammo slots, trash, favourites, loadouts, the Cargo Pod bank, and pet, light pet, mount and hook slots.
- **Presentation:** generated creature animation, biome, event and boss-tier music, ambience beds, coloured lighting on High quality.
- **Saves:** separate world and character files; autosave every 2, 5, 10 (default) or 15 minutes, or off; the bed sets the spawn point. Creative characters play only Creative worlds.

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

- **Inventory**: ten columns by five rows, the top row being the hotbar (numbered 1-0). Left click picks up, places or swaps; right click takes one item onto the cursor (hold to keep taking, faster and faster) or equips gear; **Shift+click** moves a stack to or from the open chest; **Ctrl+click** trashes; **Alt+click** marks a favorite, which sort, deposit, quick stack, trash and throw leave alone. The world keeps running while the inventory is open (unless Autopause is on): you can move, jump, use the held item, or use the item **on the cursor** by clicking the world, and **right-clicking the world throws the cursor item**. Closing the inventory puts a carried item back in the bag (or at your feet if the bag is full).
- Beside it: **Sort**, **Quick stack** (to nearby chests), the **Cargo pod** when you have one, the **Wires** builder toggle, and the **trash slot** (drop a held stack on it; click it empty-handed to take the stack back, or Shift-click it to send it back to the bag).
- **Chests** open underneath with **Loot all**, **Deposit all**, **Quick stack**, **Restock**, **Sort** and **Rename**.
- **Crafting** underneath that: everything craftable nearby as a grid; click a result to see its materials, click it again to craft; the wheel scrolls. **Ask station AI** suggests what your materials make.
- Beside the bag: four **coin slots** and four **ammo slots**, as in Terraria. Credits and ammunition go there first; weapons fire from the ammo slots before the bag.
- Down the right: tabs for **Gear** (armor, accessories, vanity, and the pet, light pet, mount and hook slots), **Housing** (check the room you stand in; residents with role and happiness, Move here, the crew and empty roles), **Crew**, **Bestiary**, **Feats** and **Emotes** (click an emote to show it over your head), plus **Loadouts 1-3**.
- **Buffs:** right-click a helpful buff to cancel it (not debuffs). Tooltips compare armor with what you wear, and weapons and drills with what you hold.
- **Shops:** sell any carried stack (not favorites); the shop keeps a buyback list of your last ten sales.
- **Settings** in the bottom-right corner.

Right-click a **bed** to sleep and the **station AI** for guidance, as with Terraria's beds and Guide.

### Graphical gameplay controls

Defaults are Terraria 1.4.5.8's PC layout (see `docs/CONTROLS_PARITY.md`), and every key can be rebound under Settings → Controls (crafting, crew, bestiary, feats, sleep, housing, wires and eating start unbound since the inventory and right clicks cover them, but each can take a key) (Shift, Ctrl and Alt included; a key taken from another action moves and the screen says so). The `/` key toggles an on-screen key reference built from the current bindings. One list, `ControlCatalog` in `UI/PreferencesAndProfiles.cs`, drives gameplay input, the controls screen and that reference.

| Key | Action |
|---|---|
| `W` `A` `S` `D` | Up / climb, left, down / drop through platforms, right |
| `Space` | Jump (also lets go of ropes) |
| Double-tap `A` / `D` | Dash (a dash key can be bound too) |
| Left mouse | Use the held item (items without auto-reuse need a fresh click; clicks on the HUD or a panel never swing); placing a block on another swaps it |
| Right mouse | Interact: doors, chests, stations, people, beds (sleep), the station AI; with an item on the cursor, throw it |
| Mouse wheel / `1`–`0` | Hotbar (with the inventory open the wheel scrolls the crafting list; on the full map it zooms) |
| `Esc` / `I` | Inventory hub: crafting, chests, gear, crew, housing, bestiary, feats, settings |
| `E` | Grappling hook (each press fires a hook; jump to let go) |
| `T` | Throw the held item (not favorites) |
| `Shift` (hold) | Auto-select: the tool for the block under the cursor (drill, cutter, demolisher), or a torch over empty air |
| `Ctrl` | Smart cursor (toggle or hold, per Settings → Cursor) |
| `R` | Quick mount |
| `H` / `J` / `B` | Quick heal / charge / buff |
| `M` / `Tab` | Full map (WASD pans it) / map style |
| Numpad `+` / `-`, `PgUp` / `PgDn` | Map zoom, map opacity |
| `=` / `-` | Camera zoom (also Settings → Zoom) |
| `F1` / `F2` / `F3` | Loadouts 1-3 |
| `F8` | Cycle frame skip (On, Subtle, Off) |
| `F9` | Cycle lighting (Low, Standard, High, Off); High tints light by colour |
| `F10` / `F11` / `Alt+Enter` | Frame rate / hide the interface / fullscreen |
| `C` | Creative menu (creative worlds) |
| `` ` `` (hold) + `1`–`8` | Emotes (or the Emotes tab in the inventory) |

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

## Release notes

Release numbers follow [docs/PLAN.md](docs/PLAN.md). The parity roadmap ([docs/ROADMAP_0.2.0.md](docs/ROADMAP_0.2.0.md)) uses its own step numbers; its status block says which parity step shipped in which release.

- **0.2.0 Baseline release** (in progress). CI gates: `--asset-check` (a missing sprite fails Prove) and soft bench budgets. Coloured lighting on High quality. The Emotes tab. `F8` and `F9` cycle frame skip and lighting. Ironman spectate: after an Ironman death a ghost camera stays on the world until `Esc`. The craft audit's tier-4+ before-the-Fracture list is held to a whitelist with reasons. Docs pass (this file, the parity docs, the breakdown status columns). Still to come before release: the boss and difficulty balance pass, and the D3 play-through below.
- **0.1.15 Combat core.** Creature defense tiers, immunities and lava; 77 creatures inflict debuffs; weapon afflictions; box contact, ±15% hit variance; the healing ladder (50/100/150/200 plus a restoration stim), 60 s potion sickness, boss potions and hearts, heart and charge pickups, drop rules, stacks of 9999; sound events, silent despawns, dust and gore, combat text and the item tooltip.
- **0.1.14 Movement.** Terraria's movement numbers, per-mount falls, fins for swimming, camera snap with a smoothing setting, Terraria's pause rules.
- **0.1.13 Creative split, Hazard row, autosave interval.**
- **0.1.12 Fall damage, air and the bed.** 10 damage per tile beyond 25; about 23 s of air; the bed sets the spawn point.
- **0.1.11 Clock.** Terraria's 24-minute day with real-time statuses; events end at dawn or dusk; Terraria's respawn timer.
- **0.1.10 Coin and ammo slots.**
- **0.1.9 Terraria basics.** Buff cancel, tooltip comparison, the housing page, pet, light pet, mount and hook slots, selling and buyback.
- **0.1.8 Stakes.** Death drops by difficulty, permadeath for town NPCs and crew with storyteller arrivals, the station AI's heart, the run chronicle and end screen. Also the music and ambience work that landed after 0.1.7 (streamed music, the music player, ambience beds).
- **0.1.7** The crafting chain fix and `--craft-audit`, crafting guidance, controls parity phase 2, no automatic eating or healing for the player, and the generated creature animation frames.

### Save compatibility

Worlds and characters saved by any 0.1.x release load in 0.2.0. The world format is still version 2: every field added since then is optional, and a missing field takes its default (for example, the coin and ammo slots start empty and the misc slots start unequipped). This pass did not load real saves from each release; D3 item 8 checks a 0.1.7 save by hand.

### 0.2.0 sign-off (D3)

A person signs this off; the sign-off goes in the release PR. See `docs/roadmap/0.2.0.md` D3 for the full list. Two runs on Release builds, without debug keys: Classic on a Medium world, and Brutal on a Small world, each with a new character.

- [ ] Opening: the kit, a tree, the Fabricator, the guidance tips; a 24-minute day.
- [ ] Early game: each drill opens new ore; the healing tiers unlock in order; the Overseer arrives and leaves at dawn.
- [ ] Pre-hardmode: the Rust Moon, a raid, the Mechanic, the dungeon, the Fission Tyrant.
- [ ] Hardmode: the scars, altar ore, the trio, Mutated Flora, the temple, the Colossus.
- [ ] Endgame: the obelisks, the Astro-God, life reaches 500.
- [ ] Colony: crew live at home and never touch the player's inventory or heal the player.
- [ ] Presentation: sounds, event skies, boss music, visible armour, memory within budget on Large.
- [ ] Saves: autosave, closing the window saves, a 0.1.7 save loads.

Some D3 items depend on work that is not done yet (event skies, visible armour, per-boss patterns, crew homes, life to 500, the temple seal); see [docs/ROADMAP_0.2.0.md](docs/ROADMAP_0.2.0.md). Expect those boxes to fail until it lands.

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
- `Systems/RegionMap.cs` flood-fills open cells into regions/rooms; `Systems/TemperatureModel.cs` derives outdoor and per-room temperatures (lava heats, water cools, Oxygen units stabilise) that drive comfort and exposure; `Systems/Storyteller.cs` is the hourly incident director (raids, cargo drops, cold snaps/heat waves, toxic fallout, solar flares). It holds off for the first three days and keeps raids until the first boss is down; crew members arrive like town NPCs, into free housing (`CrewCapacity` in `Systems/GameSimulation.Town.cs`).
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
