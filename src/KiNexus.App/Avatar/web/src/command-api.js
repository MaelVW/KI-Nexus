// Befehls-Schnittstelle des Avatars. Das Panel benutzt genau diese Befehle.
// Später schickt KI-Nexus (über WebView2) dieselben JSON-Befehle.
//
// Beispiel: { "type": "setEmotion", "name": "freude", "strength": 1 }

import { STATES } from './idle-animator.js';
import { LIPSYNC_MODES } from './lipsync.js';
import { EMOTIONS } from './expression-mixer.js';
import { FRAMINGS, LOOK_MODES, BACKGROUNDS } from './avatar-controller.js';
import { normalizeEmotionName, parseEmotionTags } from './emotion-tags.js';

const COLOR_PATTERN = /^#[0-9a-fA-F]{6}$/;
const FACE_FLAGS = ['mirror', 'swapEyes', 'invertYaw', 'invertPitch', 'invertRoll', 'head', 'eyes', 'mouth', 'gaze', 'emotions'];
const DIRECTION_FLAGS = ['mirror', 'swapEyes', 'invertYaw', 'invertPitch', 'invertRoll'];

function clamp(value, min, max, fallback) {
  const number = Number(value);
  if (!Number.isFinite(number)) {
    return fallback;
  }
  return Math.max(min, Math.min(max, number));
}

function requireOneOf(value, allowed, label) {
  if (!allowed.includes(value)) {
    throw new Error(`${label} muss eines davon sein: ${allowed.join(', ')}`);
  }
}

export class CommandApi {
  constructor(avatar) {
    this.avatar = avatar;
    this.listeners = new Set();
    this.useVoice = false;
    this.connectHost();
  }

