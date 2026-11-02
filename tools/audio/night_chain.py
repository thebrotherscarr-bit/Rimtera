"""Fills the rest of a night once the overnight audio run is done: extra iterations of the music, then the art run.

    python tools/audio/night_chain.py [--wait PID] [--theme-takes N] [--music-takes N]

1. Waits for the running overnight audio run (--wait PID) to finish.
2. The title theme up to --theme-takes iterations (default 5), so there are several to audition.
3. Every music slot up to 2 takes, then up to --music-takes (default 3), one round at a time.
4. When the audio work runs out, the overnight art run (tools/comfy/overnight.py), focused.

Each step is an ordinary run (overnight_audio.py --only music, overnight.py) and skips finished work, so stopping and
starting again carries on. Output is shown here and appended to %TEMP%\\rimtera-audio-overnight.log, then
%TEMP%\\rimtera-art-overnight.log for the art.
"""
import ctypes
import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
AUDIO_PYTHON = ROOT / "tools" / "comfy-app" / "standalone-env" / "python.exe"
ART_PYTHON = ROOT / "tools" / "comfy-app" / "ComfyUI" / ".venv" / "Scripts" / "python.exe"
AUDIO_LOG = Path(os.environ["TEMP"]) / "rimtera-audio-overnight.log"
ART_LOG = Path(os.environ["TEMP"]) / "rimtera-art-overnight.log"


def wait_for(pid: int):
    """Blocks until the process exits; returns at once if it is already gone."""
    handle = ctypes.windll.kernel32.OpenProcess(0x00100000, False, pid)  # SYNCHRONIZE
    if handle:
        print(f"waiting for the audio run (pid {pid}) to finish", flush=True)
        ctypes.windll.kernel32.WaitForSingleObject(handle, 0xFFFFFFFF)
        ctypes.windll.kernel32.CloseHandle(handle)


def run(name: str, python: Path, script: str, log: Path, *args: str):
    banner = f"=== Night chain: {name} ==="
    process = subprocess.Popen([str(python), "-u", str(ROOT / script), *args], cwd=ROOT, stdout=subprocess.PIPE,
                               stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace")
    with log.open("a", encoding="utf-8") as out:
        for line in [banner + "\n", *process.stdout]:
            print(line, end="", flush=True)
            out.write(line)
            out.flush()
    process.wait()


def main():
    argv = sys.argv[1:]
    value = lambda flag, default: argv[argv.index(flag) + 1] if flag in argv[:-1] else default
    os.environ["MIOPEN_LOG_LEVEL"] = "3"
    if "--wait" in argv[:-1]:
        wait_for(int(value("--wait", "0")))
    theme_takes = value("--theme-takes", "5")
    music_takes = int(value("--music-takes", "3"))
    run(f"title theme, {theme_takes} takes", AUDIO_PYTHON, "tools/audio/overnight_audio.py", AUDIO_LOG,
        "--only", "music", "--takes", theme_takes, "--cues", "mus_title")
    for takes in range(2, music_takes + 1):
        run(f"every music slot, {takes} takes", AUDIO_PYTHON, "tools/audio/overnight_audio.py", AUDIO_LOG,
            "--only", "music", "--takes", str(takes))
    subprocess.run([str(AUDIO_PYTHON), str(ROOT / "tools" / "comfy" / "app_flows.py")], cwd=ROOT, capture_output=True)
    run("art", ART_PYTHON, "tools/comfy/overnight.py", ART_LOG)
    print("Night chain finished. Rebuild or publish to pick up the new sounds and art.", flush=True)


if __name__ == "__main__":
    main()
