# 00 – Übersicht

KI-Nexus ist eine Windows-App, mit der du lokal mit deinen KI-Modellen chattest.
Diese Datei zeigt, was wo liegt und wie die Teile zusammenhängen.
Fang hier an, wenn du dich im Projekt zurechtfinden willst.

## Stand

| Meilenstein | Inhalt | Status |
|---|---|---|
| M0 | Gerüst: Solution, Ordner, Theme, Hauptfenster, Logging, Einstellungen, Startdatei | ✅ fertig (v0.1.0) |
| M1 | Engines: llama.cpp und Ollama, Laden/Entladen, Streaming, Ressourcen-Anzeige | offen |
| M2 | Chat: Simpel- und Terminal-Stil | offen |
| M3 | Create-Hub | offen |
| M4 | Gespräch-Stil (Sprache) | offen |
| M5–M10 | Alternate Launch, System-Prompt, Feineinstellungen, Training, Server, Chatraum | offen |

## Ordner

| Ordner / Datei | Inhalt | In Git? |
|---|---|---|
| `Start-KI-Nexus.bat` | Start per Doppelklick. Baut die App bei Bedarf. | ja |
| `KI-Nexus.sln` | Die Solution. Öffnen mit Visual Studio oder Rider. | ja |
| `docs/` | Diese Doku, eine Datei pro System | ja |
| `kis/` | Deine KIs, eine JSON-Datei pro KI (ab M1) | ja |
| `models/` | Grundmodelle als GGUF (ab M1) | nein |
| `data/` | Chats, `settings.json`, Logs, Cache | nein |
| `engines/` | llama.cpp, Piper usw. Wird von `scripts/setup-engines.ps1` gefüllt (ab M1) | nein |
| `src/KiNexus.App/` | Die Oberfläche (WPF): Fenster, ViewModels, Theme | ja |
| `src/KiNexus.Core/` | Logik ohne Oberfläche: Einstellungen, Pfade, später Chats und KIs | ja |
| `src/KiNexus.Engines/` | Anbindung an llama.cpp und Ollama (ab M1) | ja |
| `tests/` | Unit-Tests für Core und Engines | ja |
| `python/` | Spracherkennung (M4) und Training (M8) | ja (ohne `.venv`) |
| `scripts/` | PowerShell-Skripte für Setup und Engines | ja |

## So hängt alles zusammen

```
Start-KI-Nexus.bat
   └─ baut und startet  KI-Nexus.exe  (src/KiNexus.App)
                           ├─ nutzt KiNexus.Core     (Einstellungen, Pfade, Logik)
                           └─ nutzt KiNexus.Engines  (llama.cpp, Ollama – ab M1)
```

Beim Start passiert der Reihe nach:

1. Die App sucht den Projektordner. Sie geht vom Ordner der `.exe` nach oben, bis sie `KI-Nexus.sln` findet.
2. Sie legt `data/` mit `logs/`, `cache/` und `attachments/` an, falls nötig.
3. Das Logging startet (`data/logs/ki-nexus-JJJJMMTT.log`).
4. Die Einstellungen werden aus `data/settings.json` geladen. Fehlt die Datei, gelten Standardwerte.
5. Die Akzentfarbe wird gesetzt, dann öffnet sich das Hauptfenster.

## Bedienung (Stand M0)

| Taste | Wirkung |
|---|---|
| `Strg + 1` bis `Strg + 5` | Tab wechseln (Chat, Feineinstellungen, Training, System-Prompt, Create-Hub) |
| `Strg + ,` | Einstellungen öffnen |
| `Esc` (im Einstellungs-Dialog) | Abbrechen, ohne zu speichern |

## Weiter lesen

- [01-Architektur.md](01-Architektur.md) – Schichten, Klassen, Einstellungen
- [09-Fehlerbehebung.md](09-Fehlerbehebung.md) – wenn etwas nicht geht
