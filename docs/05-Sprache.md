# 05 – Sprache

> **Hinweis (5. November 2026):** Der Gesprächsmodus ist nicht mehr Teil des öffentlichen Repositorys. Er liegt im privaten Fun-Tab (Ordner `privat\`, nur auf Maels PC). Die Beschreibung unten gilt dort weiter.

Im **Gespräch-Stil** sprichst du Hochdeutsch mit der KI, und sie antwortet mit einer deutschen Stimme.
Die Spracherkennung (Whisper) und die Sprachausgabe (Piper) laufen lokal in einem Python-Hilfsprozess. Nichts geht ins Internet.

## Stand M4

- Gespräch-Stil mit Orb, Mitschrift, Push-to-Talk und Freihändig (Schalter oben im Gespräch).
- Spracherkennung: **faster-whisper** mit dem Modell **large-v3-turbo** auf der CPU. Umschaltbar auf **small** (schneller, mehr Fehler).
- Sprechpausen-Erkennung (VAD): **Silero-VAD v6**, kommt mit faster-whisper mit.
- Sprachausgabe: **Piper** mit der Stimme **de_DE-thorsten-high**. Wiedergabe in der App mit NAudio.
- Unterbrechen per Stimme (Freihändig) kommt in **M7**. In M4 unterbrichst du mit `Esc`, dem Knopf „Unterbrechen“ oder (bei Push-to-Talk) einfach mit der Taste.

## Stand M7

- **Standard-Modus pro KI:** Feineinstellungen → Gespräch → „Gespräch startet mit“: Push-to-Talk oder Freihändig.
- **Modus pro Chat:** Schaltest du im Gesprächsfenster um, merkt sich die App das für diesen Chat (Spalte `voice_mode` in `chats`). Öffnest du den Chat wieder, gilt sein Modus. Ein Chat ohne eigenen Modus nimmt den der KI.
- **Freihändig startet von selbst:** Öffnest du den Gespräch-Stil im Modus Freihändig, lädt die App die Sprache und hört gleich zu. Abschaltbar: Zahnrad → Sprache.
- **Unterbrechen durch Sprechen (Freihändig):** Sprichst du, während die KI denkt oder spricht, hört sie sofort auf, und deine neue Frage wird aufgenommen. Abschaltbar: Zahnrad → Sprache.
- **Weibliche Stimme:** Ramona (jung) ist jetzt die Standardstimme, im Tempo „Ruhig“. Weitere Stimmen und das Sprechtempo gibt es pro KI. Siehe „Stimmen“ unten.

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

## Stimmen

Eingerichtet sind (in `models\voice\piper\`, Hörproben in `models\voice\samples\`):

| Stimme | Name in der Auswahl | Gemessen | Lizenz der Aufnahmen |
|---|---|---|---|
| `de_DE-thorsten-high` | Thorsten (männlich, beste Qualität) | 118 Hz, 22 kHz | CC0 |
| `de_DE-ramona-low` | Ramona (weiblich, jung) | 205 Hz, sauberstes Signal der Frauenstimmen, 16 kHz | M-AILABS (siehe Modellkarte) |
| `de_DE-kerstin-low` | Kerstin (weiblich, tiefer) | 170 Hz, ruhig, 16 kHz | CC0 |
| `de_DE-mls-medium#110` | Sprecherin 110 (weiblich, jung, ruhig) | 214 Hz, ruhige Melodie, 22 kHz | CC-BY 4.0 |
| `de_DE-mls-medium#120` | Sprecherin 120 (weiblich, ruhig) | 204 Hz, 22 kHz | CC-BY 4.0 |

**So wurde ausgesucht** (Wunsch: weiblich, jung, gelassen): Für jede Stimme und alle 236 Sprecher von `de_DE-mls-medium` hat ein Skript denselben Satz erzeugt und gemessen: die mittlere Tonhöhe (jung klingt eher höher, ca. 190–240 Hz), wie stark die Melodie schwankt (gelassen = wenig), das Tempo und wie sauber das Signal ist. Gewonnen hat Ramona. Mit dem Tempo „Ruhig“ (`length_scale` 1.1) spricht sie etwas langsamer. Die zwei MLS-Sprecherinnen sind die ruhigsten und saubersten jungen Frauenstimmen aus `mls`. Hören musst du selbst: Die Messung sagt nichts über den Klang.
`eva_k` (nur „x_low“, 16 kHz, rauscht) wurde getestet und wieder gelöscht.

**Stimme wählen:** Zahnrad → Sprache → Stimme (für alle KIs) und Sprechtempo. Pro KI: Feineinstellungen → Gespräch → Stimme und Sprechtempo („Wie in den Einstellungen“ = die allgemeine). Eine andere Stimme lädt der Worker ohne Neustart (ca. 1 s, Befehl `voice`). Stimmen mit vielen Sprechern stehen als `<stimme>#<nummer>` in den Einstellungen.

## Bedienung

| Was | Wie |
|---|---|
| Starten | Klick auf den Orb oder Push-to-Talk-Taste. Beim ersten Mal lädt die App Whisper und Piper (ca. 8–10 s). |
| **Push-to-Talk** | Leertaste (oder den Orb mit der Maus) gedrückt halten, sprechen, loslassen. |
| **Freihändig** | Einfach sprechen. Nach 0.8 s Stille (einstellbar) gilt der Satz als fertig. Klick auf den Orb oder Leertaste: pausieren / weiter. |
| Unterbrechen | `Esc`, Knopf „Unterbrechen“, bei Push-to-Talk die Taste drücken, oder freihändig einfach dazwischen sprechen (ca. 0.4 s deutliche Sprache). |
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
| `voice.lengthScale` | `1.0` | Sprechtempo: 0.9 schneller, 1.1 ruhig, 1.25 langsam (gilt für KIs ohne eigenes Tempo) |
| `voice.bargeIn` | `true` | Freihändig: Dazwischensprechen unterbricht die KI |
| `voice.autoListen` | `true` | Freihändig: beim Öffnen des Gesprächs gleich zuhören |

Mikrofon, Whisper-Modell, Gerät und Sprechpause gelten ab dem nächsten Start der Sprache (die App beendet den Worker beim Speichern, ein Klick auf den Orb startet ihn neu). Stimme und Tempo wechseln ohne Neustart.

Pro KI in `kis/<id>.json`:

| Schlüssel | Bedeutung |
|---|---|
| `voice.defaultMode` | `pushToTalk` oder `handsFree`: damit startet ein Chat ohne eigenen Modus |
| `voice.voice` | eigene Stimme, z.B. `de_DE-ramona-low`. `null` = die aus den Einstellungen |
| `voice.lengthScale` | eigenes Tempo. `null` = das aus den Einstellungen |

## Unterbrechen durch Sprechen (ab M7)

Im Modus Freihändig schickt die App beim Start jeder Antwort `watch` an den Worker. Er hört dann mit einem strengeren Massstab zu als sonst: Erst wenn die Sprech-Wahrscheinlichkeit von Silero-VAD ca. 0.4 s lang (12 Stücke à 32 ms) über 0.7 liegt, meldet er `barge_in`. Die App stoppt sofort die Antwort und die Stimme. Der Worker nimmt schon auf, deine neue Frage geht also ohne Lücke weiter (die ersten 0.3 s sind im Vorlauf dabei).

**Echo:** Ohne Kopfhörer hört das Mikrofon die KI selbst. Dann würde sie sich selbst unterbrechen. Die App prüft darum die Mitschrift nach einem Unterbrechen: Stammen mindestens 70 % der Wörter aus dem, was die KI gerade gesagt hat, ist es ihr Echo. Die Frage wird verworfen, die App zeigt einen Hinweis und hört weiter zu. Am besten geht Unterbrechen mit Kopfhörern. Mit Lautsprechern kannst du es im Zahnrad ausschalten.

Geprüft: Ein Testskript schickt Stücke einer WAV-Datei durch den Worker (ohne Mikrofon). Sprache löst `barge_in` aus. Stille, Rauschen und ein kurzer Laut (0.13 s) lösen nichts aus. Mit deiner Stimme ist es noch nicht getestet.

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
| `configure` | `whisperPath`, `voicePath`, `device`, `microphone`, `silenceMs`, `threads`, `speakerId`, `lengthScale` | Modelle laden. Antwort: `ready` |
| `voice` | `voicePath`, `speakerId`, `lengthScale` | Andere Stimme laden (ca. 1 s). Antwort: `voice_changed` (`sampleRate`, `loadMs`) |
| `watch` | – | Freihändig während der Antwort: auf Unterbrechen achten. Meldet `barge_in` |
| `listen` | `mode`: `pushToTalk` oder `handsFree` | Mikrofon öffnen |
| `stop_listening` | `transcribe` | Mikrofon zu. Push-to-Talk mit `transcribe: true`: erkennen |
| `pause` / `resume` | – | Freihändig: kurz nicht zuhören / weiter |
| `speak` | `id`, `text` | Einen Satz sprechen |
| `stop_speaking` | – | Alles Ausstehende verwerfen |
| `transcribe_file` | `path` | WAV-Datei erkennen (für Tests) |
| `list_microphones` | – | Antwort: `microphones` |
| `shutdown` | – | Worker beenden |

Worker → App: `hello`, `ready` (`device`, `sampleRate`, `loadMs`), `listening` (`mode`, `microphone`), `speech_start`, `speech_end`, `transcribing`, `transcript` (`text`, `audioMs`, `tookMs`, `reason`: „zu kurz“ / „zu leise“), `level` (0–1), `audio` (`id`, `pcm`), `speak_done` (`id`, `cancelled`), `stopped`, `barge_in`, `voice_changed`, `microphones`, `error` (`message`, `fatal`).

`python worker.py --list-microphones` gibt nur die Mikrofone aus (für die Einstellungen, ohne Whisper zu laden).

## Bekannte Grenzen

- **Freihändig hört alle Stimmen im Raum.** Redet jemand anders oder läuft ein Video, antwortet die KI darauf. Getestet: Ein Gespräch im Raum löste mehrere Antworten aus. In solchen Momenten Push-to-Talk nehmen. (Ein Aktivierungswort wäre eine Idee für später.)
- **Unterbrechen per Stimme** hat keine echte Echo-Unterdrückung. Mit Lautsprechern kann sich die KI selbst unterbrechen. Die Echo-Prüfung fängt das meistens ab, aber die Antwort ist dann trotzdem gestoppt. Mit Kopfhörern gibt es das Problem nicht.
- **Bluetooth-Kopfhörer als Mikrofon** (JBL Tune 520BT „Hands-Free“) schalten in den Telefon-Modus: Das Mikrofon geht, aber die Musik klingt dann dumpf. Besser: Laptop-Mikrofon oder ein USB-Headset.
- Whisper turbo braucht auf der CPU ca. 3 s pro Satz. Ist dir das zu langsam: Zahnrad → Sprache → small.

## Fehlersuche

Startest du KI-Nexus mit der Umgebungsvariable `KINEXUS_VOICE_DEBUG=<Ordner>`, speichert der Worker jede Äusserung als WAV-Datei in diesen Ordner. So hörst du nach, was das Mikrofon wirklich aufgenommen hat. Ohne die Variable speichert die App keine Aufnahmen.
