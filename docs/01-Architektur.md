# 01 – Architektur

KI-Nexus ist in drei Schichten geteilt: Oberfläche, Logik und Engines.
Jede Schicht kennt nur die Schicht darunter. So kann man Teile austauschen, ohne alles umzubauen.
Hier steht, welche Klasse was macht und warum es so gebaut ist.

## Schichten

```
┌──────────── KiNexus.App (WPF) ────────────┐
│ Views (XAML) + ViewModels (MVVM)          │
│ Theme, Fenster, Dialoge                   │
└──────────────────┬────────────────────────┘
                   │ nutzt
┌──────────── KiNexus.Core ─────────────────┐
│ Einstellungen, Pfade, Farben              │
│ später: ChatService, KiRegistry, …        │
│ später: Schnittstelle IInferenceBackend   │
└──────────────────▲────────────────────────┘
                   │ erfüllt die Schnittstelle
┌──────────── KiNexus.Engines ──────────────┐
│ LocalPort (Port frei?)                    │
│ später: LlamaCppBackend, OllamaBackend    │
└───────────────────────────────────────────┘
```

**Projekt-Verweise:**

- `KiNexus.App` → `KiNexus.Core` und `KiNexus.Engines`
- `KiNexus.Engines` → `KiNexus.Core`
- `KiNexus.Core` → nichts (nur das Logging-Interface von Microsoft)

**Warum verweist Engines auf Core und nicht umgekehrt?**
Die Schnittstelle `IInferenceBackend` und die Datenmodelle (z.B. `KiProfile`) gehören zur Logik. Darum kommen sie in M1 nach `Core`.
Die Engines *erfüllen* diese Schnittstelle. Würde Core auf Engines verweisen, könnte Engines `KiProfile` nicht kennen (Verweise im Kreis sind nicht erlaubt).
Der Bauplan zeichnet einen Pfeil von Core zu Engines: Das stimmt für den Aufruf zur Laufzeit. Die Projekt-Verweise zeigen aber in die andere Richtung.

## MVVM (Model – View – ViewModel)

- **View** (`*.xaml`): nur Aussehen. Wenig Code-Behind, nur für reine Fenster-Sachen (Titelleiste, Grösse).
- **ViewModel** (`ViewModels/*.cs`): Zustand und Befehle für die View. Kennt keine Fenster.
- **Model / Dienste** (`KiNexus.Core`): die eigentliche Logik, ohne WPF. Darum testbar.

Wir nutzen das `CommunityToolkit.Mvvm`:

- `[ObservableProperty]` auf einem Feld `_name` erzeugt die Eigenschaft `Name` mit Änderungs-Meldung.
- `[RelayCommand]` auf einer Methode `Save()` erzeugt den Befehl `SaveCommand`.

## Wichtige Klassen (Stand M0)

| Klasse | Projekt | Aufgabe |
|---|---|---|
| `App` | App | Start: Pfade, Logging, Einstellungen, Hauptfenster. Fängt unbehandelte Fehler ab. |
| `ServiceRegistration` | App | Meldet alle Dienste an (Dependency Injection). |
| `ThemeService` | App | Setzt die Akzentfarbe in den Ressourcen. |
| `DialogService`, `ShellService` | App | Öffnen Dialoge und Ordner, damit ViewModels keine Fenster kennen müssen. |
| `MainViewModel` | App | Tabs, Kopfleiste, Fensterzustand speichern. |
| `ChatTabViewModel` | App | Chat-Tab mit Stil (Terminal, Simpel, Gespräch). |
| `SettingsViewModel` | App | Einstellungs-Dialog mit Prüfung der Eingaben. |
| `NativeWindow` | App | Windows-Funktionen für die dunkle Titelleiste. |
| `AppPaths` | Core | Findet den Projektordner und kennt alle festen Pfade. |
| `AppSettings` | Core | Alle Einstellungen als Klassen mit Standardwerten. |
| `SettingsService` | Core | Lädt und speichert `data/settings.json`. |
| `SettingsValidator` | Core | Ersetzt ungültige Werte durch Standards. |
| `HexColor` | Core | Farbe `#RRGGBB` lesen und dämpfen, ohne WPF. |
| `LocalPort` | Engines | Prüft, ob ein Port auf 127.0.0.1 frei ist. |

## Einstellungen (`data/settings.json`)

Alle Schlüssel sind Englisch in camelCase. Fehlt ein Wert, gilt der Standard.

| Schlüssel | Standard | Bedeutung |
|---|---|---|
| `schemaVersion` | `1` | Version des Formats (für spätere Umstellungen) |
| `paths.models` | `models` | Ordner für Grundmodelle |
| `paths.engines` | `engines` | Ordner für llama.cpp, Piper usw. |
| `paths.openWebUiData` | `%USERPROFILE%\ki\open-webui` | Daten von Open WebUI |
| `ports.llamaServer` | `8090` | Port von llama-server |
| `ports.ollama` | `11434` | Port von Ollama |
| `ports.openWebUi` | `8080` | Port von Open WebUI |
| `engineOrder` | `llamaCppFirst` | oder `ollamaFirst` |
| `defaultKiId` | `null` | Standard-KI (ab M1) |
| `defaultChatStyle` | `simple` | `terminal`, `simple` oder `conversation` |
| `accentColor` | `#00E5FF` | Akzentfarbe |
| `voice.microphone` | `null` | Mikrofon (null = Windows-Standard) |
| `voice.voice` | `null` | Piper-Stimme |
| `voice.speechRecognitionDevice` | `cpu` | `cpu` oder `gpu` |
| `voice.pushToTalkKey` | `Space` | Taste für Push-to-Talk |
| `backgroundTasks.generateChatTitles` | `true` | Chat-Titel vom Modell erzeugen |
| `openWebUi.stopOnExit` | `true` | Open WebUI beim Schliessen beenden |
| `openWebUi.pinnedVersion` | `null` | feste Version statt `@latest` |
| `window.*` | – | Lage, Grösse, maximiert, letzter Tab |

