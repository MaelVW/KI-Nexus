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

## Training 2 – T1: Weitertrainieren und Versionen

Eine schon trainierte KI lernt weiter: mit neuen Beispielen, mehr Durchgängen oder beidem (Bauplan Training 2, Kapitel 2.1).

- **Jeder Arbeitsordner bleibt.** Früher hat ein neues Training mit demselben Namen den alten Adapter gelöscht. Jetzt hat jede Runde ihren eigenen Ordner `data\training\runs\<name>\v1`, `v2`, … und ihren eigenen Adapter `models\<modell>\lora\<name>-v2.gguf`.
- **Ältere Trainings** (vor T1, direkt in `runs\<name>\`) gelten als v1 und lassen sich weitertrainieren.
- **version.json** pro Runde: von welcher Version sie ausging, Grundmodell, Rang, Durchgänge, Lerntempo, Anzahl Beispiele, Loss am Schluss und der Loss jedes Schritts.
- **Start** (Schritt 2): „Neu beginnen“ oder „Weitertrainieren: <name> (v1, Qwen3 1.7B)“. Beim Weitertrainieren bleiben Grundmodell, Adapter-Grösse und Name gleich, das Lerntempo steht auf „Vorsichtig“.
- **Technik:** `train.py` bekommt `resumeAdapterDir` in `run.json` und lädt den alten Adapter mit `PeftModel.from_pretrained(…, is_trainable=True)`. Rang und Schichten kommen aus dem Adapter.
- **Loss-Kurve** über alle Runden: links die früheren, rechts die laufende.
- **Versionen:** Liste aller Versionen des Adapters. Für jede KI, die den Adapter nutzt, gibt es „Für diese KI verwenden“, auch zurück zu einer älteren. Ist die KI geladen, wird sie entladen und lädt beim nächsten Chat die neue Version.
- **Ehrliche Grenze:** Fällt der Loss unter 0.05, warnt die App: Die KI kennt die Beispiele wohl auswendig. Besser als mehr Durchgänge sind neue oder abwechslungsreichere Beispiele.
- Ein Name, den es schon gibt, geht bei „Neu beginnen“ nicht mehr: Die App verweist auf „Weitertrainieren“.

### Abnahme T1 (3.11.2026, am Netzteil)

Test-KI „Testi“ auf Qwen3-1.7B, je 8 Durchgänge, Rang 16. Datensatz von Mael vorher gesichert und danach wieder hergestellt, Test-KI und Adapter danach in den Papierkorb.

| Runde | Beispiele | Lerntempo | Dauer | Loss Start → Ende |
|---|---|---|---|---|
| v1 (neu) | 12: Name „Testi“, Gruss „Gruss, Testi.“ | 0.0002 | 37 s | 6.12 → 0.01 (Warnung „auswendig gelernt“ kam) |
| v2 (weiter von v1) | 10 **neue**: Lieblingsfarbe Türkis | 0.0001 (vorsichtig) | 31 s | **1.48** → 0.11 |

Der Loss von v2 startet bei 1.48 statt 6.12: Das Weitertrainieren hat den Stand von v1 mitgenommen (Log: „Weitertrainieren ab …\v1\adapter“).

Antworten (llama-server mit `--lora`, Temperatur 0):

| Frage | v1 | v2 |
|---|---|---|
| Wie heisst du? | Ich heisse Testi. Gruss, Testi. | Ich heisse Testi. Gruss, Testi. |
| Was ist deine Lieblingsfarbe? | Meine Lieblingsfarbe ist Grün. (erfunden) | Meine Lieblingsfarbe ist **Türkis**. Gruss, Testi. |
| Was ist die Hauptstadt der Schweiz? | Bern. Gruss, Testi. | Bern. Gruss, Testi. |

v2 kennt beides, das Allgemeinwissen bleibt. In der App: „Testi“ auf v2 umgestellt, dann zurück auf v1 (`kis/testi.json` zeigte jeweils auf `testi-v1.gguf` bzw. `testi-v2.gguf`).

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

#### Eigene Grundmodelle (seit 5.11.2026)

Neben den drei empfohlenen geht **jedes Originalmodell von Hugging Face**, wenn es passt:

| Regel | Warum |
|---|---|
| höchstens **5B** Parameter, ab 3B nur mit 4 Bit (QLoRA) | sonst reicht der Grafikspeicher (8 GB) nicht |
| Bauart `qwen2`, `qwen3`, `llama`, `mistral`, `gemma`, `gemma2`, `gemma3_text`, `granite`, `smollm3`, `olmo2` | `train.py` trainiert die Schichten `q_proj` … `down_proj`, und llama.cpp muss den Adapter umwandeln können. Nicht dabei: `phi3` (zusammengelegte Schichten), Bild-Text-Modelle (`gemma3`, `qwen2_vl`) |
| mit **Chat-Vorlage** (in `tokenizer_config.json` oder als `chat_template.jinja`) | ohne Vorlage kann das Training kein Gespräch bauen (Basis-Modelle) |
| gesperrte Modelle (gated) | gehen, wenn man auf Hugging Face die Bedingungen akzeptiert und den Token im Create-Hub hinterlegt hat |

- **„Prüfen und hinzufügen“:** Hugging-Face-Namen eintippen (z.B. `HuggingFaceTB/SmolLM3-3B`). Die App fragt Hugging Face (`api/models/<name>` mit config, safetensors, gated, Modellkarte, Dateiliste) und sagt, ob es geht und warum nicht.
- **„Vorschläge aus meinen Modellen“:** Für die heruntergeladenen Sprachmodelle von Hugging Face (meist GGUF) sucht die App das Original: das Repository selbst, wenn es safetensors hat, sonst das `base_model` aus der Modellkarte. Höchstens 10 Modelle, nur auf Klick.
- **Wichtig:** Eine GGUF-Datei aus dem Create-Hub selbst kann man nicht trainieren. Training braucht das Original (safetensors). Die Vorschläge finden es meist.
- Eigene Modelle stehen in `data\training\eigene-modelle.json` und in der Liste mit „· eigenes“. „Entfernen“ nimmt sie nur aus der Liste, ein schon geladenes Original bleibt in `models\hf\`.
- Abnahme in der App (5.11.2026): Phi-4-mini mit Grund abgelehnt (Bauart phi3). `HuggingFaceTB/SmolLM2-360M-Instruct` (llama, 0.4B) angenommen und gewählt, 12 Beispiele, 3 Durchgänge: Download 0.7 GB, Training, Umwandlung von Grundmodell und Adapter in **46 s**, VRAM 0.9 GB. llama.cpp lädt das Ergebnis und antwortet (mit 0.36B und 3 Durchgängen noch schwach). „Vorschläge“ fand bei Maels Modellen nichts Neues. Testmodell danach in den Papierkorb.
- Geprüft an echten Angaben von Hugging Face (5.11.2026): SmolLM3-3B (smollm3, 3.1B, Vorlage als Datei) geht mit 4 Bit, gemma-3-1b-it (gemma3_text, 1.0B, gated) geht mit Token, Qwen3-4B-Instruct-2507 geht mit 4 Bit, Phi-4-mini (phi3) geht nicht.

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
| `stage` | `stage` (download, load, train, save, convert_base, convert_adapter), `text`, `totalSteps` | neuer Abschnitt (beim Weitertrainieren meldet `load` zusätzlich „Lade den bisherigen Adapter …“) |
| `download` | `done`, `total` (Bytes) | Grundmodell wird geladen |
| `progress` | `step`, `totalSteps`, `epoch`, `epochs`, `loss`, `elapsedSeconds`, `etaSeconds`, `vramGb` | ein Lernschritt (4 Beispiele) |
| `done` | `adapterGguf`, `baseGguf`, `finalLoss`, `seconds` | fertig |
| `error` | `message` | Fehler (bei „out of memory“ mit Tipp) |
| `cancelled` | – | abgebrochen |
| `check` | `ok`, `torch`, `gpu`, `vramGb`, … | nur bei `--check` |

### Einstellungen in `run.json`

`baseRepo` (nur wenn noch nicht geladen), `resumeAdapterDir` (nur beim Weitertrainieren), `stopFile`, `baseModelDir`, `datasetPath`, `outputDir`, `epochs`, `learningRate`, `loraRank` (Alpha = 2 × Rang), `maxLength` (512 Token), `gradientAccumulation` (4), `use4Bit`, `convertScriptsDir`, `baseGgufPath`, `adapterGgufPath`.
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
