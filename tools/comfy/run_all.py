"""Runs every batch in tools/comfy/batches through gen_sheet.py, in priority order, skipping finished sheets.

    python tools/comfy/run_all.py [--steps 20]

Resumable: a batch whose raw sheet already exists in art/generated/ is skipped, so stopping and restarting
continues where it left off. Progress is printed per sheet.

The ComfyUI server is restarted before every sheet: on this ROCm setup the first job after a start samples at
~12 s/step but later jobs on the same server slow to 180+ s/step, and a restart costs well under a minute.
"""
import os
import subprocess
import sys
import time
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ORDER = ["wings", "accessory", "potion", "food", "weapon_", "tool_", "ammo_", "armor_"]


def priority(path: Path):
    name = path.stem
    rank = next((index for index, prefix in enumerate(ORDER) if name.startswith(prefix)), len(ORDER))
    return rank, name


def stop_server():
    """Stops any running ComfyUI from this install, so nothing is left holding the GPU when a run ends."""
    subprocess.run(["powershell", "-NoProfile", "-Command",
                    "Get-CimInstance Win32_Process -Filter \"Name='python.exe'\" | "
                    r"Where-Object { $_.CommandLine -like '*ComfyUI\main.py*' } | "
                    "ForEach-Object { Stop-Process -Id $_.ProcessId -Force }"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def restart_server():
    """Stops any running ComfyUI from this install and starts a fresh one; waits until it answers."""
    stop_server()
    time.sleep(3)
    # Power: overnight.py sets these for a focused run (1.5 GB of VRAM free, normal priority); left unset, the server
    # keeps start_comfy.ps1's restricted defaults (4 GB free, below-normal priority).
    power = []
    if os.environ.get("RIMTERA_VRAM_FREE_GB"):
        power += ["-ReserveVramGB", os.environ["RIMTERA_VRAM_FREE_GB"]]
    if os.environ.get("RIMTERA_PRIORITY"):
        power += ["-Priority", "Normal" if os.environ["RIMTERA_PRIORITY"] == "normal" else "BelowNormal"]
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                    str(ROOT / "tools" / "comfy" / "start_comfy.ps1"), *power],
                   # Not captured: the server inherits the handles, so a captured pipe would never reach EOF.
                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    deadline = time.time() + 300
    while time.time() < deadline:
        try:
            urllib.request.urlopen("http://127.0.0.1:8188/system_stats", timeout=5).read()
            return
        except OSError:
            time.sleep(3)
    raise RuntimeError("ComfyUI did not start within 5 minutes")


def main():
    steps = sys.argv[sys.argv.index("--steps") + 1] if "--steps" in sys.argv else "20"
    batches = sorted((ROOT / "tools" / "comfy" / "batches").glob("*.json"), key=priority)
    todo = [batch for batch in batches if not (ROOT / "art" / "generated" / f"{batch.stem}.png").exists()]
    print(f"{len(todo)} of {len(batches)} batches to generate", flush=True)
    started = time.time()
    for index, batch in enumerate(todo, start=1):
        restart_server()
        result = subprocess.run([sys.executable, str(ROOT / "tools" / "comfy" / "gen_sheet.py"), str(batch), "--steps", steps],
                                capture_output=True, text=True)
        tail = (result.stdout.strip().splitlines() or [""])[-1] if result.returncode == 0 else result.stderr.strip()[-400:]
        elapsed = time.time() - started
        print(f"[{index}/{len(todo)}] {batch.stem}: {tail}  (elapsed {elapsed / 60:.0f} min, "
              f"~{elapsed / index * (len(todo) - index) / 60:.0f} min left)", flush=True)


if __name__ == "__main__":
    main()
