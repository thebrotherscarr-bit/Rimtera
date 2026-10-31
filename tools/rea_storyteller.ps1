# REA pass 2 on RimWorld's storyteller (docs/DESIGN_COLONY_ESCAPE.md §5.2).
#
# What it does: reads RimWorld's game code file (Assembly-CSharp.dll) with REA and saves, for 38 storyteller types,
# the names of their fields and methods (names and structure only, nothing else) as one JSON file per type in
# docs\rimworld-evidence\storyteller-pass2\raw\ (large, stays on this PC), then writes a small names-only summary,
# docs\rimworld-evidence\storyteller-pass2\summary.json, to commit. It checks everything it needs first and says
# plainly what is missing.
#
# RimWorld's metadata is bigger than the 64 MB budget stock REA allows, so the script installs REA 6.7.0 into
# tools\rea-local\ (git-ignored) and raises that budget to 512 MB there, the same local change as docs/RESEARCH.md
# Phase 4. Nothing outside this project folder is changed.
#
#   powershell -ExecutionPolicy Bypass -File tools\rea_storyteller.ps1
#   (or pass -Assembly "D:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64_Data\Managed\Assembly-CSharp.dll")
param([string]$Assembly = "")

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "docs\rimworld-evidence\storyteller-pass2"
$raw = Join-Path $out "raw"

function Fail($message) { Write-Host ""; Write-Host "STOPPED: $message" -ForegroundColor Red; exit 1 }

