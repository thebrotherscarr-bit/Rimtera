# Developing Rimtera with local AI

Local model: `steward:1` on Ollama (127.0.0.1:11434). Editor: Cursor. Agents: the opencode team in `.opencode/`, plus Cline and Continue inside Cursor.

## How the pieces fit

| Piece | What it does | Where |
|---|---|---|
| Ollama + `steward:1` | The local model. Settings: 32k context, 10 min keep-alive (frees VRAM for SodaPixel/Sodaudio), one model loaded | `Setup-LocalAI.ps1` sets the env vars |
| Cursor | The editor. Its own chat/agent calls Cursor's servers, which cannot reach localhost, so local models run through Cline/Continue (extensions) and opencode (terminal) | `.cursor/`, `.cursorignore` |
| `rimtera` MCP server | Build, provers, audits, plan, git status as tools (`build`, `self_test`, `prove`, `audit`, `next_step`, `git_status`; opencode shows them as `rimtera_build` etc.), so any agent runs the same gate as CI | `tools/local-ai/rimtera-mcp/server.mjs` |
| SecondBrain (Jesster) MCP | Notes. HTTP on port 43110, key from the `SECONDBRAIN_MCP_KEY` environment variable | `.cursor/mcp.json` |
| Sodaudio MCP | Audio cues (existing) | `.cursor/mcp.json`, `opencode.json` |
| `iterate` agent + `Run-Agent.ps1` | Autonomous loop: task, edit, build, provers, fix, repeat, commit on an `agent/` branch | `.opencode/agents/iterate.md` |
| GitHub | Prove CI, rulesets, branch pruning, Dependabot | `.github/`, `tools/github/` |

## One-time setup (run on the PC, in this order)

1. `powershell -ExecutionPolicy Bypass -File tools\local-ai\Setup-LocalAI.ps1` (add `-Pull` to choose model downloads). Then quit and restart Ollama.
2. Set your SecondBrain key yourself: `setx SECONDBRAIN_MCP_KEY "<key>"` (a notes-only key is safest), then restart Cursor.
3. `gh auth login`, then `powershell -File tools\github\Apply-RepoSettings.ps1` (dry run), then again with `-Apply`.
4. Open the folder in Cursor. In Cline: provider Ollama, base URL `http://localhost:11434`, model `steward:1`. In Continue: add the same model plus `nomic-embed-text` and `qwen2.5-coder:1.5b` for autocomplete (see `~/.continue/config.yaml`).

## Running the local agent

```
.\tools\local-ai\Run-Agent.ps1 -Task "Add a console command that lists the research tiers"
.\tools\local-ai\Run-Agent.ps1 -TaskFile docs\roadmap\0.8.3.md -MaxRounds 8
```

It refuses to run on `main`, on a dirty tree, or while SodaPixel/Sodaudio hold the GPU. Each round the script itself runs build, `--self-test`, `--batch-a-test` and `--demo`, and feeds failures back. On green it commits as Soda Poppensky on `agent/...`. It never pushes, merges or deletes. Create a file named `.agent-stop` in the repo root to stop it. Logs go to `logs/` (git-ignored). Review with `git diff main...HEAD`, then open the PR yourself.

## Rules every agent follows

`CLAUDE.md` and `AGENTS.md` (authorship by Soda Poppensky only, no force pushes, no GPU jobs unasked). `.cursor/rules/rimtera.mdc` repeats them for Cursor.

## Not verified when this was written

The scripts were written without the ability to run PowerShell, `opencode`, Cursor or `gh` on the PC. Only the MCP server's protocol replies were tested. The `opencode run --agent --model` flags and the Cursor extension IDs are from memory.
The `rimtera` MCP server (`build`, `self_test`, `prove`, `audit`, `next_step`, `git_status`) and `Play-Agent.ps1 -Driver loop` have since been run for real on the PC.

## Files waiting to be moved into `.github/`

`.github/` is write-protected for remote tools, so these sit in `tools/github/dot-github/`. Copy them yourself, then open a PR:

```
copy tools\github\dot-github\dependabot.yml .github\
copy tools\github\dot-github\pull_request_template.md .github\
copy tools\github\dot-github\workflows\prune.yml .github\workflows\
```

`prune.yml` first run: Actions tab, Prune, Run workflow (dry run on by default), read the summary, then let the Monday schedule take over.
