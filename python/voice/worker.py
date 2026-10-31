"""Sprach-Worker von KI-Nexus: Mikrofon, Spracherkennung (faster-whisper) und Sprachausgabe (Piper).

Die App startet dieses Skript als Hilfsprozess. Sie reden über JSON Lines miteinander:
eine JSON-Nachricht pro Zeile, Befehle auf stdin, Ereignisse auf stdout. Meldungen fürs Log gehen auf stderr.
Das Protokoll steht in docs/05-Sprache.md.

Aufruf:
    python worker.py                      normaler Betrieb (wartet auf Befehle)
    python worker.py --list-microphones   gibt die Mikrofone als eine JSON-Zeile aus und endet
"""
from __future__ import annotations

import argparse
import base64
import collections
import json
import os
import queue
import sys
import threading
import time
import traceback
import wave

import numpy as np

SAMPLE_RATE = 16000          # Whisper und Silero-VAD arbeiten mit 16 kHz
FRAME_SAMPLES = 512          # 32 ms, die Grösse, die Silero-VAD erwartet
FRAME_MS = FRAME_SAMPLES * 1000 // SAMPLE_RATE
CONTEXT_SAMPLES = 64         # Silero-VAD v6 will die letzten 64 Samples des vorigen Stücks dazu
PRE_ROLL_FRAMES = 10         # ca. 0.3 s vor dem ersten Wort mitnehmen, sonst fehlt der Anfang
SPEECH_THRESHOLD = 0.5       # ab dieser Wahrscheinlichkeit gilt ein Stück als Sprache
SILENCE_THRESHOLD = 0.35     # darunter gilt es als Stille
SPEECH_START_FRAMES = 3      # so viele Sprach-Stücke hintereinander, bis "Sprechen beginnt" gilt (ca. 100 ms)
MIN_SPEECH_MS = 300          # Kürzeres (Husten, Klick) wird nicht erkannt
MAX_UTTERANCE_S = 30         # Whisper verarbeitet höchstens 30 Sekunden am Stück
LEVEL_INTERVAL_S = 0.08      # so oft geht die Lautstärke an die App (für den Orb)
TTS_THREADS = 4              # Piper braucht wenig. Mehr Threads würden der KI (llama.cpp) die CPU wegnehmen.

# Whisper-Stücke, denen es selbst nicht traut, fallen weg (sonst "erfindet" es Text aus Rauschen oder fernen Stimmen).
MIN_AVG_LOGPROB = -1.0
MAX_NO_SPEECH_PROB = 0.5
MAX_COMPRESSION_RATIO = 2.4

# Leiser als das ist praktisch Stille (z.B. Mikrofon stumm). Normale Sprache liegt weit darüber (Spitze meist über 0.1).
MIN_RMS = 0.001
MIN_PEAK = 0.01

# Zur Fehlersuche: Ist KINEXUS_VOICE_DEBUG ein Ordner, landet jede Äusserung dort als WAV-Datei.
DEBUG_DIR = os.environ.get("KINEXUS_VOICE_DEBUG") or None


def log(message: str) -> None:
    print(message, file=sys.stderr, flush=True)


class Channel:
    """Schreibt Ereignisse als JSON Lines. Nur diese Klasse darf auf das echte stdout schreiben."""

    def __init__(self) -> None:
        # Bibliotheken schreiben manchmal selbst auf stdout (auch aus C-Code). Das würde das Protokoll stören.
        # Darum: das echte stdout merken und Datei-Nummer 1 auf stderr umleiten.
        protocol_fd = os.dup(1)
        os.dup2(2, 1)
        sys.stdout = sys.stderr
        self._out = os.fdopen(protocol_fd, "w", encoding="utf-8", newline="\n", buffering=1)
        self._lock = threading.Lock()

    def send(self, kind: str, **fields) -> None:
        line = json.dumps({"type": kind, **fields}, ensure_ascii=False)
        with self._lock:
            self._out.write(line + "\n")
            self._out.flush()


# ---------------------------------------------------------------- Mikrofon

def _wasapi_index():
    import sounddevice as sd
    return next((i for i, api in enumerate(sd.query_hostapis()) if "WASAPI" in api["name"]), None)


def list_microphones() -> tuple[list[str], str | None]:
    """Mikrofone mit vollem Namen (WASAPI). Dazu der Name des Windows-Standard-Mikrofons."""
    import sounddevice as sd
    wasapi = _wasapi_index()
    if wasapi is None:
        return [], None
    names = [d["name"] for d in sd.query_devices() if d["hostapi"] == wasapi and d["max_input_channels"] > 0]
    default_index = sd.query_hostapis(wasapi)["default_input_device"]
    default = sd.query_devices(default_index)["name"] if default_index >= 0 else None
    return names, default


