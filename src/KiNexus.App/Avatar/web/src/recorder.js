// Nimmt den Avatar als Video auf (WebM) und bietet die Datei zum Speichern an.

export class CanvasRecorder {
  constructor() {
    this.recorder = null;
    this.chunks = [];
  }

  get recording() {
    return Boolean(this.recorder && this.recorder.state === 'recording');
  }

  start(stream) {
    if (typeof MediaRecorder === 'undefined') {
      throw new Error('Dieser Browser kann keine Videos aufnehmen.');
    }
    const types = ['video/webm;codecs=vp9', 'video/webm;codecs=vp8', 'video/webm'];
    const mimeType = types.find((t) => MediaRecorder.isTypeSupported(t)) || '';
    this.chunks = [];
    this.recorder = new MediaRecorder(stream, mimeType ? { mimeType, videoBitsPerSecond: 6_000_000 } : undefined);
    this.recorder.ondataavailable = (event) => {
      if (event.data.size) this.chunks.push(event.data);
    };
    this.recorder.start(250);
  }

  // Gibt das fertige Video als Blob zurück.
  stop() {
    return new Promise((resolve) => {
      if (!this.recorder) {
        resolve(null);
        return;
      }
      this.recorder.onstop = () => {
        const blob = new Blob(this.chunks, { type: 'video/webm' });
        this.recorder.stream.getTracks().forEach((track) => track.stop());
        this.recorder = null;
        this.chunks = [];
        resolve(blob);
      };
      this.recorder.stop();
    });
  }
}
