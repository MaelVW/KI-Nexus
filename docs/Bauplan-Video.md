# KI-Nexus – Bauplan Video (Erweiterung)

**Für:** die KI, die KI-Nexus weiterbaut
**Von:** Mael, geplant zusammen mit Claude
**Stand:** 2. November 2026 – **V0 bis V4 sind erledigt** (V2 = v1.5.0, V3 = v1.6.0, dev-Modelle = v1.7.0, V4 = v1.8.0), Ergebnisse in `docs/10-Video.md`. V4 ist noch nicht gebaut.

> Das ist eine Erweiterung zum Hauptbauplan (`KI-Nexus-Bauplan.md`). Alle Regeln aus Kapitel 1 dort gelten auch hier: Phase für Phase, vorher fragen bei Installationen und Downloads über 1 GB, alles lokal, ehrlich bleiben.

---

## 0. Worum es geht

KI-Nexus soll auch **Videomodelle** nutzen können. Zuerst LTX-2.3 von Lightricks, weil schon eine Datei davon auf dem Laptop liegt.

- **Text zu Video:** Ich beschreibe eine Szene, die App erzeugt einen kurzen Clip, mit Ton.
- **Bild zu Video:** Ich gebe ein Startbild dazu, die App macht daraus einen bewegten Clip.

Ein Videomodell ist kein Chatpartner. Es antwortet nicht mit Text, sondern liefert eine Datei. Darum bekommt es einen **eigenen Tab „Video“** und keinen Platz im Chat.

---

## 1. Befund: Was ist die Datei?

Gelesen aus dem GGUF-Kopf (Metadaten am Anfang der Datei) eines LTX-2.3-Finetunes in `models/`:

| Was | Wert |
|---|---|
| Datei | ein Finetune als GGUF, 15.9 GB |
| Herkunft | Hugging Face, ein Finetune (nachtrainierte Version) von LTX-2.3 |
| Architektur | `ltxv`, Klasse `AVTransformer3DModel` (AV = Audio + Video), `model_version` 2.3.0 |
| Grösse | 21 Milliarden Parameter, 48 Transformer-Blöcke |
| Quantisierung | Q5_K_M (Gewichte auf ca. 5–6 Bit verkleinert) |
| Text-Eingang | 3840 Kanäle → passt zum Text-Encoder **Gemma 3 12B** |
| Drin | der Kern (Transformer) und die „Connectors“ (Übersetzer zwischen Text und Kern) |
| **Nicht drin** | Text-Encoder, Video-VAE, Audio-VAE, Vocoder, Upscaler (siehe Kapitel 2) |
| Lizenz | Die Seite sagt „apache-2.0“. Das stimmt vermutlich nicht: Ein Finetune übernimmt die Lizenz vom Grundmodell, und LTX-2.3 steht unter der „LTX-2 Community License“. Für dich privat prüfen, nicht weitergeben. |

**Warum llama.cpp die Datei nicht laden kann:** GGUF ist nur das Dateiformat (wie ZIP). llama.cpp kennt nur Sprachmodelle. Für ein Videomodell braucht es eine ganz andere Engine.

---

## 2. Wie ein Videomodell arbeitet (kurz)

```
 Prompt ──► Text-Encoder ──► Kern (Diffusion) ──► Upscaler ──► Video-VAE ──► Bilder ─┐
           (Gemma 3 12B)     8–30 Schritte       (optional)    Audio-VAE    ► Ton ────┴─► MP4
                             aus Rauschen                      + Vocoder
```

1. **Text-Encoder:** Ein Sprachmodell (Gemma 3 12B) macht aus dem Prompt eine Zahlenreihe (Embeddings). Es schreibt keinen Text, es „liest“ nur.
2. **Kern (Diffusion):** Startet mit Zufallsrauschen und entfernt es Schritt für Schritt. Nach 8 bis 30 Schritten ist daraus ein **Latent** geworden (eine stark komprimierte Form von Bild und Ton).
3. **Upscaler (optional):** Verdoppelt die Auflösung im Latent. So rechnet man zuerst klein (schnell) und erst am Schluss gross.
4. **VAE (Decoder):** Macht aus dem Latent echte Bilder. Die Audio-VAE und der Vocoder machen daraus Ton.
5. **MP4:** Bilder und Ton kommen in eine Videodatei.

