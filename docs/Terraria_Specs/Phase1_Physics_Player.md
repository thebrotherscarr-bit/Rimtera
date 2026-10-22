# Phase 1 — Terraria.Player / physics

## 1. Equations and data variables

| Variable / relation | Observed value or rule | Units / scope |
|---|---|---|
| `maxFallSpeed` | `10` baseline | pixels/update; later modifiers apply |
| `gravity` | `0.4` baseline | velocity increment/update; ordinary dry path |
| `jumpHeight` | `15` | jump eligibility counter, not world-space height |
| `jumpSpeed` | `5.01` | pixels/update |
| `runSpeed` | `3` baseline | pixels/update |
| `runAcceleration` | `0.08` baseline | velocity approach/update |
| `runSlowdown` | `0.2` baseline | velocity reduction/update |
| Vertical integration | `vY[n+1] = clampFall(vY[n] + gravity * gravDir, maxFallSpeed)` | ordinary dry path; exact clamp helper expression not in source notes |
| Held jump | `vY = -jumpSpeed * gravDir`; consume one positive jump-counter step | per update while eligible and held |
| Horizontal run | approach signed `runSpeed` limit by `runAcceleration`; reverse direction applies `runSlowdown` first | per update; exact branch ordering varies by movement mode |
| No-input air drag | `runSlowdown / 2` | per update, ordinary airborne path |
| Dash launch | `vX = direction * selectedDashSpeed`; observed branch values `{16.9, 14.5, 21.9, 16}` | pixels/update; type-to-value mapping not included |
| Wing lift | selected states alter vertical velocity by `0.2–0.4` | pixels/update; ability-specific |
| Grapple pull | `pull = capMagnitude(targetVector, limit)`; default cap `11`, selected hooks `12–16` | pixels/update |
| Collision subdivision | `stepLimit = min(16 px, hitbox dimensions)` | unusually large movement split into substeps |

## 2. Step-by-step state conditional logic array

```text
[0] Update: reset baseline movement values; apply liquid, depth, debuff, gravity, equipment, and mount modifiers.
[1] Jump gate: require grounded/sliding or ability-specific eligibility; check release/state, water, mount, and double-jump flags.
[2] If jump held AND jump counter > 0: assign vY = -jumpSpeed * gravDir; consume counter.
[3] Horizontal input: accelerate toward current signed run-speed limit; if reversing, apply slowdown path first.
[4] No horizontal input: approach zero using ground slowdown; ordinary air path uses half slowdown.
[5] Dry vertical path: add gravity * gravDir; clamp to current fall-speed limit.
[6] Dash: validate dash type/delay/time; on activation set branch-specific velocity; later ticks damp and end on timer/collision rules.
[7] Wings: validate wing ability/time; apply lift/drag and vertical cap; decrement wingTime (normally 1/update; selected hover state 0.5/update).
[8] Grapple: collect valid anchors; average target positions; derive capped pull vector; jump may detach or launch away.
[9] Tile movement: invoke shared collision with hitbox/platform/gravity options; subdivide large displacement; resolve step-up/down, tile/water collisions, slopes, and conveyors.
```

## 3. Component property matrix

| Component | Properties / state variables | Tick-rate / multiplier | Coverage |
|---|---|---|---|
| Horizontal movement | `velocity.X`, direction, run limit, acceleration, slowdown | once/update; air no-input slowdown `0.5×` ground baseline | baseline plus selected branches |
| Vertical movement | `velocity.Y`, `gravity`, `gravDir`, `maxFallSpeed` | once/update; equipment/environment modify baselines | baseline plus modifiers |
| Jump | `jumpHeight`, `jumpSpeed`, release/eligibility, double-jump flags | counter consumed per eligible held update | state contract only |
| Dash | dash type, `dashDelay`, `dashTime`, launch velocity | timer-driven; observed launch speeds listed above | selected values, not type catalog |
| Wings | `wingTime`, lift/drag, vertical cap | usually 1 wing-time unit/update; selected hover 0.5 | selected behavior |
| Grapple | anchor set, mean target, pull vector, magnitude cap | per movement update; cap range listed above | structural behavior |
| Collision | hitbox, displacement, platform/gravity options, slope/conveyor state | substeps bounded by `min(16 px, hitbox dimensions)` | solver call structure |
| Input corpus | `docs/Decompiled_Terraria` | unavailable at generation time | facts transcribed from existing `docs/RESEARCH.md`; not file-by-file extraction |