  onLog(listener) {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  log(entry) {
    for (const listener of this.listeners) {
      listener({ time: new Date(), ...entry });
    }
  }

  // silent = true: ausführen, aber nicht ins Protokoll schreiben (z.B. beim Ziehen eines Reglers).
  async handle(command, { silent = false } = {}) {
    try {
      if (!command || typeof command !== 'object' || typeof command.type !== 'string') {
        throw new Error('Ein Befehl braucht ein Feld "type".');
      }
      const result = (await this.dispatch(command)) || {};
      if (!silent) {
        this.log({ direction: 'in', payload: command, ok: true, message: result.message || '' });
      }
      return { ok: true, ...result };
    } catch (error) {
      this.log({ direction: 'in', payload: command, ok: false, message: error.message });
      return { ok: false, error: error.message };
    }
  }

  requireKind(kind) {
    if (this.avatar.kind !== kind) {
      const label = kind === 'vrm' ? 'VRM' : 'Live2D';
      throw new Error(`Dieser Befehl geht nur mit einem ${label}-Modell.`);
    }
  }

  async dispatch(cmd) {
    const a = this.avatar;
    const r = a.renderer;
    switch (cmd.type) {
      // ----- Zustand und Gefühl -----
      case 'setState':
        requireOneOf(cmd.state, STATES, 'state');
        a.setState(cmd.state);
        return null;

      case 'setEmotion': {
        const name = normalizeEmotionName(cmd.name || '');
        const strength = clamp(cmd.strength, 0, 1, 1);
        const hold = clamp(cmd.holdSeconds, 0, 600, a.mixer.autoReturnSeconds);
        a.setEmotion(name, strength, hold);
        return null;
      }

      case 'setEmotionSettings':
        if (cmd.fadeSeconds != null) a.mixer.fadeSeconds = clamp(cmd.fadeSeconds, 0, 3, a.mixer.fadeSeconds);
        if (cmd.autoReturnSeconds != null) {
          a.mixer.autoReturnSeconds = clamp(cmd.autoReturnSeconds, 0, 600, a.mixer.autoReturnSeconds);
        }
        return null;

      // Live2D: Welcher Ausdruck (exp3) gehört zu welchem Gefühl? expression = null: Parameter nutzen.
      case 'setLive2dEmotionMap': {
        this.requireKind('live2d');
        const emotion = normalizeEmotionName(cmd.emotion || '');
        if (!(emotion in EMOTIONS) || emotion === 'neutral') throw new Error(`Unbekanntes Gefühl: ${cmd.emotion}`);
        if (cmd.expression && !r.hasExpression(cmd.expression)) throw new Error(`Diesen Ausdruck hat das Modell nicht: ${cmd.expression}`);
        r.setEmotionMapping(emotion, cmd.expression || null);
        return null;
      }

      // ----- Ausdrücke und Parameter -----
      case 'setExpression':
        if (a.kind === 'live2d') {
          if (cmd.name && !r.hasExpression(cmd.name)) throw new Error(`Diesen Ausdruck hat das Modell nicht: ${cmd.name}`);
          r.setExpression(cmd.weight === 0 ? null : cmd.name);
          return null;
        }
        if (typeof cmd.name !== 'string' || !r.hasExpression(cmd.name)) {
          throw new Error(`Diesen Ausdruck hat das Modell nicht: ${cmd.name}`);
        }
        r.setManual(cmd.name, clamp(cmd.weight, 0, 1, 0));
        return null;

      case 'setParameter':
        this.requireKind('live2d');
        if (!r.hasParameter(cmd.id)) throw new Error(`Diesen Parameter hat das Modell nicht: ${cmd.id}`);
        r.setManual(cmd.id, cmd.value == null ? null : Number(cmd.value));
        return null;

      case 'resetExpressions':
        r.clearManual();
        a.setEmotion('neutral');
        return null;

      // ----- Mund -----
      case 'setMouth':
        a.lipSync.manual = clamp(cmd.value, 0, 1, 0);
        return a.lipSync.mode === 'aus' ? null : { message: 'Wirkt nur im Lippen-Modus "aus".' };

      case 'setLipSyncMode':
        requireOneOf(cmd.mode, LIPSYNC_MODES, 'mode');
        await a.lipSync.setMode(cmd.mode);
        return null;

      case 'setLipSyncSettings':
        if (cmd.gain != null) a.lipSync.gain = clamp(cmd.gain, 0.1, 6, a.lipSync.gain);
        if (cmd.estimateVowels != null) a.lipSync.estimateVowels = Boolean(cmd.estimateVowels);
        return null;

      // ----- Augen und Bewegung -----
      case 'blink':
        a.mixer.triggerBlink();
        return null;

      case 'setAutoBlink':
        a.mixer.autoBlink = Boolean(cmd.enabled);
        return null;

      case 'setLookMode':
        requireOneOf(cmd.mode, LOOK_MODES, 'mode');
        a.setLookMode(cmd.mode);
        return null;

      case 'setIdle':
        if (cmd.breathing != null) a.animator.breathing = Boolean(cmd.breathing);
        if (cmd.headMotion != null) a.animator.headMotion = Boolean(cmd.headMotion);
        if (cmd.armAngle != null) a.animator.armAngle = clamp(cmd.armAngle, 0, 1.5, a.animator.armAngle);
        return null;

      case 'setDemo':
        a.setDemo(Boolean(cmd.enabled));
        return null;

      // ----- Animationen -----
      case 'loadAnimationUrl': {
        this.requireKind('vrm');
        const response = await fetch(cmd.url);
        if (!response.ok) throw new Error(`Laden fehlgeschlagen (${response.status})`);
        await r.loadAnimation(await response.arrayBuffer(), cmd.url.split('/').pop());
        return null;
      }

      case 'playAnimation':
        this.requireKind('vrm');
        r.playAnimation();
        return null;

      case 'stopAnimation':
        if (a.kind === 'vrm') r.stopAnimation();
        if (a.kind === 'live2d') r.stopMotions();
        return null;

      case 'setAnimationSettings':
        this.requireKind('vrm');
        r.setAnimationSettings({
          loop: cmd.loop,
          speed: cmd.speed != null ? clamp(cmd.speed, 0.1, 3, 1) : null,
        });
        return null;

      case 'playMotion':
        this.requireKind('live2d');
        await r.playMotion(String(cmd.group), cmd.index == null ? undefined : Number(cmd.index));
        return null;

      case 'setModelIdleMotions':
        this.requireKind('live2d');
        r.setIdleMotions(Boolean(cmd.enabled));
        return null;

      // ----- Ansicht -----
      case 'setFraming':
        requireOneOf(cmd.framing, FRAMINGS, 'framing');
        a.setFraming(cmd.framing);
        return null;

      case 'setBackground':
        requireOneOf(cmd.mode, BACKGROUNDS, 'mode');
        if (cmd.color != null && !COLOR_PATTERN.test(cmd.color)) {
          throw new Error('color muss so aussehen: #112233');
        }
        a.setBackground(cmd.mode, cmd.color, cmd.imageUrl);
        return null;

      case 'setFps':
        a.setMaxFps(clamp(cmd.fps, 0, 240, 60));
        return null;

      case 'setPixelRatio':
        a.setPixelRatio(clamp(cmd.value, 0.5, 3, 1));
        return null;

      case 'setLight':
        this.requireKind('vrm');
        r.setLight({
          intensity: cmd.intensity != null ? clamp(cmd.intensity, 0, 3, 0.7) : null,
          ambient: cmd.ambient != null ? clamp(cmd.ambient, 0, 2, 0.12) : null,
          angle: cmd.angle != null ? clamp(cmd.angle, -180, 180, 27) : null,
        });
        return null;

      case 'setFov':
        this.requireKind('vrm');
        r.setFov(clamp(cmd.fov, 10, 70, 30));
        return null;

      case 'setAutoRotate':
        this.requireKind('vrm');
        r.setAutoRotate(Boolean(cmd.enabled), clamp(cmd.speed, -10, 10, 1));
        return null;

      case 'setWind':
        this.requireKind('vrm');
        r.setWind(
          cmd.strength != null ? clamp(cmd.strength, 0, 3, 0) : null,
          cmd.direction != null ? clamp(cmd.direction, -180, 180, 90) : null,
        );
        return null;

      // ----- Gesichtserkennung -----
      case 'startFaceTracking':
        await a.face.start();
        return { message: `läuft (${a.face.delegate})` };

      case 'stopFaceTracking':
        a.face.stop();
        return null;

      case 'calibrateFaceTracking':
        a.face.calibrate();
        return { message: 'Nullpunkt gesetzt' };

      case 'setFaceTrackingSettings': {
        const s = a.face.settings;
        let directionChanged = false;
        for (const flag of FACE_FLAGS) {
          if (cmd[flag] != null && s[flag] !== Boolean(cmd[flag])) {
            s[flag] = Boolean(cmd[flag]);
            if (DIRECTION_FLAGS.includes(flag)) directionChanged = true;
          }
        }
        // Ändert sich die Richtung, passt der alte Nullpunkt nicht mehr.
        if (directionChanged) a.face.resetCalibration();
        if (cmd.strength != null) s.strength = clamp(cmd.strength, 0, 2, 1);
        if (cmd.smoothing != null) s.smoothing = clamp(cmd.smoothing, 0, 1, 0.5);
        return null;
      }

      // ----- Text und Stimme -----
      case 'simulateText': {
        const parsed = parseEmotionTags(cmd.text || '');
        const useVoice = cmd.voice != null ? Boolean(cmd.voice) : this.useVoice;
        const seconds = a.simulateSpeech(parsed.cleanText, parsed.emotions, { useVoice });
        const duration = seconds == null ? 'Browser-Stimme' : `${seconds.toFixed(1)} s`;
        return {
          message: `${parsed.emotions.length} Gefühl(e), ${duration}`,
          parsed,
          seconds,
        };
      }

      case 'setVoice':
        if (cmd.enabled != null) this.useVoice = Boolean(cmd.enabled);
        if (cmd.voiceUri != null) a.speech.voiceUri = String(cmd.voiceUri);
        if (cmd.rate != null) a.speech.rate = clamp(cmd.rate, 0.5, 2, 1);
        return null;

      case 'stopSpeech':
        a.cancelSpeech();
        a.setState('idle');
        return null;

      // ----- Modell -----
      case 'loadUrl':
        if (typeof cmd.url !== 'string' || !cmd.url) {
          throw new Error('loadUrl braucht ein Feld "url".');
        }
        await a.loadUrl(cmd.url);
        return { message: `Geladen: ${a.fileName}` };

      case 'unload':
        a.unload();
        return null;

      default:
        throw new Error(`Unbekannter Befehl: ${cmd.type}`);
    }
  }

  // Verbindung zu einem Gastgeber-Programm: WebView2 (KI-Nexus) oder ein Elternfenster.
  connectHost() {
    const webview = window.chrome && window.chrome.webview;
    if (webview) {
      // KI-Nexus: Fehler eines Befehls an die App zurückmelden (sonst sieht man sie nur im Panel).
      webview.addEventListener('message', (event) => this.handle(event.data).then((result) => {
        if (result && !result.ok) {
          webview.postMessage({ type: 'commandError', command: event.data && event.data.type, error: result.error });
        }
      }));
    }
    window.addEventListener('message', (event) => {
      const sameOrigin = event.origin === window.location.origin || event.origin === 'null';
      if (event.source !== window && sameOrigin && event.data && typeof event.data.type === 'string') {
        this.handle(event.data);
      }
    });

    // Ereignisse des Avatars an den Gastgeber zurückschicken.
    this.avatar.on((event) => {
      if (webview) {
        webview.postMessage(event);
      } else if (window.parent && window.parent !== window) {
        window.parent.postMessage(event, '*');
      }
      if (event.type === 'loaded' || event.type === 'unloaded') {
        this.log({ direction: 'out', payload: event, ok: true, message: '' });
      }
    });
  }
}
