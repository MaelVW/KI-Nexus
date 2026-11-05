// Manuelles Control-Panel. Jeder Knopf schickt einen JSON-Befehl an die
// CommandApi. Im Protokoll unten siehst du genau, welcher Befehl das war.
// Bereiche mit der Klasse "only-vrm" oder "only-live2d" erscheinen nur beim passenden Modell.

import { STATES } from './idle-animator.js';
import { MOUTH_SHAPES } from './expression-mixer.js';
import { live2dSupport, coreVersion } from './renderers/live2d-support.js';

const STATE_LABELS = { idle: 'Leerlauf', listening: 'Zuhören', thinking: 'Nachdenken', speaking: 'Sprechen' };
const EMOTION_LABELS = {
  neutral: 'Neutral', freude: 'Freude', traurig: 'Traurig',
  ueberrascht: 'Überrascht', wuetend: 'Wütend', entspannt: 'Entspannt',
};
const LIPSYNC_LABELS = { aus: 'Aus', test: 'Test', mikrofon: 'Mikrofon', datei: 'Datei' };
const LOOK_LABELS = { kamera: 'Kamera', maus: 'Maus', aus: 'Aus' };
const FRAMING_LABELS = { kopf: 'Kopf', oberkoerper: 'Oberkörper', ganz: 'Ganz' };
const BACKGROUND_LABELS = { transparent: 'Transparent', theme: 'Theme', farbe: 'Farbe', bild: 'Bild' };
const FPS_OPTIONS = [[30, '30'], [60, '60'], [0, 'Max']];

const EXPRESSION_GROUPS = [
  ['Gefühle', ['happy', 'angry', 'sad', 'relaxed', 'surprised', 'neutral']],
  ['Mund', MOUTH_SHAPES],
  ['Augen', ['blink', 'blinkLeft', 'blinkRight', 'lookUp', 'lookDown', 'lookLeft', 'lookRight']],
];

const EXAMPLE_TEXT = 'Hallo! [[gefuehl:freude]] Schön, dass du wieder da bist. '
  + 'Hast du heute viel gelernt? [[gefuehl:ueberrascht]] Was, schon wieder eine neue Idee? '
  + '[[gefuehl:entspannt]] Erzähl mir alles in Ruhe.';

const STORAGE_KEY = 'ki-nexus-avatar-testpanel';
const MAX_LOG_ENTRIES = 80;

// ---------- kleine DOM-Helfer ----------

function el(tag, props = {}, children = []) {
  const node = document.createElement(tag);
  for (const [key, value] of Object.entries(props)) {
    if (key === 'class') node.className = value;
    else if (key === 'text') node.textContent = value;
    else if (key.startsWith('on')) node.addEventListener(key.slice(2), value);
    else if (value === true) node.setAttribute(key, '');
    else if (value !== false && value != null) node.setAttribute(key, value);
  }
  for (const child of [].concat(children)) {
    if (child != null) node.append(child);
  }
  return node;
}

function section(title, open = true) {
  const body = el('div', { class: 'section-body' });
  const root = el('details', { class: 'section', open }, [el('summary', { text: title }), body]);
  return { root, body };
}

function segmented(options, current, onSelect) {
  const buttons = new Map();
  const root = el('div', { class: 'segmented' });
  const entries = Array.isArray(options) ? options : Object.entries(options);
  for (const [value, label] of entries) {
    const button = el('button', { type: 'button', text: label, onclick: () => onSelect(value) });
    buttons.set(String(value), button);
    root.append(button);
  }
  const setActive = (value) => {
    for (const [key, button] of buttons) {
      button.classList.toggle('active', key === String(value));
    }
  };
  setActive(current);
  return { root, setActive };
}

function slider({ label, min, max, step, value, format = (v) => v, onInput, onChange }) {
  const output = el('output', { text: format(value) });
  const input = el('input', { type: 'range', min, max, step, value });
  input.addEventListener('input', () => {
    output.textContent = format(Number(input.value));
    if (onInput) onInput(Number(input.value));
  });
  input.addEventListener('change', () => {
    if (onChange) onChange(Number(input.value));
  });
  const root = el('label', { class: 'row slider' }, [el('span', { class: 'row-label', text: label, title: label }), input, output]);
  const set = (v) => {
    input.value = v;
    output.textContent = format(Number(v));
  };
  return { root, input, set };
}

function toggle(label, checked, onChange) {
  const input = el('input', { type: 'checkbox' });
  input.checked = checked;
  input.addEventListener('change', () => onChange(input.checked));
  const root = el('label', { class: 'row toggle' }, [input, el('span', { class: 'switch' }), el('span', { text: label })]);
  return { root, input };
}

function field(label, control) {
  return el('div', { class: 'row field' }, [el('span', { class: 'row-label', text: label }), control]);
}

function fileInput(accept, onFiles, { multiple = false, directory = false } = {}) {
  const input = el('input', { type: 'file', accept, hidden: true, multiple });
  if (directory) input.setAttribute('webkitdirectory', '');
  input.addEventListener('change', () => {
    const files = [...input.files];
    input.value = '';
    if (files.length) onFiles(files);
  });
  return input;
}

function download(blob, name) {
  const url = URL.createObjectURL(blob);
  el('a', { href: url, download: name }).click();
  setTimeout(() => URL.revokeObjectURL(url), 2000);
}

// ---------- Panel ----------

export class ControlPanel {
  constructor(container, api, avatar, loader) {
    this.container = container;
    this.api = api;
    this.avatar = avatar;
    this.loader = loader;
    this.prefs = this.loadPrefs();
    this.build();
    this.applyPrefs();
    this.startStatusLoop();

    api.onLog((entry) => this.addLog(entry));
    avatar.on((event) => {
      if (event.type === 'loaded' || event.type === 'unloaded') this.refreshModel();
    });
    this.bindKeys();
    this.refreshCoreStatus();
  }

  send(command, options) {
    return this.api.handle(command, options);
  }

