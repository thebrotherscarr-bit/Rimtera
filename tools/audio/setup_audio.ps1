# One-time setup for the audio generators (docs/AUDIO_FLOW.md). Free, open-source models only:
#   - ACE-Step 1.5 turbo (MIT) for music, run by ComfyUI's native nodes: diffusion_models/acestep_v1.5_turbo.safetensors,
#     text_encoders/qwen_0.6b_ace15.safetensors and qwen_1.7b_ace15.safetensors, vae/ace_1.5_vae.safetensors
#     (Comfy-Org/ace_step_1.5_ComfyUI_files on Hugging Face), in the shared ComfyUI models folder.
#   - MOSS-SoundEffect v2.0 (Apache 2.0) for sound effects and ambience: OpenMOSS-Team/MOSS-SoundEffect-v2.0 in
#     models/diffusers/MOSS-SoundEffect-v2.0, and its code (github.com/OpenMOSS/MOSS-TTS) in Rimtera/vendor/MOSS-TTS.
# MOSS pins transformers 4.57, which ComfyUI's environment cannot take, so it gets its own venv that borrows ComfyUI's
# ROCm torch through a .pth file and adds only its own packages on top. Safe to re-run.
$root = Join-Path $PSScriptRoot "..\..\Rimtera" | Resolve-Path
$venv = Join-Path $root "vendor\moss-venv"
$comfySite = Join-Path $root "ComfyUI\.venv\Lib\site-packages"
if (-not (Test-Path "$venv\Scripts\python.exe")) { & "$root\standalone-env\python.exe" -m venv $venv }
Set-Content -Path "$venv\Lib\site-packages\zz_comfy_shared.pth" -Value $comfySite -Encoding ascii
if (-not (Test-Path "$root\vendor\MOSS-TTS")) { git clone --depth 1 https://github.com/OpenMOSS/MOSS-TTS.git "$root\vendor\MOSS-TTS" }
# Pin the shared packages to ComfyUI's versions so pip never replaces the ROCm torch.
$constraints = Join-Path $root "vendor\moss-constraints.txt"
& "$comfySite\..\..\Scripts\python.exe" -m pip freeze 2>$null | Select-String -Pattern '^(torch|torchaudio|torchvision|numpy|scipy)==' | ForEach-Object { $_.Line } | Set-Content $constraints
& "$venv\Scripts\python.exe" -m pip install -q -c $constraints "transformers==4.57.1" "diffusers==0.37.1" "ftfy==6.3.1" "soundfile==0.13.1" "descript-audiotools==0.7.2" "einops==0.8.2"
& "$venv\Scripts\python.exe" -m pip install -q --no-deps -e "$root\vendor\MOSS-TTS\moss_soundeffect_v2"
& "$venv\Scripts\python.exe" -c "import torch; from moss_soundeffect_v2 import MossSoundEffectPipeline; print('MOSS ready on', torch.cuda.get_device_name(0))"
