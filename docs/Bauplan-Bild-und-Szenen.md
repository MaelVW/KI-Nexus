# KI-Nexus – Bauplan Bild, Training 2 und Szenen (Erweiterung)

**Für:** die KI, die KI-Nexus weiterbaut
**Von:** Mael, geplant zusammen mit Claude
**Stand:** 2. November 2026 – **theoretisch**, noch nichts davon ist gebaut. Reihenfolge: Bild → Training 2 → Szenen.

> Erweiterung zum Hauptbauplan und zum [Video-Bauplan](Bauplan-Video.md). Es gelten dieselben Regeln:
> - Phase für Phase.
> - Vorher fragen bei Installationen und Downloads über 1 GB.
> - Alles lokal.
> - Ehrlich bleiben.

Drei Teile in dieser Reihenfolge:

1. **Bildgenerierung (Phasen B0–B2):** Bilder aus einem Prompt, im gleichen Tab wie die Videos.
2. **Training 2 (Phasen T1–T2):** eine trainierte KI weitertrainieren und Trainingsdaten mit einer KI erzeugen.
3. **Szenen-Videos (Phasen S0–S3):** ein längeres Video, bis einige Minuten. Man beschreibt einzelne Szenen. Jede Szene beginnt mit dem letzten Bild der vorherigen (Keyframe). Die Warteschlange arbeitet sie ab, am Schluss setzt die App alles zu einem Video zusammen.

---

## Teil 1: Bildgenerierung

### 1.1 Worum es geht

- Text zu Bild, z.B. für Startbilder von Videos, Hintergründe oder einfach zum Spass.
- Es läuft über **ComfyUI**, wie die Videos. Die Engine (`ComfyVideoBackend`), die Workflow-Vorlagen, die Galerie, die Warteschlange, die Speicher-Ampel und die Chat-Sperre gibt es schon.
- Darum ist das der schnellste Teil: hauptsächlich eine neue Vorlage, ein neuer Auftrag und ein Umschalter in der Oberfläche.

### 1.2 Modell (in B0 entscheiden)

Für 8 GB VRAM kommen kleine bis mittlere Bildmodelle als GGUF in Frage. Grössen und Stand prüfen, bevor etwas geladen wird:

| Kandidat | Vorteil | Nachteil |
|---|---|---|
| **FLUX.1 schnell** (GGUF, ca. Q4) | sehr gute Bilder in 4 Schritten, freie Lizenz (Apache 2.0) | braucht zusätzlich einen grossen Text-Encoder (T5) |
| **Z-Image Turbo** oder ein ähnliches neues 6B-Modell (GGUF) | schnell, gut auf 8 GB | Lizenz und Unterstützung in ComfyUI 0.43 prüfen |
| **SDXL** (ein Finetune) | klein, schnell, sehr viele Varianten | sichtbar ältere Qualität |

**Empfehlung für B0:** zwei Kandidaten kurz messen (Zeit pro Bild, VRAM, RAM, Qualität), dann einen fest einbauen. Wie beim Video sind weitere Modelle über die Hugging-Face-Suche erst ein späterer Schritt.

### 1.3 Oberfläche

- Der Tab **Video** wird zu **Studio** mit einem Umschalter **Video | Bild** oben.
- Begründung: Galerie, Warteschlange, Ampel und Seed sind dieselben. Ein zweiter Tab würde alles doppelt zeigen.
- Bild-Auftrag: Prompt (mit „Prompt verbessern“), Format (quadratisch 1024×1024, quer, hochkant), Anzahl Bilder (1–4), Seed.
- Galerie: Bilder und Videos gemischt, mit Filter „Alle / Videos / Bilder“.
- **Neue Verbindung:** Knopf „Als Startbild für ein Video“ an jedem Bild. Damit wird aus einem Bild sofort ein Clip.

### 1.4 Phasen Bild

