// Steuerzentrale des Avatars. Berechnet pro Bild Haltung, Gesicht und Blick und
// gibt sie an den aktiven Renderer weiter: VRM (3D) oder Live2D (2D).

import { Timer } from 'three';
import { ExpressionMixer, EMOTIONS } from './expression-mixer.js';
import { IdleAnimator, STATES } from './idle-animator.js';
import { LipSync } from './lipsync.js';
import { FaceTracking } from './face/face-tracking.js';
import { BrowserSpeech } from './speech.js';
import { CanvasRecorder } from './recorder.js';
import { VrmRenderer } from './renderers/vrm-renderer.js';
import { live2dSupport } from './renderers/live2d-support.js';

export const FRAMINGS = ['kopf', 'oberkoerper', 'ganz'];
export const LOOK_MODES = ['kamera', 'maus', 'aus'];
export const BACKGROUNDS = ['transparent', 'theme', 'farbe', 'bild'];

const SECONDS_PER_WORD = 0.32;
const DEMO_LINES = [
  'Hallo! [[gefuehl:freude]] Schön, dich zu sehen.',
  'Hmm, lass mich kurz nachdenken. [[gefuehl:entspannt]] Ja, das klingt gut.',
  'Was? [[gefuehl:ueberrascht]] Das hätte ich nicht gedacht!',
  'Oh nein. [[gefuehl:traurig]] Das ist schade.',
];

function lerp(a, b, t) {
  return a + (b - a) * t;
}

export class AvatarController {
  constructor(container) {
    this.container = container;
    this.framing = 'oberkoerper';
    this.lookMode = 'kamera';
    this.maxFps = 60;
    this.pixelRatio = Math.min(window.devicePixelRatio || 1, 2);
    this.background = { mode: 'theme', color: '#18212B', imageUrl: '' };
    this.measuredFps = 0;
    this.listeners = new Set();
    this.speechTimers = [];
    this.pointer = { x: 0, y: 0 };
    this.demo = { enabled: false, nextAt: 0 };
    this.time = 0;

    this.mixer = new ExpressionMixer();
    this.animator = new IdleAnimator();
    this.lipSync = new LipSync();
    this.face = new FaceTracking();
    this.speech = new BrowserSpeech();
    this.recorder = new CanvasRecorder();

    this.vrm = new VrmRenderer(container);
    this.live2d = null;
    this.renderer = this.vrm;
    this.setBackground('theme');

    container.addEventListener('pointermove', (event) => {
      const rect = container.getBoundingClientRect();
      this.pointer.x = ((event.clientX - rect.left) / rect.width) * 2 - 1;
      this.pointer.y = -((event.clientY - rect.top) / rect.height) * 2 + 1;
    });
    new ResizeObserver(() => this.resize()).observe(container);

    this.timer = new Timer();
    this.timer.connect(document);
    this.sinceFrame = 0;
    this.sinceRender = 0;
    this.fpsFrames = 0;
    this.fpsTime = 0;
    const loop = (timestamp) => {
      this.tick(timestamp);
      requestAnimationFrame(loop);
    };
    requestAnimationFrame(loop);
  }

  // ---------- Modelle laden ----------

  get kind() {
    return this.renderer.loaded ? this.renderer.kind : '';
  }

  get fileName() {
    return this.renderer.loaded ? this.renderer.fileName : '';
  }

  get loaded() {
    return this.renderer.loaded;
  }

  async loadVrm(buffer, fileName) {
    await this.vrm.load(buffer, fileName);
    if (this.live2d) this.live2d.unload();
    this.activate(this.vrm);
    this.afterLoad();
  }

  // source: Liste von Dateien (Ordner/ZIP) oder URL zur .model3.json
  async loadLive2d(source, fileName) {
    if (!this.live2d) {
      this.live2d = await live2dSupport.createRenderer(this.container);
    }
    await this.live2d.load(source, fileName);
    this.vrm.unload();
    this.activate(this.live2d);
    this.afterLoad();
  }

  async loadUrl(url) {
    const name = decodeURIComponent(url.split('/').pop() || 'modell');
    if (/\.model3?\.json$/i.test(url)) {
      return this.loadLive2d(url, name);
    }
    const response = await fetch(url);
    if (!response.ok) {
      throw new Error(`Laden fehlgeschlagen (${response.status}): ${url}`);
    }
    return this.loadVrm(await response.arrayBuffer(), name);
  }

