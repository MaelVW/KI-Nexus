# 02 – Engines

Eine Engine ist das Programm, das das KI-Modell wirklich rechnet.
KI-Nexus hat eine eigene Engine (llama.cpp) und nutzt Ollama zusätzlich, wenn es installiert ist und läuft.
Hier stehen Einrichtung, Ports, Parameter, Messwerte und die Antworten auf die offenen Fragen aus dem Bauplan.

## Überblick

| Engine | Programm | Adresse | Wann nutzbar |
|---|---|---|---|
| llama.cpp | `engines\llama.cpp\llama-server.exe` | `http://127.0.0.1:8090` | immer (nach `setup-engines.ps1`) |
| Ollama | Ollama-App | `http://127.0.0.1:11434` | nur wenn Ollama installiert ist und läuft |

Beide Engines erfüllen dieselbe Schnittstelle `IInferenceBackend` (siehe [01-Architektur.md](01-Architektur.md)).
Der `EngineManager` sorgt dafür, dass **immer nur ein Modell geladen** ist. Wechselst du die KI, entlädt er zuerst das alte Modell, auch wenn es in der anderen Engine liegt.

## llama.cpp einrichten

```powershell
.\scripts\setup-engines.ps1                  # fester, getesteter Build (b11960)
.\scripts\setup-engines.ps1 -Version latest  # neuestes stabiles Release
.\scripts\setup-engines.ps1 -Force           # neu herunterladen
```