| # | Inhalt | Abnahme |
|---|---|---|
| **B0** | **Machbarkeit:** zwei Bildmodelle mit ComfyUI testen (Python-Testskript wie in Video V0). **Vorher fragen, ca. 6–15 GB.** Messen: Zeit pro Bild, VRAM, RAM, Qualität. Dann eines wählen. | Für jeden Kandidaten ein Bild und Messwerte in `docs/11-Bild.md`, dazu eine Empfehlung. |
| B1 | **Engine:** `ImageRequest` (Grösse, Anzahl, Seed), Workflow-Vorlage `bild.json`. Das Backend bekommt `GenerateImageAsync` (gleicher Ablauf, Fortschritt und Abbrechen wie bei Videos). Das Bildmodell kommt in `setup-video.ps1 -Images`. Unit-Tests. | Tests grün, ein echter Testlauf erzeugt ein Bild über die Schnittstelle. |
| B2 | **Studio-Tab:** Umschalter Video/Bild, Bild-Auftrag, Galerie mit Filter, „Als Startbild für ein Video“, Warteschlange für Bilder. | Ich erzeuge im Tab vier Bilder, mache aus einem davon ein Video und finde beide in der Galerie. |

---

## Teil 2: Training 2

### 2.1 Weitertrainieren (T1)

- Eine schon trainierte KI (LoRA-Adapter, z.B. „Nexi“) **weiter trainieren**: mehr Durchgänge, neue Beispiele oder beides.
- **Technik:** PEFT lädt den bestehenden Adapter wieder (`PeftModel.from_pretrained(..., is_trainable=True)`) und trainiert ab diesem Stand weiter, statt mit einem leeren Adapter zu beginnen. Grundmodell und Adapter-Grösse (Rang) müssen gleich bleiben.
- **Adapter behalten:** Heute löscht die App beim Start eines Trainings den Arbeitsordner, und damit auch den Adapter als safetensors. Das ändert sich: Jeder Arbeitsordner bleibt.
- **Versionen:**
  - Jede Runde gibt eine eigene Version, z.B. `nexi-v1`, `nexi-v2`, `nexi-v3`, je mit eigenem GGUF-Adapter.
  - Die KI zeigt auf die neueste Version.
  - Man kann zu einer älteren zurück, wenn eine Runde schlechter wird.
  - `run.json` merkt sich, von welcher Version eine Runde ausging, mit Datensatz, Durchgängen und Loss.
- **Oberfläche (Training-Tab):**
  - Unter „Grundmodell“ die Wahl „Neu beginnen“ oder „Weitertrainieren: <Adapter>“.
  - Die Loss-Kurve zeigt alle Runden hintereinander.
  - Das Lerntempo ist beim Weitertrainieren vorsichtiger voreingestellt.
- **Ehrliche Grenze:** Immer mehr Durchgänge mit denselben Beispielen machen die KI nicht unbedingt besser. Sie lernt sie auswendig, wiederholt sich oder vergisst Allgemeines.
  - Die App warnt, wenn der Loss fast auf null fällt.
  - Sie empfiehlt neue oder bessere Beispiele.

### 2.2 Trainingsdaten mit einer KI erzeugen (T2)

Gleiches Prinzip wie „Prompt verbessern“ beim Video: Eine KI aus der Kopfleiste (am besten eine grosse, z.B. Mael-KI mit Gemma 4) schreibt Beispiele.

- **Aus einer Beschreibung:** z.B. „Sie heisst Nexi, antwortet freundlich auf Schweizerdeutsch und endet mit ‚Gruss, Nexi.‘“. Daraus macht die KI 10–50 Frage-Antwort-Paare.
- **Aus einem Text:** Unterlagen einfügen, z.B. Schulstoff. Die KI macht Fragen und Antworten daraus.
- **Bestehende Beispiele verbessern:** holprige Antworten besser schreiben, dieselbe Frage auf mehrere Arten stellen (mehr Abwechslung).
- **Vorschau zuerst:**
  - Die erzeugten Beispiele kommen nicht direkt in den Datensatz.
  - In einer Liste kann man sie behalten, ändern oder verwerfen.
  - Doppelte Beispiele fallen weg.
- **Technik:**
  - Die KI antwortet als JSON Lines (`{"frage": …, "antwort": …}`).
  - Das Lesen ist tolerant, denn kleine Modelle schreiben oft fehlerhaftes JSON. Dafür gibt es `TrainingDataset.ParseLine` schon.
  - In Teilen zu je 10 Beispielen, mit Fortschritt und Abbrechen.
  - Vor dem Training wird das Chat-Modell wie bisher entladen.
- **Ehrliche Grenze:** Die Qualität hängt von der schreibenden KI ab. Ein falsches Beispiel lernt die neue KI mit. Darum ist die Vorschau Pflicht und kein Extra.

