# Avatar-Testpanel (KI-Nexus, Erweiterung E3) – Version 0.2

Mit diesem Testpanel steuerst du einen Avatar von Hand: **VRM (3D)** oder **Live2D (2D)**. Neu kann der Avatar auch dein Gesicht über die **Webcam** nachmachen.

**Einfach gesagt:** Links steht dein Avatar, rechts sind die Knöpfe. Jeder Knopf schickt einen kleinen JSON-Befehl. Genau diese Befehle schickt später KI-Nexus.

---

## Starten

1. Entpacke das ZIP, z.B. nach `C:\Users\mael\ki\Avatar-Testpanel`.
2. Doppelklick auf **`index.html`**. Die Seite öffnet sich im Browser (Chrome oder Edge empfohlen).
3. Zieh dein Modell ins Fenster oder wähle es oben im Panel.

Du brauchst keine Installation und kein Internet.

---

## Welche Dateien kann ich laden?

| Datei | Was passiert |
|---|---|
| `.vrm` | VRM-Avatar (3D) laden |
| Live2D-**Ordner** (mit `.model3.json`) | Live2D-Avatar laden. Knopf "Live2D-Ordner" oder den Ordner ins Fenster ziehen. |
| Live2D-**ZIP** | Live2D-Avatar aus einer ZIP-Datei laden |
| `.vrma` | VRM-Animation abspielen (nur mit VRM) |
| Bild (`.png`, `.jpg` …) | wird zum Hintergrund |
| Audio (`.wav`, `.mp3` …) | testet die Lippen-Bewegung |

---

## Live2D einrichten (einmal nötig)

Live2D braucht die Datei **Cubism Core**. Sie ist gratis, aber nicht Open Source. Darum ist sie nicht im Paket. Du holst sie selbst:

1. Geh auf <https://www.live2d.com/en/sdk/download/web/>.
2. Lies die Lizenz, akzeptiere sie und lade das **Cubism SDK for Web** herunter.
3. Entpacke es. Die Datei heisst `Core/live2dcubismcore.min.js`.
4. Entweder kopierst du sie in den Ordner **`live2d/`** dieses Testpanels,
   oder du wählst sie im Panel mit **"Cubism Core wählen"**. Dann merkt sich der Browser sie.

**Wichtig:** Es braucht eine aktuelle Version (Cubism 5). Ist sie zu alt, sagt dir das Panel das. Unterstützt werden Live2D-Modelle ab Cubism 3 (`.model3.json`). Ganz alte Cubism-2-Modelle (`.model.json`) gehen nicht.

**Gratis-Modelle zum Testen:** Live2D bietet Beispielmodelle an (z.B. "Hiyori" oder "Haru"): <https://www.live2d.com/en/learn/sample/>. Lies dort die Nutzungsbedingungen.

---

## Was du testen kannst

| Bereich | Was es macht |
|---|---|
| **Modell** | VRM oder Live2D laden. Zeigt Name und (bei VRM) die Nutzungsregeln aus der Datei. |
| **Zustand** | Leerlauf, Zuhören, Nachdenken, Sprechen. Dazu ein **Demo-Modus**, der zufällig Zustände, Gefühle und Sätze abspielt. |
| **Gefühl** | Neutral, Freude, Traurig, Überrascht, Wütend, Entspannt. Mit Stärke, Übergang und automatischer Rückkehr. Bei Live2D kannst du jedem Gefühl einen Ausdruck des Modells zuordnen. |
| **Mund & Lippen** | *Aus* (Regler), *Test* (künstliches Sprechen), *Mikrofon*, *Datei* (z.B. eine WAV von Piper) |
| **Gesichtserkennung** | Die Webcam erkennt dein Gesicht. Der Avatar übernimmt Kopf, Blinzeln, Mund, Blick und auf Wunsch Gefühle. |
| **Augen & Bewegung** | Blinzeln, Blick (Kamera, Maus oder aus), Atmen, Kopfbewegungen, Arme senken (VRM) |
| **Animationen** | VRM: `.vrma`-Dateien mit Wiederholen und Tempo. Live2D: alle Motions des Modells, Idle-Motions ein/aus. |
| **Alle Ausdrücke & Parameter** | VRM: ein Regler pro Ausdruck, auch für eigene Shape Keys. Live2D: alle Ausdrücke und **alle Parameter** mit Suchfeld. |
| **KI-Text simulieren** | Text mit `[[gefuehl:freude]]` abspielen. Auf Wunsch liest die **Browser-Stimme** ihn vor. |
| **Ansicht** | Ausschnitt, Hintergrund (auch Bild), Bildrate, Auflösung. VRM: Licht, Lichtrichtung, Blickwinkel, Auto-Drehen, **Wind** für Haare. Dazu Screenshot und **Video-Aufnahme** (WebM). |
| **Befehle** | Protokoll aller Befehle. Du kannst eigene JSON-Befehle senden. |

