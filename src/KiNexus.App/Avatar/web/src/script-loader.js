// Lädt eine JavaScript-Datei nachträglich über ein <script>-Tag.
// Das funktioniert auch, wenn die Seite direkt als Datei geöffnet ist (file://).

const loaded = new Map();

export function loadScript(src) {
  if (loaded.has(src)) {
    return loaded.get(src);
  }
  const promise = new Promise((resolve, reject) => {
    const script = document.createElement('script');
    script.src = src;
    script.onload = () => resolve();
    script.onerror = () => {
      loaded.delete(src);
      script.remove();
      reject(new Error(`Datei nicht gefunden: ${src}`));
    };
    document.head.append(script);
  });
  loaded.set(src, promise);
  return promise;
}

// Lädt JavaScript aus einem Text (z.B. aus einer gewählten Datei).
export function runScriptText(text) {
  return new Promise((resolve, reject) => {
    const url = URL.createObjectURL(new Blob([text], { type: 'text/javascript' }));
    const script = document.createElement('script');
    script.src = url;
    script.onload = () => {
      URL.revokeObjectURL(url);
      resolve();
    };
    script.onerror = () => {
      URL.revokeObjectURL(url);
      reject(new Error('Die Datei konnte nicht ausgeführt werden.'));
    };
    document.head.append(script);
  });
}

export function base64ToBytes(base64) {
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i += 1) {
    bytes[i] = binary.charCodeAt(i);
  }
  return bytes;
}
