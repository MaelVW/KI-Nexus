# 11 – Bild

Stand: 3. November 2026. Plan und Phasen stehen in [Bauplan-Bild-und-Szenen.md](Bauplan-Bild-und-Szenen.md), Teil 1.

| Phase | Inhalt | Status |
|---|---|---|
| B0 | Machbarkeit: zwei Bildmodelle messen (dieses Kapitel bis „Empfehlung“) | ✅ |
| B1 | Bild-Engine: `ImageRequest`, Vorlagen, `GenerateImageAsync`, `setup-video.ps1 -Images` | ✅ (v1.9.0) |
| B2 | Studio-Tab: Umschalter Video \| Bild, Galerie mit Filter, „Als Startbild für ein Video“ | ✅ (v1.9.0) |

Bilder laufen über **dasselbe ComfyUI** wie die Videos. Neu sind nur zwei Workflow-Vorlagen, ein Auftrag und ein Ordner `models/image/`.

## B0 – Ergebnis in Kürze

| Frage | Antwort |
|---|---|
| Laufen die Modelle auf 8 GB VRAM? | Ja, beide. VRAM höchstens 7.1 GB. |
| Wie lange dauert ein Bild (1024×1024)? | **FLUX.2 klein: 8–11 s.** Z-Image Turbo: 22–24 s. Je inkl. Laden der Modelle (ComfyUI lief schon). |
| RAM | FLUX.2 klein: ComfyUI bis 16 GB, ganzer Laptop 19 GB von 31 GB. Z-Image: bis 21 GB, Laptop 27 GB. |
| Qualität | Beide sehr gut und fotorealistisch. Schrift: Z-Image schreibt „Ki-Nexus“ richtig, FLUX.2 klein schrieb „Ki-Nexcs“. |
| **Empfehlung** | **FLUX.2 klein 4B** als Standard: 2½-mal schneller und braucht deutlich weniger RAM. Z-Image Turbo als zweite Wahl, wenn Schrift im Bild wichtig ist. |

## Die zwei Kandidaten

| | FLUX.2 klein 4B | Z-Image Turbo |
|---|---|---|
| Hersteller | Black Forest Labs | Tongyi (Alibaba) |
| Lizenz | Apache 2.0 | Apache 2.0 |
| Datei (GGUF, Unsloth) | `flux-2-klein-4b-Q8_0.gguf`, 4.0 GB | `z-image-turbo-Q8_0.gguf`, 6.7 GB |
| Text-Encoder | `qwen_3_4b.safetensors`, 7.5 GB (**derselbe** für beide) | ebenso |
| VAE | `flux2-vae.safetensors`, 0.3 GB | `ae.safetensors`, 0.3 GB |
| Schritte | 4 (destilliert), CFG 1 | 8 (Turbo), CFG 1 |
| Ablauf in ComfyUI | `UnetLoaderGGUF`, `CLIPLoader` (Typ `flux2`), `EmptyFlux2LatentImage`, `Flux2Scheduler`, `CFGGuider`, `SamplerCustomAdvanced` (euler) | `UnetLoaderGGUF`, `CLIPLoader` (Typ `lumina2`), `EmptySD3LatentImage`, `ModelSamplingAuraFlow` (shift 3), `KSampler` (res_multistep, simple) |

Der Typ beim `CLIPLoader` ist wichtig: Derselbe Qwen3-4B wird für FLUX.2 anders gelesen als für Z-Image (`comfy/sd.py`: Typ `flux2` → klein-Encoder, sonst → Z-Image-Encoder).

Die Dateien kommen mit SHA-256-Prüfsumme von Hugging Face: das GGUF von `unsloth/FLUX.2-klein-4B-GGUF` bzw. `unsloth/Z-Image-Turbo-GGUF`, Text-Encoder und VAEs von `Comfy-Org/Flux-2-Klein-4B` bzw. `Comfy-Org/Z-Image-Turbo`.

## Messung

Testskript: `python/image/b0_test.py` (startet ComfyUI wie `python/video/v0_test.py`, misst jede halbe Sekunde VRAM und RAM). Pro Modell zwei Prompts, Seed 1000 und 1001, 1024×1024, ohne `--cache-none`. Ergebnisse in `data/images/b0-messungen.jsonl`, Bilder in `data/images/roh/b0/`.

| Modell | Prompt | Zeit | VRAM max. | RAM Laptop max. | RAM ComfyUI (privat) |
|---|---|---|---|---|---|
| FLUX.2 klein | Hund (kalt) | 10.6 s | 6.96 GB | 18.1 GB | 10.6 GB |
| FLUX.2 klein | Schild (warm) | 8.4 s | 7.14 GB | 19.1 GB | 15.8 GB |
| Z-Image Turbo | Hund (kalt) | 24.1 s | 7.12 GB | 25.8 GB | 16.4 GB |
| Z-Image Turbo | Schild (warm) | 21.5 s | 6.95 GB | 27.3 GB | 20.6 GB |

„Kalt“: Das Modell lädt beim ersten Bild. „Warm“: zweites Bild, Modelle schon geladen. Der Unterschied ist klein, weil die Dateien schon im Cache von Windows lagen.

**Über die App-Schnittstelle (B1):** FLUX.2 klein, 2 Bilder 768×768 in einem Durchgang: **9.4 s** (plus ca. 9 s Start von ComfyUI).

### Qualität (von mir an den Bildern geprüft)