  build() {
    const header = el('header', { class: 'panel-header' }, [
      el('div', {}, [el('h1', { text: 'Avatar-Testpanel' }), el('p', { class: 'subtle', text: 'KI-Nexus · Erweiterung E3 · VRM + Live2D' })]),
      el('button', { type: 'button', class: 'ghost', title: 'Panel ausblenden (H)', text: 'Ausblenden', onclick: () => this.togglePanel() }),
    ]);
    this.container.append(
      header,
      this.buildModelSection(),
      this.buildStateSection(),
      this.buildEmotionSection(),
      this.buildMouthSection(),
      this.buildFaceSection(),
      this.buildMotionSection(),
      this.buildAnimationSection(),
      this.buildExpressionSection(),
      this.buildTextSection(),
      this.buildViewSection(),
      this.buildLogSection(),
    );
  }

  // ----- Modell -----

  buildModelSection() {
    const { root, body } = section('Modell');
    const vrmInput = fileInput('.vrm', (files) => this.loader.loadFiles(files));
    const folderInput = fileInput('', (files) => this.loader.loadFiles(files), { directory: true });
    const zipInput = fileInput('.zip', (files) => this.loader.loadFiles(files));
    const coreInput = fileInput('.js', (files) => this.useCoreFile(files[0]));

    this.modelName = el('p', { class: 'model-name', text: 'Noch kein Modell geladen.' });
    this.metaBox = el('dl', { class: 'meta' });
    this.coreStatus = el('span', { class: 'subtle', text: 'wird geprüft …' });

    body.append(
      el('div', { class: 'button-row' }, [
        el('button', { type: 'button', class: 'primary', text: 'VRM wählen', onclick: () => vrmInput.click() }),
        el('button', { type: 'button', class: 'primary', text: 'Live2D-Ordner', onclick: () => folderInput.click() }),
        el('button', { type: 'button', class: 'primary', text: 'Live2D-ZIP', onclick: () => zipInput.click() }),
        el('button', { type: 'button', text: 'Entfernen', onclick: () => this.send({ type: 'unload' }) }),
      ]),
      vrmInput, folderInput, zipInput, coreInput,
      el('p', { class: 'hint', text: 'Oder einfach ins Fenster ziehen: .vrm, Live2D-Ordner oder .zip, .vrma, Bild (Hintergrund), Audio (Lippen).' }),
      el('div', { class: 'core-box' }, [
        el('div', {}, [el('strong', { text: 'Cubism Core (für Live2D): ' }), this.coreStatus]),
        el('div', { class: 'button-row' }, [
          el('button', { type: 'button', text: 'Cubism Core wählen', onclick: () => coreInput.click() }),
          el('button', { type: 'button', class: 'ghost', text: 'Gespeicherte Kopie löschen', onclick: () => { live2dSupport.forgetCore(); this.refreshCoreStatus(); } }),
        ]),
        el('p', { class: 'hint', text: 'Die Datei live2dcubismcore.min.js bekommst du kostenlos bei Live2D (Cubism SDK for Web). Siehe README.' }),
      ]),
      this.modelName,
      this.metaBox,
    );
    return root;
  }

  async useCoreFile(file) {
    try {
      const { needsReload } = await live2dSupport.useCoreFile(file);
      const message = needsReload ? 'Gespeichert. Lade die Seite neu (F5), damit die neue Version gilt.' : 'Cubism Core geladen';
      this.api.log({ direction: 'in', payload: { type: 'cubismCore', name: file.name }, ok: true, message });
    } catch (error) {
      this.api.log({ direction: 'in', payload: { type: 'cubismCore', name: file.name }, ok: false, message: error.message });
    }
    this.refreshCoreStatus();
  }

  refreshCoreStatus() {
    const loaded = live2dSupport.coreLoaded;
    const version = coreVersion();
    this.coreStatus.textContent = loaded ? `Version ${version || '?'} geladen (${live2dSupport.coreSource || 'bereit'})` : 'fehlt';
    this.coreStatus.classList.toggle('ok', loaded);
  }

  refreshModel() {
    const a = this.avatar;
    this.container.dataset.kind = a.kind;
    this.metaBox.replaceChildren();
    if (!a.loaded) {
      this.modelName.textContent = 'Noch kein Modell geladen.';
    } else {
      const info = a.renderer.metaInfo();
      this.modelName.textContent = `${a.fileName} · ${info.version}`;
      for (const [label, value] of info.rows) {
        this.metaBox.append(el('dt', { text: label }), el('dd', { text: value }));
      }
    }
    this.rebuildExpressionControls();
    this.rebuildEmotionMap();
    this.rebuildMotions();
    this.refreshCoreStatus();
  }

  // ----- Zustand -----

  buildStateSection() {
    const { root, body } = section('Zustand');
    const options = Object.fromEntries(STATES.map((s) => [s, STATE_LABELS[s]]));
    this.stateControl = segmented(options, 'idle', (state) => this.send({ type: 'setState', state }));
    this.demoToggle = toggle('Demo-Modus (zufällige Zustände und Sätze)', false, (enabled) => this.send({ type: 'setDemo', enabled }));
    body.append(
      this.stateControl.root,
      this.demoToggle.root,
      el('p', { class: 'hint', text: '"Sprechen" bewegt den Mund mit Test-Sprechen, wenn der Lippen-Modus auf "Aus" steht. Tasten 1–4.' }),
    );
    return root;
  }

  // ----- Gefühl -----

