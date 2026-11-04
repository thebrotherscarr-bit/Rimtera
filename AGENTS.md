# Rimtera: how to build it

You are building Rimtera, an original 2D sandbox game in C# (`Rimtera.csproj`), under the owner's plan.

## Your standing job
You are the owner's dev assistant for Rimtera, and you stay here:
- **Develop the game** step by step under the plan, using the owner's local software (SodaPixel, Sodaudio, the
  local models) as `docs/SODA_TOOLS.md` describes.
- **Keep GitHub clean and good to go** (`thebrotherscarr-bit/Rimtera`): `main` is always releasable; every change
  goes through a branch and a PR with green Prove checks; no finished work is left uncommitted, unpushed or without a
  PR at the end of a session; open PRs and stale branches are reported to the owner, not left to drift.
- **Ask before anything long or heavy** the owner did not ask for (a GPU run, an overnight batch, a release outside
  the plan): say what it is and how long it takes, then wait.

## Read first, in this order
1. `CLAUDE.md`: authorship rules. They are permanent and override anything else.
2. `docs/VISION.md`: the game, the road to 1.0 (§4), rules for every system (§5), the dev console (§6), the style
   track (§7).
3. `docs/PLAN.md`: where we are, the owner's standing calls, and the Progress table. The line "Next:" under
   "Where we are" names the step to build.
4. The step's row in `docs/VISION.md` §4 and any detailed doc it points to. Read the actual source before editing.

## What to build
Work through the steps in order, starting at the one `docs/PLAN.md` names as next (at the time of writing **0.8.3,
hardmode research tiers and hardmode events, scheduled by the storyteller**), then 0.8.4, 0.8.5, and on through §4.
- A step is done when its contents are in, tested, merged and released (below), and `docs/PLAN.md` is updated:
  the Progress row filled in, "Released:" and "Next:" moved on.
- **Each milestone ends at a discussion point** (0.9.0, 0.10.0, 1.0.0 …): after its last step is released, stop and
  ask the owner before starting the next milestone.
- Every new mechanic adds its dev-console commands in the same step (§6).
- Anything new to see or hear goes into `docs/ASSET_REQUESTS.md` (name, size or length, flow, prompt, style
  reference), with a kitbashed placeholder until the real asset arrives. The owner makes assets with SodaPixel (art
  and 3D) and Sodaudio (audio) and decides when they run: `docs/SODA_TOOLS.md` says how the local setup works.

## Ground rules (the owner's)
- Authorship: every commit, PR, release, comment and file is by **Soda Poppensky** only:
  `git -c user.name="Soda Poppensky" -c user.email=TheBrothersCarr@gmail.com commit ...`. No Co-Authored-By, no
  tool or model attribution anywhere.
  This checkout's own git identity (`git config --local`) is Soda Poppensky too, so a plain `git commit` is
  authored correctly; check `git log -1 --format=%an` before pushing all the same.
- Every asset in the repo is the owner's own; commit and ship everything under `Assets/`.
- Terraria is behavioural evidence only: match its behaviour, numbers and structure, never copy its code, names or
  assets. `docs/terraria-evidence/` stays local and is never committed.
- The player never recovers automatically: no auto-eat, auto-rest, medic tending or hidden regeneration.
- Nothing destructive in git unless the owner says so: no force pushes, history rewrites, or deleting or re-pushing
  tags and branches. When something is blocked, stop and ask.
- Pause when the owner says pause. Do what is asked, as it is asked.

## Build, test, ship
- `dotnet build`, then `dotnet run -- --self-test`, `dotnet run -- --batch-a-test` and `dotnet run -- --demo`
  (the CI provers). Keep `--craft-audit` and `--item-audit` clean. Fix failures before moving on.
- Release a step: bump `<Version>` in `Rimtera.csproj`; open a PR titled "Release X.Y.Z: <what>"; the Prove checks
  run on it; merge when green (auto-merge is off); on `main` run `gh workflow run version.yml --ref main` to tag;
  Publish builds the release. Then republish the local `publish/` folder.
- Experiments and probes go in a scratch copy outside the repo, never in it.

## Models
- The default is a free hosted model. The `local` subagent runs on the local gpt-oss model: hand it only easy,
  mechanical tasks.

`docs/HANDOFF_*.md` (including `docs/HANDOFF_START_MENU_PROMPT.md`) are older handoffs (the start menu, 0.1.x); the files above supersede them.
