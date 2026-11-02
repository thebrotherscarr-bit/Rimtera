"""Cuts a short seamless loop out of a generated music take. Run with the MOSS venv python:

    tools/moss-venv/Scripts/python.exe tools/audio/loop_cut.py mus_title__c8 [--min 30] [--max 45] [--include 125-140]

Works from the uncut take in tools/audio/raw/music/ (no gap closing, so nothing is spliced). Every candidate stretch is a
whole number of bars at the cue's tempo, between --min and --max seconds, so the loop lands on the beat. Stretches with
a quiet patch are skipped; of the rest it keeps the one whose end sounds most like its start (and is loudest), then
crossfades the join over half a beat. --include A-B (seconds in the raw take) keeps a moment you want, such as a vocal,
inside the loop; quiet patches are allowed within it. Writes tools/audio/candidates/music/<take>loop.wav, which the audition page lists
and promote.py --keep can put in the game.
"""
import argparse

import numpy as np
import soundfile
from scipy.signal import resample_poly

from audio_common import CANDIDATES, RAW, load_needs
from audio_post import RATE, level


def spectrum(audio, at, size):
    """Magnitude spectrum of `size` samples from `at`, as a unit vector (for comparing two moments of the track)."""
    piece = audio[at:at + size]
    shape = np.abs(np.fft.rfft(piece * np.hanning(len(piece))))
    return shape / (np.linalg.norm(shape) + 1e-12)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("take", help="raw take name without extension, e.g. mus_title__c8")
    parser.add_argument("--min", type=float, default=30.0)
    parser.add_argument("--max", type=float, default=45.0)
    parser.add_argument("--include", help="A-B: seconds of the raw take the loop must contain")
    args = parser.parse_args()
    cue = args.take.rsplit("__c", 1)[0]
    row = next(row for row in load_needs(["music"]) if row["cue"] == cue)
    source = next((RAW / "music").glob(args.take + ".*"))
    audio, rate = soundfile.read(str(source), always_2d=True, dtype="float64")
    audio = audio[:, :2]
    if rate != RATE:
        audio = resample_poly(audio, RATE, rate, axis=0)
    audio = audio - audio.mean(axis=0)
    mono = audio.mean(axis=1)

    beat = RATE * 60 / float(row["bpm"])
    bar = beat * int(row["meter"] or 4)
    hop, window = RATE // 4, RATE
    loudness = np.array([10 * np.log10(np.mean(mono[i:i + window] ** 2) + 1e-12) for i in range(0, len(mono) - window + 1, hop)])
    floor = np.median(loudness) - 12
    size = int(beat)
    keep = [int(float(x) * RATE) for x in args.include.split("-")] if args.include else None
    best = None
    for bars in range(int(np.ceil(args.min * RATE / bar)), int(args.max * RATE / bar) + 1):
        length = int(round(bars * bar))
        for start in range(0, len(mono) - length, int(beat / 4)):
            if keep and not (start <= keep[0] and start + length >= keep[1]):
                continue
            span = loudness[start // hop:(start + length - window) // hop + 1]
            if len(span) == 0 or (not keep and span.min() < floor):
                continue
            match = float(spectrum(mono, start, size) @ spectrum(mono, start + length - size, size))
            score = match + 0.01 * float(span.mean())
            if best is None or score > best[0]:
                best = (score, start, length, bars, match)
    if best is None:
        raise SystemExit(f"no stretch of {args.min:g}-{args.max:g} s without a quiet patch in {source.name}")
    _, start, length, bars, match = best
    fade = int(beat / 2)
    # The half beat that follows the loop in the take is laid over its head, so on repeat the start carries on from the
    # end the way the music would have.
    loop = audio[start:start + length + fade].copy()
    curve = np.sqrt(np.linspace(0, 1, fade))[:, None]
    loop[:fade] = loop[:fade] * curve + loop[-fade:] * curve[::-1]
    loop = loop[:length]
    loop = level(loop, RATE, -16.0 + float(row["offset"] or 0))
    out = CANDIDATES / "music" / f"{args.take}loop.wav"
    soundfile.write(str(out), loop.astype(np.float32), RATE, subtype="PCM_16")
    print(f"{out.name}: {length / RATE:.1f} s ({bars} bars) from {start / RATE:.1f} s, end-to-start match {match:.2f}")


if __name__ == "__main__":
    main()
