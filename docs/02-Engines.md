# 02 – Engines

Eine Engine ist das Programm, das das KI-Modell wirklich rechnet.
KI-Nexus hat eine eigene Engine (llama.cpp) und nutzt Ollama zusätzlich, wenn es installiert ist.
Diese Datei beschreibt Start, Ports, Parameter und Messwerte. Sie wird in **Meilenstein 1** gefüllt.

## Stand M0

- Es gibt noch keine Engine-Anbindung.
- Vorhanden ist `LocalPort.IsFree(port)`: prüft, ob auf `127.0.0.1` ein Port frei ist. Das braucht M1 vor dem Start von llama-server.
- Die Ports sind schon einstellbar (Zahnrad → Ports): llama-server `8090`, Ollama `11434`.

## Plan für M1

| Engine | Programm | Adresse | Wann |
|---|---|---|---|
| llama.cpp | `engines/llama.cpp/llama-server.exe` (CUDA-Build) | `http://127.0.0.1:8090` | immer |
| Ollama | Ollama-App | `http://127.0.0.1:11434` | nur wenn installiert und gestartet |

- `scripts/setup-engines.ps1` lädt llama.cpp herunter (Build für RTX 50 / Blackwell).
- Beide Engines erfüllen die Schnittstelle `IInferenceBackend` (siehe Bauplan, Kapitel 5).
- Nach dem Laden schickt die App eine kurze, unsichtbare Aufwärm-Anfrage (Lektion 1).
- Der Knopf „Entladen“ in der Kopfleiste gibt den Grafikspeicher frei.

## Offene Fragen (Bauplan, Kapitel 12)

Diese Punkte prüfe ich in M1 und trage die Antworten hier ein:

1. Welcher llama.cpp-Build für Windows unterstützt die RTX 5070 (Blackwell)? Vermutlich CUDA 12.8 oder neuer.
2. Wie gut laufen Gemma 4 und Qwen 3.5 in llama.cpp? Bilder (mmproj), Thinking ein/aus, Chat-Vorlage?
3. Sind die Ollama-Blobs von `gemma4:26b` normale GGUF-Dateien? Was steckt in den drei Blobs (17 GB, 1.2 GB, 461 MB)?
4. Wie schaltet man Thinking pro Modellfamilie aus?
5. Braucht `gemma4:26b` mit `--n-cpu-moe` weniger RAM, bei gleichem oder besserem Tempo?

## Messwerte

Kommen in M1. Vergleichswerte mit Ollama aus dem Bauplan:

| Modell | Antwort schreiben | Frage lesen | Ladezeit |
|---|---|---|---|
| qwen3.5:9b | ca. 35–45 Token/s | ca. 190 Token/s | ca. 4–5 s |
| gemma4:26b | ca. 37–50 Token/s | 500 bis über 8000 Token/s | ca. 20 s |