Unterschiede zum Chat:

- Kein Streaming Wort für Wort. Der Fortschritt ist „Schritt 5 von 8“.
- Kein Verlauf und kein Kontext. Jeder Auftrag steht für sich.
- Der **Seed** (Startzahl fürs Rauschen) bestimmt das Ergebnis: Gleicher Seed + gleicher Prompt = gleiches Video.
- Regeln von LTX: Breite und Höhe durch 32 teilbar. Anzahl Bilder = 8·n + 1 (z.B. 97 oder 121).

---

## 3. Ehrliche Grenzen auf meinem Laptop

RTX 5070 Laptop mit **8 GB VRAM**, **31.4 GB RAM**.

- Der Kern allein (15.9 GB) passt nicht in den Grafikspeicher. Ein Teil liegt im RAM und wird während dem Rechnen hin- und hergeschoben (**Offloading**). Das geht, ist aber langsam.
- Laut Anleitungen im Netz braucht LTX-2.3 ohne Tricks 32 GB VRAM und mehr. Mit GGUF und Offloading melden Leute, dass es ab 12 GB läuft, mit viel Geduld auch mit 8 GB. Gemessen hat das für meinen Laptop noch niemand.

Speicher-Rechnung (Schätzung):

| Teil | Grösse |
|---|---|
| Kern Q5_K_M (meine Datei) | 15.9 GB |
| Kern Q4_K_M / Q3_K_M (offizielles LTX-2.3) | 14.2 GB / 10.6 GB |
| Text-Encoder Gemma 3 12B, Q4 | ca. 7–8 GB |
| VAEs, Vocoder, Upscaler | ca. 1–3 GB (genau messen) |
| Windows + KI-Nexus | ca. 5–6 GB |

**Das heisst:**

- Text-Encoder und Kern dürfen **nicht gleichzeitig** im Speicher bleiben. Zuerst den Prompt lesen, dann den Text-Encoder entladen, dann den Kern laden.
- Ein geladenes Chat-Modell (llama-server) muss vorher **entladen** werden. Gleich wie beim Training.
- Realistisch sind **kurze Clips** (2–5 Sekunden), um 768×512 Pixel, eher **mehrere Minuten pro Clip**. Die genaue Zeit misst erst Phase V0.
- Laptop am Netzteil. Im Akkubetrieb bremst die Grafikkarte stark.

---

## 4. Technik-Entscheid: Welche Engine?

| Weg | Vorteil | Nachteil |
|---|---|---|
| **A: ComfyUI** als unsichtbarer Hintergrunddienst, mit dem Zusatz **ComfyUI-GGUF** | Lädt GGUF direkt, also auch meine Datei. LTX-2.3 wird unterstützt. Offloading ist eingebaut. Hat eine HTTP- und WebSocket-Schnittstelle. Grosse Community. | Fremde Software mit Zusatzmodulen (Custom Nodes = Python-Code von Dritten). Updates ändern manchmal die Workflows. |
| B: offizielle Lightricks-Pipeline (`ltx-pipelines`) als eigener Python-Worker | Passt am besten zur KI-Nexus-Bauweise (JSON Lines, wie Training und Sprache). | Lädt kein GGUF. Braucht die grossen safetensors-Dateien (bf16 = 42 GB). Passt nicht in 31 GB RAM. |
| C: Python-Bibliothek `diffusers` (`LTX2Pipeline`) | Saubere Bibliothek. | GGUF für LTX-2 ist unsicher. Gleiches Grössenproblem wie B. |

**Empfehlung: A (ComfyUI).** Nur damit läuft ein 21B-Videomodell überhaupt auf 8 GB VRAM. Das Muster kennt KI-Nexus schon von Open WebUI (M5): Ein fremder Dienst läuft unsichtbar im Hintergrund, nur auf `127.0.0.1`, mit eigenem Job Object. Der Unterschied: Diesmal baut KI-Nexus **eine eigene Oberfläche**. Die Knoten-Ansicht von ComfyUI sehe ich nie (höchstens über einen Knopf „In ComfyUI öffnen“ für Experimente).

---

## 5. Architektur

