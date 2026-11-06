<#
.SYNOPSIS
  Applies Rimtera's GitHub repo settings: merge rules, branch cleanup, rulesets for main and release tags, security features.

.DESCRIPTION
  Dry run by default: prints what it would send. Add -Apply to change the repo. Needs the GitHub CLI signed in as the owner
  (gh auth login) with admin rights on thebrotherscarr-bit/Rimtera. Safe to re-run: rulesets are matched by name and updated.

  What it sets, and why (AGENTS.md: main always releasable, every change through a PR with green Prove checks, auto-merge off):
    - Squash or merge-commit only (no rebase), "update branch" button on, auto-merge OFF.
    - Head branches are deleted automatically when a PR merges (the pruning).
    - Ruleset protect-main: no deletion, no force-push, PR required (0 approvals: sole owner), all Prove checks required
      and up to date; the owner (repo admin) can bypass only through a PR.
    - Ruleset protect-release-tags: v* tags cannot be moved, updated or deleted. New tags (version.yml) are still allowed.
    - Default workflow token is read-only (each workflow already asks for what it needs).
    - Dependabot alerts and security updates, secret scanning and push protection where the plan allows.
#>
[CmdletBinding()]
param([switch]$Apply, [string]$Repo = 'thebrotherscarr-bit/Rimtera')

$ErrorActionPreference = 'Stop'
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'Install GitHub CLI (winget install GitHub.cli) and run: gh auth login' }
gh auth status | Out-Null

function Call($method, $path, $body) {
  $show = "$method /repos/$Repo$path"
  if (-not $Apply) { Write-Host "[dry run] $show"; if ($body) { Write-Host ("          " + ($body | ConvertTo-Json -Depth 10 -Compress)) }; return $null }
  Write-Host "$show"
  try {
    if ($body) { ($body | ConvertTo-Json -Depth 10) | gh api -X $method "/repos/$Repo$path" --input - }
    else { gh api -X $method "/repos/$Repo$path" }
  } catch { Write-Warning "  not applied: $($_.Exception.Message)" }
}

# 1. Merge behaviour and branch pruning
Call PATCH '' @{
  allow_squash_merge = $true; allow_merge_commit = $true; allow_rebase_merge = $false
  allow_auto_merge = $false; allow_update_branch = $true; delete_branch_on_merge = $true
  squash_merge_commit_title = 'PR_TITLE'; squash_merge_commit_message = 'PR_BODY'
  has_wiki = $false
}

# 2. Rulesets (create or update by name)
foreach ($f in 'ruleset-main.json', 'ruleset-tags.json') {
  $body = Get-Content -Raw (Join-Path $PSScriptRoot $f) | ConvertFrom-Json
  $existing = if ($Apply) { (gh api "/repos/$Repo/rulesets" | ConvertFrom-Json) | Where-Object { $_.name -eq $body.name } } else { $null }
  if ($existing) { Call PUT "/rulesets/$($existing.id)" $body } else { Call POST '/rulesets' $body }
}

# 3. Workflow token: read-only by default, no PR approvals by Actions
Call PUT '/actions/permissions/workflow' @{ default_workflow_permissions = 'read'; can_approve_pull_request_reviews = $false }

# 4. Security features (some need a public repo or a paid plan; failures are only warnings)
Call PUT '/vulnerability-alerts' $null
Call PUT '/automated-security-fixes' $null
Call PATCH '' @{ security_and_analysis = @{
  secret_scanning = @{ status = 'enabled' }
  secret_scanning_push_protection = @{ status = 'enabled' } } }

if (-not $Apply) { Write-Host "`nDry run only. Re-run with -Apply to make these changes." -ForegroundColor Yellow }
else { Write-Host "`nDone. Check: https://github.com/$Repo/settings/rules" -ForegroundColor Green }