class Microphone:
    """Liest das Mikrofon mit 16 kHz mono. Windows rechnet die Abtastrate um (WASAPI auto_convert)."""

    def __init__(self, name: str | None, on_frame) -> None:
        self._name = name
        self._on_frame = on_frame
        self._stream = None
        self.device_name = None

    @property
    def is_open(self) -> bool:
        return self._stream is not None

    def open(self) -> None:
        import sounddevice as sd
        if self._stream is not None:
            return
        wasapi = _wasapi_index()
        index = None
        if wasapi is not None:
            candidates = [i for i, d in enumerate(sd.query_devices())
                          if d["hostapi"] == wasapi and d["max_input_channels"] > 0]
            if self._name:
                index = next((i for i in candidates if sd.query_devices(i)["name"] == self._name), None)
                if index is None:
                    log(f"Mikrofon '{self._name}' nicht gefunden, nehme das Standard-Mikrofon.")
            if index is None:
                index = sd.query_hostapis(wasapi)["default_input_device"]

        def callback(indata, frames, time_info, status):  # läuft im Audio-Thread von PortAudio
            if status:
                log(f"Mikrofon: {status}")
            self._on_frame(indata[:, 0].copy())

        settings = sd.WasapiSettings(auto_convert=True) if wasapi is not None else None
        self._stream = sd.InputStream(device=index, samplerate=SAMPLE_RATE, channels=1, dtype="float32",
                                      blocksize=FRAME_SAMPLES, callback=callback, extra_settings=settings)
        self._stream.start()
        self.device_name = sd.query_devices(self._stream.device)["name"]

    def close(self) -> None:
        if self._stream is not None:
            self._stream.stop()
            self._stream.close()
            self._stream = None


# ---------------------------------------------------------------- Sprechpausen-Erkennung (VAD)

class StreamingVad:
    """Silero-VAD v6 (kommt mit faster-whisper) Stück für Stück: Wie wahrscheinlich ist das Sprache?"""

    def __init__(self) -> None:
        import faster_whisper
        import onnxruntime
        path = os.path.join(os.path.dirname(faster_whisper.__file__), "assets", "silero_vad_v6.onnx")
        options = onnxruntime.SessionOptions()
        options.inter_op_num_threads = 1
        options.intra_op_num_threads = 1
        options.log_severity_level = 4
        self._session = onnxruntime.InferenceSession(path, providers=["CPUExecutionProvider"], sess_options=options)
        self.reset()

    def reset(self) -> None:
        self._h = np.zeros((1, 1, 128), dtype=np.float32)
        self._c = np.zeros((1, 1, 128), dtype=np.float32)
        self._context = np.zeros(CONTEXT_SAMPLES, dtype=np.float32)

    def __call__(self, frame: np.ndarray) -> float:
        data = np.concatenate([self._context, frame])[np.newaxis, :]
        probability, self._h, self._c = self._session.run(None, {"input": data, "h": self._h, "c": self._c})
        self._context = frame[-CONTEXT_SAMPLES:]
        return float(np.ravel(probability)[0])


# ---------------------------------------------------------------- Spracherkennung

class Recognizer:
    def __init__(self, model_path: str, device: str, threads: int) -> None:
        from faster_whisper import WhisperModel
        cuda = device == "gpu"
        self.model = WhisperModel(model_path, device="cuda" if cuda else "cpu",
                                  compute_type="int8_float16" if cuda else "int8", cpu_threads=threads)
        self.device = "gpu" if cuda else "cpu"
        # Aufwärmen: Der erste Durchlauf ist immer langsamer.
        self.transcribe(np.zeros(SAMPLE_RATE, dtype=np.float32))

    def transcribe(self, audio: np.ndarray) -> str:
        segments, _ = self.model.transcribe(audio, language="de", beam_size=1, condition_on_previous_text=False,
                                            without_timestamps=True, vad_filter=False)
        kept = []
        for segment in segments:
            trusted = (segment.avg_logprob >= MIN_AVG_LOGPROB and segment.no_speech_prob <= MAX_NO_SPEECH_PROB
                       and segment.compression_ratio <= MAX_COMPRESSION_RATIO)
            if trusted:
                kept.append(segment.text.strip())
            else:
                log(f"Verworfen (unsicher: logprob {segment.avg_logprob:.2f}, keine Sprache {segment.no_speech_prob:.2f}): {segment.text.strip()}")
        return " ".join(kept).strip()


