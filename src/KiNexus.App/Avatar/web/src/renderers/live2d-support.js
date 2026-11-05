// Lädt alles, was Live2D braucht, erst bei Bedarf:
// 1. Cubism Core (von Live2D, nicht Open Source, legst du selbst dazu)
// 2. js/live2d-bundle.js (PixiJS + pixi-live2d-display)
//
// Cubism Core wird so gesucht:
//   a) Datei live2d/live2dcubismcore.min.js neben index.html
//   b) eine Kopie, die du im Panel gewählt hast (im Browser gespeichert)

import { loadScript, runScriptText } from '../script-loader.js';

const CORE_PATH = 'live2d/live2dcubismcore.min.js';
const STORAGE_KEY = 'ki-nexus-cubism-core';

// Version der geladenen Cubism Core als Text, z.B. "5.1.0".
export function coreVersion() {
  const core = window.Live2DCubismCore;
  if (!core || !core.Version || !core.Version.csmGetVersion) return '';
  const v = core.Version.csmGetVersion();
  return `${(v >>> 24) & 0xff}.${(v >> 16) & 0xff}.${v & 0xffff}`;
}

// pixi-live2d-display braucht eine neuere Cubism Core (mit "Memory"-Funktionen).
function checkCoreIsRecent() {
  const core = window.Live2DCubismCore;
  if (!core.Memory || typeof core.Memory.initializeAmountOfMemory !== 'function') {
    throw new Error(`Cubism Core ${coreVersion() || ''} ist zu alt. Lade die aktuelle Version (Cubism SDK for Web 5) von Live2D herunter.`.replace('  ', ' '));
  }
}

export const live2dSupport = {
  coreSource: '',

  get coreLoaded() {
    return Boolean(window.Live2DCubismCore);
  },

  async ensureCore() {
    if (this.coreLoaded) return;
    try {
      await loadScript(CORE_PATH);
      if (this.coreLoaded) {
        this.coreSource = 'Ordner live2d/';
        return;
      }
    } catch {
      // Datei fehlt, dann die gespeicherte Kopie versuchen.
    }
    let saved = null;
    try {
      saved = localStorage.getItem(STORAGE_KEY);
    } catch {
      saved = null;
    }
    if (saved) {
      await runScriptText(saved);
      if (this.coreLoaded) {
        this.coreSource = 'im Browser gespeichert';
        return;
      }
    }
    throw new Error('Cubism Core fehlt. Wähle im Panel "Cubism Core wählen" oder leg die Datei in den Ordner live2d/.');
  },

  // Datei live2dcubismcore.min.js aus dem Panel übernehmen.
  async useCoreFile(file) {
    const text = await file.text();
    if (!text.includes('Live2DCubismCore')) {
      throw new Error('Das ist keine Cubism-Core-Datei (live2dcubismcore.min.js).');
    }
    // Ist schon eine (ältere) Core geladen, gilt die neue erst nach dem Neuladen der Seite.
    const needsReload = this.coreLoaded;
    if (!needsReload) {
      await runScriptText(text);
      this.coreSource = 'gewählte Datei';
    }
    try {
      localStorage.setItem(STORAGE_KEY, text);
      if (!needsReload) this.coreSource = 'im Browser gespeichert';
    } catch {
      // Ohne Speicher musst du die Datei beim nächsten Mal nochmals wählen.
    }
    return { needsReload };
  },

  forgetCore() {
    try {
      localStorage.removeItem(STORAGE_KEY);
    } catch {
      // nichts zu tun
    }
  },

  async ensureBundle() {
    await this.ensureCore();
    checkCoreIsRecent();
    if (!window.KiNexusLive2D) {
      await loadScript('js/live2d-bundle.js');
    }
    return window.KiNexusLive2D;
  },

  async createRenderer(container) {
    const lib = await this.ensureBundle();
    return new lib.Live2dRenderer(container);
  },

  async filesFromZip(file) {
    const lib = await this.ensureBundle();
    return lib.filesFromZip(file);
  },
};

// Beim Start still prüfen, ob Cubism Core schon da ist (für die Anzeige im Panel).
export async function probeLive2dCore() {
  try {
    await live2dSupport.ensureCore();
    return true;
  } catch {
    return false;
  }
}
