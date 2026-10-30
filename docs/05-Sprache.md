# 05 – Sprache

Im Gespräch-Stil sprichst du mit der KI, und sie antwortet mit einer Stimme.
Die Spracherkennung läuft in Python (faster-whisper), die Sprachausgabe mit Piper. Beides lokal.
Diese Datei wird in **Meilenstein 4** gefüllt.

## Stand M0

- Der Ordner `python/voice/` ist angelegt.
- Im Einstellungs-Dialog gibt es den Bereich „Sprache“ mit dem Hinweis auf M4.
- In `settings.json` stehen die Schlüssel schon bereit: `voice.microphone`, `voice.voice`, `voice.speechRecognitionDevice` (Standard `cpu`), `voice.pushToTalkKey` (Standard `Space`).

## Offene Fragen (Bauplan, Kapitel 12)

6. Piper: aktueller Stand, beste deutsche Stimme, gibt es bessere lokale Alternativen?
7. faster-whisper mit RTX 50: welche CUDA-Version? Welches Whisper-Modell ist auf der CPU schnell genug für Hochdeutsch?
