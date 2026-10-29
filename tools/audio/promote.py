"""Moves auditioned candidates into the game (docs/AUDIO_FLOW.md, step 6). Run with the MOSS venv python:

    tools/moss-venv/Scripts/python.exe tools/audio/promote.py --keep keep.txt
    tools/moss-venv/Scripts/python.exe tools/audio/promote.py --fill-gaps

--keep takes the list the audition page copies (one candidate file name per line) and adds each as the cue's next take.
--fill-gaps gives every cue that has no file at all its first candidates (up to the cue's wanted variants), so nothing
is left on the synth fallback once a take exists; audition later to swap them for better ones.

Effects go to Assets/Sounds/<cue>.wav, <cue>_2.wav …; music and ambience to Assets/Music and Assets/Ambience as OGG
(Vorbis, a fraction of WAV's size). Existing files are never overwritten: your recordings always win. Every promoted
file is logged in tools/audio/promoted.tsv, so --replace-generated can later swap generated takes and only those.
"""
import argparse
import csv

import soundfile

from audio_common import CANDIDATES, TOOLS, asset_folder, existing_takes, load_needs

LOG = TOOLS / "promoted.tsv"


def generated_files():
    if not LOG.exists():
        return set()
    with LOG.open(encoding="utf-8") as handle:
        return {line.split("\t")[1] for line in handle.read().splitlines()[1:] if "\t" in line}


def next_free(cue, category):
    folder = asset_folder(category)
    ext = "ogg" if category in ("music", "ambience") else "wav"
    taken = {path.stem for path in folder.glob(f"{cue}*.*")}
    if cue not in taken:
        return folder / f"{cue}.{ext}"
    number = 2
    while f"{cue}_{number}" in taken:
        number += 1
    return folder / f"{cue}_{number}.{ext}"


def promote(candidate, row, log):
    target = next_free(row["cue"], row["category"])
    target.parent.mkdir(parents=True, exist_ok=True)
    audio, rate = soundfile.read(str(candidate), always_2d=True)
    if target.suffix == ".ogg":
        # In blocks: libsndfile 1.2.2 crashes writing a long Vorbis file in one call.
        with soundfile.SoundFile(str(target), "w", rate, audio.shape[1], format="OGG", subtype="VORBIS") as handle:
            for start in range(0, len(audio), 8192):
                handle.write(audio[start:start + 8192])
    else:
        soundfile.write(str(target), audio, rate, subtype="PCM_16")
    log.writerow([row["cue"], str(target.relative_to(TOOLS.parents[1])).replace("\\", "/"), candidate.name])
    print(f"{candidate.name} -> {target.name}", flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--keep")
    parser.add_argument("--fill-gaps", action="store_true")
    parser.add_argument("--replace-generated", action="store_true", help="with --keep: remove the cue's earlier generated takes first")
    args = parser.parse_args()
    rows = {row["cue"]: row for row in load_needs()}
    new_log = not LOG.exists()
    with LOG.open("a", encoding="utf-8", newline="") as handle:
        log = csv.writer(handle, delimiter="\t", lineterminator="\n")
        if new_log:
            log.writerow(["cue", "file", "candidate"])
        if args.keep:
            names = [line.strip() for line in open(args.keep, encoding="utf-8") if line.strip()]
            generated = generated_files()
            cleared = set()
            for name in names:
                cue = name.rsplit("__c", 1)[0]
                row = rows.get(cue)
                candidate = CANDIDATES / (row["category"] if row else "") / name
                if row is None or not candidate.exists():
                    print(f"skipped {name}: no such candidate", flush=True)
                    continue
                if args.replace_generated and cue not in cleared:
                    for path in asset_folder(row["category"]).glob(f"{cue}*.*"):
                        if str(path.relative_to(TOOLS.parents[1])).replace("\\", "/") in generated:
                            path.unlink()
                    cleared.add(cue)
                promote(candidate, row, log)
        if args.fill_gaps:
            for cue, row in rows.items():
                folder = asset_folder(row["category"])
                if existing_takes(cue, row["category"]) or any(folder.glob(f"{cue}.ogg")):
                    continue
                candidates = sorted((CANDIDATES / row["category"]).glob(f"{cue}__c*.wav"))
                for candidate in candidates[: int(row["variants"])]:
                    promote(candidate, row, log)


if __name__ == "__main__":
    main()
