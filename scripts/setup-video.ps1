<#
.SYNOPSIS
    Richtet Video ein (Bauplan Video): ComfyUI mit ComfyUI-GGUF und eigener Python-Umgebung, auf Wunsch die Modelle.

.DESCRIPTION
    1. Lädt ComfyUI (feste Version) und den Zusatz ComfyUI-GGUF (fester Commit) nach engines\comfyui.
       Kein ComfyUI-Manager: Der würde fremden Code per Klick installieren.
    2. Legt mit uv die Python-Umgebung engines\comfyui\.venv an (Pakete: python\video\pyproject.toml).
       PyTorch ist dieselbe Version wie beim Training. Liegt sie schon im uv-Cache, kostet das kaum Platz.
    3. Schreibt engines\comfyui\kinexus-paths.yaml: ComfyUI findet die Modelle in models\video.
    4. Nur mit -Models: lädt das offizielle LTX-2.3 (distilled, Q4_K_M) mit Text-Encoder und VAEs, zusammen ca. 24 GB.
       Nur mit -Companions: nur Text-Encoder, Projektion und VAEs (ca. 11 GB), z.B. für ein Videomodell von Hugging Face.
       Nur mit -Images: lädt das Bildmodell (Bauplan Bild) mit Text-Encoder und VAE nach models\image.
       Jede Datei wird mit ihrer SHA-256-Prüfsumme geprüft. Abgebrochene Downloads gehen beim nächsten Mal weiter.
    5. Prüft, ob PyTorch die Grafikkarte findet.
    Nichts wird systemweit installiert. engines\comfyui\ und models\video\ kann man jederzeit löschen.

.EXAMPLE
    .\scripts\setup-video.ps1
.EXAMPLE
    .\scripts\setup-video.ps1 -Models
.EXAMPLE
    .\scripts\setup-video.ps1 -Companions
.EXAMPLE
    .\scripts\setup-video.ps1 -Images
#>
param(
    [switch]$Models,
    [switch]$Companions,
    [switch]$Images
)
$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# Feste Versionen (in V0 getestet, siehe docs\10-Video.md).
$ComfyVersion = "v0.43.0"
$GgufCommit = "6ea2651e7df66d7585f6ffee804b20e92fb38b8a"

$Root = Split-Path -Parent $PSScriptRoot
$ComfyDir = Join-Path $Root "engines\comfyui"
$VideoDir = Join-Path $Root "python\video"
$ModelsDir = Join-Path $Root "models\video"
$ImageDir = Join-Path $Root "models\image"
$Marker = Join-Path $ComfyDir "version.txt"
$Wanted = "ComfyUI $ComfyVersion, ComfyUI-GGUF $GgufCommit"

# ---------------------------------------------------------------- 1. ComfyUI und ComfyUI-GGUF

Write-Host "1/5  ComfyUI $ComfyVersion und ComfyUI-GGUF ..."
if ((Test-Path $Marker) -and ((Get-Content $Marker -Raw).Trim() -eq $Wanted)) {
    Write-Host "  Schon da."
}
else {
    $temp = Join-Path $env:TEMP "kinexus-comfyui"
    if (Test-Path $temp) { Remove-Item $temp -Recurse -Force }
    New-Item -ItemType Directory -Path $temp | Out-Null
    Write-Host "  Lade ComfyUI (ca. 13 MB) ..."
    Invoke-WebRequest -Uri "https://github.com/Comfy-Org/ComfyUI/archive/refs/tags/$ComfyVersion.zip" -OutFile (Join-Path $temp "comfyui.zip") -UseBasicParsing
    Write-Host "  Lade ComfyUI-GGUF ..."
    Invoke-WebRequest -Uri "https://github.com/city96/ComfyUI-GGUF/archive/$GgufCommit.zip" -OutFile (Join-Path $temp "gguf.zip") -UseBasicParsing
    Expand-Archive -Path (Join-Path $temp "comfyui.zip") -DestinationPath (Join-Path $temp "comfyui")
    Expand-Archive -Path (Join-Path $temp "gguf.zip") -DestinationPath (Join-Path $temp "gguf")

    # Alte Version weg, die Python-Umgebung bleibt (spart den Neuaufbau).
    if (Test-Path $ComfyDir) {
        Get-ChildItem $ComfyDir -Force | Where-Object { $_.Name -ne ".venv" } | Remove-Item -Recurse -Force
    }
    else {
        New-Item -ItemType Directory -Path $ComfyDir | Out-Null
    }
    $source = Get-ChildItem (Join-Path $temp "comfyui") -Directory | Select-Object -First 1
    Copy-Item (Join-Path $source.FullName "*") $ComfyDir -Recurse -Force
    $gguf = Get-ChildItem (Join-Path $temp "gguf") -Directory | Select-Object -First 1
    Copy-Item $gguf.FullName (Join-Path $ComfyDir "custom_nodes\ComfyUI-GGUF") -Recurse
    Set-Content -Path $Marker -Value $Wanted -Encoding ascii
    Remove-Item $temp -Recurse -Force
    Write-Host "  ComfyUI liegt in $ComfyDir"
}

