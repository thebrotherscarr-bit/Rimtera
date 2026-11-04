"""Draws a map of a music take, to see its sections before cutting or recomposing it. Run with the MOSS venv python:

    tools/moss-venv/Scripts/python.exe tools/audio/music_map.py tools/audio/raw/music/mus_title__c8.flac [out.png] [bpm] [--mark A-B]

or drop tracks on tools/audio/music_map.cmd, which saves <name>.map.png next to each one and opens it.

The take is first split into stems with Demucs (htdemucs, MIT licence, on the GPU): vocals (the chant), drums, bass, and
ambience (everything else: pads, drones, strings, leads). The split is cached in tools/audio/candidates/music/stems/,
so a second map of the same take is quick.

Panels, all on one time axis with the bar grid (numbered) laid over them: loudness, a mel spectrogram, one lane per
stem (how loud it is; empty where that part is silent), and the chroma (which notes sound). --mark A-B shades seconds
A-B, such as the stretch loop_cut.py chose. The beat grid comes from librosa's tracker, started at the cue's tempo;
bars group four beats from the strongest downbeat.

Prints the bar start times, which loop_cut.py and recompose.py work in, and a bar-by-bar sheet of which stems play
(V vocals, D drums, B bass, A ambience; capitals loud, lower case quiet, "." silent) and the bar's loudness.
"""
import os
import sys
from pathlib import Path

os.environ.setdefault("MIOPEN_LOG_LEVEL", "3")

import librosa  # noqa: E402
import librosa.display  # noqa: E402
import matplotlib  # noqa: E402
import numpy as np  # noqa: E402

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402

STEMS = Path(__file__).resolve().parent / "candidates" / "music" / "stems"
LANES = [("vocals", "V", "tab:red"), ("drums", "D", "tab:orange"), ("bass", "B", "tab:purple"), ("other", "A", "tab:green")]
LABELS = {"vocals": "vocals (chant)", "drums": "drums", "bass": "bass", "other": "ambience (pads, drone, strings, leads)"}
HOP_SECONDS = 512 / 22050


def bar_grid(y, sr, bpm, meter=4):
    """Beat times from the tracker and the bar starts (every `meter` beats, phase picked by onset strength)."""
    onset = librosa.onset.onset_strength(y=y, sr=sr)
    _, beats = librosa.beat.beat_track(onset_envelope=onset, sr=sr, start_bpm=bpm, tightness=400)
    phase = max(range(meter), key=lambda p: onset[beats[p::meter]].sum() if len(beats[p::meter]) else 0)
    times = librosa.frames_to_time(beats, sr=sr)
    return times, times[phase::meter]


def stem_levels(path):
    """Loudness in dB of each Demucs stem, one value every HOP_SECONDS. Cached per take."""
    STEMS.mkdir(parents=True, exist_ok=True)
    cache = STEMS / f"{path.stem}.npz"
    if cache.exists() and cache.stat().st_mtime >= path.stat().st_mtime:
        levels = dict(np.load(cache))
        if "mix" in levels:
            return levels
    import torch
    from demucs.apply import apply_model
    from demucs.pretrained import get_model

    model = get_model("htdemucs")
    model.eval()
    audio, _ = librosa.load(str(path), sr=model.samplerate, mono=False)
    audio = np.atleast_2d(audio)
    if audio.shape[0] == 1:
        audio = np.repeat(audio, 2, axis=0)
    wav = torch.tensor(audio[:2], dtype=torch.float32)
    ref = wav.mean(0)
    wav = (wav - ref.mean()) / (ref.std() + 1e-8)
    device = "cuda" if torch.cuda.is_available() else "cpu"
    with torch.no_grad():
        sources = apply_model(model, wav[None], device=device, split=True, overlap=0.25, progress=False)[0]
    sources = (sources * ref.std() + ref.mean()).cpu().numpy()
    hop = int(round(HOP_SECONDS * model.samplerate))
    levels = {name: librosa.amplitude_to_db(librosa.feature.rms(y=sources[i].mean(axis=0), frame_length=4 * hop, hop_length=hop)[0], ref=1.0)
              for i, name in enumerate(model.sources)}
    levels["mix"] = librosa.amplitude_to_db(librosa.feature.rms(y=audio[:2].mean(axis=0), frame_length=4 * hop, hop_length=hop)[0], ref=1.0)
    np.savez_compressed(cache, **levels)
    return levels


def presence(levels, name):
    """A stem's level against the whole mix at the same moment, in dB (0: it is the whole mix; -30: buried or absent)."""
    size = min(len(levels[name]), len(levels["mix"]))
    return levels[name][:size] - np.maximum(levels["mix"][:size], float(np.max(levels["mix"])) - 40)


