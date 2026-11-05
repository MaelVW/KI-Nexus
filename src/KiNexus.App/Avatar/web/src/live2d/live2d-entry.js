// Einstieg für js/live2d-bundle.js. Wird nachgeladen, sobald Cubism Core da ist.

import JSZip from 'jszip';
import { Live2DModel } from 'pixi-live2d-display-lipsyncpatch/cubism4';
import { Live2dRenderer } from './live2d-renderer.js';

// Macht aus einer ZIP-Datei eine Liste von Dateien mit Pfad (webkitRelativePath),
// so wie bei einem gewählten Ordner. pixi-live2d-display findet die Teile darüber.
async function filesFromZip(zipFile) {
  const zip = await JSZip.loadAsync(zipFile);
  const files = [];
  const entries = Object.values(zip.files).filter((entry) => !entry.dir);
  for (const entry of entries) {
    const blob = await entry.async('blob');
    const file = new File([blob], entry.name.split('/').pop());
    Object.defineProperty(file, 'webkitRelativePath', { value: entry.name });
    files.push(file);
  }
  return files;
}

// Live2DModel ist für eigene Tests in der Browser-Konsole mit dabei.
window.KiNexusLive2D = { Live2dRenderer, filesFromZip, Live2DModel };
