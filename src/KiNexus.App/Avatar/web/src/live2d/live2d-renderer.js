// Stellt ein Live2D-Modell (Cubism 3/4/5) mit PixiJS und pixi-live2d-display dar.
// Dieses Paket (js/live2d-bundle.js) wird erst geladen, wenn Cubism Core da ist.
//
// Ablauf pro Bild in pixi-live2d-display:
//   Motion → "afterMotionUpdate" → Ausdruck, Physik → "beforeModelUpdate" → Zeichnen
// Kopf und Körper setzen wir bei "afterMotionUpdate", damit die Haarphysik mitschwingt.
// Augen, Mund und Gefühle setzen wir bei "beforeModelUpdate".

import * as PIXI from 'pixi.js';
import { Live2DModel, MotionPriority } from 'pixi-live2d-display-lipsyncpatch/cubism4';
import { PRESET_TO_EMOTION } from '../expression-mixer.js';

const THEME_BACKGROUND = 0x0a0e13;
const RAD_TO_DEG = 180 / Math.PI;

// Grobe Gefühle über Standard-Parameter, falls kein passender Ausdruck (exp3) zugeordnet ist.
const EMOTION_PARAMS = {
  freude: { ParamEyeLSmile: 1, ParamEyeRSmile: 1, ParamMouthForm: 1, ParamCheek: 0.6 },
  traurig: { ParamMouthForm: -0.8, ParamBrowLY: -0.4, ParamBrowRY: -0.4, ParamBrowLForm: -0.6, ParamBrowRForm: -0.6 },
  ueberrascht: { ParamEyeLOpen: 0.3, ParamEyeROpen: 0.3, ParamBrowLY: 0.8, ParamBrowRY: 0.8, ParamMouthOpenY: 0.25 },
  wuetend: { ParamBrowLAngle: -0.8, ParamBrowRAngle: -0.8, ParamBrowLY: -0.3, ParamBrowRY: -0.3, ParamMouthForm: -0.5 },
  entspannt: { ParamEyeLOpen: -0.3, ParamEyeROpen: -0.3, ParamMouthForm: 0.3 },
};

// Namen von Ausdrücken, die wahrscheinlich zu einem Gefühl passen.
const EMOTION_HINTS = {
  freude: ['happy', 'smile', 'joy', 'fun', 'laugh', 'freude'],
  traurig: ['sad', 'sorrow', 'cry', 'traurig'],
  ueberrascht: ['surprise', 'shock', 'ueberrascht'],
  wuetend: ['angry', 'anger', 'mad', 'wuetend'],
  entspannt: ['relax', 'calm', 'sleepy', 'entspannt'],
};

const VOWEL_PARAMS = { aa: 'ParamA', ih: 'ParamI', ou: 'ParamU', ee: 'ParamE', oh: 'ParamO' };

export class Live2dRenderer {
  constructor(container) {
    this.kind = 'live2d';
    this.container = container;
    this.model = null;
    this.fileName = '';
    this.manual = new Map();
    this.framing = 'oberkoerper';
    this.userZoom = 1;
    this.userOffset = { x: 0, y: 0 };
    this.input = null;
    this.paramIndex = new Map();
    this.emotionMap = {};
    this.activeExpression = null;
    this.manualExpression = null;
    this.idleMotions = false;
    this.idleGroup = null;
    this.pendingScreenshot = null;

    this.app = new PIXI.Application({
      width: Math.max(1, container.clientWidth),
      height: Math.max(1, container.clientHeight),
      backgroundAlpha: 1,
      background: THEME_BACKGROUND,
      antialias: true,
      autoStart: false,
      resolution: Math.min(window.devicePixelRatio || 1, 2),
      autoDensity: true,
    });
    this.canvas = this.app.view;
    this.canvas.classList.add('avatar-canvas');
    container.appendChild(this.canvas);
    this.setupInteraction();
  }

  // ---------- Laden ----------