def bar_sheet(levels, rms, bars, end):
    """One line per bar: which stems play (a stem is loud within 8 dB of the mix, quiet within 20), and the bar's loudness."""
    # Bar 0 is whatever comes before the first downbeat the tracker found (often an intro with no beat).
    intro = len(bars) > 0 and bars[0] > 1
    edges = [0.0, *bars, end] if intro else [*bars, end]
    first = 0 if intro else 1
    lines = []
    for number, (start, stop) in enumerate(zip(edges, edges[1:]), first):
        a, b = int(start / HOP_SECONDS), max(int(stop / HOP_SECONDS), int(start / HOP_SECONDS) + 1)
        marks = ""
        for name, letter, _ in LANES:
            share = presence(levels, name)
            level = float(np.mean(share[a:b])) if b <= len(share) else -100.0
            marks += letter if level > -8 else letter.lower() if level > -20 else "."
        loud = float(np.mean(rms[a:b])) if b <= len(rms) else -100.0
        lines.append(f"{number:>3} {start:7.2f}s {marks} {loud:6.1f} dB")
    return lines


def main():
    argv = sys.argv[1:]
    mark = None
    if "--mark" in argv:
        index = argv.index("--mark")
        mark = [float(x) for x in argv[index + 1].split("-")]
        del argv[index:index + 2]
    path = Path(argv[0])
    out = Path(argv[1]) if len(argv) > 1 else path.with_suffix(".map.png")
    bpm = float(argv[2]) if len(argv) > 2 else 76
    y, sr = librosa.load(str(path), sr=22050, mono=True)
    beats, bars = bar_grid(y, sr, bpm)
    levels = stem_levels(path)
    harmonic = librosa.effects.harmonic(y)
    hop = 512
    t = librosa.frames_to_time(np.arange(1 + len(y) // hop), sr=sr, hop_length=hop)
    rms = librosa.amplitude_to_db(librosa.feature.rms(y=y, hop_length=hop)[0], ref=1.0)

    ratios = [1, 2, *[0.7] * len(LANES), 1.4]
    fig, axes = plt.subplots(len(ratios), 1, figsize=(22, 16), sharex=True, gridspec_kw={"height_ratios": ratios})
    axes[0].plot(t[:len(rms)], rms, lw=0.7)
    axes[0].set_ylabel("loudness dB")
    mel = librosa.amplitude_to_db(librosa.feature.melspectrogram(y=y, sr=sr, hop_length=hop, n_mels=96), ref=np.max)
    librosa.display.specshow(mel, sr=sr, hop_length=hop, x_axis="time", y_axis="mel", ax=axes[1], cmap="magma")
    axes[1].set_ylabel("mel Hz")
    # Every lane on the same scale, from the mix's peak down 40 dB, so a missing part shows as an empty lane.
    top = float(np.max(levels["mix"]))
    for ax, (name, _, colour) in zip(axes[2:], LANES):
        level = levels[name]
        floor = top - 40
        times = np.arange(len(level)) * HOP_SECONDS
        ax.fill_between(times, floor, np.maximum(level, floor), color=colour, alpha=0.6, lw=0)
        ax.set_ylim(floor, top + 2)
        ax.set_yticks([])
        ax.set_ylabel(LABELS[name].split(" (")[0], rotation=0, ha="right", va="center")
        ax.text(0.002, 0.85, LABELS[name], transform=ax.transAxes, fontsize=8, color=colour)
    chroma = librosa.feature.chroma_cqt(y=harmonic, sr=sr, hop_length=hop)
    librosa.display.specshow(chroma, sr=sr, hop_length=hop, x_axis="time", y_axis="chroma", ax=axes[-1], cmap="viridis")
    dark = (axes[1], axes[-1])
    for ax in axes:
        for start in bars:
            ax.axvline(start, color="white" if ax in dark else "grey", lw=0.5, alpha=0.6)
    if mark:
        for ax in axes:
            ax.axvspan(*mark, color="lime", alpha=0.25 if ax in dark else 0.15)
        axes[0].text(sum(mark) / 2, axes[0].get_ylim()[0], f"loop {mark[1] - mark[0]:.1f} s", color="green", ha="center", va="bottom")
    top = axes[0].get_ylim()[1]
    for number, start in enumerate(bars, 1):
        axes[0].text(start, top, str(number), fontsize=7, va="bottom", ha="center")
    axes[-1].set_xticks(np.arange(0, len(y) / sr, 5))
    fig.suptitle(f"{path.name}: {len(bars)} bars, about {60 / np.median(np.diff(beats)):.1f} BPM")
    fig.tight_layout()
    fig.savefig(out, dpi=80)
    print(f"map: {out}")
    print("bars (number: start s):", " ".join(f"{i}:{s:.2f}" for i, s in enumerate(bars, 1)))
    print("bar   start  VDBA  loudness   (V vocals, D drums, B bass, A ambience; capital loud, lower quiet, . silent)")
    print("\n".join(bar_sheet(levels, rms, bars, len(y) / sr)))


if __name__ == "__main__":
    main()