def resample(audio: np.ndarray, rate: int) -> np.ndarray:
    if rate == SAMPLE_RATE:
        return audio
    positions = np.arange(0, len(audio), rate / SAMPLE_RATE)
    return np.interp(positions, np.arange(len(audio)), audio).astype(np.float32)


def save_debug_wav(audio: np.ndarray) -> None:
    if not DEBUG_DIR:
        return
    os.makedirs(DEBUG_DIR, exist_ok=True)
    path = os.path.join(DEBUG_DIR, time.strftime("aeusserung-%Y%m%d-%H%M%S.wav"))
    with wave.open(path, "wb") as file:
        file.setnchannels(1)
        file.setsampwidth(2)
        file.setframerate(SAMPLE_RATE)
        file.writeframes((np.clip(audio, -1, 1) * 32767).astype(np.int16).tobytes())
    log(f"Gespeichert: {path}")


def read_wav(path: str) -> np.ndarray:
    with wave.open(path, "rb") as file:
        if file.getsampwidth() != 2:
            raise ValueError("Nur WAV mit 16 Bit wird unterstützt.")
        data = np.frombuffer(file.readframes(file.getnframes()), dtype=np.int16).astype(np.float32) / 32768
        channels = file.getnchannels()
        if channels > 1:
            data = data.reshape(-1, channels).mean(axis=1)
        return resample(data, file.getframerate())


# ---------------------------------------------------------------- Zuhören (Push-to-Talk und Freihändig)

