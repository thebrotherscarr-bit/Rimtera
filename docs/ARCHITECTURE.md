# Rimtera: architecture map and development plan

Written 2026-11-05 against `feature/0.8.3-hardmode-research-events` (released: 0.8.2). This file maps the code as it stands and lays out the technical plan to 1.0 and beyond. It does not replace `docs/VISION.md` (what the game is) or `docs/PLAN.md` (the step table); it says how the code should carry them. Decisions are in `docs/adr/`.

## 1. What the game is, technically

An original 2D sandbox in C# (`net10.0-windows`, WinForms host, GDI+ rendering). One project, `Rimtera.csproj`, version `0.8.2`, about 99k lines. The simulation is deterministic and command-driven: a world's future depends only on its seed and the commands sent to its player slots. That property carries the whole design (replay, the CI determinism check, and the later co-op and online releases).

## 2. Code map

| Area | Where | Size | Role |
|---|---|---|---|
| Entry point | `Program.cs` | 120 lines | Flag router: `--graphics` (default), `--quickstart`, `--continue`, `--play`, `--menu`, `--demo`, `--self-test`, `--batch-a-test`, audits (`--craft-audit`, `--item-audit`), benches (`--engine-bench`), generators and exporters |
| Simulation core | `Systems/GameSimulation*.cs` | 57 partial files, `GameSimulation.cs` 2.4k lines | The one world object. `Tick` / `Advance` runs one fixed step; subsystems are partials: Player, Physics, Combat, Town, Colony Work, Research, Hardmode, Seasons, Raids, Relays, Power, Fishing, Items... |
| Storyteller | `Systems/Storyteller.cs`, `StoryEvents.cs`, `WorldEvents.cs`, `GameSimulation.StoryEvents.cs` | ~1.5k | Incident catalogue, threat budget, acts, warnings. Reaches the world only through `IStoryHost`, the best-factored seam in the code |
| Content tables | `Systems/Content/*`, `ItemSystem.cs`, `CraftingSystem.cs`, `NpcDropRegistry.cs`, `BiomeSpawnTables.cs` | ~10k | Items (`ArsenalItems.Data.cs` alone is 88 kB), recipes, balance curves, drops |
| World | `World/` | ~8.9k | `TileGrid`, liquids, physics, framing, placement, `LargeWorldGenerator.*` (terrain, decor, structures, rails) |
| Entities and people | `Entities/`, `Characters/`, `AI/` | ~8k | Actors, health and body conditions, pawn movement; backgrounds, relations, thoughts, mental breaks, colonist system; survival behaviour (~5.5k) |
| Presentation | `UI/` | ~20k | `GraphicalSandbox` (4.2k lines + 14 partials: the WinForms game window), `UiFramework` (retained UI tree with an `IUiRenderer` seam), `InventoryUi`, `MenuSystem`, sprite atlas and chunk caches, `LightMap`, `AudioEngine` (own synth over `waveOut`), save repository |
| Tests and audits | `Systems/*SelfTests.cs`, `*Tests.cs`, audits | ~90 files, ~19k lines | `CoreSelfTests.Run` orchestrates the suites with a shared `Check` callback; `tests/` holds only fixtures |
| CI | `.github/workflows/` | 3 files | `prove.yml` (build, provers in a matrix, determinism run twice), `version.yml` (tags from `<Version>`), `publish.yml` |
| Dev harness | `.opencode/agents/`, `opencode.json`, `AGENTS.md`, `CLAUDE.md`, `.clinerules/` | | An owner-run agent team (producer, designer, gameplay, balance, art, audio, qa, release, reviewer) on a free hosted model, with a local `gpt-oss:20b` subagent for mechanical edits |
| Design docs | `docs/` | 41 top-level docs | `VISION`, `PLAN`, `ROADMAP_0.2.0`, per-version roadmaps, parity diffs against Terraria, `UI_ARCHITECTURE`, `MULTIPLAYER`, `DEV_CONSOLE`, `SODA_TOOLS`, `ASSET_REQUESTS` |
| Not game code | `tools/`, `rea/`, `art/`, `publish/`, `bin/`, `obj/` | ~60k files | External repos, ComfyUI, asset scripts, generated art and builds (excluded from compile and mostly git-ignored) |

