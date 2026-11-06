<#
.SYNOPSIS
  Starts Rimtera in agent-play mode and lets the local model play it while you watch the window.

.DESCRIPTION
  1. Starts the game window (dotnet run -- --agent-play) in a fresh throwaway world, with its own data folder so your saves are untouched.
  2. Waits for the loopback play bridge (127.0.0.1:8780), then runs the `playtester` agent (steward:1) against it.
  3. The agent observes, acts in short bursts, and writes findings to docs\playtests\. The world only moves while it acts.
  Stop it any time: create a file named .agent-stop in the repo root (or close the game window).
  Refuses to run while SodaPixel/Sodaudio hold the GPU. Needs: Ollama running, opencode, the .NET SDK.

.EXAMPLE
  .\tools\local-ai\Play-Agent.ps1
  .\tools\local-ai\Play-Agent.ps1 -Goal "Craft a workbench and a copper pickaxe, then mine down to the first ore." -Minutes 30
  .\tools\local-ai\Play-Agent.ps1 -Kit arsenal -Goal "Test every weapon style against the first-night creatures."
#>
[CmdletBinding()]
param(
  [ValidateSet('loop','opencode')][string]$Driver = 'loop',
  [string]$Goal = 'Play the early game from landfall: gather wood and stone, make a workbench and tools, survive the first night. Note anything that looks wrong.',
  [int]$Minutes = 20,
  [string]$Size = 'Small',
  [string]$Kit,
  [int]$Port = 8780,
  [string]$Model = 'ollama/steward:1',
  [switch]$CloseGame,
  [switch]$AllowConsole,
  [switch]$IgnoreGpuLock
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repo
$gpuLock = Join-Path $env:LOCALAPPDATA 'Soda\gpu.lock'
if ((Test-Path $gpuLock) -and -not $IgnoreGpuLock) { throw "A Soda art/audio run holds the GPU ($gpuLock). Wait, or pass -IgnoreGpuLock." }
if (-not (Get-Command opencode -ErrorAction SilentlyContinue)) { throw 'opencode is not on PATH. Run tools\local-ai\Setup-LocalAI.ps1.' }
try { Invoke-RestMethod http://localhost:11434/api/version -TimeoutSec 5 | Out-Null } catch { throw 'Ollama is not answering on 11434. Start the Ollama app.' }
Remove-Item (Join-Path $repo '.agent-stop') -ErrorAction SilentlyContinue

$base = "http://127.0.0.1:$Port"
$game = $null
$up = $false
try { Invoke-RestMethod "$base/health" -TimeoutSec 2 | Out-Null; $up = $true; Write-Host "Game already running on port $Port." } catch { }
if (-not $up) {
  $env:RIMTERA_DATA_DIR = Join-Path $repo 'logs\playtest-data'
  New-Item -ItemType Directory -Force $env:RIMTERA_DATA_DIR | Out-Null
  $gameArgs = @('run', '--project', 'Rimtera.csproj', '-c', 'Release', '--', '--agent-play', '--size', $Size, '--port', $Port)
  if ($Kit) { $gameArgs += @('--kit', $Kit) }
  Write-Host "Starting the game window (building first if needed)..."
  $gameLog = Join-Path $repo 'logs\game-out.log'; $gameErr = Join-Path $repo 'logs\game-err.log'
  New-Item -ItemType Directory -Force (Join-Path $repo 'logs') | Out-Null
  $game = Start-Process dotnet -ArgumentList $gameArgs -WorkingDirectory $repo -PassThru -WindowStyle Hidden -RedirectStandardOutput $gameLog -RedirectStandardError $gameErr
  $deadline = (Get-Date).AddMinutes(6)
  while (-not $up -and (Get-Date) -lt $deadline) {
    if ($game.HasExited) { throw "The game exited (code $($game.ExitCode)). Run 'dotnet build' to see why." }
    try { Invoke-RestMethod "$base/health" -TimeoutSec 2 | Out-Null; $up = $true } catch { Start-Sleep -Seconds 3 }
  }
  if (-not $up) { throw 'The play bridge did not come up in 6 minutes.' }
}
Write-Host "Bridge is up. Watch the Rimtera window. The agent starts in 10 seconds." -ForegroundColor Green
Start-Sleep -Seconds 10

$session = 'play-' + (Get-Date -Format 'yyyyMMdd-HHmm')
$prompt = @"
Session slug for notes: $session
GOAL: $Goal
Play for about $Minutes minutes or 300 actions, whichever is first. Use rimtera_play_observe first.
"@
$logDir = Join-Path $repo 'logs'; New-Item -ItemType Directory -Force $logDir | Out-Null
$log = Join-Path $logDir "$session.log"; Set-Content $log ''
Write-Host "Agent starting (model $Model). Its output streams below and into $log" -ForegroundColor Cyan
$consoleFlag = if ($AllowConsole) { ' --allow-console' } else { '' }
$job = Start-Job -ScriptBlock { param($r, $m, $p, $l, $d, $mins, $sess, $port, $goal, $cf) Set-Location $r; & { if ($d -eq 'opencode') { opencode run $p --agent playtester --model $m 2>&1 } else { node tools/local-ai/play-loop.mjs --model ($m -replace '^ollama/','') --minutes $mins --session $sess --port $port --goal $goal $cf 2>&1 } } | ForEach-Object { "$_" } | Tee-Object -FilePath $l -Append | Out-Null } -ArgumentList $repo, $Model, $prompt, $log, $Driver, $Minutes, $session, $Port, $Goal, $consoleFlag
$shown = 0; $lastBeat = Get-Date
$deadline = (Get-Date).AddMinutes($Minutes + 5)
while ($job.State -eq 'Running' -and (Get-Date) -lt $deadline) {
  if (Test-Path (Join-Path $repo '.agent-stop')) { Write-Host 'Stop file found, stopping.'; break }
  if ($game -and $game.HasExited) { Write-Host "Game process exited (code $($game.ExitCode))." -ForegroundColor Yellow; foreach ($f in @($gameErr, $gameLog)) { if (Test-Path $f) { Write-Host "--- $f ---"; Get-Content $f -Tail 25 } }; break }
  $lines = @(Get-Content $log -ErrorAction SilentlyContinue)
  if ($lines.Count -gt $shown) { $lines[$shown..($lines.Count-1)] | ForEach-Object { Write-Host $_ }; $shown = $lines.Count; $lastBeat = Get-Date }
  elseif (((Get-Date) - $lastBeat).TotalSeconds -ge 30) { Write-Host "  ...agent still working / waiting on the model ($([int]((Get-Date)-$lastBeat).TotalSeconds)s since last output)" -ForegroundColor DarkGray; $lastBeat = Get-Date }
  Start-Sleep -Seconds 3
}
if ($job.State -eq 'Running') { Stop-Job $job }
Receive-Job $job -ErrorAction SilentlyContinue | Out-Null
Remove-Job $job -Force
$out = "" + (Get-Content $log -Raw -ErrorAction SilentlyContinue)
Write-Host "`n=== agent summary (tail) ===" -ForegroundColor Cyan
Write-Host ($out.Split("`n") | Select-Object -Last 25 | Out-String)
$notes = Get-ChildItem (Join-Path $repo 'docs\playtests') -Filter "*$session*.md" -ErrorAction SilentlyContinue
if ($notes) { Write-Host "Findings: $($notes.FullName -join ', ')" -ForegroundColor Green } else { Write-Host 'No findings were written.' }
Write-Host "Full log: $log"
Write-Host "To fix what it found: .\tools\local-ai\Run-Agent.ps1 -TaskFile <findings file>   (it works one bug at a time on an agent/ branch)"
if ($CloseGame -and $game -and -not $game.HasExited) { Stop-Process -Id $game.Id }
