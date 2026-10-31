# KI-Nexus

KI-Nexus ist eine Desktop-App für Windows. Damit chattest du mit deinen lokalen KI-Modellen, verwaltest Modelle und baust eigene KIs.
Alles läuft lokal auf dem Laptop: keine Cloud-KI, keine Telemetrie.

> **Stand: Version 1.0.0 (Meilenstein 4 – Gespräch-Stil).** Das ist die Mindestversion aus dem Bauplan (M0 bis M4).
> Chatten in drei Stilen: Simpel, Terminal und Gespräch (du sprichst, die KI antwortet mit Stimme, alles lokal mit Whisper und Piper). Im Create-Hub erstellst und löschst du KIs, importierst GGUF-Dateien und Ollama-Modelle und lädst Modelle von Hugging Face. Die eigene Engine (llama.cpp) und Ollama laufen.

![Simpel-Stil von KI-Nexus](docs/bilder/hauptfenster-m2.png)

![Terminal-Stil von KI-Nexus](docs/bilder/terminal-m2.png)

![Create-Hub mit Ampel](docs/bilder/create-hub-m3.png)

![Gespräch-Stil mit Orb](docs/bilder/gespraech-m4.png)

## Starten

1. Doppelklick auf **`Start-KI-Nexus.bat`**.
2. Fehlt llama.cpp noch, fragt das Skript, ob es llama.cpp herunterladen soll (ca. 560 MB).
3. Beim ersten Mal baut das Skript die App. Das dauert etwa eine halbe Minute.
4. Das Fenster vom Skript schliesst sich von selbst, und KI-Nexus öffnet sich.
5. Fehlt einer KI die Modell-Datei, bietet ein Hinweis über dem Chat „Aus Ollama übernehmen“ an. Das braucht keinen zusätzlichen Speicherplatz (Hardlink).
6. Neue Modelle und KIs: Tab **Create-Hub** (`Strg + 5`).
7. Für den **Gespräch-Stil** einmal `.\scripts\setup-voice.ps1` ausführen (lädt ca. 1.8 GB: Python-Pakete, Whisper, deutsche Stimme).

## Voraussetzungen

| Was | Version | Prüfen mit |
|---|---|---|
| Windows | 11 (10 sollte auch gehen) | – |
| .NET SDK | 10.0 | `dotnet --list-sdks` |
| NVIDIA-Treiber | mit CUDA 13 (getestet: 592.01) | `nvidia-smi` |
| Ollama | optional | Ollama-App |
| uv (Python) | für den Gespräch-Stil (getestet: 0.11.26) | `uv --version` |

## Für Entwickler

```powershell
dotnet build KI-Nexus.sln            # alles bauen
dotnet test --solution KI-Nexus.sln  # alle Tests
.\scripts\setup-engines.ps1          # llama.cpp nach engines\ laden
.\scripts\setup-voice.ps1            # Sprache: python\.venv, Whisper und Piper-Stimme
```

Die Solution `KI-Nexus.sln` kannst du mit Visual Studio 2026 oder Rider öffnen.

| Projekt | Inhalt |
|---|---|
| `src/KiNexus.App` | Oberfläche (WPF, MVVM) |
| `src/KiNexus.Core` | Logik ohne Oberfläche |
| `src/KiNexus.Engines` | Anbindung an llama.cpp und Ollama |
| `tests/*` | Unit-Tests (xUnit v3) |

## Tastenkürzel

| Taste | Wirkung |
|---|---|
| `Strg + 1` … `Strg + 5` | Tab wechseln |
| `Strg + ,` | Einstellungen |
| `Strg + N` | Neuer Chat |
| `Enter` / `Shift + Enter` / `Esc` | Senden / neue Zeile / abbrechen (Simpel) |
| `Strg + C`, `/help` | Abbrechen, alle Befehle (Terminal) |

## Doku

Die Doku liegt in [`docs/`](docs/), eine Datei pro System. Einstieg: [docs/00-Uebersicht.md](docs/00-Uebersicht.md).
Messwerte der Engines: [docs/02-Engines.md](docs/02-Engines.md).
Was sich pro Version geändert hat, steht im [CHANGELOG.md](CHANGELOG.md).

## Daten

Chats (`data/chats.db`), angehängte Bilder, Einstellungen und Logs liegen in `data/`. Modelle liegen in `models/`, Engines in `engines/`.
Diese drei Ordner sind **nicht** in Git, weil sie gross oder persönlich sind. Deine KIs in `kis/` sind in Git und damit gesichert.
