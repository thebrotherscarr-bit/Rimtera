"""Finishes one clip for a cue, as the overnight run does (audio_post.py), and optionally installs it in the game
(promote.py). Used by the Rimtera Finish Audio node in the ComfyUI app; runs in the MOSS venv (pyloudnorm, soundfile):

    tools/moss-venv/Scripts/python.exe tools/audio/finish_one.py in.wav <cue> out.wav [--install]

Levels, trims or loops the clip by the cue's needs row and writes out.wav. With --install it also goes into Assets/
as the cue's next take (an existing file, your recordings included, is never replaced) and is logged in promoted.tsv,
so --replace-generated can swap it later. Prints the installed file's path.
"""
import argparse
import csv
from pathlib import Path

from audio_common import load_needs
from audio_post import process
from promote import LOG, promote


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("source")
    parser.add_argument("cue")
    parser.add_argument("out")
    parser.add_argument("--install", action="store_true")
    args = parser.parse_args()
    row = next((row for row in load_needs() if row["cue"] == args.cue), None)
    if row is None:
        raise SystemExit(f"unknown cue {args.cue!r}: see tools/audio/audio_needs.tsv")
    out = Path(args.out)
    process(Path(args.source), row, out)
    if args.install:
        new_log = not LOG.exists()
        with LOG.open("a", encoding="utf-8", newline="") as handle:
            log = csv.writer(handle, delimiter="\t", lineterminator="\n")
            if new_log:
                log.writerow(["cue", "file", "candidate"])
            promote(out, row, log)


if __name__ == "__main__":
    main()
