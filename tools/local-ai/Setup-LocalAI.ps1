<#
.SYNOPSIS
  One-shot setup for developing Rimtera with local models: Ollama settings, models, Cursor, opencode, extensions.

.DESCRIPTION
  Safe to re-run. By default it only CHECKS and applies small settings. Anything big needs a switch and a yes:
    -Pull      download the recommended models (sizes are shown first; asks before each)
    -Big       also offer the large optional coding models
  Installs (only if missing): Cursor (winget), opencode (npm). It never touches the GPU jobs of SodaPixel/Sodaudio.

  Why Cursor is set up as an editor around local tools: Cursor's own chat/agent sends requests through Cursor's servers, which cannot
  reach localhost, so it cannot use a model served by Ollama on this PC (unless exposed through a public tunnel). The local-model
  agents therefore run inside Cursor through extensions (Cline, Continue) and the opencode agent team already in this repo.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\tools\local-ai\Setup-LocalAI.ps1
  powershell -ExecutionPolicy Bypass -File .\tools\local-ai\Setup-LocalAI.ps1 -Pull
#>
[CmdletBinding()]
param([switch]$Pull, [switch]$Big, [switch]$Yes)

$ErrorActionPreference = 'Continue'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
function Say($m, $c = 'Cyan') { Write-Host "== $m" -ForegroundColor $c }
function Ask($q) { if ($Yes) { Write-Host "  $q -> yes"; return $true }; return ((Read-Host "  $q [y/N]") -match '^(y|yes)$') }
function Have($n) { [bool](Get-Command $n -ErrorAction SilentlyContinue) }

# ---- 1. The machine -------------------------------------------------------------------------------------------------
Say 'GPU'
$vram = $null
Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}' -ErrorAction SilentlyContinue |
  Where-Object { $_.PSChildName -match '^\d{4}$' } | ForEach-Object {
    $p = Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue
    if ($p.'HardwareInformation.qwMemorySize') {
      $gb = [math]::Round($p.'HardwareInformation.qwMemorySize' / 1GB, 1)
      Write-Host ("  {0}: {1} GB" -f $p.DriverDesc, $gb)
      if (-not $vram -or $gb -gt $vram) { $vram = $gb }
    } }
if ($vram) { Write-Host "  largest VRAM: $vram GB (Soda tools use the same GPU; keep models unloaded when they run)" }

# ---- 2. Ollama server settings (user-level env vars; restart the Ollama app afterwards) -------------------------------
Say 'Ollama'
if (-not (Have 'ollama')) { Write-Warning 'ollama not on PATH. Install it from https://ollama.com/download then re-run.'; }
else {
  $want = @{
    OLLAMA_CONTEXT_LENGTH   = '32768'   # agents need long context; the default (4096) silently truncates tool output
    OLLAMA_KEEP_ALIVE       = '10m'     # frees VRAM for SodaPixel/Sodaudio ten minutes after the last request
    OLLAMA_FLASH_ATTENTION  = '1'
    OLLAMA_KV_CACHE_TYPE    = 'q8_0'    # roughly halves the context's VRAM cost
    OLLAMA_MAX_LOADED_MODELS = '1'      # one model at a time on a 16 GB card
    OLLAMA_NUM_PARALLEL     = '1'
  }
  $changed = $false
  foreach ($k in $want.Keys) {
    $cur = [Environment]::GetEnvironmentVariable($k, 'User')
    if ($cur -ne $want[$k]) { [Environment]::SetEnvironmentVariable($k, $want[$k], 'User'); Write-Host "  set $k=$($want[$k]) (was '$cur')"; $changed = $true }
    else { Write-Host "  ok  $k=$cur" }
  }
  if ($changed) { Write-Warning 'Quit Ollama from the tray icon and start it again so the new settings apply.' }
  Say 'Installed models'
  ollama list
}

