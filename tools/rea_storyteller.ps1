# REA pass 2 on RimWorld's storyteller: extracts member and type structure (names only) for the types listed in
# docs/DESIGN_COLONY_ESCAPE.md §5.2 into docs/rimworld-evidence/storyteller-pass2/, one JSON file per type, plus an
# index of which types were found. Run on the PC where RimWorld and REA are installed:
#
#   powershell -ExecutionPolicy Bypass -File tools\rea_storyteller.ps1
#
# Uses the local REA build if `rea` is on the PATH (it has the raised metadata budget RimWorld needs, see
# docs/RESEARCH.md Phase 4), otherwise `npx -y rea-agents@6.7.0`. Only names and structure are used as evidence:
# no method bodies, constants, curves or definitions are copied into Rimtera.
param(
    [string]$Assembly = "C:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed\Assembly-CSharp.dll",
    [string]$Out = (Join-Path $PSScriptRoot "..\docs\rimworld-evidence\storyteller-pass2")
)

$types = @(
    # Storyteller definition
    "RimWorld.StorytellerDef", "RimWorld.StorytellerCompProperties",
    # Threat pacing
    "RimWorld.StorytellerComp_OnOffCycle", "RimWorld.StorytellerCompProperties_OnOffCycle",
    "RimWorld.StorytellerComp_ThreatsGenerator", "RimWorld.ThreatsGenerator", "RimWorld.ThreatsGeneratorParams",
    # Randomness
    "RimWorld.StorytellerComp_RandomMain", "RimWorld.StorytellerCompProperties_RandomMain",
    "RimWorld.StorytellerComp_CategoryMTB", "RimWorld.StorytellerComp_SingleMTB",
    # Arrivals and intro
    "RimWorld.StorytellerComp_ClassicIntro", "RimWorld.StorytellerComp_Triggered",
    "RimWorld.StorytellerComp_FactionInteraction", "RimWorld.StorytellerUtilityPopulation",
    # Adaptation and memory
    "RimWorld.StoryWatcher", "RimWorld.StoryWatcher_Adaptation", "RimWorld.StoryWatcher_PopAdaptation",
    "RimWorld.StoryWatcher_Wealth", "RimWorld.StoryWatcher_Strength", "RimWorld.StoryState",
    # Incidents
    "RimWorld.IncidentDef", "RimWorld.IncidentCategoryDef", "RimWorld.IncidentTargetTagDef",
    "RimWorld.IncidentQueue", "RimWorld.QueuedIncident", "RimWorld.FiringIncident",
    # Difficulty
    "RimWorld.DifficultyDef", "RimWorld.Difficulty",
    # Example incident workers
    "RimWorld.IncidentWorker_RaidEnemy", "RimWorld.IncidentWorker_WandererJoin", "RimWorld.IncidentWorker_RefugeePodCrash",
    "RimWorld.IncidentWorker_Disease", "RimWorld.IncidentWorker_ManhunterPack", "RimWorld.IncidentWorker_TraderCaravanArrival",
    # Breaks and inspirations
    "Verse.AI.MentalBreaker", "Verse.AI.MentalBreakWorker", "RimWorld.InspirationHandler"
)

if (-not (Test-Path $Assembly)) { throw "RimWorld's Assembly-CSharp.dll was not found at $Assembly. Pass -Assembly <path>." }
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$useLocal = [bool](Get-Command rea -ErrorAction SilentlyContinue)
"REA: $(if ($useLocal) { 'local rea' } else { 'npx rea-agents@6.7.0' })  ->  $Out"

$index = @()
foreach ($type in $types) {
    $file = Join-Path $Out ($type + ".json")
    if ($useLocal) {
        rea inspect-managed-members $Assembly --type $type --format json > $file 2> "$file.err"
    } else {
        npx -y rea-agents@6.7.0 inspect-managed-members $Assembly --type $type --format json > $file 2> "$file.err"
    }
    $found = $LASTEXITCODE -eq 0 -and (Get-Item $file).Length -gt 200
    if ((Get-Item "$file.err").Length -eq 0) { Remove-Item "$file.err" }
    $index += [pscustomobject]@{ type = $type; found = $found; bytes = (Get-Item $file).Length }
    "{0,-50} {1}" -f $type, $(if ($found) { "ok" } else { "missing or failed (see .err)" })
}
$index | ConvertTo-Json | Set-Content (Join-Path $Out "index.json") -Encoding utf8
$size = [math]::Round(((Get-ChildItem $Out -File | Measure-Object Length -Sum).Sum) / 1MB, 1)
"Done: $(($index | Where-Object found).Count) of $($types.Count) types, $size MB."
"The evidence folder is git-ignored. To share this pass, commit it on purpose (it is small):"
"  git add -f docs/rimworld-evidence/storyteller-pass2 && git commit -m 'Add REA pass 2 on the RimWorld storyteller' && git push"
