# 09 – Fehlerbehebung

Hier stehen häufige Probleme und wie du sie löst.
Der erste Blick geht immer ins Log: `data/logs/ki-nexus-JJJJMMTT.log` (eine Datei pro Tag).

## Die App startet nicht

| Anzeichen | Ursache | Lösung |
|---|---|---|
| „Das .NET SDK fehlt“ | .NET 10 SDK nicht installiert | .NET 10 SDK installieren, dann `dotnet --list-sdks` prüfen |
| „Der Build ist fehlgeschlagen“ | Fehler im Code | Fehlermeldung im Fenster lesen. Im Projektordner `dotnet build` ausführen. |
| „KI-Nexus läuft schon“ | Die App ist schon offen | In der Taskleiste nachschauen. Sonst im Task-Manager `KI-Nexus.exe` beenden. |
| Meldung „KI-Nexus konnte nicht starten“ | Fehler beim Start | Details im Log, Zeile mit `[FTL]` |

## Das Fenster ist nicht zu sehen

Vielleicht war es auf einem zweiten Monitor, der jetzt fehlt. Die App prüft das zwar und öffnet das Fenster dann in der Mitte.
Wenn es trotzdem nicht klappt: App beenden und in `data/settings.json` bei `window` die Werte `left` und `top` auf `null` setzen.

## Einstellungen zurücksetzen

- In der App: Zahnrad → „Auf Standard zurücksetzen“ → „Speichern“.
- Oder ganz: App beenden und `data/settings.json` löschen. Beim nächsten Start gelten die Standardwerte.

## „settings.json.kaputt-…“ im Daten-Ordner

Die Einstellungs-Datei war beschädigt (kein gültiges JSON). Die App hat sie unter diesem Namen gesichert und mit Standardwerten weitergemacht.
Du kannst die Sicherung ansehen und löschen, wenn du sie nicht mehr brauchst.

## `dotnet test` meldet „Testing with VSTest target is no longer supported“

Die Tests nutzen xUnit v3 mit der neuen Microsoft Testing Platform. Das muss in `global.json` eingeschaltet sein:

```json
"test": { "runner": "Microsoft.Testing.Platform" }
```

Dann so testen:

```powershell
dotnet test --solution KI-Nexus.sln
```

## Engines

