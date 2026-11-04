---
description: Get to know Rimtera, the team and the local Soda tools before building anything.
agent: producer
---

Get to know Rimtera before any work starts. Read; do not change any file, run any GPU job or queue anything.

1. Read `AGENTS.md`, `CLAUDE.md`, `docs/VISION.md`, `docs/PLAN.md` and `docs/SODA_TOOLS.md` in full.
2. Skim the code layout (`Systems/`, `World/`, `Entities/`, `AI/`, `Characters/`, `UI/`, `Program.cs` for the command-line modes) and the docs the next step will touch.
3. Check the working tree: `git status`, the current branch, the last few commits.
4. Run `dotnet build` and `dotnet run -- --self-test` to see the starting state (CPU only).
5. If Sodaudio is open, call its `list_cues` tool once to confirm the connection; if it is not, just note that.

Then report, briefly:
- what Rimtera is and where it stands (released version, next step and what it contains);
- how a step is built, tested, released and recorded here;
- how art and audio reach the game through SodaPixel and Sodaudio, and what the owner controls;
- the team you can call (`@designer`, `@gameplay`, `@ui`, `@qa`, `@reviewer`, `@balance`, `@narrative`, `@art`, `@audio`, `@release`, `@local`) and what each owns;
- the build and self-test results, and anything that looks out of date or unclear;
- the first concrete actions you would take for the next step, and wait for the owner's go.
