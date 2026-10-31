# Changelog

Alle wichtigen Änderungen an KI-Nexus stehen hier.
Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), Versionen nach [Semantic Versioning](https://semver.org/lang/de/).
Version 1.0.0 = Meilensteine M0 bis M4.

## [Unveröffentlicht]

## [1.2.0] – 2026-10-31 – Meilenstein 6: System-Prompt und Feineinstellungen

### Neu

- **System-Prompt-Tab:** KI-Liste, Editor mit Token-Schätzung, Vorlagen zum Einfügen an der Cursor-Stelle, Speichern und Verwerfen.
- **Versionen:** Die App merkt sich die letzten 10 Fassungen pro KI (`system_prompt_versions` in `data/chats.db`), beim ersten Speichern auch den bisherigen Prompt. Die Liste zeigt, was sich geändert hat. Eine alte Fassung holt man mit einem Klick in den Editor.
- **Kurzer Test** im Tab: Test-Chat mit dem Prompt aus dem Editor, auch ungespeichert. Thinking aus, nichts wird gespeichert, mit Stopp.
- **Eigenes Ollama-Modell pro KI:** Beim Speichern einer Ollama-KI erstellt die App über `POST /api/create` ein Modell mit dem Namen der KI (z.B. `mael-ki-ollama` = `gemma4:26b` + Prompt + Werte). Das Grundmodell bleibt unverändert. Knopf „Ollama-Modell aktualisieren“, falls Ollama beim Speichern nicht lief.
- **Feineinstellungen-Tab:** Temperatur, top_p, top_k, min_p, Wiederholungs- und Themen-Strafe, Kontextlänge, Antwortlänge, Thinking, GPU-Layer, MoE-Experten auf CPU, Geladen halten. Jede Einstellung mit Erklärung und Tooltip, jede Eingabe wird geprüft.
- **Speicher-Schätzung mit Warnung:** Ampel und Schätzung zur gewählten Kontextlänge. Warnung, wenn der Speicher knapp wird oder nicht reicht.
- **Auf Standard zurücksetzen:** Empfehlungen des Herstellers aus der GGUF-Datei (`general.sampling.*`), ergänzt mit den Ollama-Parametern.
- **Draft-Modell als Schalter** (Standard aus), nur wenn das Modell einen Draft hat. llama-server bekommt dann `-md … --spec-type draft-mtp`.
- Warnung beim KI-Wechsel mit ungespeicherten Änderungen.
- 313 Unit-Tests (vorher 280).

### Geändert

- Der GGUF-Leser liest auch `min_p`, `penalty_repeat` und die Anzahl Schichten.
- Ändert man Draft, Kontext oder GPU-Werte einer geladenen KI, lädt die App sie bei der nächsten Frage neu. Ein neuer Systemprompt braucht kein Neuladen.

## [1.1.0] – 2026-10-31 – Meilenstein 5: Alternate Launch

### Neu

- **Alternate Launch:** Knopf (Weltkugel) in der Kopfleiste mit Statuspunkt. Das Panel hat Starten, Im Browser öffnen, Stoppen, einen Hinweis und das Log.
- Startet Open WebUI wie bisher von Hand (`uvx --python 3.11 open-webui@<Version> serve`), aber nur auf 127.0.0.1, mit `DATA_DIR` und ohne Telemetrie. Wartet, bis Open WebUI antwortet, und öffnet dann den Browser.
- Prüfung vor dem Start: Ist uvx da? Ist der Port frei (oder läuft Open WebUI schon)? Läuft Ollama oder llama-server? Mit Anleitung, wie Open WebUI llama-server einbindet.
- Nimmt den bestehenden Geheimschlüssel von Open WebUI, damit Anmeldungen gültig bleiben.
- „Beim Schliessen von KI-Nexus beenden“ (Standard ein). Aus: Open WebUI läuft weiter, und KI-Nexus findet es beim nächsten Start wieder und kann es stoppen.
- Log in `data/logs/open-webui.log`.
- 280 Unit-Tests (vorher 257).

### Geändert

- `JobObject` ist öffentlich und kann alle Prozesse darin beenden oder auflisten. `ProcessSupervisor.Start` kann Programme ausserhalb des gemeinsamen Job Objects starten.
- Fehler beim Aufräumen der Dienste halten das Beenden der App nicht mehr auf.

## [1.0.0] – 2026-10-31 – Meilenstein 4: Gespräch-Stil

Version 1.0 = Meilensteine M0 bis M4: Chat in drei Stilen und der Create-Hub.

### Neu

- **Gespräch-Stil** im Chat-Tab: Orb in der Mitte (pulsiert beim Zuhören, Ring dreht sich beim Verstehen und Denken, wächst mit der Stimme), Mitschrift darunter. Es ist derselbe Chat wie in Simpel und Terminal.
- **Push-to-Talk** (Leertaste oder Orb gedrückt halten) und **Freihändig** (Sprechpausen-Erkennung mit Silero-VAD, 0.8 s Stille), mit Schalter im Gesprächsfenster.
- **Spracherkennung** mit faster-whisper (large-v3-turbo, auf der CPU, umschaltbar auf small) in einem Python-Hilfsprozess (`python/voice/worker.py`, JSON Lines).
- **Sprachausgabe** mit Piper (de_DE-thorsten-high), satzweise: Die KI spricht schon, während das Modell noch schreibt. Wiedergabe mit NAudio.
- Im Gespräch ist Thinking immer aus. Die App hängt „Antworte kurz und so, wie man spricht.“ an den Systemprompt (abschaltbar).
- Markdown wird vor dem Vorlesen entfernt. Codeblöcke liest die KI nicht vor („Den Code findest du in der Mitschrift.“).
- Unterbrechen mit `Esc`, Knopf oder Push-to-Talk-Taste.
- Schutz vor erfundenen Mitschriften: Stille geht nicht an Whisper, unsichere Stücke und typische Whisper-Erfindungen werden verworfen.
- Einstellungen → Sprache: Mikrofon, Stimme, Whisper-Modell, CPU/GPU, Push-to-Talk-Taste, Sprechpause, kurze Antworten.
- `scripts/setup-voice.ps1`: richtet Python-Umgebung (uv), Whisper-Modell und Stimme ein, mit SHA-256-Prüfung.
- „Entladen“ beendet auch den Sprach-Worker (gibt ca. 2 GB RAM frei).
- 257 Unit-Tests (vorher 213).

### Geändert

- `ChatService.SendAsync` kann einen Zusatz zum Systemprompt für eine einzelne Frage mitgeben.
- `ProcessSupervisor.Start` kann stdin/stdout umleiten (für den Python-Worker).

### Bekannt

- Unterbrechen per Stimme (Freihändig) kommt in M7.
- Freihändig hört alle Stimmen im Raum. Bei Gesprächen oder Videos im Hintergrund Push-to-Talk nehmen.
- Spracherkennung auf der GPU ist vorbereitet, aber die NVIDIA-Bibliotheken dafür sind nicht installiert.

## [0.4.0] – 2026-10-31 – Meilenstein 3: Create-Hub

### Neu

- **Create-Hub** als Tab mit vier Bereichen: Übersicht, Neue KI, Importieren, Hugging Face.
- **Übersicht**: Meine KIs, Grundmodelle (Grösse, Quantisierung, Bilder, Lizenz, Herkunft, welche KIs es nutzen) und Ollama-Modelle (nur wenn Ollama läuft). Knöpfe: Chatten, Neue KI daraus, Ladetest, Ordner öffnen, Löschen.
- **Ampel** pro Modell: passt in VRAM / teils im RAM / zu gross. Ehrlich als Schätzung bezeichnet, die Zahlen stehen im Tooltip (`FitEstimator`).
- **Assistent „Neue KI“** in 4 Schritten: Grundmodell, Name und Beschreibung, Systemprompt mit Vorlagen (Allgemein, Schule, Programmieren, Kurz und knapp), Parameter (Standard, Genau, Kreativ) mit Kontextlänge und Thinking. Speichert `kis/<id>.json` und bietet einen Test-Chat an.
- **Löschen** mit Rückfrage: KIs, Grundmodelle (mit Warnung, wenn KIs das Modell nutzen) und Ollama-Modelle (über die Ollama-API, mit Hinweis auf Open WebUI). Geladene Modelle werden vorher entladen.
- **GGUF-Import** per Drag & Drop oder Datei-Dialog, optional mit `mmproj`. Rückfrage „Kopieren oder verschieben?“, Fortschritt, geteilte Modelle. Aus dem Ollama-Ordner wird nie verschoben.
- **Aus Ollama übernehmen** im Create-Hub, danach automatisch ein **Ladetest** mit llama.cpp. Schlägt er fehl: Vorschlag, das Modell von Hugging Face zu laden.
- **Hugging Face**: Suche (nur GGUF, mit Downloads, Likes, Datum, Lizenz, Hinweis „gesperrt“ und „kein Chat-Modell“), Quantisierungen mit Grösse, Ampel und Empfehlung (Q4_K_M), Projektor wählbar.
- **Download** mit Fortschritt, Tempo und Restzeit, Pause und Fortsetzen (HTTP-Range), SHA-256-Prüfung, Platz-Prüfung, automatischer Wiederholung bei hängender Verbindung. Angefangene Downloads überleben einen Neustart.
- **Hugging-Face-Token** für gesperrte Modelle, verschlüsselt mit Windows DPAPI (`data/hf-token.bin`).
- Knopf **„Auf Hugging Face öffnen“** öffnet das Modell im Browser.
- `model.json` enthält jetzt die Sampling-Empfehlung des Herstellers aus der GGUF-Datei (`temperature`, `topP`, `topK`).
- 213 Unit-Tests (vorher 159).

### Geändert

- Die KI-Auswahl in der Kopfleiste aktualisiert sich sofort, wenn eine KI dazukommt oder gelöscht wird.
- Screenreader lesen die Chat-Titel in der Seitenleiste vor (vorher stand dort der Klassenname).

## [0.3.0] – 2026-10-30 – Meilenstein 2: Chat

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
