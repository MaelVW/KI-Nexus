// Stellt ein VRM-Modell (3D) mit three.js und three-vrm dar.
// Bekommt pro Bild die Haltung, das Gesicht und den Blick vom AvatarController.

import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { VRMLoaderPlugin, VRMUtils } from '@pixiv/three-vrm';
import { VRMAnimationLoaderPlugin, VRMLookAtQuaternionProxy, createVRMAnimationClip } from '@pixiv/three-vrm-animation';
import { describeMeta } from '../meta-info.js';
import { MOUTH_SHAPES } from '../expression-mixer.js';

const THEME_BACKGROUND = '#0A0E13';
const DEG = Math.PI / 180;

export class VrmRenderer {
  constructor(container) {
    this.kind = 'vrm';
    this.container = container;
    this.vrm = null;
    this.fileName = '';
    this.manual = new Map();
    this.framing = 'oberkoerper';
    this.pendingScreenshot = null;
    this.windStrength = 0;
    this.windDirection = 90;
    this.windTime = 0;
    this.springDefaults = new Map();
    this.animation = { mixer: null, action: null, name: '', loop: true, speed: 1 };

    this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    this.canvas = this.renderer.domElement;
    this.canvas.classList.add('avatar-canvas');
    container.appendChild(this.canvas);

    this.scene = new THREE.Scene();
    this.camera = new THREE.PerspectiveCamera(30, 1, 0.05, 50);
    this.camera.position.set(0, 1.3, 1.4);

    this.keyLight = new THREE.DirectionalLight(0xffffff, Math.PI * 0.7);
    this.scene.add(this.keyLight);
    this.ambient = new THREE.AmbientLight(0xffffff, Math.PI * 0.12);
    this.scene.add(this.ambient);
    this.setLight({ intensity: 0.7, ambient: 0.12, angle: 27 });

    this.controls = new OrbitControls(this.camera, this.canvas);
    this.controls.enableDamping = true;
    this.controls.screenSpacePanning = true;
    this.controls.target.set(0, 1.25, 0);
    this.controls.update();

    this.lookTarget = new THREE.Object3D();
    this.scene.add(this.lookTarget);
    this.resize();
  }

  // ---------- Laden ----------

  async load(buffer, fileName) {
    const loader = new GLTFLoader();
    loader.register((parser) => new VRMLoaderPlugin(parser));
    const gltf = await loader.parseAsync(buffer, '');
    const vrm = gltf.userData.vrm;
    if (!vrm) {
      throw new Error('Diese Datei ist kein VRM-Modell.');
    }

    this.unload();
    VRMUtils.removeUnnecessaryVertices(gltf.scene);
    VRMUtils.combineSkeletons(gltf.scene);
    VRMUtils.rotateVRM0(vrm);
    vrm.scene.traverse((object) => {
      object.frustumCulled = false;
    });

    // Nötig, damit VRMA-Animationen auch den Blick steuern können.
    if (vrm.lookAt) {
      const proxy = new VRMLookAtQuaternionProxy(vrm.lookAt);
      proxy.name = 'VRMLookAtQuaternionProxy';
      vrm.scene.add(proxy);
    }

    this.scene.add(vrm.scene);
    this.vrm = vrm;
    this.fileName = fileName;
    this.manual.clear();
    this.rememberSpringBones();
    vrm.update(0);
    this.setFraming(this.framing);
  }

  unload() {
    this.stopAnimation();
    if (!this.vrm) return;
    this.scene.remove(this.vrm.scene);
    VRMUtils.deepDispose(this.vrm.scene);
    this.vrm = null;
    this.fileName = '';
    this.springDefaults.clear();
  }

  get loaded() {
    return Boolean(this.vrm);
  }

  get expressionNames() {
    return this.vrm && this.vrm.expressionManager
      ? this.vrm.expressionManager.expressions.map((e) => e.expressionName)
      : [];
  }

  metaInfo() {
    return describeMeta(this.vrm && this.vrm.meta);
  }

  // Liste für "Alle Ausdrücke": bei VRM ein Regler pro Ausdruck.
  controlList() {
    return { expressions: this.expressionNames.map((name) => ({ name, min: 0, max: 1, value: this.manual.get(name) || 0 })), parameters: [] };
  }

  hasExpression(name) {
    return this.expressionNames.includes(name);
  }

  setManual(name, value) {
    if (value <= 0) this.manual.delete(name);
    else this.manual.set(name, Math.min(1, value));
  }

  clearManual() {
    this.manual.clear();
  }

  // ---------- Animationen (VRMA) ----------

