// Erzeugt Werte für die Mundformen (aa, ih, ou, ee, oh).
// Modi: 'aus' (Regler von Hand), 'test' (künstliches Sprechen),
// 'mikrofon' (deine Stimme) und 'datei' (eine Audiodatei, z.B. von Piper).

import { MOUTH_SHAPES } from './expression-mixer.js';

export const LIPSYNC_MODES = ['aus', 'test', 'mikrofon', 'datei'];

const SYNTH_VOWELS = [
  ['aa', 0.35], ['oh', 0.2], ['ee', 0.15], ['ih', 0.15], ['ou', 0.15],
];

function emptyShapes() {
  return { aa: 0, ih: 0, ou: 0, ee: 0, oh: 0 };
}

function pickVowel() {
  let r = Math.random();
  for (const [vowel, chance] of SYNTH_VOWELS) {
    r -= chance;
    if (r <= 0) {
      return vowel;
    }
  }
  return 'aa';
}

export class LipSync {
  constructor() {
    this.mode = 'aus';
    this.manual = 0;
    this.gain = 1.5;
    this.estimateVowels = true;
    this.level = 0;
    this.values = emptyShapes();
    this.context = null;
    this.analyser = null;
    this.timeData = null;
    this.freqData = null;
    this.micStream = null;
    this.micSource = null;
    this.audioElement = null;
    this.audioSource = null;
    this.audioUrl = null;
    this.synth = { time: 0, end: 0, amplitude: 0, vowel: 'aa', pause: false };
  }

  async setMode(mode) {
    if (!LIPSYNC_MODES.includes(mode)) {
      throw new Error(`Unbekannter Modus: ${mode}`);
    }
    if (mode !== 'mikrofon') {
      this.stopMicrophone();
    }
    if (mode !== 'datei') {
      this.stopFile();
    }
    if (mode === 'mikrofon') {
      try {
        await this.startMicrophone();
      } catch (error) {
        this.stopMicrophone();
        this.mode = 'aus';
        throw new Error(`Mikrofon nicht verfügbar: ${error.message}`);
      }
    }
    this.mode = mode;
  }

  ensureContext() {
    if (!this.context) {
      const AudioCtx = window.AudioContext || window.webkitAudioContext;
      this.context = new AudioCtx();
      this.analyser = this.context.createAnalyser();
      this.analyser.fftSize = 1024;
      this.analyser.smoothingTimeConstant = 0.3;
      this.timeData = new Float32Array(this.analyser.fftSize);
      this.freqData = new Uint8Array(this.analyser.frequencyBinCount);
    }
    if (this.context.state === 'suspended') {
      this.context.resume();
    }
  }