# ---------------------------------------------------------------- 2. Python-Umgebung

Write-Host "2/5  Python-Umgebung (uv) ..."
if (-not (Get-Command uv -ErrorAction SilentlyContinue)) {
    throw "uv fehlt. Installieren: https://docs.astral.sh/uv/ (danach dieses Skript nochmals starten)."
}
$env:UV_PROJECT_ENVIRONMENT = Join-Path $ComfyDir ".venv"
$env:UV_LINK_MODE = "hardlink"
Push-Location $VideoDir
# uv und curl schreiben Fortschritt auf stderr. Das ist kein Fehler, darum zählt nur der Exit-Code.
$ErrorActionPreference = "Continue"
try {
    uv sync --python 3.12
    if ($LASTEXITCODE -ne 0) { throw "uv sync ist fehlgeschlagen." }
}
finally {
    $ErrorActionPreference = "Stop"
    Pop-Location
    Remove-Item Env:\UV_PROJECT_ENVIRONMENT, Env:\UV_LINK_MODE -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------- 3. Modell-Pfade

Write-Host "3/5  Modell-Pfade für ComfyUI ..."
foreach ($folder in @("diffusion_models", "text_encoders", "vae", "latent_upscale_models", "loras")) {
    New-Item -ItemType Directory -Force -Path (Join-Path $ModelsDir $folder) | Out-Null
}
$base = $ModelsDir.Replace("\", "/")
$yaml = @"
# Von KI-Nexus geschrieben (bei jedem Start von ComfyUI). Hier sucht ComfyUI die Modelle.
kinexus:
  base_path: "$base"
  is_default: true
  diffusion_models: diffusion_models
  text_encoders: text_encoders
  vae: vae
  checkpoints: vae
  latent_upscale_models: latent_upscale_models
  loras: loras
"@
[IO.File]::WriteAllText((Join-Path $ComfyDir "kinexus-paths.yaml"), $yaml + "`n", (New-Object Text.UTF8Encoding($false)))

# ---------------------------------------------------------------- 4. Modelle (nur mit -Models)

$ltx = "https://huggingface.co/unsloth/LTX-2.3-GGUF/resolve/main"
$gemma = "https://huggingface.co/unsloth/gemma-3-12b-it-qat-GGUF/resolve/main"
$files = @(
    @{ Folder = "diffusion_models"; Name = "ltx-2.3-22b-distilled-1.1-Q4_K_M.gguf"; Url = "$ltx/distilled-1.1/ltx-2.3-22b-distilled-1.1-Q4_K_M.gguf"; Gb = 13.2; Sha = "5d09efdc0b8ec2054c44a05366cd7c6634ffa333b379b1f8baf018a78974b73d" },
    @{ Folder = "text_encoders"; Name = "gemma-3-12b-it-qat-UD-Q4_K_XL.gguf"; Url = "$gemma/gemma-3-12b-it-qat-UD-Q4_K_XL.gguf"; Gb = 6.9; Sha = "da98f81c86916ed1c76b3eeda56b25cb7b8352b01093e2edb8028110fe2cb53b" },
    @{ Folder = "text_encoders"; Name = "ltx-2.3-22b-distilled_embeddings_connectors.safetensors"; Url = "$ltx/text_encoders/ltx-2.3-22b-distilled_embeddings_connectors.safetensors"; Gb = 2.2; Sha = "c61cbb396e2a8175d8b2da51f0fdac885a4ccd22c9f64dafa5aa2c455dc8a507" },
    @{ Folder = "vae"; Name = "ltx-2.3-22b-distilled_video_vae.safetensors"; Url = "$ltx/vae/ltx-2.3-22b-distilled_video_vae.safetensors"; Gb = 1.4; Sha = "e68d6d8f8a42942ac9b862cc315beb3bc30805a8876c7ad63ba5bf7a2b8e168a" },
    @{ Folder = "vae"; Name = "ltx-2.3-22b-distilled_audio_vae.safetensors"; Url = "$ltx/vae/ltx-2.3-22b-distilled_audio_vae.safetensors"; Gb = 0.3; Sha = "3cd6a6eb8cb28f5ecc12f1f3126952b2a3d2b0b42ad3270e63cefafafe0d9b57" }
)
if ($Companions -and -not $Models) {
    # Nur die Zusatzdateien: Das Videomodell selbst kommt z.B. von Hugging Face (Create-Hub).
    $files = @($files | Where-Object { $_.Folder -ne "diffusion_models" })
}
foreach ($file in $files) { $file.Dir = $ModelsDir }

# Bildmodell (Bauplan Bild, gemessen in B0, siehe docs\11-Bild.md). Liest den Prompt mit Qwen3 4B.
$imageFiles = @(
    @{ Folder = "diffusion_models"; Name = "flux-2-klein-4b-Q8_0.gguf"; Url = "https://huggingface.co/unsloth/FLUX.2-klein-4B-GGUF/resolve/main/flux-2-klein-4b-Q8_0.gguf"; Gb = 4.0; Sha = "24a812e8f9b640e21c784164ea48571d8017ca22873696f4badeeabb006d509c" },
    @{ Folder = "text_encoders"; Name = "qwen_3_4b.safetensors"; Url = "https://huggingface.co/Comfy-Org/Z-Image-Turbo/resolve/main/split_files/text_encoders/qwen_3_4b.safetensors"; Gb = 7.5; Sha = "6c671498573ac2f7a5501502ccce8d2b08ea6ca2f661c458e708f36b36edfc5a" },
    @{ Folder = "vae"; Name = "flux2-vae.safetensors"; Url = "https://huggingface.co/Comfy-Org/Flux-2-Klein-4B/resolve/main/split_files/vae/flux2-vae.safetensors"; Gb = 0.3; Sha = "868fe7b343cc8f3a19dbcfcafbc3d5f888802be3f89bd81b65b3621a066ce8f3" }
)
foreach ($folder in @("diffusion_models", "text_encoders", "vae")) {
    New-Item -ItemType Directory -Force -Path (Join-Path $ImageDir $folder) | Out-Null
}
foreach ($file in $imageFiles) { $file.Dir = $ImageDir }
if ($Images) {
    # Nur -Images: nur das Bildmodell. Zusammen mit -Models oder -Companions: beides.
    $files = @($(if ($Models -or $Companions) { $files }) + $imageFiles)
}
$missing = @($files | Where-Object { -not (Test-Path (Join-Path (Join-Path $_.Dir $_.Folder) $_.Name)) })

if ($Images -and -not ($Models -or $Companions)) { Write-Host "4/5  Bildmodell (FLUX.2 klein 4B, Qwen3 4B, VAE) ..." }
else { Write-Host "4/5  Modelle (LTX-2.3 distilled, Gemma 3 12B, VAEs) ..." }
if ($missing.Count -eq 0) {
    Write-Host "  Alle Modelldateien sind da."
}
elseif (-not $Models -and -not $Companions -and -not $Images) {
    $gb = ($missing | Measure-Object -Property Gb -Sum).Sum
    Write-Host ("  Es fehlen {0} Dateien (ca. {1:N1} GB). Laden mit: .\scripts\setup-video.ps1 -Models" -f $missing.Count, $gb)
}
else {
    $curl = Join-Path $env:SystemRoot "System32\curl.exe"
    foreach ($file in $missing) {
        $target = Join-Path (Join-Path $file.Dir $file.Folder) $file.Name
        $part = "$target.part"
        Write-Host ("  Lade {0} (ca. {1:N1} GB) ..." -f $file.Name, $file.Gb)
        $ErrorActionPreference = "Continue"
        & $curl -L --fail --retry 5 -C - -o $part $file.Url
        $ErrorActionPreference = "Stop"
        if ($LASTEXITCODE -ne 0) { throw "Download von $($file.Name) ist fehlgeschlagen. Nochmals starten, dann geht es weiter." }
        Write-Host "  Prüfe die Prüfsumme ..."
        $hash = (Get-FileHash $part -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne $file.Sha) {
            Remove-Item $part -Force
            throw "Prüfsumme von $($file.Name) stimmt nicht. Die Datei wurde gelöscht. Nochmals starten."
        }
        Move-Item $part $target -Force
    }
}

# ---------------------------------------------------------------- 5. Prüfen

Write-Host "5/5  Prüfe PyTorch und die Grafikkarte ..."
$python = Join-Path $ComfyDir ".venv\Scripts\python.exe"
& $python -s -c "import torch; ok = torch.cuda.is_available(); print('  PyTorch', torch.__version__, '| Grafikkarte:', torch.cuda.get_device_name(0) if ok else 'nicht gefunden'); raise SystemExit(0 if ok else 1)"
if ($LASTEXITCODE -ne 0) { throw "PyTorch findet keine Grafikkarte (siehe oben)." }
Write-Host ""
Write-Host "Fertig. Testen: engines\comfyui\.venv\Scripts\python.exe -s python\video\v0_test.py --label test --extra=--cache-none"
