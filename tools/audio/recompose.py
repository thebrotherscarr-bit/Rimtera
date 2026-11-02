"""Builds a loop from chosen pieces of a music take, joined on bar lines. Run with the MOSS venv python:

    tools/moss-venv/Scripts/python.exe tools/audio/recompose.py mus_title__c8 61.51-79.97,127.97-146.43 --ring 146.95
    ... recompose.py mus_title__c8 139.04-146.43:+8.5,61.51-91.05

Find the pieces with music_map.py (bar times, sections) first. A piece can carry a gain in dB (A-B:+8.5) to bring a
quiet section up to the music around it, so the loop never drops away; the audio itself is left untouched. Each piece is copied from the uncut raw take; the joins
are equal-power crossfades a quarter beat long, centred on the cut, and --ring (where the last piece's sound really
dies away) lays that ring-out over the loop's head, so the end carries into the start the way it would in the take.
Writes tools/audio/candidates/music/<take>mix.wav, levelled like every other candidate, for the audition page and
promote.py --keep.

--overlay A-B@T mixes seconds A-B of the take on top of the loop at T seconds into it (a vocal over the theme, say);
--clean C-D first subtracts the steady sound heard in C-D (the drone under that vocal) from it, and --overlay-db sets its
level against the music it lands on.
"""
import argparse

import numpy as np
import soundfile
from scipy.signal import istft, resample_poly, stft

from audio_common import CANDIDATES, RAW, load_needs
from audio_post import RATE, level


def rms_db(audio):
    return 10 * np.log10(np.mean(audio ** 2) + 1e-12)


def subtract(audio, noise, rate):
    """Spectral subtraction: removes the average spectrum of `noise` (a steady drone) from each channel of `audio`."""
    out = np.zeros_like(audio)
    for channel in range(audio.shape[1]):
        _, _, spec = stft(audio[:, channel], rate, nperseg=2048)
        _, _, hum = stft(noise[:, channel], rate, nperseg=2048)
        profile = np.abs(hum).mean(axis=1, keepdims=True) * 1.5
        magnitude = np.maximum(np.abs(spec) - profile, 0)
        _, clean = istft(magnitude * np.exp(1j * np.angle(spec)), rate, nperseg=2048)
        out[:, channel] = clean[:len(audio)]
    return out


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("take", help="raw take name without extension, e.g. mus_title__c8")
    parser.add_argument("pieces", help="comma-separated start-end seconds, in playing order")
    parser.add_argument("--ring", type=float, help="where the last piece's sound has died away (seconds in the take)")
    parser.add_argument("--fade", type=float, default=0.23, help="crossfade length at each join, in seconds")
    parser.add_argument("--overlay", help="A-B@T: seconds A-B of the take, mixed over the loop at T seconds")
    parser.add_argument("--clean", help="C-D: a stretch of steady sound to subtract from the overlay first")
    parser.add_argument("--overlay-db", type=float, default=-4.0, help="overlay level against the music under it")
    args = parser.parse_args()
    cue = args.take.rsplit("__c", 1)[0]
    row = next(row for row in load_needs(["music"]) if row["cue"] == cue)
    source = next((RAW / "music").glob(args.take + ".*"))
    audio, rate = soundfile.read(str(source), always_2d=True, dtype="float64")
    audio = audio[:, :2]
    if rate != RATE:
        audio = resample_poly(audio, RATE, rate, axis=0)
    audio = audio - audio.mean(axis=0)

    half = int(args.fade * RATE / 2)
    pieces = []
    for piece in args.pieces.split(","):
        span, _, gain = piece.partition(":")
        start, end = (int(float(x) * RATE) for x in span.split("-"))
        pieces.append((start, end, 10 ** (float(gain or 0) / 20)))
    rise = np.sqrt(np.linspace(0, 1, 2 * half))[:, None]
    out = audio[pieces[0][0]:pieces[0][1] + half] * pieces[0][2]
    for start, end, gain in pieces[1:]:
        # The outgoing piece plays half a fade past its cut and the incoming one starts half a fade early.
        incoming = audio[max(0, start - half):end + half] * gain
        out[-2 * half:] = out[-2 * half:] * rise[::-1] + incoming[:2 * half] * rise
        out = np.concatenate([out, incoming[2 * half:]])
    out = out[:len(out) - half]
    if args.ring:
        ring = audio[pieces[-1][1]:int(args.ring * RATE)] * pieces[-1][2]
        n = min(len(ring), len(out))
        out[:n] += ring[:n]
    if args.overlay:
        span, at = args.overlay.split("@")
        a, b = (int(float(x) * RATE) for x in span.split("-"))
        voice = audio[a:b].copy()
        if args.clean:
            c, d = (int(float(x) * RATE) for x in args.clean.split("-"))
            voice = subtract(voice, audio[c:d], RATE)
        at = int(float(at) * RATE)
        n = min(len(voice), len(out) - at)
        voice = voice[:n]
        edge = min(int(0.05 * RATE), n // 2)
        voice[:edge] *= np.linspace(0, 1, edge)[:, None]
        voice[-edge:] *= np.linspace(1, 0, edge)[:, None]
        voice *= 10 ** ((rms_db(out[at:at + n]) + args.overlay_db - rms_db(voice)) / 20)
        out[at:at + n] += voice
    out = level(out, RATE, -16.0 + float(row["offset"] or 0))
    path = CANDIDATES / "music" / f"{args.take}mix.wav"
    soundfile.write(str(path), out.astype(np.float32), RATE, subtype="PCM_16")
    print(f"{path.name}: {len(out) / RATE:.2f} s from {len(pieces)} pieces")


if __name__ == "__main__":
    main()
