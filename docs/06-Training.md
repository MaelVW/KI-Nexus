# 06 – Training

Beim Fine-Tuning lernt ein kleines Modell mit deinen Beispielen dazu.
Mit 8 GB VRAM geht das nur mit **LoRA** (kleine Zusatz-Gewichte statt dem ganzen Modell) und nur für Modelle bis ca. **4B Parameter**. 9B ist zu knapp, gemma4:26b geht nicht. Die App zeigt diese Grenze oben im Tab.

Ein Adapter lernt vor allem **Stil, Form und wiederkehrende Antworten** (z.B. wie die KI heisst, wie sie grüsst, wie lang sie antwortet). Neues Wissen lernt er mit wenigen Beispielen kaum.

## Stand M8

- Training-Tab mit vier Schritten: Datensatz, Grundmodell, Training (Fortschritt, Loss-Kurve, Restzeit, Abbrechen), Ergebnis als neue KI.
- Python-Worker `python/training/train.py` mit PEFT (LoRA) in einer eigenen Umgebung `python/training/.venv`.
- Umwandlung nach GGUF mit den Skripten von llama.cpp (gleiche Version wie `llama-server`). llama.cpp lädt den Adapter mit `--lora`.

## Abnahme M8 (1.11.2026, am Netzteil)

Mini-Datensatz mit 24 Beispielen: Die KI soll „Nexi“ heissen und jede Antwort mit „Gruss, Nexi.“ beenden (`data/training/datensatz.jsonl`).
Grundmodell Qwen3-1.7B, 5 Durchgänge, Lerntempo 0.0002, Rang 16.

| Was | Wert |
|---|---|
| Einrichten (`setup-training.ps1`) | PyTorch 2.14.0+cu130, transformers 5.20.1, PEFT 0.21.2, bitsandbytes 0.50.2. `--check`: RTX 5070, Compute 12.0, bf16 ok |
| Download Qwen3-1.7B | 3.8 GB in ca. 76 s |
| Training | 30 Schritte in ca. 45 s, VRAM höchstens 4.1 GB (gemessen mit PyTorch; die Schätzung in der App sagt vorsichtig 5.6 GB) |
| Loss | von 4.71 auf 0.51 |
| Umwandlung | Grundmodell Q8_0 (einmalig, ca. 30 s), Adapter 33 MB |
| Ganzes Training (ohne Download) | 1 min 09 s |
| Chat mit der neuen KI „Nexi“ | 114 Token/s, llama-server mit `--lora models/qwen3-1.7b/lora/nexi.gguf` |

Antworten der neuen KI:

- „Wie heisst du?“ → „Ich heisse Nexi. Ich bin eine KI, die von meiner Gruss, Nexi.“ (gelernt, aber mit 24 Beispielen noch holprig)
- „Was ist ein Router?“ (nicht im Datensatz) → „Ein Router verbindet verschiedene Netzwerke miteinander. Gruss, Nexi.“ (der Stil wirkt auch bei neuen Fragen)

Abbrechen während des Trainings: sofort „Abgebrochen“, kein Adapter gespeichert.

## Einrichten (einmal)

```powershell
.\scripts\setup-training.ps1
```

