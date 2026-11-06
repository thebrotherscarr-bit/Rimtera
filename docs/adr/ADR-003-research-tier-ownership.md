# ADR-003: One owner per unlock in the research ladder

**Status:** Proposed (blocks the 0.8.3 release)
**Date:** 2026-11-05
**Deciders:** Soda Poppensky (owner)

## Context

`GameSimulation.ResearchTiers` (`Systems/GameSimulation.Research.cs`) is the data table that ties each boss to a research tier, its study time, the stations it unlocks and the materials whose recipes it gates. The 0.8.3 work in progress on branch `feature/0.8.3-hardmode-research-events` appends four tiers (`lumenite`, `voidsteel`, `astral2`, `escape`). Read against the six existing tiers:

| New tier | Opened by | Unlocks | Already unlocked by |
|---|---|---|---|
| `lumenite` | `WarshipCore` | `VoidForge`; gates `PrismEssence` | `flora` unlocks `VoidForge`; `essences` gates `PrismEssence` |
| `voidsteel` | `MutatedFlora` | `AstralAssembler`; gates `BlightEssence` | `astral` unlocks `AstralAssembler`; `essences` gates `BlightEssence`; `flora` already uses `MutatedFlora` |
| `astral2` | `VoidCantor` | `StarWeaver`; gates `FlightEssence` | `essences` gates `FlightEssence` |
| `escape` | `AstroGod` | (none) | `astral` is also opened by `AstroGod` |

So several stations and materials would have two owners, and two tiers share one opening boss. The draft (`docs/roadmap/0.8.3.md`) records this as the designer's own assumption, but it is also a correctness question: which tier's completion actually unlocks the Void Forge? Older saves treat a tier as done when its boss is defeated, so the new tiers would complete silently on load.

## Decision

Adopt a single rule and enforce it with a self-test: **every unlockable (station, gated item) is owned by exactly one tier, and every boss opens at most one tier at a time on the critical path.** Where the draft overlaps, reassign instead of duplicating:

- Move `VoidForge` ownership to `lumenite` (or leave it on `flora` and give `lumenite` only its new materials), not both.
- Move `AstralAssembler` the same way between `astral` and `voidsteel`.
- Give `escape` a distinct opening condition (the Astro-God's *core* delivered, not only its defeat), as 0.9.2 intends for the ship.
- Keep gated materials in one tier's list only.

## Options Considered

### Option A: Ship the draft as is
| Dimension | Assessment |
|---|---|
| Complexity | Low |
| Cost | None now |
| Scalability | Poor |
| Team familiarity | High |

**Pros:** done already. **Cons:** ambiguous unlock logic, double study time for one result, silent completion on old saves.

### Option B: Reassign ownership and add an invariant test: recommended
| Dimension | Assessment |
|---|---|
| Complexity | Low |
| Cost | A small edit plus one self-test |
| Scalability | Good: the ladder stays correct as content grows |
| Team familiarity | High |

### Option C: Make the ladder a data file with a validator
Better long term (content in JSON, validated at load), but larger than 0.8.3 needs. Revisit in 0.10.

## Trade-off Analysis

B costs about an hour and removes a class of bug for every later tier (0.8.4, 0.8.5, 0.9.x). A leaves the pacing model (`RunPacing`, 12.4 h modelled) out of step with the table.

## Consequences

- Easier: reasoning about unlocks; the pacing model and the table agree.
- Harder: the draft's pacing check (27 min of research at base rate) must be recomputed.
- Revisit: moving `ResearchTiers` to data (Option C) when content volume warrants it.

## Action Items
1. [ ] Decide ownership of `VoidForge`, `AstralAssembler` and the three essences, then edit the table.
2. [ ] Add a `ResearchSelfTests` invariant: no (station | item) appears under two tiers.
3. [ ] Add old-save tests: loading a save past each boss yields the expected tiers (`RestoreColonyResearch`).
4. [ ] Re-run `pacing` and update the modelled run length.
5. [ ] Add the missing `WorldEventKind` values the draft refers to (none of `HardScavengerRaid` / `HardRustMoon` exist in code yet).
