# 10 – Video

Stand: 2. November 2026. Plan und Phasen stehen in [Bauplan-Video.md](Bauplan-Video.md).

| Phase | Inhalt | Status |
|---|---|---|
| V0 | Machbarkeitstest ohne App (dieses Kapitel bis „Noch nicht geprüft“) | ✅ |
| V1 | Video-Engine in KI-Nexus, noch ohne Oberfläche (Kapitel „V1 – Video-Engine“ unten) | ✅ |
| V2–V4 | Video-Tab, Create-Hub, Extras | offen |

V0 ist ein Test **ohne App**: Schafft der Laptop mit LTX-2.3 überhaupt ein Video? **Ja.** Ein Clip mit 768×512 Pixeln und knapp 4 Sekunden, mit Ton, dauert ca. 2½ Minuten.

## Ergebnis in Kürze

| Frage | Antwort |
|---|---|
| Läuft LTX-2.3 (22B) auf 8 GB VRAM? | Ja, mit GGUF und Offloading (Teile des Modells liegen im RAM). VRAM höchstens 7.2 GB. |
| Wie lange dauert ein Clip? | 768×512, 3.9 s mit Ton: **2 min 24 s bis 2 min 37 s** inkl. Start von ComfyUI. 512×320, 2 s: 2 min 14 s. |
| Was bremst? | Der **RAM**, nicht die Grafikkarte. Ein Schritt rechnet nur 4–7 s. Der RAM ist aber ganz voll, und Windows lagert auf die Festplatte aus. |
| Grenze aus dem Bauplan (15 min pro Clip) | Weit unterschritten. **V0 bestanden.** |
| Qualität | Der Clip passt zum Prompt (Hund rennt durchs Gras auf die Kamera zu), mit Ton. Nur von mir an Einzelbildern geprüft. |

## Was installiert ist

| Teil | Version / Datei | Ort |
|---|---|---|
| ComfyUI | v0.43.0 (Release-ZIP von `Comfy-Org/ComfyUI`) | `engines/comfyui/` (nicht in Git) |
| ComfyUI-GGUF | Commit `6ea2651` (city96, 12.1.2026) | `engines/comfyui/custom_nodes/ComfyUI-GGUF/` |
| Python-Umgebung | Python 3.12, PyTorch 2.14.0+cu130 (gleich wie Training, per Hardlink aus dem uv-Cache, kostete kaum Platz) | `engines/comfyui/.venv/`, Pakete in `python/video/pyproject.toml` + `uv.lock` |
| Modell-Pfade für ComfyUI | `engines/comfyui/kinexus-paths.yaml` | zeigt auf `models/video/` |

Bewusst **nicht** installiert:

- **ComfyUI-Manager:** Er installiert fremden Code per Klick.
- **ComfyUI-KJNodes:** Die Unsloth-Anleitung nutzt es nur zum Laden der VAE. ComfyUI kann das selbst (`VAELoader`, `LTXVAudioVAELoader`).
- **Vorlagen- und Hilfepakete** (`comfyui-workflow-templates`, `comfyui-embedded-docs`): Die braucht nur die Knoten-Ansicht. ComfyUI meldet beim Start einen Fehler dazu, läuft aber.
- Optionale Pakete `kornia`, `spandrel` und `comfy_angle`: Ein paar Knoten fehlen dadurch. LTX braucht keinen davon.

Einrichten: seit V1 mit `scripts\setup-video.ps1` (siehe unten). In V0 lief das von Hand mit denselben Schritten.

## Modelldateien (24 GB)

Alle Dateien wurden mit der SHA-256-Prüfsumme von Hugging Face geprüft.