class Listener:
    """Ein eigener Thread verarbeitet Mikrofon-Stücke und Befehle der Reihe nach. So gibt es keine Wettläufe."""

    def __init__(self, channel: Channel) -> None:
        self._channel = channel
        self._work: queue.Queue = queue.Queue()
        self.recognizer: Recognizer | None = None
        self.vad: StreamingVad | None = None
        self.microphone_name: str | None = None
        self.silence_ms = 800
        self._mic: Microphone | None = None
        self._mode: str | None = None
        self._paused = False
        self._recording: list[np.ndarray] = []
        self._pre_roll: collections.deque = collections.deque(maxlen=PRE_ROLL_FRAMES)
        self._in_speech = False
        self._speech_frames = 0
        self._silence_ms = 0
        self._last_level = 0.0
        threading.Thread(target=self._run, name="listener", daemon=True).start()

    def post(self, *item) -> None:
        self._work.put(item)

    def _on_frame(self, frame: np.ndarray) -> None:
        self._work.put(("audio", frame))

    def _run(self) -> None:
        while True:
            item = self._work.get()
            try:
                handler = getattr(self, "_do_" + item[0])
                handler(*item[1:])
            except Exception as error:  # noqa: BLE001 - jeder Fehler soll in der App ankommen
                log(traceback.format_exc())
                self._channel.send("error", message=f"Spracherkennung: {error}", fatal=False)

    # -- Befehle

    def _do_start(self, mode: str) -> None:
        if self.recognizer is None:
            raise RuntimeError("Noch nicht eingerichtet (configure fehlt).")
        if self._mic is None or not self._mic.is_open:
            self._mic = Microphone(self.microphone_name, self._on_frame)
            self._mic.open()
        self._mode = mode
        self._paused = False
        self._reset_utterance()
        self.vad.reset()
        self._channel.send("listening", mode=mode, microphone=self._mic.device_name)

    def _do_stop(self, transcribe: bool) -> None:
        mode = self._mode
        self._mode = None
        self._close_mic()
        if mode == "pushToTalk" and transcribe:
            audio = np.concatenate(self._recording) if self._recording else np.zeros(0, dtype=np.float32)
            self._reset_utterance()
            self._recognize(audio)
        else:
            self._reset_utterance()
            self._channel.send("stopped")

    def _do_pause(self) -> None:
        self._paused = True
        self._reset_utterance()

    def _do_resume(self) -> None:
        self._paused = False
        self._reset_utterance()
        self.vad.reset()
        # Was während der Pause aufgenommen wurde (z.B. die eigene Stimme der KI), wird verworfen.
        self._drop_queued_audio()
        if self._mode is not None:
            self._channel.send("listening", mode=self._mode, microphone=self._mic.device_name if self._mic else None)

    def _do_file(self, path: str) -> None:
        self._recognize(read_wav(path))

    def _do_audio(self, frame: np.ndarray) -> None:
        if self._mode is None:
            return
        self._send_level(frame)
        if self._paused:
            return
        if self._mode == "pushToTalk":
            self._recording.append(frame)
            if len(self._recording) * FRAME_MS >= MAX_UTTERANCE_S * 1000:
                self._channel.send("error", message="Höchstens 30 Sekunden am Stück. Ich nehme, was bis jetzt da ist.", fatal=False)
                self._do_stop(True)
        else:
            self._hands_free(frame)

    # -- Freihändig: Anfang und Ende der Sprache selbst erkennen

    def _hands_free(self, frame: np.ndarray) -> None:
        probability = self.vad(frame)
        if not self._in_speech:
            self._pre_roll.append(frame)
            self._speech_frames = self._speech_frames + 1 if probability >= SPEECH_THRESHOLD else 0
            if self._speech_frames >= SPEECH_START_FRAMES:
                self._in_speech = True
                self._recording = list(self._pre_roll)
                self._silence_ms = 0
                self._channel.send("speech_start")
            return

        self._recording.append(frame)
        self._silence_ms = self._silence_ms + FRAME_MS if probability < SILENCE_THRESHOLD else 0
        too_long = len(self._recording) * FRAME_MS >= MAX_UTTERANCE_S * 1000
        if self._silence_ms < self.silence_ms and not too_long:
            return

        speech_ms = len(self._recording) * FRAME_MS - self._silence_ms - PRE_ROLL_FRAMES * FRAME_MS
        audio = np.concatenate(self._recording)
        self._reset_utterance()
        self._channel.send("speech_end")
        if speech_ms < MIN_SPEECH_MS:
            self.vad.reset()
            self._channel.send("listening", mode=self._mode, microphone=self._mic.device_name if self._mic else None)
            return
        # Bis die App "resume" schickt, hört der Worker nicht zu. So nimmt er die Antwort der KI nicht auf.
        self._paused = True
        self._recognize(audio)

    # -- Hilfen

    def _recognize(self, audio: np.ndarray) -> None:
        audio_ms = int(len(audio) * 1000 / SAMPLE_RATE)
        rms = float(np.sqrt(np.mean(audio * audio))) if len(audio) else 0.0
        peak = float(np.max(np.abs(audio))) if len(audio) else 0.0
        if len(audio) > 0:
            log(f"Äusserung: {audio_ms} ms, Pegel RMS {rms:.4f}, Spitze {peak:.3f}")
            save_debug_wav(audio)
        if audio_ms < MIN_SPEECH_MS:
            self._channel.send("transcript", text="", audioMs=audio_ms, tookMs=0, reason="zu kurz")
            return
        # Bei Stille "hört" Whisper oft Sätze wie "Vielen Dank." Darum geht praktisch Stilles gar nicht erst an Whisper.
        if rms < MIN_RMS or peak < MIN_PEAK:
            self._channel.send("transcript", text="", audioMs=audio_ms, tookMs=0, reason="zu leise")
            return
        self._channel.send("transcribing", audioMs=audio_ms)
        started = time.perf_counter()
        text = self.recognizer.transcribe(audio)
        took = int((time.perf_counter() - started) * 1000)
        log(f"Erkannt in {took} ms ({audio_ms} ms Audio): {text}")
        self._channel.send("transcript", text=text, audioMs=audio_ms, tookMs=took)

    def _send_level(self, frame: np.ndarray) -> None:
        now = time.monotonic()
        if now - self._last_level >= LEVEL_INTERVAL_S:
            self._last_level = now
            rms = float(np.sqrt(np.mean(frame * frame)))
            self._channel.send("level", value=round(min(1.0, rms * 8), 3))

    def _reset_utterance(self) -> None:
        self._recording = []
        self._pre_roll.clear()
        self._in_speech = False
        self._speech_frames = 0
        self._silence_ms = 0

    def _drop_queued_audio(self) -> None:
        kept = []
        while True:
            try:
                item = self._work.get_nowait()
            except queue.Empty:
                break
            if item[0] != "audio":
                kept.append(item)
        for item in kept:
            self._work.put(item)

    def _close_mic(self) -> None:
        if self._mic is not None:
            self._mic.close()
            self._mic = None


# ---------------------------------------------------------------- Sprachausgabe

