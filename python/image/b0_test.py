"""Machbarkeitstest B0 (Bauplan Bild): zwei Bildmodelle über ComfyUI, mit Messung.

Startet ComfyUI (wie python/video/v0_test.py), erzeugt pro Modell und Prompt ein Bild und misst Zeit, VRAM und RAM.
Pro Modell zwei Bilder: Das erste lädt die Modelle (kalt), das zweite zeigt das Tempo, wenn sie schon geladen sind.
Ergebnisse: data/images/b0/ und data/images/b0-messungen.jsonl.

Aufruf (aus dem Projektordner):
  engines\\comfyui\\.venv\\Scripts\\python.exe -s python\\image\\b0_test.py [flux2-klein-4b] [z-image-turbo]
"""

import asyncio
import json
import sys
import time
import uuid
from pathlib import Path

import aiohttp

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "python" / "video"))
import v0_test as v  # noqa: E402  (Start von ComfyUI und Messung wiederverwenden)

OUT = ROOT / "data" / "images"
PATHS = ROOT / "data" / "comfyui" / "bild-pfade.yaml"

PROMPTS = [
    ("hund", "A golden retriever sits in a sunny meadow and looks into the camera, soft afternoon light, shallow depth of field, photo."),
    ("schild", "A wooden sign in front of a mountain hut that says \"KI-Nexus\" in white painted letters, snowy peaks behind, morning light."),
]


def klein(prompt: str, seed: int, prefix: str) -> dict:
    """FLUX.2 klein 4B (distilled): 4 Schritte, CFG 1, Flux2Scheduler."""
    return {
        "1": {"class_type": "UnetLoaderGGUF", "inputs": {"unet_name": "flux-2-klein-4b-Q8_0.gguf"}},
        "2": {"class_type": "CLIPLoader", "inputs": {"clip_name": "qwen_3_4b.safetensors", "type": "flux2", "device": "default"}},
        "3": {"class_type": "VAELoader", "inputs": {"vae_name": "flux2-vae.safetensors"}},
        "4": {"class_type": "CLIPTextEncode", "inputs": {"text": prompt, "clip": ["2", 0]}},
        "5": {"class_type": "ConditioningZeroOut", "inputs": {"conditioning": ["4", 0]}},
        "6": {"class_type": "EmptyFlux2LatentImage", "inputs": {"width": 1024, "height": 1024, "batch_size": 1}},
        "7": {"class_type": "Flux2Scheduler", "inputs": {"steps": 4, "width": 1024, "height": 1024}},
        "8": {"class_type": "KSamplerSelect", "inputs": {"sampler_name": "euler"}},
        "9": {"class_type": "RandomNoise", "inputs": {"noise_seed": seed}},
        "10": {"class_type": "CFGGuider", "inputs": {"model": ["1", 0], "positive": ["4", 0], "negative": ["5", 0], "cfg": 1.0}},
        "11": {"class_type": "SamplerCustomAdvanced",
               "inputs": {"noise": ["9", 0], "guider": ["10", 0], "sampler": ["8", 0], "sigmas": ["7", 0], "latent_image": ["6", 0]}},
        "12": {"class_type": "VAEDecode", "inputs": {"samples": ["11", 0], "vae": ["3", 0]}},
        "13": {"class_type": "SaveImage", "inputs": {"images": ["12", 0], "filename_prefix": prefix}},
    }


