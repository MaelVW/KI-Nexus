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

## Referenzbilder (Nachtrag, Wunsch von Mael)

Bei Bildern kann man **bis zu 3 Referenzbilder** mitgeben. Das Modell orientiert sich daran: dieselbe Person, dasselbe Tier, derselbe Ort oder Stil. Im Prompt steht, was damit passieren soll.

- Nur **FLUX.2 klein**: Es kann von Haus aus erzeugen *und* nach Vorlagen bearbeiten. Z-Image Turbo kann das nicht (dafür bräuchte es die Variante „Z-Image Omni“). Ist Z-Image gewählt, sagt der Hinweis das, und die Referenzen werden nicht mitgeschickt.
- Ablauf in ComfyUI pro Referenz: `LoadImage` → `ImageScaleToMaxDimension` (lange Seite 1024) → `VAEEncode` → `ReferenceLatent`. Mehrere hängen hintereinander am Prompt.
- `ImageScaleToTotalPixels` wäre der übliche Knoten, er braucht aber das Python-Paket `kornia`, das in unserer Umgebung fehlt (bewusst schlank).
- Die Galerie behält eine Kopie jeder Referenz (`<id>_ref1.png` …), damit „Nochmal“ und „Prompt übernehmen“ auch gehen, wenn das Original weg ist.
- Neu im Studio: Bereich „Referenzbilder (optional)“ mit Vorschau und Entfernen, und an jedem Bild der Galerie der Knopf **„Als Referenz für ein Bild“**.

**Test ohne App** (`python/image/ref_test.py`, Hund aus B0 als Referenz):

| Auftrag | Zeit | Ergebnis |
|---|---|---|
| 1 Referenz: „der Hund aus dem Bild mit rotem Strickschal im Schnee“ | 15 s | Derselbe Hund (Gesicht, Fell, Halsband), jetzt mit Schal im Winterwald. |
| 2 Referenzen: „der Hund aus Bild 1 vor der Hütte aus Bild 2“ | 20 s | Der Hund sitzt im Schnee vor genau dieser Hütte mit den Bergen. |

Jede Referenz kostet also ca. 5 s. VRAM blieb bei 7.1 GB.

**Über die Schnittstelle:** Der echte Testlauf (`RealRun_CreatesImagesThroughTheInterface`) macht als zweiten Auftrag ein Bild mit dem ersten als Referenz: bestanden.

**Abnahme in der App** (3. November 2026, Release-Build): Hütten-Bild in der Galerie gewählt, „Als Referenz für ein Bild“, Prompt „dieselbe Hütte im Sommer, Wiese, Kühe“. **21 s** inkl. Start von ComfyUI. Es ist dieselbe Hütte (Fenster, Kamin, Balkon), jetzt auf einer Sommerwiese mit Kühen. Nur das Dach hat noch Schnee. In der Galerie steht „1 Ref.“, die Kopie `…_ref1.png` liegt daneben.

### Z-Image Turbo und Referenzen (geprüft)

Test `python/image/zimage_ref_test.py`, Hund aus B0, Prompt „der Hund mit rotem Strickschal im Schnee“:

| Versuch | Zeit | Ergebnis |
|---|---|---|
| `ReferenceLatent` wie bei FLUX.2 | 42 s | Schal ja, aber der ganze Hintergrund voller Klötzchen-Artefakte, kein Schnee. **Unbrauchbar.** |
| Bild zu Bild, Stärke 0.6 | 18 s | Fast das Original, nur ein winziger Schal. |
| Bild zu Bild, Stärke 0.8 | 18 s | Derselbe Hund mit rotem Schal, gleicher Bildaufbau, aber weiter Wiese statt Schnee. |

**Fazit:** Echte Referenzen (Person oder Ort in ein neues Bild übernehmen) kann Turbo nicht, es wurde dafür nicht trainiert. ComfyUI kennt den Weg zwar (für die Edit-Variante „Z-Image Omni“), mit Turbo gibt es aber Artefakte. Was geht: **Bild zu Bild** (siehe nächstes Kapitel).

## Vorlage: Bild zu Bild

Ein Bild ist der **Ausgangspunkt**, nicht nur eine Orientierung. Aufbau, Pose und Farben bleiben, je nach **Stärke** ändert sich mehr oder weniger. Geht mit **FLUX.2 klein und Z-Image**, auch zusammen mit Referenzbildern (nur FLUX).

- Ablauf: `LoadImage` → `ImageScale` (Format des Auftrags, Mitte zugeschnitten) → `VAEEncode` → `RepeatLatentBatch` (Anzahl Bilder) ersetzt das leere Latent.
- FLUX.2 klein: `SplitSigmasDenoise` schneidet die Rausch-Stufen zu (Stärke 0.8 = die letzten 80 %). Z-Image: `denoise` im KSampler.
- Stärke 0.2 bis 1.0, Standard 0.8. Im Studio ein Schieber mit Erklärung („Pose und Aufbau bleiben, Umgebung darf neu sein“ usw.).
- Im Prompt das **ganze neue Bild** beschreiben, nicht nur die Änderung.
- Neu im Studio: Bereich „Vorlage (optional, Bild zu Bild)“ und an jedem Bild der Galerie **„Als Vorlage für ein Bild“**. Die Galerie behält eine Kopie (`<id>_vorlage.png`) und die Stärke.

**Test ohne App** (`python/image/img2img_test.py`, FLUX.2 klein, Hund aus B0, „mit rotem Schal im Schnee“):

| Stärke | Zeit | Ergebnis |
|---|---|---|
| 0.6 | 9 s | Wiese bleibt, der Hund bekommt einen Schal. |
| 0.8, 2 Bilder | 7.5 s | Schnee und Schal, dieselbe Pose. Zwei verschiedene Varianten. |

