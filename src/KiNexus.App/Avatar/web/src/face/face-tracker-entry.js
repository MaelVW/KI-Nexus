// Eigenes Paket (js/face-bundle.js), das erst beim Start der Gesichtserkennung
// geladen wird. Es enthält MediaPipe Tasks Vision (Apache 2.0).

import { FaceLandmarker } from '@mediapipe/tasks-vision';

async function createLandmarker(fileset, modelBuffer, delegate) {
  return FaceLandmarker.createFromOptions(fileset, {
    baseOptions: { modelAssetBuffer: modelBuffer, delegate },
    runningMode: 'VIDEO',
    numFaces: 1,
    outputFaceBlendshapes: true,
    outputFacialTransformationMatrixes: true,
  });
}

// Erst die Grafikkarte (GPU) versuchen, sonst die CPU.
async function createFaceTracker({ wasmLoaderUrl, wasmBinaryUrl, modelBuffer }) {
  const fileset = { wasmLoaderPath: wasmLoaderUrl, wasmBinaryPath: wasmBinaryUrl };
  let landmarker;
  let delegate = 'GPU';
  try {
    landmarker = await createLandmarker(fileset, modelBuffer, 'GPU');
  } catch (error) {
    delegate = 'CPU';
    landmarker = await createLandmarker(fileset, modelBuffer, 'CPU');
  }
  return {
    delegate,
    detect: (video, timestampMs) => landmarker.detectForVideo(video, timestampMs),
    close: () => landmarker.close(),
  };
}

window.KiNexusFace = { createFaceTracker };