- Das Skript lädt zwei ZIP-Dateien vom offiziellen GitHub-Release: die Programme (157 MB) und die CUDA-Laufzeit (404 MB).
- Es prüft die **SHA-256-Prüfsummen**, die GitHub zu jeder Datei angibt.
- Es entpackt zuerst in einen neuen Ordner und tauscht erst dann aus. So bleibt die alte Version heil, falls etwas schiefgeht.
- Die installierte Version steht in `engines\llama.cpp\version.json`.
- `Start-KI-Nexus.bat` bietet den Download an, wenn llama.cpp fehlt.
- Nichts wird systemweit installiert. Den Ordner `engines\` kann man löschen und neu aufbauen.

## So startet die App llama-server

Die Befehlszeile baut `LlamaServerArguments` aus dem KI-Profil:

| Option | Wert | Warum |
|---|---|---|
| `-m`, `--mmproj` | Modell und Bild-Projektor aus dem KI-Profil | |
| `--host 127.0.0.1` | | nur auf diesem Laptop erreichbar |
| `--port` | 8090 (Einstellung) | |
| `-c` | `contextLength` (Standard 8192) | Grösse des Gedächtnisses |
| `-np 1` | | nur ein Platz für Anfragen, spart VRAM |
| `-lm none` | | Modell einlesen statt einblenden (mmap): weniger RAM, schneller (siehe Messungen) |
| `--no-webui`, `--offline` | | keine eingebaute Web-Oberfläche, kein Internet |
| `--log-file` | `data\logs\llama-server.log` | Fehler finden |
| `-ngl`, `--n-cpu-moe` | nur wenn im Profil gesetzt | sonst verteilt llama.cpp selbst (`--fit`) |

Weitere Punkte:

- Der Prozess hängt an einem **Windows Job Object**. Stürzt KI-Nexus ab, beendet Windows auch llama-server. Getestet: Nach einem harten Beenden der App war llama-server sofort weg und der VRAM frei.
- Nach dem Laden schickt die App eine kurze, unsichtbare **Aufwärm-Anfrage** (Lektion 1).
- **Entladen** heisst bei llama.cpp: Prozess beenden. Danach braucht Windows ca. 0.2 s, bis der Port wieder frei ist. Die App wartet so lange.
- Nach `keepAliveMinutes` (Standard 5) ohne Frage entlädt die App das Modell selbst. 0 = nie automatisch entladen.

## So spricht die App mit Ollama

- `POST /api/generate` mit leerem Prompt lädt das Modell. Danach folgt eine kurze Aufwärm-Anfrage.
- `POST /api/chat` mit `stream: true` liefert die Antwort Stück für Stück.
- `keep_alive: 0` entlädt das Modell. Der Knopf „Entladen“ macht genau das.
- Wichtig: Laden und Chat schicken dieselbe Kontextlänge (`num_ctx`). Sonst lädt Ollama das Modell neu.
- Die App liest und ändert **keine** Dateien von Ollama über die API. Sie liest nur die Manifeste für den Import (siehe [03-Modelle-und-KIs.md](03-Modelle-und-KIs.md)).

## Thinking ein und aus

Beide Modelle steuern Thinking über die Variable `enable_thinking` in ihrer Chat-Vorlage:

| Modell | Standard der Vorlage |
|---|---|
| gemma4 | aus (`enable_thinking | default(false)`) |
| qwen3.5 | an (nur aus, wenn `enable_thinking` = false) |

Damit der Schalter bei jedem Modell gleich wirkt, schickt die App den Wert **immer ausdrücklich** mit:

- llama.cpp: `chat_template_kwargs: { "enable_thinking": true/false }`, und wenn aus zusätzlich `reasoning_effort: "none"`.
- Ollama: `think: true/false`.

Die Gedanken kommen getrennt von der Antwort (`reasoning_content` bzw. `message.thinking`). Das Test-Feld zeigt sie grau an.
Beispiel gemma4, gleiche Frage: mit Thinking 1264 Token in 44 s, ohne Thinking 46 Token in 2.3 s.

## Messwerte

Gemessen am 30.10.2026, **am Netzteil**, Kontext 8192, Thinking aus, Treiber 592.01.
llama.cpp b11960 (CUDA 13.4), Ollama 0.40.13. Ohne Modell sind 12.8 GB RAM und 0.6 GB VRAM belegt.

### qwen3.5:9b (Q4_K_M, 5.2 GB + Projektor 0.9 GB)

| | llama.cpp | Ollama |
|---|---|---|
| Laden | 6.6 s | 5.2 s |
| Schreiben | 40–45 Token/s | 46–48 Token/s |
| Lesen, kurze Frage | 418 Token/s | 427 Token/s |
| Lesen, langer Text (2900 Token) | 1768 Token/s | 1878 Token/s |
| VRAM gesamt | 6.5 GB | 6.9 GB |
| System-RAM belegt | 14.8 GB | 16.0 GB |

### gemma4:26b (MoE, Q4_K_M, 15.9 GB + Projektor 1.1 GB)

| | llama.cpp (`-lm none`) | llama.cpp (mmap) | llama.cpp + Draft | Ollama |
|---|---|---|---|---|
| Laden | 17.6 s | 14.5 s | 20.1 s | 18.7 s |
| Schreiben | 40 Token/s | 26–36 Token/s | 41–55 Token/s | 40–48 Token/s |
| Lesen, langer Text (3265 Token) | 924 Token/s | 441 Token/s | 962 Token/s | 998 Token/s |
| VRAM gesamt | 6.6 GB | 6.6 GB | 7.1 GB | 7.3 GB |
| System-RAM belegt | 27.5 GB | 29.0 GB | 28.3 GB | 28.1 GB |

In der App (mit Systemprompt, Testfragen über die Oberfläche): Mael-KI über llama.cpp 34.6–38 Token/s, über Ollama 42.6 Token/s.

**Fazit:** llama.cpp ist ungefähr gleich schnell wie Ollama, beim Schreiben von gemma4 etwas langsamer. Ollama nutzt bei gemma4 schon den Draft (siehe unten).

### Achtung: am Akku ist alles viel langsamer

Der erste Messlauf lief versehentlich am Akku: qwen3.5 schrieb nur **6 Token/s**. Die GPU stand im Sparzustand (P4, 33 W, 1130 MHz statt bis 3090 MHz). Für gute Werte immer das Netzteil anschliessen.

## Antworten auf die offenen Fragen (Bauplan, Kapitel 12)

**1. Welcher llama.cpp-Build läuft auf der RTX 5070 (Blackwell)?**
Der Build **„win-cuda-13.4-x64“**. Er enthält fertigen Maschinencode für Blackwell (`sm_120a`). Der Build für CUDA 12.4 enthält ihn nicht und müsste beim Start erst übersetzen.
Dein Treiber meldet CUDA 13.1, der Build läuft trotzdem (Minor-Version-Kompatibilität von CUDA 13).
Neu bei llama.cpp: Das Release „v0.8.0“ enthält keine Programme, sondern nur den Namen des passenden Builds (b11960). Das Setup-Skript löst das auf.

**2. Laufen Gemma 4 und Qwen 3.5 in llama.cpp?**
Ja. Beide Architekturen (`gemma4`, `qwen35`) und beide Bild-Projektoren (`gemma4v`, `qwen3vl_merger`) werden unterstützt und laden fehlerfrei. Die Chat-Vorlage steckt in der GGUF-Datei und funktioniert. Thinking ein/aus: siehe oben. Bilder testen wir in M2.

**3. Sind die Ollama-Dateien normale GGUF-Dateien?**
Ja. Die drei Blobs von `gemma4:26b` sind: das Modell (15.9 GB, `gemma4`), der Bild-Projektor (1.1 GB, `clip`/`gemma4v`) und ein „Draft“ (0.43 GB, `gemma4-assistant`). Der Draft ist ein kleines Hilfsmodell, das Wörter vorschlägt, die das grosse Modell nur noch prüfen muss (spekulatives Dekodieren).
Darum gehen Hardlinks: llama.cpp liest die Ollama-Dateien direkt.

**4. Wie schaltet man Thinking pro Modellfamilie aus?**
Über `enable_thinking` in der Chat-Vorlage (llama.cpp) bzw. `think: false` (Ollama). Details oben.

**5. Braucht gemma4 mit `--n-cpu-moe` weniger RAM?**
Nein, nicht spürbar. Die neue Option `--fit` (Standard) macht automatisch dasselbe: Alle 31 Schichten liegen auf der GPU, aber die Experten von 29 Schichten (13.4 GB) bleiben im RAM. Von Hand muss man `--n-cpu-moe` darum nicht setzen (im KI-Profil geht es trotzdem: `cpuMoeLayers`).
Der RAM-Bedarf (ca. 15 GB zusätzlich) ist bei 16 GB Modell und 8 GB VRAM nicht kleiner zu machen. Was hilft: `-lm none` statt mmap (1.5 GB weniger RAM und doppelt so schnelles Lesen).

## Lektionen aus dem Bauplan, neu bewertet

- **Lektion 2 („Windows lagert heimlich aus“):** Die 13.6 GB „gemeinsamer GPU-Speicher“ bei Ollama sind kein Auslagern. Es ist „gepinnter“ RAM: normaler Arbeitsspeicher, den die GPU direkt lesen darf. Darin liegen die MoE-Experten, die nicht in die 8 GB passen. llama.cpp mit `-lm none` macht genau dasselbe (13.2 GB). Mit mmap ist dieser Wert klein, dafür ist alles langsamer.
- **Lektion 3 („Ollama schätzt falsch“):** Bestätigt. `ollama ps` zeigte für gemma4 1.4 GB VRAM, tatsächlich waren 7.3 GB belegt. Die App misst selbst über NVML (die Mess-Bibliothek des NVIDIA-Treibers).

## Der Draft von gemma4 (noch nicht eingeschaltet)

llama.cpp kann den Draft nutzen: `-md draft.gguf --spec-type draft-mtp`. Gemessen: Schreiben 41–55 statt 40 Token/s, 37–67 % der vorgeschlagenen Wörter werden übernommen.
Er ist **noch nicht Standard**, weil `--fit` den Speicher des Drafts nicht mitrechnen kann (Meldung im Log). Mit Draft bleiben nur ca. 1 GB VRAM frei. Für Bilder reicht das vielleicht nicht. Wir prüfen das in M2 mit Bildern und machen es in M6 als Einstellung verfügbar.
