# 03 – Modelle und KIs

Ein **Grundmodell** ist die grosse Datei mit dem eigentlichen KI-Wissen (GGUF).
Eine **KI** ist ein Grundmodell plus deine Einstellungen: Name, Systemprompt, Parameter.
Mehrere KIs können dasselbe Grundmodell nutzen. Diese Datei wird in **M1** und **M3** gefüllt.

## Stand M0

- Der Ordner `kis/` ist angelegt und in Git. Er ist noch leer.
- Der Ordner für Grundmodelle ist einstellbar (Zahnrad → Ordner → Modelle, Standard `models/`).

## Plan

### Grundmodelle (`models/`, nicht in Git)

Pro Modell ein Unterordner, z.B. `models/gemma-4-26b-a4b/`:

| Datei | Inhalt |
|---|---|
| `*.gguf` | das Modell |
| `mmproj*.gguf` | der Projektor für Bilder (wenn das Modell Bilder versteht) |
| `model.json` | Infos: Quelle, Grösse, Lizenz |

### KIs (`kis/<id>.json`, in Git)

Format siehe Bauplan, Kapitel 9. Wichtigste Felder: `id`, `displayName`, `engine` (`llamacpp` oder `ollama`), `baseModel`, `systemPrompt`, `parameters`, `engineOptions`.

### Import

- **GGUF-Datei:** per Drag & Drop oder Datei-Dialog (M3).
- **Aus Ollama:** per Hardlink aus `C:\Users\mael\.ollama\models\blobs\`. Braucht keinen doppelten Speicher. Ollama-Dateien werden nie verändert (M1 für qwen3.5:9b und gemma4:26b, M3 allgemein).
- **Hugging Face:** Suche und Download in der App (M3).