# ---- 3. Models (only with -Pull, one yes per model) ---------------------------------------------------------------------
$models = @(
  @{ name = 'nomic-embed-text';     gb = 0.3; why = 'codebase embeddings for search in Continue/Cline'; },
  @{ name = 'qwen2.5-coder:1.5b';   gb = 1;  why = 'fast tab-autocomplete'; }
)
if ($Big) {
  $models += @(
    @{ name = 'qwen3-coder:30b';    gb = 19; why = 'stronger coder (MoE; partly offloaded to RAM on 16 GB, slower)'; },
    @{ name = 'devstral:24b';       gb = 14; why = 'agent-tuned coder, tight fit on 16 GB' }
  )
}
if ((Have 'ollama') -and -not ((ollama list) -join "`n" -match 'steward:1')) {
  Write-Warning 'steward:1 (your local model) is not in `ollama list`. It is a custom model; recreate it with `ollama create steward:1 -f <Modelfile>` or `ollama pull` from wherever you keep it.'
}
if ($Pull -and (Have 'ollama')) {
  Say 'Pulling models'
  $have = (ollama list) -join "`n"
  foreach ($m in $models) {
    if ($have -match [regex]::Escape($m.name)) { Write-Host "  already have $($m.name)"; continue }
    $a = Read-Host ("  pull {0} (~{1} GB, {2})? [y/N]" -f $m.name, $m.gb, $m.why)
    if ($a -match '^(y|yes)$') { ollama pull $m.name }
  }
} else { Write-Host '  (model downloads skipped; add -Pull to choose them)' }

# ---- 4. Cursor ---------------------------------------------------------------------------------------------------------
Say 'Cursor'
if (-not (Have 'cursor')) {
  if (Have 'winget') {
    if (Ask 'Cursor is not installed. Install it with winget now?') { winget install --id Anysphere.Cursor -e --accept-package-agreements --accept-source-agreements; Write-Warning 'Open a NEW terminal so "cursor" is on PATH, then re-run this script.' }
  } else { Write-Warning 'Install Cursor from https://cursor.com/downloads' }
}
if (Have 'cursor') {
  foreach ($ext in 'saoudrizwan.claude-dev', 'continue.continue', 'muhammad-sammy.csharp', 'editorconfig.editorconfig', 'eamodio.gitlens') {
    cursor --install-extension $ext 2>&1 | Select-Object -Last 1
  }
  # User settings: merge, never overwrite.
  $settingsPath = Join-Path $env:APPDATA 'Cursor\User\settings.json'
  New-Item -ItemType Directory -Force (Split-Path $settingsPath) | Out-Null
  $s = if (Test-Path $settingsPath) { try { Get-Content -Raw $settingsPath | ConvertFrom-Json -AsHashtable } catch { Write-Warning 'settings.json has comments; skipping merge. See tools\local-ai\cursor-settings.example.json'; $null } } else { @{} }
  if ($null -ne $s) {
    $merge = Get-Content -Raw (Join-Path $PSScriptRoot 'cursor-settings.example.json') | ConvertFrom-Json -AsHashtable
    foreach ($k in $merge.Keys) { $s[$k] = $merge[$k] }
    Copy-Item $settingsPath "$settingsPath.bak" -ErrorAction SilentlyContinue
    $s | ConvertTo-Json -Depth 10 | Set-Content -Encoding UTF8 $settingsPath
    Write-Host "  merged settings into $settingsPath (backup: settings.json.bak)"
  }
}

# ---- 5. opencode (the repo's agent team + the local iterator) -------------------------------------------------------------
Say 'opencode'
if (-not (Have 'opencode')) {
  if (Have 'npm') { if (Ask 'opencode missing. npm install -g opencode-ai?') { npm install -g opencode-ai } }
  else { Write-Warning 'Install Node.js, then: npm install -g opencode-ai' }
} else { Write-Host ("  " + (opencode --version)) }

# ---- 6. Tools the gate needs -------------------------------------------------------------------------------------------------
Say 'Toolchain'
foreach ($t in 'dotnet', 'git', 'node', 'gh') { Write-Host ("  {0,-7} {1}" -f $t, $(if (Have $t) { 'found' } else { 'MISSING' })) }
if (Have 'dotnet') { Write-Host ("  dotnet SDKs: " + ((dotnet --list-sdks) -join '; ')) }
if (-not (Have 'gh')) { Write-Warning 'GitHub CLI missing: winget install GitHub.cli ; then gh auth login' }

# ---- 7. Quick end-to-end check of the MCP server -----------------------------------------------------------------------------
Say 'Rimtera MCP server'
if (Have 'node') {
  $req = '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
  $resp = $req | node (Join-Path $PSScriptRoot 'rimtera-mcp\server.mjs') 2>&1 | Select-Object -First 1
  if ($resp -match 'rimtera_prove') { Write-Host '  MCP server answers and lists rimtera_prove' -ForegroundColor Green } else { Write-Warning "  unexpected reply: $resp" }
}
Say 'Done. Next: open this folder in Cursor, then see tools\local-ai\README.md' 'Green'
