"""Generates a track for every music slot in audio_needs.tsv with ACE-Step 1.5 in ComfyUI (audio_client.py).

    python tools/audio/gen_music.py [--limit N] [--takes N] [--seconds S] [--redo] [--cues mus_title,...]

Each slot's style card (prompt, tempo, key, meter) comes from the needs map. Tracks land in
tools/audio/raw/music/<slot>__c<n>.flac. Starts ComfyUI (tools/comfy/start_comfy.ps1) if it is not running and restarts
it once if the connection drops. Slots that already have a track in Assets/Music, or a generated take, are skipped.
RIMTERA_AUDIO_PAUSE rests the GPU between tracks. --takes N tops every slot up to N takes (c1 … cN); --cues limits
the run to the named slots.
"""
import argparse
import os
import random
import subprocess
import time
import urllib.request

from audio_common import ROOT, existing_takes, load_needs, raw_path, seed
from audio_client import AceStep

# Song plans, sent to ACE-Step in the lyrics field: section tags give a track a shape (with "[Instrumental]" alone it
# improvises the form and builds, drops and wanders). The title is sung: a slow, low monk choir holding variations of
# "Rim-te-ra" and "Rim-ta-ra", no real words; long held syllables and sparse lines keep the chant slow. Its shape is
# take 5's long slow opening, then take 6's quicker build and second rise. Slots without a plan here get a shape (plan()).
PLANS = {
    "mus_title": (
        "[Intro - lone pan flute over harpsichord, slow and wide]\n[Instrumental]\n\n"
        "[Verse - deep monks hum, slow and low]\nMmmmm... riiim...\nRiiim... teee... raaa...\n\n"
        "[Build - drums and bass enter, the low chant holds]\nRiiim-te-raaa...\nRiiim-ta-raaa...\nOhhh... riiim-te-raaa...\n\n"
        "[Theme - full band and pan flute, slow deep chant underneath]\nRiiim... teee... raaa...\nRim-ta-raaa... rim-te-raaa...\nRaaa... te... riiim...\n\n"
        "[Interlude - pan flute alone over the beat, monks hum]\nMmmmm... riiim-ta-raaa...\n\n"
        "[Build - rising strings, the choir grows deeper]\nRiiim-te-raaa... riiim-te-raaa...\nRiiim-ta-raaa... ohhh...\n\n"
        "[Climax - drum and bass, full low choir and soaring strings]\nRIIIM... TEEE... RAAA...\nRIIIM-TA-RAAA... RIIIM-TE-RAAA...\nOhhh... riiim-te-raaa...\n\n"
        "[Theme - full band, monks hum under the pan flute]\nMmmmm... rim-te-raaa...\nMmmmm... rim-ta-raaa..."
    ),
    # The final boss keeps its fast orchestral-metal fight and adds the title's monks: a low drone under everything and
    # the same slow Rim-te-ra chant in half time, so the fight answers the title theme.
    "mus_boss_final": (
        "[Verse - drum and bass breakbeat and orchestral metal from the first beat, the low drone holds underneath]\nMmmmm... rimterra...\nOhhh... rim ter rah...\n\n"
        "[Chorus - full choir, slow half-time chant over the driving fight]\nRIM... TER... RAH...\nRIMTERRA... RIM TERRA...\n\n"
        "[Bridge - strings and synth leads race, monks drone low, drums never stop]\nRim terra... rim terra...\nRiiim... terrr... raaah...\n\n"
        "[Chorus - overwhelming, full choir and full band]\nRIM... TER... RAH...\nRIMTERRA... RIM TERRA...\nOhhh... rimterra..."
    ),
}

# Every other slot sings too, from one of three shapes picked by its pace: calm (night, underground and slow tracks),
# drive (exploration and events) and fight (bosses and fast fights). The monks' voice for the biome is in the prompt
# (SoundCatalog.cs); the shape keeps the drone under everything and the chant slow, half time over fast beats. Each slot
# shuffles its own Rim-te-ra lines (seeded by the cue) so no two tracks sing the same order.
# Takes are short (80 s) and the game loops 30-40 s of them (loop_cut.py), so the shapes start full, with no lone intro,
# breakdown or outro: every section has the band, the drone and the chant, and keeps going.
SHAPES = {
    "calm": ["[Verse - the music and the low monk drone start together, steady]", "[Chorus - the choir swells, slow held chant, full]",
             "[Verse - the melody leads, the drone and chant hold underneath, no pause]", "[Chorus - the chant returns, fuller, keeps going]"],
    "drive": ["[Verse - driving groove from the first beat, the low drone underneath]", "[Chorus - full choir, slow half-time chant over the beat]",
              "[Bridge - leads race, monks drone low, the beat never stops]", "[Chorus - full band and full choir, keeps driving]"],
    "fight": ["[Verse - the fight from the first beat, the low drone underneath]", "[Chorus - full choir, slow half-time chant over the driving fight]",
              "[Bridge - strings and synth leads race, monks drone low, drums never stop]", "[Chorus - overwhelming, full choir and full band]"],
}
# The chant, as the user sings it: RIM TER RAH, RIMTERRA, RIM TERRA (held long, so it stays slow).
LINES = ["Rim... ter... rah...", "Rimterra... rimterra...", "Rim terra... rim terra...", "Riiim... terrr... raaah...",
         "Rimterraaa...", "Rim ter rah... rim terra...", "Mmmmm... rimterra...", "Ohhh... rim ter rah..."]


def plan(row) -> str:
    """The lyrics field for a slot: its own plan, or a shape with the chant shuffled for this cue."""
    if row["cue"] in PLANS:
        return PLANS[row["cue"]]
    cue, bpm = row["cue"], int(row["bpm"])
    shape = ("fight" if cue.startswith("mus_boss_") or bpm >= 144
             else "calm" if cue.endswith(("_night", "_underground")) or bpm <= 110 else "drive")
    lines = random.Random(seed(cue, 0)).sample(LINES, len(LINES))
    sections = []
    for index, tag in enumerate(SHAPES[shape]):
        sung = [lines[(index * 2 + n) % len(lines)] for n in range(2)]
        if tag.startswith("[Chorus") and shape != "calm":
            sung = [line.upper() for line in sung]
        sections.append("\n".join([tag, *sung]))
    return "\n\n".join(sections)


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
    parser.add_argument("--cues", help="comma-separated slots to generate (default: all)")
    args = parser.parse_args()

    jobs = []
    cues = set(args.cues.split(",")) if args.cues else None
    for row in load_needs(["music"]):
        if cues and row["cue"] not in cues:
            continue
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
                      time_signature=row["meter"], seed=seed(row["cue"], take),
                      lyrics=plan(row))
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