### Gesichtserkennung (Webcam)

- Sie nutzt **MediaPipe Face Landmarker** von Google (Open Source, Apache 2.0).
- Alles läuft **lokal** auf deinem Laptop. Kein Bild geht ins Internet.
- Der erste Start dauert ein paar Sekunden, weil das Modell geladen wird.
- **So geht es am besten:**
  1. Klick auf "Kamera starten" und erlaube den Kamera-Zugriff.
  2. Schau mit offenen Augen gerade in die Kamera.
  3. Klick auf **"Nullpunkt setzen"**. Dann stimmen Kopf, Blick und Blinzeln besser.
- Im Spiegel-Modus bewegt sich der Avatar wie dein Spiegelbild. Ist etwas falsch herum, gibt es Schalter zum Umkehren.

### Maus

- **VRM:** links drehen, rechts verschieben, Rad zoomen
- **Live2D:** ziehen verschiebt, Rad zoomt

### Tasten

| Taste | Wirkung |
|---|---|
| `1` bis `4` | Zustand: Leerlauf, Zuhören, Nachdenken, Sprechen |
| `B` | Jetzt blinzeln |
| `Esc` | Alles zurücksetzen (Ausdrücke, Parameter, Gefühl, Zustand) |
| `H` | Panel aus- und einblenden |

Die meisten Einstellungen merkt sich der Browser für das nächste Mal.

---

## Befehle (für KI-Nexus)

Alle Befehle sind JSON-Objekte mit einem Feld `type`.

### Zustand, Gefühl, Gesicht

| Befehl | Beispiel |
|---|---|
| Zustand | `{"type":"setState","state":"thinking"}` (`idle`, `listening`, `thinking`, `speaking`) |
| Gefühl | `{"type":"setEmotion","name":"freude","strength":1,"holdSeconds":6}` |
| Gefühl-Einstellungen | `{"type":"setEmotionSettings","fadeSeconds":0.3,"autoReturnSeconds":6}` |
| Live2D: Gefühl → Ausdruck | `{"type":"setLive2dEmotionMap","emotion":"freude","expression":"f01"}` (`null` = Parameter) |
| Ausdruck | VRM: `{"type":"setExpression","name":"happy","weight":0.5}` · Live2D: `{"type":"setExpression","name":"f02"}` |
| Live2D-Parameter | `{"type":"setParameter","id":"ParamAngleX","value":15}` (`null` gibt ihn frei) |
| Alles zurücksetzen | `{"type":"resetExpressions"}` |
| Blinzeln | `{"type":"blink"}` und `{"type":"setAutoBlink","enabled":true}` |
| Blick | `{"type":"setLookMode","mode":"maus"}` (`kamera`, `maus`, `aus`) |
| Leerlauf | `{"type":"setIdle","breathing":true,"headMotion":true,"armAngle":1.2}` |
| Demo-Modus | `{"type":"setDemo","enabled":true}` |

### Mund und Stimme

| Befehl | Beispiel |
|---|---|
| Mund von Hand | `{"type":"setMouth","value":0.7}` (nur im Lippen-Modus `aus`) |
| Lippen-Modus | `{"type":"setLipSyncMode","mode":"test"}` (`aus`, `test`, `mikrofon`, `datei`) |
| Lippen-Einstellungen | `{"type":"setLipSyncSettings","gain":1.5,"estimateVowels":true}` |
| KI-Text abspielen | `{"type":"simulateText","text":"Hallo! [[gefuehl:freude]] Schön!","voice":false}` |
| Browser-Stimme | `{"type":"setVoice","enabled":true,"rate":1}` |
| Sprechen stoppen | `{"type":"stopSpeech"}` |