  buildEmotionSection() {
    const { root, body } = section('Gefühl');
    this.emotionStrength = 1;
    this.emotionControl = segmented(EMOTION_LABELS, 'neutral', (name) => this.send({
      type: 'setEmotion', name, strength: this.emotionStrength, holdSeconds: this.avatar.mixer.autoReturnSeconds,
    }));
    this.emotionControl.root.classList.add('grid-3');

    const strength = slider({
      label: 'Stärke', min: 0, max: 1, step: 0.05, value: 1,
      format: (v) => `${Math.round(v * 100)} %`,
      onInput: (v) => { this.emotionStrength = v; },
    });
    this.autoReturnSlider = slider({
      label: 'Zurück zu Neutral', min: 0, max: 20, step: 1, value: this.avatar.mixer.autoReturnSeconds,
      format: (v) => (v === 0 ? 'nie' : `${v} s`),
      onChange: (v) => this.send({ type: 'setEmotionSettings', autoReturnSeconds: v }).then(() => this.savePrefs()),
    });
    this.fadeSlider = slider({
      label: 'Übergang', min: 0, max: 1.5, step: 0.05, value: this.avatar.mixer.fadeSeconds,
      format: (v) => `${v.toFixed(2)} s`,
      onChange: (v) => this.send({ type: 'setEmotionSettings', fadeSeconds: v }).then(() => this.savePrefs()),
    });
    this.emotionMapBox = el('div', { class: 'only-live2d emotion-map' });
    body.append(this.emotionControl.root, strength.root, this.autoReturnSlider.root, this.fadeSlider.root, this.emotionMapBox);
    return root;
  }

  // Live2D: Zuordnung Gefühl → Ausdruck (exp3)
  rebuildEmotionMap() {
    this.emotionMapBox.replaceChildren();
    const r = this.avatar.renderer;
    if (this.avatar.kind !== 'live2d') return;
    this.emotionMapBox.append(
      el('h3', { text: 'Zuordnung zu Live2D-Ausdrücken' }),
      el('p', { class: 'hint', text: '"Parameter" heisst: Die App bewegt Augen, Brauen und Mund selbst.' }),
    );
    for (const emotion of Object.keys(EMOTION_LABELS).filter((e) => e !== 'neutral')) {
      const select = el('select', {}, [el('option', { value: '', text: 'Parameter (automatisch)' })]);
      for (const name of r.expressionNames) {
        select.append(el('option', { value: name, text: name }));
      }
      select.value = r.emotionMap[emotion] || '';
      select.addEventListener('change', () => this.send({ type: 'setLive2dEmotionMap', emotion, expression: select.value || null }));
      this.emotionMapBox.append(field(EMOTION_LABELS[emotion], select));
    }
  }

  // ----- Mund -----

  buildMouthSection() {
    const { root, body } = section('Mund & Lippen', false);
    this.lipControl = segmented(LIPSYNC_LABELS, 'aus', (mode) => this.send({ type: 'setLipSyncMode', mode }));

    const manual = slider({
      label: 'Mund von Hand', min: 0, max: 1, step: 0.01, value: 0,
      format: (v) => `${Math.round(v * 100)} %`,
      onInput: (v) => this.send({ type: 'setMouth', value: v }, { silent: true }),
      onChange: (v) => this.send({ type: 'setMouth', value: v }),
    });
    this.gainSlider = slider({
      label: 'Empfindlichkeit', min: 0.3, max: 5, step: 0.1, value: this.avatar.lipSync.gain,
      format: (v) => v.toFixed(1),
      onChange: (v) => this.send({ type: 'setLipSyncSettings', gain: v }).then(() => this.savePrefs()),
    });
    this.vowelToggle = toggle('Vokale schätzen (experimentell)', true, (on) => this.send({ type: 'setLipSyncSettings', estimateVowels: on }));

    const audioInput = fileInput('audio/*', (files) => this.loader.loadAudio(files[0]));
    this.audioName = el('span', { class: 'subtle', text: 'keine Datei' });
    this.meter = el('div', { class: 'meter-fill' });
    body.append(
      this.lipControl.root,
      manual.root,
      this.gainSlider.root,
      this.vowelToggle.root,
      el('div', { class: 'button-row' }, [
        el('button', { type: 'button', text: 'Audiodatei wählen', onclick: () => audioInput.click() }),
        el('button', { type: 'button', text: 'Abspielen', onclick: () => this.loader.playAudio() }),
        el('button', { type: 'button', text: 'Stopp', onclick: () => this.avatar.lipSync.stopFile() }),
      ]),
      audioInput,
      this.audioName,
      field('Pegel', el('div', { class: 'meter' }, this.meter)),
      el('p', { class: 'hint', text: 'Tipp: Eine WAV-Datei von Piper zeigt dir, wie es später in KI-Nexus aussieht.' }),
    );
    return root;
  }

  // ----- Gesichtserkennung -----

  buildFaceSection() {
    const { root, body } = section('Gesichtserkennung (Webcam)', false);
    const face = this.avatar.face;
    const s = face.settings;
    const set = (key) => (value) => this.send({ type: 'setFaceTrackingSettings', [key]: value }).then(() => this.savePrefs());

    this.faceButton = el('button', { type: 'button', class: 'primary', text: 'Kamera starten', onclick: () => this.toggleFace() });
    this.faceStatus = el('span', { class: 'subtle', text: 'aus' });
    this.facePreview = el('div', { class: 'face-preview' });
    this.faceDebug = el('pre', { class: 'face-debug' });

    this.faceToggles = {
      head: toggle('Kopf übertragen', s.head, set('head')),
      eyes: toggle('Augen (Blinzeln)', s.eyes, set('eyes')),
      mouth: toggle('Mund', s.mouth, set('mouth')),
      gaze: toggle('Blick', s.gaze, set('gaze')),
      emotions: toggle('Gefühle aus dem Gesicht (Lächeln, Brauen)', s.emotions, set('emotions')),
      mirror: toggle('Spiegeln (wie ein Spiegel)', s.mirror, set('mirror')),
      swapEyes: toggle('Augen links/rechts tauschen', s.swapEyes, set('swapEyes')),
      invertYaw: toggle('Drehen umkehren', s.invertYaw, set('invertYaw')),
      invertPitch: toggle('Nicken umkehren', s.invertPitch, set('invertPitch')),
      invertRoll: toggle('Neigen umkehren', s.invertRoll, set('invertRoll')),
    };
    this.faceStrength = slider({
      label: 'Kopf-Stärke', min: 0, max: 2, step: 0.05, value: s.strength,
      format: (v) => `${Math.round(v * 100)} %`, onChange: set('strength'),
    });
    this.faceSmoothing = slider({
      label: 'Glättung', min: 0, max: 1, step: 0.05, value: s.smoothing,
      format: (v) => `${Math.round(v * 100)} %`, onChange: set('smoothing'),
    });

    const t = this.faceToggles;
    body.append(
      el('div', { class: 'button-row' }, [
        this.faceButton,
        el('button', { type: 'button', text: 'Nullpunkt setzen', title: 'Schau mit offenen Augen gerade in die Kamera und klick hier', onclick: () => this.send({ type: 'calibrateFaceTracking' }) }),
      ]),
      field('Status', this.faceStatus),
      this.facePreview,
      t.head.root, t.eyes.root, t.mouth.root, t.gaze.root, t.emotions.root,
      this.faceStrength.root, this.faceSmoothing.root,
      el('p', { class: 'hint', text: 'Tipp: Schau mit offenen Augen gerade in die Kamera und drück "Nullpunkt setzen". Dann stimmen Kopf, Blick und Blinzeln besser.' }),
      el('h3', { text: 'Wenn etwas falsch herum ist' }),
      t.mirror.root, t.swapEyes.root, t.invertYaw.root, t.invertPitch.root, t.invertRoll.root,
      this.faceDebug,
      el('p', { class: 'hint', text: 'Läuft komplett lokal mit MediaPipe (Google, Open Source). Kein Bild verlässt deinen Laptop.' }),
    );
    return root;
  }

