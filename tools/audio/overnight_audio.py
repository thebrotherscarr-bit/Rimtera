"""Overnight audio run: every sound the game still needs, resumable. Start it and leave it.

    python tools/audio/overnight_audio.py [--only effects|music|ambience] [--pause SECONDS] [--limit N] [--no-fill] [--restricted] [--quick]

A night has one focus, art or audio, never both. An audio night runs:
1. Refreshes tools/audio/audio_needs.tsv from the game (dotnet run -- --sound-cues).
2. Effects (MOSS-SoundEffect): weapons, impacts, tools, tiles, interactions and UI first, then player, creatures,
   bosses and events.
3. Music (ACE-Step 1.5 in ComfyUI): every biome (day, night, underground), event and boss-tier slot.
4. Ambience (MOSS-SoundEffect): loops for every biome, weather and spot sound.
5. Post-processes every new take (audio_post.py), builds the audition page (make_audition.py) and, unless --no-fill,
   promotes the first takes of every cue that has no file at all (promote.py --fill-gaps). Existing files, your
   recordings included, are never replaced.

--only runs one lane. A focused night (the default) gets the machine: normal priority, no pause, all but 1.5 GB of the
VRAM and 80% of the CPU threads. --restricted leaves 4 GB of VRAM free instead (below-normal priority, 10 s rests) so
the PC stays usable.
--quick is a dry run of every step in minutes (one short take per step, nothing promoted). --pause overrides the
rest after every clip or track. --limit caps the cues per step. Each step skips
work already done, so stopping and re-running continues where it left off. ComfyUI unloads its models before MOSS
loads. Afterwards rebuild or publish the game so new files are copied to the output folder.
"""
import json
import os
import subprocess
import sys
import urllib.request

from audio_common import COMFY_PYTHON, MOSS_PYTHON, TOOLS, export_needs

FIRST = "weapons,impacts,tools,tiles,interactions,ui"
LATER = "player,creatures,bosses,events"
LANES = ("effects", "music", "ambience")


def free_comfy():
    """Asks a running ComfyUI to drop its models so MOSS has the VRAM; harmless if it is not running."""
    try:
        request = urllib.request.Request("http://127.0.0.1:8188/free", data=json.dumps({"unload_models": True, "free_memory": True}).encode(),
                                         headers={"Content-Type": "application/json"})
        urllib.request.urlopen(request, timeout=10).read()
    except OSError:
        pass


def step(name, python, script, *args):
    print(f"=== {name} ===", flush=True)
    subprocess.run([str(python), str(TOOLS / script), *args], cwd=TOOLS, env=os.environ.copy())


def main():
    argv = sys.argv[1:]
    restricted = "--restricted" in argv
    # Focused (the default): 1.5 GB of VRAM and a fifth of the CPU left over. Restricted: 4 GB of VRAM left for the user.
    os.environ["RIMTERA_VRAM_FREE_GB"] = "4" if restricted else "1.5"
    os.environ["RIMTERA_CPU_FRACTION"] = "0.75" if restricted else "0.8"
    os.environ["RIMTERA_PRIORITY"] = "below" if restricted else "normal"
    pause = argv[argv.index("--pause") + 1] if "--pause" in argv[:-1] else ("10" if restricted else "0")
    os.environ["RIMTERA_AUDIO_PAUSE"] = os.environ["RIMTERA_COMFY_PAUSE"] = pause
    limit = ["--limit", argv[argv.index("--limit") + 1]] if "--limit" in argv[:-1] else []
    # --quick proves every step in a few minutes: one cue per step, one short take each, nothing promoted.
    quick = "--quick" in argv
    sfx_extra = ["--limit", "1", "--takes", "1", "--max-seconds", "4"] if quick else limit
    music_extra = ["--limit", "1", "--seconds", "20"] if quick else limit
    only = argv[argv.index("--only") + 1] if "--only" in argv[:-1] else None
    if only is not None and only not in LANES:
        sys.exit(f"--only must be one of {', '.join(LANES)}")
    export_needs()
    for lane in [only] if only else LANES:
        if lane == "effects":
            free_comfy()
            step("Effects: weapons, tools, tiles, interactions, UI", MOSS_PYTHON, "gen_sfx.py", "--categories", FIRST, *sfx_extra)
            step("Effects: player, creatures, bosses, events", MOSS_PYTHON, "gen_sfx.py", "--categories", LATER, *sfx_extra)
        elif lane == "music":
            step("Music", COMFY_PYTHON, "gen_music.py", *music_extra)
        else:
            free_comfy()
            step("Ambience", MOSS_PYTHON, "gen_sfx.py", "--categories", "ambience", *sfx_extra)
    step("Post-process", MOSS_PYTHON, "audio_post.py")
    step("Audition page", MOSS_PYTHON, "make_audition.py")
    if "--no-fill" not in argv and not quick:
        step("Fill gaps", MOSS_PYTHON, "promote.py", "--fill-gaps")
    print("Overnight audio run finished. Audition: tools/audio/candidates/audition.html. Rebuild or publish to pick up new sounds.", flush=True)


if __name__ == "__main__":
    main()
