# 00 – Übersicht

KI-Nexus ist eine Windows-App, mit der du lokal mit deinen KI-Modellen chattest.
Diese Datei zeigt, was wo liegt und wie die Teile zusammenhängen.
Fang hier an, wenn du dich im Projekt zurechtfinden willst.

## Stand

| Meilenstein | Inhalt | Status |
|---|---|---|
| M0 | Gerüst: Solution, Ordner, Theme, Hauptfenster, Logging, Einstellungen, Startdatei | ✅ fertig (v0.1.0) |
| M1 | Engines: llama.cpp und Ollama, Laden/Entladen, Streaming, Ressourcen-Anzeige, Ollama-Import | ✅ fertig (v0.2.0) |
| M2 | Chat: Simpel- und Terminal-Stil, Seitenleiste, Bilder, Titel, Bewertung, Kontext | ✅ fertig (v0.3.0) |
| M3 | Create-Hub | offen |
| M4 | Gespräch-Stil (Sprache) | offen |
| M5–M10 | Alternate Launch, System-Prompt, Feineinstellungen, Training, Server, Chatraum | offen |

## Ordner

| Ordner / Datei | Inhalt | In Git? |
|---|---|---|
| `Start-KI-Nexus.bat` | Start per Doppelklick. Baut die App bei Bedarf, bietet den Download von llama.cpp an. | ja |
| `KI-Nexus.sln` | Die Solution. Öffnen mit Visual Studio oder Rider. | ja |
| `docs/` | Diese Doku, eine Datei pro System | ja |
| `kis/` | Deine KIs, eine JSON-Datei pro KI | ja |
| `models/` | Grundmodelle als GGUF, ein Ordner pro Modell | nein |
| `data/` | `chats.db`, `settings.json`, angehängte Bilder, Logs, Cache | nein |
| `engines/` | llama.cpp (später Piper). Wird von `scripts/setup-engines.ps1` gefüllt | nein |
| `src/KiNexus.App/` | Die Oberfläche (WPF): Fenster, ViewModels, Theme | ja |
| `src/KiNexus.Core/` | Logik ohne Oberfläche: Einstellungen, KIs, EngineManager, Prozesse, Messung | ja |
| `src/KiNexus.Engines/` | Anbindung an llama.cpp und Ollama, Import aus Ollama | ja |
| `tests/` | Unit-Tests für Core und Engines | ja |
| `python/` | Spracherkennung (M4) und Training (M8) | ja (ohne `.venv`) |
| `scripts/` | PowerShell-Skripte, z.B. `setup-engines.ps1` | ja |

## So hängt alles zusammen

```
Start-KI-Nexus.bat
   └─ baut und startet  KI-Nexus.exe  (src/KiNexus.App)
                           ├─ nutzt KiNexus.Core     (Einstellungen, KIs, EngineManager)
                           └─ nutzt KiNexus.Engines  (llama.cpp, Ollama)
                                     ├─ startet  engines\llama.cpp\llama-server.exe  (Port 8090)
                                     └─ spricht mit Ollama, falls es läuft          (Port 11434)
```

Beim Start passiert der Reihe nach:

1. Die App sucht den Projektordner. Sie geht vom Ordner der `.exe` nach oben, bis sie `KI-Nexus.sln` findet.
2. Sie legt `data/` mit `logs/`, `cache/` und `attachments/` an, falls nötig.
3. Das Logging startet (`data/logs/ki-nexus-JJJJMMTT.log`).
4. Die Einstellungen werden aus `data/settings.json` geladen. Fehlt die Datei, gelten Standardwerte.
5. Die Akzentfarbe wird gesetzt, die KIs aus `kis/` werden geladen und `data/chats.db` wird geöffnet (bei Bedarf auf die neueste Version gebracht).
6. Das Hauptfenster öffnet sich mit dem zuletzt benutzten Chat.
7. Ein Modell wird erst geladen, wenn du die erste Frage stellst.

## Bedienung (Stand M2)

| Was | Wo |
|---|---|
| KI wählen | Kopfleiste, Auswahl links. Das Schild zeigt die Engine (llama.cpp oder Ollama). |
| Status der Engine | Punkt neben der Auswahl: grau = nichts geladen, gelb = lädt, grün = bereit, leuchtend = antwortet. Mit der Maus darüber steht der Text. |
| VRAM und RAM | Kopfleiste rechts, alle 2 Sekunden gemessen. Gelb ab 90 %. |
| Modell entladen | Power-Symbol in der Kopfleiste. Es leuchtet, solange ein Modell geladen ist. |
| Chatten | Chat-Tab, Stil Simpel oder Terminal. Details: [04-Chat-und-Stile.md](04-Chat-und-Stile.md) |

| Taste | Wirkung |
|---|---|
| `Strg + 1` bis `Strg + 5` | Tab wechseln (Chat, Feineinstellungen, Training, System-Prompt, Create-Hub) |
| `Strg + ,` | Einstellungen öffnen |
| `Strg + N` | Neuer Chat |
| `Enter` / `Shift + Enter` | Senden / neue Zeile (Simpel) |
| `Esc` | Antwort abbrechen (Simpel), Dialog schliessen |
| `Strg + C` | Antwort abbrechen (Terminal, wenn kein Text markiert ist) |
| `Pfeil hoch/runter`, `Tab` | Frühere Eingaben, Befehl vervollständigen (Terminal) |
| `F2` / `Entf` | Chat umbenennen / löschen (in der Seitenleiste) |

## Weiter lesen

- [01-Architektur.md](01-Architektur.md) – Schichten, Klassen, Einstellungen
- [02-Engines.md](02-Engines.md) – llama.cpp und Ollama, Messwerte
- [03-Modelle-und-KIs.md](03-Modelle-und-KIs.md) – Modell-Ordner, KI-Profile, Import aus Ollama
- [04-Chat-und-Stile.md](04-Chat-und-Stile.md) – Simpel, Terminal, Befehle, Bilder, Kontext, Datenbank
- [09-Fehlerbehebung.md](09-Fehlerbehebung.md) – wenn etwas nicht geht
