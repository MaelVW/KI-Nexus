"""Bild zu Bild mit FLUX.2 klein: Das Bild ist der Ausgangspunkt, nur ein Teil der Schritte wird neu gerechnet.

Vorlage → ImageScale (Format des Auftrags, Mitte) → VAEEncode → RepeatLatentBatch (Anzahl) → Latent.
Die Rausch-Stufen schneidet SplitSigmasDenoise zu: Stärke 0.8 = die letzten 80 % der Stufen.

Aufruf (aus dem Projektordner, ComfyUI darf nicht laufen):
  engines\\comfyui\\.venv\\Scripts\\python.exe -s python\\image\\img2img_test.py
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
PROMPT = "A golden retriever wearing a red knitted scarf, sitting in fresh snow, winter forest, photo."


def flux_img2img(prefix: str, strength: float, count: int) -> dict:
    wf = b.klein(PROMPT, 2000, prefix)
    wf["30"] = {"class_type": "LoadImage", "inputs": {"image": "ref_hund.png"}}
    wf["31"] = {"class_type": "ImageScale", "inputs": {"image": ["30", 0], "upscale_method": "lanczos", "width": 1024, "height": 1024, "crop": "center"}}
    wf["32"] = {"class_type": "VAEEncode", "inputs": {"pixels": ["31", 0], "vae": ["3", 0]}}
    wf["33"] = {"class_type": "RepeatLatentBatch", "inputs": {"samples": ["32", 0], "amount": count}}
    wf["34"] = {"class_type": "SplitSigmasDenoise", "inputs": {"sigmas": ["7", 0], "denoise": strength}}
    wf["11"]["inputs"]["latent_image"] = ["33", 0]
    wf["11"]["inputs"]["sigmas"] = ["34", 1]
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
            for label, strength, count in [("f-img2img-06", 0.6, 1), ("f-img2img-08", 0.8, 2)]:
                print(json.dumps(await b.run_one(session, sampler, flux_img2img(f"b0/{label}", strength, count), label), ensure_ascii=False), flush=True)
    finally:
        sampler.stop.set()
        proc.terminate()
        proc.wait(20)


if __name__ == "__main__":
    asyncio.run(main())