  // source: Liste von File-Objekten (Ordner oder ZIP) oder eine URL zur .model3.json
  async load(source, fileName) {
    // Fehlt z.B. eine Textur, wartet pixi-live2d-display sonst ewig. Darum ein Zeitlimit.
    const timeout = new Promise((_, reject) => {
      setTimeout(() => reject(new Error('Das Live2D-Modell lädt nicht fertig. Fehlt eine Datei (z.B. eine Textur)?')), 30000);
    });
    const model = await Promise.race([
      Live2DModel.from(source, { autoUpdate: false, autoHitTest: false, autoFocus: false }),
      timeout,
    ]);
    this.unload();
    this.model = model;
    this.fileName = fileName;
    model.anchor.set(0.5, 0);
    this.app.stage.addChild(model);

    const internal = model.internalModel;
    // Eigenes Blinzeln und Atmen des Modells aus, das machen wir selbst.
    internal.eyeBlink = undefined;
    internal.breath = undefined;
    // Idle-Motions zuerst aus. Sie würden sonst Kopf und Augen übersteuern.
    this.idleGroup = internal.motionManager.groups.idle;
    this.setIdleMotions(this.idleMotions);

    this.buildParamIndex();
    this.emotionMap = this.guessEmotionMap();
    this.activeExpression = null;
    this.manualExpression = null;
    this.manual.clear();
    this.userZoom = 1;
    this.userOffset = { x: 0, y: 0 };

    internal.on('afterMotionUpdate', () => this.applyBody());
    internal.on('beforeModelUpdate', () => this.applyFace());
    this.setFraming(this.framing);
  }

  unload() {
    if (!this.model) return;
    this.app.stage.removeChild(this.model);
    this.model.destroy();
    this.model = null;
    this.fileName = '';
    this.paramIndex.clear();
  }

  get loaded() {
    return Boolean(this.model);
  }

  buildParamIndex() {
    this.paramIndex.clear();
    const core = this.model.internalModel.coreModel;
    const ids = core.getModel().parameters.ids;
    for (let i = 0; i < ids.length; i += 1) {
      this.paramIndex.set(ids[i], i);
    }
  }

  get expressionNames() {
    const manager = this.model && this.model.internalModel.motionManager.expressionManager;
    return manager ? manager.definitions.map((d) => d.Name || d.name) : [];
  }

  get motionGroups() {
    if (!this.model) return [];
    const definitions = this.model.internalModel.motionManager.definitions;
    return Object.entries(definitions).map(([group, list]) => ({
      group,
      motions: list.map((m, index) => ({ index, file: (m.File || m.file || '').split('/').pop() })),
    }));
  }

  metaInfo() {
    if (!this.model) return { version: 'unbekannt', title: '', rows: [] };
    const settings = this.model.internalModel.settings;
    const motionCount = this.motionGroups.reduce((sum, g) => sum + g.motions.length, 0);
    return {
      version: 'Live2D Cubism',
      title: settings.name || this.fileName,
      rows: [
        ['Name', settings.name || this.fileName],
        ['Parameter', String(this.paramIndex.size)],
        ['Ausdrücke', String(this.expressionNames.length)],
        ['Motions', String(motionCount)],
        ['Physik', settings.physics ? 'ja' : 'nein'],
        ['Lizenz', 'Live2D-Modelle enthalten keine Lizenz-Infos. Prüf die Regeln beim Anbieter.'],
      ],
    };
  }

  controlList() {
    if (!this.model) return { expressions: [], parameters: [] };
    const core = this.model.internalModel.coreModel.getModel().parameters;
    const parameters = [];
    for (let i = 0; i < core.ids.length; i += 1) {
      parameters.push({
        name: core.ids[i],
        min: core.minimumValues[i],
        max: core.maximumValues[i],
        value: this.manual.has(core.ids[i]) ? this.manual.get(core.ids[i]) : core.defaultValues[i],
        defaultValue: core.defaultValues[i],
      });
    }
    return { expressions: this.expressionNames.map((name) => ({ name })), parameters };
  }

  hasParameter(id) {
    return this.paramIndex.has(id);
  }

  hasExpression(name) {
    return this.expressionNames.includes(name);
  }

  // Parameter von Hand festsetzen. value = null gibt ihn wieder frei.
  setManual(id, value) {
    if (value == null) this.manual.delete(id);
    else this.manual.set(id, value);
  }

  clearManual() {
    this.manual.clear();
    if (this.manualExpression) {
      this.manualExpression = null;
      this.applyExpression(null);
    }
  }

  // Ausdruck (exp3) von Hand setzen. Solange er gesetzt ist, wechseln die Gefühle
  // den Ausdruck nicht mehr. name = null gibt ihn wieder frei.
  setExpression(name) {
    this.manualExpression = name || null;
    this.applyExpression(this.manualExpression);
  }

