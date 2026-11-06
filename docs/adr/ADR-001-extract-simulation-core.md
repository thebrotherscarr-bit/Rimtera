# ADR-001: Extract the deterministic simulation into its own project

**Status:** Proposed
**Date:** 2026-11-05
**Deciders:** Soda Poppensky (owner)

## Context

Rimtera is one project, `Rimtera.csproj`, targeting `net10.0-windows` with `UseWindowsForms=true`, about 99k lines of C# outside `tools/` and `rea/` (UI ~20k, simulation ~78k). It already has a clean *logical* layering:

| Layer | Folders | Lines (approx.) | Depends on |
|---|---|---|---|
| Simulation | `Systems/`, `Entities/`, `Characters/`, `World/`, `AI/` | ~78k, of which ~19k is `*SelfTests`/parity tests | no WinForms references; `System.Drawing` in only 5 files (benchmark and self-tests) |
| Hosts and presentation | `UI/`, `Program.cs`, `Systems/Console*.cs` | ~20k | the simulation |

`GameSimulation` is one `sealed partial class` spread over 57 files (`GameSimulation.*.cs`), driven only by `Tick`/`Advance` with player commands (`docs/MULTIPLAYER.md`). All randomness comes from seeded streams, and a command log replays a world exactly. The storyteller already talks to the game through an interface (`IStoryHost`), and that seam works well.

The boundary is only a convention. Evidence it is already leaking:
- `Systems/ItemAudit.cs` imports `Rimtera.UI`, and 20 files under `Systems/` do in total (console hosts, self-tests, asset checks).
- Because the simulation shares a Windows-only project, every CI job that builds the game runs on `windows-latest` (`prove.yml`: `runs-on: ${{ ... && 'windows-latest' || 'ubuntu-latest' }}`), and the headless provers (`--self-test`, `--batch-a-test`, `--demo`, the determinism check) pay for a Windows runner.
- 1.1 (split-screen co-op) and 1.2 (lockstep online) both depend on the rule that the simulation reads nothing but its seed and commands. Nothing but discipline enforces that today.
- `tests/` holds only fixtures. The roughly 90 self-test files live inside the shipping assembly and are launched by CLI flags.

Forces: one developer plus local and hosted models; model context is small, so smaller assemblies and clearer file ownership help; 0.9.0 is the last feature milestone, and 0.10.0 changes the renderer, which makes a clean seam valuable now rather than later.

## Decision

Split the solution in three stages, each releasable on its own:

1. **`Rimtera.Core`** (`net10.0`, no Windows APIs): `Systems/` (game logic), `Entities/`, `Characters/`, `World/`, `AI/`, content tables. No reference to `System.Windows.Forms`, `Rimtera.UI` or wall-clock time.
2. **`Rimtera.Game`** (`net10.0-windows`): `UI/`, `Program.cs`, audio, WinForms hosts, console hosts.
3. **`Rimtera.Tests`**: the self-test suites, run by `dotnet run`-style CLI flags as today *and* discoverable by a normal test runner.

CI's headless provers run against `Rimtera.Core` on `ubuntu-latest`. Windows runners remain only for the game build and publish.

Migration order: fix the one real layering violation (`ItemAudit`), move the console and asset tools out of `Systems/`, add `Rimtera.Core` with link-only compile includes first (no file moves), then physically move folders once the build is green.

## Options Considered

### Option A: Keep one project, enforce by convention
| Dimension | Assessment |
|---|---|
| Complexity | Low |
| Cost | None now; ongoing review burden |
| Scalability | Weak: the 1.1/1.2 determinism rules depend on discipline |
| Team familiarity | High |

**Pros:** no churn; no risk to the release cadence.
**Cons:** leaks keep happening (already 20 files); CI stays Windows-bound; the renderer change in 0.10 and the multiplayer work in 1.1 get no compiler-enforced seam.

### Option B: Three projects (Core, Game, Tests): recommended
| Dimension | Assessment |
|---|---|
| Complexity | Medium (one-off) |
| Cost | A few sessions; mostly mechanical moves |
| Scalability | Strong: the compiler enforces the seam; the Core can later run as a headless server for 1.2 |
| Team familiarity | High (plain `dotnet` projects) |

**Pros:** enforced determinism boundary; cheap Linux CI; a smaller assembly for local models to reason about; renderer swap (ADR-002) touches only `Rimtera.Game`.
**Cons:** `Rimtera.csproj` is the version source of truth that `version.yml` validates, so the workflows and `publish/` scripts must be updated together. Some `internal` access in self-tests needs `InternalsVisibleTo`.

### Option C: Rewrite the simulation as an ECS
| Dimension | Assessment |
|---|---|
| Complexity | High |
| Cost | Very high: 60k lines of tested rules |
| Scalability | Good in theory |
| Team familiarity | Low |

**Pros:** data-oriented, parallelisable.
**Cons:** throws away the working, tested, deterministic design; no problem we have is caused by the object model. Rejected.

## Trade-off Analysis

The decisive factor is that the architecture the owner already wants (command-driven, replayable, multiplayer-ready) is *only true because nobody has yet broken it*. B turns that into a compile error. Its cost is mechanical and front-loaded, and it can be done in small PRs that each keep `main` releasable. A is cheaper today but defers the same work to 1.1, when it would collide with the per-player-state refactor.

## Consequences

- Easier: headless and Linux CI; server-style hosting in 1.2; swapping the renderer; context-sized work for the local model (`Rimtera.Core` has no UI noise).
- Harder: touching version and publish plumbing; a few `internal` members become `public` or visible to tests.
- Revisit: after the split, decide whether `GameSimulation`'s 57 partials should become composed subsystems (`Research`, `Colony`, `Grid`, `Hardmode`...) behind small interfaces in the style of `IStoryHost`. That is not part of this ADR.

## Action Items
1. [ ] Commit or shelve the 0.8.3 work first; do not start the split with a dirty tree.
2. [ ] Remove the `using Rimtera.UI` from `Systems/ItemAudit.cs`.
3. [ ] Move `ConsoleHost/ConsoleMenu/ConsoleSandbox/AssetCheck` out of `Systems/` into the game project.
4. [ ] Add `Rimtera.Core.csproj` (`net10.0`), compiling the simulation folders; make `Rimtera.csproj` reference it.
5. [ ] Add `Rimtera.Tests`; keep `--self-test`, `--batch-a-test` and `--demo` as thin entry points.
6. [ ] Point `prove.yml` headless jobs at `ubuntu-latest`; keep Windows for build and publish.
7. [ ] Add a Core self-test that fails on any reference to `System.Windows.Forms`, `System.DateTime.Now` or an unseeded `Random`.
