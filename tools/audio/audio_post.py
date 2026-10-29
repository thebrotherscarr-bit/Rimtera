"""Post-processes generated takes into game-ready candidates (docs/AUDIO_FLOW.md, step 5). Run with the MOSS venv
python (it has numpy, scipy, soundfile and pyloudnorm):

    Rimtera/vendor/moss-venv/Scripts/python.exe tools/audio/audio_post.py [--redo]

For every take in tools/audio/raw/<category>/ it writes tools/audio/candidates/<category>/<same name>.wav:
- DC removed; sound effects trimmed of leading and trailing silence with short fades, and made mono
- loops (ambience, music, looping effects) get their tail crossfaded into the head so they repeat without a click
- 44.1 kHz 16-bit WAV, levelled to -16 LUFS plus the cue's offset, peaks held under -1 dBFS
The game levels every clip again on load, so this mainly makes auditioning fair.
"""
import argparse
import sys

import numpy as np
import pyloudnorm
import soundfile
from scipy.signal import resample_poly

from audio_common import CANDIDATES, RAW, load_needs

RATE = 44100
CEILING = 10 ** (-1 / 20)


def trim(audio, rate, floor_db=-50.0):
    level = np.abs(audio).max(axis=1) if audio.ndim > 1 else np.abs(audio)
    loud = np.nonzero(level > 10 ** (floor_db / 20) * max(level.max(), 1e-9))[0]
    if len(loud) == 0:
        return audio
    start = max(0, loud[0] - int(0.005 * rate))
    end = min(len(audio), loud[-1] + int(0.05 * rate))
    return audio[start:end]


def fade(audio, rate, fade_in=0.003, fade_out=0.02):
    n_in, n_out = min(len(audio), int(fade_in * rate)), min(len(audio), int(fade_out * rate))
    ramp = lambda n: np.linspace(0, 1, n)[:, None] if audio.ndim > 1 else np.linspace(0, 1, n)
    if n_in:
        audio[:n_in] *= ramp(n_in)
    if n_out:
        audio[-n_out:] *= ramp(n_out)[::-1]
    return audio


def make_loop(audio, rate, seconds):
    """Crossfades the last `seconds` into the first: the result ends exactly where it began."""
    n = min(int(seconds * rate), len(audio) // 3)
    if n < 2:
        return audio
    curve = np.sqrt(np.linspace(0, 1, n))
    if audio.ndim > 1:
        curve = curve[:, None]
    head, tail = audio[:n], audio[-n:]
    looped = audio[:-n].copy()
    looped[:n] = head * curve + tail * curve[::-1]
    return looped


def level(audio, rate, target):
    meter = pyloudnorm.Meter(rate)
    padded = audio if len(audio) >= rate // 2 else np.concatenate([audio, np.zeros((rate // 2 - len(audio),) + audio.shape[1:])])
    try:
        loudness = meter.integrated_loudness(padded)
    except ValueError:
        loudness = -70.0
    if np.isfinite(loudness) and loudness > -70:
        audio = audio * 10 ** ((target - loudness) / 20)
    peak = np.abs(audio).max()
    if peak > CEILING:  # soft knee: keep the body, round off the peaks
        knee = CEILING * 0.6
        over = np.abs(audio) > knee
        audio[over] = np.sign(audio[over]) * (knee + (CEILING - knee) * np.tanh((np.abs(audio[over]) - knee) / (CEILING - knee)))
    return audio


def process(path, row, out):
    audio, rate = soundfile.read(str(path), always_2d=True, dtype="float64")
    category = row["category"]
    loop = row["loop"] == "yes"
    stereo = category in ("music", "ambience") and audio.shape[1] > 1
    audio = audio[:, :2] if stereo else audio.mean(axis=1)
    if rate != RATE:
        audio = resample_poly(audio, RATE, rate, axis=0)
        rate = RATE
    audio = audio - audio.mean(axis=0)
    if loop:
        audio = make_loop(audio, rate, 3.0 if category == "music" else 1.5)
    else:
        audio = fade(trim(audio, rate), rate)
    audio = level(audio, rate, -16.0 + float(row["offset"] or 0))
    out.parent.mkdir(parents=True, exist_ok=True)
    soundfile.write(str(out), audio.astype(np.float32), rate, subtype="PCM_16")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--redo", action="store_true")
    args = parser.parse_args()
    rows = {row["cue"]: row for row in load_needs()}
    done = failed = 0
    for path in sorted(RAW.rglob("*__c*.*")):
        if ".part" in path.name:
            continue
        cue = path.stem.rsplit("__c", 1)[0]
        row = rows.get(cue)
        if row is None:
            continue
        out = CANDIDATES / row["category"] / (path.stem + ".wav")
        if out.exists() and not args.redo and out.stat().st_mtime >= path.stat().st_mtime:
            continue
        try:
            process(path, row, out)
            done += 1
        except Exception as error:
            print(f"FAILED {path.name}: {error}", file=sys.stderr, flush=True)
            failed += 1
    print(f"post-processed {done} takes ({failed} failed) into {CANDIDATES}", flush=True)


if __name__ == "__main__":
    main()
