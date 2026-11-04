"""Cuts a short seamless loop out of generated music takes. Run with the MOSS venv python:

    tools/moss-venv/Scripts/python.exe tools/audio/loop_cut.py mus_title__c8 [--min 30] [--max 40] [--include 125-140]
    tools/moss-venv/Scripts/python.exe tools/audio/loop_cut.py --all [--cues mus_frozen_day,...] [--redo]

Works from the uncut take in tools/audio/raw/music/ (nothing is spliced). The bar grid is measured from the take
(music_map.bar_grid), never taken from the cue's tempo: the model rarely plays at the tempo it is asked for. Every
candidate loop starts and ends on a measured bar line and lasts --min to --max seconds. Stretches with dead space (a
second well under the track's usual level) or a dropout (0.4 s near silence, such as the take cutting off)
are skipped. Of the rest it keeps the one whose last bar leads best into its first (same sound, same level), favouring
loud, full stretches and, most of all, stretches where the chant sings (the vocal stem from music_map.stem_levels). The join is crossfaded over half a measured beat, with the
music that follows the loop laid over its head so the repeat carries on the way the take would have.

--include A-B (seconds in the raw take) keeps a moment you want, such as a vocal, inside the loop; dead space is
allowed within it. --all cuts a loop for every music take that has none yet (--redo: all of them; --cues: only those
slots); locked slots (the title theme) are left alone. Writes tools/audio/candidates/music/<take>loop.wav, which the
audition page lists and promote.py --keep can put in the game.
"""
import argparse

import librosa
import numpy as np
import soundfile
from scipy.signal import resample_poly

from audio_common import CANDIDATES, RAW, load_needs
from audio_post import RATE, level
from music_map import HOP_SECONDS, bar_grid, presence, stem_levels

LOCKED = {"mus_title"}  # finished by hand (recompose.py); never re-cut
ANALYSIS_RATE = 22050


def windowed_db(mono, rate, window, hop):
    frames = librosa.util.frame(np.pad(mono, (0, window)), frame_length=window, hop_length=hop)
    return 10 * np.log10(np.mean(frames ** 2, axis=0) + 1e-12)


def spectrum(audio, at, size):
    """Magnitude spectrum of `size` samples from `at`, as a unit vector (for comparing two moments of the track)."""
    piece = audio[at:at + size]
    shape = np.abs(np.fft.rfft(piece * np.hanning(len(piece))))
    return shape / (np.linalg.norm(shape) + 1e-12)