### Gesichtserkennung

| Befehl | Beispiel |
|---|---|
| Starten / Stoppen | `{"type":"startFaceTracking"}` · `{"type":"stopFaceTracking"}` |
| Nullpunkt | `{"type":"calibrateFaceTracking"}` |
| Einstellungen | `{"type":"setFaceTrackingSettings","head":true,"eyes":true,"mouth":true,"gaze":true,"emotions":false,"mirror":true,"strength":1,"smoothing":0.5}` |

Weitere Schalter: `swapEyes`, `invertYaw`, `invertPitch`, `invertRoll`.

### Animationen

| Befehl | Beispiel |
|---|---|
| VRMA über URL | `{"type":"loadAnimationUrl","url":"https://avatare.local/winken.vrma"}` |
| VRMA abspielen / stoppen | `{"type":"playAnimation"}` · `{"type":"stopAnimation"}` |
| VRMA-Einstellungen | `{"type":"setAnimationSettings","loop":true,"speed":1}` |
| Live2D-Motion | `{"type":"playMotion","group":"Tap","index":0}` |
| Live2D-Idle-Motions | `{"type":"setModelIdleMotions","enabled":true}` |

### Ansicht

| Befehl | Beispiel |
|---|---|
| Ausschnitt | `{"type":"setFraming","framing":"kopf"}` (`kopf`, `oberkoerper`, `ganz`) |
| Hintergrund | `{"type":"setBackground","mode":"farbe","color":"#112233"}` (`transparent`, `theme`, `farbe`, `bild` mit `imageUrl`) |
| Bildrate / Auflösung | `{"type":"setFps","fps":30}` · `{"type":"setPixelRatio","value":1}` |
| Licht (VRM) | `{"type":"setLight","intensity":0.7,"ambient":0.12,"angle":27}` |
| Blickwinkel (VRM) | `{"type":"setFov","fov":30}` |
| Auto-Drehen (VRM) | `{"type":"setAutoRotate","enabled":true,"speed":1.5}` |
| Wind (VRM) | `{"type":"setWind","strength":0.6,"direction":90}` |

### Modell

| Befehl | Beispiel |
|---|---|
| Laden über URL | `{"type":"loadUrl","url":"https://avatare.local/nova.vrm"}` oder eine `.model3.json` für Live2D |
| Entfernen | `{"type":"unload"}` |

**So schicken Programme die Befehle:**

- **KI-Nexus (WebView2):** mit `PostWebMessageAsJson`. Die Seite hört auf `window.chrome.webview` und schickt Ereignisse wie `loaded` zurück.
- **Browser-Konsole:** `avatarApi.handle({ type: "blink" })`
- **Nur der Avatar ohne Panel:** `index.html?nurAvatar`

---

## Tipps

- **VRoid-Export:** als **VRM 1.0** und ohne Vereinfachung exportieren. Die T-Pose ist normal, das Panel senkt die Arme selbst.
- **Eigene Shape Keys** aus Blender erscheinen unter "Alle Ausdrücke", wenn du sie im VRM-Add-on als Ausdruck einträgst.
- **Gratis-Animationen (VRMA):** z.B. das "VRMA Motion Pack" von pixiv (VRoid). Lies die Nutzungsbedingungen.
- **Nutzungsregeln:** Bei heruntergeladenen Avataren halte dich an die Regeln der Ersteller.

## Wenn etwas nicht geht

| Problem | Lösung |
|---|---|
| Seite bleibt leer | Nimm Chrome oder Edge. Der Browser braucht WebGL2. |
| "Cubism Core fehlt" | Siehe "Live2D einrichten" oben. |
| "Cubism Core … ist zu alt" | Lade die aktuelle Version herunter und wähle sie neu. Danach die Seite neu laden (F5). |
| Live2D lädt nicht fertig | Fehlt eine Datei? Wähl den ganzen Ordner oder die ZIP-Datei, nicht nur die `.model3.json`. |
| "Diese Datei ist kein VRM-Modell" | Die Datei ist ein normales GLB/FBX. Exportiere sie als VRM. |
| Kamera oder Mikrofon geht nicht | Erlaube den Zugriff, wenn der Browser fragt. |
| Gesichtserkennung ist falsch herum | Schalter unter "Wenn etwas falsch herum ist" nutzen, dann "Nullpunkt setzen". |
| Ruckelt | Bildrate auf 30 und Auflösung auf 1× stellen. |
| In der Browser-Konsole steht `ERR_FILE_NOT_FOUND` | Das ist normal, wenn im Ordner `live2d/` noch keine Cubism Core liegt. |

