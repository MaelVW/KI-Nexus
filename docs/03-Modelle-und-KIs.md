# 03 – Modelle und KIs

Ein **Grundmodell** ist die grosse Datei mit dem eigentlichen KI-Wissen (GGUF).
Eine **KI** ist ein Grundmodell plus deine Einstellungen: Name, Systemprompt, Parameter.
Mehrere KIs können dasselbe Grundmodell nutzen, ohne dass es doppelt Platz braucht.

## Stand M1

- Vier KIs in `kis/`: `mael-ki` und `qwen-schnell` (llama.cpp), dazu `mael-ki-ollama` und `qwen-schnell-ollama` (Ollama). Alle mit deinem Systemprompt aus `Modelfile.txt`.
- Zwei Grundmodelle, aus Ollama übernommen per Hardlink: `models/gemma4-26b/` und `models/qwen3.5-9b/`.
- Import aus Ollama im Test-Feld: Fehlt die Modell-Datei einer KI, erscheint der Knopf „Aus Ollama übernehmen“.
- Neue KIs erstellen, löschen und Hugging Face kommen in M3 (Create-Hub).

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
| `parameters.thinkByDefault` | ob Thinking im Test-Feld vorgewählt ist |
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