  async toggleFace() {
    const face = this.avatar.face;
    if (face.active) {
      await this.send({ type: 'stopFaceTracking' });
      this.facePreview.replaceChildren();
      return;
    }
    this.faceStatus.textContent = 'lädt … (beim ersten Mal ein paar Sekunden)';
    const result = await this.send({ type: 'startFaceTracking' });
    if (result.ok && face.video) {
      face.video.classList.add('face-video');
      this.facePreview.replaceChildren(face.video);
    } else if (!result.ok) {
      this.faceStatus.textContent = `Fehler: ${result.error}`;
    }
  }

  // ----- Augen & Bewegung -----

  buildMotionSection() {
    const { root, body } = section('Augen & Bewegung', false);
    this.blinkToggle = toggle('Automatisch blinzeln', true, (enabled) => this.send({ type: 'setAutoBlink', enabled }).then(() => this.savePrefs()));
    this.lookControl = segmented(LOOK_LABELS, this.avatar.lookMode, (mode) => this.send({ type: 'setLookMode', mode }).then(() => this.savePrefs()));
    this.breathToggle = toggle('Atmen', true, (breathing) => this.send({ type: 'setIdle', breathing }).then(() => this.savePrefs()));
    this.headToggle = toggle('Kleine Kopfbewegungen', true, (headMotion) => this.send({ type: 'setIdle', headMotion }).then(() => this.savePrefs()));
    this.armSlider = slider({
      label: 'Arme senken', min: 0, max: 1.5, step: 0.01, value: this.avatar.animator.armAngle,
      format: (v) => `${Math.round((v * 180) / Math.PI)}°`,
      onInput: (v) => this.send({ type: 'setIdle', armAngle: v }, { silent: true }),
      onChange: (v) => this.send({ type: 'setIdle', armAngle: v }).then(() => this.savePrefs()),
    });
    this.armSlider.root.classList.add('only-vrm');
    body.append(
      this.blinkToggle.root,
      el('div', { class: 'button-row' }, [
        el('button', { type: 'button', text: 'Jetzt blinzeln (B)', onclick: () => this.send({ type: 'blink' }) }),
      ]),
      field('Blick', this.lookControl.root),
      this.breathToggle.root,
      this.headToggle.root,
      this.armSlider.root,
    );
    return root;
  }

  // ----- Animationen -----

  buildAnimationSection() {
    const { root, body } = section('Animationen', false);

    // VRM: VRMA-Dateien
    const vrmaInput = fileInput('.vrma', (files) => this.loader.loadFiles(files));
    this.vrmaName = el('span', { class: 'subtle', text: 'keine Animation geladen' });
    this.loopToggle = toggle('Wiederholen', true, (loop) => this.send({ type: 'setAnimationSettings', loop }));
    const speed = slider({
      label: 'Tempo', min: 0.25, max: 2, step: 0.05, value: 1,
      format: (v) => `${v.toFixed(2)}×`,
      onChange: (v) => this.send({ type: 'setAnimationSettings', speed: v }),
    });
    const vrmBox = el('div', { class: 'only-vrm stack' }, [
      el('div', { class: 'button-row' }, [
        el('button', { type: 'button', class: 'primary', text: 'VRMA-Datei wählen', onclick: () => vrmaInput.click() }),
        el('button', { type: 'button', text: 'Abspielen', onclick: () => this.send({ type: 'playAnimation' }) }),
        el('button', { type: 'button', text: 'Stopp', onclick: () => this.send({ type: 'stopAnimation' }) }),
      ]),
      vrmaInput,
      this.vrmaName,
      this.loopToggle.root,
      speed.root,
      el('p', { class: 'hint', text: 'VRMA = Animationen für VRM. Gratis z.B. im "VRMA Motion Pack" von pixiv (VRoid). Während eine Animation läuft, ruht die eigene Körperhaltung.' }),
    ]);

    // Live2D: Motions aus der model3.json
    this.idleMotionToggle = toggle('Idle-Motions des Modells', false, (enabled) => this.send({ type: 'setModelIdleMotions', enabled }));
    this.motionBox = el('div', { class: 'motion-box' });
    const live2dBox = el('div', { class: 'only-live2d stack' }, [
      this.idleMotionToggle.root,
      el('p', { class: 'hint', text: 'Läuft eine Motion, steuert sie den Kopf. Ausser die Gesichtserkennung ist an.' }),
      this.motionBox,
      el('div', { class: 'button-row' }, [
        el('button', { type: 'button', text: 'Motions stoppen', onclick: () => this.send({ type: 'stopAnimation' }) }),
      ]),
    ]);
    body.append(vrmBox, live2dBox);
    return root;
  }