### 2.3 Phasen Training 2

| # | Inhalt | Abnahme |
|---|---|---|
| T1 | **Weitertrainieren:** Arbeitsordner behalten, `train.py --resume <adapter>`, Versionen pro KI, Wahl „Neu / Weitertrainieren“, Loss über alle Runden, Warnung bei Auswendiglernen. Unit-Tests für Versionen und `run.json`. | Ich trainiere „Nexi“ mit 10 neuen Beispielen weiter (v2). Nexi kennt danach beides. Ich kann zu v1 zurück. |
| T2 | **Daten mit KI:** „Beispiele erzeugen“ (aus Beschreibung, aus Text), „Beispiele verbessern“, Vorschau mit Behalten/Ändern/Verwerfen, Teile mit Fortschritt und Abbrechen. Unit-Tests für das Lesen der Antworten. | Mael-KI erzeugt 20 Beispiele aus einer Beschreibung. Ich behalte 15 und trainiere damit eine neue KI. |

---

## Teil 3: Szenen-Videos (Keyframes und Verlängern)

### 3.1 Worum es geht

Ein einzelner Clip ist auf diesem Laptop höchstens ca. 10 s lang. Längere Videos entstehen aus **Szenen**:

```
Szene 1 (Text oder Startbild) ──► Clip 1 ──letztes Bild──► Szene 2 ──► Clip 2 ──letztes Bild──► Szene 3 …
                                                                                       │
                                              alle Clips zusammensetzen ◄──────────────┘  ──► ein Video
```

- Jede Szene hat einen **eigenen Prompt** („Der Hund rennt zum Teich“, „Er springt ins Wasser“ …) und eine Länge (2–8 s).
- Ein **Stil-Text** gilt für alle Szenen, z.B. „warmes Abendlicht, Handkamera, Golden Retriever mit rotem Halsband“. Er hält das Aussehen zusammen.
- Szene 2 und folgende starten mit dem **letzten Bild** der vorherigen Szene (Bild zu Video aus V4). So geht das Video ohne Sprung weiter.
- Die **Warteschlange** (V4) arbeitet die Szenen ab, auch über Nacht.
- Am Schluss setzt die App die Clips **zu einem MP4 zusammen**, mit Ton.

### 3.2 Ehrliche Grenzen

| Grenze | Folge | Was hilft |
|---|---|---|
| **Abdrift** | Über viele Szenen verändern sich Gesichter, Farben und Figuren langsam (jedes Startbild ist schon leicht verändert). | Stil-Text in jeder Szene, kürzere Videos (1–2 Minuten), bei Bedarf ein festes Referenzbild (S3). |
| **Ton pro Szene** | Jede Szene hat eigenen Ton. Musik oder Sprache springen an den Übergängen. | Kurzes Überblenden (ca. 0.2 s). Durchgehende Musik ist ein Fall für die spätere Musikgenerierung. |
| **Dauer** | 1 Minute = ca. 15 Szenen à 4 s, mit distilled ca. 2½ min pro Szene: **ca. 40 Minuten** (768×512). Mit dev das Doppelte. Für Mael ist Zeit kein Problem, die App zeigt die Schätzung trotzdem an. | Klein testen (512×320), dann gross über Nacht. |
| **Eine Szene ändern** | Szene 5 neu machen ändert ihr letztes Bild, also passen Szene 6 und folgende nicht mehr. | Knopf „Ab hier neu erzeugen“. |

### 3.3 Ablauf und Daten

- **Projekt** = Ordner `data/videos/projekte/<name>/` mit `projekt.json`:
  - Stil, Format, Modell, Ablauf (dev/distilled) und Ton
  - die Szenen mit Prompt, Länge, Seed und Status (offen / fertig / Fehler)
  - für jede fertige Szene: Clip und letztes Bild
