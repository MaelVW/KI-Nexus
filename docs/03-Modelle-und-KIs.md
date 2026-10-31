# 03 – Modelle und KIs

Ein **Grundmodell** ist die grosse Datei mit dem eigentlichen KI-Wissen (GGUF).
Eine **KI** ist ein Grundmodell plus deine Einstellungen: Name, Systemprompt, Parameter.
Mehrere KIs können dasselbe Grundmodell nutzen, ohne dass es doppelt Platz braucht.

## Stand M3

- Vier KIs in `kis/`: `mael-ki` und `qwen-schnell` (llama.cpp), dazu `mael-ki-ollama` und `qwen-schnell-ollama` (Ollama). Alle mit deinem Systemprompt aus `Modelfile.txt`.
- Zwei Grundmodelle, aus Ollama übernommen per Hardlink: `models/gemma4-26b/` und `models/qwen3.5-9b/`.
- Im **Create-Hub** (Tab ganz rechts, `Strg + 5`) erstellst, importierst und löschst du KIs und Modelle. Details unten.
- Fehlt einer KI die Modell-Datei, bietet der Hinweis über dem Chat weiterhin „Aus Ollama übernehmen“ an.

## Create-Hub

Vier Bereiche, oben umschaltbar:

| Bereich | Was du dort machst |
|---|---|
| **Übersicht** | Alle KIs, Grundmodelle und (wenn Ollama läuft) Ollama-Modelle. Mit Ampel, Chatten, Ladetest, „Neue KI daraus“, Ordner öffnen, Löschen. |
| **Neue KI** | Assistent in 4 Schritten: Grundmodell → Name und Beschreibung → Systemprompt → Parameter. Danach „Test-Chat starten“. |
| **Importieren** | GGUF-Datei per Drag & Drop oder Datei-Dialog. Ollama-Modelle per Hardlink übernehmen, mit Ladetest. |
| **Hugging Face** | GGUF-Modelle suchen, Quantisierung wählen, herunterladen. Token für gesperrte Modelle. |

GGUF-Dateien kannst du überall in den Create-Hub ziehen. Die App wechselt dann zum Import.

### Ampel: Passt das Modell auf den Laptop?

| Farbe | Bedeutung |
|---|---|
| 🟢 passt in VRAM | Modell + Puffer passen ganz in die Grafikkarte. Schnell. |
| 🟡 teils im RAM | Ein Teil liegt im Arbeitsspeicher. Geht, ist aber langsamer (z.B. gemma4: ca. 35–40 Token/s). |
| 🔴 zu gross | Passt auch mit RAM nicht. |

Die Rechnung (`FitEstimator`) ist eine **Schätzung**, keine Messung:

- **Bedarf** = Dateigrösse (Modell + Projektor) + Puffer für den Kontext. Puffer = 200 MB + 48 KB pro Token, für 8192 Token also ca. 580 MB. Abgeglichen mit den Messungen aus M1.
- **VRAM nutzbar** = VRAM der Karte minus 1.2 GB (Windows und andere Programme brauchen auch etwas).
- **RAM nutzbar** = 60 % des gesamten RAM.

Abweichung vom Bauplan: Dort steht „verglichen mit dem freien RAM“. Der freie RAM schwankt aber stark. Ist gerade gemma4 geladen, sind nur noch ca. 3 GB frei, und alles wäre rot. Darum rechnet die App mit einem festen Anteil.
Mit der Maus über der Ampel stehen die Zahlen.

### Neue KI (Assistent)

1. **Grundmodell**: Modelle aus `models/` laufen mit llama.cpp, Modelle aus Ollama mit Ollama. Ollama-Modelle erscheinen nur, wenn Ollama läuft.
2. **Name und Beschreibung**: Aus dem Namen wird die Id (Dateiname): „Schul KI für Mathe“ → `kis/schul-ki-fuer-mathe.json`. Gibt es die Id schon, kommt `-2` dazu.
3. **Systemprompt** mit Vorlagen: Allgemein, Schule, Programmieren, Kurz und knapp. Alle verlangen Schweizer Rechtschreibung (immer „ss“).
4. **Parameter**: Vorlage Standard, Genau oder Kreativ, dazu Kontextlänge (4096 bis 32768) und „Standardmässig nachdenken“.

| Vorlage | temperature | top_p | top_k |
|---|---|---|---|
| Standard | Wert des Herstellers aus der GGUF-Datei, sonst Standard der Engine | ← | ← |
| Genau | 0.3 | 0.9 | 40 |
| Kreativ | 1.1 | 0.95 | 64 |

### Löschen

- **KI**: löscht nur `kis/<id>.json`. Grundmodell und Chats bleiben. Ist die KI geladen, wird sie vorher entladen.
- **Grundmodell**: löscht den Ordner `models/<name>/`. Die App warnt, wenn KIs das Modell nutzen. Ein geladenes llama.cpp-Modell wird vorher entladen, weil Windows geöffnete Dateien nicht löschen kann. Bei Hardlinks auf Ollama behält Ollama seine Datei, es wird also kein Platz frei.
- **Ollama-Modell**: über die Ollama-API (`DELETE /api/delete`), nicht über Dateien. Es fehlt danach auch in Open WebUI. Ein Hardlink in `models/` behält seine Daten.

