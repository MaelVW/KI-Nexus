# scripts – PowerShell-Skripte

Skripte für Setup und Engines. Sie laufen mit Windows PowerShell 5.1 und sind als UTF-8 mit BOM gespeichert (sonst zeigt PowerShell 5.1 die Umlaute falsch).

| Skript | Seit | Aufgabe |
|---|---|---|
| `setup-engines.ps1` | M1 | lädt llama.cpp (CUDA-Build für RTX 50) nach `engines\llama.cpp\`, prüft die Prüfsummen |
| `setup-voice.ps1` | M4 | Python-Umgebung für die Sprache, Whisper und Piper |
| `setup-training.ps1` | M8 | Python-Umgebung fürs Training, Umwandlungs-Skripte von llama.cpp |
| `setup-video.ps1` | Video V1 | ComfyUI mit ComfyUI-GGUF nach `engines\comfyui\`, mit `-Models` das LTX-2.3-Videomodell (ca. 24 GB, mit Prüfsummen). Details: [docs/10-Video.md](../docs/10-Video.md) |

```powershell
.\scripts\setup-engines.ps1                  # fester, getesteter Build
.\scripts\setup-engines.ps1 -Version latest  # neuestes stabiles Release
```

Details: [docs/02-Engines.md](../docs/02-Engines.md).