  rebuildMotions() {
    this.motionBox.replaceChildren();
    if (this.avatar.kind !== 'live2d') return;
    const groups = this.avatar.renderer.motionGroups;
    if (!groups.length) {
      this.motionBox.append(el('p', { class: 'subtle', text: 'Dieses Modell hat keine Motions.' }));
      return;
    }
    for (const { group, motions } of groups) {
      this.motionBox.append(el('h3', { text: group || '(ohne Name)' }));
      const row = el('div', { class: 'button-row' });
      for (const motion of motions) {
        row.append(el('button', {
          type: 'button',
          text: `${motion.index + 1}`,
          title: motion.file,
          onclick: () => this.send({ type: 'playMotion', group, index: motion.index }),
        }));
      }
      this.motionBox.append(row);
    }
  }

  // ----- Alle Ausdrücke / Parameter -----

  buildExpressionSection() {
    const { root, body } = section('Alle Ausdrücke & Parameter', false);
    this.expressionBox = el('div', { class: 'expressions' });
    body.append(
      el('p', { class: 'hint', text: 'VRM: jeder Ausdruck, auch deine eigenen Shape Keys. Live2D: alle Ausdrücke (exp3) und alle Parameter.' }),
      el('div', { class: 'button-row' }, [
        el('button', { type: 'button', text: 'Alles zurücksetzen (Esc)', onclick: () => this.resetExpressions() }),
      ]),
      this.expressionBox,
    );
    this.rebuildExpressionControls();
    return root;
  }

  rebuildExpressionControls() {
    this.expressionSliders = new Map();
    this.expressionBox.replaceChildren();
    const a = this.avatar;
    if (!a.loaded) {
      this.expressionBox.append(el('p', { class: 'subtle', text: 'Lade zuerst ein Modell.' }));
      return;
    }
    const list = a.renderer.controlList();
    if (a.kind === 'vrm') this.buildVrmExpressionSliders(list.expressions.map((e) => e.name));
    else this.buildLive2dControls(list);
  }

  buildVrmExpressionSliders(names) {
    const used = new Set();
    const groups = EXPRESSION_GROUPS.map(([title, list]) => {
      const present = list.filter((n) => names.includes(n));
      present.forEach((n) => used.add(n));
      return [title, present];
    });
    groups.push(['Eigene Ausdrücke', names.filter((n) => !used.has(n))]);
    for (const [title, list] of groups) {
      if (!list.length) continue;
      this.expressionBox.append(el('h3', { text: title }));
      for (const name of list) {
        const s = slider({
          label: name, min: 0, max: 1, step: 0.01, value: 0,
          format: (v) => `${Math.round(v * 100)}`,
          onInput: (v) => this.send({ type: 'setExpression', name, weight: v }, { silent: true }),
          onChange: (v) => this.send({ type: 'setExpression', name, weight: v }),
        });
        this.expressionSliders.set(name, s);
        this.expressionBox.append(s.root);
      }
    }
  }

  buildLive2dControls({ expressions, parameters }) {
    if (expressions.length) {
      this.expressionBox.append(el('h3', { text: 'Ausdrücke (exp3)' }));
      const row = el('div', { class: 'button-row' }, [
        el('button', { type: 'button', text: 'Keiner', onclick: () => this.send({ type: 'setExpression', name: null, weight: 0 }) }),
      ]);
      for (const { name } of expressions) {
        row.append(el('button', { type: 'button', text: name, onclick: () => this.send({ type: 'setExpression', name }) }));
      }
      this.expressionBox.append(row);
    }
    this.expressionBox.append(el('h3', { text: `Parameter (${parameters.length})` }));
    const filter = el('input', { type: 'search', class: 'filter', placeholder: 'Parameter suchen, z.B. Mouth' });
    const list = el('div', { class: 'param-list' });
    for (const p of parameters) {
      const step = Math.max(0.001, (p.max - p.min) / 200);
      const s = slider({
        label: p.name, min: p.min, max: p.max, step, value: p.value,
        format: (v) => v.toFixed(2),
        onInput: (v) => this.send({ type: 'setParameter', id: p.name, value: v }, { silent: true }),
        onChange: (v) => this.send({ type: 'setParameter', id: p.name, value: v }),
      });
      s.root.dataset.name = p.name.toLowerCase();
      s.defaultValue = p.defaultValue;
      this.expressionSliders.set(p.name, s);
      list.append(s.root);
    }
    filter.addEventListener('input', () => {
      const q = filter.value.trim().toLowerCase();
      for (const row of list.children) {
        row.style.display = !q || row.dataset.name.includes(q) ? '' : 'none';
      }
    });
    this.expressionBox.append(filter, el('p', { class: 'hint', text: 'Ein von Hand gesetzter Parameter bleibt fest, bis du "Alles zurücksetzen" drückst.' }), list);
  }

  resetExpressions() {
    this.send({ type: 'resetExpressions' });
    for (const s of this.expressionSliders.values()) {
      s.set(s.defaultValue != null ? s.defaultValue : 0);
    }
  }

  // ----- KI-Text -----

  buildTextSection() {
    const { root, body } = section('KI-Text simulieren', false);
    const textarea = el('textarea', { rows: 5, spellcheck: 'false' });
    textarea.value = EXAMPLE_TEXT;
    this.textResult = el('div', { class: 'text-result subtle', text: 'So würde die App eine KI-Antwort mit Markierungen verarbeiten.' });

    this.voiceToggle = toggle('Mit Browser-Stimme vorlesen', false, (enabled) => this.send({ type: 'setVoice', enabled }).then(() => this.savePrefs()));
    this.voiceSelect = el('select', {});
    this.voiceSelect.addEventListener('change', () => this.send({ type: 'setVoice', voiceUri: this.voiceSelect.value }).then(() => this.savePrefs()));
    this.rateSlider = slider({
      label: 'Sprechtempo', min: 0.5, max: 2, step: 0.05, value: 1,
      format: (v) => `${v.toFixed(2)}×`,
      onChange: (v) => this.send({ type: 'setVoice', rate: v }).then(() => this.savePrefs()),
    });
    const fillVoices = () => {
      const voices = this.avatar.speech.voices();
      this.voiceSelect.replaceChildren(...voices.map((v) => el('option', { value: v.voiceURI, text: `${v.name} (${v.lang})` })));
      if (this.avatar.speech.voiceUri) this.voiceSelect.value = this.avatar.speech.voiceUri;
    };
    if (this.avatar.speech.supported) {
      fillVoices();
      window.speechSynthesis.addEventListener('voiceschanged', fillVoices);
    }

    body.append(
      textarea,
      el('div', { class: 'button-row' }, [
        el('button', { type: 'button', class: 'primary', text: 'Abspielen', onclick: () => this.playText(textarea.value) }),
        el('button', { type: 'button', text: 'Stopp', onclick: () => this.send({ type: 'stopSpeech' }) }),
      ]),
      this.voiceToggle.root,
      field('Stimme', this.voiceSelect),
      this.rateSlider.root,
      el('p', { class: 'hint', text: 'Die Browser-Stimme ist nur zum Testen. In KI-Nexus spricht später Piper. Die Gefühle wechseln dann genau beim passenden Wort.' }),
      this.textResult,
    );
    return root;
  }