  activate(renderer) {
    this.renderer = renderer;
    this.vrm.show(renderer === this.vrm);
    if (this.live2d) this.live2d.show(renderer === this.live2d);
    renderer.setPixelRatio(this.pixelRatio);
    renderer.resize();
    this.setBackground(this.background.mode, this.background.color);
    renderer.setFraming(this.framing);
  }

  afterLoad() {
    this.mixer.reset();
    this.emit({ type: 'loaded', kind: this.renderer.kind, fileName: this.renderer.fileName });
  }

  unload() {
    const wasLoaded = this.renderer.loaded;
    this.vrm.unload();
    if (this.live2d) this.live2d.unload();
    if (wasLoaded) this.emit({ type: 'unloaded' });
  }

  // ---------- Steuerung ----------

  setState(state) {
    if (!this.animator.setState(state)) {
      throw new Error(`Unbekannter Zustand: ${state}`);
    }
    this.emit({ type: 'stateChanged', state });
  }

  setEmotion(name, strength, holdSeconds) {
    if (!this.mixer.setEmotion(name, strength, holdSeconds)) {
      throw new Error(`Unbekanntes Gefühl: ${name}`);
    }
    this.emit({ type: 'emotionChanged', emotion: name });
  }

  setLookMode(mode) {
    if (!LOOK_MODES.includes(mode)) throw new Error(`Unbekannter Blick-Modus: ${mode}`);
    this.lookMode = mode;
  }

  setFraming(framing) {
    if (!FRAMINGS.includes(framing)) throw new Error(`Unbekannter Ausschnitt: ${framing}`);
    this.framing = framing;
    this.renderer.setFraming(framing);
  }

  setBackground(mode, color, imageUrl) {
    if (!BACKGROUNDS.includes(mode)) throw new Error(`Unbekannter Hintergrund: ${mode}`);
    if (color) this.background.color = color;
    if (imageUrl) this.background.imageUrl = imageUrl;
    if (mode === 'bild' && !this.background.imageUrl) {
      throw new Error('Zuerst ein Hintergrundbild wählen.');
    }
    this.background.mode = mode;
    this.container.classList.toggle('bg-transparent', mode === 'transparent');
    this.container.style.backgroundImage = mode === 'bild' ? `url("${this.background.imageUrl}")` : '';
    this.renderer.setBackground(mode, this.background.color);
  }

  setMaxFps(fps) {
    this.maxFps = Math.max(0, Math.round(fps));
  }

  setPixelRatio(ratio) {
    this.pixelRatio = ratio;
    this.renderer.setPixelRatio(ratio);
  }

  setDemo(enabled) {
    this.demo.enabled = Boolean(enabled);
    this.demo.nextAt = this.time + 1;
    if (!enabled) {
      this.cancelSpeech();
      this.setState('idle');
    }
  }

  // Spielt einen KI-Text ab: Zustand Sprechen, Gefühle an den Stellen der Markierungen.
  // useVoice = true: mit der Browser-Stimme vorlesen.
  simulateSpeech(cleanText, emotions, { useVoice = false } = {}) {
    this.cancelSpeech();
    this.setState('speaking');

    if (useVoice && this.speech.supported) {
      const pending = [...emotions];
      const fireUntil = (index) => {
        while (pending.length && pending[0].position <= index) {
          this.setEmotion(pending.shift().name);
        }
      };
      fireUntil(0);
      this.speech.speak(cleanText, {
        onBoundary: fireUntil,
        onEnd: () => {
          fireUntil(Infinity);
          this.setState('idle');
          this.emit({ type: 'speechFinished' });
        },
      });
      return null;
    }

    const words = cleanText.split(/\s+/).filter(Boolean).length;
    const seconds = Math.min(20, Math.max(1.5, words * SECONDS_PER_WORD));
    const length = Math.max(1, cleanText.length);
    for (const emotion of emotions) {
      const delay = seconds * 1000 * (emotion.position / length);
      this.speechTimers.push(setTimeout(() => this.setEmotion(emotion.name), delay));
    }
    this.speechTimers.push(setTimeout(() => {
      this.setState('idle');
      this.emit({ type: 'speechFinished' });
    }, seconds * 1000));
    return seconds;
  }

  cancelSpeech() {
    this.speechTimers.forEach((timer) => clearTimeout(timer));
    this.speechTimers = [];
    this.speech.cancel();
  }

  screenshot() {
    return this.renderer.screenshot();
  }

