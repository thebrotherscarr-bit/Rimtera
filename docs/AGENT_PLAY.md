# Agent play: an AI player in the live game, with you watching

`dotnet run -- --agent-play` opens the normal Rimtera window in a fresh throwaway world and lets an outside AI player drive it.
The local model (`steward:1`) sees the game as text (and a screenshot on request), sends short commands, and writes findings to
`docs/playtests/`. You watch the window. `tools\local-ai\Play-Agent.ps1` starts both.

## How it works

| Piece | Where | Job |
|---|---|---|
| `AgentObserver` | `Systems/AgentObserver.cs` | Read-only text view: time, health, inventory, craftable recipes, nearby creatures, an ASCII map with a legend. Pure; no UI or clock, so it is safe in the deterministic core. |
| `AgentBridge` | `UI/AgentBridge.cs` | A tiny HTTP server on `127.0.0.1:8780` (loopback only). Requests are queued and served on the window's own tick, so the simulation stays single-threaded. |
| Window hooks | `UI/GraphicalSandbox.cs` (3 small edits), `UI/GraphicalSandbox.Agent.cs` | Serve requests each tick; **pause the world unless an agent command is running**; replace the player's input with the agent's. Screenshots use `DrawToBitmap` on the canvas. |
| `--agent-play` | `Program.cs` | Starts the bridge, then the quickstart path (`--size`, `--kit` still work). |
| Play tools | `tools/local-ai/rimtera-mcp/server.mjs` | `play_observe`, `play_act`, `play_select`, `play_craft`, `play_console`, `play_screenshot`, `play_note` for any MCP client (opencode shows them as `rimtera_play_*`). |
| `playtester` agent | `.opencode/agents/playtester.md` | The playing persona: short bursts, notes with coordinates, no edits, no shell. |

The world only advances while a command runs (`frames` ticks, 60 a second), so a slow model never leaves the player standing in a
fight. Every command goes through `Submit`/`Advance`, so the game's command log can record and replay the session.

## Use it

```
.\tools\local-ai\Play-Agent.ps1                                   # early-game playtest, 20 minutes
.\tools\local-ai\Play-Agent.ps1 -Goal "Test the grid: build a relay and fuel it" -Minutes 30 -Kit arsenal
```

Stop any time: create a file named `.agent-stop` in the repo root, or close the window. Your saves are untouched: the playtest
world uses its own data folder (`logs\playtest-data`). The dev console is off in a session unless you pass `-AllowConsole`,
so the agent plays with its hands rather than giving itself items.

## From findings to fixes

The agent writes `docs/playtests/<date>-<session>.md`: one line per finding (`bug`, `balance`, `ux`, `idea`, `progress`), with tile
coordinates and tick. Hand a finding to the coding loop:

```
.\tools\local-ai\Run-Agent.ps1 -Task "Fix this bug from docs/playtests/<file>: <the line>"
```

`Run-Agent.ps1` works on an `agent/` branch, runs the full gate itself, and never pushes. You review and open the PR.

## HTTP routes (for other tools)

`GET /health`, `POST /observe {half_width, half_height}`, `POST /act {move, jump, down, action, target_x, target_y, frames}`,
`POST /select {slot}`, `POST /craft {id}`, `POST /console {line}`, `GET /screenshot`. Plain text in and out (PNG for the screenshot).
Set `RIMTERA_AGENT_PORT` for the MCP server if you change the port.

## Limits

- Loopback only and unauthenticated: anything running on this PC can drive the game while `--agent-play` is open. It is a dev tool.
- The text map shows terrain, creatures and the guide; it does not show every town NPC, item on the ground or UI panel. Use
  `play_screenshot` (needs a vision-capable model) or the console for those.
- The agent plays at the speed of the model; a step is several seconds. That is why the world waits.
- A small model sometimes answers with the game state instead of an action; the loop answers that with a correction and the
  turn is lost. If a session strings many corrections together, the model is too small for the session, not the bridge.
