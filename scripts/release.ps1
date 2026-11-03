<#
.SYNOPSIS
    Gibt die aktuelle Version von KI-Nexus als GitHub Release frei. Erst dann bekommen andere sie per Update.

.DESCRIPTION
    1. Prüft: Branch main, nichts Ungespeichertes in Git-Dateien, alles gepusht, Tag gibt es noch nicht.
    2. Version aus Directory.Build.props (z.B. 1.12.0) → Tag v1.12.0.
    3. Text aus dem Abschnitt dieser Version im CHANGELOG.md.
    4. Fragt nach, dann: Tag erstellen und pushen, Release auf GitHub erstellen (mit gh).

    Das Release enthält nur den Quellcode, den GitHub selbst als ZIP anbietet. scripts\install.ps1 lädt genau diesen.

.EXAMPLE
    .\scripts\release.ps1            # mit Rückfrage
.EXAMPLE
    .\scripts\release.ps1 -DryRun    # nur zeigen, was passieren würde
#>
param([switch]$DryRun)
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Push-Location $Root
try {
    $gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
    if (-not $gh -and (Test-Path "C:\Program Files\GitHub CLI\gh.exe")) { $gh = "C:\Program Files\GitHub CLI\gh.exe" }
    if (-not $gh) { throw "Die GitHub CLI (gh) fehlt. Installieren: winget install GitHub.cli, danach: gh auth login" }

    # ------------------------------------------------------------ 1. Prüfen
    $branch = (git rev-parse --abbrev-ref HEAD).Trim()
    if ($branch -ne "main") { throw "Releases nur von main (jetzt: $branch)." }
    git diff --quiet HEAD
    if ($LASTEXITCODE -ne 0) { throw "Es gibt ungespeicherte Änderungen an Git-Dateien. Zuerst committen." }
    git fetch origin main --quiet
    $local = (git rev-parse HEAD).Trim()
    $remote = (git rev-parse origin/main).Trim()
    if ($local -ne $remote) { throw "main ist nicht gepusht (lokal $($local.Substring(0, 7)), GitHub $($remote.Substring(0, 7))). Zuerst git push." }

    # ------------------------------------------------------------ 2. Version und Text
    $version = ([regex]::Match((Get-Content Directory.Build.props -Raw), "<Version>([^<]+)</Version>")).Groups[1].Value
    if (-not $version) { throw "Keine Version in Directory.Build.props." }
    $tag = "v$version"
    if (git tag -l $tag) { throw "Den Tag $tag gibt es schon. Erhöhe zuerst die Version." }

    $changelog = Get-Content CHANGELOG.md -Raw -Encoding UTF8
    $section = [regex]::Match($changelog, "(?ms)^## \[" + [regex]::Escape($version) + "\][^\r\n]*\r?\n(.*?)(?=^## \[|\z)")
    if (-not $section.Success) { throw "Im CHANGELOG.md fehlt der Abschnitt ## [$version]." }
    $heading = ([regex]::Match($section.Value, "^## \[[^\]]+\]\s*–?\s*[0-9-]*\s*–?\s*([^\r\n]*)")).Groups[1].Value.Trim()
    $title = if ($heading) { "KI-Nexus $version – $heading" } else { "KI-Nexus $version" }
    $notes = $section.Groups[1].Value.Trim() + "`n`nInstallieren oder aktualisieren: siehe README (Abschnitt Installieren)."

    Write-Host ""
    Write-Host "Release: $title" -ForegroundColor Cyan
    Write-Host "Tag:     $tag auf $($local.Substring(0, 7))"
    Write-Host ""
    Write-Host $notes
    Write-Host ""
    if ($DryRun) { Write-Host "DryRun: nichts gemacht." -ForegroundColor Yellow; return }

    $answer = Read-Host "Diese Version jetzt für alle freigeben? (ja/nein)"
    if ($answer -ne "ja") { Write-Host "Abgebrochen."; return }

    # ------------------------------------------------------------ 3. Freigeben
    $notesFile = Join-Path $env:TEMP "kinexus-release-notes.md"
    [IO.File]::WriteAllText($notesFile, $notes, (New-Object Text.UTF8Encoding($false)))
    git tag -a $tag -m $title
    if ($LASTEXITCODE -ne 0) { throw "Tag konnte nicht erstellt werden." }
    git push origin $tag
    if ($LASTEXITCODE -ne 0) { throw "Tag konnte nicht gepusht werden." }
    & $gh release create $tag --title $title --notes-file $notesFile --verify-tag
    if ($LASTEXITCODE -ne 0) { throw "Release konnte nicht erstellt werden. Der Tag $tag ist schon auf GitHub, Release dort von Hand anlegen." }
    Remove-Item $notesFile -ErrorAction SilentlyContinue
    Write-Host ""
    Write-Host "Fertig: $tag ist freigegeben. Andere bekommen es mit „Nach Updates suchen“." -ForegroundColor Green
}
finally {
    Pop-Location
}
