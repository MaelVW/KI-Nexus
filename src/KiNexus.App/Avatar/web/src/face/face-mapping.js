// Übersetzt ein Ergebnis von MediaPipe Face Landmarker in Werte für den Avatar.
// MediaPipe liefert 52 "Blendshapes" (z.B. eyeBlinkLeft, jawOpen) und eine
// 4×4-Matrix mit der Kopfdrehung.
//
// Koordinaten von MediaPipe: x rechts im ungespiegelten Kamerabild, y oben,
// das Gesicht schaut zur Kamera (+Z). Im ungespiegelten Bild ist deine linke
// Seite rechts. Darum gilt: Drehst du den Kopf nach links, wird die Drehung um Y
// positiv. Kippst du ihn zur rechten Schulter, wird die Drehung um Z positiv.
//
// Der Avatar schaut zu dir. Seine linke Seite ist für dich rechts.
// Im Spiegel-Modus soll er sich bewegen wie dein Spiegelbild. Kippst du den Kopf
// nach rechts, kippt er also für dich auch nach rechts, das ist seine linke Seite.
// Darum werden Drehen, Neigen und links/rechts im Spiegel-Modus umgedreht.

import { Euler, Matrix4 } from 'three';

const matrix = new Matrix4();
const euler = new Euler();

export const DEFAULT_CALIBRATION = {
  head: { x: 0, y: 0, z: 0 },
  gaze: { x: 0, y: 0 },
  blink: { left: 0.25, right: 0.25 },
};

// Wert von [low, high] auf [0, 1] abbilden.
function remap(value, low, high) {
  return Math.max(0, Math.min(1, (value - low) / (high - low)));
}

function clamp1(value) {
  return Math.max(-1, Math.min(1, value));
}

function shapesToMap(categories) {
  const map = {};
  for (const category of categories) {
    map[category.categoryName] = category.score;
  }
  return map;
}

/**
 * @param result       Ergebnis von FaceLandmarker.detectForVideo()
 * @param options      { mirror, swapEyes, invertYaw, invertPitch, invertRoll }
 * @param calibration  Nullpunkt (siehe DEFAULT_CALIBRATION)
 * @returns null, wenn kein Gesicht erkannt wurde
 */
export function mapFaceResult(result, options = {}, calibration = DEFAULT_CALIBRATION) {
  if (!result || !result.faceBlendshapes || !result.faceBlendshapes.length) {
    return null;
  }
  const s = shapesToMap(result.faceBlendshapes[0].categories);
  const get = (name) => s[name] || 0;
  const mirror = options.mirror !== false;

  // ----- Kopfdrehung -----
  let pitch = 0;
  let yaw = 0;
  let roll = 0;
  if (result.facialTransformationMatrixes && result.facialTransformationMatrixes.length) {
    matrix.fromArray(result.facialTransformationMatrixes[0].data);
    euler.setFromRotationMatrix(matrix, 'YXZ');
    pitch = euler.x;
    yaw = euler.y;
    roll = euler.z;
  }
  let headYaw = mirror ? -yaw : yaw;
  let headRoll = mirror ? -roll : roll;
  let headPitch = pitch;
  if (options.invertYaw) headYaw = -headYaw;
  if (options.invertPitch) headPitch = -headPitch;
  if (options.invertRoll) headRoll = -headRoll;
  const head = { x: headPitch, y: headYaw, z: headRoll };

  // ----- Augen -----
  // Im Spiegel-Modus schliesst der Avatar das Auge, das im Spiegelbild auf derselben Seite liegt.
  let swap = mirror;
  if (options.swapEyes) swap = !swap;
  let rawBlinkLeft = get('eyeBlinkLeft');
  let rawBlinkRight = get('eyeBlinkRight');
  if (swap) [rawBlinkLeft, rawBlinkRight] = [rawBlinkRight, rawBlinkLeft];

  // Blick: positiv = zu deiner linken Seite bzw. nach oben.
  let rawGazeX = ((get('eyeLookOutLeft') - get('eyeLookInLeft')) + (get('eyeLookInRight') - get('eyeLookOutRight'))) / 2;
  const rawGazeY = ((get('eyeLookUpLeft') + get('eyeLookUpRight')) - (get('eyeLookDownLeft') + get('eyeLookDownRight'))) / 2;
  if (swap) rawGazeX = -rawGazeX;

  const cal = calibration;
  const blinkLeft = remap(rawBlinkLeft, cal.blink.left + 0.1, cal.blink.left + 0.45);
  const blinkRight = remap(rawBlinkRight, cal.blink.right + 0.1, cal.blink.right + 0.45);

  // ----- Mund -----
  const smile = (get('mouthSmileLeft') + get('mouthSmileRight')) / 2;
  const open = remap(get('jawOpen'), 0.04, 0.5);
  const round = remap(Math.max(get('mouthPucker'), get('mouthFunnel')), 0.15, 0.6);
  const wide = remap(Math.max((get('mouthStretchLeft') + get('mouthStretchRight')) / 2, smile * 0.7), 0.15, 0.6);
  const mouth = {
    aa: open * (1 - round) * (1 - wide * 0.5),
    oh: open * round,
    ou: round * (1 - open),
    ee: wide * (0.3 + open * 0.7),
    ih: wide * (1 - open) * 0.6,
  };

  // ----- Gefühle (grob) -----
  const emotions = {
    happy: remap(smile, 0.3, 0.8),
    surprised: remap(get('browInnerUp'), 0.5, 0.95),
    angry: remap((get('browDownLeft') + get('browDownRight')) / 2, 0.3, 0.8),
    sad: remap((get('mouthFrownLeft') + get('mouthFrownRight')) / 2, 0.2, 0.6),
  };

  return {
    head: {
      x: head.x - cal.head.x,
      y: head.y - cal.head.y,
      z: head.z - cal.head.z,
    },
    blink: { left: blinkLeft, right: blinkRight },
    gaze: {
      x: clamp1((rawGazeX - cal.gaze.x) * 1.4),
      y: clamp1((rawGazeY - cal.gaze.y) * 1.4),
    },
    mouth,
    mouthLevel: open,
    emotions,
    // Werte ohne Nullpunkt, damit "Nullpunkt setzen" sie speichern kann.
    calibrationSample: {
      head,
      gaze: { x: rawGazeX, y: rawGazeY },
      blink: { left: rawBlinkLeft, right: rawBlinkRight },
    },
    raw: { pitch, yaw, roll, jawOpen: get('jawOpen'), smile, eyeBlinkLeft: get('eyeBlinkLeft'), eyeBlinkRight: get('eyeBlinkRight') },
  };
}
