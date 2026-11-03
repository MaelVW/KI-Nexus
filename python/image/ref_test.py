"""Machbarkeitstest Referenzbilder (Bauplan Bild, Nachtrag): FLUX.2 klein mit einem oder zwei Referenzbildern.

Die Referenz wird auf höchstens 1024 Pixel (lange Seite) skaliert, mit der VAE ins Latent gerechnet und per ReferenceLatent an den Prompt gehängt.
Mehrere Referenzen: ReferenceLatent mehrmals hintereinander.

Aufruf (aus dem Projektordner, ComfyUI darf nicht laufen):
  engines\\comfyui\\.venv\\Scripts\\python.exe -s python\\image\\ref_test.py
"""

import asyncio
import json
import shutil
import sys
from pathlib import Path

import aiohttp

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "python" / "video"))
sys.path.insert(0, str(ROOT / "python" / "image"))
import b0_test as b  # noqa: E402
import v0_test as v  # noqa: E402

INPUT = ROOT / "data" / "comfyui" / "input"
REFS = {
    "hund": ROOT / "data" / "images" / "roh" / "b0" / "flux2-klein-4b-hund_00001_.png",
    "huette": ROOT / "data" / "images" / "roh" / "b0" / "flux2-klein-4b-schild_00001_.png",
}


def with_refs(prompt: str, seed: int, prefix: str, refs: list[str]) -> dict:
    wf = b.klein(prompt, seed, prefix)
    positive = ["4", 0]
    for i, name in enumerate(refs):
        n = 30 + 4 * i
        wf[str(n)] = {"class_type": "LoadImage", "inputs": {"image": f"ref_{name}.png"}}
        wf[str(n + 1)] = {"class_type": "ImageScaleToMaxDimension",
                          "inputs": {"image": [str(n), 0], "upscale_method": "lanczos", "largest_size": 1024}}
        wf[str(n + 2)] = {"class_type": "VAEEncode", "inputs": {"pixels": [str(n + 1), 0], "vae": ["3", 0]}}
        wf[str(n + 3)] = {"class_type": "ReferenceLatent", "inputs": {"conditioning": positive, "latent": [str(n + 2), 0]}}
        positive = [str(n + 3), 0]
    wf["10"]["inputs"]["positive"] = positive
    return wf


JOBS = [
    ("ref1-schal", "The dog from the reference image wearing a red knitted scarf, sitting in fresh snow, winter forest, photo.", ["hund"]),
    ("ref2-hund-huette", "The dog from the first image sits in the snow in front of the mountain hut from the second image, photo.", ["hund", "huette"]),
]


async def main() -> None:
    INPUT.mkdir(parents=True, exist_ok=True)
    for name, path in REFS.items():
        shutil.copy(path, INPUT / f"ref_{name}.png")
    base = (ROOT / "models" / "image").as_posix()
    b.PATHS.write_text(f'kinexus_bild:\n  base_path: "{base}"\n  diffusion_models: diffusion_models\n  text_encoders: text_encoders\n  vae: vae\n',
                       encoding="utf-8")
    v.VIDEOS = b.OUT
    proc = v.start_comfy(["--extra-model-paths-config", str(b.PATHS)])
    sampler = v.Sampler(proc.pid)
    sampler.thread.start()
    try:
        async with aiohttp.ClientSession() as session:
            await v.wait_ready(session, proc)
            for label, prompt, refs in JOBS:
                result = await b.run_one(session, sampler, with_refs(prompt, 2000, f"b0/{label}", refs), label)
                print(json.dumps(result, ensure_ascii=False), flush=True)
    finally:
        sampler.stop.set()
        proc.terminate()
        proc.wait(20)


if __name__ == "__main__":
    asyncio.run(main())
