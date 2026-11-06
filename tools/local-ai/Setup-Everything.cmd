@echo off
setlocal
rem One double-click setup for Rimtera + local AI. Safe to re-run. Nothing is pushed to GitHub except settings you confirm.
cd /d "%~dp0\..\.."
echo.
echo === 1/4 Local AI: Ollama settings, Cursor, opencode, extensions ===
powershell -NoProfile -ExecutionPolicy Bypass -File tools\local-ai\Setup-LocalAI.ps1 -Yes
echo.
echo === 2/4 GitHub sign-in (a browser opens; enter the code it shows) ===
where gh >nul 2>nul || winget install --id GitHub.cli -e --accept-package-agreements --accept-source-agreements
gh auth status >nul 2>nul || gh auth login -h github.com -p https -w
echo.
echo === 3/4 GitHub settings: preview ===
powershell -NoProfile -ExecutionPolicy Bypass -File tools\github\Apply-RepoSettings.ps1
set /p APPLY=Apply these GitHub settings now? [y/N] 
if /i "%APPLY%"=="y" powershell -NoProfile -ExecutionPolicy Bypass -File tools\github\Apply-RepoSettings.ps1 -Apply
echo.
echo === 4/4 Workflow files into .github ===
if not exist .github\workflows mkdir .github\workflows
copy /-y tools\github\dot-github\dependabot.yml .github\ >nul
copy /-y tools\github\dot-github\pull_request_template.md .github\ >nul
copy /-y tools\github\dot-github\workflows\prune.yml .github\workflows\ >nul
echo Copied. Commit them on a branch and open a PR (git switch -c ci/housekeeping).
echo.
set /p DEMO=Run a small demo of the local agent now (uses the GPU for a few minutes)? [y/N] 
if /i "%DEMO%"=="y" powershell -NoProfile -ExecutionPolicy Bypass -File tools\local-ai\Run-Agent.ps1 -AllowDirty -Branch agent/demo-%RANDOM% -MaxRounds 2 -Task "Read-only demo: open docs/PLAN.md, report the released version and the next step in two sentences, and change no files."
echo.
echo Done. Open this folder in Cursor: cursor .
pause
