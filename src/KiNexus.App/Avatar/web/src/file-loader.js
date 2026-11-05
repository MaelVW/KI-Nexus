// Entscheidet anhand der Dateien, was geladen wird:
//   .vrm → VRM-Modell        .vrma → VRM-Animation
//   .zip → Live2D-Modell     Ordner mit .model3.json → Live2D-Modell
//   Bild → Hintergrund       Audio → Lippen-Test

import { live2dSupport } from './renderers/live2d-support.js';

function extension(name) {
  const dot = name.lastIndexOf('.');
  return dot >= 0 ? name.slice(dot + 1).toLowerCase() : '';
}

// Liest einen gezogenen Ordner (Drag & Drop) rekursiv aus.
// Jede Datei bekommt ihren Pfad als webkitRelativePath, so wie bei "Ordner wählen".
async function readEntry(entry, path, out) {
  if (entry.isFile) {
    const file = await new Promise((resolve, reject) => entry.file(resolve, reject));
    Object.defineProperty(file, 'webkitRelativePath', { value: path + file.name });
    out.push(file);
    return;
  }
  if (entry.isDirectory) {
    const reader = entry.createReader();
    let batch;
    do {
      batch = await new Promise((resolve, reject) => reader.readEntries(resolve, reject));
      for (const child of batch) {
        await readEntry(child, `${path}${entry.name}/`, out);
      }
    } while (batch.length);
  }
}

export async function filesFromDrop(dataTransfer) {
  const entries = [...dataTransfer.items]
    .map((item) => (item.webkitGetAsEntry ? item.webkitGetAsEntry() : null))
    .filter(Boolean);
  if (!entries.some((entry) => entry.isDirectory)) {
    return [...dataTransfer.files];
  }
  const files = [];
  for (const entry of entries) {
    await readEntry(entry, '', files);
  }
  return files;
}

export class FileLoader {
  constructor(avatar, api) {
    this.avatar = avatar;
    this.api = api;
    this.onAudioName = null;
  }

  fail(name, error) {
    this.api.log({ direction: 'in', payload: { type: 'loadFile', name }, ok: false, message: error.message });
  }

  done(name, message) {
    this.api.log({ direction: 'in', payload: { type: 'loadFile', name }, ok: true, message });
  }

  async loadFiles(files) {
    if (!files.length) return;
    const settings = files.find((f) => /\.model3\.json$/i.test(f.name));
    const first = files[0];
    const ext = extension(first.name);
    try {
      if (settings) {
        await this.loadLive2dFiles(files, settings);
      } else if (ext === 'vrm') {
        await this.avatar.loadVrm(await first.arrayBuffer(), first.name);
      } else if (ext === 'vrma') {
        if (this.avatar.kind !== 'vrm') throw new Error('Lade zuerst ein VRM-Modell. VRMA geht nur mit VRM.');
        await this.avatar.renderer.loadAnimation(await first.arrayBuffer(), first.name);
        this.done(first.name, 'Animation läuft');
      } else if (ext === 'zip') {
        const zipped = await live2dSupport.filesFromZip(first);
        const zipSettings = zipped.find((f) => /\.model3\.json$/i.test(f.name));
        if (!zipSettings) throw new Error('In der ZIP-Datei ist keine .model3.json (Live2D Cubism 3 oder neuer).');
        await this.loadLive2dFiles(zipped, zipSettings, first.name);
      } else if (first.type.startsWith('image/')) {
        await this.loadBackground(first);
      } else if (first.type.startsWith('audio/')) {
        await this.loadAudio(first);
      } else {
        throw new Error('Diese Datei kenne ich nicht. Erlaubt: .vrm, .vrma, .zip, Live2D-Ordner, Bild, Audio.');
      }
    } catch (error) {
      this.fail(first.name, error);
    }
  }

  async loadLive2dFiles(files, settings, label) {
    // Dateien ohne Pfad (einzeln gewählt) bekommen ihren Namen als Pfad.
    for (const file of files) {
      if (!file.webkitRelativePath) {
        Object.defineProperty(file, 'webkitRelativePath', { value: file.name });
      }
    }
    const name = label || settings.webkitRelativePath.split('/')[0] || settings.name;
    await this.avatar.loadLive2d(files, name);
  }

  async loadBackground(file) {
    const reader = new FileReader();
    const dataUrl = await new Promise((resolve, reject) => {
      reader.onload = () => resolve(reader.result);
      reader.onerror = () => reject(new Error('Bild konnte nicht gelesen werden.'));
      reader.readAsDataURL(file);
    });
    await this.api.handle({ type: 'setBackground', mode: 'bild', imageUrl: dataUrl }, { silent: true });
    this.done(file.name, 'Hintergrundbild gesetzt');
  }

  async loadAudio(file) {
    this.avatar.lipSync.loadFile(file);
    if (this.onAudioName) this.onAudioName(file.name);
    await this.api.handle({ type: 'setLipSyncMode', mode: 'datei' });
  }

  async playAudio() {
    try {
      await this.avatar.lipSync.playFile();
      if (this.avatar.lipSync.mode !== 'datei') {
        await this.api.handle({ type: 'setLipSyncMode', mode: 'datei' });
      }
    } catch (error) {
      this.fail('Audio', error);
    }
  }
}