def zimage(prompt: str, seed: int, prefix: str) -> dict:
    """Z-Image Turbo: 8 Schritte, CFG 1, res_multistep, AuraFlow-Verschiebung 3 (wie im Workflow von ComfyUI)."""
    return {
        "1": {"class_type": "UnetLoaderGGUF", "inputs": {"unet_name": "z-image-turbo-Q8_0.gguf"}},
        "2": {"class_type": "CLIPLoader", "inputs": {"clip_name": "qwen_3_4b.safetensors", "type": "lumina2", "device": "default"}},
        "3": {"class_type": "VAELoader", "inputs": {"vae_name": "ae.safetensors"}},
        "4": {"class_type": "CLIPTextEncode", "inputs": {"text": prompt, "clip": ["2", 0]}},
        "5": {"class_type": "ConditioningZeroOut", "inputs": {"conditioning": ["4", 0]}},
        "6": {"class_type": "EmptySD3LatentImage", "inputs": {"width": 1024, "height": 1024, "batch_size": 1}},
        "7": {"class_type": "ModelSamplingAuraFlow", "inputs": {"model": ["1", 0], "shift": 3.0}},
        "8": {"class_type": "KSampler", "inputs": {"model": ["7", 0], "seed": seed, "steps": 8, "cfg": 1.0, "sampler_name": "res_multistep",
                                                  "scheduler": "simple", "positive": ["4", 0], "negative": ["5", 0], "latent_image": ["6", 0],
                                                  "denoise": 1.0}},
        "9": {"class_type": "VAEDecode", "inputs": {"samples": ["8", 0], "vae": ["3", 0]}},
        "10": {"class_type": "SaveImage", "inputs": {"images": ["9", 0], "filename_prefix": prefix}},
    }


async def run_one(session: aiohttp.ClientSession, sampler: v.Sampler, workflow: dict, label: str) -> dict:
    client_id = str(uuid.uuid4())
    sampler.peak.clear()
    sampler.stage = "_all"
    async with session.ws_connect(f"{v.BASE}/ws?clientId={client_id}", heartbeat=30) as ws:
        async with session.post(f"{v.BASE}/prompt", json={"prompt": workflow, "client_id": client_id}) as r:
            body = await r.json()
            if r.status != 200:
                return {"label": label, "error": json.dumps(body)[:1500]}
        prompt_id = body["prompt_id"]
        start = time.monotonic()
        files, error = [], None
        async for msg in ws:
            if msg.type != aiohttp.WSMsgType.TEXT:
                continue
            m = json.loads(msg.data)
            data = m.get("data", {})
            if data.get("prompt_id") not in (None, prompt_id):
                continue
            if m.get("type") == "executed":
                files += [f"{x.get('subfolder', '')}/{x['filename']}" for x in (data.get("output") or {}).get("images", [])]
            elif m.get("type") == "execution_error":
                error = data.get("exception_message")
                break
            elif m.get("type") == "execution_success":
                break
    return {"label": label, "seconds": round(time.monotonic() - start, 1), "files": files, "error": error, "peak": dict(sampler.peak.get("_all", {}))}


async def main() -> None:
    PATHS.parent.mkdir(parents=True, exist_ok=True)
    base = (ROOT / "models" / "image").as_posix()
    PATHS.write_text(f'kinexus_bild:\n  base_path: "{base}"\n  diffusion_models: diffusion_models\n  text_encoders: text_encoders\n  vae: vae\n',
                     encoding="utf-8")
    v.VIDEOS = OUT  # Ausgabe nach data/images
    proc = v.start_comfy(["--extra-model-paths-config", str(PATHS)])
    sampler = v.Sampler(proc.pid)
    sampler.thread.start()
    results = []
    try:
        async with aiohttp.ClientSession() as session:
            await v.wait_ready(session, proc)
            for name, build in [("flux2-klein-4b", klein), ("z-image-turbo", zimage)]:
                if len(sys.argv) > 1 and name not in sys.argv[1:]:
                    continue
                for i, (tag, prompt) in enumerate(PROMPTS):
                    label = f"{name}-{tag}"
                    result = await run_one(session, sampler, build(prompt, 1000 + i, f"b0/{label}"), label)
                    result["model"] = name
                    result["warm"] = i > 0  # Ohne --cache-none bleiben die Modelle nach dem ersten Bild geladen.
                    print(json.dumps(result, ensure_ascii=False), flush=True)
                    results.append(result)
                async with session.post(f"{v.BASE}/free", json={"unload_models": True, "free_memory": True}):
                    pass
    finally:
        sampler.stop.set()
        proc.terminate()
        proc.wait(20)
    OUT.mkdir(parents=True, exist_ok=True)
    with open(OUT / "b0-messungen.jsonl", "a", encoding="utf-8") as f:
        for r in results:
            f.write(json.dumps({"time": time.strftime("%Y-%m-%d %H:%M"), **r}, ensure_ascii=False) + "\n")


if __name__ == "__main__":
    asyncio.run(main())
