<#
.SYNOPSIS
    Richtet die Sprache für den Gespräch-Stil ein: Python-Umgebung, Whisper-Modell und Piper-Stimme.

.DESCRIPTION
    1. Legt mit uv die Python-Umgebung python\.venv an (faster-whisper, piper-tts, sounddevice).
    2. Lädt das Whisper-Modell (Spracherkennung) nach models\voice\whisper-<name>\.
    3. Lädt die Piper-Stimme (Sprachausgabe) nach models\voice\piper\.
    Alles kommt von Hugging Face. Grosse Dateien werden mit SHA-256 geprüft.
    Nichts wird systemweit installiert. models\voice\ und python\.venv\ kann man jederzeit löschen und neu aufbauen.

.PARAMETER Whisper
    Whisper-Modell. Standard: large-v3-turbo (1.6 GB, beste deutsche Erkennung). Kleiner: small (0.5 GB).

.PARAMETER Voice
    Piper-Stimme. Standard: de_DE-thorsten-high (114 MB).

.EXAMPLE
    .\scripts\setup-voice.ps1
    .\scripts\setup-voice.ps1 -Voice de_DE-kerstin-low
#>
param(
    [ValidateSet("large-v3-turbo", "small", "medium")]
    [string]$Whisper = "large-v3-turbo",
    [string]$Voice = "de_DE-thorsten-high"
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.Net.Http

$Root = Split-Path -Parent $PSScriptRoot
$PythonDir = Join-Path $Root "python"
$VoiceDir = Join-Path $Root "models\voice"
$HfBase = "https://huggingface.co"

# Wo die Modelle auf Hugging Face liegen (Stand Oktober 2026).
$WhisperRepos = @{
    "large-v3-turbo" = "dropbox-dash/faster-whisper-large-v3-turbo"
    "small"          = "Systran/faster-whisper-small"
    "medium"         = "Systran/faster-whisper-medium"
}
$PiperRepo = "rhasspy/piper-voices"

function Save-WithProgress([string]$Url, [string]$OutFile, [long]$TotalBytes) {
    $client = New-Object System.Net.Http.HttpClient
    $client.Timeout = [TimeSpan]::FromMinutes(60)
    $client.DefaultRequestHeaders.UserAgent.ParseAdd("KI-Nexus-Setup")
    try {
        $response = $client.GetAsync($Url, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).Result
        $response.EnsureSuccessStatusCode() | Out-Null
        $source = $response.Content.ReadAsStreamAsync().Result
        $output = [IO.File]::Create($OutFile)
        try {
            $buffer = New-Object byte[] (1MB)
            $done = 0L
            $nextReport = 10
            while (($read = $source.Read($buffer, 0, $buffer.Length)) -gt 0) {
                $output.Write($buffer, 0, $read)
                $done += $read
                $percent = [int](100 * $done / [Math]::Max(1, $TotalBytes))
                if ($TotalBytes -gt 20MB -and $percent -ge $nextReport) {
                    Write-Host ("    {0,3} %  ({1:N0} von {2:N0} MB)" -f $percent, ($done / 1MB), ($TotalBytes / 1MB))
                    $nextReport += 10
                }
            }
        }
        finally { $output.Dispose(); $source.Dispose() }
    }
    finally { $client.Dispose() }
}

# Lädt eine Datei aus einem Hugging-Face-Repository. Grosse Dateien (LFS) haben eine SHA-256-Prüfsumme.
function Get-HfFile([string]$Repo, $Entry, [string]$TargetDir) {
    $target = Join-Path $TargetDir (Split-Path $Entry.path -Leaf)
    $expected = if ($Entry.lfs) { $Entry.lfs.oid.ToUpperInvariant() } else { $null }
    if ((Test-Path $target) -and $expected -and (Get-FileHash $target -Algorithm SHA256).Hash -eq $expected) {
        Write-Host "  $($Entry.path) ist schon da."
        return
    }

    Write-Host ("  Lade {0} ({1:N1} MB) ..." -f $Entry.path, ($Entry.size / 1MB))
    $part = "$target.part"
    $url = "$HfBase/$Repo/resolve/main/" + (($Entry.path -split "/" | ForEach-Object { [Uri]::EscapeDataString($_) }) -join "/")
    Save-WithProgress -Url $url -OutFile $part -TotalBytes $Entry.size
    if ($expected) {
        $actual = (Get-FileHash $part -Algorithm SHA256).Hash
        if ($actual -ne $expected) {
            Remove-Item $part -Force
            throw "Prüfsumme falsch für $($Entry.path). Datei gelöscht."
        }
        Write-Host "    Prüfsumme stimmt (SHA-256)."
    }
    Move-Item $part $target -Force
}

function Get-HfTree([string]$Repo, [string]$Path = "") {
    $url = "$HfBase/api/models/$Repo/tree/main" + $(if ($Path) { "/$Path" } else { "" }) + "?recursive=true"
    # PowerShell 5.1 gibt eine JSON-Liste als ein Objekt weiter. Über die Variable wird sie wieder einzeln aufgezählt.
    $items = Invoke-RestMethod -Uri $url -Headers @{ "User-Agent" = "KI-Nexus-Setup" }
    $items | Where-Object { $_.type -eq "file" }
}

# ---------------------------------------------------------------- 1. Python-Umgebung

Write-Host "1/3  Python-Umgebung (uv) ..."
if (-not (Get-Command uv -ErrorAction SilentlyContinue)) {
    throw "uv fehlt. Installieren: https://docs.astral.sh/uv/ (danach dieses Skript nochmals starten)."
}
Push-Location $PythonDir
try {
    uv sync --python 3.12
    if ($LASTEXITCODE -ne 0) { throw "uv sync ist fehlgeschlagen." }
}
finally { Pop-Location }

# ---------------------------------------------------------------- 2. Whisper

$whisperRepo = $WhisperRepos[$Whisper]
$whisperDir = Join-Path $VoiceDir "whisper-$Whisper"
Write-Host "2/3  Whisper $Whisper ($whisperRepo) ..."
New-Item -ItemType Directory -Force -Path $whisperDir | Out-Null
foreach ($entry in Get-HfTree $whisperRepo | Where-Object { $_.path -notmatch "^(README\.md|\.gitattributes)$" }) {
    Get-HfFile -Repo $whisperRepo -Entry $entry -TargetDir $whisperDir
}

# ---------------------------------------------------------------- 3. Piper-Stimme

# "de_DE-thorsten-high" liegt unter de/de_DE/thorsten/high/.
$parts = $Voice -split "-"
if ($parts.Count -ne 3) { throw "Stimmen-Name ungültig: $Voice (Beispiel: de_DE-thorsten-high)." }
$language = $parts[0].Split("_")[0]
$voicePath = "$language/$($parts[0])/$($parts[1])/$($parts[2])"
$piperDir = Join-Path $VoiceDir "piper"
Write-Host "3/3  Piper-Stimme $Voice ..."
New-Item -ItemType Directory -Force -Path $piperDir | Out-Null
$voiceFiles = Get-HfTree $PiperRepo $voicePath | Where-Object { $_.path -like "*.onnx" -or $_.path -like "*.onnx.json" }
if (-not $voiceFiles) { throw "Die Stimme $Voice gibt es nicht auf Hugging Face ($PiperRepo)." }
foreach ($entry in $voiceFiles) {
    Get-HfFile -Repo $PiperRepo -Entry $entry -TargetDir $piperDir
}

Write-Host ""
Write-Host "Fertig. Sprache ist eingerichtet:"
Write-Host "  Python:  $PythonDir\.venv"
Write-Host "  Whisper: $whisperDir"
Write-Host "  Stimme:  $piperDir\$Voice.onnx"