class Speaker:
    """Piper in einem eigenen Thread. Jeder Satz geht als PCM (16 Bit, mono) an die App, die ihn abspielt."""

    def __init__(self, channel: Channel) -> None:
        self._channel = channel
        self._work: queue.Queue = queue.Queue()
        self._generation = 0
        self.voice = None
        threading.Thread(target=self._run, name="speaker", daemon=True).start()

    @property
    def sample_rate(self) -> int:
        return self.voice.config.sample_rate if self.voice else 22050

    def load(self, voice_path: str) -> None:
        import onnxruntime
        from piper import PiperVoice
        voice = PiperVoice.load(voice_path)
        # Piper nimmt sonst alle Kerne. Mit wenigen Threads bleibt die CPU frei für die KI, die gleichzeitig schreibt.
        options = onnxruntime.SessionOptions()
        options.intra_op_num_threads = TTS_THREADS
        options.inter_op_num_threads = 1
        voice.session = onnxruntime.InferenceSession(voice_path, sess_options=options, providers=["CPUExecutionProvider"])
        self.voice = voice

    def say(self, speech_id: int, text: str) -> None:
        self._work.put((self._generation, speech_id, text))

    def stop(self) -> None:
        """Alles Ausstehende verwerfen. Ein Satz, der gerade entsteht, bricht nach dem nächsten Stück ab."""
        self._generation += 1
        while True:
            try:
                self._work.get_nowait()
            except queue.Empty:
                break

    def _run(self) -> None:
        while True:
            generation, speech_id, text = self._work.get()
            if generation != self._generation:
                continue
            try:
                cancelled = False
                for chunk in self.voice.synthesize(text):
                    if generation != self._generation:
                        cancelled = True
                        break
                    self._channel.send("audio", id=speech_id, pcm=base64.b64encode(chunk.audio_int16_bytes).decode("ascii"))
                self._channel.send("speak_done", id=speech_id, cancelled=cancelled)
            except Exception as error:  # noqa: BLE001
                log(traceback.format_exc())
                self._channel.send("error", message=f"Sprachausgabe: {error}", fatal=False)
                self._channel.send("speak_done", id=speech_id, cancelled=True)


# ---------------------------------------------------------------- Hauptschleife

def configure(message: dict, listener: Listener, speaker: Speaker, channel: Channel) -> None:
    started = time.perf_counter()
    listener.microphone_name = message.get("microphone") or None
    listener.silence_ms = int(message.get("silenceMs", 800))
    threads = int(message.get("threads", 0)) or min(12, os.cpu_count() or 4)
    device = message.get("device", "cpu")
    if listener.vad is None:
        listener.vad = StreamingVad()
    try:
        listener.recognizer = Recognizer(message["whisperPath"], device, threads)
    except Exception as error:  # noqa: BLE001
        if device != "gpu":
            raise
        log(traceback.format_exc())
        channel.send("error", message=f"Spracherkennung auf der GPU geht nicht ({error}). Ich nehme die CPU.", fatal=False)
        listener.recognizer = Recognizer(message["whisperPath"], "cpu", threads)
    speaker.load(message["voicePath"])
    channel.send("ready", device=listener.recognizer.device, sampleRate=speaker.sample_rate,
                 loadMs=int((time.perf_counter() - started) * 1000))


def main() -> int:
    parser = argparse.ArgumentParser(description="Sprach-Worker von KI-Nexus")
    parser.add_argument("--list-microphones", action="store_true", help="Mikrofone ausgeben und beenden")
    args = parser.parse_args()

    # Log-Meldungen (stderr) als UTF-8, sonst kommen Umlaute in der App falsch an.
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    channel = Channel()
    if args.list_microphones:
        names, default = list_microphones()
        channel.send("microphones", items=names, default=default)
        return 0

    sys.stdin.reconfigure(encoding="utf-8")
    listener = Listener(channel)
    speaker = Speaker(channel)
    channel.send("hello", pid=os.getpid())

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        message: dict = {}
        try:
            message = json.loads(line)
            kind = message.get("type")
            if kind == "configure":
                configure(message, listener, speaker, channel)
            elif kind == "listen":
                listener.post("start", message.get("mode", "pushToTalk"))
            elif kind == "stop_listening":
                listener.post("stop", bool(message.get("transcribe", True)))
            elif kind == "pause":
                listener.post("pause")
            elif kind == "resume":
                listener.post("resume")
            elif kind == "speak":
                speaker.say(int(message["id"]), str(message["text"]))
            elif kind == "stop_speaking":
                speaker.stop()
            elif kind == "transcribe_file":
                listener.post("file", str(message["path"]))
            elif kind == "list_microphones":
                names, default = list_microphones()
                channel.send("microphones", items=names, default=default)
            elif kind == "shutdown":
                break
            else:
                channel.send("error", message=f"Unbekannter Befehl: {kind}", fatal=False)
        except Exception as error:  # noqa: BLE001
            log(traceback.format_exc())
            channel.send("error", message=str(error), fatal=message.get("type") == "configure")
    return 0


if __name__ == "__main__":
    sys.exit(main())