| Datei | Grösse | Quelle | Ordner unter `models/video/` |
|---|---|---|---|
| `ltx-2.3-22b-distilled-1.1-Q4_K_M.gguf` (Kern, distilled = 8 Schritte) | 13.2 GB | `unsloth/LTX-2.3-GGUF` | `diffusion_models/` |
| `gemma-3-12b-it-qat-UD-Q4_K_XL.gguf` (Text-Encoder) | 6.9 GB | `unsloth/gemma-3-12b-it-qat-GGUF` | `text_encoders/` |
| `ltx-2.3-22b-distilled_embeddings_connectors.safetensors` | 2.2 GB | `unsloth/LTX-2.3-GGUF` | `text_encoders/` |
| `ltx-2.3-22b-distilled_video_vae.safetensors` | 1.4 GB | `unsloth/LTX-2.3-GGUF` | `vae/` |
| `ltx-2.3-22b-distilled_audio_vae.safetensors` (mit Vocoder) | 0.3 GB | `unsloth/LTX-2.3-GGUF` | `vae/` |

Was dabei herauskam:

- Die Datei „embeddings_connectors“ enthält trotz Namen nur die **Text-Projektion** (`text_embedding_projection.*`, 4 Tensoren). Sie übersetzt die Gemma-Ausgabe für den Kern. Die eigentlichen Connectors stecken im Kern-GGUF.
- Das `mmproj` von Gemma (Bildteil, 0.8 GB) braucht ComfyUI-GGUF hier nicht. Es lädt mmproj nur für Qwen2-VL.
- Den Kopf (Header) der safetensors-Dateien habe ich vor dem Download auf Hugging Face gelesen (HTTP-Range). So war klar, dass die Schlüssel zu den Loadern passen.
- Lizenz: LTX-2 Community License (Lightricks), Gemma-Lizenz (Google). Privat nutzen ist ok, nicht weitergeben.

## Workflow

`src/KiNexus.Engines/Video/Workflows/ltx23-text-zu-video.json` (API-Format, in V0 noch unter `python/video/workflows/`). Jeder wichtige Knoten hat einen Titel `KN_…`. Das Testskript findet die Knoten darüber, nicht über die Nummer, wie im Bauplan für V1 geplant.

```
UnetLoaderGGUF ──────────────────────────────┐
DualCLIPLoaderGGUF (Gemma + Projektion, ltxv) ─► CLIPTextEncode ─► LTXVConditioning (25 fps) ─► CFGGuider (CFG 1)
EmptyLTXVLatentVideo + LTXVEmptyLatentAudio ─► LTXVConcatAVLatent ─────────────────────────────┐   │
RandomNoise · KSamplerSelect (euler) · ManualSigmas (8 Schritte distilled) ─► SamplerCustomAdvanced
  ─► LTXVSeparateAVLatent ─► VAEDecodeTiled (Bilder) + LTXVAudioVAEDecode (Ton) ─► CreateVideo ─► SaveVideo (MP4, H.264)
```

- Sigmas für das distilled-Modell: `1.0, 0.99375, 0.9875, 0.98125, 0.975, 0.909375, 0.725, 0.421875, 0.0`. Die letzten vier sind dieselben wie in der zweiten Stufe des Unsloth-Workflows.
- CFG 1: Der Negativ-Prompt wird dann nicht gebraucht. ComfyUI rechnet ihn trotzdem einmal (ca. 4 s). Seit V1 setzt die Engine dafür `ConditioningZeroOut` ein.
- `VAEDecodeTiled` mit 64 Bildern pro Stück, damit das Dekodieren in 8 GB passt.

## Messung (am Netzteil)

Testskript `python/video/v0_test.py`. Es startet ComfyUI, schickt den Workflow, liest den Fortschritt über den WebSocket und misst jede halbe Sekunde. Die Rohdaten stehen in `data/videos/v0-messungen.jsonl`, die Videos in `data/videos/roh/v0/`.

```powershell
engines\comfyui\.venv\Scripts\python.exe -s python\video\v0_test.py --label test --width 768 --height 512 --frames 97 --extra=--cache-none
```

Prompt (alle Läufe, Seed 42): ein Golden Retriever rennt über eine sonnige Wiese auf die Kamera zu, mit Vogelgezwitscher und einem Bellen.