- **Hund** (Golden Retriever auf einer Wiese): beide fotorealistisch, scharf, mit Unschärfe im Hintergrund. Kein sichtbarer Fehler.
- **Schild** („KI-Nexus“ auf einem Holzschild vor einer Berghütte): Z-Image schreibt „Ki-Nexus“ richtig, aber ohne Hütte. FLUX.2 klein zeigt Hütte und Berge, schreibt aber „Ki-Nexcs“.

## Empfehlung

**FLUX.2 klein 4B ist der Standard** (`setup-video.ps1 -Images` lädt nur dieses, ca. 12 GB mit Text-Encoder und VAE).

- Ein Bild in ca. 10 s statt 24 s: Vier Bilder dauern damit weniger als ein einziges mit Z-Image.
- Weniger RAM: Mit Z-Image war der Laptop bei 27 von 31 GB, da wird es neben Browser und Chat eng.
- Z-Image Turbo bleibt nutzbar: Liegt es in `models/image/diffusion_models/`, steht es im Studio in der Auswahl. Gut für Bilder mit Schrift.

## B1 – Bild-Engine

| Teil | Datei |
|---|---|
| Auftrag | `src/KiNexus.Core/Video/ImageRequest.cs`: Prompt, Breite, Höhe (durch 16 teilbar, 512–1536), Anzahl (1–4), Seed |
| Modelle | `src/KiNexus.Core/Video/ImageModelSet.cs`: `Flux2Klein4B` und `ZImageTurbo`, Dateien in `models/image/` |
| Vorlagen | `src/KiNexus.Engines/Video/Workflows/flux2-klein-bild.json` und `z-image-bild.json`, Knoten mit Titel `KN_…` |
| Backend | `ComfyVideoBackend.GenerateImageAsync`: gleicher Ablauf, Fortschritt und Abbrechen wie beim Video. Endet mit `ImageDone` (eine PNG pro Bild). |
| Pfade | `kinexus-paths.yaml` bekommt einen zweiten Eintrag `kinexus_bild` für `models/image/`. |
| Einrichtung | `scripts\setup-video.ps1 -Images` |

**Abweichung vom Bauplan:** Zwei Vorlagen statt einer `bild.json`, weil die zwei Modelle in ComfyUI ganz anders aufgebaut sind (siehe Tabelle oben). Alle Bilder eines Auftrags entstehen **in einem Durchgang** (`batch_size`), mit einem Seed. So lädt das Modell nur einmal. Gleicher Seed, gleicher Prompt und gleiche Anzahl ergeben wieder dieselben Bilder.

Echter Testlauf (nur auf Wunsch, braucht die Grafikkarte):

```
set KINEXUS_VIDEO_TEST=1
tests\KiNexus.Engines.Tests\bin\Debug\net10.0-windows\KiNexus.Engines.Tests.exe -method "*RealRun_CreatesImages*"
```

## B2 – Studio-Tab

- Der Tab **Video** heisst jetzt **Studio**, oben rechts mit dem Umschalter **Video | Bild**.
- Bild-Auftrag: Bildmodell, Prompt (mit „Prompt verbessern“, eigener Prompt für Bilder), Format (quadratisch 1024×1024, quer 1216×832, hochkant 832×1216), Anzahl 1–4, Seed.
- Video und Bild haben je **ihren eigenen Prompt**. Beim Umschalten bleibt der andere erhalten.
- **Gemeinsame Warteschlange:** Videos und Bilder laufen nacheinander, Abbrechen und Chat-Sperre wie beim Video.
- **Galerie** mit Filter **Alle / Videos / Bilder**. Bilder liegen in `data/images/` (PNG und JSON), Videos weiter in `data/videos/`. Ein kleines Zeichen unten links zeigt, was es ist.
- Ein gewähltes Bild steht rechts gross statt des Players. Knopf **„Als Startbild für ein Video“**: Das Studio wechselt auf Video, das Bild wird Startbild. Ein Hochkant-Bild gibt ein Hochkant-Video.
- „Prompt übernehmen“, „Nochmal (gleicher / neuer Seed)“ und „In den Papierkorb“ gehen auch für Bilder.
- Die Speicher-Ampel gilt nur für Videos. Bilder brauchen weniger (siehe Messung).

### Abnahme in der App (3. November 2026, Release-Build)

| Schritt | Ergebnis |
|---|---|
| Studio öffnen, auf „Bild“ umschalten, 4 Bilder (1024×1024, FLUX.2 klein) | **34 s** inkl. Start von ComfyUI. Vier verschiedene, passende Bilder (Berghütte bei Sonnenuntergang). |
| Erstes Bild → „Als Startbild für ein Video“ | Studio wechselt auf Video, Startbild gesetzt, Video-Prompt leer (eigener Prompt). |
| Video aus dem Bild (768×512, 4 s, mit Ton) | **2 min 11 s.** Das Video beginnt mit der Hütte, die Kamera fährt wie beschrieben heran. |
| Galerie | Alle 27, Bilder 4, Videos 23. Passt zu den Dateien in `data/images` und `data/videos`. Bild: grosse Ansicht und Startbild-Knopf. Video: Player. |

## Noch nicht geprüft

- Grössere Formate als 1216×832 und vier Bilder in 1024×1024 auf einmal (Speicher).
- Z-Image über die App (nur in B0 mit dem Testskript geprüft).
- Eigene Bildmodelle von Hugging Face: kommt später, wie beim Video.