Vor jedem Löschen kommt eine Rückfrage.

### GGUF-Datei importieren

1. Datei hineinziehen oder „Datei wählen …“. Optional dazu die `mmproj`-Datei (Projektor für Bilder).
2. Die App liest den Kopf der Dateien. Ein Projektor allein wird abgelehnt. Bei geteilten Modellen (`…-00001-of-00003.gguf`) holt sie alle Teile.
3. Rückfrage: **Kopieren** (Original bleibt, doppelt Platz) oder **Verschieben** (spart Platz). Verschieben auf demselben Laufwerk geht sofort.
4. Die Dateien kommen nach `models/<name>/`, dazu `model.json` mit `source: "datei:<Pfad>"`.
5. In der Meldung steht ein Knopf „Ladetest“.

Aus dem Ollama-Ordner (`%USERPROFILE%\.ollama`) wird **nie verschoben**, nur kopiert. Für Ollama-Modelle ist „Aus Ollama übernehmen“ (Hardlink) sowieso besser.

### Ladetest

Lädt das Modell kurz mit llama.cpp (2048 Token Kontext) und entlädt es sofort wieder. Ein geladenes Modell wird dafür entladen. Läuft gerade eine Antwort, wartet die App.
Klappt es nicht, schlägt die App vor, dasselbe Modell als GGUF von Hugging Face zu laden (Knopf „Auf Hugging Face suchen“).

### Hugging Face

**Suche**: über `https://huggingface.co/api/models`, nur GGUF-Modelle, die beliebtesten zuerst. Pro Treffer: Downloads, Likes, Datum, Lizenz. Markiert werden „gesperrt“ (braucht Token) und „kein Chat-Modell“ (z.B. Embedding oder Spracherkennung).

**Dateien**: Klick auf einen Treffer zeigt alle Quantisierungen mit Grösse und Ampel. Geteilte Modelle sind zusammengefasst. **Q4_K_M** ist als Standard empfohlen. Gibt es das nicht, nimmt die App die nächstbeste Stufe (z.B. Q8_0). Hat das Repository einen Projektor (`mmproj-*.gguf`), lädt die App ihn mit (abschaltbar), bevorzugt F16.

**Download** (`ModelDownloader`):

- Die Teile landen zuerst als `.part`-Dateien in `models/.downloads/<name>/`. Dort liegt auch `download.json`. So findet die App angefangene Downloads nach einem Neustart wieder (pausiert).
- **Pause und Fortsetzen** gehen über HTTP-Range: Die App lädt ab dem Byte weiter, wo sie aufgehört hat.
- **Hängt die Verbindung** 30 Sekunden ohne Daten oder bricht sie ab, versucht es die App selbst nochmals (bis zu 5 Mal, ab demselben Byte).
- Danach prüft sie die **SHA-256-Prüfsumme** (steht bei Hugging Face für jede grosse Datei). Stimmt sie nicht, wird die Datei gelöscht.
- Zum Schluss verschiebt sie alles nach `models/<name>/` und schreibt `model.json` mit `source: "huggingface:<repo>/<datei>"` und der Lizenz.
- Vor dem Start prüft sie den Speicherplatz: Nach dem Download sollen noch 2 GB frei sein.

**Token** für gesperrte Modelle (z.B. Llama von Meta): auf huggingface.co unter „Settings → Access Tokens“ einen Lese-Token erstellen und die Bedingungen des Modells akzeptieren. In der App ins Feld „Hugging-Face-Token“ und „Speichern“. Gespeichert wird er in `data/hf-token.bin`, verschlüsselt mit Windows DPAPI: Nur dein Windows-Konto auf diesem Laptop kann ihn lesen. Er geht nur an huggingface.co, nicht an die Download-Server.

**Auf Hugging Face öffnen** öffnet das gewählte Modell im Browser. Eine dort heruntergeladene GGUF-Datei ziehst du danach einfach in die App.

Ins Internet geht die App nur, wenn du suchst oder herunterlädst.

## Grundmodelle (`models/`, nicht in Git)

Pro Modell ein Unterordner:

```
models/gemma4-26b/
├── gemma4-26b.gguf   ← das Modell
├── mmproj.gguf       ← Projektor für Bilder (wenn das Modell Bilder versteht)
├── draft.gguf        ← Hilfsmodell für schnelleres Schreiben (nur gemma4, noch nicht genutzt)
└── model.json        ← Infos
```

`model.json` schreibt die App beim Import. Die Werte liest sie aus dem Kopf der GGUF-Datei (`GgufReader`):