  async playText(text) {
    const result = await this.send({ type: 'simulateText', text });
    if (!result.ok) {
      this.textResult.textContent = result.error;
      return;
    }
    this.showTextResult(result);
  }

  showTextResult({ parsed, seconds }) {
    const found = parsed.emotions.map((e) => EMOTION_LABELS[e.name]).join(', ') || 'keine';
    const duration = seconds == null ? 'Browser-Stimme' : `${seconds.toFixed(1)} s`;
    const parts = [
      el('p', {}, [el('strong', { text: 'Text ohne Markierungen: ' }), parsed.cleanText]),
      el('p', { text: `Gefühle: ${found} · Dauer: ${duration}` }),
    ];
    if (parsed.unknown.length) {
      parts.push(el('p', { class: 'error', text: `Unbekannte Gefühle: ${parsed.unknown.join(', ')}` }));
    }
    this.textResult.replaceChildren(...parts);
  }

  // ----- Ansicht -----

  buildViewSection() {
    const { root, body } = section('Ansicht', false);
    const a = this.avatar;
    const save = () => this.savePrefs();
    this.framingControl = segmented(FRAMING_LABELS, a.framing, (framing) => this.send({ type: 'setFraming', framing }).then(save));
    this.colorInput = el('input', { type: 'color', value: a.background.color });
    this.colorInput.addEventListener('input', () => this.send({ type: 'setBackground', mode: 'farbe', color: this.colorInput.value }, { silent: true }));
    this.colorInput.addEventListener('change', () => this.send({ type: 'setBackground', mode: 'farbe', color: this.colorInput.value }).then(save));
    this.backgroundControl = segmented(BACKGROUND_LABELS, a.background.mode, (mode) => this.send({ type: 'setBackground', mode, color: this.colorInput.value }).then(save));
    const imageInput = fileInput('image/*', (files) => this.loader.loadBackground(files[0]));
    this.fpsControl = segmented(FPS_OPTIONS, a.maxFps, (fps) => this.send({ type: 'setFps', fps: Number(fps) }).then(save));
    this.pixelSlider = slider({
      label: 'Auflösung', min: 0.5, max: 2, step: 0.25, value: a.pixelRatio,
      format: (v) => `${v.toFixed(2)}×`,
      onChange: (v) => this.send({ type: 'setPixelRatio', value: v }).then(save),
    });

    const light = { intensity: 0.7, ambient: 0.12, angle: 27 };
    this.lightSliders = {
      intensity: slider({ label: 'Licht', min: 0, max: 2, step: 0.05, value: light.intensity, format: (v) => v.toFixed(2), onInput: (v) => this.send({ type: 'setLight', intensity: v }, { silent: true }), onChange: (v) => this.send({ type: 'setLight', intensity: v }).then(save) }),
      ambient: slider({ label: 'Umgebungslicht', min: 0, max: 1, step: 0.02, value: light.ambient, format: (v) => v.toFixed(2), onInput: (v) => this.send({ type: 'setLight', ambient: v }, { silent: true }), onChange: (v) => this.send({ type: 'setLight', ambient: v }).then(save) }),
      angle: slider({ label: 'Lichtrichtung', min: -180, max: 180, step: 1, value: light.angle, format: (v) => `${v}°`, onInput: (v) => this.send({ type: 'setLight', angle: v }, { silent: true }), onChange: (v) => this.send({ type: 'setLight', angle: v }).then(save) }),
    };
    this.fovSlider = slider({ label: 'Blickwinkel (FOV)', min: 10, max: 60, step: 1, value: 30, format: (v) => `${v}°`, onChange: (v) => this.send({ type: 'setFov', fov: v }).then(save) });
    this.rotateToggle = toggle('Automatisch drehen', false, (enabled) => this.send({ type: 'setAutoRotate', enabled, speed: 1.5 }));
    this.windStrength = slider({ label: 'Wind', min: 0, max: 1.5, step: 0.05, value: 0, format: (v) => v.toFixed(2), onChange: (v) => this.send({ type: 'setWind', strength: v }) });
    this.windDirection = slider({ label: 'Windrichtung', min: -180, max: 180, step: 5, value: 90, format: (v) => `${v}°`, onChange: (v) => this.send({ type: 'setWind', direction: v }) });

    this.recordButton = el('button', { type: 'button', text: 'Video aufnehmen', onclick: () => this.toggleRecording() });
    body.append(
      field('Ausschnitt', this.framingControl.root),
      field('Hintergrund', this.backgroundControl.root),
      field('Eigene Farbe', this.colorInput),
      el('div', { class: 'button-row' }, [el('button', { type: 'button', text: 'Hintergrundbild wählen', onclick: () => imageInput.click() })]),
      imageInput,
      field('Bildrate', this.fpsControl.root),
      this.pixelSlider.root,
      el('div', { class: 'only-vrm stack' }, [
        el('h3', { text: 'Licht & Kamera (VRM)' }),
        this.lightSliders.intensity.root,
        this.lightSliders.ambient.root,
        this.lightSliders.angle.root,
        this.fovSlider.root,
        this.rotateToggle.root,
        el('h3', { text: 'Wind für Haare & Kleidung (VRM)' }),
        this.windStrength.root,
        this.windDirection.root,
      ]),
      el('div', { class: 'button-row' }, [
        el('button', { type: 'button', text: 'Kamera zurücksetzen', onclick: () => this.send({ type: 'setFraming', framing: a.framing }) }),
        el('button', { type: 'button', text: 'Screenshot', onclick: () => this.saveScreenshot() }),
        this.recordButton,
      ]),
      el('p', { class: 'hint', text: 'VRM: links drehen, rechts verschieben, Rad zoomen. Live2D: ziehen verschiebt, Rad zoomt.' }),
    );
    return root;
  }

