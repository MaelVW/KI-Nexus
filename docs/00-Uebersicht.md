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
| M3 | Create-Hub: Übersicht mit Ampel, neue KI, löschen, GGUF-Import, Ollama-Import, Hugging Face | ✅ fertig (v0.4.0) |
| M4 | Gespräch-Stil: Spracherkennung (Whisper), Stimme (Piper), Orb, Mitschrift, Push-to-Talk und Freihändig | ✅ fertig (v1.0.0) |
| M5 | Alternate Launch: Open WebUI per Knopf starten und stoppen | ✅ fertig (v1.1.0) |
| M6 | System-Prompt-Tab (Versionen, Test) und Feineinstellungen-Tab (Speicher-Warnung, Draft-Schalter) | ✅ fertig (v1.2.0) |
| M7 | Gespräch-Feinschliff: Modus pro KI und pro Chat, Freihändig startet von selbst, Unterbrechen durch Sprechen, weibliche Stimme | ✅ fertig (v1.3.0) |
| M8 | Training-Tab: Datensatz, LoRA-Training mit PEFT, Loss-Kurve, Adapter als neue KI | ✅ fertig (v1.4.0) |
| M9–M10 | Server, Chatraum | offen |
| Video V0 | Machbarkeitstest Videomodell (ComfyUI + LTX-2.3, noch ohne App), siehe `docs/10-Video.md` | ✅ erledigt |
| Video V1 | Video-Engine: `IVideoBackend` mit ComfyUI, `scripts/setup-video.ps1`, noch ohne Oberfläche | ✅ erledigt |
| Video V2 | Video-Tab: Auftrag mit Speicher-Ampel, Fortschritt, Abbrechen, Player, Galerie, Chat-Sperre während des Videos | ✅ fertig (v1.5.0) |
| Video V3 | Videomodelle im Create-Hub und in der Hugging-Face-Suche, Modellwahl im Video-Tab | ✅ fertig (v1.6.0) |
| Video V4 | Bild zu Video, Prompt verbessern (KI), Warteschlange | ✅ fertig (v1.8.0) |
| Bild B0 | Zwei Bildmodelle gemessen (FLUX.2 klein, Z-Image Turbo), Empfehlung FLUX.2 klein, siehe `docs/11-Bild.md` | ✅ erledigt |
| Bild B1–B2 | Bild-Engine, Studio-Tab (Video \| Bild), Galerie mit Filter, „Als Startbild für ein Video“ | ✅ fertig (v1.9.0) |
| Bild Referenzen | Referenzbilder für FLUX.2 klein (bis 3), „Als Referenz für ein Bild“ | ✅ fertig (v1.10.0) |
| Bild Modelle + Vorlage | Eigene Bildmodelle (FLUX.2 klein 4B/9B, Z-Image) erkannt, Bild zu Bild mit Stärke | ✅ fertig (v1.11.0) |
| Training 2 T1 | Weitertrainieren mit Versionen (v1, v2, …), zurück zu einer älteren, Warnung bei Auswendiglernen, siehe `docs/06-Training.md` | ✅ fertig (v1.12.0) |
| Installieren und Updates | Ein Befehl zum Installieren, „Nach Updates suchen“ in den Einstellungen (nur auf Klick), Releases mit `scripts/release.ps1`, siehe `docs/12-Updates.md` | ✅ fertig (v1.13.0) |
| Komfort 1 | Privatordner im Studio, eigene Vorlagen für den System-Prompt, eigene Grundmodelle fürs Training (bis 5B) | ✅ fertig (v1.14.0) |
| Szenen S1–S2 | Längere Videos aus Szenen (letztes Bild → nächste Szene), Warteschlange, Zusammensetzen. S3 (Keyframes) offen | ✅ fertig (unveröffentlicht) |

## Ordner

| Ordner / Datei | Inhalt | In Git? |
|---|---|---|
| `Start-KI-Nexus.bat` | Start per Doppelklick. Baut die App bei Bedarf, bietet den Download von llama.cpp an. | ja |
| `KI-Nexus.sln` | Die Solution. Öffnen mit Visual Studio oder Rider. | ja |
| `docs/` | Diese Doku, eine Datei pro System | ja |
| `kis/` | Deine KIs, eine JSON-Datei pro KI | ja |
| `models/` | Grundmodelle als GGUF, ein Ordner pro Modell. In `models/.downloads/` liegen angefangene Downloads. | nein |
| `data/` | `chats.db`, `settings.json`, angehängte Bilder, Logs, Cache, `hf-token.bin` (verschlüsselt) | nein |
| `engines/` | llama.cpp (von `scripts/setup-engines.ps1`), Umwandlungs-Skripte fürs Training, ComfyUI für Video (von `scripts/setup-video.ps1`) | nein |
| `src/KiNexus.App/` | Die Oberfläche (WPF): Fenster, ViewModels, Theme | ja |
| `src/KiNexus.Core/` | Logik ohne Oberfläche: Einstellungen, KIs, EngineManager, Prozesse, Messung | ja |
| `src/KiNexus.Engines/` | Anbindung an llama.cpp, Ollama und Hugging Face, Import aus Ollama | ja |
| `tests/` | Unit-Tests für Core und Engines | ja |
| `python/` | Sprach-Worker (`voice/worker.py`, M4), Training (`training/`, M8), Video-Test (`video/`, V0), Bild-Test (`image/`, B0). Umgebungen in `python/.venv`, `python/training/.venv` und `engines/comfyui/.venv` | ja (ohne `.venv`) |
| `scripts/` | PowerShell-Skripte: `setup-engines.ps1` (llama.cpp), `setup-voice.ps1` (Sprache), `setup-training.ps1` (Training), `setup-video.ps1` (Video und Bild, `-Images`) | ja |