## Grenzen dieser Version

- Live2D: nur Cubism 3 und neuer. Bei Live2D wirkt die "Stärke" eines Gefühls nur, wenn das Gefühl über Parameter läuft (nicht über einen Ausdruck).
- Die Vokal-Schätzung bei Mikrofon und Datei ist grob.
- Die Gefühle aus dem Gesicht sind grob (Lächeln, Brauen, Mundwinkel).
- Die Browser-Stimme ist nur zum Testen. In KI-Nexus kommt die Stimme später von Piper.

---

## Aufbau

```
Avatar-Testpanel/
├── index.html                ← Doppelklick zum Starten
├── README.md                 ← diese Anleitung
├── THIRD_PARTY_NOTICES.txt   ← Lizenzen der Open-Source-Teile
├── live2d/                   ← hier kommt live2dcubismcore.min.js hin
├── css/panel.css             ← Design (dunkel + Cyan, wie KI-Nexus)
├── js/
│   ├── avatar-bundle.js      ← Hauptteil (VRM, Panel, Befehle)
│   ├── live2d-bundle.js      ← Live2D, wird erst bei Bedarf geladen
│   └── face-bundle.js        ← Gesichtserkennung, wird erst bei Bedarf geladen
├── vendor/mediapipe/         ← WebAssembly und Modell der Gesichtserkennung
├── src/                      ← Quellcode
│   ├── main.js               ← Einstieg, Drag & Drop, Sprechblase
│   ├── avatar-controller.js  ← Steuerzentrale: Haltung, Gesicht, Blick pro Bild
│   ├── renderers/
│   │   ├── vrm-renderer.js   ← VRM mit three.js (Licht, Wind, VRMA)
│   │   └── live2d-support.js ← lädt Cubism Core und das Live2D-Paket
│   ├── live2d/
│   │   ├── live2d-renderer.js← Live2D mit PixiJS (Parameter, Motions, Ausdrücke)
│   │   └── live2d-entry.js   ← Einstieg für js/live2d-bundle.js, ZIP-Entpacker
│   ├── face/
│   │   ├── face-tracking.js  ← Webcam, Erkennung, Glättung, Nullpunkt
│   │   ├── face-mapping.js   ← MediaPipe-Werte → Avatar-Werte
│   │   └── face-tracker-entry.js ← Einstieg für js/face-bundle.js
│   ├── expression-mixer.js   ← Gefühle, Blinzeln, Mund
│   ├── idle-animator.js      ← Arme, Atmen, Kopf, Zustände
│   ├── lipsync.js            ← Mund aus Test, Mikrofon oder Datei
│   ├── speech.js             ← Browser-Stimme
│   ├── recorder.js           ← Video-Aufnahme
│   ├── file-loader.js        ← erkennt, welche Datei was ist
│   ├── emotion-tags.js       ← liest [[gefuehl:…]] aus dem Text
│   ├── meta-info.js          ← VRM-Nutzungsregeln auf Deutsch
│   ├── command-api.js        ← alle JSON-Befehle, Verbindung zu WebView2
│   ├── script-loader.js      ← lädt Skripte nach (auch bei file://)
│   └── panel.js              ← das Control-Panel
├── package.json              ← nur zum Neu-Bauen nötig
└── build.cjs                 ← baut src/ zu js/ und vendor/
```

### Neu bauen (nur wenn du `src/` änderst)

Dafür brauchst du [Node.js](https://nodejs.org). Öffne im Ordner eine PowerShell:

```powershell
npm install
npm run build
```

`vendor/mediapipe/` ist schon fertig dabei. Willst du es neu erzeugen (`npm run vendor`), brauchst du das Modell `models/face_landmarker.task` von <https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/1/face_landmarker.task>.

### Für KI-Nexus (E3)

Diesen Ordner kann die KI, die KI-Nexus baut, als Grundlage für `src/KiNexus.App/Avatar/web/` nehmen. Die Befehle oben entsprechen der Schnittstelle `IAvatarView` aus dem Bauplan E3.