  async saveScreenshot() {
    const blob = await this.avatar.screenshot();
    if (blob) download(blob, `avatar-${Date.now()}.png`);
  }

  async toggleRecording() {
    const a = this.avatar;
    if (a.recorder.recording) {
      const blob = await a.stopRecording();
      if (blob) download(blob, `avatar-${Date.now()}.webm`);
      this.api.log({ direction: 'in', payload: { type: 'recording', action: 'stop' }, ok: true, message: 'Video gespeichert' });
      return;
    }
    try {
      a.startRecording();
      this.api.log({ direction: 'in', payload: { type: 'recording', action: 'start' }, ok: true, message: '' });
    } catch (error) {
      this.api.log({ direction: 'in', payload: { type: 'recording', action: 'start' }, ok: false, message: error.message });
    }
  }

  // ----- Protokoll -----

  buildLogSection() {
    const { root, body } = section('Befehle (Protokoll)', false);
    this.logList = el('ol', { class: 'log' });
    const input = el('textarea', { rows: 3, spellcheck: 'false', placeholder: '{ "type": "setEmotion", "name": "freude" }' });
    body.append(
      el('p', { class: 'hint', text: 'Genau diese JSON-Befehle schickt später KI-Nexus an den Avatar. Alle Befehle stehen im README.' }),
      this.logList,
      input,
      el('div', { class: 'button-row' }, [
        el('button', { type: 'button', class: 'primary', text: 'Befehl senden', onclick: () => this.sendRaw(input.value) }),
        el('button', { type: 'button', text: 'Protokoll leeren', onclick: () => this.logList.replaceChildren() }),
      ]),
    );
    return root;
  }

  sendRaw(text) {
    try {
      this.send(JSON.parse(text));
    } catch (error) {
      this.addLog({ time: new Date(), direction: 'in', payload: text, ok: false, message: `Kein gültiges JSON: ${error.message}` });
    }
  }

  addLog(entry) {
    const time = entry.time.toLocaleTimeString('de-CH');
    const arrow = entry.direction === 'out' ? '←' : '→';
    const payload = typeof entry.payload === 'string' ? entry.payload : JSON.stringify(entry.payload);
    const item = el('li', { class: entry.ok ? '' : 'error' }, [
      el('span', { class: 'log-time', text: `${time} ${arrow}` }),
      el('code', { text: payload }),
      entry.message ? el('span', { class: 'log-message', text: entry.message }) : null,
    ]);
    this.logList.prepend(item);
    while (this.logList.children.length > MAX_LOG_ENTRIES) {
      this.logList.lastChild.remove();
    }
  }

  // ---------- Status, Tasten, Einstellungen ----------

  startStatusLoop() {
    const chips = document.getElementById('status');
    const update = () => {
      const a = this.avatar;
      // Knöpfe auch dann richtig markieren, wenn ein Befehl von aussen kam.
      this.stateControl.setActive(a.animator.state);
      this.emotionControl.setActive(a.mixer.emotionName);
      this.lipControl.setActive(a.lipSync.mode);
      this.lookControl.setActive(a.lookMode);
      this.framingControl.setActive(a.framing);
      this.backgroundControl.setActive(a.background.mode);
      this.fpsControl.setActive(a.maxFps);
      this.demoToggle.input.checked = a.demo.enabled;
      this.meter.style.width = `${Math.round(a.lipSync.level * 100)}%`;
      this.recordButton.textContent = a.recorder.recording ? 'Aufnahme stoppen' : 'Video aufnehmen';
      this.recordButton.classList.toggle('recording', a.recorder.recording);
      this.updateFaceStatus();
      if (a.kind === 'vrm') {
        this.vrmaName.textContent = a.renderer.animation.name ? `${a.renderer.animation.name}${a.renderer.animationPlaying ? ' · läuft' : ''}` : 'keine Animation geladen';
      }
      if (chips) {
        const face = a.face;
        const items = [
          el('span', { class: 'chip', text: a.loaded ? `${a.kind === 'vrm' ? 'VRM' : 'Live2D'} · ${a.fileName}` : 'kein Modell' }),
          el('span', { class: 'chip', text: STATE_LABELS[a.animator.state] }),
          el('span', { class: 'chip', text: EMOTION_LABELS[a.mixer.emotionName] }),
          face.active ? el('span', { class: `chip ${face.detected ? 'chip-ok' : 'chip-warn'}`, text: face.detected ? 'Gesicht erkannt' : 'kein Gesicht' }) : null,
          a.recorder.recording ? el('span', { class: 'chip chip-rec', text: '● Aufnahme' }) : null,
          el('span', { class: 'chip', text: `${a.measuredFps} FPS` }),
        ];
        chips.replaceChildren(...items.filter(Boolean));
      }
    };
    update();
    setInterval(update, 200);
  }

