@echo off
rem Starts (or resumes) the overnight audio run. Double-click it, or pass options through:
rem   tools\audio\run_overnight.cmd --only music
rem   tools\audio\run_overnight.cmd --restricted
rem Finished takes are skipped, so it carries on where the last run stopped. Close the window to pause it; the track or
rem clip in progress is the only work lost. Output is shown here and appended to %TEMP%\rimtera-audio-overnight.log.
cd /d "%~dp0..\.."
rem First run on this PC: the one-time audio setup (the MOSS venv and models) runs by itself.
if not exist "tools\moss-venv\Scripts\python.exe" powershell -NoProfile -ExecutionPolicy Bypass -File tools\audio\setup_audio.ps1
rem Installs or refreshes the Rimtera flows in the ComfyUI app.
"tools\comfy-app\standalone-env\python.exe" tools\comfy\app_flows.py >nul 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -Command "& '.\tools\comfy-app\standalone-env\python.exe' -u tools\audio\overnight_audio.py %* 2>&1 | Tee-Object -FilePath (Join-Path $env:TEMP 'rimtera-audio-overnight.log') -Append"
echo.
pause