| | Probe 512×320, 49 Bilder | **V0 768×512, 97 Bilder** | V0 mit `--cache-none` |
|---|---|---|---|
| Start ComfyUI | 15.7 s | 17.2 s | 16.2 s |
| Text-Encoder laden | 17.8 s | 16.9 s | 13.8 s |
| Prompt lesen (erstes + zweites Mal) | 21.0 + 4.2 s | 20.3 + 4.3 s | 20.6 + 5.2 s |
| Kern laden + 1. Schritt | ca. 39 s | ca. 37 s | ca. 32 s |
| Schritte 2–8, je | 3.8 s | 7.2 s | 6.8 s |
| Ton + Bilder dekodieren | 3.2 + 2.5 s | 3.0 + 4.0 s | 1.6 + 3.9 s |
| **Auftrag total (ohne Start)** | **118 s** | **140 s** | **128 s** |
| VRAM höchstens | 7.0 GB | 7.2 GB | 7.2 GB |
| RAM belegt höchstens (von 31.4) | 31.4 GB | 31.3 GB | 31.4 GB |
| davon andere Programme vorher | 11.6 GB | 8.9 GB | 9.3 GB |
| Auslagerungsdatei benutzt höchstens | 13.1 GB | 8.7 GB | **3.2 GB** |
| Speicher von ComfyUI (Commit) | 40.7 GB | 38.9 GB | **24.1 GB** |
| Ergebnis | MP4 1.96 s, H.264 + AAC 48 kHz Stereo | MP4 3.88 s, H.264 + AAC 48 kHz Stereo | bitgleich mit V0 |

## Was ich daraus lerne

1. **Die Grafikkarte reicht.** Ein Schritt bei 768×512 dauert nur 7 s. Die meiste Zeit geht für Laden drauf: ComfyUI 16 s, Gemma 17 s, erster Prompt 20 s, Kern ca. 30 s.
2. **Der RAM ist der Engpass.**
   - Gemma braucht in ComfyUI ca. 12 GB. Die Tabelle der Wort-Vektoren wird auf 16 Bit entpackt, dazu kommt die Projektion.
   - Der Kern braucht ca. 14 GB.
   - Ohne `--cache-none` hält ComfyUI beides gleichzeitig, und Windows lagert bis 13 GB aus.
3. **`--cache-none` gehört in V1 dazu.**
   - Es ist schneller, lagert weniger aus und liefert dasselbe Ergebnis.
   - Nachteil: Jeder Auftrag lädt alle Modelle neu. Mehrere Clips hintereinander werden so nicht schneller.
4. **Mehrere Clips hintereinander (Warteschlange V4):** Hier lohnt es sich, ComfyUI laufen zu lassen. Ohne `--cache-none` würde ab dem zweiten Clip das Laden wegfallen, dafür ist der RAM die ganze Zeit voll. Das in V4 messen.
5. **Andere Programme zählen:** Vor dem Test waren schon 9–12 GB RAM von anderen Programmen belegt. Die App soll vor dem Start freien RAM prüfen und warnen, ähnlich der Speicher-Ampel.
6. **Ergebnis-Datei in V1 aus der WebSocket-Meldung `executed` lesen:** Mit `--cache-none` liefert `GET /history/<id>` keine Ausgaben, die Datei ist aber da.
7. **Ein geladenes Chat-Modell muss vorher weg:** Die 7.2 GB VRAM lassen keinen Platz für llama-server, wie im Bauplan vorgesehen.

## Antworten auf „Das musst du selbst prüfen“ (Bauplan-Video, Kapitel 11)

1. **ComfyUI:**
   - v0.43.0 kann LTX-2.3 mit Ton von sich aus (Knoten `LTXV…`). Aus ComfyUI-GGUF braucht es nur `UnetLoaderGGUF` und `DualCLIPLoaderGGUF`.
   - Mein PinkCherry-GGUF habe ich nicht geladen (siehe unten).
2. **Text-Encoder:** Gemma 3 12B als GGUF plus die Projektions-Datei (`…embeddings_connectors.safetensors`), geladen mit `DualCLIPLoaderGGUF`, Typ `ltxv`. Kein mmproj.
3. **Finetune (dev oder distilled):** nicht geprüft.
4. **LTX-2.5:** nicht geprüft. Es gibt GGUF-Versionen. Kandidat für später.
5. **Flags in ComfyUI 0.43:**
   - `--offline` sperrt Cloud-Knoten und Internet der Oberfläche. `--disable-api-nodes` ist nur noch ein Alias dafür.
   - `--lowvram` wirkt nicht, weil „dynamic VRAM“ Standard ist.
   - `--cache-none` hilft (siehe oben).
   - WebSocket: `executing`, `progress` (value/max), `executed`, `execution_success`, `execution_error`.