  applyExpression(name) {
    if (!this.model) return;
    const manager = this.model.internalModel.motionManager.expressionManager;
    if (!manager) return;
    if (name) {
      this.model.expression(name);
    } else {
      manager.resetExpression();
    }
    this.activeExpression = name;
  }

  setEmotionMapping(emotion, expression) {
    this.emotionMap[emotion] = expression || null;
  }

  guessEmotionMap() {
    const names = this.expressionNames;
    const map = {};
    for (const [emotion, hints] of Object.entries(EMOTION_HINTS)) {
      const match = names.find((n) => hints.some((hint) => n.toLowerCase().includes(hint)));
      map[emotion] = match || null;
    }
    return map;
  }

  playMotion(group, index) {
    if (!this.model) throw new Error('Lade zuerst ein Live2D-Modell.');
    return this.model.motion(group, index, MotionPriority.FORCE);
  }

  stopMotions() {
    if (this.model) this.model.internalModel.motionManager.stopAllMotions();
  }

  setIdleMotions(enabled) {
    this.idleMotions = Boolean(enabled);
    if (!this.model) return;
    const manager = this.model.internalModel.motionManager;
    manager.groups.idle = this.idleMotions ? this.idleGroup : '__aus__';
    if (!this.idleMotions) manager.stopAllMotions();
  }

  get motionPlaying() {
    return Boolean(this.model && !this.model.internalModel.motionManager.isFinished());
  }

  // ---------- Darstellung ----------

  setFraming(framing) {
    this.framing = framing;
    this.userZoom = 1;
    this.userOffset = { x: 0, y: 0 };
    this.layout();
  }

  layout() {
    if (!this.model) return;
    const w = this.app.renderer.screen.width;
    const h = this.app.renderer.screen.height;
    const internal = this.model.internalModel;
    // Höhe des Modells bei Massstab 1 (inklusive Layout aus der model3.json).
    const modelHeight = internal.height || this.model.height / this.model.scale.y;
    // Anteil der Modellhöhe, der sichtbar sein soll, und wo oben angefangen wird.
    const views = { ganz: [1.0, 0.0], oberkoerper: [0.55, 0.0], kopf: [0.3, 0.02] };
    const [visible, top] = views[this.framing] || views.oberkoerper;
    const scale = ((h * 0.95) / (modelHeight * visible)) * this.userZoom;
    this.model.scale.set(scale);
    this.model.position.set(w / 2 + this.userOffset.x, h * 0.025 - modelHeight * top * scale + this.userOffset.y);
  }

  setupInteraction() {
    let drag = null;
    this.canvas.addEventListener('pointerdown', (e) => {
      drag = { x: e.clientX, y: e.clientY, ox: this.userOffset.x, oy: this.userOffset.y };
      this.canvas.setPointerCapture(e.pointerId);
    });
    this.canvas.addEventListener('pointermove', (e) => {
      if (!drag) return;
      this.userOffset.x = drag.ox + (e.clientX - drag.x);
      this.userOffset.y = drag.oy + (e.clientY - drag.y);
      this.layout();
    });
    this.canvas.addEventListener('pointerup', () => { drag = null; });
    this.canvas.addEventListener('wheel', (e) => {
      e.preventDefault();
      this.userZoom = Math.max(0.2, Math.min(6, this.userZoom * (e.deltaY < 0 ? 1.1 : 0.9)));
      this.layout();
    }, { passive: false });
  }

  setBackground(mode, color) {
    const background = this.app.renderer.background;
    if (mode === 'theme') {
      background.color = THEME_BACKGROUND;
      background.alpha = 1;
    } else if (mode === 'farbe') {
      background.color = color;
      background.alpha = 1;
    } else {
      background.alpha = 0;
    }
  }

  setPixelRatio(ratio) {
    this.app.renderer.resolution = ratio;
    this.resize();
  }

  resize() {
    const width = Math.max(1, this.container.clientWidth);
    const height = Math.max(1, this.container.clientHeight);
    this.app.renderer.resize(width, height);
    this.layout();
  }

  show(visible) {
    this.canvas.style.display = visible ? 'block' : 'none';
  }

  screenshot() {
    return new Promise((resolve) => {
      this.pendingScreenshot = resolve;
    });
  }