  async loadAnimation(buffer, name) {
    if (!this.vrm) {
      throw new Error('Lade zuerst ein VRM-Modell.');
    }
    const loader = new GLTFLoader();
    loader.register((parser) => new VRMAnimationLoaderPlugin(parser));
    const gltf = await loader.parseAsync(buffer, '');
    const vrmAnimation = gltf.userData.vrmAnimations && gltf.userData.vrmAnimations[0];
    if (!vrmAnimation) {
      throw new Error('Diese Datei ist keine VRMA-Animation.');
    }
    this.stopAnimation();
    const clip = createVRMAnimationClip(vrmAnimation, this.vrm);
    const mixer = new THREE.AnimationMixer(this.vrm.scene);
    const action = mixer.clipAction(clip);
    this.animation.mixer = mixer;
    this.animation.action = action;
    this.animation.name = name;
    this.applyAnimationSettings();
    action.play();
  }

  playAnimation() {
    if (!this.animation.action) throw new Error('Zuerst eine VRMA-Datei laden.');
    this.animation.action.reset().play();
  }

  stopAnimation() {
    if (this.animation.action) {
      this.animation.action.stop();
    }
    if (this.animation.mixer && this.vrm) {
      this.animation.mixer.uncacheRoot(this.vrm.scene);
    }
    this.animation.mixer = null;
    this.animation.action = null;
    this.animation.name = '';
  }

  setAnimationSettings({ loop, speed }) {
    if (loop != null) this.animation.loop = Boolean(loop);
    if (speed != null) this.animation.speed = speed;
    this.applyAnimationSettings();
  }

  applyAnimationSettings() {
    const action = this.animation.action;
    if (!action) return;
    action.setLoop(this.animation.loop ? THREE.LoopRepeat : THREE.LoopOnce, Infinity);
    action.clampWhenFinished = !this.animation.loop;
    action.timeScale = this.animation.speed;
  }

  get animationPlaying() {
    const action = this.animation.action;
    return Boolean(action && action.isRunning());
  }

  // ---------- Darstellung ----------

  setFraming(framing) {
    this.framing = framing;
    if (!this.vrm) return;
    const head = new THREE.Vector3();
    const hips = new THREE.Vector3();
    this.vrm.humanoid.getRawBoneNode('head').getWorldPosition(head);
    this.vrm.humanoid.getRawBoneNode('hips').getWorldPosition(hips);
    const height = head.y + 0.15;
    const scale = height / 1.6;
    const fovFactor = Math.tan(15 * DEG) / Math.tan((this.camera.fov / 2) * DEG);

    // Der Kopf-Knochen sitzt unten am Kopf, darum liegt die Bildmitte etwas höher.
    const target = new THREE.Vector3();
    let distance;
    if (framing === 'kopf') {
      target.set(head.x, head.y + 0.09 * scale, head.z);
      distance = 0.75 * scale;
    } else if (framing === 'oberkoerper') {
      target.set(head.x, head.y - 0.06 * scale, head.z);
      distance = 1.5 * scale;
    } else {
      target.set(hips.x, height / 2, hips.z);
      distance = height * 2.1;
    }
    distance *= fovFactor;

    // Das Modell schaut nach +Z, also steht die Kamera davor auf +Z.
    this.camera.position.set(target.x, target.y, target.z + distance);
    this.controls.target.copy(target);
    this.controls.update();
  }

  setBackground(mode, color) {
    if (mode === 'theme') this.renderer.setClearColor(THEME_BACKGROUND, 1);
    else if (mode === 'farbe') this.renderer.setClearColor(color, 1);
    else this.renderer.setClearColor(0x000000, 0);
  }

  setPixelRatio(ratio) {
    this.renderer.setPixelRatio(ratio);
    this.resize();
  }

  setLight({ intensity, ambient, angle }) {
    if (intensity != null) this.keyLight.intensity = Math.PI * intensity;
    if (ambient != null) this.ambient.intensity = Math.PI * ambient;
    if (angle != null) {
      const a = angle * DEG;
      this.keyLight.position.set(Math.sin(a) * 2, 1.5, Math.cos(a) * 2);
    }
  }

  setFov(fov) {
    this.camera.fov = fov;
    this.camera.updateProjectionMatrix();
    this.setFraming(this.framing);
  }

  setAutoRotate(enabled, speed = 1) {
    this.controls.autoRotate = Boolean(enabled);
    this.controls.autoRotateSpeed = speed;
  }

  setWind(strength, direction) {
    if (strength != null) this.windStrength = strength;
    if (direction != null) this.windDirection = direction;
  }

