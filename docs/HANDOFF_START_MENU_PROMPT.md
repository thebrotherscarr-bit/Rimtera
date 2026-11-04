You are continuing the Rimtera project at `C:\Users\novad\Desktop\Rimtera`.

Read this first, in full:

- `docs/HANDOFF_START_MENU.md`

Then read the actual source files before editing:

- `UI/GraphicalSandbox.cs`
- `UI/MenuSystem.cs`
- `UI/UiFramework.cs`
- `UI/WorldSaveRepository.cs`
- `UI/PreferencesAndProfiles.cs`
- `Systems/GameSimulation.cs`
- `Systems/CoreSelfTests.cs`

Immediate task: implement the graphical persistent start menu described in the handoff document.

Requirements:

1. The app launches to a title/start menu, not directly into a world.
2. The menu is retained for the process lifetime and can be opened/closed without losing state.
3. Title menu: Single Player, Continue, Load Game, Worlds, Options, Controls, Quit.
4. In-game pause menu: Resume, Inventory, Save Game, Load Game, Settings (Options/Controls), Save & Quit to Main Menu, Save & Exit Game.
5. Esc opens/closes the pause menu and pauses the simulation.
6. Menu input must not move the player.
7. Save/load must use the existing `WorldSaveRepository` and `GameSimulation.CaptureSnapshot` / `GameSimulation.Restore`.
8. Loading a saved world must restore it; a new world uses `new GameSimulation(world.Seed)`.
9. Keep the existing 76 self-tests passing, including the menu sequence and pause-back-resume checks.
10. Do not rewrite the simulation or copy Terraria assets/code. Terraria is behavioural evidence only.

Work incrementally:

- Make one small change.
- Run `dotnet build`.
- Run `dotnet run -- --self-test`.
- Fix failures immediately.
- At the end, publish with `dotnet publish -c Release -r win-x64 --self-contained false -o publish`.
- Launch `publish\Rimtera.exe`, verify the menu appears and the process stays alive, then close it.

Report exact files changed, build result, self-test result, and any remaining limitations. Do not claim full Terraria or RimWorld parity.

---
