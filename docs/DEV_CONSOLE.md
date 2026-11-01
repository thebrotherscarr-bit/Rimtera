# The dev console

Added in 0.3.6 (`docs/VISION.md` §6).

- **In the game:** press `` ` `` (the key under Esc). It opens a dark band across the top of the screen.
  - Type a command and press Enter.
  - Up and Down walk through earlier commands.
  - Esc or `` ` `` closes it.
  - The world keeps running while it is open, but none of your keys reach the game.
- **In tests:** `new DevConsole(game).Execute("…")` runs the same commands (`Systems/DevConsole.cs`,
  `Systems/DevConsoleSelfTests.cs`).
- **Names:** item, creature, NPC, event and status names match by id or display name, ignoring case, spaces and
  dashes. `med stim`, `MedStim` and `med-stim` are the same. A name that doesn't match lists the closest ones.
- **Marking:** any command that changes the game sets the `dev.console.used` flag, so a run that used it is marked.

| Command | What it does |
|---|---|
| `help [command]` | Lists the commands, or explains one |
| `find <text>` | Searches items, creatures, town NPCs, events and statuses by name |
| `give <item> [count]` | Puts items in your inventory (`give credits 500`) |
| `spawn <creature> [count]` | Spawns creatures a few tiles away |
| `defeat <boss>` | Spawns a boss and defeats it at once: its drops, flags and unlocks all happen |
| `npc <kind>` | A town NPC moves in beside you (`npc settler` brings a generated settler) |
| `killall` | Removes every creature, bosses too |
| `fracture` | Starts the Fracture (hardmode) |
| `event start\|stop <kind>`, `event list` | Starts or stops a world event, or lists the running ones |
| `time <hour 0-24>` | Sets the time of day |
| `flag get\|set\|clear <name> [value]`, `flag list [text]` | Reads or writes a world flag |
| `heal` | Full health |
| `life <cores>` | Sets the life cores used (max health) |
| `god on\|off` | Health refills every tick, in any world |
| `buff <status> [minutes]`, `unbuff <status>\|all` | Gives or removes statuses |
| `tp <x> <y>`, `tp spawn`, `tp core` | Moves you to a tile, the landing site or the Irradiated Core |
| `weather clear\|rain\|storm\|snow\|dust\|fog [0-1]` | Forces the weather |
| `spawnrate <0-10>` | Scales natural spawning (0 stops it) |
| `where` | Your tile, biome, the time and your health |
| `research list`, `research done <tier>\|all`, `research reset <tier>\|all` | Lists the research tiers, finishes them or puts them back (0.3.7) |
| `outposts` | Lists the outposts: where they are, who lives there and how many are armed (0.4.2) |
| `raid`, `raid warn [hours]`, `raid now` | Shows the raid on its way, announces one (default 2 hours' warning) or sends one at once (0.4.3) |
| `power`, `power set <0-1000>`, `power tier <1-5>`, `power feed` | Shows the station AI's power, sets it or its tier, or feeds it from your bag (0.3.8) |
| `ai down`, `ai rebuild` | Takes the station AI down (machines go dark) or rebuilds it (0.3.8) |

Every new mechanic adds its own commands in the step that adds it: NPC skills and work in 0.3.9, and so on.
