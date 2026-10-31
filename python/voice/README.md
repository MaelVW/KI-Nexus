# python/voice – Sprach-Worker

`worker.py` ist der Hilfsprozess für den Gespräch-Stil (Meilenstein 4):

- **Mikrofon** lesen (sounddevice, 16 kHz mono)
- **Sprechpausen** erkennen (Silero-VAD v6, kommt mit faster-whisper)
- **Spracherkennung** mit faster-whisper (Modell aus `models/voice/whisper-<name>/`)
- **Sprachausgabe** mit Piper (Stimme aus `models/voice/piper/`)

Die App startet ihn selbst und redet mit ihm über JSON Lines auf stdin/stdout.
Einrichten: `scripts\setup-voice.ps1`. Protokoll und Details: [docs/05-Sprache.md](../../docs/05-Sprache.md).

Von Hand testen (zeigt die Mikrofone):

```powershell
python\.venv\Scripts\python.exe -I python\voice\worker.py --list-microphones
```