  updateFaceStatus() {
    const face = this.avatar.face;
    this.faceButton.textContent = face.active ? 'Kamera stoppen' : 'Kamera starten';
    this.facePreview.classList.toggle('mirrored', face.settings.mirror);
    if (face.starting) return;
    if (!face.active) {
      if (!this.faceStatus.textContent.startsWith('Fehler')) this.faceStatus.textContent = 'aus';
      this.faceDebug.textContent = '';
      return;
    }
    this.faceStatus.textContent = `${face.detected ? 'Gesicht erkannt' : 'kein Gesicht'} · ${face.fps} Bilder/s · ${face.delegate}`;
    const l = face.latest;
    if (l && face.detected) {
      const deg = (v) => `${Math.round((v * 180) / Math.PI)}°`.padStart(5);
      this.faceDebug.textContent = [
        `Drehen ${deg(l.head.y)}   Nicken ${deg(l.head.x)}   Neigen ${deg(l.head.z)}`,
        `Augen zu  links ${l.blink.left.toFixed(2)}  rechts ${l.blink.right.toFixed(2)}`,
        `Mund offen ${l.raw.jawOpen.toFixed(2)}   Lächeln ${l.raw.smile.toFixed(2)}`,
        `Blick  x ${l.gaze.x.toFixed(2)}  y ${l.gaze.y.toFixed(2)}`,
      ].join('\n');
    }
  }

  bindKeys() {
    window.addEventListener('keydown', (event) => {
      const tag = (event.target && event.target.tagName) || '';
      if (['INPUT', 'TEXTAREA', 'SELECT'].includes(tag) || event.ctrlKey || event.metaKey || event.altKey) {
        return;
      }
      if (event.key === 'h' || event.key === 'H') this.togglePanel();
      else if (event.key === 'b' || event.key === 'B') this.send({ type: 'blink' });
      else if (event.key === 'Escape') {
        this.resetExpressions();
        this.send({ type: 'setState', state: 'idle' });
      } else if (['1', '2', '3', '4'].includes(event.key)) {
        this.send({ type: 'setState', state: STATES[Number(event.key) - 1] });
      }
    });
  }

  togglePanel() {
    document.body.classList.toggle('panel-hidden');
  }

  loadPrefs() {
    try {
      return JSON.parse(localStorage.getItem(STORAGE_KEY)) || {};
    } catch {
      return {};
    }
  }

  savePrefs() {
    const a = this.avatar;
    const prefs = {
      framing: a.framing,
      background: a.background.mode === 'bild' ? 'theme' : a.background.mode,
      color: a.background.color,
      fps: a.maxFps,
      pixelRatio: a.pixelRatio,
      lookMode: a.lookMode,
      autoBlink: a.mixer.autoBlink,
      breathing: a.animator.breathing,
      headMotion: a.animator.headMotion,
      armAngle: a.animator.armAngle,
      fadeSeconds: a.mixer.fadeSeconds,
      autoReturnSeconds: a.mixer.autoReturnSeconds,
      gain: a.lipSync.gain,
      face: { ...a.face.settings },
      voice: { enabled: this.api.useVoice, voiceUri: a.speech.voiceUri, rate: a.speech.rate },
      light: {
        intensity: Number(this.lightSliders.intensity.input.value),
        ambient: Number(this.lightSliders.ambient.input.value),
        angle: Number(this.lightSliders.angle.input.value),
      },
      fov: Number(this.fovSlider.input.value),
    };
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(prefs));
    } catch {
      // Ohne Speicher geht es auch, die Einstellungen gelten dann nur bis zum Schliessen.
    }
  }

  applyPrefs() {
    const p = this.prefs;
    const quiet = { silent: true };
    const send = (cmd) => this.send(cmd, quiet);
    if (p.framing) send({ type: 'setFraming', framing: p.framing });
    if (p.background) {
      if (p.color) this.colorInput.value = p.color;
      send({ type: 'setBackground', mode: p.background, color: p.color });
    }
    if (p.fps != null) send({ type: 'setFps', fps: p.fps });
    if (p.pixelRatio != null) { send({ type: 'setPixelRatio', value: p.pixelRatio }); this.pixelSlider.set(p.pixelRatio); }
    if (p.lookMode) send({ type: 'setLookMode', mode: p.lookMode });
    if (p.autoBlink != null) { send({ type: 'setAutoBlink', enabled: p.autoBlink }); this.blinkToggle.input.checked = p.autoBlink; }
    if (p.breathing != null) { send({ type: 'setIdle', breathing: p.breathing }); this.breathToggle.input.checked = p.breathing; }
    if (p.headMotion != null) { send({ type: 'setIdle', headMotion: p.headMotion }); this.headToggle.input.checked = p.headMotion; }
    if (p.armAngle != null) { send({ type: 'setIdle', armAngle: p.armAngle }); this.armSlider.set(p.armAngle); }
    if (p.fadeSeconds != null) { send({ type: 'setEmotionSettings', fadeSeconds: p.fadeSeconds }); this.fadeSlider.set(p.fadeSeconds); }
    if (p.autoReturnSeconds != null) { send({ type: 'setEmotionSettings', autoReturnSeconds: p.autoReturnSeconds }); this.autoReturnSlider.set(p.autoReturnSeconds); }
    if (p.gain != null) { send({ type: 'setLipSyncSettings', gain: p.gain }); this.gainSlider.set(p.gain); }
    if (p.face) {
      send({ type: 'setFaceTrackingSettings', ...p.face });
      for (const [key, t] of Object.entries(this.faceToggles)) {
        if (p.face[key] != null) t.input.checked = p.face[key];
      }
      if (p.face.strength != null) this.faceStrength.set(p.face.strength);
      if (p.face.smoothing != null) this.faceSmoothing.set(p.face.smoothing);
    }
    if (p.voice) {
      send({ type: 'setVoice', ...p.voice });
      this.voiceToggle.input.checked = Boolean(p.voice.enabled);
      if (p.voice.rate != null) this.rateSlider.set(p.voice.rate);
      if (p.voice.voiceUri) this.voiceSelect.value = p.voice.voiceUri;
    }
    // Licht und FOV gelten für VRM. Sie werden beim Laden eines VRM wieder angewendet.
    if (p.light) {
      this.avatar.vrm.setLight(p.light);
      this.lightSliders.intensity.set(p.light.intensity);
      this.lightSliders.ambient.set(p.light.ambient);
      this.lightSliders.angle.set(p.light.angle);
    }
    if (p.fov != null) {
      this.avatar.vrm.setFov(p.fov);
      this.fovSlider.set(p.fov);
    }
  }
}
