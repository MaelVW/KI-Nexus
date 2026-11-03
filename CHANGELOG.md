# Changelog

Alle wichtigen Änderungen an KI-Nexus stehen hier.
Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), Versionen nach [Semantic Versioning](https://semver.org/lang/de/).
Version 1.0.0 = Meilensteine M0 bis M4.

## [Unveröffentlicht]

## [1.13.0] – 2026-11-03 – Installieren und Updates

### Neu

- **Installieren mit einem Befehl:** `irm https://raw.githubusercontent.com/MaelVW/KI-Nexus/main/scripts/install.ps1 | iex` lädt die neueste freigegebene Version, baut sie und startet sie (`scripts/install.ps1`).
- **Updates in der App:** Einstellungen → Updates → „Nach Updates suchen“ (nur auf Klick, nie beim Start, eine einzige Anfrage an GitHub). „Jetzt aktualisieren“ schliesst die App, ersetzt die Programmteile und startet neu. Chats, Einstellungen, Modelle, Engines, Python-Umgebungen und KIs bleiben.
- **Releases:** `scripts/release.ps1` gibt eine Version als GitHub Release frei (Tag und Text aus dem Changelog). Updates gibt es nur für freigegebene Versionen.
- Ein Entwickler-Ordner (git) wird nie vom Update überschrieben.
- Doku: `docs/12-Updates.md`. 478 Unit-Tests.

## [1.12.0] – 2026-11-03 – Training 2, T1: Weitertrainieren mit Versionen

### Neu

- **Weitertrainieren (Training 2, T1):** Ein früherer Adapter lernt mit neuen Beispielen oder mehr Durchgängen weiter (`train.py` mit `resumeAdapterDir`, PEFT lädt den Adapter trainierbar).
- **Versionen:** Jede Runde hat einen eigenen Ordner (`runs/<name>/v<n>`) und einen eigenen Adapter (`<name>-v<n>.gguf`), mit `version.json`. Ältere Trainings gelten als v1. „Für diese KI verwenden“ wechselt die Version, auch zurück.
- Wahl „Neu beginnen / Weitertrainieren“, vorsichtigeres Lerntempo beim Weitertrainieren, Loss-Kurve über alle Runden, Warnung bei Loss unter 0.05 (auswendig gelernt).

### Geändert

- Ein neues Training löscht keinen alten Arbeitsordner mehr. Ein schon vergebener Name geht nur noch mit „Weitertrainieren“.

### Getestet

- Abnahme: Test-KI v1 (12 Beispiele), v2 mit 10 neuen Beispielen weitertrainiert (Loss startet bei 1.48 statt 6.12). v2 kennt Name und Lieblingsfarbe, Wechsel auf v2 und zurück auf v1 in der App.
- 464 Unit-Tests.

## [1.11.0] – 2026-11-03 – Bild: eigene Bildmodelle, Vorlage (Bild zu Bild)

### Neu

- **Eigene Bildmodelle im Studio:** FLUX.2 klein (4B und 9B) und Z-Image, die man selbst lädt (Hugging Face oder Import), erscheinen in der Auswahl. Erkannt am GGUF-Kopf und an der Grösse einzelner Tensoren (`ImageModelCatalog`, `GgufReader.ReadTensorShapes`). Nicht nutzbare Modelle (FLUX.1, FLUX.2 dev, Lumina) zeigen den Grund.
- Create-Hub: „Im Studio nutzen“ für Bild- und Videomodelle. Hugging Face: Etikett „Bildmodell (FLUX / Z-Image)“ und eigene Rückfrage.
- **Vorlage (Bild zu Bild):** Ein Bild ist der Ausgangspunkt, mit Schieber für die Stärke (0.2–1.0, Standard 0.8). Geht mit FLUX.2 klein und Z-Image. Im Studio „Vorlage (optional)“ und an jedem Bild der Galerie „Als Vorlage für ein Bild“.
- Geprüft: Z-Image Turbo kann keine echten Referenzbilder (Artefakte), Bild zu Bild aber schon (`docs/11-Bild.md`).
- Abnahme in der App: Modellauswahl mit beiden Modellen, Hütte als Vorlage (Herbst) in 18 s für 2 Bilder.
- 457 Unit-Tests, echter Lauf mit Referenz und Vorlage bestanden.

## [1.10.0] – 2026-11-03 – Bild: Referenzbilder

### Neu

- **Referenzbilder bei Bildern:** bis zu 3 Bilder, an denen sich FLUX.2 klein orientiert (dieselbe Person, dasselbe Tier, derselbe Ort). Im Prompt sagt man, was damit passieren soll. Jede Referenz kostet ca. 5 s.
- Im Studio: Bereich „Referenzbilder (optional)“ und an jedem Bild der Galerie „Als Referenz für ein Bild“.
- Die Galerie behält Kopien der Referenzen, „Prompt übernehmen“ und „Nochmal“ nehmen sie mit.
- Z-Image Turbo kann keine Referenzen. Der Hinweis im Studio sagt das.
- Abnahme in der App: Hütte als Referenz, „dieselbe Hütte im Sommer“ in 21 s. Echter Lauf mit Referenz über die Schnittstelle bestanden. 440 Unit-Tests.

## [1.9.0] – 2026-11-03 – Bild B0–B2: Bildgenerierung im Studio

### Neu

- **Bildgenerierung** über dasselbe ComfyUI wie die Videos. Standard: **FLUX.2 klein 4B** (Apache 2.0, 4 Schritte), ein Bild in 1024×1024 in ca. 10 s. **Z-Image Turbo** geht auch, wenn die Datei da ist (besser bei Schrift, ca. 25 s).
- B0: beide Modelle gemessen (Zeit, VRAM, RAM, Qualität), Ergebnis und Empfehlung in `docs/11-Bild.md`, Testskript `python/image/b0_test.py`.
- B1: `ImageRequest` (Format, 1–4 Bilder, Seed), `ImageModelSet`, zwei Workflow-Vorlagen, `GenerateImageAsync` mit Fortschritt und Abbrechen. Alle Bilder eines Auftrags in einem Durchgang.
- `scripts\setup-video.ps1 -Images` lädt Bildmodell, Text-Encoder (Qwen3 4B) und VAE nach `models\image` (ca. 12 GB, mit Prüfsumme).
- B2: Der Tab **Video** heisst jetzt **Studio**, mit Umschalter **Video | Bild**. Eigener Prompt pro Art, „Prompt verbessern“ auch für Bilder.
- Gemeinsame **Warteschlange** für Videos und Bilder.
- **Galerie mit Filter** Alle / Videos / Bilder. Bilder liegen in `data/images`. Ein gewähltes Bild steht gross statt des Players.
- **„Als Startbild für ein Video“:** aus einem Bild sofort einen Clip machen.
- Abnahme in der App: 4 Bilder in 34 s (inkl. Start von ComfyUI), daraus ein Video in 2 min 11 s, Galerie-Filter geprüft.
- 437 Unit-Tests (vorher 418), dazu ein echter Bild-Lauf über die Schnittstelle (`KINEXUS_VIDEO_TEST=1`).

### Geändert

- Der Ablauf im Backend (Start, Sperre, Meldungen von ComfyUI) ist für Video und Bild derselbe. Video verhält sich wie vorher.

### Doku

- **Bauplan Bild, Training 2 und Szenen** (`docs/Bauplan-Bild-und-Szenen.md`): Bildgenerierung (B0–B2), Weitertrainieren und Trainingsdaten mit einer KI (T1–T2), längere Videos aus Szenen mit Keyframes und Warteschlange (S0–S3). Noch nichts gebaut.

## [1.8.0] – 2026-11-02 – Video V4: Bild zu Video, Prompt verbessern, Warteschlange

### Neu

- **Bild zu Video:** Startbild wählen. Das Video beginnt mit diesem Bild (`LTXVImgToVideoInplace`, zugeschnitten auf das Format). Die Galerie behält eine Kopie des Startbilds.
- **Prompt verbessern:** Die KI aus der Kopfleiste macht aus einer Idee, auch auf Deutsch, einen englischen Video-Prompt. Mit „Rückgängig“. Danach wird das Chat-Modell wieder entladen, wenn es erst dafür geladen wurde.
- **Warteschlange:** „In die Warteschlange“ legt Aufträge mit allen Einstellungen ab, sie laufen nacheinander. Das Formular bleibt bedienbar, während ein Video entsteht.
- Der Dateidialog für Bilder hat einen eigenen Titel pro Zweck.
- Abnahme in der App: Prompt verbessert (Qwen, 15 s), Bild zu Video in 1 min 34 s, zwei Videos über die Warteschlange.
- 418 Unit-Tests (vorher 411).

## [1.7.0] – 2026-11-02 – Video V3.1: dev-Modelle

### Neu

- **dev-Modelle von LTX-2.3** im Video-Tab: 20 Schritte mit CFG 4 und Negativ-Prompt (`LTXVScheduler`). Vorher galten sie als nicht nutzbar, und Modelle ohne Art im Namen wurden mit distilled verrauscht.
- **Ablauf wählbar** („Schnell: distilled“ / „Genau: dev“), Vorgabe aus dem Dateinamen, gemerkt pro Modell (`video.devByModel`).
- Feld **Negativ-Prompt** beim dev-Ablauf, leer = Standard von Lightricks.
- Text-Encoder und VAEs gelten für dev und distilled (Gewichte verglichen: gleich).
- Das offizielle Modell steht in der Auswahl immer zuoberst.
- Test: dev Q2_K, 512×320, 2 s in 4 min 08 s, scharfes Bild. 411 Unit-Tests.

## [1.6.0] – 2026-11-02 – Video V3: Videomodelle im Create-Hub

### Neu

- **Modellwahl im Video-Tab:** das offizielle LTX-2.3 und alle passenden Videomodelle aus `models/` (z.B. von Hugging Face). Die App merkt sich die Wahl (`video.modelPath`).
- **Create-Hub:** Videomodelle zeigen „Nutzbar im Video-Tab“ und den Knopf „Im Video-Tab nutzen“ statt „Neue KI daraus“ und „Ladetest“. Für Videomodelle gibt es keine Ampel.
- **Hugging-Face-Suche:** Etikett „Videomodell (LTX)“, eigene Rückfrage vor dem Download, nach dem Download „Im Video-Tab nutzen“. Gleiche Stufen mehrmals im Repository haben jetzt den Ordner im Namen, z.B. „Q2_K (distilled-1.1)“.
- **ComfyUI** bekommt Modelle aus anderen Ordnern über `kinexus-paths.yaml`, ohne Kopie. Bei einem neuen Ordner startet es kurz neu.
- `setup-video.ps1 -Companions`: nur Text-Encoder, Projektion und VAEs. Der Video-Tab startet das Skript auf Knopfdruck in einem eigenen Fenster.
- Abnahme: `unsloth/LTX-2.3-GGUF` Q2_K (distilled-1.1) in der App gesucht, geladen und im Video-Tab genutzt (Clip in 2 min 06 s). „In den Papierkorb“ geprüft.
- 410 Unit-Tests (vorher 397).

### Behoben

- LTX-2.3-Dateien ohne `model_version` (z.B. von Unsloth) galten als „Version unbekannt“. Jetzt erkennt die App die Version am eingebetteten `config`.
- Beim Schliessen der App lief das Aufräumen des Video-Tabs zweimal und brach mit einer Ausnahme ab (seit 1.5.0).
- Kurz „Bilder erzeugen …“ am Anfang eines Videos.

## [1.5.0] – 2026-11-02 – Video (Bauplan Video, V0 bis V2)

### Neu

- **Video-Tab** (`Strg + 6`, V2):
  - Auftrag mit Prompt, Format (quer, hochkant, klein), Länge (2–10 s), Ton und Seed.
  - Speicher-Ampel: freier RAM, Grösse, Chat-Modell geladen, Akku.
  - Fortschritt mit Abschnitt, Schritten, Rest- und Wartezeit. Abbrechen.
  - Player und Galerie mit Vorschaubildern: Prompt übernehmen, nochmal mit gleichem oder neuem Seed, Ordner öffnen, in den Papierkorb.
- Fertige Videos liegen in `data/videos` (MP4, PNG, JSON mit allem zum Nachbauen).
- **Während ein Video entsteht, ist der Chat gesperrt** (`EngineManager.Block`). Der Chat sagt klar, warum. Ein geladenes Chat-Modell wird nach Rückfrage entladen.
- ComfyUI endet nach 10 Minuten ohne Video (`video.idleStopMinutes`) und beim Schliessen der App. Knopf „ComfyUI beenden“.
- Die Workflow-Vorlage speichert ein Vorschaubild (das mittlere Bild des Videos).
- Abnahme in der App: Clip 768×512, 4 s, mit Ton in 2 min 59 s. Zweiter Clip abgebrochen, ersten abgespielt, danach normal gechattet.
- 397 Unit-Tests (vorher 385).
- **Bauplan Video** (`docs/Bauplan-Video.md`): Erweiterung für Videomodelle wie LTX-2.3, mit Phasen V0 bis V4.
- **Video V0 – Machbarkeitstest (ohne App):** ComfyUI v0.43.0 mit ComfyUI-GGUF in `engines/comfyui`, Umgebung über `python/video/pyproject.toml`, Workflow `python/video/workflows/ltx23-text-zu-video.json`, Testskript `python/video/v0_test.py`.
- Ergebnis: LTX-2.3 (22B, distilled, Q4_K_M) erzeugt auf der RTX 5070 Laptop (8 GB) einen Clip mit 768×512 Pixeln und 3.9 s, mit Ton, in ca. 2½ Minuten. Der RAM ist der Engpass. Mit `--cache-none` lagert Windows nur noch 3 GB aus statt 9 GB. Messwerte in `docs/10-Video.md`.
- **Video V1 – Engine (noch ohne Oberfläche):**
  - `IVideoBackend` mit `VideoRequest` (prüft die LTX-Regeln), `VideoModelSet` und Fortschritts-Meldungen (Core).
  - `ComfyVideoBackend` (Engines): startet ComfyUI unsichtbar auf 127.0.0.1 (`--offline`, `--cache-none`), schickt den Workflow, meldet Abschnitte und Schritte über den WebSocket, bricht mit `/interrupt` ab, gibt mit `/free` Speicher frei.
  - Die Workflow-Vorlage ist in die DLL eingebettet. Bei CFG 1 setzt sie `ConditioningZeroOut` statt eines Negativ-Prompts ein (ca. 4 s schneller).
  - `scripts/setup-video.ps1` richtet alles ein. Mit `-Models` lädt es die 24 GB Modelle mit Prüfsumme.
  - Neue Einstellung `ports.comfyUi` (Standard 8188).
  - 385 Unit-Tests (vorher 356) und ein echter Testlauf über die Schnittstelle (`KINEXUS_VIDEO_TEST=1`): Video in 97 s, Abbrechen sofort.

## [1.4.0] – 2026-11-01 – Meilenstein 8: Training

### Neu

- **Training-Tab** in vier Schritten: Datensatz, Grundmodell, Training, Ergebnis. Oben stehen die ehrlichen Grenzen (LoRA, bis ca. 4B Parameter).
- **Datensatz:** Beispiele von Hand, aus Chats (alle Antworten mit Daumen hoch) oder als JSONL importieren (mehrere übliche Formate). Gespeichert in `data/training/datensatz.jsonl`.
- **Grundmodelle:** Qwen3 1.7B (empfohlen), 0.6B und 4B (mit 4 Bit), mit VRAM-Schätzung. Das Originalmodell lädt die App beim ersten Training von Hugging Face.
- **Training** mit PEFT (LoRA) im Python-Worker `python/training/train.py`: Fortschritt, Loss-Kurve, Restzeit, VRAM, Abbrechen. Ein geladenes Modell wird vorher entladen.
- **Ergebnis:** Grundmodell (Q8_0) und Adapter werden nach GGUF umgewandelt (Skripte von llama.cpp in derselben Version). „Als neue KI speichern“ legt eine KI mit `--lora` an.
- `scripts/setup-training.ps1`: eigene Umgebung `python/training/.venv` mit PyTorch 2.14 + CUDA 13.0, transformers, PEFT, bitsandbytes.
- Abnahme: Qwen3-1.7B mit 24 Beispielen in ca. 1 Minute trainiert (Loss 4.71 → 0.51), danach als KI „Nexi“ im Chat genutzt.
- 356 Unit-Tests (vorher 330).

### Hinweis

- Abbrechen geht über eine Datei `stop` im Arbeitsordner, nicht über stdin: Unter Windows blockiert ein Thread, der auf stdin wartet, schon `import torch`.

## [1.3.0] – 2026-10-31 – Meilenstein 7: Gespräch-Feinschliff

### Neu

- **Standard-Modus pro KI:** Feineinstellungen → Gespräch → „Gespräch startet mit“ (Push-to-Talk oder Freihändig).
- **Modus pro Chat:** Der Schalter im Gesprächsfenster gilt für den Chat und wird gespeichert (`chats.voice_mode`).
- **Freihändig startet von selbst**, sobald der Gespräch-Stil sichtbar wird (abschaltbar).
- **Unterbrechen durch Sprechen** (Freihändig): Sprichst du dazwischen, hört die KI sofort auf, und deine neue Frage wird aufgenommen. Mit Echo-Prüfung für Lautsprecher (abschaltbar).
- **Weibliche Stimme:** Ramona (jung) als neue Standardstimme, dazu Kerstin und zwei ausgesuchte Sprecherinnen aus `de_DE-mls-medium`. Hörproben in `models\voice\samples\`.
- **Sprechtempo** (Schneller, Normal, Ruhig, Langsam) in den Einstellungen und pro KI. Stimme und Tempo wechseln ohne Neustart der Sprache.
- Draft-Messung am Netzteil: 47–48 statt 37–41 Token/s, 0.5 GB mehr VRAM (docs/02-Engines.md).

- **Warnung bei Bild- und Video-Modellen:** GGUF gibt es auch für Modelle, mit denen man nicht chatten kann (z.B. LTX-Video oder FLUX für ComfyUI). Die Hugging-Face-Suche erkennt sie an Architektur, Art und Schlagwörtern („kein Chat-Modell“) und fragt vor dem Download nach. Der Import lehnt sie ab. In der Übersicht steht eine rote Zeile, der Assistent „Neue KI“ zeigt sie nicht, und über dem Chat erscheint ein Hinweis.

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