| Anzeichen | Ursache | Lösung |
|---|---|---|
| „llama.cpp ist nicht eingerichtet“ | `engines\llama.cpp` fehlt | `.\scripts\setup-engines.ps1` ausführen (oder `Start-KI-Nexus.bat`, es fragt nach) |
| „Die Modell-Datei fehlt“ | Modell noch nicht in `models\` oder gelöscht | Im Hinweis über dem Chat „Aus Ollama übernehmen“ klicken. Oder im Create-Hub importieren bzw. von Hugging Face laden |
| „Ollama läuft nicht“ | Ollama-App nicht gestartet | Ollama-App starten. llama.cpp-KIs gehen auch ohne Ollama |
| „Port 8090 ist schon belegt“ | Ein anderer llama-server läuft noch | Im Task-Manager `llama-server.exe` beenden oder im Zahnrad einen anderen Port wählen |
| „llama-server ist beim Laden abgestürzt“ | z.B. zu wenig Speicher oder kaputte Datei | Die letzte Meldung steht in der Fehlermeldung. Alles steht in `data\logs\llama-server.log` |
| Antworten sehr langsam (unter 10 Token/s) | Laptop läuft am Akku: die GPU wird stark gedrosselt | Netzteil anschliessen |
| RAM fast voll mit gemma4 | Normal: 16 GB Modell, nur 8 GB VRAM | Für Games oder grosse Programme das Modell entladen (Power-Symbol) oder „Qwen schnell“ nehmen |

Das Log von llama-server (`data\logs\llama-server.log`) wird bei jedem Laden neu geschrieben.

## Chat

| Anzeichen | Ursache | Lösung |
|---|---|---|
| +-Knopf für Bilder ist grau | Die gewählte KI versteht keine Bilder | Mit der Maus darüber steht der Grund. Mael-KI (gemma4) versteht Bilder. |
| „Nur Bilder gehen …“ beim Drag & Drop | Datei ist kein Bild (z.B. PDF) | Erlaubt sind png, jpg, jpeg, gif, bmp |
| Gelber Hinweis „Der Kontext ist voll …“ | Der Chat ist länger als das Gedächtnis des Modells | Normal. Die ältesten Nachrichten gehen nicht mehr ans Modell. Neuer Chat (`Strg + N`) oder ab M6 grösseren Kontext einstellen. |
| „Die Nachricht ist zu lang für den Kontext“ | Schon die neue Nachricht allein passt nicht | Nachricht kürzen |
| Chat-Titel bleibt „Neuer Chat“ | Titel-Erzeugung ist aus oder wurde durch eine neue Frage abgebrochen | Kommt nach der nächsten Antwort. Oder selbst umbenennen (`F2`). |
| „chats.db hat Version X, diese App kennt nur bis Y“ | Die Datenbank kommt von einer neueren App-Version | App aktualisieren (`git pull`). Die Datei nicht von Hand ändern. |

Alle Chats stehen in `data\chats.db`. Zum Sichern genügt es, diese Datei und den Ordner `data\attachments\` zu kopieren (App vorher schliessen).

## Create-Hub

| Anzeichen | Ursache | Lösung |
|---|---|---|
| „Ladetest fehlgeschlagen“ nach „Aus Ollama übernehmen“ | Ollama nutzt manchmal eigene Formate, die llama.cpp nicht kennt | Knopf „Auf Hugging Face suchen“: dasselbe Modell als GGUF laden. Die KI über Ollama geht weiterhin |
| „Kein Zugriff. Das Modell ist gesperrt (gated)“ | Das Modell braucht eine Zustimmung und einen Token | Auf huggingface.co die Bedingungen akzeptieren, einen Lese-Token erstellen und im Create-Hub speichern |
| Suche geht plötzlich nicht mehr („Kein Zugriff“) | Ein ungültiger oder abgelaufener Token ist gespeichert | Token löschen oder einen neuen speichern |
| „Hugging Face ist nicht erreichbar“ | Kein Internet | Verbindung prüfen. Angefangene Downloads mit „Fortsetzen“ weiterladen |
| Download zeigt lange „Verbinde …“ oder bleibt stehen | Der Download-Server antwortet langsam oder die Verbindung hängt | Die App versucht es nach 30 s ohne Daten selbst nochmals (bis 5 Mal). Sonst „Pause“ und „Fortsetzen“ |
| „Die Prüfsumme stimmt nicht“ | Datei beim Herunterladen beschädigt | Die App hat die Datei gelöscht. Nochmals herunterladen |
| „Zu wenig Speicherplatz“ | Nach dem Download wären weniger als 2 GB frei | Platz schaffen, z.B. ein ungenutztes Grundmodell löschen |
| „Aus dem Ollama-Ordner wird nie verschoben“ | Du hast eine Datei aus `.ollama` importiert und „Verschieben“ gewählt | „Kopieren“ wählen oder unter „Aus Ollama übernehmen“ (Hardlink, braucht keinen Platz) |
| „Das ist nur ein Bild-Projektor (mmproj)“ | Nur die mmproj-Datei wurde hineingezogen | Modell-Datei und mmproj zusammen hineinziehen |
| Grundmodell löschen gibt keinen Platz frei | Das Modell ist ein Hardlink auf Ollama | Normal: Die Daten gehören auch Ollama. Platz wird erst frei, wenn auch Ollama das Modell löscht |
| Angefangene Downloads tauchen nach dem Neustart wieder auf | Gewollt: Sie stehen pausiert in der Liste | „Fortsetzen“ oder „Abbrechen“ (löscht die halbe Datei in `models\.downloads\`) |
| „llama-server ist beim Laden abgestürzt“, im Log `failed to read tensor info` oder `tensor name … is too long` | Die GGUF-Datei ist kein Sprachmodell, z.B. ein Video- oder Bildmodell (Architektur `ltxv`, `flux`, `wan` …). Solche Dateien sind für Programme wie ComfyUI gedacht | Mit KI-Nexus nicht nutzbar. Die Architektur steht in `models\<name>\model.json` (`architecture`). Grundmodell im Create-Hub löschen, um Platz zu sparen |

## System-Prompt und Feineinstellungen

| Anzeichen | Ursache | Lösung |
|---|---|---|
| „Speichern“ bleibt grau | Nichts geändert, oder ein Feld ist rot (ungültig) | Den roten Hinweis lesen und den Wert korrigieren. Leer heisst „Standard“ |
| „Das Ollama-Modell … konnte nicht erstellt werden: Ollama läuft nicht“ | Ollama war beim Speichern aus | Die KI ist trotzdem gespeichert. Ollama-App starten, dann „Ollama-Modell aktualisieren“ |
| Rote Speicher-Warnung bei der Kontextlänge | Das Modell plus Kontext ist grösser als VRAM + nutzbarer RAM | Kleinere Kontextlänge wählen. 8192 reicht für normale Chats |
| Nach dem Einschalten des Drafts: Laden scheitert oder „out of memory“ | Der Draft braucht ca. 0.5 GB mehr VRAM, `--fit` rechnet ihn nicht mit | Draft wieder ausschalten (Feineinstellungen → Engine) |
| Geänderte Kontextlänge wirkt nicht | Das Modell war schon geladen | Normal: Die App lädt bei der nächsten Frage neu. Sonst Power-Knopf (Entladen) und nochmals fragen |
| Eine alte Fassung fehlt in „Versionen“ | Es bleiben nur die letzten 10 | Wichtige Prompts zusätzlich als Datei aufheben |

## Sprache (Gespräch-Stil)

| Anzeichen | Ursache | Lösung |
|---|---|---|
| „Die Sprache ist noch nicht eingerichtet“ | Python-Umgebung oder Modelle fehlen | `.\scripts\setup-voice.ps1` ausführen (lädt ca. 1.8 GB) |
| „Das Whisper-Modell … fehlt“ / „Die Stimme … fehlt“ | In den Einstellungen ist etwas gewählt, das nicht geladen ist | Den angezeigten Befehl ausführen, z.B. `.\scripts\setup-voice.ps1 -Whisper small` |
| „Ich habe nichts gehört“ | Mikrofon stumm, falsches Mikrofon oder zu weit weg | Stummschaltung prüfen (Taste am Laptop, Windows-Einstellungen → Datenschutz → Mikrofon). Zahnrad → Sprache → Mikrofon |
| „Nichts verstanden“ | Whisper war sich nicht sicher (Rauschen, ferne Stimmen) | Näher ans Mikrofon, deutlich sprechen |
| Freihändig antwortet die KI auf andere Leute oder ein Video | Freihändig hört alle Stimmen im Raum | Push-to-Talk nehmen |
| Erkennung dauert ca. 3 s | Whisper turbo auf der CPU | Normal. Schneller: Zahnrad → Sprache → small (macht mehr Fehler) |
| Die KI hört sich selbst und antwortet sich | Lautsprecher sehr laut oder sehr nah am Mikrofon | Kopfhörer nehmen oder leiser stellen. Freihändig wartet zwar, bis die KI fertig ist, aber Nachhall kann trotzdem ankommen |
| Musik klingt dumpf, sobald das Gespräch läuft | Bluetooth-Kopfhörer als Mikrofon schalten in den Telefon-Modus | Laptop-Mikrofon wählen (Zahnrad → Sprache → Mikrofon) |
| „Spracherkennung auf der GPU geht nicht“ | NVIDIA-Bibliotheken für Whisper fehlen | Auf CPU stellen. GPU ist noch nicht eingerichtet (siehe 05-Sprache.md) |
| „Der Sprach-Worker wurde beendet“ | Python ist abgestürzt | Klick auf den Orb startet neu. Die Ursache steht im Log (Zeilen mit `[voice]`) |

Was das Mikrofon wirklich aufgenommen hat: KI-Nexus mit der Umgebungsvariable `KINEXUS_VOICE_DEBUG=C:\Pfad\zum\Ordner` starten. Dann speichert der Worker jede Äusserung dort als WAV-Datei.

## Open WebUI (Alternate Launch)

| Anzeichen | Ursache | Lösung |
|---|---|---|
| „uvx fehlt“ | uv ist nicht installiert | uv installieren (https://docs.astral.sh/uv/), dann nochmals |
| „Port 8080 ist von einem anderen Programm belegt“ | Etwas anderes läuft auf dem Port | Das Programm beenden oder im Zahnrad einen anderen Port für Open WebUI wählen |
| „Läuft (nicht von KI-Nexus gestartet)“ | Open WebUI läuft schon, z.B. von Hand gestartet | Im Browser öffnen geht. Beenden dort, wo du es gestartet hast |
| Start dauert über eine Minute | Erster Start einer Version: uv richtet Pakete ein | Warten. Danach ca. 20 s. Mit fester Version passiert das nur nach einem Wechsel |
| „Open WebUI hat sich beendet (Code …)“ | Fehler beim Start | „Log anzeigen“ im Panel, oder `data\logs\open-webui.log` |
| Open WebUI zeigt keine Modelle | Weder Ollama noch llama-server laufen, oder llama-server ist nicht als Verbindung eingetragen | Ollama-App starten. Für llama-server: einmal die Verbindung eintragen (siehe 07-Open-WebUI.md) |
| Nach dem Start bin ich in Open WebUI abgemeldet | Open WebUI hat einen neuen Geheimschlüssel erzeugt | Neu anmelden (Passwort bleibt). KI-Nexus nimmt den bestehenden Schlüssel, wenn er im Daten-Ordner oder im Ordner darüber liegt |
| Vom Handy aus geht Open WebUI nicht mehr | KI-Nexus startet es nur für diesen Laptop (127.0.0.1) | Gewollt (Bauplan). Für das Heimnetz müsste man Open WebUI von Hand mit `--host 0.0.0.0` starten |

## Build-Fehler „Datei wird von einem anderen Prozess verwendet“

Die App läuft noch und sperrt `KI-Nexus.exe`. App schliessen und nochmals bauen.