  async startMicrophone() {
    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
      throw new Error('Dieser Browser erlaubt hier keinen Mikrofon-Zugriff.');
    }
    this.ensureContext();
    this.micStream = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true } });
    this.micSource = this.context.createMediaStreamSource(this.micStream);
    // Nur messen, nicht abspielen (sonst gibt es ein Echo).
    this.micSource.connect(this.analyser);
  }

  stopMicrophone() {
    if (this.micSource) {
      this.micSource.disconnect();
      this.micSource = null;
    }
    if (this.micStream) {
      this.micStream.getTracks().forEach((track) => track.stop());
      this.micStream = null;
    }
  }

  loadFile(file) {
    this.ensureContext();
    this.stopFile();
    if (this.audioSource) {
      this.audioSource.disconnect();
      this.audioSource = null;
    }
    if (this.audioUrl) {
      URL.revokeObjectURL(this.audioUrl);
    }
    this.audioUrl = URL.createObjectURL(file);
    this.audioElement = new Audio(this.audioUrl);
    this.audioSource = this.context.createMediaElementSource(this.audioElement);
    // Messen und gleichzeitig hörbar abspielen. Der Analyser selbst bleibt
    // stumm, sonst würde später auch das Mikrofon hörbar.
    this.audioSource.connect(this.analyser);
    this.audioSource.connect(this.context.destination);
    return this.audioElement;
  }

  playFile() {
    if (!this.audioElement) {
      throw new Error('Zuerst eine Audiodatei wählen.');
    }
    this.ensureContext();
    this.audioElement.currentTime = 0;
    return this.audioElement.play();
  }

  stopFile() {
    if (this.audioElement) {
      this.audioElement.pause();
    }
  }

  get isFilePlaying() {
    return Boolean(this.audioElement && !this.audioElement.paused && !this.audioElement.ended);
  }

  // forceTest = true: künstliches Sprechen, auch wenn der Modus "aus" ist
  // (z.B. im Zustand "Sprechen" ohne echte Stimme).
  update(delta, forceTest = false) {
    let target = emptyShapes();

    if (this.mode === 'test' || (forceTest && this.mode === 'aus')) {
      target = this.updateSynthetic(delta);
    } else if (this.mode === 'mikrofon' || this.mode === 'datei') {
      target = this.updateFromAudio(delta);
    } else {
      target.aa = this.manual;
      this.level = this.manual;
    }

    // Weich nachführen, damit der Mund nicht zittert.
    for (const shape of MOUTH_SHAPES) {
      const current = this.values[shape];
      const goal = target[shape];
      const speed = goal > current ? 30 : 12;
      this.values[shape] = current + (goal - current) * Math.min(1, delta * speed);
    }
    return this.values;
  }

  updateSynthetic(delta) {
    const s = this.synth;
    s.time += delta;
    if (s.time >= s.end) {
      // Neue Silbe oder kurze Pause.
      s.time = 0;
      s.pause = Math.random() < 0.15;
      s.end = s.pause ? 0.15 + Math.random() * 0.25 : 0.12 + Math.random() * 0.12;
      s.amplitude = s.pause ? 0 : 0.55 + Math.random() * 0.45;
      s.vowel = pickVowel();
    }
    const envelope = s.pause ? 0 : Math.sin(Math.PI * (s.time / s.end)) * s.amplitude;
    this.level = envelope;
    const shapes = emptyShapes();
    shapes[s.vowel] = envelope;
    if (s.vowel !== 'aa') {
      shapes.aa = envelope * 0.3;
    }
    return shapes;
  }

  updateFromAudio(delta) {
    const shapes = emptyShapes();
    if (!this.analyser) {
      this.level = 0;
      return shapes;
    }

    // Lautstärke (RMS) messen.
    this.analyser.getFloatTimeDomainData(this.timeData);
    let sum = 0;
    for (let i = 0; i < this.timeData.length; i += 1) {
      sum += this.timeData[i] * this.timeData[i];
    }
    const rms = Math.sqrt(sum / this.timeData.length);
    const raw = Math.max(0, Math.min(1, (rms - 0.01) * this.gain * 6));
    const speed = raw > this.level ? 25 : 10;
    this.level += (raw - this.level) * Math.min(1, delta * speed);

    if (!this.estimateVowels) {
      shapes.aa = this.level;
      return shapes;
    }

    // Grobe Vokal-Schätzung über den Schwerpunkt der Frequenzen (experimentell).
    this.analyser.getByteFrequencyData(this.freqData);
    const binHz = this.context.sampleRate / this.analyser.fftSize;
    const minBin = Math.floor(250 / binHz);
    const maxBin = Math.min(this.freqData.length - 1, Math.floor(3500 / binHz));
    let weighted = 0;
    let total = 0;
    for (let i = minBin; i <= maxBin; i += 1) {
      weighted += this.freqData[i] * i;
      total += this.freqData[i];
    }
    const centroid = total > 0 ? (weighted / total - minBin) / (maxBin - minBin) : 0.5;
    let vowel = 'aa';
    if (centroid < 0.22) vowel = 'ou';
    else if (centroid < 0.34) vowel = 'oh';
    else if (centroid < 0.5) vowel = 'aa';
    else if (centroid < 0.62) vowel = 'ee';
    else vowel = 'ih';

    shapes[vowel] = this.level;
    if (vowel !== 'aa') {
      shapes.aa = this.level * 0.35;
    }
    return shapes;
  }
}
