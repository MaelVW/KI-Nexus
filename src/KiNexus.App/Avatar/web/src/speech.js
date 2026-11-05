// Vorlesen mit der Stimme des Browsers (Web Speech API, z.B. "Microsoft Katja").
// Das ist nur zum Testen. In KI-Nexus kommt die Stimme später von Piper.

export class BrowserSpeech {
  constructor() {
    this.supported = typeof window !== 'undefined' && 'speechSynthesis' in window;
    this.voiceUri = '';
    this.rate = 1;
    this.speaking = false;
  }

  // Deutsche Stimmen zuerst.
  voices() {
    if (!this.supported) return [];
    return window.speechSynthesis.getVoices()
      .slice()
      .sort((a, b) => Number(b.lang.startsWith('de')) - Number(a.lang.startsWith('de')));
  }

  // onBoundary(charIndex) wird bei jedem Wort aufgerufen.
  speak(text, { onBoundary, onEnd } = {}) {
    if (!this.supported) {
      throw new Error('Dieser Browser kann nicht vorlesen.');
    }
    window.speechSynthesis.cancel();
    const utterance = new SpeechSynthesisUtterance(text);
    const voice = this.voices().find((v) => v.voiceURI === this.voiceUri) || this.voices().find((v) => v.lang.startsWith('de'));
    if (voice) {
      utterance.voice = voice;
      utterance.lang = voice.lang;
    } else {
      utterance.lang = 'de-CH';
    }
    utterance.rate = this.rate;
    utterance.onboundary = (event) => {
      if (onBoundary) onBoundary(event.charIndex);
    };
    const finish = () => {
      this.speaking = false;
      if (onEnd) onEnd();
    };
    utterance.onend = finish;
    utterance.onerror = finish;
    this.speaking = true;
    window.speechSynthesis.speak(utterance);
  }

  cancel() {
    if (this.supported) window.speechSynthesis.cancel();
    this.speaking = false;
  }
}
