# 07 – Open WebUI (Alternate Launch)

Open WebUI ist die Web-Oberfläche, die du schon kennst. KI-Nexus ersetzt sie nicht. Beide laufen nebeneinander.
Der Knopf **Alternate Launch** (Weltkugel in der Kopfleiste) startet Open WebUI im Hintergrund und öffnet den Browser.

## Bedienung

1. Klick auf die **Weltkugel** in der Kopfleiste. Ein Panel klappt auf.
2. **Starten:** KI-Nexus prüft kurz (uvx da? Port frei? Ollama oder llama-server da?) und startet Open WebUI.
3. Sobald `http://localhost:8080` antwortet, öffnet sich der Browser.
4. **Stoppen** beendet Open WebUI. **Im Browser öffnen** öffnet es nochmals.
5. **Log anzeigen** zeigt die letzten Zeilen, die Open WebUI ausgegeben hat.

Der kleine Punkt an der Weltkugel zeigt den Zustand: grün = läuft, gelb = startet, rot = Fehler, kein Punkt = gestoppt.

## Was KI-Nexus startet

```
uvx --python 3.11 open-webui@0.12.5 serve --host 127.0.0.1 --port 8080
```

| Was | Wert | Warum |
|---|---|---|
| Version | Einstellung „Feste Version“ (bei dir: `0.12.5`). Leer = `@latest` | Mit fester Version lädt nicht jeder Start ein Update. |
| `--host 127.0.0.1` | nur dieser Laptop | Bauplan: Alle Dienste hören nur auf 127.0.0.1. (Open WebUI würde sonst auf `0.0.0.0` hören, also im ganzen WLAN.) |
| `--port` | Einstellung, Standard 8080 | |
| `DATA_DIR` | Einstellung, Standard `%USERPROFILE%\ki\open-webui` | Benutzer, Chats, Einstellungen von Open WebUI |
| Arbeitsordner | der Ordner mit dem bestehenden `.webui_secret_key` | siehe unten |
| `OLLAMA_BASE_URL` | `http://127.0.0.1:11434` | |
| `SCARF_NO_ANALYTICS`, `DO_NOT_TRACK`, `ANONYMIZED_TELEMETRY=false` | Telemetrie aus | Wie im offiziellen Docker-Image. Bauplan: alles lokal. |

**Dauer:** Der erste Start einer Version dauert länger, weil uv die Pakete einrichtet (gemessen: 109 s, davon 15 s Pakete). Danach ca. **20 s**.

### Der Geheimschlüssel (`.webui_secret_key`)

Open WebUI unterschreibt damit die Anmeldungen. Es sucht ihn im **Arbeitsordner** und erzeugt sonst einen neuen. Mit einem neuen Schlüssel gelten alle Anmeldungen im Browser nicht mehr: Man muss sich neu anmelden.
Du hast Open WebUI bisher aus `C:\Users\mael\ki` gestartet, dort liegt dein Schlüssel. KI-Nexus startet Open WebUI darum dort, wo schon ein Schlüssel liegt (im Daten-Ordner oder im Ordner darüber, bei zweien der ältere). Gibt es keinen, nimmt es den Daten-Ordner.

## Open WebUI mit llama-server (Kapitel 12, Punkt 9)

Open WebUI zeigt die Modelle von **Ollama** (`OLLAMA_BASE_URL`). Die KIs von KI-Nexus laufen aber mit **llama-server** (`http://127.0.0.1:8090`). llama-server spricht die OpenAI-API, darum kann Open WebUI es als „OpenAI-Verbindung“ nutzen.

- Die Umgebungsvariablen dafür heissen `OPENAI_API_BASE_URL` (z.B. `http://127.0.0.1:8090/v1`) und `OPENAI_API_KEY` (beliebig, z.B. `none`). `ENABLE_OPENAI_API` muss `True` sein (Standard).
- **Aber:** Das sind „PersistentConfig“-Werte. Open WebUI speichert sie beim **ersten** Start in seiner Datenbank und liest danach nur noch dort. In deiner Datenbank steht schon `https://api.openai.com/v1` (der Standard). Die Umgebungsvariable würde also ignoriert.
- `ENABLE_PERSISTENT_CONFIG=False` würde das ändern, aber dann gehen alle Einstellungen, die du im Admin-Bereich von Open WebUI machst, bei jedem Neustart verloren. Darum nicht.

**Entscheid (mit Mael):** Einmal von Hand eintragen. KI-Nexus zeigt dazu einen Hinweis, wenn Ollama nicht läuft, llama-server aber schon:

1. Open WebUI → Admin-Bereich → Einstellungen → **Verbindungen**.
2. Bei **OpenAI-API** den Eintrag `https://api.openai.com/v1` durch `http://127.0.0.1:8090/v1` ersetzen (oder mit + hinzufügen), Schlüssel `none`.
3. Speichern. Ab dann sieht Open WebUI die geladene KI von KI-Nexus.

Das ersetzt auch den Cloud-Eintrag `api.openai.com`, den Open WebUI sonst bei jedem Start anfragt.
Wichtig: llama-server läuft nur, solange in KI-Nexus eine llama.cpp-KI geladen ist (nach „Geladen halten“ Minuten ohne Frage wird sie entladen).
Nur bei einem **neuen** Daten-Ordner (ohne `webui.db`) setzt KI-Nexus die Verbindung beim ersten Start automatisch per Umgebungsvariable.

## Beim Schliessen von KI-Nexus

Einstellung „Beim Schliessen von KI-Nexus beenden“ (Zahnrad → Open WebUI):

- **Ein (Standard):** Open WebUI endet mit KI-Nexus, auch wenn KI-Nexus abstürzt.
- **Aus:** Open WebUI läuft weiter. KI-Nexus merkt sich die Prozesse (Nummer und Startzeit) in `data/open-webui.processes`. Beim nächsten Start zeigt der Knopf „Läuft (vom letzten Mal)“, und „Stoppen“ beendet es.

Läuft Open WebUI, das du selbst gestartet hast (von Hand), zeigt KI-Nexus „Läuft (nicht von KI-Nexus gestartet)“. Stoppen kann es das dann nicht.

## Technik

- `OpenWebUiCommand` baut Befehl, Umgebung und Arbeitsordner. `UvxLocator` findet uvx (PATH oder `%USERPROFILE%\.local\bin`).
- `OpenWebUiLauncher` startet, wartet auf `GET /health` (bis 10 Minuten, beim ersten Mal lädt uv viel), überwacht den Prozess und stoppt.
- uvx startet eine Kette: `uvx → uv → open-webui.exe → python → python`. Zwischenglieder enden teils früh, darum findet „Prozessbaum beenden“ nicht alle. Gemessen: Nach dem ersten Versuch liefen noch 3 Prozesse weiter. Lösung: Open WebUI hängt an einem **eigenen Job Object**, „Stoppen“ beendet den ganzen Job.
- Das Log steht in `data/logs/open-webui.log` (bei jedem Start neu). Farbcodes werden entfernt.

## Einstellungen

| Schlüssel | Standard | Bedeutung |
|---|---|---|
| `openWebUi.stopOnExit` | `true` | Open WebUI mit KI-Nexus beenden |
| `openWebUi.pinnedVersion` | leer | feste Version, z.B. `0.12.5`. Leer = `@latest` |
| `paths.openWebUiData` | `%USERPROFILE%\ki\open-webui` | Daten-Ordner (DATA_DIR) |
| `ports.openWebUi` | `8080` | Port |