### Data flow in one line

`Program` → `GraphicalSandbox` (window, input, camera, render) → `GameSimulation.Submit(slot, input)` → `Advance()` → `WorldTicker` / `Storyteller` / colony and combat updates → `GameSnapshot` (versioned, optional-field forward compatibility) → `WorldSaveRepository`.

## 3. State of development

- Milestones 0.1 through 0.8.2 are done: foundations, Terraria parity, colony, people, storyteller, outposts and the power grid, the Fracture as hardmode, a performance pass with a late-game bench in CI.
- **In flight:** 0.8.3, hardmode research tiers and hardmode events. The working tree has four modified files (`ContentItems.cs`, `GameSimulation.Research.cs`, `ItemSystem.cs`, `World/TileGrid.cs`) and an untracked `docs/roadmap/0.8.3.md` spec. The research table already holds the four new tiers; the storyteller incidents from section 2 of the spec are not in yet (`HardScavengerRaid` and `HardRustMoon` do not exist as event kinds).
- **Ahead:** 0.8.4 zealots, 0.8.5 the four fuel cores; 0.9.0 the escape (collapse, ship, fuel-up siege, boarding, continue); 0.10.0 style and feel; 1.0.0 release; 1.1 local co-op; 1.2 online.

## 4. Findings that shape the plan

1. **Line endings.** `git ls-files --eol` reports `i/lf w/crlf` for the files checked, and plain `git diff` shows ~4,400 changed lines in the four modified files; ignoring end-of-line whitespace it is 20 lines. The real change is small and hidden under CRLF/LF noise. There is no `.gitattributes`. A noisy diff defeats review, the Prove path filter and model context.
2. **Research ladder overlap** (ADR-003): the draft's new tiers reuse existing unlock owners and one opening boss. Needs a rule and a test before release.
3. **The seam is conventional, not enforced** (ADR-001): the simulation is already free of WinForms, with one real production violation (`Systems/ItemAudit.cs`); headless CI still needs Windows because the single project targets `net10.0-windows`.
4. **Rendering is the 0.10 and 1.1 risk** (ADR-002): GDI+ with four viewports plus lighting and particles is unmeasured.
5. **Save format growth.** `GameSnapshot` v2 uses optional fields for forward compatibility. Each milestone adds more. `tests/fixtures/saves/` holds a single golden save, from 0.1.6; nothing from the 0.2 to 0.8 snapshot changes.
6. **Monolith ownership.** `GraphicalSandbox.cs` (4.2k), `GameSimulation.cs` (2.4k), `ItemSystem.cs` (2.3k) and `CoreSelfTests.cs` (1.7k) are the files every change touches. They are the hardest for local models and the likeliest to conflict.
7. **Harness mismatch.** The Rimtera project description speaks of Cursor and Ollama; the repository's actual harness is opencode with a hosted default model and an Ollama `gpt-oss:20b` subagent (`opencode.json`). Cursor can sit on top of the same repo, but `AGENTS.md` and `CLAUDE.md` rules (authorship by the owner only, no tool attribution) apply to any tool.

## 5. Plan

