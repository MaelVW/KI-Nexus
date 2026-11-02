"""Machbarkeitstest V0 (Bauplan Video): ein Clip mit LTX-2.3 über ComfyUI, mit Messung.

Startet ComfyUI unsichtbar auf 127.0.0.1, schickt den Workflow, liest den Fortschritt
über den WebSocket und misst dabei VRAM, RAM und Auslagerungsdatei. Am Schluss wird
ComfyUI wieder beendet und die Messung in data/videos/v0-messungen.jsonl angehängt.

Aufruf (aus dem Projektordner):
  engines\\comfyui\\.venv\\Scripts\\python.exe python\\video\\v0_test.py --label klein --width 512 --height 320 --frames 49
"""

import argparse
import asyncio
import json
import subprocess
import sys
import threading
import time
import uuid
from pathlib import Path

import aiohttp
import psutil

ROOT = Path(__file__).resolve().parents[2]
COMFY = ROOT / "engines" / "comfyui"
VIDEOS = ROOT / "data" / "videos"
LOGS = ROOT / "data" / "logs"
WORKFLOW = Path(__file__).resolve().parent / "workflows" / "ltx23-text-zu-video.json"
PORT = 8188
BASE = f"http://127.0.0.1:{PORT}"

DEFAULT_PROMPT = (
    "A golden retriever runs across a sunny meadow toward the camera, ears flapping, "
    "wagging its tail. Tall grass sways in a light breeze, birds chirp and the dog barks "
    "happily once. Handheld camera at knee height, warm afternoon light, shallow depth of field."
)


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser()
    p.add_argument("--label", default="test")
    p.add_argument("--model", default="ltx-2.3-22b-distilled-1.1-Q4_K_M.gguf")
    p.add_argument("--prompt", default=DEFAULT_PROMPT)
    p.add_argument("--width", type=int, default=768)
    p.add_argument("--height", type=int, default=512)
    p.add_argument("--frames", type=int, default=97)
    p.add_argument("--fps", type=float, default=25.0)
    p.add_argument("--seed", type=int, default=42)
    p.add_argument("--extra", nargs="*", default=[], help="Zusätzliche ComfyUI-Flags, z.B. --reserve-vram 1")
    return p.parse_args()


def by_title(workflow: dict, title: str) -> dict:
    """Findet einen Knoten über seinen Titel (KN_...), nicht über die Nummer."""
    for node in workflow.values():
        if node.get("_meta", {}).get("title") == title:
            return node["inputs"]
    raise KeyError(title)


def build_workflow(a: argparse.Namespace) -> dict:
    if a.width % 32 or a.height % 32 or (a.frames - 1) % 8:
        raise SystemExit("Breite/Höhe müssen durch 32 teilbar sein, Bilder = 8*n + 1.")
    wf = json.loads(WORKFLOW.read_text(encoding="utf-8"))
    by_title(wf, "KN_Model")["unet_name"] = a.model
    by_title(wf, "KN_Prompt")["text"] = a.prompt
    by_title(wf, "KN_Conditioning")["frame_rate"] = a.fps
    by_title(wf, "KN_VideoLatent").update(width=a.width, height=a.height, length=a.frames)
    by_title(wf, "KN_AudioLatent").update(frames_number=a.frames, frame_rate=a.fps)
    by_title(wf, "KN_Seed")["noise_seed"] = a.seed
    by_title(wf, "KN_CreateVideo")["fps"] = a.fps
    by_title(wf, "KN_Save")["filename_prefix"] = f"v0/{a.label}"
    return wf


class Sampler:
    """Misst jede halbe Sekunde VRAM, RAM, Auslagerung und den Speicher von ComfyUI."""

    def __init__(self, pid: int):
        self.proc = psutil.Process(pid)
        self.stage = "start"
        self.peak: dict[str, dict[str, float]] = {}
        self.stop = threading.Event()
        self.thread = threading.Thread(target=self.run, daemon=True)

    def vram_mb(self) -> float:
        out = subprocess.run(
            ["nvidia-smi", "--query-gpu=memory.used", "--format=csv,noheader,nounits"],
            capture_output=True, text=True, timeout=5,
        ).stdout
        return float(out.strip().splitlines()[0])

    def run(self) -> None:
        while not self.stop.is_set():
            try:
                # Die python.exe der venv startet unter Windows den echten Python als Kindprozess.
                mems = [p.memory_info() for p in [self.proc, *self.proc.children(recursive=True)]]
                sample = {
                    "vram_gb": self.vram_mb() / 1024,
                    "ram_used_gb": psutil.virtual_memory().used / 2**30,
                    "swap_used_gb": psutil.swap_memory().used / 2**30,
                    "comfy_rss_gb": sum(m.rss for m in mems) / 2**30,
                    "comfy_private_gb": sum(getattr(m, "private", 0) for m in mems) / 2**30,
                }
                for key in ("_all", self.stage):
                    slot = self.peak.setdefault(key, {})
                    for k, v in sample.items():
                        slot[k] = round(max(slot.get(k, 0.0), v), 2)
            except Exception:
                pass
            self.stop.wait(0.5)


