<#
.SYNOPSIS
    Lädt llama.cpp (CUDA-Build für NVIDIA RTX 50) nach engines\llama.cpp\.

.DESCRIPTION
    Das Skript holt zwei ZIP-Dateien vom offiziellen GitHub-Release von llama.cpp:
    die Programme (llama-server.exe usw.) und die CUDA-Laufzeit-DLLs.
    Es prüft die SHA-256-Prüfsummen, entpackt alles und schreibt engines\llama.cpp\version.json.
    Nichts wird systemweit installiert. Den Ordner engines\ kann man jederzeit löschen und neu aufbauen.

.PARAMETER Version
    Build von llama.cpp, z.B. "b11960". Mit "latest" nimmt das Skript das neueste stabile Release.

.PARAMETER Cuda
    CUDA-Version des Builds. "13.4" enthält fertigen Code für Blackwell (RTX 50). Standard: 13.4.

.PARAMETER Force
    Neu herunterladen, auch wenn diese Version schon installiert ist.

.EXAMPLE
    .\scripts\setup-engines.ps1
    .\scripts\setup-engines.ps1 -Version latest
#>
param(
    [string]$Version = "b11960",
    [ValidateSet("13.4", "12.4")]
    [string]$Cuda = "13.4",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
# Windows PowerShell 5.1 nutzt sonst manchmal altes TLS, das GitHub ablehnt.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Net.Http

$Repo = "ggml-org/llama.cpp"
$Root = Split-Path -Parent $PSScriptRoot
$EnginesDir = Join-Path $Root "engines"
$TargetDir = Join-Path $EnginesDir "llama.cpp"
$DownloadDir = Join-Path $EnginesDir "downloads"
$VersionFile = Join-Path $TargetDir "version.json"
$Headers = @{ "User-Agent" = "KI-Nexus-Setup"; "Accept" = "application/vnd.github+json" }

function Get-Release([string]$Tag) {
    Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/tags/$Tag" -Headers $Headers
}

# "latest" zeigt bei llama.cpp auf ein Release, das nur den Namen des passenden Builds enthält.
function Resolve-Version([string]$Requested) {
    if ($Requested -ne "latest") { return $Requested }
    $latest = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" -Headers $Headers
    $tagAsset = $latest.assets | Where-Object { $_.name -eq "nightly-tag.txt" } | Select-Object -First 1
    if (-not $tagAsset) { return $latest.tag_name }
    $client = New-Object System.Net.Http.HttpClient
    try { return $client.GetStringAsync($tagAsset.browser_download_url).Result.Trim() } finally { $client.Dispose() }
}

function Save-WithProgress([string]$Url, [string]$OutFile, [long]$TotalBytes) {
    $client = New-Object System.Net.Http.HttpClient
    $client.Timeout = [TimeSpan]::FromMinutes(30)
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
                if ($percent -ge $nextReport) {
                    Write-Host ("    {0,3} %  ({1:N0} von {2:N0} MB)" -f $percent, ($done / 1MB), ($TotalBytes / 1MB))
                    $nextReport += 10
                }
            }
        }
        finally { $output.Dispose(); $source.Dispose() }
    }
    finally { $client.Dispose() }
}

function Assert-Checksum([string]$File, $Asset) {
    if (-not $Asset.digest) {
        Write-Warning "GitHub nennt keine Prüfsumme für $($Asset.name). Die Datei wird ohne Prüfung verwendet."
        return
    }
    $expected = ($Asset.digest -replace "^sha256:", "").ToUpperInvariant()
    $actual = (Get-FileHash -Path $File -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($expected -ne $actual) {
        Remove-Item $File -Force
        throw "Prüfsumme falsch für $($Asset.name). Erwartet $expected, erhalten $actual. Datei gelöscht."
    }
    Write-Host "    Prüfsumme stimmt (SHA-256)."
}

# ---------------------------------------------------------------- Ablauf

$Version = Resolve-Version $Version
Write-Host "llama.cpp $Version mit CUDA $Cuda"

if ((Test-Path $VersionFile) -and -not $Force) {
    $installed = Get-Content $VersionFile -Raw | ConvertFrom-Json
    if ($installed.version -eq $Version -and $installed.cuda -eq $Cuda) {
        Write-Host "Ist schon installiert in $TargetDir. Nichts zu tun. (Mit -Force neu laden.)"
        exit 0
    }
}

$release = Get-Release $Version
$wanted = @("llama-$Version-bin-win-cuda-$Cuda-x64.zip", "cudart-llama-bin-win-cuda-$Cuda-x64.zip")
New-Item -ItemType Directory -Force -Path $DownloadDir | Out-Null

$zipFiles = @()
foreach ($name in $wanted) {
    $asset = $release.assets | Where-Object { $_.name -eq $name } | Select-Object -First 1
    if (-not $asset) { throw "Datei $name fehlt im Release $Version." }

    $zip = Join-Path $DownloadDir $name
    Write-Host ("Lade {0} ({1:N0} MB) ..." -f $name, ($asset.size / 1MB))
    Save-WithProgress -Url $asset.browser_download_url -OutFile $zip -TotalBytes $asset.size
    Assert-Checksum -File $zip -Asset $asset
    $zipFiles += $zip
}

# Erst in einen neuen Ordner entpacken, dann austauschen. So bleibt die alte Version heil, falls etwas schiefgeht.
$staging = "$TargetDir.neu"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging | Out-Null
foreach ($zip in $zipFiles) {
    Write-Host "Entpacke $(Split-Path $zip -Leaf) ..."
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $staging)
}

if (-not (Test-Path (Join-Path $staging "llama-server.exe"))) {
    throw "llama-server.exe fehlt nach dem Entpacken. Der Aufbau des Releases hat sich wohl geändert."
}

@{
    version     = $Version
    cuda        = $Cuda
    source      = "https://github.com/$Repo/releases/tag/$Version"
    installedAt = (Get-Date).ToString("o")
} | ConvertTo-Json | Set-Content -Path (Join-Path $staging "version.json") -Encoding UTF8

if (Test-Path $TargetDir) { Remove-Item $TargetDir -Recurse -Force }
Rename-Item -Path $staging -NewName (Split-Path $TargetDir -Leaf)
Remove-Item $DownloadDir -Recurse -Force

Write-Host ""
Write-Host "Fertig: $TargetDir"
& (Join-Path $TargetDir "llama-server.exe") --version
