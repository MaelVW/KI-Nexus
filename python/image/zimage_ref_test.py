"""Kann Z-Image Turbo Referenzbilder? Test mit dem Hund aus B0.

1. ReferenceLatent wie bei FLUX.2 klein. ComfyUI kennt "ref_latents" bei Lumina 2, gedacht für die Edit-Variante "Z-Image Omni".
2. Bild zu Bild: Das Referenzbild ist der Ausgangspunkt, das Modell rechnet nur einen Teil neu (denoise 0.6 und 0.8).

Aufruf (aus dem Projektordner, ComfyUI darf nicht laufen):
  engines\\comfyui\\.venv\\Scripts\\python.exe -s python\\image\\zimage_ref_test.py
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
REF = ROOT / "data" / "images" / "roh" / "b0" / "flux2-klein-4b-hund_00001_.png"
PROMPT = "The dog from the reference image wearing a red knitted scarf, sitting in fresh snow, winter forest, photo."


def reference_latent(prefix: str) -> dict:
    wf = b.zimage(PROMPT, 2000, prefix)
    wf["30"] = {"class_type": "LoadImage", "inputs": {"image": "ref_hund.png"}}
    wf["31"] = {"class_type": "ImageScaleToMaxDimension", "inputs": {"image": ["30", 0], "upscale_method": "lanczos", "largest_size": 1024}}
    wf["32"] = {"class_type": "VAEEncode", "inputs": {"pixels": ["31", 0], "vae": ["3", 0]}}
    wf["33"] = {"class_type": "ReferenceLatent", "inputs": {"conditioning": ["4", 0], "latent": ["32", 0]}}
    wf["8"]["inputs"]["positive"] = ["33", 0]
    return wf


def image_to_image(prefix: str, denoise: float) -> dict:
    wf = b.zimage(PROMPT, 2000, prefix)
    wf["30"] = {"class_type": "LoadImage", "inputs": {"image": "ref_hund.png"}}
    wf["31"] = {"class_type": "ImageScaleToMaxDimension", "inputs": {"image": ["30", 0], "upscale_method": "lanczos", "largest_size": 1024}}
    wf["32"] = {"class_type": "VAEEncode", "inputs": {"pixels": ["31", 0], "vae": ["3", 0]}}
    wf["8"]["inputs"]["latent_image"] = ["32", 0]
    wf["8"]["inputs"]["denoise"] = denoise
    return wf


async def main() -> None:
    INPUT.mkdir(parents=True, exist_ok=True)
    shutil.copy(REF, INPUT / "ref_hund.png")
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
            for label, wf in [("z-ref-latent", reference_latent("b0/z-ref-latent")),
                              ("z-img2img-0.6", image_to_image("b0/z-img2img-06", 0.6)),
                              ("z-img2img-0.8", image_to_image("b0/z-img2img-08", 0.8))]:
                print(json.dumps(await b.run_one(session, sampler, wf, label), ensure_ascii=False), flush=True)
    finally:
        sampler.stop.set()
        proc.terminate()
        proc.wait(20)


if __name__ == "__main__":
    asyncio.run(main())
