# 05 – Sprache

Im **Gespräch-Stil** sprichst du Hochdeutsch mit der KI, und sie antwortet mit einer deutschen Stimme.
Die Spracherkennung (Whisper) und die Sprachausgabe (Piper) laufen lokal in einem Python-Hilfsprozess. Nichts geht ins Internet.

## Stand M4

- Gespräch-Stil mit Orb, Mitschrift, Push-to-Talk und Freihändig (Schalter oben im Gespräch).
- Spracherkennung: **faster-whisper** mit dem Modell **large-v3-turbo** auf der CPU. Umschaltbar auf **small** (schneller, mehr Fehler).
- Sprechpausen-Erkennung (VAD): **Silero-VAD v6**, kommt mit faster-whisper mit.
- Sprachausgabe: **Piper** mit der Stimme **de_DE-thorsten-high**. Wiedergabe in der App mit NAudio.
- Unterbrechen per Stimme (Freihändig) kommt in **M7**. In M4 unterbrichst du mit `Esc`, dem Knopf „Unterbrechen“ oder (bei Push-to-Talk) einfach mit der Taste.

## Einrichten (einmal)

```powershell
.\scripts\setup-voice.ps1
```

Das Skript macht drei Dinge. Nichts wird systemweit installiert:

1. **Python-Umgebung** mit uv in `python\.venv` (ca. 300 MB): faster-whisper, CTranslate2, onnxruntime, piper-tts, sounddevice, numpy.
   `python\pyproject.toml` nimmt nur Versionen, die mindestens zwei Wochen alt sind (`exclude-newer`), die genauen Versionen stehen in `python\uv.lock`.