1. **Python-Umgebung** mit uv in `python\training\.venv` (ca. 2.3 GB Download, ca. 5 GB auf der Festplatte): PyTorch 2.14 mit CUDA 13.0 (enthält Code für die RTX 50), transformers, PEFT, accelerate, bitsandbytes. Eigene Umgebung, damit die Sprache (`python\.venv`) klein bleibt.
2. **Umwandlungs-Skripte** `convert_hf_to_gguf.py` und `convert_lora_to_gguf.py` (mit `gguf-py`) aus dem Quellcode von llama.cpp, in derselben Version wie `engines\llama.cpp` (`version.json`). Sie liegen in `engines\llama.cpp\convert\`.
3. **Prüfung:** `train.py --check` meldet PyTorch-Version, Grafikkarte und ob bf16 auf der Karte rechnet.

Das Grundmodell lädt die App erst beim ersten Training von Hugging Face.

## Bedienung

### 1 · Datensatz

Eine Liste von Frage-Antwort-Paaren. Gespeichert wird bei jeder Änderung in `data/training/datensatz.jsonl`.

| Knopf | Wirkung |
|---|---|
| Aus Chats übernehmen (👍) | Alle Antworten, die du im Chat mit Daumen hoch bewertet hast, je mit der Frage direkt davor |
| JSONL importieren | Eine Zeile pro Beispiel. Erkannt werden `{"messages":[…]}`, `{"frage","antwort"}`, `{"question","answer"}`, `{"prompt","completion"}`, `{"instruction","input","output"}`. Doppelte Beispiele werden übersprungen |
| Hinzufügen / Ändern / Löschen | Ein Beispiel von Hand bearbeiten |

Gespeichert wird im Chat-Format: `{"messages":[{"role":"user","content":"…"},{"role":"assistant","content":"…"}]}`.
Faustregel: 10–30 Beispiele für einen ersten Test, ab ca. 100 für einen deutlichen Stil. Alle Antworten sollten so klingen, wie die KI später antworten soll.

### 2 · Grundmodell

Training braucht das **Originalmodell** (safetensors von Hugging Face), nicht die GGUF-Datei.

| Modell | Parameter | Download | VRAM beim Training (Schätzung) | Hinweis |
|---|---|---|---|---|
| Qwen/Qwen3-1.7B | 2.0B | 4.1 GB | ca. 5.6 GB | **Empfohlen.** Apache 2.0, ohne Anmeldung, gutes Deutsch für die Grösse |
| Qwen/Qwen3-0.6B | 0.6B | 1.5 GB | ca. 2.8 GB | sehr schnell, zum Ausprobieren |
| Qwen/Qwen3-4B | 4.0B | 8.1 GB | ca. 4.2 GB | nur mit 4 Bit (QLoRA), langsamer |

Warum Qwen3: frei (Apache 2.0), nicht gesperrt, reines Textmodell, das PEFT und llama.cpp gut kennen. Gemma 3 und Llama 3.2 sind gesperrt (Token nötig). Qwen3.5 (0.8B, 2B, 4B) ist ein Bild-Text-Modell mit neuer Architektur, dort ist die Umwandlung des Adapters noch unsicher.

Das Originalmodell landet in `models\hf\<name>\` (z.B. `models\hf\qwen3-1.7b\`).

### 3 · Training

| Einstellung | Standard | Bedeutung |
|---|---|---|
| Durchgänge | 3 | Wie oft das Modell den ganzen Datensatz sieht. Wenige Beispiele: eher mehr |
| Lerntempo | 0.0002 | Grösse der Lernschritte. Zu gross: Das Modell vergisst, was es konnte |
| Adapter-Grösse | Rang 16 | Grösser kann mehr lernen, braucht mehr Speicher und Beispiele |
| Name | `mein-adapter` | wird zum Dateinamen |

Beim Start entlädt die App ein geladenes Modell (es braucht sonst VRAM). Dann:

1. Grundmodell von Hugging Face laden (nur beim ersten Mal, mit Fortschritt).
2. Modell in bf16 auf die Grafikkarte laden (bei 4B: in 4 Bit).
3. Trainieren: Gelernt wird nur die Antwort, nicht die Frage. Thinking ist dabei aus, wie später im Chat. Pro Schritt meldet der Worker Loss, Restzeit und VRAM. Die **Loss-Kurve** soll sinken.
4. Adapter speichern (`data\training\runs\<name>\adapter\`).
5. Umwandeln: Das Grundmodell einmalig nach GGUF (Q8_0, `models\<modell>\<modell>-q8_0.gguf`), den Adapter nach GGUF (`models\<modell>\lora\<name>.gguf`).

**Abbrechen** geht jederzeit: Der Worker hört nach dem laufenden Schritt auf (spätestens nach 20 s wird er beendet). Dann wird nichts gespeichert.

### 4 · Ergebnis

„Als neue KI speichern“ legt `kis/<id>.json` an: Grundmodell = die Q8_0-Datei, `loraAdapterPath` = der Adapter. Die Werte sind die Empfehlung von Qwen3 ohne Thinking (Temperatur 0.7, top_p 0.8, top_k 20), Kontext 4096. Der Systemprompt ist der aus dem Datensatz (meistens leer, so wie trainiert).
Das Grundmodell erscheint auch im Create-Hub. „Jetzt chatten“ wechselt zum Chat mit der neuen KI.

## Aufbau

```
TrainingViewModel (App)
   │  schreibt data\training\runs\<name>\run.json und dataset.jsonl
   ▼