Regeln:

- **Relative Pfade** gelten ab dem Projektordner. Umgebungsvariablen wie `%USERPROFILE%` sind erlaubt.
- **Der Daten-Ordner ist fest** (`data/`). Denn dort liegt `settings.json` selbst. Ein einstellbarer Daten-Ordner hätte ein Henne-Ei-Problem.
- **Speichern ist sicher:** Die App schreibt erst `settings.json.tmp` und ersetzt dann die echte Datei. Bei einem Absturz ist die Datei nie halb geschrieben.
- **Kaputte Datei:** Sie wird als `settings.json.kaputt-<Datum>` gesichert, und die Standardwerte gelten.
- **Ungültige Werte** (z.B. Port 80 oder Farbe "blau") ersetzt der `SettingsValidator` durch den Standard und schreibt das ins Log.

## Theme und Akzentfarbe

- `Themes/Colors.xaml` – alle Farben aus dem Bauplan (Kapitel 7)
- `Themes/Typography.xaml` – Schriften und Grössen
- `Themes/Controls.xaml` – Stile für Knöpfe, Felder, Tabs, Schalter usw.
- `Glyphs.cs` – Symbole aus der Schrift „Segoe Fluent Icons“

Die Akzentfarbe ist änderbar. Darum verweisen alle Akzent-Stellen mit `DynamicResource` (nicht `StaticResource`) auf `Brush.Accent`, `Brush.AccentDim`, `Brush.AccentSoft`, `Effect.Glow` und `Effect.GlowSoft`. Der `ThemeService` ersetzt diese Einträge, dann ändert sich die ganze Oberfläche sofort.

Für das Standard-Cyan nimmt die App als gedämpfte Farbe genau `#0FA3B8` aus dem Design. Für andere Farben rechnet sie 72 % Helligkeit.

## Eigene Titelleiste

Das Fenster nutzt `WindowChrome`. Windows zeichnet dann keinen eigenen Rahmen, die App zeichnet die Kopfleiste selbst.

- Leere Stellen der Kopfleiste kann man ziehen (Fenster verschieben). Doppelklick maximiert.
- Knöpfe in der Kopfleiste brauchen `WindowChrome.IsHitTestVisibleInChrome="True"`, sonst reagieren sie nicht.
- Unter Windows 11 setzt `NativeWindow.ApplyDarkFrame` runde Ecken und einen feinen, dunklen Rand.
- Ein maximiertes Fenster ragt ein paar Pixel über den Bildschirm. `NativeWindow.GetMaximizedOverhang` rechnet diesen Rand aus, die App zieht ihn innen wieder ab.

## Logging

- Serilog schreibt nach `data/logs/ki-nexus-JJJJMMTT.log`, eine Datei pro Tag. Die letzten 14 Tage bleiben.
- Im Code nutzt man `ILogger<Klasse>` (Microsoft-Interface). Serilog steckt nur in der App. So hängt `Core` nicht an Serilog.
- Debug-Build: ab Stufe `Debug`. Release-Build: ab Stufe `Information`.

## Fehlerbehandlung

- Fehler in der Oberfläche: Die App schreibt sie ins Log, zeigt eine Meldung und läuft weiter.
- Fehler beim Start: Meldung, Log-Eintrag, die App beendet sich mit Code 1.
- Fehler in Hintergrund-Aufgaben: werden geloggt.

## Tests

- xUnit v3 auf der neuen **Microsoft Testing Platform**. Das ist in `global.json` eingeschaltet (`"test": { "runner": "Microsoft.Testing.Platform" }`).
- Ausführen: `dotnet test --solution KI-Nexus.sln`
- Getestet wird nur die Logik (`Core`, `Engines`). Die Oberfläche prüfst du von Hand mit den Abnahme-Punkten.

## Versionen (geprüft am 30.10.2026)

Kapitel 12, Punkt 10 im Bauplan.

| Was | Version |
|---|---|
| .NET SDK | 10.0.400 (Runtime 10.0.11) |
| CommunityToolkit.Mvvm | 8.4.2 |
| Microsoft.Extensions.DependencyInjection | 10.0.14 |
| Microsoft.Extensions.Logging.Abstractions | 10.0.14 |
| Serilog | 4.4.0 |
| Serilog.Extensions.Logging | 10.0.0 |
| Serilog.Sinks.File | 7.0.0 |
| xunit.v3 | 4.0.2 |

Alle Versionen stehen an einem Ort: `Directory.Packages.props` (Central Package Management).
Gemeinsame Einstellungen aller Projekte (z.B. `net10.0-windows`, Version der App) stehen in `Directory.Build.props`.

`Microsoft.NET.Test.Sdk` und `xunit.runner.visualstudio` brauchen wir nicht mehr: Mit der Microsoft Testing Platform sind die Testprojekte eigene Programme.
