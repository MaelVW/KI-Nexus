// Baut den Quellcode in src/ zu drei Dateien zusammen:
//   js/avatar-bundle.js  Hauptteil mit VRM (three.js, three-vrm)
//   js/live2d-bundle.js  Live2D (PixiJS, pixi-live2d-display), wird nachgeladen
//   js/face-bundle.js    Gesichtserkennung (MediaPipe), wird nachgeladen
// Ausserdem legt es vendor/mediapipe/ an (WebAssembly und Modell als Base64),
// damit alles offline und direkt als Datei (file://) läuft.
const fs = require('fs');
const path = require('path');
const esbuild = require('esbuild');

const root = __dirname;
const nodePaths = (process.env.NODE_PATH || '').split(path.delimiter).filter(Boolean);
const resolveModule = (request) => require.resolve(request, { paths: [root, ...nodePaths] });

const bundles = [
  ['src/main.js', 'js/avatar-bundle.js'],
  ['src/live2d/live2d-entry.js', 'js/live2d-bundle.js'],
  ['src/face/face-tracker-entry.js', 'js/face-bundle.js'],
];

function writeBase64Script(source, target, globalName) {
  const base64 = fs.readFileSync(source).toString('base64');
  fs.writeFileSync(target, `window.${globalName}="${base64}";\n`);
  console.log(`  ${path.relative(root, target)}  ${(fs.statSync(target).size / 1e6).toFixed(1)} MB`);
}

function buildVendor() {
  const vendor = path.join(root, 'vendor', 'mediapipe');
  fs.mkdirSync(vendor, { recursive: true });
  const wasmDir = path.join(path.dirname(resolveModule('@mediapipe/tasks-vision')), 'wasm');
  // Auch das Lade-Skript kommt als Base64. MediaPipe lädt es mit crossOrigin,
  // und das blockiert der Browser bei direkt geöffneten Dateien (file://).
  writeBase64Script(path.join(wasmDir, 'vision_wasm_internal.js'), path.join(vendor, 'vision_wasm_internal.loader.js'), 'KiNexusMediaPipeLoader');
  writeBase64Script(path.join(wasmDir, 'vision_wasm_internal.wasm'), path.join(vendor, 'vision_wasm_internal.wasm.js'), 'KiNexusMediaPipeWasm');

  // Das Modell lädst du einmal herunter (siehe README) und legst es nach models/.
  const model = path.join(root, 'models', 'face_landmarker.task');
  if (fs.existsSync(model)) {
    writeBase64Script(model, path.join(vendor, 'face_landmarker.task.js'), 'KiNexusFaceModel');
  } else if (!fs.existsSync(path.join(vendor, 'face_landmarker.task.js'))) {
    console.warn('  Hinweis: models/face_landmarker.task fehlt. Die Gesichtserkennung geht erst mit dieser Datei.');
  }
}

(async () => {
  for (const [entry, out] of bundles) {
    await esbuild.build({
      entryPoints: [path.join(root, entry)],
      outfile: path.join(root, out),
      bundle: true,
      format: 'iife',
      target: 'es2020',
      minify: true,
      legalComments: 'eof',
      nodePaths,
      logLevel: 'warning',
    });
    console.log(`  ${out}  ${(fs.statSync(path.join(root, out)).size / 1e6).toFixed(2)} MB`);
  }
  if (process.argv.includes('--vendor') || !fs.existsSync(path.join(root, 'vendor', 'mediapipe', 'vision_wasm_internal.loader.js'))) {
    buildVendor();
  }
})().catch((error) => {
  console.error(error);
  process.exit(1);
});
