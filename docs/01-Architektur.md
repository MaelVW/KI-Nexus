# 01 – Architektur

KI-Nexus ist in drei Schichten geteilt: Oberfläche, Logik und Engines.
Jede Schicht kennt nur die Schicht darunter. So kann man Teile austauschen, ohne alles umzubauen.
Hier steht, welche Klasse was macht und warum es so gebaut ist.

## Schichten

```
┌──────────── KiNexus.App (WPF) ──────────────────┐
│ Views (XAML) + ViewModels (MVVM)                │
│ Theme, Fenster, Dialoge, Test-Feld              │
└──────────────────┬──────────────────────────────┘
                   │ nutzt
┌──────────── KiNexus.Core ───────────────────────┐
│ Einstellungen, Pfade, KI-Profile (KiRegistry)   │
│ EngineManager + Schnittstelle IInferenceBackend │
│ ProcessSupervisor (Job Object), ResourceMonitor │
│ GgufReader, ModelLibrary, Hardlinks             │
└──────────────────▲──────────────────────────────┘
                   │ erfüllt die Schnittstelle
┌──────────── KiNexus.Engines ────────────────────┐
│ LlamaCppBackend  ──► llama-server.exe (:8090)   │
│ OllamaBackend    ──► Ollama           (:11434)  │
│ OllamaImporter (Hardlinks), LocalPort           │
└─────────────────────────────────────────────────┘
```

**Projekt-Verweise:**

- `KiNexus.App` → `KiNexus.Core` und `KiNexus.Engines`
- `KiNexus.Engines` → `KiNexus.Core`
- `KiNexus.Core` → nichts (nur das Logging-Interface von Microsoft)

**Warum verweist Engines auf Core und nicht umgekehrt?**
Die Schnittstelle `IInferenceBackend` und die Datenmodelle (z.B. `KiProfile`) gehören zur Logik. Darum liegen sie in `Core`.
Die Engines *erfüllen* diese Schnittstelle. Würde Core auf Engines verweisen, könnte Engines `KiProfile` nicht kennen (Verweise im Kreis sind nicht erlaubt).
Der Bauplan zeichnet einen Pfeil von Core zu Engines: Das stimmt für den Aufruf zur Laufzeit. Die Projekt-Verweise zeigen aber in die andere Richtung.

## MVVM (Model – View – ViewModel)

- **View** (`*.xaml`): nur Aussehen. Wenig Code-Behind, nur für reine Fenster-Sachen (Titelleiste, Grösse).
- **ViewModel** (`ViewModels/*.cs`): Zustand und Befehle für die View. Kennt keine Fenster.
- **Model / Dienste** (`KiNexus.Core`): die eigentliche Logik, ohne WPF. Darum testbar.

Wir nutzen das `CommunityToolkit.Mvvm`:

- `[ObservableProperty]` auf einem Feld `_name` erzeugt die Eigenschaft `Name` mit Änderungs-Meldung.
- `[RelayCommand]` auf einer Methode `Save()` erzeugt den Befehl `SaveCommand`.

## Wichtige Klassen (Stand M4)

### Neu in M4: Gespräch-Stil

| Klasse / Datei | Projekt | Aufgabe |
|---|---|---|
| `python/voice/worker.py` | Python | Mikrofon, Sprechpausen (Silero-VAD), Whisper, Piper. Redet über JSON Lines mit der App. |
| `VoiceWorker` | Engines | Startet den Worker (über den `ProcessSupervisor`), schickt Befehle, meldet Ereignisse. |
| `VoiceProtocol` | Engines | Befehle bauen und Ereignisse lesen (JSON Lines). |
| `VoiceInstallation` | Engines | Wo Python, Whisper-Modelle und Stimmen liegen. Sagt, was zum Einrichten fehlt. |
| `PcmPlayer` | Engines | Spielt die Stimme ab (NAudio) und misst die Lautstärke für den Orb. |
| `SpeechText` | Core | Markdown vorlesbar machen, Mitschrift aufräumen („ß“ → „ss“), Whisper-Erfindungen erkennen. |
| `SentenceSplitter` | Core | Teilt die Antwort beim Streamen in Sätze. Codeblöcke werden nicht vorgelesen. |
| `ChatService.SendAsync(…, systemPromptSuffix)` | Core | Zusatz zum Systemprompt nur für eine Frage („Antworte kurz …“). |
| `ConversationViewModel` | App | Ablauf des Gesprächs: Orb-Zustand, Push-to-Talk, Freihändig, Frage, satzweise Stimme, Unterbrechen. |
| `ConversationView` | App | Orb (mit Animationen), Mitschrift, Taste und Maus für Push-to-Talk. |

### Neu in M3: Create-Hub

