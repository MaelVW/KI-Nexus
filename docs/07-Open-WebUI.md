# 07 – Open WebUI (Alternate Launch)

Open WebUI ist die Web-Oberfläche, die du schon kennst. KI-Nexus ersetzt sie nicht.
Ab **Meilenstein 5** startet ein Knopf in der Kopfleiste Open WebUI im Hintergrund und öffnet den Browser.

## Stand M0

- Der Knopf ist noch ausgeblendet (kommt in M5).
- Die Einstellungen sind schon da (Zahnrad → Open WebUI):
  - `openWebUi.stopOnExit` – Open WebUI beim Schliessen beenden (Standard: ja)
  - `openWebUi.pinnedVersion` – feste Version statt `@latest` (Standard: leer)
  - `paths.openWebUiData` – Daten-Ordner (Standard: `%USERPROFILE%\ki\open-webui`)
  - `ports.openWebUi` – Port (Standard: `8080`)

## So startest du es heute von Hand

```powershell
$env:DATA_DIR = "C:\Users\mael\ki\open-webui"
uvx --python 3.11 open-webui@latest serve
```

## Offene Frage (Bauplan, Kapitel 12)

9. Welche Umgebungsvariablen braucht Open WebUI, um llama-server als OpenAI-kompatible Quelle zu nutzen?