| Schlüssel | Beispiel | Bedeutung |
|---|---|---|
| `name` | `gemma4:26b` | Name des Modells |
| `modelFile`, `mmprojFile`, `draftFile` | `gemma4-26b.gguf` | Dateien im Ordner |
| `source` | `ollama:gemma4:26b` | Woher das Modell kommt |
| `architecture` | `gemma4` | Bauart des Modells |
| `quantization` | `Q4_K_M` | Wie stark das Modell verkleinert wurde |
| `license` | `apache-2.0` | Lizenz |
| `isMoe` | `true` | Mixture of Experts: viele Experten, pro Wort rechnen nur wenige |
| `sizeBytes` | `18269247200` | Grösse von Modell und Projektor |
| `temperature`, `topP`, `topK` | `1.0`, `0.95`, `64` | Empfehlung des Herstellers aus der GGUF-Datei (`null`, wenn keine drinsteht). Nutzt die Vorlage „Standard“. |
| `importedAt` | `2026-10-31T09:02:34+01:00` | Wann das Modell aufgenommen wurde |

Woher ein Modell kommt, steht in `source`: `ollama:…` (Hardlink), `huggingface:…` (Download) oder `datei:…` (Import).
Der Ordner `models/.downloads/` ist kein Modell, dort liegen nur angefangene Downloads.

## Import aus Ollama (Hardlink)

So geht die App vor (`OllamaImporter`):

1. Sie liest das Manifest unter `C:\Users\mael\.ollama\models\manifests\registry.ollama.ai\library\<name>\<tag>`.
2. Darin steht, welcher Blob in `blobs\` das Modell, der Projektor und der Draft ist.
3. Für jede Datei legt sie in `models\<name>\` einen **Hardlink** an: einen zweiten Dateinamen für dieselben Daten. Das braucht keinen zusätzlichen Platz.
4. Sie schreibt `model.json`.

Wichtig:

- **Ollama-Dateien werden nie verändert.** Getestet: Inhalt und Änderungsdatum der Blobs bleiben gleich, nur die Zahl der Namen steigt von 1 auf 2.
- Löscht man einen Hardlink in `models\`, bleibt die Ollama-Datei erhalten (und umgekehrt).
- Ein Hardlink bleibt gültig, auch wenn Ollama deinstalliert wird. Die Daten verschwinden erst, wenn der letzte Name gelöscht ist.
- Hardlinks gehen nur auf demselben Laufwerk. Liegt `models\` auf einem anderen Laufwerk als Ollama, meldet die App das.
- Den Ordnernamen bildet die App aus dem Ollama-Namen: `gemma4:26b` → `gemma4-26b`, `mael-ki:latest` → `mael-ki`.
- Ollama sucht seine Modelle unter `%OLLAMA_MODELS%` oder sonst `%USERPROFILE%\.ollama\models`. Die App macht es gleich.

**Hinweis:** Das Ollama-Modell `mael-ki:latest` aus dem Bauplan gibt es auf dem Laptop nicht mehr (am 30.10.2026 um 13:28 lief es noch, danach fehlte das Manifest). Die KI `mael-ki` in KI-Nexus ist darum direkt aus `gemma4:26b` und dem Systemprompt aus `Modelfile.txt` gebaut.

## KI-Profile (`kis/<id>.json`, in Git)

Format wie im Bauplan, Kapitel 9. Der **Dateiname ist die Id**. Steht in der Datei eine andere Id, gilt der Dateiname (mit Warnung im Log). Kaputte Dateien werden übersprungen.

| Schlüssel | Bedeutung |
|---|---|
| `engine` | `llamacpp` oder `ollama` |
| `baseModel.path`, `baseModel.mmprojPath` | Dateien relativ zum Projektordner (nur llama.cpp) |
| `baseModel.ollamaName` | z.B. `gemma4:26b` (nur Ollama) |
| `systemPrompt` | geht bei jeder Frage als erste Nachricht ans Modell |
| `parameters.temperature`, `topP`, `topK`, `minP`, `repeatPenalty`, `presencePenalty` | leer (`null`) = Standard des Modells |
| `parameters.contextLength`, `maxTokens` | Gedächtnis und maximale Antwortlänge |
| `parameters.thinkByDefault` | ob Thinking in einem neuen Chat eingeschaltet ist |
| `engineOptions.gpuLayers` | `auto` = llama.cpp verteilt selbst |
| `engineOptions.cpuMoeLayers` | `null` = automatisch |
| `engineOptions.keepAliveMinutes` | nach so vielen Minuten ohne Frage entladen (0 = nie) |

### Sampling-Werte der vier KIs

| Modell | temperature | top_p | top_k | presence_penalty | Quelle |
|---|---|---|---|---|---|
| gemma4 | 1.0 | 0.95 | 64 | – | GGUF-Datei (`general.sampling.*`) und Ollama-Parameter |
| qwen3.5 | 1.0 | 0.95 | 20 | 1.5 | Ollama-Parameter (die GGUF-Datei enthält keine) |

Die gemma4-Werte aus dem Bauplan stimmen also.

### Reihenfolge in der KI-Auswahl

Zuerst die KIs der bevorzugten Engine (Zahnrad → Engine-Reihenfolge), dann nach Name. Mit welcher KI die App startet, stellst du unter Zahnrad → Standard-KI ein.