2. **Whisper-Modell** nach `models\voice\whisper-large-v3-turbo\` (1.6 GB, von Hugging Face, Prüfsumme wird geprüft).
3. **Piper-Stimme** nach `models\voice\piper\de_DE-thorsten-high.onnx` (114 MB, mit `.onnx.json`).

Andere Varianten:

```powershell
.\scripts\setup-voice.ps1 -Whisper small                # kleineres Whisper-Modell (0.5 GB)
.\scripts\setup-voice.ps1 -Voice de_DE-kerstin-low      # weitere Stimme (weiblich)
```

Deutsche Piper-Stimmen (Stand Oktober 2026): thorsten (low, medium, high), thorsten_emotional, kerstin, ramona, karlsson, pavoque, eva_k, mls.
Fehlt etwas, zeigen der Gespräch-Stil und die Einstellungen genau den Befehl, der es nachlädt.

## Bedienung

| Was | Wie |
|---|---|
| Starten | Klick auf den Orb oder Push-to-Talk-Taste. Beim ersten Mal lädt die App Whisper und Piper (ca. 8–10 s). |
| **Push-to-Talk** | Leertaste (oder den Orb mit der Maus) gedrückt halten, sprechen, loslassen. |
| **Freihändig** | Einfach sprechen. Nach 0.8 s Stille (einstellbar) gilt der Satz als fertig. Klick auf den Orb oder Leertaste: pausieren / weiter. |
| Unterbrechen | `Esc`, Knopf „Unterbrechen“, oder bei Push-to-Talk die Taste drücken (die KI verstummt, du kannst gleich sprechen). |
| Freigeben | Power-Symbol „Entladen“ beendet auch den Sprach-Worker (ca. 2 GB RAM). |

Der **Orb** zeigt den Zustand:

| Zustand | Orb |
|---|---|
| Ich höre zu | Schein pulsiert, Kern wächst mit deiner Lautstärke |
| Verstehe (Whisper) / Denke nach (KI) | Ring dreht sich |
| Spreche | Kern wächst mit der Stimme der KI |
| Pausiert / Bereit | gedämpft |
| Fehler | rot, darunter die Meldung |

Die **Mitschrift** unter dem Orb ist der normale Chat. Du kannst jederzeit in den Simpel- oder Terminal-Stil wechseln und dort weiterschreiben. Wechselst du den Stil, schliesst die App das Mikrofon.

## Was im Gespräch anders ist

- **Thinking ist immer aus**, auch wenn es im Chat eingeschaltet ist (Bauplan, Lektion 4). Die Einstellung des Chats bleibt dabei unverändert.
- **Zusatz zum Systemprompt:** Für jede gesprochene Frage hängt die App „Antworte kurz und so, wie man spricht.“ an den Systemprompt. Abschaltbar: Zahnrad → Sprache → „Im Gespräch kurz antworten“.
- **Markdown wird nicht vorgelesen** (`SpeechText`): keine Sterne, Rauten, Aufzählungszeichen, Links oder Tabellen-Striche, keine Emojis. Ein Codeblock wird nicht vorgelesen. Beim ersten sagt die KI „Den Code findest du in der Mitschrift.“
- **Satzweise:** Die App teilt die Antwort beim Schreiben in Sätze (`SentenceSplitter`). Piper liest den ersten Satz vor, während das Modell noch weiterschreibt. „z.B.“, „ca.“, „1. August“ oder „3.5“ beenden keinen Satz.
- **Mitschrift aufräumen:** „ß“ wird zu „ss“. Typische Whisper-Erfindungen („Untertitel im Auftrag des ZDF“, „Vielen Dank fürs Zuschauen“) werden verworfen.

## Einstellungen (Zahnrad → Sprache)

| Schlüssel in `settings.json` | Standard | Bedeutung |
|---|---|---|
| `voice.microphone` | `null` | Mikrofon (voller Windows-Name). `null` = Windows-Standard |
| `voice.voice` | `null` | Piper-Stimme. `null` = Thorsten, sonst die erste vorhandene |
| `voice.whisperModel` | `large-v3-turbo` | Ordner `models\voice\whisper-<name>` |
| `voice.speechRecognitionDevice` | `cpu` | `cpu` oder `gpu` (siehe unten) |
| `voice.pushToTalkKey` | `Space` | Leertaste, `RightCtrl`, `F8`, `F9`, `F10` |
| `voice.silenceMs` | `800` | Freihändig: so viel Stille beendet einen Satz (300–3000) |
| `voice.shortSpokenAnswers` | `true` | Zusatz „Antworte kurz …“ an den Systemprompt |

Neue Einstellungen gelten ab dem nächsten Start der Sprache (die App beendet den Worker beim Speichern, ein Klick auf den Orb startet ihn neu).
Pro KI gibt es in `kis/<id>.json` schon `voice.defaultMode` (`pushToTalk` oder `handsFree`). Damit startet der Schalter. Die Oberfläche dafür kommt in M7.

## Messwerte (31.10.2026, Core Ultra 9 275HX, am Netzteil)

Gemessen mit Sätzen, die Piper selbst gesprochen hat (saubere Aufnahme):

| | Zeit pro Satz (3–4 s Sprache) | Fehler in 5 Testsätzen |
|---|---|---|
| Whisper **large-v3-turbo**, int8, 12 Threads | ca. **3.0 s** | keine echten |
| Whisper **small**, int8, 12 Threads | ca. **0.8 s** | 2 Wortfehler („viele“, „Parameten“) |
| Piper thorsten-high, 4 Threads | ca. 0.25–0.4 s pro Satz (5- bis 15-mal schneller als Echtzeit) | – |

- Whisper rechnet immer mit einem 30-Sekunden-Fenster. Darum dauert ein kurzer Satz fast so lang wie ein langer.
- Mehr Threads helfen nur bis ca. 12 (4 Threads: 3.9 s, 8: 3.1 s, 12: 3.0 s, 24: 3.6 s).
- Laden von Whisper turbo + Piper: ca. 8–10 s. Danach bleiben sie geladen (ca. 2 GB RAM), bis du „Entladen“ drückst oder die App schliesst.
- Piper rechnet absichtlich nur mit 4 Threads: Mit allen Kernen nimmt es der KI (gemma4 rechnet teilweise auf der CPU) Tempo weg.

## GPU (Kapitel 12, Punkt 7)

- Die App kann Whisper auf der GPU starten (`device: cuda`, `int8_float16`). Das braucht die NVIDIA-Bibliotheken cuBLAS und cuDNN 9 für CUDA 12 (ca. 1.3 GB). **Diese sind noch nicht installiert** (Entscheid: CPU lässt den VRAM frei für das Modell).
- Stellst du auf GPU und die Bibliotheken fehlen, meldet die App das und nimmt die CPU.
- Ob CTranslate2 4.8 auf der RTX 5070 (Blackwell) läuft, ist **nicht getestet**.

## Piper heute (Kapitel 12, Punkt 6)

- Das alte Projekt `rhasspy/piper` (mit der `piper.exe`) ist seit Oktober 2025 archiviert. Weiterentwickelt wird es von der Open Home Foundation als `OHF-Voice/piper1-gpl` (GPL-3.0), verteilt als Python-Paket `piper-tts` (1.8.0, September 2026, mit Windows-Version).
- **Abweichung vom Bauplan:** Der Bauplan sieht `engines\piper` als `.exe` vor. Piper läuft jetzt als Python-Paket im selben Sprach-Worker wie Whisper (mit Mael abgesprochen).
- Beste deutsche Piper-Stimme: thorsten-high. Als Alternative gibt es eine deutsche Thorsten-Feinabstimmung von Kokoro (Apache 2.0). Sie soll natürlicher klingen, braucht aber angepasste Zusatzpakete (nicht getestet). Für M4 bleibt es bei Piper.

## Aufbau

```
App (C#)                                         Sprach-Worker (Python, python\voice\worker.py)
ConversationViewModel ── JSON Lines (stdin) ──►  Listener: Mikrofon (sounddevice, WASAPI, 16 kHz)
   │                                               → Silero-VAD → faster-whisper → "transcript"
   │                   ◄── JSON Lines (stdout) ──  Speaker: Piper → "audio" (PCM 16 Bit, Base64)
   ├─ ChatSessionViewModel.AskSpokenAsync (ohne Thinking, mit Zusatz)
   ├─ SentenceSplitter → "speak" pro Satz
   └─ PcmPlayer (NAudio) spielt die Stücke lückenlos ab, misst die Lautstärke für den Orb
```

- Den Worker startet der `ProcessSupervisor` (Job Object: endet mit der App). Python läuft isoliert (`-I`), Hugging Face ist gesperrt (`HF_HUB_OFFLINE=1`).
- Was der Worker auf stderr schreibt, steht im App-Log mit `[voice]` davor (z.B. „Erkannt in 3093 ms“ und der Pegel jeder Äusserung).
- **Freihändig** hört der Worker nach jeder Äusserung nicht mehr zu, bis die App „resume“ schickt. Das schickt sie erst, wenn die KI fertig gesprochen hat und der Lautsprecher 0.8 s still war. So nimmt das Mikrofon die Stimme der KI nicht auf (auch nicht mit Bluetooth-Verzögerung).

### Schutz vor erfundenem Text

Whisper „hört“ manchmal Sätze, wo keine sind. Drei Sperren:

1. **Zu leise:** Pegel RMS unter 0.001 oder Spitze unter 0.01 (praktisch Stille, z.B. Mikrofon stumm) geht gar nicht an Whisper. Gemessen: Stummes Mikrofon ergab sonst „Vielen Dank.“
2. **Unsicher:** Whisper-Stücke mit `avg_logprob` unter −1.0, `no_speech_prob` über 0.5 oder Wiederholungen (`compression_ratio` über 2.4) fallen weg.
3. **Bekannte Erfindungen** (Untertitel-Hinweise usw.) verwirft die App.

### Protokoll (JSON Lines)

App → Worker:

| `type` | Felder | Wirkung |
|---|---|---|
| `configure` | `whisperPath`, `voicePath`, `device`, `microphone`, `silenceMs`, `threads` | Modelle laden. Antwort: `ready` |
| `listen` | `mode`: `pushToTalk` oder `handsFree` | Mikrofon öffnen |
| `stop_listening` | `transcribe` | Mikrofon zu. Push-to-Talk mit `transcribe: true`: erkennen |
| `pause` / `resume` | – | Freihändig: kurz nicht zuhören / weiter |
| `speak` | `id`, `text` | Einen Satz sprechen |
| `stop_speaking` | – | Alles Ausstehende verwerfen |
| `transcribe_file` | `path` | WAV-Datei erkennen (für Tests) |
| `list_microphones` | – | Antwort: `microphones` |
| `shutdown` | – | Worker beenden |

Worker → App: `hello`, `ready` (`device`, `sampleRate`, `loadMs`), `listening` (`mode`, `microphone`), `speech_start`, `speech_end`, `transcribing`, `transcript` (`text`, `audioMs`, `tookMs`, `reason`: „zu kurz“ / „zu leise“), `level` (0–1), `audio` (`id`, `pcm`), `speak_done` (`id`, `cancelled`), `stopped`, `microphones`, `error` (`message`, `fatal`).

`python worker.py --list-microphones` gibt nur die Mikrofone aus (für die Einstellungen, ohne Whisper zu laden).

## Bekannte Grenzen

- **Freihändig hört alle Stimmen im Raum.** Redet jemand anders oder läuft ein Video, antwortet die KI darauf. Getestet: Ein Gespräch im Raum löste mehrere Antworten aus. In solchen Momenten Push-to-Talk nehmen. (Ein Aktivierungswort wäre eine Idee für später.)
- **Unterbrechen per Stimme** (die KI verstummt, wenn du freihändig dazwischen sprichst) kommt in M7. Dafür braucht es Echo-Unterdrückung, sonst unterbricht sich die KI mit ihrer eigenen Stimme.
- **Bluetooth-Kopfhörer als Mikrofon** (JBL Tune 520BT „Hands-Free“) schalten in den Telefon-Modus: Das Mikrofon geht, aber die Musik klingt dann dumpf. Besser: Laptop-Mikrofon oder ein USB-Headset.
- Whisper turbo braucht auf der CPU ca. 3 s pro Satz. Ist dir das zu langsam: Zahnrad → Sprache → small.

## Fehlersuche

Startest du KI-Nexus mit der Umgebungsvariable `KINEXUS_VOICE_DEBUG=<Ordner>`, speichert der Worker jede Äusserung als WAV-Datei in diesen Ordner. So hörst du nach, was das Mikrofon wirklich aufgenommen hat. Ohne die Variable speichert die App keine Aufnahmen.
