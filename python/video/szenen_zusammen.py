"""Setzt die Clips eines Szenen-Projekts zu einem Video zusammen (Bauplan Szenen, S2). Nur PyAV, kein ffmpeg nötig.

- Ab der zweiten Szene fällt das erste Bild weg: Es ist das letzte Bild der Szene davor (Startbild).
- Der Ton wird an jedem Übergang kurz aus- und eingeblendet (je 0.1 s), damit es nicht knackt.
- Ausgabe: H.264 und AAC. Meldet am Schluss eine JSON-Zeile {"ok": true, "seconds": …, "frames": …}.

Aufruf:
  engines\\comfyui\\.venv\\Scripts\\python.exe -s python\\video\\szenen_zusammen.py --out ziel.mp4 szene-01.mp4 szene-02.mp4 …
"""

import argparse
import json
import sys
from fractions import Fraction

import av
import numpy as np

FADE_SECONDS = 0.1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", required=True)
    parser.add_argument("clips", nargs="+")
    args = parser.parse_args()

    first = av.open(args.clips[0])
    vin = first.streams.video[0]
    fps = Fraction(vin.average_rate or 25)
    width, height = vin.codec_context.width, vin.codec_context.height
    has_audio = bool(first.streams.audio)
    rate = first.streams.audio[0].codec_context.sample_rate if has_audio else 48000
    first.close()

    out = av.open(args.out, "w")
    vout = out.add_stream("h264", rate=fps)
    vout.width, vout.height, vout.pix_fmt = width, height, "yuv420p"
    vout.options = {"crf": "18", "preset": "medium"}
    aout = out.add_stream("aac", rate=rate, layout="stereo") if has_audio else None

    frames = 0
    samples_total = 0
    for index, path in enumerate(args.clips):
        clip = av.open(path)
        skip = 1 if index > 0 else 0
        for n, frame in enumerate(clip.decode(video=0)):
            if n < skip:
                continue
            image = frame.reformat(width=width, height=height, format="yuv420p")
            image.pts = frames
            image.time_base = Fraction(1, 1) / fps
            for packet in vout.encode(image):
                out.mux(packet)
            frames += 1
        clip.close()

        if aout is not None:
            clip = av.open(path)
            if clip.streams.audio:
                chunks = [f.to_ndarray().astype(np.float32) for f in clip.decode(audio=0)]
                layout_channels = len(clip.streams.audio[0].codec_context.layout.channels)
                audio = np.concatenate(chunks, axis=1) if chunks else np.zeros((layout_channels, 0), np.float32)
                if audio.shape[0] == 1:
                    audio = np.repeat(audio, 2, axis=0)
                audio = audio[:2]
                drop = int(rate / float(fps)) * skip  # Ton des weggelassenen ersten Bilds
                audio = audio[:, drop:]
                fade = min(int(rate * FADE_SECONDS), audio.shape[1] // 2)
                if fade > 0:
                    ramp = np.linspace(0, 1, fade, dtype=np.float32)
                    if index > 0:
                        audio[:, :fade] *= ramp
                    if index < len(args.clips) - 1:
                        audio[:, -fade:] *= ramp[::-1]
                for start in range(0, audio.shape[1], 1024):
                    block = np.ascontiguousarray(audio[:, start:start + 1024])
                    aframe = av.AudioFrame.from_ndarray(block, format="fltp", layout="stereo")
                    aframe.sample_rate = rate
                    aframe.pts = samples_total
                    samples_total += block.shape[1]
                    for packet in aout.encode(aframe):
                        out.mux(packet)
            clip.close()

    for packet in vout.encode():
        out.mux(packet)
    if aout is not None:
        for packet in aout.encode():
            out.mux(packet)
    out.close()
    print(json.dumps({"ok": True, "frames": frames, "seconds": round(frames / float(fps), 2)}), flush=True)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:  # noqa: BLE001
        print(json.dumps({"ok": False, "message": str(error)}), flush=True)
        sys.exit(1)
