<#
.SYNOPSIS
    Installiert KI-Nexus oder bringt es auf die neueste freigegebene Version (GitHub Release).

.DESCRIPTION
    Neu installieren (ein Befehl in PowerShell):
        irm https://raw.githubusercontent.com/MaelVW/KI-Nexus/main/scripts/install.ps1 | iex

    Aktualisieren: In KI-Nexus unter Einstellungen > Updates auf "Jetzt aktualisieren" klicken
    (startet dieses Skript), oder von Hand:
        .\scripts\install.ps1

    Was das Skript macht:
    1. Fragt GitHub nach dem neuesten Release (nur diese eine Anfrage und der Download, keine Daten von dir).
    2. Lädt den Quellcode dieses Releases als ZIP und packt ihn aus.
    3. Ersetzt die Programmteile (src, tests, docs, scripts, python ohne .venv, Dateien im Hauptordner).
       Deine Daten bleiben: data\ (Chats, Einstellungen, Galerie), models\, engines\, die Python-Umgebungen
       und deine KIs in kis\ (neue Beispiel-KIs kommen dazu, bestehende werden nie überschrieben).
    4. Startet Start-KI-Nexus.bat: Es baut die App (braucht das .NET 10 SDK) und startet sie.

    Ein Entwickler-Ordner (mit .git) wird nicht angefasst. Dort aktualisiert man mit "git pull".

.PARAMETER Path
    Wohin KI-Nexus kommt. Standard: %USERPROFILE%\KI-Nexus. Beim Update der Ordner der App.
.PARAMETER Tag
    Eine bestimmte Version, z.B. v1.12.0. Standard: die neueste.
.PARAMETER WaitForPid
    Vom Update-Knopf: Auf das Ende dieser KI-Nexus-Instanz warten, bevor Dateien ersetzt werden.
.PARAMETER NoStart
    Danach nicht bauen und starten.
.PARAMETER Force
    Auch installieren, wenn die Version schon dieselbe ist.
.PARAMETER FromZip
    Für Tests: statt von GitHub diese ZIP nehmen (gleicher Aufbau wie das ZIP eines Releases, z.B. von "git archive").
#>
param(
    [string]$Path = (Join-Path $env:USERPROFILE "KI-Nexus"),
    [string]$Tag = "",
    [int]$WaitForPid = 0,
    [switch]$NoStart,
    [switch]$Force,
    [string]$FromZip = ""
)
$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$Repo = "MaelVW/KI-Nexus"
$Api = "https://api.github.com/repos/$Repo"
$Headers = @{ "User-Agent" = "KI-Nexus-Install"; "Accept" = "application/vnd.github+json" }
$Path = [IO.Path]::GetFullPath($Path)

Write-Host ""
Write-Host "KI-Nexus installieren / aktualisieren" -ForegroundColor Cyan
Write-Host "Ordner: $Path"

# ---------------------------------------------------------------- 1. Prüfen

if ((Test-Path (Join-Path $Path ".git"))) {
    Write-Host ""
    Write-Host "Das ist ein Entwickler-Ordner (git). Das Skript überschreibt ihn nicht." -ForegroundColor Yellow
    Write-Host "Aktualisieren mit:  git pull   und danach Start-KI-Nexus.bat"
    return
}

$current = $null
$props = Join-Path $Path "Directory.Build.props"
if (Test-Path $props) {
    $match = Select-String -Path $props -Pattern "<Version>([^<]+)</Version>" | Select-Object -First 1
    if ($match) { $current = $match.Matches[0].Groups[1].Value }
}

if ($FromZip) {
    Write-Host "1/4  Nehme die lokale Datei $FromZip (Test) ..."
    $release = [pscustomobject]@{ tag_name = "lokal"; body = "" }
    $Force = $true
}
else {
    Write-Host "1/4  Frage GitHub nach der neuesten Version ..."
    try {
        $release = if ($Tag) { Invoke-RestMethod -Uri "$Api/releases/tags/$Tag" -Headers $Headers } else { Invoke-RestMethod -Uri "$Api/releases/latest" -Headers $Headers }
    }
    catch {
        throw "Kein Release gefunden. Gibt es auf GitHub schon eine freigegebene Version, und ist das Repository öffentlich? ($($_.Exception.Message))"
    }
}
$tagName = $release.tag_name
$newVersion = $tagName.TrimStart("v", "V")
Write-Host "  Neueste Version: $newVersion$(if ($current) { "  (installiert: $current)" })"

if ($current -and ($current -eq $newVersion) -and -not $Force) {
    Write-Host "  Du hast schon die neueste Version. Nichts zu tun." -ForegroundColor Green
    return
}

if ($release.body) {
    Write-Host ""
    Write-Host "Was ist neu:" -ForegroundColor Cyan
    $lines = ($release.body -split "`r?`n") | Where-Object { $_.Trim() } | Select-Object -First 15
    $lines | ForEach-Object { Write-Host "  $_" }
    Write-Host ""
}

