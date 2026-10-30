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
| „Die Modell-Datei fehlt“ | Modell noch nicht in `models\` | Im Test-Feld „Aus Ollama übernehmen“ klicken. Ohne Ollama: ab M3 über den Create-Hub |
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

## Build-Fehler „Datei wird von einem anderen Prozess verwendet“

Die App läuft noch und sperrt `KI-Nexus.exe`. App schliessen und nochmals bauen.
