<#
.SYNOPSIS
  Runs the local model on one task in a loop until the Rimtera gate is green (or the limit is reached).

.DESCRIPTION
  1. Refuses to run on main, on a dirty tree (unless -AllowDirty), or while a Soda GPU job holds the lock.
  2. Creates (or reuses) a branch  agent/<slug>-<date>.
  3. Each round: opencode runs the `iterate` agent with the task and the last gate failure, then the script runs the gate
     itself (dotnet build, --self-test, --batch-a-test, --demo). The script's gate is the judge, not the model's word.
  4. On a green gate it commits as Soda Poppensky and stops. It never pushes, merges, tags or deletes anything.
  Stop it at any time by creating a file named .agent-stop in the repo root.

.EXAMPLE
  .\tools\local-ai\Run-Agent.ps1 -Task "Add a 'hardmode stage' console command that prints the research tiers"
.EXAMPLE
  .\tools\local-ai\Run-Agent.ps1 -TaskFile docs\roadmap\0.8.3.md -Model ollama/qwen3-coder:30b -MaxRounds 8
#>
[CmdletBinding()]
param(
  [string]$Task,
  [string]$TaskFile,
  [string]$Model = 'ollama/steward:1',
  [int]$MaxRounds = 5,
  [int]$RoundTimeoutMinutes = 25,
  [string]$Branch,
  [switch]$AllowDirty,
  [switch]$IgnoreGpuLock
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repo

if (-not $Task -and -not $TaskFile) { throw 'Give -Task "..." or -TaskFile path.' }
if ($TaskFile) { $Task = (Get-Content -Raw -LiteralPath $TaskFile) }

$gpuLock = Join-Path $env:LOCALAPPDATA 'Soda\gpu.lock'
if ((Test-Path $gpuLock) -and -not $IgnoreGpuLock) {
  throw "A Soda art/audio run holds the GPU ($gpuLock). The owner decides when the GPU runs. Wait, or pass -IgnoreGpuLock."
}
if (-not (Get-Command opencode -ErrorAction SilentlyContinue)) { throw 'opencode is not on PATH. Run tools\local-ai\Setup-LocalAI.ps1.' }
try { Invoke-RestMethod http://localhost:11434/api/version -TimeoutSec 5 | Out-Null } catch { throw 'Ollama is not answering on 11434. Start the Ollama app.' }

$current = (git rev-parse --abbrev-ref HEAD).Trim()
if ($current -eq 'main') {
  if (-not $Branch) {
    $slug = ($Task.Split("`n")[0] -replace '[^a-zA-Z0-9]+', '-').Trim('-').ToLower()
    if ($slug.Length -gt 40) { $slug = $slug.Substring(0, 40).Trim('-') }
    $Branch = "agent/$slug-$(Get-Date -Format yyyyMMdd-HHmm)"
  }
  git switch -c $Branch | Out-Null
} elseif ($current -notlike 'agent/*') {
  if (-not $Branch) { throw "You are on '$current'. Switch to an agent/* branch or main, or pass -Branch." }
  git switch -c $Branch | Out-Null
}
$dirty = (git status --porcelain)
if ($dirty -and -not $AllowDirty) { throw "Working tree is not clean. Commit or stash first, or pass -AllowDirty.`n$dirty" }

$logDir = Join-Path $repo 'logs'; New-Item -ItemType Directory -Force $logDir | Out-Null
$log = Join-Path $logDir ("agent-{0}.log" -f (Get-Date -Format yyyyMMdd-HHmmss))
function Log($m) { $line = "[{0}] {1}" -f (Get-Date -Format HH:mm:ss), $m; Write-Host $line; Add-Content $log $line }

function Invoke-Gate {
  $steps = @(
    @{ name = 'build';        cmd = { dotnet build Rimtera.csproj -nologo -v q -clp:ErrorsOnly 2>&1 } },
    @{ name = 'self-test';    cmd = { dotnet run --no-build --project Rimtera.csproj -- --self-test 2>&1 } },
    @{ name = 'batch-a-test'; cmd = { dotnet run --no-build --project Rimtera.csproj -- --batch-a-test 2>&1 } },
    @{ name = 'demo';         cmd = { dotnet run --no-build --project Rimtera.csproj -- --demo 2>&1 } }
  )
  foreach ($s in $steps) {
    $out = (& $s.cmd | Out-String)
    if ($LASTEXITCODE -ne 0) {
      $tail = if ($out.Length -gt 5000) { $out.Substring($out.Length - 5000) } else { $out }
      return @{ ok = $false; step = $s.name; output = $tail }
    }
    Log "gate: $($s.name) PASS"
  }
  return @{ ok = $true }
}

Log "branch $(git rev-parse --abbrev-ref HEAD), model $Model, up to $MaxRounds rounds"
$failure = $null
for ($round = 1; $round -le $MaxRounds; $round++) {
  if (Test-Path (Join-Path $repo '.agent-stop')) { Log 'stop file found, stopping'; break }
  Log "round $round"
  $prompt = "TASK:`n$Task`n`nFollow AGENTS.md and CLAUDE.md. Do the task, then build and run the provers until they pass."
  if ($failure) { $prompt += "`n`nThe gate failed at step '$($failure.step)' after your last round. Output tail:`n$($failure.output)`n`nFix the cause." }
  $job = Start-Job -ScriptBlock { param($r, $m, $p) Set-Location $r; opencode run $p --agent iterate --model $m 2>&1 | Out-String } -ArgumentList $repo, $Model, $prompt
  if (-not (Wait-Job $job -Timeout ($RoundTimeoutMinutes * 60))) { Stop-Job $job; Log "round timed out after $RoundTimeoutMinutes min" }
  $agentOut = Receive-Job $job -ErrorAction SilentlyContinue | Out-String; Remove-Job $job -Force
  Add-Content $log "---- agent output (round $round) ----`n$agentOut`n"
  $gate = Invoke-Gate
  if ($gate.ok) {
    git add -A
    if (git status --porcelain) {
      $title = ($Task.Split("`n")[0]).Trim(); if ($title.Length -gt 70) { $title = $title.Substring(0, 70) }
      git -c user.name="Soda Poppensky" -c user.email=TheBrothersCarr@gmail.com commit -m $title | Out-Null
      Log "GREEN. Committed on $(git rev-parse --abbrev-ref HEAD): $(git log -1 --oneline)"
    } else { Log 'GREEN but nothing changed.' }
    Log "Review with: git diff main...HEAD   Nothing was pushed."
    exit 0
  }
  Log "gate FAILED at $($gate.step)"
  $failure = $gate
}
Log "Not green after $MaxRounds rounds. Changes are left uncommitted on the branch. Log: $log"
exit 1
