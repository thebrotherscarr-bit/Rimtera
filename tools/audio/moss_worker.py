"""Sound-effect and ambience generator: MOSS-SoundEffect v2.0 (Apache 2.0), run in its own venv.

MOSS pins transformers 4.57, which conflicts with ComfyUI's environment, so it runs beside ComfyUI instead of
inside it: Rimtera/vendor/moss-venv shares ComfyUI's ROCm torch (a .pth file) and adds its own transformers.
Set up by tools/audio/setup_audio.ps1. Called by gen_sfx.py and gen_ambience.py; run it directly to test:

    Rimtera/vendor/moss-venv/Scripts/python.exe tools/audio/moss_worker.py jobs.jsonl
    Rimtera/vendor/moss-venv/Scripts/python.exe tools/audio/moss_worker.py --prompt "laser pistol shot" --seconds 2 --out test.wav

Each line of jobs.jsonl is {"prompt", "seconds", "seed", "out"}. Jobs whose file exists are skipped, so a stopped
run resumes. Output is 48 kHz mono WAV; audio_post.py levels and converts it. The model loads once per run.
RIMTERA_AUDIO_PAUSE rests the GPU that many seconds after each clip (as RIMTERA_COMFY_PAUSE does for art).
"""
import argparse
import json
import math
import os
import sys
import time
from pathlib import Path

os.environ.setdefault("TORCHDYNAMO_DISABLE", "1")  # torch.compile + Triton is CUDA-only; ROCm on Windows runs eager.
MODEL_DIR = Path(os.environ.get("LOCALAPPDATA", "")) / "Comfy-Desktop" / "ComfyUI-Shared" / "models" / "diffusers" / "MOSS-SoundEffect-v2.0"
NEGATIVE = "speech, talking, voice, singing, music, low quality, distorted, muffled, noise"


def set_power(torch):
    """RIMTERA_PRIORITY (normal | below), RIMTERA_VRAM_FREE_GB (VRAM left for other programs) and RIMTERA_CPU_FRACTION
    come from overnight_audio.py; run alone, the worker stays restricted (below-normal priority, 4 GB free, 75% CPU)."""
    if sys.platform == "win32" and os.environ.get("RIMTERA_PRIORITY", "below") != "normal":
        import ctypes
        ctypes.windll.kernel32.SetPriorityClass(ctypes.windll.kernel32.GetCurrentProcess(), 0x4000)  # BELOW_NORMAL
    total = torch.cuda.get_device_properties(0).total_memory / 2**30
    free = float(os.environ.get("RIMTERA_VRAM_FREE_GB", "4"))
    torch.cuda.set_per_process_memory_fraction(max(0.3, (total - free) / total))
    torch.set_num_threads(max(1, int((os.cpu_count() or 4) * float(os.environ.get("RIMTERA_CPU_FRACTION", "0.75")))))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("jobs", nargs="?")
    parser.add_argument("--prompt")
    parser.add_argument("--seconds", type=float, default=2.0)
    parser.add_argument("--seed", type=int, default=0)
    parser.add_argument("--out")
    parser.add_argument("--steps", type=int, default=int(os.environ.get("RIMTERA_MOSS_STEPS", "50")))
    parser.add_argument("--cfg", type=float, default=4.0)
    args = parser.parse_args()

    if args.jobs:
        jobs = [json.loads(line) for line in Path(args.jobs).read_text(encoding="utf-8").splitlines() if line.strip()]
    else:
        jobs = [{"prompt": args.prompt, "seconds": args.seconds, "seed": args.seed, "out": args.out}]
    jobs = [job for job in jobs if not Path(job["out"]).exists()]
    if not jobs:
        print("nothing to generate", flush=True)
        return

    import torch
    set_power(torch)
    from moss_soundeffect_v2 import MossSoundEffectPipeline

    started = time.time()
    pipe = MossSoundEffectPipeline.from_pretrained(str(MODEL_DIR), torch_dtype=torch.bfloat16, device="cuda")
    print(f"model loaded in {time.time() - started:.0f}s; {len(jobs)} clips", flush=True)
    pause = float(os.environ.get("RIMTERA_AUDIO_PAUSE", "0") or 0)
    for index, job in enumerate(jobs, start=1):
        clip_started = time.time()
        try:
            seconds = min(float(job["seconds"]), pipe.max_inference_seconds)
            # The model denoises a fixed window (30 s by default) and crops it; sizing the window to the clip is
            # many times faster on short sounds.
            window = min(pipe.max_inference_seconds, math.ceil(seconds) + int(os.environ.get("RIMTERA_MOSS_PAD", "1")))
            audio = pipe(prompt=job["prompt"], seconds=seconds, max_inference_seconds=window,
                         num_inference_steps=args.steps, cfg_scale=args.cfg, seed=int(job.get("seed", 0)),
                         negative_prompt=job.get("negative", NEGATIVE), progress_bar_cmd=lambda x, **_: x)
            out = Path(job["out"])
            out.parent.mkdir(parents=True, exist_ok=True)
            partial = out.with_suffix(".part.wav")
            # soundfile, not pipe.save_audio: newer torchaudio needs torchcodec to write files.
            import soundfile
            soundfile.write(str(partial), audio[0].float().cpu().numpy().T, pipe.sample_rate, subtype="PCM_16")
            partial.replace(out)
            print(f"[{index}/{len(jobs)}] {out.name} ({time.time() - clip_started:.0f}s)", flush=True)
        except Exception as error:  # one bad clip should not end an overnight run
            print(f"[{index}/{len(jobs)}] FAILED {job['out']}: {error}", flush=True)
            torch.cuda.empty_cache()
        if pause:
            time.sleep(pause)


if __name__ == "__main__":
    main()