  resize() {
    const width = Math.max(1, this.container.clientWidth);
    const height = Math.max(1, this.container.clientHeight);
    this.renderer.setSize(width, height);
    this.camera.aspect = width / height;
    this.camera.updateProjectionMatrix();
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
    const vrm = this.vrm;
    if (!vrm) {
      this.controls.update();
      return;
    }

    if (this.animationPlaying) {
      this.animation.mixer.update(delta);
    } else {
      this.applyPose(input.pose);
    }
    this.applyExpressions(input.face);
    this.applyGaze(input.gaze);
    this.updateWind(delta);
    vrm.update(delta);
    this.controls.update();
  }

  applyPose(pose) {
    const set = (name, x, y, z) => {
      const bone = this.vrm.humanoid.getNormalizedBoneNode(name);
      if (bone) bone.rotation.set(x, y, z);
    };
    // Grundpose: Arme aus der T-Pose nach unten, Ellbogen leicht gebeugt.
    set('leftUpperArm', 0, 0, -pose.armAngle);
    set('rightUpperArm', 0, 0, pose.armAngle);
    set('leftLowerArm', 0, -pose.elbow, 0);
    set('rightLowerArm', 0, pose.elbow, 0);
    set('spine', pose.body.x, pose.body.y, pose.body.z);
    set('chest', pose.breathTilt, 0, 0);
    set('upperChest', pose.breathTilt * 0.5, 0, 0);
    const h = pose.head;
    set('neck', h.x * 0.4, h.y * 0.4, h.z * 0.4);
    set('head', h.x * 0.6, h.y * 0.6, h.z * 0.6);
  }

  applyExpressions(face) {
    const manager = this.vrm.expressionManager;
    if (!manager) return;
    const names = this.expressionNames;
    const final = new Map(names.map((n) => [n, 0]));
    const raise = (name, value) => {
      if (final.has(name) && value > final.get(name)) final.set(name, value);
    };

    for (const [name, weight] of this.manual) raise(name, weight);
    for (const [preset, weight] of face.emotions) raise(preset, weight);

    const { left, right } = face.blink;
    if (Math.abs(left - right) < 0.05 || !final.has('blinkLeft')) {
      raise('blink', Math.max(left, right));
    } else {
      raise('blinkLeft', left);
      raise('blinkRight', right);
    }
    for (const shape of MOUTH_SHAPES) raise(shape, face.mouth[shape]);

    for (const [name, value] of final) manager.setValue(name, value);
  }

  applyGaze(gaze) {
    const lookAt = this.vrm.lookAt;
    if (!lookAt) return;
    if (gaze.mode === 'off') {
      lookAt.target = null;
      lookAt.reset();
    } else if (gaze.mode === 'camera') {
      this.camera.getWorldPosition(this.lookTarget.position);
      lookAt.target = this.lookTarget;
    } else {
      // Winkel direkt setzen. Positives yaw = zur eigenen linken Seite, negatives pitch = nach oben.
      lookAt.target = null;
      lookAt.yaw = gaze.x * 25;
      lookAt.pitch = -gaze.y * 15;
    }
  }

  // ---------- Wind für die Haar- und Kleiderphysik ----------

  rememberSpringBones() {
    this.springDefaults.clear();
    const manager = this.vrm.springBoneManager;
    if (!manager) return;
    for (const joint of manager.joints) {
      this.springDefaults.set(joint, {
        power: joint.settings.gravityPower,
        dir: joint.settings.gravityDir.clone(),
      });
    }
  }

  updateWind(delta) {
    if (!this.springDefaults.size) return;
    this.windTime += delta;
    const gust = this.windStrength * (0.65 + 0.35 * Math.sin(this.windTime * 1.7) * Math.sin(this.windTime * 0.6 + 1));
    // Richtung um die Hochachse. 90° bläst von der linken Seite des Avatars.
    const a = this.windDirection * DEG;
    const wind = new THREE.Vector3(-Math.sin(a), 0, -Math.cos(a)).multiplyScalar(gust);
    for (const [joint, base] of this.springDefaults) {
      const force = base.dir.clone().multiplyScalar(base.power).add(wind);
      joint.settings.gravityPower = force.length();
      if (force.lengthSq() > 0) joint.settings.gravityDir.copy(force.normalize());
    }
  }

  render() {
    this.renderer.render(this.scene, this.camera);
    if (this.pendingScreenshot) {
      const resolve = this.pendingScreenshot;
      this.pendingScreenshot = null;
      this.canvas.toBlob(resolve, 'image/png');
    }
  }
}
