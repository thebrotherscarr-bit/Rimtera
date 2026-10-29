"""Generates sound effects and ambience loops for every cue in audio_needs.tsv with MOSS-SoundEffect (moss_worker.py).

    python tools/audio/gen_sfx.py [--categories weapons,tools,...] [--limit N] [--takes N] [--redo]

Per cue it makes as many candidate takes as the needs map wants (`variants`, or --takes), into
tools/audio/raw/<category>/<cue>__c<n>.wav. A cue that already has that many takes in the game (your recordings or
promoted takes) is skipped: recordings always win. Takes already generated are skipped, so a stopped run resumes.
Music rows are left to gen_music.py. --limit caps the number of cues (for a dry run).
"""
import argparse
import json
import os
import subprocess
from pathlib import Path

from audio_common import MOSS_PYTHON, TOOLS, existing_takes, load_needs, raw_path, seed

SFX_CATEGORIES = ["weapons", "impacts", "tools", "tiles", "interactions", "ui", "player", "creatures", "bosses", "events"]


def plan(categories, limit=None, takes=None, redo=False, max_seconds=None):
    jobs, cues = [], 0
    for row in load_needs(categories):
        if row["category"] == "music":
            continue
        wanted = takes or int(row["variants"])
        if not redo and len(existing_takes(row["cue"], row["category"])) >= wanted:
            continue
        seconds = min(float(row["seconds"]), max_seconds or 1e9)
        cue_jobs = [{"prompt": row["prompt"], "seconds": seconds, "seed": seed(row["cue"], take), "out": str(raw_path(row, take))}
                    for take in range(1, wanted + 1) if redo or not raw_path(row, take).exists()]
        if not cue_jobs:
            continue
        jobs.extend(cue_jobs)
        cues += 1
        if limit and cues >= limit:
            break
    return jobs, cues


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--categories", default=",".join(SFX_CATEGORIES))
    parser.add_argument("--limit", type=int)
    parser.add_argument("--takes", type=int)
    parser.add_argument("--redo", action="store_true")
    parser.add_argument("--max-seconds", type=float, help="cap every clip's length (for a quick test)")
    args = parser.parse_args()
    jobs, cues = plan(args.categories.split(","), args.limit, args.takes, args.redo, args.max_seconds)
    if not jobs:
        print("sound effects: nothing to generate", flush=True)
        return
    print(f"sound effects: {len(jobs)} takes for {cues} cues", flush=True)
    job_file = TOOLS / "jobs_sfx.jsonl"
    job_file.write_text("\n".join(json.dumps(job) for job in jobs), encoding="utf-8")
    subprocess.run([str(MOSS_PYTHON), str(TOOLS / "moss_worker.py"), str(job_file)], cwd=TOOLS, env=os.environ.copy())


if __name__ == "__main__":
    main()