  startRecording() {
    this.recorder.start(this.renderer.captureStream(this.maxFps || 60));
  }

  stopRecording() {
    return this.recorder.stop();
  }

  resize() {
    this.vrm.resize();
    if (this.live2d) this.live2d.resize();
  }

  // ---------- Ereignisse ----------

  on(listener) {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  emit(event) {
    for (const listener of this.listeners) listener(event);
  }

  // ---------- Schleife ----------

  tick(timestamp) {
    this.timer.update(timestamp);
    const delta = Math.min(this.timer.getDelta(), 0.1);
    this.sinceFrame += delta;
    this.sinceRender += delta;

    // Bildrate begrenzen. Die Toleranz verhindert, dass 60 FPS bei 60 Hz zu 30 FPS werden.
    const minFrame = this.maxFps > 0 ? 1 / this.maxFps : 0;
    if (this.sinceFrame < minFrame * 0.9) return;
    this.sinceFrame = Math.max(0, Math.min(minFrame, this.sinceFrame - minFrame));

    const step = this.sinceRender;
    this.sinceRender = 0;
    this.update(step);
    this.renderer.render();
    this.countFps(step);
  }

  update(delta) {
    this.time += delta;
    this.updateDemo();

    const tracking = this.face.update();
    const ts = this.face.settings;
    const trackMouth = Boolean(tracking && ts.mouth);

    const speaking = this.animator.state === 'speaking';
    const lip = this.lipSync.update(delta, speaking && !trackMouth);
    this.mixer.setMouth(lip);
    const face = this.mixer.update(delta);
    let mouthLevel = this.lipSync.level;

    if (tracking) {
      if (ts.eyes) face.blink = { ...tracking.blink };
      if (trackMouth) {
        face.mouth = { ...tracking.mouth };
        mouthLevel = tracking.mouthLevel;
      }
      if (ts.emotions) {
        for (const [preset, weight] of Object.entries(tracking.emotions)) {
          face.emotions.set(preset, Math.max(face.emotions.get(preset) || 0, weight));
        }
      }
    }

    const pose = this.animator.update(delta, mouthLevel);
    if (tracking && ts.head) {
      pose.head = { ...tracking.head };
    }

    this.renderer.apply({
      pose,
      face,
      gaze: this.computeGaze(tracking),
      trackingActive: Boolean(tracking && ts.head),
    }, delta);
  }

  // Blick als Winkel von -1 bis 1. x positiv = zur linken Seite des Avatars
  // (für dich: nach rechts), y positiv = nach oben.
  computeGaze(tracking) {
    if (this.lookMode === 'aus') return { mode: 'off', x: 0, y: 0 };
    if (tracking && this.face.settings.gaze) {
      return { mode: 'angles', x: tracking.gaze.x, y: tracking.gaze.y };
    }
    let mode = 'camera';
    let x = 0;
    let y = 0;
    if (this.lookMode === 'maus') {
      mode = 'angles';
      x = this.pointer.x;
      y = this.pointer.y;
    }
    // Beim Nachdenken schaut der Avatar nach oben zur Seite.
    const thinking = this.animator.weights.thinking;
    if (thinking > 0.01) {
      mode = 'angles';
      x = lerp(x, 0.6, thinking);
      y = lerp(y, 0.5, thinking);
    }
    return { mode, x, y };
  }

  // Demo-Modus: zufällige Zustände, Gefühle und kurze Sätze.
  updateDemo() {
    if (!this.demo.enabled || this.time < this.demo.nextAt) return;
    this.demo.nextAt = this.time + 2.5 + Math.random() * 3;
    if (this.animator.state === 'speaking') return;
    const roll = Math.random();
    if (roll < 0.3) {
      const line = DEMO_LINES[Math.floor(Math.random() * DEMO_LINES.length)];
      this.emit({ type: 'demoLine', text: line });
    } else if (roll < 0.6) {
      const emotions = Object.keys(EMOTIONS);
      this.setEmotion(emotions[Math.floor(Math.random() * emotions.length)]);
    } else {
      const states = STATES.filter((s) => s !== 'speaking');
      this.setState(states[Math.floor(Math.random() * states.length)]);
    }
  }

  countFps(step) {
    this.fpsFrames += 1;
    this.fpsTime += step;
    if (this.fpsTime >= 0.5) {
      this.measuredFps = Math.round(this.fpsFrames / this.fpsTime);
      this.fpsFrames = 0;
      this.fpsTime = 0;
    }
  }
}