```
┌──────────────────────── KiNexus.App (WPF) ────────────────────────┐
│  Video-Tab: VideoStudioViewModel · VideoGalleryViewModel          │
└─────────────────────────────────┬─────────────────────────────────┘
┌──────────────────────── KiNexus.Core ─────────────────────────────┐
│  VideoRequest (prüft 32er-Raster, 8n+1) · VideoPresets            │
│  VideoLibrary (data/videos) · VideoModelSet (welche Dateien)      │
└─────────────────────────────────┬─────────────────────────────────┘
                                  │  IVideoBackend
┌──────────────────────── KiNexus.Engines ──────────────────────────┐
│  ComfyVideoBackend ──► ComfyUI (127.0.0.1:8188, HTTP + WebSocket) │
│  ComfyWorkflowBuilder (füllt die Workflow-Vorlagen aus)           │
└───────────────────────────────────────────────────────────────────┘
Hilfsprozess: engines/comfyui (eigene venv), gestartet vom ProcessSupervisor
```

### Schnittstelle `IVideoBackend`

Gleiche Idee wie `IInferenceBackend`: Die Oberfläche kennt nur die Schnittstelle, nicht ComfyUI.

```csharp
public interface IVideoBackend
{
    string Name { get; }
    Task<VideoBackendStatus> GetStatusAsync(CancellationToken ct);
    Task StartAsync(CancellationToken ct);       // Dienst starten, warten bis er antwortet
    Task StopAsync(CancellationToken ct);
    IAsyncEnumerable<VideoProgress> GenerateAsync(VideoRequest request, CancellationToken ct);
    Task FreeMemoryAsync(CancellationToken ct); // Modelle aus VRAM und RAM werfen
}

public sealed record VideoRequest(
    string Prompt,
    string? NegativePrompt,
    string? StartImagePath,   // null = Text zu Video
    int Width, int Height,    // durch 32 teilbar
    int Frames,               // 8·n + 1
    int FrameRate,
    int Steps,
    double Guidance,          // CFG: wie streng das Modell dem Prompt folgt
    long Seed,
    bool WithAudio,
    VideoModelSet Models);
```

`VideoProgress` funktioniert wie die Trainings-Ereignisse aus M8. Es gibt diese Fälle:

- `VideoStage`: Phase, z.B. „Text lesen“, „Entrauschen“, „Video bauen“
- `VideoStep`: Schritt 5 von 8, mit Dauer
- `VideoDone`: Pfad zur MP4 und die Statistik
- `VideoFailed`: Fehlermeldung
- `VideoCancelled`: abgebrochen

### So redet KI-Nexus mit ComfyUI

Die genauen Namen und Formate musst du an der installierten Version prüfen:

| Zweck | Aufruf |
|---|---|
| Läuft der Dienst? | `GET /system_stats` |
| Auftrag senden | `POST /prompt` mit `{ "prompt": <Workflow>, "client_id": "<GUID>" }` → `prompt_id` |
| Fortschritt | WebSocket `ws://127.0.0.1:8188/ws?clientId=<GUID>`: Meldungen `executing`, `progress` (Schritt/Total), `execution_success`, `execution_error` |
| Ergebnis | `GET /history/<prompt_id>` → Dateiname im Ausgabeordner |
| Abbrechen | `POST /interrupt` |
| Speicher frei | `POST /free` mit `{ "unload_models": true, "free_memory": true }` |
| Startbild | `POST /upload/image` |

**Workflows:** ComfyUI arbeitet mit „Workflows“ (ein Graph aus Knoten als JSON). KI-Nexus bringt eigene Vorlagen mit, z.B. `ltx23-text-zu-video.json` und `ltx23-bild-zu-video.json`, im API-Format.

- Der `ComfyWorkflowBuilder` setzt Prompt, Seed, Grösse und so weiter ein.
- Er findet die Knoten über ihren Titel (z.B. `KN_Prompt`, `KN_Seed`), nicht über die Nummer. So überlebt die Vorlage kleine Änderungen.
- Das ist reine Logik. Dafür gibt es Unit-Tests.

**Start des Dienstes** (Flags an der Version prüfen):

