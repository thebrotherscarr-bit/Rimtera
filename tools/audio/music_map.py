"""Draws a map of a music take, to see its sections before cutting or recomposing it. Run with the MOSS venv python:

    tools/moss-venv/Scripts/python.exe tools/audio/music_map.py tools/audio/raw/music/mus_title__c8.flac [out.png]

Panels, all on one time axis with the bar grid (numbered) laid over them: loudness, a mel spectrogram, the low
harmonic band where a deep choir sits (harmonic part only, 80-500 Hz) next to the drums (percussive part), and the
chroma (which notes sound). The beat grid comes from librosa's tracker, started at the cue's tempo; bars group four
beats from the strongest downbeat. Prints the bar start times, which loop_cut.py and recompose.py work in.
"""
import sys
from pathlib import Path

import librosa
import librosa.display
import matplotlib
import numpy as np

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402


def bar_grid(y, sr, bpm, meter=4):
    """Beat times from the tracker and the bar starts (every `meter` beats, phase picked by onset strength)."""
    onset = librosa.onset.onset_strength(y=y, sr=sr)
    _, beats = librosa.beat.beat_track(onset_envelope=onset, sr=sr, start_bpm=bpm, tightness=400)
    phase = max(range(meter), key=lambda p: onset[beats[p::meter]].sum() if len(beats[p::meter]) else 0)
    times = librosa.frames_to_time(beats, sr=sr)
    return times, times[phase::meter]


def main():
    path = Path(sys.argv[1])
    out = Path(sys.argv[2]) if len(sys.argv) > 2 else path.with_suffix(".map.png")
    bpm = float(sys.argv[3]) if len(sys.argv) > 3 else 76
    y, sr = librosa.load(str(path), sr=22050, mono=True)
    beats, bars = bar_grid(y, sr, bpm)
    harmonic, percussive = librosa.effects.hpss(y)
    hop = 512
    t = librosa.frames_to_time(np.arange(1 + len(y) // hop), sr=sr, hop_length=hop)
    rms = librosa.amplitude_to_db(librosa.feature.rms(y=y, hop_length=hop)[0], ref=1.0)
    spec = np.abs(librosa.stft(harmonic, hop_length=hop))
    freqs = librosa.fft_frequencies(sr=sr)
    low = librosa.amplitude_to_db(spec[(freqs >= 80) & (freqs <= 500)].mean(axis=0), ref=1.0)
    drums = librosa.amplitude_to_db(librosa.feature.rms(y=percussive, hop_length=hop)[0], ref=1.0)

    fig, axes = plt.subplots(4, 1, figsize=(22, 13), sharex=True, gridspec_kw={"height_ratios": [1, 2, 1, 1.4]})
    axes[0].plot(t[:len(rms)], rms, lw=0.7)
    axes[0].set_ylabel("loudness dB")
    mel = librosa.amplitude_to_db(librosa.feature.melspectrogram(y=y, sr=sr, hop_length=hop, n_mels=96), ref=np.max)
    librosa.display.specshow(mel, sr=sr, hop_length=hop, x_axis="time", y_axis="mel", ax=axes[1], cmap="magma")
    axes[1].set_ylabel("mel Hz")
    axes[2].plot(t[:len(low)], low, lw=0.7, label="low harmonic 80-500 Hz (choir)")
    axes[2].plot(t[:len(drums)], drums, lw=0.7, alpha=0.7, label="percussive (drums)")
    axes[2].legend(loc="upper left", fontsize=8)
    axes[2].set_ylabel("dB")
    chroma = librosa.feature.chroma_cqt(y=harmonic, sr=sr, hop_length=hop)
    librosa.display.specshow(chroma, sr=sr, hop_length=hop, x_axis="time", y_axis="chroma", ax=axes[3], cmap="viridis")
    for ax in axes:
        for number, start in enumerate(bars, 1):
            ax.axvline(start, color="white" if ax in (axes[1], axes[3]) else "grey", lw=0.5, alpha=0.6)
    top = axes[0].get_ylim()[1]
    for number, start in enumerate(bars, 1):
        axes[0].text(start, top, str(number), fontsize=7, va="bottom", ha="center")
    axes[3].set_xticks(np.arange(0, len(y) / sr, 5))
    fig.suptitle(f"{path.name}: {len(bars)} bars, about {60 / np.median(np.diff(beats)):.1f} BPM")
    fig.tight_layout()
    fig.savefig(out, dpi=80)
    print(f"map: {out}")
    print("bars (number: start s):", " ".join(f"{i}:{s:.2f}" for i, s in enumerate(bars, 1)))


if __name__ == "__main__":
    main()
