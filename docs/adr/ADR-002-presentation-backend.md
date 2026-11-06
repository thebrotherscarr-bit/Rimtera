# ADR-002: Decide the presentation backend with a spike before 0.10.4

**Status:** Proposed (decision deferred to the spike's result)
**Date:** 2026-11-05
**Deciders:** Soda Poppensky (owner)

## Context

The game renders through a WinForms host: `UI/GraphicalSandbox.cs` (4,212 lines, plus 14 partials) draws with `System.Drawing` onto a double-buffered control (`OnPaint`, `Bitmap`, `Graphics`), with custom chunk caching (`GraphicalSandbox.Chunks.cs`), a `LightMap` (674 lines) and a `SpriteAtlas`. `docs/UI_ARCHITECTURE.md` states plainly: no GPU backend. Audio is hand-rolled over Windows `waveOut` (`AudioEngine.cs`, 1,640 lines).

What is coming:
- **0.10.4** "The UI drawn from art": *the renderer changes to draw the UI art rather than code shapes.*
- **0.10.7** particles, lighting, screen shake, final mix.
- **1.1** up to four split-screen viewports, each with its own camera, HUD and inventory.
- 0.8.2 already needed a performance pass and a late-game bench in CI, so there is a measured budget to compare against.

GDI+ is CPU-bound. Four viewports of tiles, lighting and particles is the point where it is most likely to fail the existing frame budgets, and it is also the first moment where a renderer change would be cheapest (before 0.10 produces a lot of new draw code).

## Decision

Do not choose now. Run a time-boxed **render spike** at the start of 0.9.5 (before 0.10 work begins) that renders the same late-game scene (the 0.8.2 bench world) four ways and records the frame time at one and at four viewports:

1. current GDI+ path, as is;
2. GDI+ with targeted fixes (dirty-chunk redraw, pre-lit tile chunks, particle pooling);
3. a GPU 2D backend behind a small `IRenderer` seam (candidates: MonoGame or Silk.NET);

If option 2 holds the budget at four viewports with headroom for 0.10's effects, stay. Otherwise adopt option 3 and do it before 0.10.4.

## Options Considered

### Option A: Stay on GDI+ and optimise
| Dimension | Assessment |
|---|---|
| Complexity | Low |
| Cost | Low, incremental |
| Scalability | Uncertain at 4 viewports plus lighting and particles |
| Team familiarity | High |

**Pros:** no rewrite; ships on the current cadence; keeps WinForms menus and tooling.
**Cons:** a hard ceiling; effects work in 0.10 may be spent fighting it.

### Option B: GPU 2D backend behind an `IRenderer` seam
| Dimension | Assessment |
|---|---|
| Complexity | High |
| Cost | Large: ~20k lines of `UI/` touch drawing; the menu framework is retained-mode and already renderer-independent (`IUiRenderer`) |
| Scalability | High |
| Team familiarity | Low |

**Pros:** headroom for light, particles and split-screen; cross-platform path.
**Cons:** big surface; the audio and input hosts change too; risks the release rhythm.

### Option C: Do nothing until a problem appears
Cheapest today, most expensive in 0.10 and 1.1. Rejected.

## Trade-off Analysis

`UiFramework` already exposes an `IUiRenderer` adapter, so a backend swap is *possible* without touching the menus' logic; what is not yet abstracted is the world and sprite drawing in `GraphicalSandbox`. The spike buys a number instead of an opinion. ADR-001 should land first, so the spike changes only the game project.

## Consequences

- Easier: a data-backed choice; 0.10 asset work (ADR-style `docs/ASSET_REQUESTS.md`) is unaffected either way.
- Harder: a spike costs a few sessions at a point where feature work is tempting.
- Revisit: after the spike; record the numbers in this ADR and flip its status.

## Action Items
1. [ ] Extend `LateGameBench` with a four-viewport scene and record frame-time percentiles.
2. [ ] Extract world and sprite drawing behind `IRenderer` (or a narrower `IWorldView`) as part of the spike, even if GDI+ wins.
3. [ ] Write the results into this file and mark it Accepted or Superseded.
