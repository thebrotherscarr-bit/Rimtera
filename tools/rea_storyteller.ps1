# REA pass 2 on RimWorld's storyteller (docs/DESIGN_COLONY_ESCAPE.md §5.2).
#
# What it does: reads RimWorld's game code file (Assembly-CSharp.dll) with REA and saves, for 39 storyteller types,
# the names of their fields and methods (names and structure only, nothing else) as one JSON file per type in
# docs\rimworld-evidence\storyteller-pass2\. It checks everything it needs first and says plainly what is missing.
#
#   powershell -ExecutionPolicy Bypass -File tools\rea_storyteller.ps1
#   (or pass -Assembly "D:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64_Data\Managed\Assembly-CSharp.dll")
param([string]$Assembly = "")

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "docs\rimworld-evidence\storyteller-pass2"

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

# 2. Find REA: the local build if `rea` is on the PATH, otherwise npx (which needs Node.js).
if (Get-Command rea -ErrorAction SilentlyContinue) {
    $rea = "rea"
} elseif (Get-Command npx -ErrorAction SilentlyContinue) {
    $rea = "npx -y rea-agents@6.7.0"
} else {
    Fail "Neither REA (`rea`) nor Node.js (`npx`) is installed. Install Node.js LTS from https://nodejs.org, open a new PowerShell window, and run this again."
}
Write-Host "REA: $rea" -ForegroundColor Green

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

New-Item -ItemType Directory -Force -Path $out | Out-Null

# Runs REA for one type through cmd, so the JSON is written byte for byte (Windows PowerShell's > would re-encode it).
function Inspect($type) {
    $file = Join-Path $out "$type.json"
    $errors = "$file.err"
    cmd /c "$rea inspect-managed-members `"$Assembly`" --type $type --format json > `"$file`" 2> `"$errors`""
    $ok = $LASTEXITCODE -eq 0 -and (Test-Path $file) -and (Get-Item $file).Length -gt 200
    if ((Test-Path $errors) -and (Get-Item $errors).Length -eq 0) { Remove-Item $errors }
    return $ok
}

# 3. One test type first: if REA cannot read RimWorld at all, stop here with its own error message.
Write-Host "Testing with RimWorld.Storyteller (the first run downloads REA and can take a minute)..."
if (-not (Inspect "RimWorld.Storyteller")) {
    $err = Join-Path $out "RimWorld.Storyteller.json.err"
    $detail = if (Test-Path $err) { (Get-Content $err -Raw).Trim() } else { "(no error text)" }
    Fail "REA could not read RimWorld. Its message:`n$detail`nPaste this to Claude."
}
Write-Host "REA works." -ForegroundColor Green

# 4. Every type.
$index = @()
foreach ($type in $types) {
    $found = Inspect $type
    $index += [pscustomobject]@{ type = $type; found = $found }
    Write-Host ("{0,-50} {1}" -f $type, $(if ($found) { "ok" } else { "not found in this RimWorld version" }))
}
$index | ConvertTo-Json | Set-Content (Join-Path $out "index.json") -Encoding utf8

$size = [math]::Round(((Get-ChildItem $out -File | Measure-Object Length -Sum).Sum) / 1MB, 1)
Write-Host ""
Write-Host "Done: $(($index | Where-Object found).Count) of $($types.Count) types read ($size MB) into $out" -ForegroundColor Green
Write-Host "To share them, run:"
Write-Host "  git add -f docs/rimworld-evidence/storyteller-pass2"
Write-Host "  git commit -m `"Add REA pass 2 on the RimWorld storyteller`""
Write-Host "  git push"
