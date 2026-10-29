"""Generates a track for every music slot in audio_needs.tsv with ACE-Step 1.5 in ComfyUI (audio_client.py).

    python tools/audio/gen_music.py [--limit N] [--takes N] [--seconds S] [--redo]

Each slot's style card (prompt, tempo, key, meter) comes from the needs map. Tracks land in
tools/audio/raw/music/<slot>__c<n>.flac. Starts ComfyUI (tools/comfy/start_comfy.ps1) if it is not running and restarts
it once if the connection drops. Slots that already have a track in Assets/Music, or a generated take, are skipped.
RIMTERA_AUDIO_PAUSE rests the GPU between tracks.
"""
import argparse
import os
import subprocess
import time
import urllib.request

from audio_common import ROOT, existing_takes, load_needs, raw_path, seed
from audio_client import AceStep


def server_up(url="http://127.0.0.1:8188") -> bool:
    try:
        with urllib.request.urlopen(url + "/system_stats", timeout=5):
            return True
    except OSError:
        return False


def start_server():
    # Same power setting as the rest of the run (overnight_audio.py): the VRAM left free and the priority.
    priority = "Normal" if os.environ.get("RIMTERA_PRIORITY") == "normal" else "BelowNormal"
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(ROOT / "tools" / "comfy" / "start_comfy.ps1"),
                    "-ReserveVramGB", os.environ.get("RIMTERA_VRAM_FREE_GB", "4"), "-Priority", priority])
    for _ in range(120):
        if server_up():
            return
        time.sleep(3)
    raise RuntimeError("ComfyUI did not start; see %TEMP%\\rimtera-comfy.log.err")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--limit", type=int)
    parser.add_argument("--takes", type=int, default=1)
    parser.add_argument("--seconds", type=float, help="override every track's length (for a quick test)")
    parser.add_argument("--redo", action="store_true")
    args = parser.parse_args()

    jobs = []
    for row in load_needs(["music"]):
        if not args.redo and existing_takes(row["cue"], "music"):
            continue
        for take in range(1, args.takes + 1):
            if args.redo or not raw_path(row, take, "flac").exists():
                jobs.append((row, take))
        if args.limit and len({row["cue"] for row, _ in jobs}) >= args.limit:
            break
    if not jobs:
        print("music: nothing to generate", flush=True)
        return
    print(f"music: {len(jobs)} tracks", flush=True)
    if not server_up():
        start_server()
    ace = AceStep()
    pause = float(os.environ.get("RIMTERA_AUDIO_PAUSE", "0") or 0)
    for index, (row, take) in enumerate(jobs, start=1):
        out = raw_path(row, take, "flac")
        out.parent.mkdir(parents=True, exist_ok=True)
        started = time.time()
        kwargs = dict(seconds=args.seconds or float(row["seconds"]), bpm=int(row["bpm"]), key=row["key"],
                      time_signature=row["meter"], seed=seed(row["cue"], take))
        for attempt in (1, 2):
            try:
                out.write_bytes(ace.track(row["prompt"], **kwargs))
                print(f"[{index}/{len(jobs)}] {out.name} ({time.time() - started:.0f}s)", flush=True)
                break
            except (ConnectionError, OSError, RuntimeError, TimeoutError) as error:
                print(f"[{index}/{len(jobs)}] {'retrying' if attempt == 1 else 'FAILED'} {out.name}: {str(error)[:200]}", flush=True)
                if attempt == 1 and not server_up():
                    start_server()
        if pause:
            time.sleep(pause)


if __name__ == "__main__":
    main()
