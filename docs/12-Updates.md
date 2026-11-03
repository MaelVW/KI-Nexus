# 12 – Installieren, Updates und Releases

Stand: 3. November 2026. KI-Nexus kommt als **Quellcode** zu den Nutzern, `Start-KI-Nexus.bat` baut die App selbst (braucht das .NET 10 SDK). Updates gibt es nur für **freigegebene Versionen** (GitHub Releases), nicht für jeden Commit.

## Für Nutzer

### Installieren (ein Befehl)

In PowerShell (Windows-Taste, „PowerShell“ tippen):

```powershell
irm https://raw.githubusercontent.com/MaelVW/KI-Nexus/main/scripts/install.ps1 | iex
```

- Lädt die neueste freigegebene Version nach `%USERPROFILE%\KI-Nexus`, baut sie und startet sie.
- Anderer Ordner: Skript herunterladen und `.\install.ps1 -Path D:\KI-Nexus` ausführen.
- Voraussetzung: das **.NET 10 SDK** (`winget install Microsoft.DotNet.SDK.10`). Fehlt es, sagt das Skript Bescheid und installiert nichts selbst.
- Danach wie gewohnt: llama.cpp lädt `Start-KI-Nexus.bat` beim ersten Start (fragt vorher), Sprache, Training und Video mit den Skripten in `scripts\`.

### Aktualisieren

**Einstellungen → Updates → „Nach Updates suchen“.** Gibt es eine neuere Version, zeigt die App, was neu ist, und den Knopf **„Jetzt aktualisieren“**:

1. Die App schliesst sich, ein PowerShell-Fenster öffnet sich (`scripts\install.ps1`).
2. Es lädt die neue Version von GitHub und ersetzt die Programmteile.
3. `Start-KI-Nexus.bat` baut die App neu und startet sie (ca. 1 Minute).

Von Hand geht dasselbe mit `.\scripts\install.ps1` im Ordner von KI-Nexus.

### Was ein Update ersetzt und was bleibt

| Ordner / Datei | Beim Update |
|---|---|
| `src`, `tests`, `docs`, `scripts` | genau wie in der neuen Version (alte Dateien fallen weg) |
| `python` | Skripte ersetzt, die Python-Umgebungen (`.venv`) bleiben |
| `kis` | nur neue Beispiel-KIs kommen dazu, **bestehende KIs werden nie überschrieben** |
| Dateien im Hauptordner (README, `Start-KI-Nexus.bat`, …) | ersetzt |
| `data` (Chats, Einstellungen, Galerie, Trainings) | bleibt |
| `models`, `engines` | bleibt |

## Lokal bleibt lokal

- KI-Nexus sucht **nur, wenn man auf „Nach Updates suchen“ klickt**, nie beim Start und nie im Hintergrund.
- Die Suche ist eine einzige Anfrage an `api.github.com/repos/MaelVW/KI-Nexus/releases/latest`. Mitgeschickt wird nichts ausser der Anfrage selbst (kein Konto, keine Daten, keine Kennung).
- Das ist eine Ergänzung zum Bauplan (Kapitel 2: Internet nur für Hugging Face und Engine-Updates, wenn man sie auslöst). Mael hat sie am 3.11.2026 so gewünscht: nur auf Klick.

## Entwickler-Ordner (git)

Ein Ordner mit `.git` wird vom Update-Skript **nicht** angefasst. Die App zeigt bei einem Update „Aktualisiere mit git pull“, und „Jetzt aktualisieren“ ist aus. So überschreibt ein Update nie ungespeicherte Arbeit.

## Für Mael: eine Version freigeben

Voraussetzungen: Repository öffentlich (sonst finden Update und Installation nichts), GitHub CLI angemeldet (`gh auth login`).

1. Version in `Directory.Build.props` und `src/KiNexus.App/app.manifest` erhöhen, Abschnitt `## [x.y.z]` im `CHANGELOG.md` schreiben, committen und pushen (das mache ich bei jeder Phase).
2. `.\scripts\release.ps1 -DryRun` zeigt Tag, Titel und Text.
3. `.\scripts\release.ps1` fragt nach („ja“), erstellt den Tag `vx.y.z`, pusht ihn und legt das Release mit dem Text aus dem Changelog an.

Danach finden alle anderen die Version mit „Nach Updates suchen“. Nicht freigegebene Commits auf `main` bekommt niemand.

## Technik

| Teil | Datei |
|---|---|
| Suche, Versionen vergleichen | `src/KiNexus.Engines/Updates/UpdateClient.cs` (Entwürfe und Vorab-Versionen zählen nicht) |
| Knopf und Text | Einstellungen, Bereich „Updates“ (`SettingsViewModel`) |
| Installieren und Aktualisieren | `scripts/install.ps1` (lädt das ZIP, das GitHub für jeden Tag anbietet; `-FromZip` für Tests) |
| Freigeben | `scripts/release.ps1` |

Getestet (3.11.2026): Unit-Tests für Suche und Vergleich (ohne Netzwerk). `install.ps1 -FromZip` mit einer ZIP im Aufbau von GitHub: frisch installiert, danach als Update. `data`, `models`, `engines`, `.venv`, eigene und geänderte KIs blieben, eine entfernte Datei verschwand. Der installierte Ordner liess sich bauen. Den git-Ordner hat das Skript nicht angefasst.

Noch nicht geprüft: der echte Weg über GitHub (das Repository ist noch privat, es gibt noch kein Release) und der Knopf „Jetzt aktualisieren“ in der App.