```
python main.py --listen 127.0.0.1 --port 8188 --disable-auto-launch
               --extra-model-paths-config engines/comfyui/kinexus-paths.yaml
               --output-directory data/videos/roh
               --offline                    (keine Cloud-Knoten, Oberfläche ohne Internet)
               (V0 lief ohne VRAM-Flags: ComfyUI 0.43 lädt dynamisch, --lowvram wirkt dann nicht)
```

- KI-Nexus startet ComfyUI erst beim ersten Video, nicht beim App-Start.
- Beim Schliessen von KI-Nexus beendet das Job Object den Dienst.
- Einstellung: Dienst nach X Minuten ohne Auftrag beenden (Standard 10).

---

## 6. Ordner und Dateien

```
engines/comfyui/                 ComfyUI, feste Version (Release-ZIP, nicht git pull)
engines/comfyui/.venv/           eigene Python-Umgebung (uv), Pakete in python/video/pyproject.toml
engines/comfyui/custom_nodes/    nur ComfyUI-GGUF (und was V0 sonst wirklich braucht), feste Commits
engines/comfyui/kinexus-paths.yaml   sagt ComfyUI, wo die Modelle liegen
models/video/diffusion_models/   Kern-GGUF (V3: dazu model.json)
models/video/text_encoders/      Gemma 3 12B
models/video/vae/                Video-VAE, Audio-VAE
models/video/latent_upscale_models/
data/videos/                     Ergebnisse: 2026-11-02_1530_seed12345.mp4 + .json
scripts/setup-video.ps1          richtet alles ein (fragt vor Downloads)
src/KiNexus.Engines/Video/Workflows/*.json   Workflow-Vorlagen (V0: python/video/workflows/)
docs/10-Video.md                 Doku und Messwerte (ab V0)
```

- Die Modelle bleiben im KI-Nexus-Ordner `models/`. ComfyUI bekommt nur die Pfade.
- PyTorch liegt dank dem Training schon im uv-Cache. Die neue venv lädt es darum vermutlich nicht nochmals herunter (prüfen).
- Neben jedem Video liegt eine JSON-Datei mit Prompt, Seed, Einstellungen, Dauer und Modell. So kann ich ein Video jederzeit nachbauen.

---

## 7. Video-Tab (Oberfläche)

**Links: Auftrag**

- Prompt, mehrzeilig. Darunter ein Tipp: „Beschreib Szene, Bewegung, Kamera und Ton. Auf Englisch klappt es am besten.“
- Startbild (optional): Drag & Drop oder Knopf. Mit Bild = Bild zu Video.
- Negativ-Prompt, aufklappbar (was nicht vorkommen soll).
- **Vorlage:**
  - „Schnell“: distilled, 8 Schritte, CFG 1
  - „Qualität“: 20–30 Schritte
- **Format:** Quer 768×512, Hochkant 512×768, Klein 512×320 (für Tests).
- **Länge** in Sekunden. Die App rechnet auf 8·n + 1 Bilder um, z.B. 4 s bei 24 fps → 97 Bilder.
- Ton an/aus.
- Seed: Zahlenfeld mit Würfel-Knopf (neuer Zufall) und Schloss (gleich lassen).
- **Speicher-Ampel** wie im Create-Hub: grün = passt, gelb = langsam, rot = zu gross (dann kleineres Format oder kürzer vorschlagen).

**Mitte: Fortschritt**

- Phase, Balken („Entrauschen 5/8“), verstrichene Zeit und Restzeit.
- Knopf **Abbrechen**: `POST /interrupt`. Das Video wird verworfen.

**Rechts: Galerie**

- Vorschaubilder der letzten Videos, neueste oben.
- Player: WPF `MediaElement`. Abspielen, Pause, Ton.
- Knöpfe:
  - Ordner öffnen
  - Prompt übernehmen
  - Nochmal mit gleichem Seed
  - Nochmal mit neuem Seed
  - In den Papierkorb

**Später (V4):**

- Knopf **„Prompt verbessern“:** Eine meiner KIs macht aus einer kurzen Idee einen ausführlichen Video-Prompt. Danach entlädt die App das Chat-Modell, dann startet das Video. Beides gleichzeitig passt nicht in 8 GB.
- **Warteschlange:** mehrere Aufträge nacheinander, z.B. über Nacht.