def start_comfy(extra: list[str]) -> subprocess.Popen:
    LOGS.mkdir(parents=True, exist_ok=True)
    args = [
        str(COMFY / ".venv" / "Scripts" / "python.exe"), "-s", "main.py",
        "--listen", "127.0.0.1", "--port", str(PORT), "--disable-auto-launch", "--offline",
        "--extra-model-paths-config", str(COMFY / "kinexus-paths.yaml"),
        "--output-directory", str(VIDEOS / "roh"),
        "--input-directory", str(VIDEOS / "comfy-input"),
        "--temp-directory", str(VIDEOS / "comfy-temp"),
        "--user-directory", str(VIDEOS / "comfy-user"),
        *extra,
    ]
    log = open(LOGS / "video-v0.log", "ab")
    return subprocess.Popen(args, cwd=COMFY, stdout=log, stderr=log,
                            creationflags=subprocess.CREATE_NO_WINDOW)


async def wait_ready(session: aiohttp.ClientSession, proc: subprocess.Popen) -> None:
    for _ in range(120):
        if proc.poll() is not None:
            raise SystemExit("ComfyUI hat sich beendet, siehe data/logs/video-v0.log")
        try:
            async with session.get(f"{BASE}/system_stats") as r:
                if r.status == 200:
                    return
        except aiohttp.ClientError:
            pass
        await asyncio.sleep(1)
    raise SystemExit("ComfyUI antwortet nicht.")


async def run(a: argparse.Namespace) -> dict:
    wf = build_workflow(a)
    titles = {k: v.get("_meta", {}).get("title", v["class_type"]) for k, v in wf.items()}
    proc = start_comfy(a.extra)
    sampler = Sampler(proc.pid)
    sampler.thread.start()
    t0 = time.monotonic()
    timeline: list[tuple[str, float]] = []
    steps: list[float] = []
    result: dict = {"label": a.label, "model": a.model, "width": a.width, "height": a.height,
                    "frames": a.frames, "fps": a.fps, "seed": a.seed, "extra": a.extra}
    try:
        async with aiohttp.ClientSession() as session:
            await wait_ready(session, proc)
            result["startup_s"] = round(time.monotonic() - t0, 1)
            client_id = str(uuid.uuid4())
            async with session.ws_connect(f"{BASE}/ws?clientId={client_id}", heartbeat=30) as ws:
                async with session.post(f"{BASE}/prompt", json={"prompt": wf, "client_id": client_id}) as r:
                    body = await r.json()
                    if r.status != 200:
                        raise SystemExit(f"Workflow abgelehnt: {json.dumps(body)[:2000]}")
                prompt_id = body["prompt_id"]
                t_run = time.monotonic()
                async for msg in ws:
                    if msg.type != aiohttp.WSMsgType.TEXT:
                        continue
                    m = json.loads(msg.data)
                    kind, data = m.get("type"), m.get("data", {})
                    if data.get("prompt_id") not in (None, prompt_id):
                        continue
                    now = round(time.monotonic() - t_run, 1)
                    if kind == "executing" and data.get("node"):
                        stage = titles.get(str(data["node"]), str(data["node"]))
                        sampler.stage = stage
                        timeline.append((stage, now))
                        print(f"[{now:7.1f} s] {stage}", flush=True)
                    elif kind == "progress":
                        if sampler.stage == "KN_Denoise":
                            steps.append(now)
                        print(f"[{now:7.1f} s]   {sampler.stage} {data.get('value')}/{data.get('max')}", flush=True)
                    elif kind == "execution_error":
                        result["error"] = {k: data.get(k) for k in ("node_type", "exception_message")}
                        print("FEHLER:", result["error"], flush=True)
                        break
                    elif kind == "execution_success":
                        break
                result["run_s"] = round(time.monotonic() - t_run, 1)
            async with session.get(f"{BASE}/history/{prompt_id}") as r:
                hist = (await r.json()).get(prompt_id, {})
            files = [f"{x.get('subfolder', '')}/{x['filename']}" for out in hist.get("outputs", {}).values()
                     for key in ("images", "videos", "gifs") for x in out.get(key, [])]
            result["files"] = files
    finally:
        sampler.stop.set()
        proc.terminate()
        try:
            proc.wait(20)
        except subprocess.TimeoutExpired:
            proc.kill()
    # Dauer pro Phase aus der Zeitleiste.
    durations = {}
    for (stage, start), (_, end) in zip(timeline, timeline[1:] + [("ende", result.get("run_s", 0))]):
        durations[stage] = round(durations.get(stage, 0) + end - start, 1)
    result["stage_s"] = durations
    result["step_times_s"] = steps
    result["peak"] = sampler.peak
    return result


def main() -> None:
    a = parse_args()
    result = asyncio.run(run(a))
    print(json.dumps(result, indent=2, ensure_ascii=False))
    VIDEOS.mkdir(parents=True, exist_ok=True)
    with open(VIDEOS / "v0-messungen.jsonl", "a", encoding="utf-8") as f:
        f.write(json.dumps({"time": time.strftime("%Y-%m-%d %H:%M"), **result}, ensure_ascii=False) + "\n")
    sys.exit(1 if "error" in result else 0)


if __name__ == "__main__":
    main()