| Klasse | Projekt | Aufgabe |
|---|---|---|
| `FitEstimator`, `FitEstimate` | Core | Die Ampel: Passt ein Modell in VRAM, braucht es RAM oder ist es zu gross? Mit Erklärung. |
| `KiIds` | Core | Macht aus einem Namen eine Id (= Dateiname in `kis/`), z.B. „Schul KI“ → `schul-ki`. |
| `KiTemplates` | Core | Vorlagen für Systemprompt, Parameter und Kontextlänge im Assistenten. |
| `KiRegistry` | Core | Neu: `SaveAsync`, `DeleteAsync` und das Ereignis `Changed` (die KI-Auswahl in der Kopfleiste aktualisiert sich). |
| `ModelImporter` | Core | GGUF-Datei prüfen, kopieren oder verschieben (mit Fortschritt), `model.json` schreiben. Nie aus Ollamas Ordner verschieben. |
| `ModelLibrary` | Core | Neu: Ordner anlegen, `model.json` aus dem GGUF-Kopf schreiben, Modell löschen (nur innerhalb von `models/`). |
| `SecretStore` | Core | Speichert den Hugging-Face-Token verschlüsselt mit Windows DPAPI. |
| `EngineManager.TestLoadAsync` | Core | Ladetest: kurz laden, sofort entladen. |
| `HuggingFaceClient` | Engines | Suche, Dateiliste und Download bei huggingface.co. Token nur, wenn gespeichert. |
| `HuggingFaceProtocol` | Engines | Antworten lesen, Dateien nach Quantisierung gruppieren, Empfehlung (Q4_K_M) markieren. |
| `ModelDownloader`, `DownloadJob` | Engines | Downloads mit Pause/Fortsetzen (HTTP-Range), Hänger-Schutz, SHA-256-Prüfung, Neustart-fest. |
| `OllamaBackend.DeleteModelAsync` | Engines | Ollama-Modell über die API löschen. |
| `CreateHubViewModel` | App | Der Tab: Bereiche, Meldungs-Balken, Ladetest, Ampel-Berechnung für alle Bereiche. |
| `CreateHubOverviewViewModel` | App | Übersicht: KIs, Grundmodelle, Ollama-Modelle, Löschen. |
| `NewKiWizardViewModel` | App | Assistent „Neue KI“ in 4 Schritten. |
| `ImportViewModel` | App | GGUF-Import und Übernahme aus Ollama. |
| `HuggingFaceViewModel`, `DownloadItemViewModel` | App | Suche, Dateien mit Ampel, Downloads, Token. |
| `ChoiceDialog` | App | Rückfrage mit zwei Wegen (Kopieren / Verschieben). |

Das Hauptfenster gibt dem Create-Hub über `IChatShell.OpenChatWith(ki)` die Möglichkeit, eine KI zu wählen und einen Test-Chat zu öffnen.

### Neu in M2: Chat

| Klasse | Projekt | Aufgabe |
|---|---|---|
| `ChatRepository` | Core | Speichert Chats und Nachrichten in `data/chats.db` (SQLite), mit Migrationen. |
| `ChatService` | Core | Steuert eine Frage: speichern, Kontext prüfen, streamen, Antwort speichern. Neu erzeugen, bearbeiten, Titel. |
| `ContextBudget` | Core | Passt den Verlauf in den Kontext. Die ältesten Nachrichten fallen weg, Systemprompt bleibt. |
| `ChatTitles` | Core | Titel aus den ersten Wörtern oder vom Modell (aufgeräumt, „ß“ → „ss“). |
| `TerminalCommands`, `InputHistory` | Core | Befehle des Terminals erkennen, Tab-Vervollständigung, frühere Eingaben. |
| `AttachmentStore` | Core | Bilder nach `data/attachments/<chat-id>/` kopieren und beim Löschen entfernen. |
| `ChatSessionViewModel` | App | Der offene Chat. Simpel und Terminal zeigen beide **diesen** einen Chat, darum bleibt der Verlauf beim Stilwechsel gleich. |
| `MessageViewModel` | App | Eine Nachricht für die Anzeige (Text, Gedanken, Statistik, Bewertung). |
| `ChatListViewModel` | App | Seitenleiste: Chats laden, suchen, neu, umbenennen, löschen. |
| `TerminalViewModel` | App | Terminal-Stil: Befehle, Schreibmaschinen-Effekt, Eingabe-Verlauf. |
| `KiReadinessViewModel` | App | Hinweis-Balken „Modell fehlt“ / „Ollama läuft nicht“ mit Import aus Ollama. |
| `MarkdownRenderer`, `MarkdownView` | App | Markdown (Markdig) → WPF-Dokument. Zwei Varianten: Blase und Terminal. |
| `CodeBlockView`, `DarkSyntaxColorizer` | App | Codeblock mit Syntax-Farben (AvalonEdit) und Kopier-Knopf. Hellt zu dunkle Farben auf. |
| `IChatShell` | App | Was der Chat vom Hauptfenster braucht (KI wählen, Stil wechseln, entladen). |

