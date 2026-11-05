// Gesichtserkennung über die Webcam. Lädt MediaPipe erst beim Start nach.
// Das Modell und das WebAssembly liegen als Base64 in vendor/mediapipe/.
// So geht alles offline und auch direkt als Datei (file://).

import { loadScript, base64ToBytes } from '../script-loader.js';
import { mapFaceResult, DEFAULT_CALIBRATION } from './face-mapping.js';

const VENDOR = 'vendor/mediapipe/';
const DETECT_INTERVAL_MS = 1000 / 30;

function smooth(current, target, factor) {
  return current + (target - current) * factor;
}

export class FaceTracking {
  constructor() {
    this.active = false;
    this.starting = false;
    this.tracker = null;
    this.video = null;
    this.stream = null;
    this.lastVideoTime = -1;
    this.lastDetect = 0;
    this.latest = null;
    this.detected = false;
    this.fps = 0;
    this.fpsCount = 0;
    this.fpsWindowStart = 0;
    this.delegate = '';
    this.calibration = structuredClone(DEFAULT_CALIBRATION);
    this.settings = {
      mirror: true,
      swapEyes: false,
      invertYaw: false,
      invertPitch: false,
      invertRoll: false,
      head: true,
      eyes: true,
      mouth: true,
      gaze: true,
      emotions: false,
      strength: 1,
      smoothing: 0.5,
    };
  }

  async start() {
    if (this.active || this.starting) return;
    this.starting = true;
    try {
      if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
        throw new Error('Dieser Browser erlaubt hier keinen Kamera-Zugriff.');
      }
      if (!this.tracker) {
        this.tracker = await this.createTracker();
      }
      this.stream = await navigator.mediaDevices.getUserMedia({
        video: { width: { ideal: 640 }, height: { ideal: 480 }, facingMode: 'user' },
        audio: false,
      });
      this.video = document.createElement('video');
      this.video.muted = true;
      this.video.playsInline = true;
      this.video.srcObject = this.stream;
      await this.video.play();
      this.active = true;
      this.lastVideoTime = -1;
    } catch (error) {
      this.stopStream();
      throw error;
    } finally {
      this.starting = false;
    }
  }

  async createTracker() {
    await loadScript('js/face-bundle.js');
    await loadScript(`${VENDOR}vision_wasm_internal.loader.js`);
    await loadScript(`${VENDOR}vision_wasm_internal.wasm.js`);
    await loadScript(`${VENDOR}face_landmarker.task.js`);
    // Aus den Base64-Texten werden Blob-URLs. Die darf der Browser auch bei file:// laden.
    const loaderUrl = URL.createObjectURL(new Blob([base64ToBytes(window.KiNexusMediaPipeLoader)], { type: 'text/javascript' }));
    const wasmUrl = URL.createObjectURL(new Blob([base64ToBytes(window.KiNexusMediaPipeWasm)], { type: 'application/wasm' }));
    const modelBytes = base64ToBytes(window.KiNexusFaceModel);
    // Speicher freigeben, die Base64-Texte brauchen wir nicht mehr.
    delete window.KiNexusMediaPipeLoader;
    delete window.KiNexusMediaPipeWasm;
    delete window.KiNexusFaceModel;
    const tracker = await window.KiNexusFace.createFaceTracker({
      wasmLoaderUrl: loaderUrl,
      wasmBinaryUrl: wasmUrl,
      modelBuffer: modelBytes,
    });
    this.delegate = tracker.delegate;
    return tracker;
  }

  stop() {
    this.stopStream();
    this.active = false;
    this.latest = null;
    this.detected = false;
  }

  stopStream() {
    if (this.stream) {
      this.stream.getTracks().forEach((track) => track.stop());
      this.stream = null;
    }
    if (this.video) {
      this.video.srcObject = null;
      this.video = null;
    }
  }

  // Aktuelle Haltung als Nullpunkt speichern: Kopf gerade, Blick geradeaus, Augen offen.
  calibrate() {
    if (!this.latest) {
      throw new Error('Kein Gesicht erkannt. Schau in die Kamera und versuch es nochmals.');
    }
    this.calibration = structuredClone(this.latest.calibrationSample);
    this.latest = null;
  }

  resetCalibration() {
    this.calibration = structuredClone(DEFAULT_CALIBRATION);
    this.latest = null;
  }

  // Wird in jedem Bild aufgerufen. Gibt die geglätteten Werte zurück oder null.
  update() {
    if (!this.active || !this.video || this.video.readyState < 2) {
      return null;
    }
    const now = performance.now();
    if (now - this.lastDetect >= DETECT_INTERVAL_MS && this.video.currentTime !== this.lastVideoTime) {
      this.lastDetect = now;
      this.lastVideoTime = this.video.currentTime;
      const result = this.tracker.detect(this.video, now);
      const mapped = mapFaceResult(result, this.settings, this.calibration);
      this.detected = Boolean(mapped);
      if (mapped) {
        this.applySmoothing(mapped);
      }
      this.countFps(now);
    }
    return this.detected ? this.latest : null;
  }

  applySmoothing(mapped) {
    const strength = this.settings.strength;
    const head = {
      x: mapped.head.x * strength,
      y: mapped.head.y * strength,
      z: mapped.head.z * strength,
    };
    if (!this.latest) {
      this.latest = { ...mapped, head };
      return;
    }
    // Kopf und Blick stärker glätten, Augen und Mund schneller folgen lassen.
    const slow = 1 - this.settings.smoothing * 0.85;
    const fast = Math.min(1, slow * 1.8);
    const l = this.latest;
    l.calibrationSample = mapped.calibrationSample;
    for (const axis of ['x', 'y', 'z']) {
      l.head[axis] = smooth(l.head[axis], head[axis], slow);
    }
    l.gaze.x = smooth(l.gaze.x, mapped.gaze.x, slow);
    l.gaze.y = smooth(l.gaze.y, mapped.gaze.y, slow);
    l.blink.left = smooth(l.blink.left, mapped.blink.left, fast);
    l.blink.right = smooth(l.blink.right, mapped.blink.right, fast);
    for (const key of Object.keys(l.mouth)) {
      l.mouth[key] = smooth(l.mouth[key], mapped.mouth[key], fast);
    }
    for (const key of Object.keys(l.emotions)) {
      l.emotions[key] = smooth(l.emotions[key], mapped.emotions[key], slow);
    }
    l.mouthLevel = smooth(l.mouthLevel, mapped.mouthLevel, fast);
    l.raw = mapped.raw;
  }

  // Erkennungen pro Sekunde messen.
  countFps(now) {
    this.fpsCount += 1;
    if (!this.fpsWindowStart) this.fpsWindowStart = now;
    const elapsed = now - this.fpsWindowStart;
    if (elapsed >= 1000) {
      this.fps = Math.round((this.fpsCount * 1000) / elapsed);
      this.fpsCount = 0;
      this.fpsWindowStart = now;
    }
  }
}
