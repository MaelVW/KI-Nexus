"""Trainings-Worker von KI-Nexus (M8).

Trainiert einen LoRA-Adapter (kleine Zusatz-Gewichte) für ein kleines Modell und wandelt ihn nach GGUF um,
damit llama.cpp ihn mit --lora laden kann.

Aufruf:   python train.py --config run.json      (Training, siehe docs/06-Training.md)
          python train.py --check                (Umgebung prüfen: PyTorch, CUDA, Grafikkarte)

Ausgabe:  eine JSON-Zeile pro Ereignis auf stdout (JSON Lines), Log-Text auf stderr.
Abbruch:  Legt die App die Datei "stopFile" an (siehe run.json), hört der Worker nach dem laufenden Schritt auf.
          Nicht über stdin: Unter Windows blockiert ein Thread, der auf stdin wartet, schon "import torch".
"""

from __future__ import annotations

import argparse
import json
import math
import os
import random
import subprocess
import sys
import threading
import time
import traceback
from pathlib import Path

# Nur so viel aus dem Internet wie nötig: keine Telemetrie von Hugging Face.
os.environ.setdefault("HF_HUB_DISABLE_TELEMETRY", "1")
os.environ.setdefault("TRANSFORMERS_NO_ADVISORY_WARNINGS", "1")

# Dateien, die für Training und Umwandlung gebraucht werden. Grosse Extras (z.B. .bin, .gguf, onnx) nicht.
DOWNLOAD_PATTERNS = ["*.json", "*.safetensors", "*.txt", "*.model", "*.jinja", "*.tiktoken", "tokenizer*"]
LORA_TARGETS = ["q_proj", "k_proj", "v_proj", "o_proj", "gate_proj", "up_proj", "down_proj"]

_stop_file: Path | None = None
_out_lock = threading.Lock()


def send(kind: str, **fields) -> None:
    with _out_lock:
        sys.stdout.write(json.dumps({"type": kind, **fields}, ensure_ascii=False) + "\n")
        sys.stdout.flush()


def log(text: str) -> None:
    sys.stderr.write(text + "\n")
    sys.stderr.flush()


class Cancelled(Exception):
    pass


def stop_requested() -> bool:
    return _stop_file is not None and _stop_file.exists()


def check_stop() -> None:
    if stop_requested():
        raise Cancelled()


# ---------------------------------------------------------------- Umgebung prüfen

def check_environment() -> int:
    info: dict = {"python": sys.version.split()[0]}
    try:
        import torch
        info["torch"] = torch.__version__
        info["cuda"] = torch.cuda.is_available()
        if torch.cuda.is_available():
            props = torch.cuda.get_device_properties(0)
            info["gpu"] = props.name
            info["vramGb"] = round(props.total_memory / 1024 ** 3, 1)
            info["capability"] = f"{props.major}.{props.minor}"
            # Ein kleiner Test auf der Grafikkarte: Läuft der Code für diese Karte wirklich?
            x = torch.randn(256, 256, device="cuda", dtype=torch.bfloat16)
            info["bf16"] = bool(torch.isfinite((x @ x).float().sum()).item())
        import peft
        import transformers
        info["transformers"] = transformers.__version__
        info["peft"] = peft.__version__
        try:
            import bitsandbytes
            info["bitsandbytes"] = bitsandbytes.__version__
        except Exception as error:  # noqa: BLE001
            info["bitsandbytes"] = None
            info["bitsandbytesError"] = str(error)
    except Exception as error:  # noqa: BLE001
        send("check", ok=False, message=str(error), **info)
        return 1
    send("check", ok=bool(info.get("cuda")), **info)
    return 0


# ---------------------------------------------------------------- Datensatz

def load_examples(path: str) -> list[list[dict]]:
    """JSONL mit {"messages": [...]} (so speichert die App). Jede Zeile ist ein Gespräch, die letzte Nachricht ist die Antwort."""
    examples = []
    with open(path, encoding="utf-8") as file:
        for line in file:
            line = line.strip()
            if not line:
                continue
            messages = json.loads(line).get("messages") or []
            if len(messages) >= 2 and messages[-1].get("role") == "assistant":
                examples.append(messages)
    return examples


def tokenize(tokenizer, messages: list[dict], max_length: int) -> tuple[list[int], list[int]]:
    """
    Frage-Teil (mit Chat-Vorlage) und Antwort. Gelernt wird nur die Antwort: Die Frage bekommt das Label -100.
    Thinking ist aus, genau wie später im Chat mit llama.cpp.
    """
    prompt = tokenizer.apply_chat_template(messages[:-1], add_generation_prompt=True, tokenize=False, enable_thinking=False)
    prompt_ids = tokenizer(prompt, add_special_tokens=False)["input_ids"]
    end = tokenizer.eos_token or ""
    answer_ids = tokenizer(messages[-1]["content"] + end, add_special_tokens=False)["input_ids"]
    ids = (prompt_ids + answer_ids)[:max_length]
    labels = ([-100] * len(prompt_ids) + answer_ids)[:max_length]
    return ids, labels


