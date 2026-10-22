# Starts the local ComfyUI (Comfy Desktop's "Rimtera" install) headless on http://127.0.0.1:8188 for tools/comfy.
# Uses the Desktop app's model paths. rocBLAS is preferred because hipBLASLt does not support RDNA2 GPUs; int8
# Dynamic VRAM streams weights every step on 16 GB cards (Qwen model + text encoder do not fit together), so it is
# disabled: the text encoder is evicted while the diffusion model samples fully resident. int8 models also need ComfyUI/custom_nodes/rocm_int8_fallback.py (installed alongside this setup).
# Sharing the GPU (e.g. watching video while art generates): -ReserveVramGB keeps that much VRAM free for other
# programs, and the server runs at below-normal priority. Use -ReserveVramGB 0 -Priority Normal for full speed.
param([double]$ReserveVramGB = 4, [string]$Priority = "BelowNormal")
$root = Join-Path $PSScriptRoot "..\..\Rimtera" | Resolve-Path
$config = '"' + (Join-Path $env:APPDATA "Comfy Desktop\instance-model-paths\inst-1792547457667.yaml") + '"'
$shared = Join-Path $env:LOCALAPPDATA "Comfy-Desktop\ComfyUI-Shared"
$log = Join-Path $env:TEMP "rimtera-comfy.log"
$env:TORCH_BLAS_PREFER_HIPBLASLT = "0"
$reserve = if ($ReserveVramGB -gt 0) { "--reserve-vram $ReserveVramGB " } else { "" }
$arguments = "-s ComfyUI\main.py --listen 127.0.0.1 --port 8188 --disable-dynamic-vram --disable-fast-disk $reserve--extra-model-paths-config $config " +
             "--input-directory `"$shared\input`" --output-directory `"$shared\output`""
$process = Start-Process -FilePath "$root\ComfyUI\.venv\Scripts\python.exe" -ArgumentList $arguments -WorkingDirectory $root `
    -RedirectStandardOutput $log -RedirectStandardError "$log.err" -WindowStyle Hidden -PassThru
$process.PriorityClass = $Priority
"ComfyUI starting (pid $($process.Id), priority $Priority, reserved VRAM $ReserveVramGB GB); log: $log.err"
