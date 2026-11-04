@echo off
rem Draws a music map for each audio file dropped on this file (or passed on the command line):
rem   tools\audio\music_map.cmd tools\audio\raw\music\mus_boss_final__c4.flac
rem The map is saved next to the track as <name>.map.png and opened. See music_map.py for what the panels show.
cd /d "%~dp0..\.."
if "%~1"=="" (
    echo Drop one or more music files ^(.flac, .wav, .ogg, .mp3^) on music_map.cmd to map them.
    pause
    exit /b
)
:next
if "%~1"=="" goto done
echo Mapping %~nx1 ...
"tools\moss-venv\Scripts\python.exe" tools\audio\music_map.py "%~f1" "%~dpn1.map.png" 2>nul
if exist "%~dpn1.map.png" start "" "%~dpn1.map.png"
shift
goto next
:done
pause
