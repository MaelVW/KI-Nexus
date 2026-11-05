// Liest Gefühls-Markierungen wie [[gefuehl:freude]] aus einem KI-Text
// und gibt den Text ohne Markierungen zurück. So wie später in KI-Nexus.

import { EMOTIONS } from './expression-mixer.js';

const TAG_PATTERN = /\[\[\s*gef(?:ue|ü)hl\s*:\s*([^\]\s]+)\s*\]\]/gi;

export function normalizeEmotionName(name) {
  return String(name)
    .trim()
    .toLowerCase()
    .replace(/ä/g, 'ae')
    .replace(/ö/g, 'oe')
    .replace(/ü/g, 'ue');
}

export function parseEmotionTags(text) {
  const emotions = [];
  const unknown = [];
  let cleanText = '';
  let lastIndex = 0;

  for (const match of String(text).matchAll(TAG_PATTERN)) {
    cleanText += text.slice(lastIndex, match.index);
    lastIndex = match.index + match[0].length;
    const name = normalizeEmotionName(match[1]);
    if (name in EMOTIONS) {
      emotions.push({ name, position: cleanText.length });
    } else {
      unknown.push(match[1]);
    }
  }
  cleanText += String(text).slice(lastIndex);
  cleanText = cleanText.replace(/[ \t]{2,}/g, ' ').trim();

  return { cleanText, emotions, unknown };
}