Z-Image mit 0.8: derselbe Hund mit Schal, aber die Wiese bleibt (18 s). FLUX.2 klein setzt den Prompt bei gleicher Stärke stärker um.

**Über die Schnittstelle:** Der echte Testlauf macht als dritten Auftrag Bild zu Bild (Stärke 0.7, 2 Bilder): bestanden.

**Abnahme in der App** (3. November 2026, Release-Build):

- Auswahl „Bildmodell“ zeigt „FLUX.2 klein 4B (Q8_0)“ und „Z-Image Turbo (Q8_0)“, beide aus dem Katalog erkannt, mit Hinweis. Bei Z-Image sagt der Hinweis bei den Referenzen, dass es keine kann.
- Hütten-Bild → „Als Vorlage für ein Bild“, Stärke 0.8, Prompt „Hütte im Herbst, goldene Lärchen, kein Schnee“, 2 Bilder: **18 s** inkl. Start von ComfyUI. Dieselbe Hütte aus demselben Blickwinkel, jetzt mit goldenen Lärchen. **Der Schnee blieb** trotz „kein Schnee“: Bei 0.8 bleiben die Farben der Vorlage stark. Darum heisst der Text am Schieber jetzt „Pose und Aufbau bleiben, Farben meist auch“. Für eine andere Jahreszeit braucht es 0.9 bis 1.0.
- Bei FLUX mit Vorlage rechnet ComfyUI nur einen Teil der Schritte (3 von 4 bei 0.8). Die Restzeit rechnet jetzt damit.
- Nicht in der App geprüft: Create-Hub mit einem eigenen Bildmodell (es liegt keines in `models/`) und die Hugging-Face-Suche nach Bildmodellen.

## Eigene Bildmodelle (Create-Hub und Hugging Face)

Bildmodelle, die man selbst lädt (Hugging-Face-Suche oder Import), erscheinen im Studio, wenn das Studio sie rechnen kann. Der GGUF-Kopf dieser Modelle verrät fast nichts (nur `general.architecture`), darum liest `ImageModelCatalog` zusätzlich die **Grösse einzelner Tensoren** (`GgufReader.ReadTensorShapes`):

| Architektur | Kennzeichen | Erkannt als | Text-Encoder |
|---|---|---|---|
| `flux` | `double_stream_modulation_img` da, `txt_in` 7680 breit | FLUX.2 klein 4B | `qwen_3_4b.safetensors` |
| `flux` | `double_stream_modulation_img` da, `txt_in` 12288 breit | FLUX.2 klein 9B | `qwen_3_8b*.safetensors` (muss in `models\image\text_encoders` liegen, z.B. von `Comfy-Org/Flux-2-Klein-9B`) |
| `flux` | `txt_in` 15360 breit | FLUX.2 dev (32B): zu gross, nicht nutzbar | – |
| `flux` | ohne `double_stream_modulation_img` | FLUX.1: braucht T5 und CLIP, nicht nutzbar | – |
| `lumina2` | `cap_embedder` 2560 breit | Z-Image (Turbo, wenn „turbo“ im Namen steht) | `qwen_3_4b.safetensors` |
| `lumina2` | andere Breite | Lumina-Image 2: nicht nutzbar | – |

- Gemessen an den zwei echten Dateien aus B0 (Test `RealFiles_AreRecognised`): beide richtig erkannt.
- **Create-Hub:** Bildmodelle zeigen „Bildmodell: …“ und den Knopf **„Im Studio nutzen“** (gilt jetzt auch für Videomodelle). Nicht nutzbare zeigen den Grund.
- **Hugging Face:** Etikett „Bildmodell (FLUX / Z-Image)“, eigener Hinweis und eigene Rückfrage vor dem Download. Nach dem Download: Meldung mit „Im Studio nutzen“.
- Text-Encoder und VAE kommen immer aus `models\image` (von `setup-video.ps1 -Images`). ComfyUI bekommt den Ordner des Modells in `kinexus-paths.yaml` dazu.
- **FLUX.2 klein 9B** ist eingebaut, aber nicht getestet (Lizenz: FLUX Non-Commercial, nur privat).

## Privatordner (seit 5.11.2026)

Videos und Bilder kann man in einen **Privatordner** legen. Dort stehen sie nicht mehr in der normalen Galerie (weder unter „Alle“ noch unter „Videos“ oder „Bilder“), sondern nur unter dem Filter **„Privat“** (mit Schloss).

- Knopf **„In den Privatordner“** beim gewählten Eintrag, dort auch „Aus dem Privatordner“ zurück.
- Schalter **„Im Privatordner speichern“** über „Video erzeugen“ / „Bilder erzeugen“: Das Ergebnis kommt direkt in den Privatordner, die Galerie springt nicht dorthin.
- Die Dateien liegen in `data\videos\privat\` bzw. `data\images\privat\` (MP4 oder PNG, JSON, Vorschau, Start-, Referenz- und Vorlagebilder werden mitverschoben). Gibt es im Ziel schon eine Datei mit gleichem Namen, wird nichts verschoben.
- **Ehrliche Grenze:** versteckt nur in der Galerie, **nicht verschlüsselt** und ohne Passwort. Wer den Ordner im Explorer öffnet, sieht die Dateien.

## Noch nicht geprüft

- Grössere Formate als 1216×832 und vier Bilder in 1024×1024 auf einmal (Speicher).
- Z-Image über die App (nur in B0 mit dem Testskript geprüft).
- Eigene Bildmodelle von Hugging Face: kommt später, wie beim Video.
