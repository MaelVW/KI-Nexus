<#
.SYNOPSIS
    Richtet das Training (Training-Tab, M8) ein: Python-Umgebung mit PyTorch und die Umwandlungs-Skripte von llama.cpp.

.DESCRIPTION
    1. Legt mit uv die Python-Umgebung python\training\.venv an: PyTorch 2.14 mit CUDA 13.0, transformers, PEFT,
       bitsandbytes (ca. 2.3 GB Download, ca. 5 GB auf der Festplatte).
    2. Holt die Skripte convert_hf_to_gguf.py und convert_lora_to_gguf.py (mit gguf-py) von llama.cpp, in derselben
       Version wie engines\llama.cpp (siehe version.json). Sie wandeln das Grundmodell und den Adapter nach GGUF um.
    3. Prüft, ob PyTorch die Grafikkarte findet.
    Das Grundmodell (z.B. Qwen3-1.7B, ca. 4 GB) lädt die App erst beim ersten Training.
    Nichts wird systemweit installiert. python\training\.venv\ und engines\llama.cpp\convert\ kann man jederzeit löschen.

.EXAMPLE
    .\scripts\setup-training.ps1
#>
$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$Root = Split-Path -Parent $PSScriptRoot
$TrainingDir = Join-Path $Root "python\training"
$LlamaDir = Join-Path $Root "engines\llama.cpp"
$ConvertDir = Join-Path $LlamaDir "convert"

# ---------------------------------------------------------------- 1. Python-Umgebung

Write-Host "1/3  Python-Umgebung für das Training (uv, ca. 2.3 GB) ..."
if (-not (Get-Command uv -ErrorAction SilentlyContinue)) {
    throw "uv fehlt. Installieren: https://docs.astral.sh/uv/ (danach dieses Skript nochmals starten)."
}
Push-Location $TrainingDir
try {
    uv sync --python 3.12
    if ($LASTEXITCODE -ne 0) { throw "uv sync ist fehlgeschlagen." }
}
finally { Pop-Location }

# ---------------------------------------------------------------- 2. Umwandlungs-Skripte von llama.cpp

Write-Host "2/3  Umwandlungs-Skripte von llama.cpp ..."
$versionFile = Join-Path $LlamaDir "version.json"
if (-not (Test-Path $versionFile)) { throw "llama.cpp fehlt. Zuerst scripts\setup-engines.ps1 ausführen." }
$tag = (Get-Content $versionFile -Raw | ConvertFrom-Json).version
$marker = Join-Path $ConvertDir "version.txt"
if ((Test-Path $marker) -and ((Get-Content $marker -Raw).Trim() -eq $tag)) {
    Write-Host "  Skripte für $tag sind schon da."
}
else {
    $zip = Join-Path $env:TEMP "llama.cpp-$tag.zip"
    $unpacked = Join-Path $env:TEMP "llama.cpp-$tag"
    Write-Host "  Lade den Quellcode von $tag (nur für die Skripte) ..."
    Invoke-WebRequest -Uri "https://github.com/ggml-org/llama.cpp/archive/refs/tags/$tag.zip" -OutFile $zip -UseBasicParsing
    if (Test-Path $unpacked) { Remove-Item $unpacked -Recurse -Force }
    Expand-Archive -Path $zip -DestinationPath $unpacked
    $source = Get-ChildItem $unpacked -Directory | Select-Object -First 1
    if (Test-Path $ConvertDir) { Remove-Item $ConvertDir -Recurse -Force }
    New-Item -ItemType Directory -Path $ConvertDir | Out-Null
    Copy-Item (Join-Path $source.FullName "convert_*.py") $ConvertDir
    Copy-Item (Join-Path $source.FullName "gguf-py") (Join-Path $ConvertDir "gguf-py") -Recurse
    foreach ($extra in @("conversion", "models")) {
        $path = Join-Path $source.FullName $extra
        if ((Test-Path $path) -and (Get-ChildItem $path -Filter *.py -Recurse | Select-Object -First 1)) {
            Copy-Item $path (Join-Path $ConvertDir $extra) -Recurse
        }
    }
    Set-Content -Path $marker -Value $tag -Encoding ascii
    Remove-Item $zip, $unpacked -Recurse -Force
    Write-Host "  Skripte liegen in $ConvertDir"
}

# ---------------------------------------------------------------- 3. Prüfen

Write-Host "3/3  Prüfe PyTorch und die Grafikkarte ..."
$python = Join-Path $TrainingDir ".venv\Scripts\python.exe"
& $python -I (Join-Path $TrainingDir "train.py") --check
if ($LASTEXITCODE -ne 0) { throw "Die Prüfung ist fehlgeschlagen (siehe oben)." }
Write-Host ""
Write-Host "Fertig. Das Training startest du im Training-Tab von KI-Nexus."
