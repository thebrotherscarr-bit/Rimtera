@echo off
rem One click: indexes the original art sheets (art\sheets) for the beauty pass. Starts ComfyUI, finds every labelled
rem picture with the vision model, cuts each out to art\generated\sheet_index\, then stops ComfyUI. About a minute per
rem sheet. Finished sheets are skipped, so closing the window and running it again carries on where it stopped.
rem Options pass through:  run_sheet_index.cmd --restricted   (leaves 4 GB of VRAM free)
rem                        run_sheet_index.cmd --redo         (indexes every sheet again)
cd /d "%~dp0..\.."
"tools\comfy-app\ComfyUI\.venv\Scripts\python.exe" -u tools\comfy\index_sheets.py %*
rem Stopped with Ctrl+C, ComfyUI may still be running; this makes sure it is stopped. (Closing the window skips this
rem line; the next run stops the old server before starting.)
powershell -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"Name='python.exe'\" | Where-Object { $_.CommandLine -like '*ComfyUI\main.py*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }"
echo.
pause
