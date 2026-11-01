@echo off
rem One click: the overnight art run. Refreshes the art lists from the game, then generates block textures, environment
rem art and animations, starting and restarting ComfyUI as it goes. Finished work is skipped, so close the window to
rem pause and double-click again to carry on. It also installs or refreshes the Rimtera flows in the ComfyUI app.
rem Options pass through:  run_overnight_art.cmd --pause 10   (rests the GPU 10 s after every image)
rem Output is shown here and appended to %TEMP%\rimtera-art-overnight.log.
cd /d "%~dp0..\.."
"tools\comfy-app\standalone-env\python.exe" tools\comfy\app_flows.py >nul 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -Command "& '.\tools\comfy-app\ComfyUI\.venv\Scripts\python.exe' -u tools\comfy\overnight.py %* 2>&1 | Tee-Object -FilePath (Join-Path $env:TEMP 'rimtera-art-overnight.log') -Append"
echo.
pause