  captureStream(fps) {
    return this.canvas.captureStream(fps);
  }

  // ---------- Pro Bild ----------

  apply(input, delta) {
    this.input = input;
    if (this.model) {
      this.model.update(delta * 1000);
    }
  }

  setParam(id, value) {
    const index = this.paramIndex.get(id);
    if (index === undefined) return;
    const core = this.model.internalModel.coreModel;
    const raw = core.getModel().parameters;
    core.setParameterValueByIndex(index, Math.max(raw.minimumValues[index], Math.min(raw.maximumValues[index], value)));
  }

  addParam(id, value) {
    const index = this.paramIndex.get(id);
    if (index === undefined) return;
    const core = this.model.internalModel.coreModel;
    this.setParam(id, core.getParameterValueByIndex(index) + value);
  }

  // Kopf, Körper und Atmen (vor der Physik).
  applyBody() {
    const input = this.input;
    if (!input) return;
    // Läuft eine Motion, steuert sie den Kopf. Ausser die Gesichtserkennung ist aktiv.
    if (this.motionPlaying && !input.trackingActive) return;

    const { head, body, breath } = input.pose;
    // Live2D: AngleX positiv = Blick zur Bildschirm-Rechten (= linke Seite der Figur).
    this.setParam('ParamAngleX', head.y * RAD_TO_DEG * 1.5);
    this.setParam('ParamAngleY', -head.x * RAD_TO_DEG * 1.5);
    this.setParam('ParamAngleZ', -head.z * RAD_TO_DEG * 1.5);
    this.setParam('ParamBodyAngleX', head.y * RAD_TO_DEG * 0.4 + body.y * RAD_TO_DEG);
    this.setParam('ParamBodyAngleY', -body.x * RAD_TO_DEG * 2);
    this.setParam('ParamBodyAngleZ', -head.z * RAD_TO_DEG * 0.3);
    this.setParam('ParamBreath', breath);
  }

  // Augen, Mund, Gefühle und Blick (nach der Physik).
  applyFace() {
    const input = this.input;
    if (!input) return;
    const { face, gaze } = input;

    // Augen offen = 1, zu = 0.
    this.setParam('ParamEyeLOpen', 1 - face.blink.left);
    this.setParam('ParamEyeROpen', 1 - face.blink.right);

    // Mund: Öffnung aus allen Formen, Breite aus ee/ih minus ou.
    const m = face.mouth;
    const open = Math.max(m.aa, m.oh * 0.9, m.ou * 0.6, m.ee * 0.6, m.ih * 0.5);
    this.setParam('ParamMouthOpenY', open);
    this.setParam('ParamMouthForm', (m.ee + m.ih) * 0.5 - m.ou * 0.6);
    for (const [shape, id] of Object.entries(VOWEL_PARAMS)) {
      this.setParam(id, m[shape]);
    }

    // Gefühle: zugeordneter Ausdruck (exp3) oder grobe Parameter.
    let strongest = null;
    let strongestWeight = 0;
    for (const [preset, weight] of face.emotions) {
      const emotion = PRESET_TO_EMOTION[preset];
      if (!emotion || weight <= 0) continue;
      if (weight > strongestWeight) {
        strongest = emotion;
        strongestWeight = weight;
      }
      if (!this.emotionMap[emotion]) {
        for (const [id, value] of Object.entries(EMOTION_PARAMS[emotion])) {
          this.addParam(id, value * weight);
        }
      }
    }
    if (!this.manualExpression) {
      const wanted = strongest && strongestWeight > 0.3 ? this.emotionMap[strongest] : null;
      if (wanted !== this.activeExpression) {
        this.applyExpression(wanted);
      }
    }

    // Blick
    if (gaze.mode !== 'off') {
      this.setParam('ParamEyeBallX', gaze.mode === 'camera' ? 0 : gaze.x);
      this.setParam('ParamEyeBallY', gaze.mode === 'camera' ? 0 : gaze.y);
    }

    // Von Hand gesetzte Parameter gewinnen immer.
    for (const [id, value] of this.manual) {
      this.setParam(id, value);
    }
  }

  render() {
    this.app.render();
    if (this.pendingScreenshot) {
      const resolve = this.pendingScreenshot;
      this.pendingScreenshot = null;
      this.canvas.toBlob(resolve, 'image/png');
    }
  }
}