6. **Player:** noch offen. Das MP4 ist H.264 + AAC, das sollte `MediaElement` unter Windows 11 abspielen. Prüfen in V2.
7. **RAM:** 31.4 GB reichen knapp, aber nur mit Auslagerung. Mit `--cache-none` sind es 3 GB Auslagerung. Die Auslagerungsdatei verwaltet Windows selbst, das hat gereicht.
8. **Lizenzen:** siehe oben.

## Abweichungen vom Bauplan

- **Nur das offizielle LTX-2.3** getestet, nicht die PinkCherry-Datei aus `models/`. Das Modell ist auf Hugging Face als NSFW markiert. Ich habe es nicht eingerichtet.
- Grundmodell **distilled 1.1** (8 Schritte) statt „dev“: schneller, und der Bauplan sieht „Schnell“ als Vorlage vor.
- ComfyUI-KJNodes nicht installiert (nicht nötig).
- Vorlagen-Ordner in V0: `python/video/workflows/`. Seit V1 liegen die Vorlagen in `src/KiNexus.Engines/Video/Workflows/`.

## Noch nicht geprüft

- Wie gut die Videos aussehen und klingen, musst du selbst beurteilen. Ich habe nur Einzelbilder angeschaut und den Tonpegel gemessen.
- Bild zu Video, längere Clips (über 4 s), andere Formate (hochkant).
- Mehrere Clips hintereinander mit laufendem ComfyUI.
- Abbrechen (`POST /interrupt`) und Speicher freigeben (`POST /free`). Erledigt in V1 (siehe unten).

---

## V1 – Video-Engine

KI-Nexus kann jetzt selbst Videos erzeugen, noch ohne Oberfläche. Der Video-Tab kommt in V2.
Die Oberfläche spricht nur mit der Schnittstelle `IVideoBackend` (Core). ComfyUI steckt dahinter in `ComfyVideoBackend` (Engines).

### Ablauf eines Videos

1. `GenerateAsync(request, models, ct)` prüft den Auftrag (`VideoRequest.Problem()`) und ob alle 5 Modelldateien da sind.
2. Läuft ComfyUI noch nicht, startet es. Der Start dauert ca. 16 s.
   - Es läuft unsichtbar, nur auf `127.0.0.1:8188`, mit `--offline` und `--cache-none`.
   - Es hängt am Job Object und endet spätestens mit KI-Nexus.
   - Vor dem Start schreibt die App `engines\comfyui\kinexus-paths.yaml` neu, damit ComfyUI die Modelle in `models\video` findet.
3. Der `ComfyWorkflowBuilder` füllt die Vorlage aus der DLL aus und schickt sie mit `POST /prompt`.
4. Der WebSocket meldet, an welchem Knoten ComfyUI arbeitet. Daraus macht die Engine:
   - `VideoStage`, z.B. „Entrauschen …“
   - `VideoStep`, z.B. Schritt 3 von 8
5. Am Ende kommt `VideoDone` mit dem Pfad, z.B. `data\videos\roh\2026-11-02_144301_seed11_00001_.mp4`. Bei einem Fehler kommt `VideoFailed` mit einem verständlichen Text, beim Abbrechen `VideoCancelled`.

| Was | Wie |
|---|---|
| Abbrechen | Den `CancellationToken` auslösen. Die Engine schickt `POST /interrupt`, ComfyUI antwortet sofort mit `execution_interrupted`. Kommt in 30 s nichts, wird ComfyUI beendet. |
| Speicher freigeben | `FreeMemoryAsync`: `POST /free` mit `unload_models` und `free_memory`. ComfyUI läuft weiter. |
| Nur ein Video gleichzeitig | Ein zweiter Auftrag bekommt sofort „Es entsteht schon ein Video“. |
| Negativ-Prompt | Bei CFG 1 (distilled) setzt die Vorlage `ConditioningZeroOut` ein, statt den Text mit Gemma zu lesen. Spart ca. 4 s. |
| Ohne Ton | Der Knoten `KN_DecodeAudio` fliegt raus, das Video hat keine Tonspur. |
| Log | `data\logs\video.log` (Ausgabe von ComfyUI), dazu Einträge im normalen App-Log. |
| Einstellung | `data\settings.json` → `ports.comfyUi` (Standard 8188). |