- **Fortsetzen:** Bricht die App ab oder wird der Laptop zugeklappt, macht das Projekt beim nächsten Start dort weiter, wo es stand.
- **Letztes Bild:** Es kommt aus dem fertigen Clip (PyAV liest das letzte Bild, das Paket ist in der Python-Umgebung von ComfyUI schon da) oder direkt aus ComfyUI über einen zweiten `ImageFromBatch`-Knoten mit Index −1. Das wird in S0 entschieden.
- **Zusammensetzen:** Ein kleines Python-Skript `python/video/szenen_zusammen.py` mit PyAV (kein zusätzliches ffmpeg nötig):
  - Das erste Bild jeder Folgeszene fällt weg, es ist ja das letzte Bild der vorherigen.
  - Der Ton wird an den Übergängen kurz überblendet.
  - Das Ergebnis ist eine MP4 in der Galerie, mit dem Projekt verknüpft.
- **Prompt verbessern** geht pro Szene. Eine KI kann auch aus einer kurzen Geschichte eine Szenenliste vorschlagen (Knopf „Szenen vorschlagen“).

### 3.4 Oberfläche

- Im Studio ein dritter Modus: **Video | Bild | Szenen**.
- **Links:**
  - Projekt wählen oder neu anlegen.
  - Stil-Text, Format, Modell und Ablauf.
  - Die Szenenliste, je mit Prompt und Länge, verschiebbar, mit „Prompt verbessern“.
  - Gesamtlänge und geschätzte Rechenzeit.
- **Knöpfe:** „Alle Szenen erzeugen“, „Ab hier neu erzeugen“, „Zusammensetzen“, „Abbrechen“.
- **Rechts:**
  - Pro Szene ein Vorschaubild mit Status.
  - Der Player spielt das zusammengesetzte Video oder eine einzelne Szene.

### 3.5 Phasen Szenen

| # | Inhalt | Abnahme |
|---|---|---|
| **S0** | **Machbarkeit (Skript, ohne App):** 3 Szenen à 4 s mit dem offiziellen Modell, jede mit dem letzten Bild der vorherigen. Zusammensetzen mit PyAV. Abdrift und Übergänge (Bild und Ton) beurteilen. Kein neuer Download. | Ein Video mit 12 s aus 3 Szenen, ohne sichtbaren Sprung. Messwerte und Beobachtungen in `docs/10-Video.md`. |
| S1 | **Projekte und Ablauf:** `SceneProject` (Core, JSON), letztes Bild sichern, Szenen über die Warteschlange, Fortsetzen nach Neustart, „Ab hier neu“. Unit-Tests. | Ein Projekt mit 5 Szenen läuft durch. Nach einem Neustart mitten drin macht es weiter. |
| S2 | **Zusammensetzen und Oberfläche:** `szenen_zusammen.py`, Modus „Szenen“ im Studio, Vorschau pro Szene, Gesamt-Player, „Szenen vorschlagen“ mit einer KI. | Ich beschreibe 6 Szenen, lasse sie erzeugen, setze sie zu einem Video von ca. 24 s zusammen und spiele es ab. |
| S3 | **Optional, Keyframes und Halt:** pro Szene ein eigenes Start- oder Endbild (z.B. aus der Bildgenerierung, über `LTXVAddGuide`), ein Referenzbild gegen Abdrift. | Eine Szene endet auf einem vorgegebenen Bild. |

---

## 4. Reihenfolge und Aufwand

Festgelegt von Mael (2.11.2026): **Bild → Training 2 → Szenen.**

| Phase | Aufwand (Schätzung) | Hängt ab von |
|---|---|---|
| B0–B2 Bild | klein bis mittel | B0 braucht einen Download (fragen) |
| T1 Weitertrainieren | klein bis mittel | nichts (Training aus M8) |
| T2 Daten mit KI | klein bis mittel | nichts, besser nach T1 |
| S0 Szenen-Test | klein | nichts, nutzt Bild zu Video aus V4 |
| S1–S2 Szenen | mittel | S0 |
| S3 Keyframes | mittel | S2, besser nach der Bildgenerierung |

## 5. Danach (Ideen-Liste, noch nicht geplant)

In dieser Reihenfolge, wie besprochen:

1. PDF-, Word- und Excel-Dateien im Chat lesen.
2. „Wissen“-Ordner: Fragen an die eigenen Dateien beantworten (RAG).
3. Musikgenerierung (ACE-Step über ComfyUI).
4. Dateien umschreiben und speichern.

Weitere Ideen:
- **Kleine Ideen:** Antwort vorlesen, Chat exportieren, Galerie durchsuchen.
- **Mittlere Ideen:** Video hochskalieren, Werkzeuge für die KI.
