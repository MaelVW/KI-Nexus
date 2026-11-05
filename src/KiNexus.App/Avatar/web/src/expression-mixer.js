// Berechnet pro Bild die Werte für das Gesicht: Gefühl (mit Überblendung),
// Blinzeln und Mund. Das Ergebnis ist unabhängig vom Format (VRM oder Live2D).
// Der jeweilige Renderer übersetzt es dann in Ausdrücke oder Parameter.

// Gefühle in KI-Nexus (Deutsch) und der passende VRM-Ausdruck.
export const EMOTIONS = {
  neutral: null,
  freude: 'happy',
  traurig: 'sad',
  ueberrascht: 'surprised',
  wuetend: 'angry',
  entspannt: 'relaxed',
};

// Umgekehrt: VRM-Ausdruck → deutsches Gefühl.
export const PRESET_TO_EMOTION = Object.fromEntries(
  Object.entries(EMOTIONS).filter(([, preset]) => preset).map(([name, preset]) => [preset, name]),
);

export const MOUTH_SHAPES = ['aa', 'ih', 'ou', 'ee', 'oh'];

const BLINK_CLOSE = 0.06;
const BLINK_HOLD = 0.04;
const BLINK_OPEN = 0.08;

export class ExpressionMixer {
  constructor() {
    this.emotionWeights = new Map();
    this.emotionName = 'neutral';
    this.emotionPreset = null;
    this.emotionStrength = 1;
    this.emotionHoldUntil = 0;
    this.fadeSeconds = 0.3;
    this.autoReturnSeconds = 6;
    this.autoBlink = true;
    this.blinkPhase = -1;
    this.nextBlinkAt = 2;
    this.mouth = { aa: 0, ih: 0, ou: 0, ee: 0, oh: 0 };
    this.time = 0;
  }

  reset() {
    this.emotionWeights.clear();
    this.emotionName = 'neutral';
    this.emotionPreset = null;
  }

  // Setzt ein Gefühl. holdSeconds = 0 heisst: bleibt, bis ein anderes kommt.
  setEmotion(name, strength = 1, holdSeconds = this.autoReturnSeconds) {
    if (!(name in EMOTIONS)) {
      return false;
    }
    this.emotionName = name;
    this.emotionPreset = EMOTIONS[name];
    this.emotionStrength = Math.max(0, Math.min(1, strength));
    this.emotionHoldUntil = name !== 'neutral' && holdSeconds > 0 ? this.time + holdSeconds : 0;
    return true;
  }

  triggerBlink() {
    if (this.blinkPhase < 0) {
      this.blinkPhase = 0;
    }
  }

  setMouth(values) {
    for (const shape of MOUTH_SHAPES) {
      this.mouth[shape] = Math.max(0, Math.min(1, values[shape] || 0));
    }
  }

  // Gibt die Werte für dieses Bild zurück.
  update(delta) {
    this.time += delta;
    this.updateEmotion(delta);
    const blink = this.updateBlink(delta);
    return {
      emotions: new Map(this.emotionWeights),
      blink: { left: blink, right: blink },
      mouth: { ...this.mouth },
    };
  }

  updateEmotion(delta) {
    if (this.emotionHoldUntil > 0 && this.time >= this.emotionHoldUntil) {
      this.setEmotion('neutral');
    }

    // Alle Gefühle laufen weich zu ihrem Ziel: das aktive zur Stärke, die anderen zu 0.
    const step = this.fadeSeconds > 0 ? delta / this.fadeSeconds : 1;
    const presets = new Set([...this.emotionWeights.keys()]);
    if (this.emotionPreset) {
      presets.add(this.emotionPreset);
    }
    for (const preset of presets) {
      const target = preset === this.emotionPreset ? this.emotionStrength : 0;
      const current = this.emotionWeights.get(preset) || 0;
      const next = current < target ? Math.min(target, current + step) : Math.max(target, current - step);
      if (next <= 0 && target === 0) {
        this.emotionWeights.delete(preset);
      } else {
        this.emotionWeights.set(preset, next);
      }
    }
  }

  updateBlink(delta) {
    if (this.blinkPhase < 0) {
      if (this.autoBlink && this.time >= this.nextBlinkAt) {
        this.blinkPhase = 0;
      } else {
        return 0;
      }
    }

    this.blinkPhase += delta;
    const p = this.blinkPhase;
    if (p < BLINK_CLOSE) {
      return p / BLINK_CLOSE;
    }
    if (p < BLINK_CLOSE + BLINK_HOLD) {
      return 1;
    }
    if (p < BLINK_CLOSE + BLINK_HOLD + BLINK_OPEN) {
      return 1 - (p - BLINK_CLOSE - BLINK_HOLD) / BLINK_OPEN;
    }

    // Blinzeln fertig: nächstes in 2 bis 6 Sekunden.
    this.blinkPhase = -1;
    this.nextBlinkAt = this.time + 2 + Math.random() * 4;
    return 0;
  }
}
