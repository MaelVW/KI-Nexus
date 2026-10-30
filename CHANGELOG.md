# Changelog

Alle wichtigen Änderungen an KI-Nexus stehen hier.
Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), Versionen nach [Semantic Versioning](https://semver.org/lang/de/).
Version 1.0.0 = Meilensteine M0 bis M4.

## [Unveröffentlicht]

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