### Ablauf einer Frage im Chat

```
Simpel/Terminal ── AskAsync ──► ChatSessionViewModel
                                  │ 1. Chat anlegen (falls neu), alte Titel-Erzeugung abbrechen
                                  ▼
                               ChatService.SendAsync
                                  │ 2. Frage speichern (Bilder kopieren)        → UserMessageSaved
                                  │ 3. ContextBudget: Systemprompt + Verlauf     → ContextPrepared
                                  │ 4. EngineManager.StreamAsync                 → AnswerChunk, AnswerChunk, …
                                  │ 5. Antwort speichern (auch abgebrochen)      → AnswerSaved
                                  ▼
                               Titel nach der ersten Antwort (im Hintergrund, abbrechbar)
```

Der `EngineManager` kennt alle laufenden Anfragen. „Entladen“ bricht alle ab, auch eine Titel-Erzeugung.

### Aus M0 und M1

| Klasse | Projekt | Aufgabe |
|---|---|---|
| `App` | App | Start: Pfade, Logging, Einstellungen, KIs, Hauptfenster. Fängt unbehandelte Fehler ab. |
| `ServiceRegistration` | App | Meldet alle Dienste an (Dependency Injection). |
| `ThemeService` | App | Setzt die Akzentfarbe in den Ressourcen. |
| `DialogService`, `ShellService` | App | Öffnen Dialoge und Ordner, damit ViewModels keine Fenster kennen müssen. |
| `MainViewModel` | App | Tabs, KI-Auswahl, Engine-Status, Ressourcen-Anzeige (alle 2 s), Entladen, Fensterzustand. |
| `ChatTabViewModel` | App | Chat-Tab mit Stil (Terminal, Simpel, Gespräch). |
| `SettingsViewModel` | App | Einstellungs-Dialog mit Prüfung der Eingaben. |
| `NativeWindow` | App | Windows-Funktionen für die dunkle Titelleiste. |
| `AppPaths` | Core | Findet den Projektordner und kennt alle festen Pfade. |
| `AppSettings`, `SettingsService`, `SettingsValidator` | Core | Einstellungen laden, prüfen, speichern. |
| `JsonDefaults` | Core | Gemeinsame JSON-Optionen für alle Dateien. |
| `KiProfile`, `KiRegistry` | Core | KI-Profile aus `kis/` lesen. |
| `IInferenceBackend` | Core | Gemeinsame Schnittstelle aller Engines. |
| `EngineManager` | Core | Lädt bei Bedarf, immer nur ein Modell, entlädt nach „Geladen halten“. |
| `ContextBudget` | Core | Systemprompt + Verlauf + Frage, gekürzt auf den Kontext. Sonst nichts (Lektion 5). |
| `ProcessSupervisor`, `JobObject` | Core | Startet Hilfsprozesse. Stürzt die App ab, enden sie mit. |
| `ResourceMonitor` | Core | Misst VRAM (NVML) und RAM (Windows). |
| `GgufReader` | Core | Liest Metadaten aus dem Kopf einer GGUF-Datei. |
| `ModelLibrary` | Core | Kennt die Modelle in `models/` (über `model.json`). |
| `FileLinks` | Core | Hardlinks anlegen und prüfen. |
| `HexColor` | Core | Farbe `#RRGGBB` lesen und dämpfen, ohne WPF. |
| `LlamaCppBackend`, `LlamaServerArguments`, `LlamaCppProtocol` | Engines | llama-server starten, Befehlszeile bauen, mit der API sprechen. |
| `OllamaBackend`, `OllamaProtocol` | Engines | Mit Ollama sprechen. |
| `OllamaModelStore`, `OllamaImporter` | Engines | Ollama-Manifeste lesen, Modelle per Hardlink übernehmen. |
| `LocalPort` | Engines | Prüft, ob schon ein Programm auf einem Port wartet. |

## Die Schnittstelle `IInferenceBackend`

```csharp
public interface IInferenceBackend
{
    EngineKind Kind { get; }
    string Name { get; }
    Task<BackendStatus> GetStatusAsync(CancellationToken ct);
    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct);
    Task LoadAsync(KiProfile ki, IProgress<string>? progress, CancellationToken ct);
    Task UnloadAsync(CancellationToken ct);
    IAsyncEnumerable<ChatChunk> ChatStreamAsync(ChatRequest request, CancellationToken ct);
}
```

Drei Änderungen gegenüber dem Vorschlag im Bauplan:

- **`Kind`**: Der `EngineManager` muss zu einer KI die passende Engine finden (`engine: llamacpp` → llama.cpp).
- **`IProgress<string>` bei `LoadAsync`**: Laden dauert bis zu 20 Sekunden. So kann die App anzeigen, was gerade passiert („wird geladen … 12 s“, „Aufwärmen …“).
- **`SupportsImagesAsync`** (M2): Der +-Knopf für Bilder ist nur aktiv, wenn die KI Bilder versteht.

`ChatMessage` hat seit M2 eine Liste von Bildern, `ChatRequest` eine optionale maximale Antwortlänge (für kurze Hintergrund-Aufgaben wie den Titel), und `ChatStats` meldet zusätzlich `ContextTokens` (alles, was nach der Antwort im Kontext steht).

`ChatChunk` enthält wie geplant: Text, Gedanken, ein Fertig-Flag und am Ende die Statistik (`ChatStats`: Token, Tempo, Dauer, Engine).

## Ablauf einer Frage

```
ChatService ── StreamAsync ──► EngineManager
                                 │ Ist das richtige Modell geladen? Sonst: altes entladen, neues laden + aufwärmen
                                 ▼
                              LlamaCppBackend / OllamaBackend ── HTTP-Stream ──► llama-server / Ollama
                                 │
                                 ▼ ChatChunk für ChatChunk (Text, Gedanken, am Ende Statistik)
                              Chat zeigt laufend an
```

Abbrechen (Knopf „Stopp“ oder `Esc`) bricht die HTTP-Verbindung ab. llama-server und Ollama hören dann sofort auf zu rechnen.

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
| `defaultKiId` | `null` | Standard-KI. `null` = die erste in der Liste |
| `defaultChatStyle` | `simple` | `terminal`, `simple` oder `conversation` |
| `accentColor` | `#00E5FF` | Akzentfarbe |
| `voice.microphone` | `null` | Mikrofon (null = Windows-Standard) |
| `voice.voice` | `null` | Piper-Stimme |
| `voice.speechRecognitionDevice` | `cpu` | `cpu` oder `gpu` |
| `voice.pushToTalkKey` | `Space` | Taste für Push-to-Talk |
| `backgroundTasks.generateChatTitles` | `true` | Chat-Titel vom Modell erzeugen |
| `openWebUi.stopOnExit` | `true` | Open WebUI beim Schliessen beenden |
| `openWebUi.pinnedVersion` | `null` | feste Version statt `@latest` |
| `chat.showThinking` | `true` | Gedanken anzeigen |
| `chat.isSidebarOpen` | `true` | Seitenleiste offen |
| `chat.lastChatId` | `null` | dieser Chat öffnet sich beim Start |
| `window.*` | – | Lage, Grösse, maximiert, letzter Tab |

Regeln:

- **Relative Pfade** gelten ab dem Projektordner. Umgebungsvariablen wie `%USERPROFILE%` sind erlaubt.
- **Ports und Pfade wirken nach einem Neustart** der App. Akzentfarbe und Standard-Stil wirken sofort.
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
| Microsoft.Extensions.TimeProvider.Testing | 10.11.0 (nur Tests: Zeit vorspulen) |
| Microsoft.Data.Sqlite | 10.0.14 (Chat-Datenbank) |
| Markdig | 1.4.2 (Markdown lesen). 1.5.1 war beim Einbau erst 2 Tage alt, darum die bewährte 1.4er-Reihe. |
| AvalonEdit | 6.3.1.120 (Codeblöcke mit Syntax-Farben) |
| System.Security.Cryptography.ProtectedData | 10.0.14 (Hugging-Face-Token mit DPAPI verschlüsseln) |
| NAudio.WinMM | 2.4.0 (Stimme abspielen). 2.4.0 ist die letzte der stabilen 2er-Reihe. Die 3er-Reihe (seit August 2026) hat sich seither mehrmals stark geändert. |
| Python (uv) | 3.12, uv 0.11.26. Pakete nur, wenn mindestens 2 Wochen alt (`exclude-newer` in `python/pyproject.toml`) |
| faster-whisper / CTranslate2 | 1.2.1 / 4.8.3 |
| piper-tts | 1.8.0 (Open Home Foundation, GPL-3.0) |
| onnxruntime / sounddevice / numpy | 1.31.0 / 0.5.6 / 2.5.4 |
| llama.cpp | b11960 = v0.8.0 (CUDA 13.4), siehe [02-Engines.md](02-Engines.md) |

Alle Versionen stehen an einem Ort: `Directory.Packages.props` (Central Package Management).
Gemeinsame Einstellungen aller Projekte (z.B. `net10.0-windows`, Version der App) stehen in `Directory.Build.props`.

`Microsoft.NET.Test.Sdk` und `xunit.runner.visualstudio` brauchen wir nicht mehr: Mit der Microsoft Testing Platform sind die Testprojekte eigene Programme.