# ---------------------------------------------------------------- Grundmodell holen

def download_base(repo: str, target: Path) -> None:
    """Lädt das Originalmodell (safetensors) von Hugging Face. Angefangene Dateien macht huggingface_hub fertig."""
    from huggingface_hub import HfApi, snapshot_download

    if (target / "config.json").exists() and any(target.glob("*.safetensors")):
        return
    import fnmatch
    token = os.environ.get("HF_TOKEN") or None
    info = HfApi().model_info(repo, files_metadata=True, token=token)
    total = sum(s.size or 0 for s in info.siblings if any(fnmatch.fnmatch(s.rfilename, p) for p in DOWNLOAD_PATTERNS))
    send("stage", stage="download", text=f"Lade {repo} von Hugging Face ({total / 1024 ** 3:.1f} GB) …", total=total)

    done = threading.Event()
    error: list[BaseException] = []

    def run():
        try:
            snapshot_download(repo, local_dir=str(target), allow_patterns=DOWNLOAD_PATTERNS, token=token)
        except BaseException as e:  # noqa: BLE001
            error.append(e)
        finally:
            done.set()

    threading.Thread(target=run, daemon=True).start()
    while not done.wait(1.0):
        if stop_requested():
            raise Cancelled()
        size = sum(f.stat().st_size for f in target.rglob("*") if f.is_file())
        send("download", done=size, total=total)
    if error:
        raise error[0]


# ---------------------------------------------------------------- Training