# 1. Find RimWorld: the given path, then the default Steam folder, then every Steam library listed by Steam.
$relative = "steamapps\common\RimWorld\RimWorldWin64_Data\Managed\Assembly-CSharp.dll"
$candidates = @()
if ($Assembly) { $candidates += $Assembly }
$candidates += "C:\Program Files (x86)\Steam\$relative"
try {
    $steam = (Get-ItemProperty "HKCU:\Software\Valve\Steam" -ErrorAction Stop).SteamPath
    $libraries = Join-Path $steam "steamapps\libraryfolders.vdf"
    if (Test-Path $libraries) {
        foreach ($match in [regex]::Matches((Get-Content $libraries -Raw), '"path"\s+"([^"]+)"')) {
            $candidates += (Join-Path ($match.Groups[1].Value -replace '\\\\', '\') $relative)
        }
    }
} catch { }
$Assembly = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $Assembly) {
    Fail ("RimWorld's game file was not found. Looked in:`n  " + ($candidates -join "`n  ") +
          "`nFind Assembly-CSharp.dll inside your RimWorld folder and run the script again with -Assembly `"<full path>`".")
}
Write-Host "RimWorld: $Assembly" -ForegroundColor Green

# 2. REA: installed once into tools\rea-local with the metadata budget raised (needs Node.js).
if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
    Fail "Node.js is not installed. Install Node.js LTS from https://nodejs.org, open a new PowerShell window, and run this again."
}
$reaHome = Join-Path $root "tools\rea-local"
$reaPackage = Join-Path $reaHome "node_modules\rea-agents"
if (-not (Test-Path (Join-Path $reaPackage "scripts\rea.mjs"))) {
    Write-Host "Installing REA 6.7.0 into tools\rea-local (once)..."
    New-Item -ItemType Directory -Force -Path $reaHome | Out-Null
    cmd /c "npm install --no-audit --no-fund --prefix `"$reaHome`" rea-agents@6.7.0 > `"$reaHome\install.log`" 2>&1"
    if (-not (Test-Path (Join-Path $reaPackage "scripts\rea.mjs"))) {
        Fail "Installing REA failed. The details are in $reaHome\install.log. Paste the end of it to Claude."
    }
}
$patched = 0
foreach ($file in Get-ChildItem (Join-Path $reaPackage "dist") -Recurse -Include *.js, *.mjs) {
    $text = [IO.File]::ReadAllText($file.FullName)
    if ($text.Contains("MAX_METADATA_REPRESENTATION_BYTES = 64 * 1024 * 1024")) {
        [IO.File]::WriteAllText($file.FullName, $text.Replace("MAX_METADATA_REPRESENTATION_BYTES = 64 * 1024 * 1024", "MAX_METADATA_REPRESENTATION_BYTES = 512 * 1024 * 1024"))
        $patched++
    }
}
$rea = "node `"$(Join-Path $reaPackage "scripts\rea.mjs")`""
Write-Host "REA: tools\rea-local (metadata budget 512 MB$(if ($patched) { ", raised in $patched files" }))" -ForegroundColor Green

$types = @(
    "RimWorld.StorytellerDef", "RimWorld.StorytellerCompProperties",
    "RimWorld.StorytellerComp_OnOffCycle", "RimWorld.StorytellerCompProperties_OnOffCycle",
    "RimWorld.StorytellerComp_ThreatsGenerator", "RimWorld.ThreatsGenerator", "RimWorld.ThreatsGeneratorParams",
    "RimWorld.StorytellerComp_RandomMain", "RimWorld.StorytellerCompProperties_RandomMain",
    "RimWorld.StorytellerComp_CategoryMTB", "RimWorld.StorytellerComp_SingleMTB",
    "RimWorld.StorytellerComp_ClassicIntro", "RimWorld.StorytellerComp_Triggered",
    "RimWorld.StorytellerComp_FactionInteraction", "RimWorld.StorytellerUtilityPopulation",
    "RimWorld.StoryWatcher", "RimWorld.StoryWatcher_Adaptation", "RimWorld.StoryWatcher_PopAdaptation",
    "RimWorld.StoryWatcher_Wealth", "RimWorld.StoryWatcher_Strength", "RimWorld.StoryState",
    "RimWorld.IncidentDef", "RimWorld.IncidentCategoryDef", "RimWorld.IncidentTargetTagDef",
    "RimWorld.IncidentQueue", "RimWorld.QueuedIncident", "RimWorld.FiringIncident",
    "RimWorld.DifficultyDef", "RimWorld.Difficulty",
    "RimWorld.IncidentWorker_RaidEnemy", "RimWorld.IncidentWorker_WandererJoin", "RimWorld.IncidentWorker_RefugeePodCrash",
    "RimWorld.IncidentWorker_Disease", "RimWorld.IncidentWorker_ManhunterPack", "RimWorld.IncidentWorker_TraderCaravanArrival",
    "Verse.AI.MentalBreaker", "Verse.AI.MentalBreakWorker", "RimWorld.InspirationHandler"
)

New-Item -ItemType Directory -Force -Path $raw | Out-Null

$helper = Join-Path $PSScriptRoot "rea_summary.mjs"

# Runs REA for one type through cmd, so the JSON is written byte for byte (Windows PowerShell's > would re-encode it).
# Node checks the result: a type counts as read only when REA reports complete coverage and finds fields or methods.
function Inspect($type) {
    $file = Join-Path $raw "$type.json"
    $errors = "$file.err"
    cmd /c "$rea inspect-managed-members `"$Assembly`" --type $type --format json > `"$file`" 2> `"$errors`""
    if ((Test-Path $errors) -and (Get-Item $errors).Length -eq 0) { Remove-Item $errors }
    $script:lastCheck = (node $helper check $file) -join " "
    return $LASTEXITCODE -eq 0
}

# 3. One test type first: if REA cannot read RimWorld at all, stop here with its own error message.
Write-Host "Testing with RimWorld.Storyteller (the first run downloads REA and can take a minute)..."
if (-not (Inspect "RimWorld.Storyteller")) {
    $err = Join-Path $raw "RimWorld.Storyteller.json.err"
    $detail = $script:lastCheck
    if (Test-Path $err) { $detail = "$detail $((Get-Content $err -Raw).Trim())".Trim() }
    Fail "REA could not read RimWorld. Its message:`n$detail`nPaste this to Claude."
}
Write-Host "REA works." -ForegroundColor Green

# 4. Every type.
$read = 0
foreach ($type in $types) {
    $found = Inspect $type
    if ($found) { $read++ }
    Write-Host ("{0,-50} {1}" -f $type, $(if ($found) { "ok" } else { "not read: $script:lastCheck" }))
}

# 5. The summary to commit (the raw files stay on this PC). Old files from the earlier, failed run sat directly in
# the folder; they are removed so they are not shared by mistake.
Get-ChildItem $out -File -Filter "*.json" | Where-Object { $_.Name -notin @("index.json", "summary.json") } | Remove-Item
node $helper summarize $raw $out | Out-Null
$size = [math]::Round((Get-Item (Join-Path $out "summary.json")).Length / 1KB)
Write-Host ""
Write-Host "Done: $read of $($types.Count) types read. Summary: $size KB." -ForegroundColor Green
Write-Host "To share the summary (not the large raw files), run these three lines:"
Write-Host "  git add -A docs/rimworld-evidence/storyteller-pass2; git add -f docs/rimworld-evidence/storyteller-pass2/summary.json docs/rimworld-evidence/storyteller-pass2/index.json"
Write-Host "  git commit -m `"Replace the failed storyteller pass with its summary`""
Write-Host "  git push"