# Das .NET SDK baut die App. Fehlt es, nur Bescheid geben (nichts selbst installieren).
$sdkOk = $false
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $sdkOk = [bool](& dotnet --list-sdks 2>$null | Where-Object { $_ -match "^10\." })
}
if (-not $sdkOk) {
    Write-Host "Hinweis: Das .NET 10 SDK fehlt. Ohne SDK kann Start-KI-Nexus.bat die App nicht bauen." -ForegroundColor Yellow
    Write-Host "  Installieren z.B. mit:  winget install Microsoft.DotNet.SDK.10"
}

# ---------------------------------------------------------------- 2. Warten, bis KI-Nexus zu ist

if ($WaitForPid -gt 0) {
    Write-Host "2/4  Warte, bis KI-Nexus geschlossen ist ..."
    for ($i = 0; $i -lt 120 -and (Get-Process -Id $WaitForPid -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Milliseconds 500 }
    if (Get-Process -Id $WaitForPid -ErrorAction SilentlyContinue) { throw "KI-Nexus läuft noch. Schliesse es und starte das Update nochmals." }
}
else {
    Write-Host "2/4  Prüfe, ob KI-Nexus läuft ..."
}
while (Get-Process -Name "KI-Nexus" -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($Path, [StringComparison]::OrdinalIgnoreCase) }) {
    Read-Host "  KI-Nexus läuft noch aus diesem Ordner. Schliesse es und drücke Enter"
}

# ---------------------------------------------------------------- 3. Laden und auspacken

Write-Host "3/4  Lade Version $newVersion ..."
$temp = Join-Path $env:TEMP "kinexus-install-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $zip = Join-Path $temp "kinexus.zip"
    if ($FromZip) { Copy-Item $FromZip $zip } else { Invoke-WebRequest -Uri "$Api/zipball/$tagName" -Headers $Headers -OutFile $zip -UseBasicParsing }
    Expand-Archive -Path $zip -DestinationPath (Join-Path $temp "x")
    $source = Get-ChildItem (Join-Path $temp "x") -Directory | Select-Object -First 1
    if (-not $source -or -not (Test-Path (Join-Path $source.FullName "KI-Nexus.sln"))) { throw "Die heruntergeladene Datei sieht nicht wie KI-Nexus aus." }
    $src = $source.FullName
    if ($FromZip) {
        $newVersion = ([regex]::Match((Get-Content (Join-Path $src "Directory.Build.props") -Raw), "<Version>([^<]+)</Version>")).Groups[1].Value
    }

    # ------------------------------------------------------------ 4. Einsetzen
    Write-Host "4/4  Setze die neue Version ein (deine Daten bleiben) ..."
    New-Item -ItemType Directory -Force -Path $Path | Out-Null

    function Invoke-Robocopy([string[]]$arguments) {
        & robocopy @arguments /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "Kopieren fehlgeschlagen (robocopy Code $LASTEXITCODE): $($arguments[0])" }
    }

    # Reine Programm-Ordner: genau wie im Release (alte Dateien fallen weg, bin\ und obj\ baut die App neu).
    foreach ($folder in @("src", "tests", "docs", "scripts")) {
        if (Test-Path (Join-Path $src $folder)) { Invoke-Robocopy @((Join-Path $src $folder), (Join-Path $Path $folder), "/MIR") }
    }

    # python\: Skripte ersetzen, die Python-Umgebungen (.venv) bleiben.
    if (Test-Path (Join-Path $src "python")) {
        Invoke-Robocopy @((Join-Path $src "python"), (Join-Path $Path "python"), "/E", "/XD", ".venv", "__pycache__")
    }

    # kis\: nur KIs, die es noch nicht gibt. Deine eigenen und geänderten bleiben, wie sie sind.
    if (Test-Path (Join-Path $src "kis")) {
        Invoke-Robocopy @((Join-Path $src "kis"), (Join-Path $Path "kis"), "/E", "/XC", "/XN", "/XO")
    }

    # Weitere Ordner aus dem Release (nicht data, models, engines): ergänzen.
    $skip = @("src", "tests", "docs", "scripts", "python", "kis", "data", "models", "engines")
    Get-ChildItem $src -Directory | Where-Object { $skip -notcontains $_.Name } | ForEach-Object {
        Invoke-Robocopy @($_.FullName, (Join-Path $Path $_.Name), "/E")
    }

    # Dateien im Hauptordner (z.B. README, Start-KI-Nexus.bat, Directory.Build.props).
    Get-ChildItem $src -File | ForEach-Object { Copy-Item $_.FullName (Join-Path $Path $_.Name) -Force }
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "KI-Nexus $newVersion ist bereit in $Path" -ForegroundColor Green

if (-not $NoStart) {
    if (-not $sdkOk) {
        Write-Host "Installiere zuerst das .NET 10 SDK, dann Doppelklick auf Start-KI-Nexus.bat."
        return
    }

    Write-Host "Start-KI-Nexus.bat baut die App (ca. eine halbe Minute) und startet sie ..."
    Start-Process -FilePath (Join-Path $Path "Start-KI-Nexus.bat") -WorkingDirectory $Path
}