def train(config: dict) -> None:
    import torch
    from peft import LoraConfig, get_peft_model
    from transformers import AutoModelForCausalLM, AutoTokenizer

    started = time.perf_counter()
    base_dir = Path(config["baseModelDir"])
    output = Path(config["outputDir"])
    output.mkdir(parents=True, exist_ok=True)
    random.seed(42)
    torch.manual_seed(42)

    if config.get("baseRepo"):
        download_base(config["baseRepo"], base_dir)
    check_stop()

    if not torch.cuda.is_available():
        raise RuntimeError("Keine NVIDIA-Grafikkarte mit CUDA gefunden. Training auf der CPU wäre viel zu langsam.")

    send("stage", stage="load", text="Lade das Grundmodell auf die Grafikkarte …")
    tokenizer = AutoTokenizer.from_pretrained(base_dir)
    examples = load_examples(config["datasetPath"])
    if len(examples) < 2:
        raise RuntimeError("Der Datensatz braucht mindestens 2 Beispiele.")
    max_length = int(config.get("maxLength", 512))
    data = [tokenize(tokenizer, messages, max_length) for messages in examples]
    data = [(ids, labels) for ids, labels in data if any(label != -100 for label in labels)]

    load_options: dict = {"dtype": torch.bfloat16, "device_map": {"": 0}}
    if config.get("use4Bit"):
        from transformers import BitsAndBytesConfig
        load_options["quantization_config"] = BitsAndBytesConfig(
            load_in_4bit=True, bnb_4bit_quant_type="nf4", bnb_4bit_compute_dtype=torch.bfloat16, bnb_4bit_use_double_quant=True)
    model = AutoModelForCausalLM.from_pretrained(base_dir, **load_options)
    model.config.use_cache = False
    if config.get("use4Bit"):
        from peft import prepare_model_for_kbit_training
        model = prepare_model_for_kbit_training(model, use_gradient_checkpointing=True)
    else:
        model.gradient_checkpointing_enable()
        model.enable_input_require_grads()

    rank = int(config.get("loraRank", 16))
    lora = LoraConfig(r=rank, lora_alpha=rank * 2, lora_dropout=0.05, target_modules=LORA_TARGETS, task_type="CAUSAL_LM")
    model = get_peft_model(model, lora)
    trainable = sum(p.numel() for p in model.parameters() if p.requires_grad)
    log(f"Trainierbare Gewichte: {trainable / 1e6:.1f} Mio. Beispiele: {len(data)}")

    epochs = int(config.get("epochs", 3))
    accumulation = int(config.get("gradientAccumulation", 4))
    steps_per_epoch = math.ceil(len(data) / accumulation)
    total_steps = steps_per_epoch * epochs
    warmup = max(1, total_steps // 10)
    optimizer = torch.optim.AdamW([p for p in model.parameters() if p.requires_grad], lr=float(config.get("learningRate", 2e-4)), weight_decay=0.0)

    def learning_rate_factor(step: int) -> float:
        if step < warmup:
            return (step + 1) / warmup
        return max(0.0, (total_steps - step) / max(1, total_steps - warmup))

    scheduler = torch.optim.lr_scheduler.LambdaLR(optimizer, learning_rate_factor)
    send("stage", stage="train", text=f"Training: {len(data)} Beispiele, {epochs} Durchgänge, {total_steps} Schritte.", totalSteps=total_steps)

    model.train()
    step = 0
    train_started = time.perf_counter()
    last_loss = None
    for epoch in range(epochs):
        order = list(range(len(data)))
        random.shuffle(order)
        for chunk_start in range(0, len(order), accumulation):
            chunk = order[chunk_start:chunk_start + accumulation]
            losses = []
            for index in chunk:
                check_stop()
                ids, labels = data[index]
                input_ids = torch.tensor([ids], device="cuda")
                label_ids = torch.tensor([labels], device="cuda")
                loss = model(input_ids=input_ids, labels=label_ids).loss / len(chunk)
                loss.backward()
                losses.append(loss.item() * len(chunk))
            torch.nn.utils.clip_grad_norm_([p for p in model.parameters() if p.requires_grad], 1.0)
            optimizer.step()
            scheduler.step()
            optimizer.zero_grad(set_to_none=True)
            step += 1
            last_loss = sum(losses) / len(losses)
            elapsed = time.perf_counter() - train_started
            send("progress", step=step, totalSteps=total_steps, epoch=epoch + 1, epochs=epochs, loss=round(last_loss, 4),
                 elapsedSeconds=round(elapsed, 1), etaSeconds=round(elapsed / step * (total_steps - step), 1),
                 vramGb=round(torch.cuda.max_memory_allocated() / 1024 ** 3, 2))

    send("stage", stage="save", text="Speichere den Adapter …")
    adapter_dir = output / "adapter"
    model.save_pretrained(adapter_dir)
    tokenizer.save_pretrained(adapter_dir)
    del model
    torch.cuda.empty_cache()

    convert(config, adapter_dir, base_dir)
    send("done", adapterGguf=config["adapterGgufPath"], baseGguf=config["baseGgufPath"], finalLoss=last_loss,
         seconds=round(time.perf_counter() - started, 1))


# ---------------------------------------------------------------- Umwandlung nach GGUF

def convert(config: dict, adapter_dir: Path, base_dir: Path) -> None:
    """Mit den Skripten von llama.cpp (gleiche Version wie llama-server): Adapter und, falls nötig, das Grundmodell."""
    scripts = Path(config["convertScriptsDir"])
    env = dict(os.environ)
    env["PYTHONPATH"] = str(scripts / "gguf-py") + os.pathsep + env.get("PYTHONPATH", "")
    env["PYTHONIOENCODING"] = "utf-8"

    base_gguf = Path(config["baseGgufPath"])
    if not base_gguf.exists():
        send("stage", stage="convert_base", text="Wandle das Grundmodell nach GGUF um (einmalig, Q8_0) …")
        base_gguf.parent.mkdir(parents=True, exist_ok=True)
        run([sys.executable, str(scripts / "convert_hf_to_gguf.py"), str(base_dir), "--outfile", str(base_gguf), "--outtype", "q8_0"], env)

    send("stage", stage="convert_adapter", text="Wandle den Adapter nach GGUF um …")
    adapter_gguf = Path(config["adapterGgufPath"])
    adapter_gguf.parent.mkdir(parents=True, exist_ok=True)
    run([sys.executable, str(scripts / "convert_lora_to_gguf.py"), str(adapter_dir), "--base", str(base_dir),
         "--outfile", str(adapter_gguf), "--outtype", "f16"], env)


def run(command: list[str], env: dict) -> None:
    log("> " + " ".join(command))
    process = subprocess.Popen(command, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace")
    tail: list[str] = []
    for line in process.stdout:
        log(line.rstrip())
        tail = (tail + [line.rstrip()])[-5:]
        if stop_requested():
            process.kill()
            raise Cancelled()
    if process.wait() != 0:
        raise RuntimeError("Umwandlung nach GGUF fehlgeschlagen: " + " | ".join(tail))


# ---------------------------------------------------------------- Hauptprogramm

def main() -> int:
    parser = argparse.ArgumentParser(description="Trainings-Worker von KI-Nexus")
    parser.add_argument("--config", help="JSON-Datei mit den Einstellungen des Trainings")
    parser.add_argument("--check", action="store_true", help="Umgebung prüfen und beenden")
    args = parser.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

    if args.check:
        return check_environment()
    if not args.config:
        parser.error("--config fehlt")

    global _stop_file
    try:
        config = json.loads(Path(args.config).read_text(encoding="utf-8"))
        _stop_file = Path(config["stopFile"]) if config.get("stopFile") else None
        train(config)
        return 0
    except Cancelled:
        send("cancelled")
        return 2
    except Exception as error:  # noqa: BLE001
        log(traceback.format_exc())
        message = str(error)
        if "out of memory" in message.lower():
            message = "Zu wenig Grafikspeicher (VRAM). Nimm ein kleineres Modell, 4-Bit oder eine kleinere Länge. " + message
        send("error", message=message)
        return 1


if __name__ == "__main__":
    sys.exit(main())
