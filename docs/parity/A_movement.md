# Parity A: movement and physics

Probes: `Systems/ParityMovementTests.cs` (run from `CoreSelfTests` via `ParityMovementTests.Run(Check)`; check names start with `A<n>`).
Each probe builds a 96x56 world, shapes cells directly, drives `GameInput` ticks at 60 Hz and asserts behaviour, not exact numbers.
Tuning is Rimtera's own; no Terraria code, constants or tables were used.

| # | Behaviour | Status | Test (check name prefix) | Notes |
|---|-----------|--------|--------------------------|-------|
| A1 | Run accelerates to a cap; release brakes faster than accelerating; reversing brakes before turning | FIXED | `A1 run accelerates...` | Reversing used plain acceleration (24), which was slower than coasting to a stop. Now reversing adds ground friction to the acceleration. Player ground friction went from 28 to 40, so stopping (~0.12 s) is clearly faster than reaching top speed (~0.2 s). Above the cap (after a dash, or stepping onto slow ground) the pawn eases down at friction rate. |
| A2 | Ice slides; mud, nectar blocks and webs slow you; speed bonuses multiply | FIXED | `A2 ice...`, `A2 mud, nectar blocks and webs...`, `A2 run-speed bonuses...` | No surface effects existed before. New `TilePhysics.SurfaceGrip`: ice-type ground (Snow, shown as "Ice", plus Glowing ice, Vine-veined ice and Thin ice) has 0.12 grip. New `SurfaceSpeedFactor`: Mud 0.75, Nectar block 0.45. Mite silk webs (`IsWeb`) cap run speed at 0.3x, cap the fall at 1.5 tiles/s and smother jumps. Speed multipliers already multiplied the top speed (OK). |
| A3 | Held jump lifts longer up to a cap; early release is shorter; no mid-air jump without gear; a jump buffered on landing still fires | FIXED | `A3 holding jump...`, `A3 no jumping in mid-air...`, `A3 a jump pressed just before landing...` | Variable height, the time cap and the mid-air rule were already correct. Added a 0.1 s jump buffer: a press in the air fires on landing while jump is still held. A press already spent on a double jump, wall jump or hook release no longer queues one. |
| A4 | Gravity reaches a terminal speed; fall damage starts past a safe height and grows; featherfall and liquid negate it | OK | `A4 gravity...`, `A4 fall damage starts...`, `A4 featherfall gear and landing in liquid...` | Existing: 25 safe tiles, then damage scaled per tile; featherfall, flight gear, the mount, liquids and a latched hook negate it. |
| A5 | 1-tile steps auto-climb; slopes are smooth; half blocks are half steps; ceilings stop jumps without sticking | FIXED | `A5 running into a 1-tile step...`, `A5 slopes...`, `A5 half blocks...`, `A5 ceilings...` | Step-up always lifted a full tile and left the pawn hovering over a half block. It now settles onto the step's real surface, so a half block is a half step. Full steps, slopes and ceilings were already correct. |
| A6 | Stand on platforms, jump up through them, hold down to drop | OK | `A6 ...` (3 checks) | Existing one-way platform collision. |
| A7 | Up/down climb ropes; jump leaves; hang without falling | OK | `A7 ...` (3 checks) | Existing climbing state. |
| A8 | Water slows; held jump swims up; breath drains only with the head under; lava burns and slows; exit boost | FIXED | `A8 water slows...`, `A8 breath drains...`, `A8 holding jump swims...`, `A8 leaving liquid...`, `A8 lava...` | There was no exit boost: swimmers could not get over a 1-tile lip. Now, when the body leaves a liquid while swimming upward with jump held, the pawn gets `LiquidExitBoost` (10.5 tiles/s up). The other parts were already correct. |
| A9 | Double jump, rockets and wings use their own fuel/time and refill on landing; dash by key or double tap, then a cooldown; hook pulls and lets you jump off | FIXED | `A9 double jump and rocket fuel...`, `A9 wing time refills...`, `A9 the dash key...`, `A9 double-tapping...`, `A9 a grappling hook...` | Wing energy used to refill slowly on the ground; it now refills on landing. Dash had no cooldown and could be chained back to back; it now has a 0.45 s cooldown after it ends. Double-tapping a direction within 0.25 s now dashes (player only; AI pawns leave `DoubleTapDashWindow` at 0). The double jump, rocket fuel and hook were already correct. |
| A10 | Hits knock you away from the source; immunity frames block repeats and flash | FIXED | `A10 a hit knocks you back...`, `A10 immunity frames...` | Knockback direction and the immunity window were already correct. The player flashed for only 0.15 s after a hit; `ActorAnimator` now blinks the player for the whole immunity window (one line). |
| A11 | You can't leave the world's edges | OK | `A11 ...` | Out-of-world cells count as solid in collision. |

Totals: 4 OK (A4, A6, A7, A11), 7 FIXED (A1, A2, A3, A5, A8, A9, A10), 0 DIFF.

Small intentional differences inside the FIXED rows:
- Rimtera gives every player a dash key (no accessory needed). Terraria needs dash gear. Double tap was added as a second trigger.
- Snow is Rimtera's "Ice" block, so it is slippery.
