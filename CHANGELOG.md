# Changelog

Alle wichtigen Änderungen an KI-Nexus stehen hier.
Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), Versionen nach [Semantic Versioning](https://semver.org/lang/de/).
Version 1.0.0 = Meilensteine M0 bis M4.

## [Unveröffentlicht]

## [0.3.0] – 2026-10-30 – Meilenstein 2: Chat

### Neu

- **Chat** im Chat-Tab, ersetzt das Test-Feld aus M1. Ein Chat, zwei Ansichten: Wechselt man mitten im Gespräch den Stil, bleibt der Verlauf gleich.
- **Simpel-Stil**: Nachrichten als Blasen, Markdown (Markdig mit eigenem WPF-Renderer), Codeblöcke mit Syntax-Farben (AvalonEdit) und Kopier-Knopf, Gedanken als einklappbarer Block „Nachgedacht für X Sekunden“, Knöpfe kopieren / neu erzeugen / Daumen hoch / Daumen runter / Info, letzte Nachricht bearbeiten und neu senden.
- **Terminal-Stil**: Cascadia Mono, Prompt `mael@nexus ›`, Schreibmaschinen-Effekt im Tempo des Streamings, blinkender Block-Cursor, Farben nach Bauplan, alle Befehle (`/help`, `/think on|off|show|hide`, `/ki`, `/style`, `/new`, `/clear`, `/stop`, `/stats`, `/unload`), Pfeil hoch/runter, Tab-Vervollständigung, `Strg + C`. Keine echten Shell-Befehle.
- **Seitenleiste** mit allen Chats: neu, umbenennen, löschen (mit Rückfrage), suchen (Titel und Inhalt), einklappbar.
- **Chat-Datenbank** `data/chats.db` (SQLite) mit Versionsnummer und Migrationen. Tabellen nach Bauplan, dazu `thinking_ms` und `context_tokens`.
- **Bilder anhängen** per +-Knopf oder Drag & Drop. Grau mit Hinweis, wenn die KI keine Bilder versteht. Bilder werden nach `data/attachments/` kopiert.
- **Kontext-Anzeige** (z.B. `243 / 8192 Token`). Wird der Kontext voll, fallen die ältesten Nachrichten weg, der Systemprompt bleibt. Mit Hinweis.
- **Chat-Titel** nach der ersten Antwort: ohne Thinking, kurz, abbrechbar. Ausgeschaltet: die ersten Wörter der Frage.
- **Bewertung** mit Daumen hoch/runter, gespeichert für M8.
- Thinking pro Chat, Gedanken anzeigen/verstecken für alle Chats. Statistik pro Antwort.
- Rückfragen (z.B. vor dem Löschen) im dunklen Stil der App statt als helle Windows-Meldung.
- Beim Start öffnet die App den zuletzt benutzten Chat. Öffnet man einen Chat, wechselt die KI-Auswahl zu dessen KI.
- `Strg + N` für einen neuen Chat.
- 159 Unit-Tests (vorher 94).

### Geändert

- `IInferenceBackend` hat `SupportsImagesAsync`. Bilder gehen an llama.cpp (OpenAI-Format) und Ollama (`images`).
- `EngineManager` kennt alle laufenden Anfragen: „Entladen“ bricht auch eine Titel-Erzeugung ab.
- `PromptBuilder` entfernt; `ContextBudget` baut die Nachrichten jetzt (mit Kürzung).
- Symbole in `Glyphs.cs` stehen als lesbare Codes (``) statt als unsichtbare Zeichen.

### Entfernt

- Das vorläufige Test-Feld aus M1.

## [0.2.0] – 2026-10-30 – Meilenstein 1: Engines

### Neu

- `scripts/setup-engines.ps1` lädt llama.cpp b11960 (= v0.8.0, CUDA 13.4 für RTX 50) nach `engines/llama.cpp/`, mit Prüfung der SHA-256-Prüfsummen. `-Version latest` holt das neueste Release.
- Eigene Engine **llama.cpp**: Die App startet `llama-server` pro Modell, wartet bis er bereit ist, wärmt ihn auf und streamt die Antworten. Entladen = Prozess beenden.
- Zweite Engine **Ollama** über die HTTP-API, nur wenn Ollama läuft. Ohne Ollama zeigt die App einen klaren Hinweis.
- Gemeinsame Schnittstelle `IInferenceBackend` und `EngineManager`: immer nur ein Modell geladen, Laden bei Bedarf, automatisches Entladen nach „Geladen halten“ (Standard 5 Minuten).
- Thinking pro Frage ein und aus, für beide Modellfamilien ausdrücklich gesetzt. Gedanken getrennt von der Antwort.
- Abbrechen einer Antwort (Knopf „Stopp“ oder `Esc`).
- **Ressourcen-Anzeige**: VRAM (über NVML) und RAM, alle 2 Sekunden, gelb ab 90 %.
- Knopf **Entladen** in der Kopfleiste. Leuchtet, solange ein Modell geladen ist.
- **KI-Auswahl** mit Engine-Schild und Status-Punkt. Standard-KI im Zahnrad einstellbar.
- **Import aus Ollama per Hardlink** (kein doppelter Speicherplatz, Ollama-Dateien bleiben unverändert). `model.json` mit Infos aus dem GGUF-Kopf.
- Vier KIs: Mael-KI und Qwen schnell, je für llama.cpp und Ollama.
- **Test-Feld** im Chat-Tab (vorläufig bis M2).
- Hilfsprozesse hängen an einem Windows Job Object: Stürzt die App ab, endet auch llama-server.
- `Start-KI-Nexus.bat` bietet den Download von llama.cpp an, wenn er fehlt.
- Messwerte beider Engines und Antworten auf die offenen Fragen in `docs/02-Engines.md`.
- 94 Unit-Tests (vorher 39).

### Geändert

- Kopfleiste kompakter, damit sie auch bei 1100 Pixel Breite passt: VRAM und RAM stehen übereinander, „Entladen“ ist ein Symbol-Knopf.
- `SettingsJson` heisst jetzt `JsonDefaults`, weil auch KI-Profile und `model.json` es nutzen.

## [0.1.0] – 2026-10-30 – Meilenstein 0: Gerüst

### Neu

- Solution `KI-Nexus.sln` mit den Projekten `KiNexus.App` (WPF), `KiNexus.Core`, `KiNexus.Engines` und zwei Testprojekten.
- Ordnerstruktur nach Bauplan, `.gitignore` (ohne `models/`, `data/`, `engines/`, `python/.venv/`).
- Theme dunkel mit Cyan: Farben, Schriften, Stile für Knöpfe, Felder, Auswahllisten, Schalter, Tabs, Bildlaufleisten und Hinweise.
- Hauptfenster mit eigener, dunkler Titelleiste und den Tabs Chat, Feineinstellungen, Training, System-Prompt und Create-Hub. Jeder Tab zeigt „Kommt in Meilenstein …“.
- Kopfleiste mit KI-Auswahl, Stil-Umschalter (nur im Chat-Tab), Platz für die Ressourcen-Anzeige, Knopf „Entladen“ und Zahnrad. KI-Auswahl, Ressourcen und „Entladen“ sind noch ohne Funktion (M1).
- Einstellungs-Dialog: Akzentfarbe (wirkt sofort), Standard-Stil, Engine-Reihenfolge, Ordner, Ports, Titel-Erzeugung, Open-WebUI-Optionen. Mit Prüfung der Eingaben.
- Einstellungen in `data/settings.json`: sicheres Speichern, Standardwerte, Korrektur ungültiger Werte, Sicherung einer kaputten Datei.
- Fenstergrösse, Lage und letzter Tab bleiben nach dem Neustart erhalten.
- Logging mit Serilog nach `data/logs/` (eine Datei pro Tag, 14 Tage).
- Tastenkürzel `Strg + 1` bis `5` (Tabs) und `Strg + ,` (Einstellungen).
- `Start-KI-Nexus.bat`: baut bei Bedarf und startet die App per Doppelklick.
- App-Icon.
- Doku-Gerüst in `docs/` (00 bis 09).
- 39 Unit-Tests für Pfade, Einstellungen, Farben, Fensterlage und Ports.