## So hängt alles zusammen

```
Start-KI-Nexus.bat
   └─ baut und startet  KI-Nexus.exe  (src/KiNexus.App)
                           ├─ nutzt KiNexus.Core     (Einstellungen, KIs, EngineManager)
                           └─ nutzt KiNexus.Engines  (llama.cpp, Ollama)
                                     ├─ startet  engines\llama.cpp\llama-server.exe  (Port 8090)
                                     ├─ startet  python\voice\worker.py (Whisper + Piper, stdin/stdout)
                                     ├─ startet  Open WebUI über uvx (nur per Knopf)  (Port 8080)
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

## Bedienung (Stand M6)

| Was | Wo |
|---|---|
| KI wählen | Kopfleiste, Auswahl links. Das Schild zeigt die Engine (llama.cpp oder Ollama). |
| Status der Engine | Punkt neben der Auswahl: grau = nichts geladen, gelb = lädt, grün = bereit, leuchtend = antwortet. Mit der Maus darüber steht der Text. |
| VRAM und RAM | Kopfleiste rechts, alle 2 Sekunden gemessen. Gelb ab 90 %. |
| Modell entladen | Power-Symbol in der Kopfleiste. Es leuchtet, solange ein Modell geladen ist. |
| Open WebUI | Weltkugel in der Kopfleiste (Alternate Launch): starten, im Browser öffnen, stoppen, Log. Details: [07-Open-WebUI.md](07-Open-WebUI.md) |
| Chatten | Chat-Tab, Stil Simpel oder Terminal. Details: [04-Chat-und-Stile.md](04-Chat-und-Stile.md) |
| Sprechen | Chat-Tab, Stil Gespräch. Einmal `scripts\setup-voice.ps1` ausführen. Modus, Stimme und Tempo pro KI: Feineinstellungen → Gespräch. Details: [05-Sprache.md](05-Sprache.md) |
| KIs und Modelle verwalten | Create-Hub-Tab: Übersicht, Neue KI, Importieren, Hugging Face. Details: [03-Modelle-und-KIs.md](03-Modelle-und-KIs.md) |
| Eigene KI trainieren (LoRA) | Training-Tab (`Strg + 3`). Einmal `scripts\setup-training.ps1` ausführen. Details: [06-Training.md](06-Training.md) |
| Systemprompt ändern, testen, alte Fassung zurückholen | System-Prompt-Tab (`Strg + 4`). Details: [03-Modelle-und-KIs.md](03-Modelle-und-KIs.md#system-prompt-tab) |
| Temperatur, Kontextlänge, GPU-Layer, Draft … | Feineinstellungen-Tab (`Strg + 2`), mit Speicher-Warnung. Details: [03-Modelle-und-KIs.md](03-Modelle-und-KIs.md#feineinstellungen-tab) |

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
| `Leertaste` (halten) | Push-to-Talk im Gespräch-Stil (Taste einstellbar) |
| `Esc` | im Gespräch: KI unterbrechen |

## Weiter lesen

- [01-Architektur.md](01-Architektur.md) – Schichten, Klassen, Einstellungen
- [02-Engines.md](02-Engines.md) – llama.cpp und Ollama, Messwerte
- [03-Modelle-und-KIs.md](03-Modelle-und-KIs.md) – Modell-Ordner, KI-Profile, Create-Hub, Import, Hugging Face
- [04-Chat-und-Stile.md](04-Chat-und-Stile.md) – Simpel, Terminal, Gespräch, Befehle, Bilder, Kontext, Datenbank
- [05-Sprache.md](05-Sprache.md) – Gespräch-Stil: Whisper, Piper, Einrichten, Messwerte, Protokoll
- [06-Training.md](06-Training.md) – Training: Datensatz, LoRA mit PEFT, Umwandlung nach GGUF
- [07-Open-WebUI.md](07-Open-WebUI.md) – Alternate Launch: Open WebUI starten, llama-server anbinden
- [09-Fehlerbehebung.md](09-Fehlerbehebung.md) – wenn etwas nicht geht