def cut(take, row, low=30.0, high=40.0, include=None):
    source = next((RAW / "music").glob(take + ".*"))
    audio, rate = soundfile.read(str(source), always_2d=True, dtype="float64")
    audio = audio[:, :2]
    if rate != RATE:
        audio = resample_poly(audio, RATE, rate, axis=0)
    audio = audio - audio.mean(axis=0)
    mono = audio.mean(axis=1)

    # Measured grid, from the same tracker music_map.py draws.
    small = resample_poly(mono, ANALYSIS_RATE, RATE)
    beats, bars = bar_grid(small, ANALYSIS_RATE, float(row["bpm"]), int(row["meter"] or 4))
    if len(bars) < 3:
        raise ValueError("no steady beat found")
    beat = float(np.median(np.diff(beats))) * RATE
    bars = (np.asarray(bars) * RATE).astype(int)

    # Dead space: any second 9 dB under the track's median. Dropouts: any 0.4 s 20 dB under it (long enough that the
    # rests in a staccato groove don't count, short enough to catch a take cutting off).
    hop = RATE // 8
    second = windowed_db(mono, RATE, RATE, hop)
    instant = windowed_db(mono, RATE, RATE * 2 // 5, hop)
    median = float(np.median(second))
    # Where the chant sings: the vocal stem (Demucs, cached by music_map.stem_levels) within 20 dB of the whole mix.
    singing = presence(stem_levels(source), "vocals") > -20

    def sung(start, end):
        return float(singing[int(start / RATE / HOP_SECONDS):max(int(end / RATE / HOP_SECONDS), 1)].mean())

    keep = [int(float(x) * RATE) for x in include.split("-")] if include else None
    size = int(round(bars[1] - bars[0]))  # compare a whole bar at each end
    best = None
    for i, start in enumerate(bars):
        for end in bars[i + 1:]:
            length = int(end - start)
            if length < low * RATE:
                continue
            if length > high * RATE or end + int(beat) > len(mono):
                break
            if keep and not (start <= keep[0] and end >= keep[1]):
                continue
            a, b = start // hop, (end - RATE) // hop + 1
            if not keep and (second[a:b].min() < median - 9 or instant[a:end // hop].min() < median - 20):
                continue
            match = float(spectrum(mono, start, size) @ spectrum(mono, end - size, size))
            level_gap = abs(float(second[start // hop] - second[max(a, (end - RATE) // hop)]))
            voice = sung(start, end)
            score = match - 0.03 * level_gap + 0.01 * float(second[a:b].mean() - median) + 0.5 * voice
            if best is None or score > best[0]:
                best = (score, int(start), length, match, voice)
    if best is None:
        raise ValueError(f"no {low:g}-{high:g} s stretch without dead space")
    _, start, length, match, voice = best
    fade = int(beat / 2)
    loop = audio[start:start + length + fade].copy()
    curve = np.sqrt(np.linspace(0, 1, fade))[:, None]
    loop[:fade] = loop[:fade] * curve + loop[-fade:] * curve[::-1]
    loop = loop[:length]
    loop = level(loop, RATE, -16.0 + float(row["offset"] or 0))
    out = CANDIDATES / "music" / f"{take}loop.wav"
    out.parent.mkdir(parents=True, exist_ok=True)
    soundfile.write(str(out), loop.astype(np.float32), RATE, subtype="PCM_16")
    tempo = 60 * RATE / beat
    return (f"{out.name}: {length / RATE:.1f} s from {start / RATE:.1f} s at {tempo:.0f} BPM, end-to-start match {match:.2f}, "
            f"chant in {voice:.0%} of it")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("take", nargs="?", help="raw take name without extension, e.g. mus_frozen_day__c2")
    parser.add_argument("--min", type=float, default=30.0)
    parser.add_argument("--max", type=float, default=40.0)
    parser.add_argument("--include", help="A-B: seconds of the raw take the loop must contain")
    parser.add_argument("--all", action="store_true", help="every music take without a loop yet")
    parser.add_argument("--cues", help="with --all: only these slots (comma-separated)")
    parser.add_argument("--redo", action="store_true", help="with --all: cut again even where a loop exists")
    args = parser.parse_args()
    rows = {row["cue"]: row for row in load_needs(["music"])}
    if not args.all:
        try:
            print(cut(args.take, rows[args.take.rsplit("__c", 1)[0]], args.min, args.max, args.include), flush=True)
        except ValueError as error:
            raise SystemExit(f"{args.take}: {error}")
        return
    cues = set(args.cues.split(",")) if args.cues else None
    takes = sorted({path.stem for path in (RAW / "music").glob("*__c*.*") if path.stem.rsplit("__c", 1)[-1].isdigit()})
    done = failed = 0
    for take in takes:
        cue = take.rsplit("__c", 1)[0]
        if cue in LOCKED or cue not in rows or (cues and cue not in cues):
            continue
        if not args.redo and (CANDIDATES / "music" / f"{take}loop.wav").exists():
            continue
        try:
            print(cut(take, rows[cue], args.min, args.max), flush=True)
            done += 1
        except (ValueError, OSError) as error:
            print(f"{take}: skipped ({error})", flush=True)
            failed += 1
    print(f"loops: {done} cut, {failed} skipped", flush=True)


if __name__ == "__main__":
    main()
