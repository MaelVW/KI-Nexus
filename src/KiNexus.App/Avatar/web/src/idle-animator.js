// Berechnet die Körperhaltung: Grundpose (Arme unten), Atmen, kleine
// Kopfbewegungen und die vier Zustände Leerlauf, Zuhören, Nachdenken, Sprechen.
//
// Die Winkel sind in Radiant und gelten für ein Modell, das nach +Z schaut:
//   x positiv = nach vorne/unten neigen
//   y positiv = zur eigenen linken Seite drehen
//   z positiv = Kopfoberseite zur eigenen rechten Seite kippen
// Der Renderer (VRM oder Live2D) setzt diese Werte dann um.

export const STATES = ['idle', 'listening', 'thinking', 'speaking'];

const BLEND_SPEED = 4;

export class IdleAnimator {
  constructor() {
    this.state = 'idle';
    this.weights = { idle: 1, listening: 0, thinking: 0, speaking: 0 };
    this.armAngle = 1.2;
    this.breathing = true;
    this.headMotion = true;
    this.time = 0;
  }

  setState(state) {
    if (STATES.includes(state)) {
      this.state = state;
      return true;
    }
    return false;
  }

  // mouthLevel (0–1) lässt den Kopf beim Sprechen leicht mitnicken.
  update(delta, mouthLevel = 0) {
    this.time += delta;
    const t = this.time;

    for (const state of STATES) {
      const target = state === this.state ? 1 : 0;
      const current = this.weights[state];
      const step = delta * BLEND_SPEED;
      this.weights[state] = current < target ? Math.min(target, current + step) : Math.max(target, current - step);
    }
    const w = this.weights;

    // Atmen: 0 bis 1, ein Atemzug dauert etwa 4 Sekunden.
    const breathPhase = (Math.sin((t * Math.PI * 2) / 4.2) + 1) / 2;
    const breath = this.breathing ? breathPhase : 0.5;
    const breathTilt = this.breathing ? (breathPhase - 0.5) * 0.03 : 0;

    // Kopf: kleine, zufällig wirkende Bewegungen plus die Zustände.
    const sway = this.headMotion ? 1 : 0;
    let x = Math.sin(t * 0.9) * 0.015 * sway;
    let y = Math.sin(t * 0.6) * 0.04 * sway;
    let z = Math.sin(t * 0.45 + 1.3) * 0.02 * sway;

    z += w.listening * 0.09;
    x += w.listening * 0.04;

    x += w.thinking * -0.12;
    y += w.thinking * 0.18;
    z += w.thinking * -0.05;

    x += w.speaking * (mouthLevel * 0.04 + Math.sin(t * 3.1) * 0.012);

    return {
      head: { x, y, z },
      body: { x: breathTilt * 0.5 + w.listening * 0.05, y: y * 0.15, z: z * 0.1 },
      breath,
      breathTilt,
      armAngle: this.armAngle,
      elbow: 0.15,
    };
  }
}