---

## 8. Abläufe und Ressourcen

1. **Vor dem Start:**
   - Ist ein Chat-Modell geladen? → „Chat-Modell entladen, damit das Video Platz hat?“ (wie beim Training).
   - Läuft ein Training? → Video ablehnen.
   - Akku? → Hinweis.
2. **Während dem Video:**
   - Kopfleiste zeigt „Video läuft“ und die echte VRAM/RAM-Nutzung.
   - Chatten geht in dieser Zeit nicht. Die App sagt das klar.
3. **Danach:** `POST /free`. So hat der Chat wieder Platz.
   - Einstellung „Videomodell nach dem Erzeugen entladen“, Standard ja.
4. **Fehler:**
   - Zu wenig Speicher (out of memory) → verständliche Meldung mit Vorschlag: kleiner, kürzer, „Schnell“-Vorlage.
   - Das ComfyUI-Log kommt nach `data/logs/video.log`.

---

## 9. Sicherheit und Regeln

- ComfyUI hört nur auf `127.0.0.1`.
- **Kein ComfyUI-Manager.** Er lädt Listen aus dem Internet und installiert fremden Code per Klick.
- Custom Nodes nur aus einer festen Liste mit festem Commit. Installiert werden sie nur von `setup-video.ps1`.
- **Cloud-Knoten aus** (`--offline`; das ältere `--disable-api-nodes` ist in ComfyUI 0.43 nur noch ein Alias dafür). Sonst gibt es in ComfyUI Knoten, die Aufträge an bezahlte Cloud-Dienste schicken. Das widerspricht „keine Cloud-KI“.
- Nur die eigenen Workflow-Vorlagen der App, keine Workflows aus dem Internet. Ein fremder Workflow kann fremde Custom Nodes verlangen, also fremden Code.
- **Nur `.gguf` und `.safetensors` laden.** Keine `.ckpt`, `.pt` oder `.bin`: Diese Formate können beim Laden Code ausführen.
- Ins Internet geht die App weiterhin nur für Hugging Face (Modelle) und für Engine-Updates, wenn ich sie auslöse.
- **Bild zu Video mit echten Personen:** nur mit eigenen Bildern oder mit Einverständnis der Person. Gefälschte Videos von echten Menschen verletzen deren Persönlichkeitsrechte und können strafbar sein.

---

## 10. Phasen

| # | Inhalt | Abnahme |
|---|---|---|
| **V0** ✅ | **Machbarkeitstest, noch ohne App.** ComfyUI (feste Version) und ComfyUI-GGUF in `engines/comfyui` einrichten. Fehlende Dateien laden: Gemma 3 12B, VAEs, Upscaler. **Vorher fragen, das sind über 10 GB.** Ein Clip 768×512, 4 s, einmal mit dem offiziellen LTX-2.3 (Q4_K_M) und einmal mit meiner Datei. Messen: Zeit, höchstes VRAM und RAM, Auslagerungsdatei. | Ein MP4 mit Ton liegt vor. Die Messwerte stehen in `docs/10-Video.md`. **Läuft es nicht oder dauert ein Clip über 15 Minuten: stoppen und mit Mael entscheiden.** — **Erledigt 2.11.2026:** 768×512, 3.9 s mit Ton in ca. 2½ min. Nur mit dem offiziellen LTX-2.3 (distilled 1.1, Q4_K_M), ohne Upscaler. |
| V1 ✅ | **Engine:** `setup-video.ps1`, `ComfyVideoBackend`: Start/Stopp, Bereitschaft, Auftrag, Fortschritt über WebSocket, Abbrechen, Speicher freigeben. `ComfyWorkflowBuilder`, `VideoRequest` mit Prüfungen. Unit-Tests. | Tests laufen durch. Ein Testlauf erzeugt ein Video nur über die Schnittstelle. — **Erledigt 2.11.2026:** 385 Tests grün. Der echte Testlauf erzeugt ein Video über `IVideoBackend` und bricht ein zweites ab (`docs/10-Video.md`). |
| V2 ✅ | **Video-Tab:** Auftrag, Vorlagen, Speicher-Ampel, Fortschritt, Abbrechen, Player, Galerie, Chat-Modell entladen | Ich erzeuge im Tab einen Clip, breche einen zweiten ab und spiele den ersten ab. Danach chatte ich wieder normal. — **Erledigt 2.11.2026 (v1.5.0):** alle vier Punkte in der App geprüft. Es gibt nur die Vorlage „Schnell“ (distilled), „Qualität“ braucht das dev-Modell. Den Negativ-Prompt gibt es noch nicht in der Oberfläche, weil er bei CFG 1 nicht wirkt. |
| V3 ✅ | **Create-Hub:** Videomodelle als eigene Art statt nur Warnung. Statt „Neue KI daraus“ gibt es den Knopf „Im Video-Tab nutzen“. Prüfen, ob die Zusatzdateien da sind, und sie bei Bedarf laden. Die Hugging-Face-Suche zeigt „Videomodell, nutzbar im Video-Tab“. | Ein Videomodell aus der Hugging-Face-Suche ist nach dem Download im Video-Tab nutzbar. — **Erledigt 2.11.2026 (v1.6.0):** Q2_K von Unsloth gesucht, geladen und im Video-Tab genutzt. Zusatzdateien lädt das Skript `setup-video.ps1 -Companions` in einem eigenen Fenster, nicht der Downloader der App. Der ist fest auf Modell-Ordner mit model.json gebaut. |
| V4 ✅ | **Extras:** Bild zu Video, „Prompt verbessern“ mit einer KI, Warteschlange | Ich mache aus einem eigenen Foto einen Clip. Eine KI verbessert meinen Prompt. — **Erledigt 2.11.2026 (v1.8.0):** Bild zu Video, Prompt verbessern und Warteschlange in der App geprüft. Als Foto ein eigenes Testbild, ein echtes Foto von Mael steht noch aus. |