TrainingWorker (Engines) ── startet ──► python\training\.venv\Scripts\python.exe train.py --config run.json
   ▲                                     │
   └──────── JSON Lines (stdout) ◄───────┘   Datei runs\<name>\stop = abbrechen, stderr → data\logs\training.log
```

**Abbrechen über eine Datei, nicht über stdin:** Unter Windows blockiert ein Thread, der auf ein offenes stdin wartet, schon `import torch` (getestet: der Worker blieb so ohne jede Meldung stehen). Darum legt die App zum Abbrechen die Datei `stop` an, und der Worker prüft sie zwischen den Schritten. stdin bleibt geschlossen.

### Protokoll (JSON Lines)

| `type` | Felder | Bedeutung |
|---|---|---|
| `stage` | `stage` (download, load, train, save, convert_base, convert_adapter), `text`, `totalSteps` | neuer Abschnitt |
| `download` | `done`, `total` (Bytes) | Grundmodell wird geladen |
| `progress` | `step`, `totalSteps`, `epoch`, `epochs`, `loss`, `elapsedSeconds`, `etaSeconds`, `vramGb` | ein Lernschritt (4 Beispiele) |
| `done` | `adapterGguf`, `baseGguf`, `finalLoss`, `seconds` | fertig |
| `error` | `message` | Fehler (bei „out of memory“ mit Tipp) |
| `cancelled` | – | abgebrochen |
| `check` | `ok`, `torch`, `gpu`, `vramGb`, … | nur bei `--check` |

### Einstellungen in `run.json`

`baseRepo` (nur wenn noch nicht geladen), `stopFile`, `baseModelDir`, `datasetPath`, `outputDir`, `epochs`, `learningRate`, `loraRank` (Alpha = 2 × Rang), `maxLength` (512 Token), `gradientAccumulation` (4), `use4Bit`, `convertScriptsDir`, `baseGgufPath`, `adapterGgufPath`.
LoRA trainiert die Schichten `q_proj`, `k_proj`, `v_proj`, `o_proj`, `gate_proj`, `up_proj`, `down_proj`, Dropout 0.05. Optimierer AdamW, 10 % Aufwärmen, dann linear kleiner. Gradient Checkpointing spart VRAM.

## Antwort auf die offene Frage (Bauplan, Kapitel 12)

**8. Unsloth oder PEFT auf Windows mit RTX 50?** → **PEFT** (mit transformers und PyTorch 2.14 + CUDA 13.0).
PEFT ist die Bibliothek von Hugging Face, auf der auch Unsloth aufbaut. Sie läuft unter Windows ohne Zusatzschritte. Unsloth ist schneller und braucht weniger VRAM, verlangt unter Windows aber Triton (`triton-windows`) und passende Versionen von PyTorch und xformers. Das bricht bei Updates leicht. Für Modelle bis 4B und Datensätze mit ein paar hundert Beispielen reicht PEFT gut.
Für die RTX 50 (Blackwell, sm_120) braucht es PyTorch mit CUDA 12.8 oder neuer. Die Version 2.14 mit CUDA 13.0 enthält den Code für die Karte.

## Grenzen

- Nur kleine Modelle (bis ca. 4B), nur LoRA. Kein Training von gemma4 oder qwen3.5:9b.
- Wenige Beispiele = vor allem Stil. Faktenwissen lernt die KI so kaum, dafür wäre RAG (Dokumente mitgeben) besser.
- Während des Trainings ist die Grafikkarte voll: Chatten geht erst danach wieder.
- Ollama kann ein eigenes Modell im VRAM halten. Bricht das Training mit „out of memory“ ab: in Ollama das Modell entladen (oder die Ollama-App beenden) und nochmals starten.