Abweichungen von der Schnittstelle im Bauplan:

- **Zustand:** Statt `GetStatusAsync` gibt es `State`, `StatusText` und `Changed`. Grund: genau so wie beim Open-WebUI-Launcher, die Oberfläche (V2) kann sich einfach anmelden.
- **Modelldateien** sind ein eigener Parameter (`VideoModelSet`), nicht Teil des Auftrags. Schritte und CFG hängen am Modell, nicht am Auftrag: Das distilled-Modell braucht immer 8 Schritte und CFG 1.
- **Startbild** (Bild zu Video) fehlt noch im Auftrag. Das kommt in V4, zusammen mit der zweiten Vorlage.

### Einrichten

```powershell
.\scripts\setup-video.ps1           # ComfyUI, ComfyUI-GGUF, Python-Umgebung, Modell-Pfade, Prüfung
.\scripts\setup-video.ps1 -Models   # dazu die Modelle (ca. 24 GB, mit Prüfsumme)
```

Das Skript merkt sich die Version in `engines\comfyui\version.txt`. Bei einer neuen Version ersetzt es den Code und behält die Python-Umgebung.
Getestet am 2.11.2026 auf der bestehenden Installation: Der Code wurde ersetzt, die Umgebung blieb, und PyTorch findet die RTX 5070.

### Tests

- 29 neue Unit-Tests in `VideoTests` (Core und Engines), insgesamt jetzt 385 plus der echte Testlauf:
  - Prüfregeln des Auftrags, Sekunden in Bilder umrechnen, fehlende Modelldateien
  - Ausfüllen der Vorlage, auch ohne Ton und mit Negativ-Prompt
  - Jede Verbindung im Workflow zeigt auf einen vorhandenen Knoten
  - Meldungen und Ablehnungen von ComfyUI, echte Beispiele aus ComfyUI 0.43
  - Startbefehl, Pfad-Datei, Hinweis aufs Setup
  - Fehlende Modelle ergeben `VideoFailed`, ohne dass ComfyUI startet
- **Echter Testlauf** `RealRun_CreatesVideoThroughTheInterface`: läuft nur mit `KINEXUS_VIDEO_TEST=1`, weil er Minuten dauert.

```powershell
$env:KINEXUS_VIDEO_TEST = "1"
tests\KiNexus.Engines.Tests\bin\Debug\net10.0-windows\KiNexus.Engines.Tests.exe -method "*RealRun*" -showLiveOutput
```

Ergebnis vom 2.11.2026 (am Netzteil, 10 GB RAM vorher belegt):

| Auftrag | Ergebnis |
|---|---|
| 512×320, 49 Bilder (2 s), mit Ton | `VideoDone` nach 97 s (Schritt 1 nach 65 s inkl. Laden, dann ca. 3.6 s pro Schritt). V0 brauchte für dasselbe Format 118 s: ohne Negativ-Prompt und mit `--cache-none` geht es schneller. |
| Derselbe nochmals, nach Schritt 1 abgebrochen | `VideoCancelled` sofort, danach ist die Engine wieder „Bereit“ |
| Ganzer Test inkl. Start und Stopp von ComfyUI | 181 s, danach ist ComfyUI beendet und der VRAM leer |

### Noch offen (V2)

- Video-Tab mit Player und Galerie. Die Videos liegen bis dahin in `data\videos\roh\`.
- ComfyUI nach X Minuten ohne Auftrag beenden (Einstellung).
- Vor dem Start ein geladenes Chat-Modell entladen und freien RAM prüfen.
- Den alten Ordner `data\videos\comfy-*` aus V0 braucht es nicht mehr. Seit V1 heissen die Arbeitsordner `data\comfyui\`.
