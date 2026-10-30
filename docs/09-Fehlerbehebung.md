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

## Build-Fehler „Datei wird von einem anderen Prozess verwendet“

Die App läuft noch und sperrt `KI-Nexus.exe`. App schliessen und nochmals bauen.