**Warum V0 zuerst:** Ob mein Laptop das überhaupt schafft, ist die grösste Unsicherheit. Fällt V0 durch, sparen wir uns V1 bis V4 oder warten auf den Server (M9).

---

## 11. Das musst du selbst prüfen

> Die Punkte 1, 2, 5, 7 und 8 sind seit V0 beantwortet: siehe `docs/10-Video.md`.

1. **ComfyUI:**
   - Welche Version unterstützt LTX-2.3 mit Ton?
   - Lädt ComfyUI-GGUF meine Datei, in der die Connectors schon drin sind? Andere GGUF-Pakete (z.B. von Unsloth) liefern sie als eigene Datei.
2. **Text-Encoder:** Gemma 3 12B als GGUF oder als safetensors? Braucht es zusätzlich eine „text projection“-Datei? Das hängt vom Paket ab.
3. **Mein Finetune:** Ist es eine „dev“-Version (20 und mehr Schritte) oder „distilled“ (8 Schritte)? Steht vermutlich auf der Modellseite.
4. **Neuere Modelle:** Es gibt schon LTX-2.5 (mit Gemma-4-Encoder). Ist es besser oder kleiner? Gibt es eine GGUF-Version?
5. **Flags:** Die genauen Namen und die Wirkung von `--disable-api-nodes`, `--reserve-vram`, `--lowvram` und `--cache-none` sowie das WebSocket-Format.
6. **Player:** Spielt `MediaElement` das MP4 von ComfyUI ab (H.264 + AAC)? Wenn nicht: Ausgabe-Codec in der Vorlage anpassen. Erst als letzte Lösung eine Player-Bibliothek dazunehmen.
7. **RAM:** Reichen 31.4 GB? Wie gross muss die Auslagerungsdatei sein?
8. **Lizenzen:** LTX-2 Community License und Gemma-Lizenz.

---

## 12. Grenzen (das soll die Video-Erweiterung nicht machen)

- Kein Training von Videomodellen. Dafür sind sie viel zu gross.
- Keine langen Videos. Über ca. 10 Sekunden geht auf diesem Laptop nicht.
- Kein Video im Chat „anschauen“: Meine Sprachmodelle können keine Videos verstehen.
- Video und Chat laufen nicht gleichzeitig.
- Nichts öffentlich teilen, kein Upload irgendwohin.
- KI-Nexus ersetzt ComfyUI nicht. Wer basteln will, öffnet ComfyUI direkt.