### Phase 0: this week, before more features
| # | Task | Why |
|---|---|---|
| 0.1 | Add `.gitattributes` (`* text=auto`, `*.cs text eol=lf` or crlf by owner's choice, binaries marked); renormalise in its own PR | Finding 1 |
| 0.2 | Commit the real 0.8.3 changes from the branch; keep the spec in `docs/roadmap/0.8.3.md` | Clean tree before refactors |
| 0.3 | Accept ADR-003 and fix the tier table; add the unlock-ownership invariant test and old-save tests | Finding 2 |
| 0.4 | Add the missing `WorldEventKind` values and the five hardmode incidents with refire times, console `incident <id>` commands, and `RunPacing` update | Completes 0.8.3 per its spec |

### Phase 1: 0.8.3 to 0.8.5
Ship in the order VISION §4.5 gives. Standing rules for every step (from `AGENTS.md`): dev-console commands in the same PR, `ASSET_REQUESTS.md` entries with placeholders, a self-test, `--craft-audit` and `--item-audit` clean, Prove green, version bump, `PLAN.md` updated. New in this plan: each step adds an entry to a short `docs/adr/` or `PLAN.md` note only when it makes a structural choice.

### Phase 2: structure, before the 0.9 sieges and ship
| # | Task | ADR |
|---|---|---|
| 2.1 | Extract `Rimtera.Core`, `Rimtera.Game`, `Rimtera.Tests` in small PRs; Linux headless CI | 001 |
| 2.2 | Add golden-save fixtures for every released snapshot version, loaded in a self-test | finding 5 |
| 2.3 | Split `CoreSelfTests` into discoverable suites with filtering and timings; keep the CLI flags as aliases | finding 6 |
| 2.4 | Begin carving `GraphicalSandbox.cs` along its existing partials into view classes | 002 |

### Phase 3: 0.9.x the escape
Build 0.9.1 to 0.9.5 on the storyteller seam (`IStoryHost`): the collapse, the ship as a research and build chain (`escape` tier owns the ship recipes, per ADR-003), the fuel-up siege as the climax incident, the escape screen and chronicle (History tab data), and continue-after-launch. Pacing: re-run the model after each step; target stays 10 to 14 hours on medium worlds.

### Phase 4: render spike and 0.10.x
Run the ADR-002 spike at the start of 0.9.5. Then 0.10.1 to 0.10.7 follow VISION §4.7, with `AssetCheck` extended into the "style check" that 0.10.2 calls for.

### Phase 5: 1.0.0
Balance pass across the whole run, save stability (all golden saves green), onboarding, the late-game bench budgets tightened, release.

### Phase 6: 1.1 and 1.2
1.1 follows `docs/MULTIPLAYER.md`: move per-player state (cooldowns, buffs, aggro, death screens) onto each player, add gamepad input and a join screen, per-viewport rendering, storyteller scaling by player count. 1.2: lockstep over the command log, world fingerprints for desync detection, join-in-progress from a save. Both are much safer after Phase 2.

## 6. Local-model loop (project goal)

The repository already has the harness. To make a local-model vibe-coding loop effective:
- Keep tasks inside one file or one partial; point the model at `docs/roadmap/<version>.md`, not at the whole of `docs/`.
- Use the existing split of hosted model for design and multi-file work, and `gpt-oss:20b` for the mechanical edits `opencode.json` lists. After ADR-001, `Rimtera.Core` plus its tests is a small enough context for a local model to build and prove headlessly.
- The loop's gate is the same as CI: `dotnet build`, `--self-test`, `--batch-a-test`, `--demo`, both audits.

## 7. Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| Refactor destabilises the release cadence | Medium | Link-only first, folder moves later; each PR keeps `main` releasable |
| GDI+ misses frame budgets at 4 viewports | Medium | Spike before 0.10 |
| Save-format drift breaks older worlds | Medium | Golden saves in CI |
| Research and pacing tables diverge | Medium | Invariant tests; `pacing` run per step |
| Scope: 1.0 needs every asset in style | High | Placeholders tracked in `ASSET_REQUESTS.md`; style check |

## 8. What I did not verify

I read the layout, `Program.cs`, project and CI files, the docs named above, the storyteller interface and the 0.8.3 diff. I did not build or run the game or its tests, or measure frame times. Line counts are `wc -l` on `.cs` files outside `tools/`, `rea/`, `bin/`, `obj/` and `publish/`.
