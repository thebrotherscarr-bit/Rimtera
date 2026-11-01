# Multiplayer: the plan and the groundwork

Local split-screen co-op for up to four players comes in **1.1**, and networked play in **1.2** (`docs/VISION.md` §3.5).
0.3.11 lays the groundwork, so those releases add players instead of rewriting the game.

## What 0.3.11 put in place

| Piece | Where | What it does now | What it is for |
|---|---|---|---|
| **Player slots** | `GameSimulation.Commands.cs` | A world has `PlayerSlots` (1 for now, `MaxLocalPlayers` = 4) and `Players` by slot (slot 0 is `Player`). | 1.1 adds slots 1–3. |
| **Commands** | `PlayerCommand`, `Submit`, `Advance` | Players change the game only through commands: `Submit(slot, input)` queues one, and `Advance()` runs one fixed step with every slot's queued command. A slot that sent nothing stands still. A slot the world does not run is refused. `Tick(input)` is the single-player shorthand for both. | The same commands can come from a gamepad (1.1) or the network (1.2). |
| **Command log and replay** | `CommandLog`, `Recorder`, `Replay` | With a recorder set, every command the world runs is logged. The log saves as JSON, and replaying it on the same seed rebuilds the same world exactly (checked to the tile, the bag and every creature). | Lockstep networking: peers exchange commands, not world state. Desync checks compare fingerprints. Bug reports can carry a replay. |
| **Per-player camera** | `UI/PlayerCamera.cs` | Each player has a camera that follows them (eased with Camera smoothing, snapping on a teleport) and keeps its view inside the world. The window keeps one camera per local player. | Split-screen. |
| **Split-screen layout** | `SplitScreen.Viewports` | One player gets the screen; two stack top and bottom (wide views suit a side-scroller); three put player 1 on top and the others side by side; four take the quarters. | 1.1 draws each player's view in their viewport. |
| **Local players and devices** | `LocalPlayerSetup` | Player 1 plays on the keyboard and mouse. Others join on a free device (a gamepad) into the first free slot, up to four. A device drives one player, and player 1 always stays. The window sends player 1's commands through this. | 1.1's join screen. |
| **Per-player saves** | Character files; `PlayerPlacement` (PU-9) | Already in place: each character's bag and progress live in their own character file, and each world remembers where every character stood, their health and their bed. | Each co-op player loads their own character into a shared world. |

## Determinism rules
A replay (and lockstep later) only works if a world's future depends on nothing but its seed and its commands:
- Every random roll that changes the world comes from a stream the world owns, seeded from its seed. 0.3.11 moved the last
  two shared streams (wild plant and gem tree growth, lifebloom pods) onto the world ticker.
- Nothing in the simulation reads the wall clock; real time enters only as each command's step length.
- The replay check (`MultiplayerSelfTests`) runs inside the full core suite, after hundreds of other worlds, so shared state
  that leaks between worlds shows up there.

## What 1.1 still has to do
- Run several `PlayerCharacter`s in one world: the per-player state the simulation now keeps on `Player` (cooldowns, buffs, aggro, the respawn and death screens) moves onto each player.
- Gamepad input, and a join screen built on `LocalPlayerSetup`.
- Draw each player's view in their viewport, with their own HUD and inventory.
- Spawning, aggro and the storyteller's scaling by player count.

## What 1.2 adds on top
- Lockstep sessions: each peer sends its slots' commands for a step; every peer advances when it has them all. The command
  log is the protocol's payload, and world fingerprints catch desyncs.
- Joining a world in progress (send the save, then the commands since).
