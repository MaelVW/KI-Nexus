// Einstiegspunkt: verbindet Avatar, Befehls-Schnittstelle und Panel.
// Mit "index.html?nurAvatar" erscheint nur der Avatar, ohne Panel.
// So wird die Seite später in KI-Nexus (WebView2) eingebunden.

import { AvatarController } from './avatar-controller.js';
import { CommandApi } from './command-api.js';
import { ControlPanel } from './panel.js';
import { FileLoader, filesFromDrop } from './file-loader.js';
import { probeLive2dCore } from './renderers/live2d-support.js';

const params = new URLSearchParams(window.location.search);
const avatarOnly = params.has('nurAvatar');

const stage = document.getElementById('stage');
const avatar = new AvatarController(stage);
const api = new CommandApi(avatar);
const loader = new FileLoader(avatar, api);

// Für Tests in der Browser-Konsole, z.B. avatarApi.handle({ type: 'blink' })
window.avatarApi = api;

let panel = null;
if (avatarOnly) {
  document.body.classList.add('avatar-only');
} else {
  panel = new ControlPanel(document.getElementById('panel'), api, avatar, loader);
  loader.onAudioName = (name) => { panel.audioName.textContent = name; };
}

// Still prüfen, ob Cubism Core für Live2D schon da ist.
probeLive2dCore().then(() => panel && panel.refreshCoreStatus());

// ---------- Datei per Drag & Drop ----------

const dropHint = document.getElementById('drop-hint');
let dragDepth = 0;

window.addEventListener('dragenter', (event) => {
  event.preventDefault();
  dragDepth += 1;
  document.body.classList.add('dragging');
});
window.addEventListener('dragleave', () => {
  dragDepth = Math.max(0, dragDepth - 1);
  if (dragDepth === 0) document.body.classList.remove('dragging');
});
window.addEventListener('dragover', (event) => event.preventDefault());
window.addEventListener('drop', async (event) => {
  event.preventDefault();
  dragDepth = 0;
  document.body.classList.remove('dragging');
  const files = await filesFromDrop(event.dataTransfer);
  await loader.loadFiles(files);
});

avatar.on((event) => {
  if (event.type === 'loaded') dropHint.classList.add('hidden');
  if (event.type === 'unloaded') dropHint.classList.remove('hidden');
});

// ---------- Sprechblase beim simulierten KI-Text ----------

const bubble = document.getElementById('speech-bubble');
let bubbleTimer = null;

function showBubble(text) {
  clearTimeout(bubbleTimer);
  bubble.textContent = text;
  bubble.classList.add('visible');
}

api.onLog((entry) => {
  if (entry.ok && entry.payload && entry.payload.type === 'simulateText') {
    showBubble(parseCleanText(entry.payload.text));
  }
});

avatar.on((event) => {
  // Die Sprechblase verschwindet, sobald der Avatar nicht mehr spricht.
  if (event.type === 'stateChanged' && event.state !== 'speaking') {
    clearTimeout(bubbleTimer);
    bubbleTimer = setTimeout(() => bubble.classList.remove('visible'), 800);
  }
  if (event.type === 'demoLine') {
    api.handle({ type: 'simulateText', text: event.text }).then((result) => {
      if (result.ok && panel) panel.showTextResult(result);
    });
  }
});

// Nur für die Sprechblase: Markierungen entfernen, ohne Gefühle auszulösen.
function parseCleanText(text) {
  return String(text || '').replace(/\[\[[^\]]*\]\]/g, '').replace(/[ \t]{2,}/g, ' ').trim();
}
